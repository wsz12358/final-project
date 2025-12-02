using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using System.Threading;

public struct MusicFrame
{
	public readonly double dspTime;
	public readonly int bufferSamples;
	public readonly float rmsL;
	public readonly float rmsR;
	public readonly float rms;
	public readonly float normL;
	public readonly float normR;
	public readonly float norm;
	public readonly float frameDuration;
	
	// Extended properties
	public readonly float flux;
	public readonly float bpm;
	public readonly bool isBeat;

	// Future-ready: expose raw percussive band energies if needed by visualizers.
	public readonly float kickEnergy;
	public readonly float snareEnergy;

	public MusicFrame(
		double dspTime,
		int bufferSamples,
		float rmsL,
		float rmsR,
		float rms,
		float normL,
		float normR,
		float norm,
		float frameDuration,
		float flux,
		float bpm,
		bool isBeat,
		float kickEnergy,
		float snareEnergy)
	{
		this.dspTime = dspTime;
		this.bufferSamples = bufferSamples;
		this.rmsL = rmsL;
		this.rmsR = rmsR;
		this.rms = rms;
		this.normL = normL;
		this.normR = normR;
		this.norm = norm;
		this.frameDuration = frameDuration;
		this.flux = flux;
		this.bpm = bpm;
		this.isBeat = isBeat;
		this.kickEnergy = kickEnergy;
		this.snareEnergy = snareEnergy;
	}
}

/// <summary>
/// Central audio driver that samples the currently playing AudioSource in OnAudioFilterRead,
/// computes RMS per buffer for L/R and combined, performs adaptive normalization, and broadcasts
/// frames on the main thread.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicDriver : MonoBehaviour
{
	/// <summary>
	/// Offline tempo analysis result for the current AudioClip.
	/// Times are expressed in seconds on the AudioClip's time axis.
	/// </summary>
	public struct TempoAnalysisResult
	{
		public bool hasResult;
		public float bpm;
		public float firstBeatTimeInClip;
		public List<float> beatTimesInClip;
	}

	[Header("Discovery")]
	[Tooltip("Name of the visualization layer. Objects on this layer can receive music frames.")]
	public string musicVisLayerName = "MusicVis";

	[Header("Global RMS Normalization (full-track scan)")]
	[Tooltip("Analysis window length in seconds used to compute RMS statistics over the AudioClip.")]
	[Min(0.001f)] public float analysisWindowSeconds = 0.02f;
	[Tooltip("Maximum visual energy assigned to the \"low energy\" band (B-type mapping).")]
	[Range(0f, 1f)] public float lowBandMaxOutput = 0.3f;
	[Tooltip("Portion of the linear input energy range treated as \"low energy\" (0-1).")]
	[Range(0f, 1f)] public float lowBandInputPortion = 0.3f;

	[Header("Buffering")]
	[Min(16)] public int ringBufferFrames = 256;

	[Header("Debug")]
	public bool enableDebugLogs = false;

	[Header("Spectrum (optional)")]
	[Tooltip("If true, MusicDriver will sample the current AudioSource spectrum each Update into Spectrum512.")]
	public bool enableSpectrumSampling = true;
	[Tooltip("FFT window used when sampling the spectrum.")]
	public FFTWindow spectrumWindow = FFTWindow.Blackman;
	/// <summary>
	/// Last sampled spectrum (size 512) for the currently playing AudioSource.
	/// Updated on the main thread in Update when enableSpectrumSampling is true.
	/// </summary>
	public static float[] Spectrum512 { get; private set; }

	public static event Action<MusicFrame> OnMusicFrame;
	public static int ActiveVisLayer { get; private set; } = -1;

	ConcurrentQueue<MusicFrame> _queue;
	int _sampleRate;
	AudioSource _src;
	BeatDetection _beatDetection;

	// BeatDetection event state for current frame
	bool _kickThisFrame;
	bool _snareThisFrame;
	bool _energyBeatThisFrame;
	float _lastBeatAudioTime;

	// Global RMS statistics from full-track analysis (main thread only)
	AudioClip _analyzedClip;
	bool _hasGlobalRmsStats;
	float _globalMinL;
	float _globalMaxL;
	float _globalMinR;
	float _globalMaxR;
	float _globalMinAll;
	float _globalMaxAll;
	float[] _analysisBuffer;

	// Safe counters to avoid expensive ConcurrentQueue.Count on the audio thread
	int _queuedFrames;
	int _droppedFrames;

	// Telemetry from audio thread (read in Update for logging)
	volatile int _audioTicks;
	volatile int _lastChannels;
	volatile int _lastDataLen;
	volatile float _lastDt;
	double _lastDspTime;
	float _logAccum;

	// Cached tempo analysis (main thread only)
	AudioClip _tempoAnalyzedClip;
	TempoAnalysisResult _tempoResult;

	void Awake()
	{
		_queue = new ConcurrentQueue<MusicFrame>();
		_sampleRate = AudioSettings.outputSampleRate;
		_src = GetComponent<AudioSource>();
		_beatDetection = GetComponent<BeatDetection>();
		if (_beatDetection == null)
		{
			_beatDetection = gameObject.AddComponent<BeatDetection>();
		}

		_beatDetection.beatMode = BeatDetection.beatmode.Both;

		if (Spectrum512 == null || Spectrum512.Length != 512)
		{
			Spectrum512 = new float[512];
		}
	}

	void OnEnable()
	{
		int idx = LayerMask.NameToLayer(musicVisLayerName);
		ActiveVisLayer = idx;
		if (ringBufferFrames < 16) ringBufferFrames = 16;
		AnalyzeClipIfNeeded();

		// Subscribe to BeatDetection events
		if (_beatDetection != null)
		{
			_beatDetection.CallBackFunction += OnBeatDetectionEvent;
		}

		if (enableDebugLogs)
		{
			AudioSettings.GetDSPBufferSize(out int buf, out int num);
			Debug.Log($"[MusicDriver] Enabled. SampleRate={_sampleRate}, DSPBuffer={buf} x {num}, Layer='{musicVisLayerName}' idx={ActiveVisLayer}");
		}
	}

	void AnalyzeClipIfNeeded()
	{
		if (_src == null)
		{
			_src = GetComponent<AudioSource>();
		}

		AudioClip clip = _src != null ? _src.clip : null;
		if (clip == null)
		{
			_hasGlobalRmsStats = false;
			_analyzedClip = null;
			return;
		}

		if (_hasGlobalRmsStats && _analyzedClip == clip)
		{
			return;
		}

		AnalyzeClipRms(clip);
	}

	void AnalyzeClipRms(AudioClip clip)
	{
		if (clip == null) return;

		int channels = Mathf.Max(1, clip.channels);
		int sampleRate = Mathf.Max(1, clip.frequency);
		int totalFrames = Mathf.Max(0, clip.samples);
		if (totalFrames == 0)
		{
			_hasGlobalRmsStats = false;
			_analyzedClip = clip;
			return;
		}

		int windowFrames = Mathf.Max(1, Mathf.RoundToInt(analysisWindowSeconds * sampleRate));

		_globalMinL = float.MaxValue;
		_globalMaxL = 0f;
		_globalMinR = float.MaxValue;
		_globalMaxR = 0f;
		_globalMinAll = float.MaxValue;
		_globalMaxAll = 0f;

		bool anyWindow = false;

		for (int frameOffset = 0; frameOffset < totalFrames; frameOffset += windowFrames)
		{
			int framesThis = Mathf.Min(windowFrames, totalFrames - frameOffset);
			if (framesThis <= 0) break;

			int samplesThis = framesThis * channels;
			if (_analysisBuffer == null || _analysisBuffer.Length < samplesThis)
			{
				_analysisBuffer = new float[samplesThis];
			}

			clip.GetData(_analysisBuffer, frameOffset);

			double sumL = 0.0;
			double sumR = 0.0;

			for (int i = 0; i < framesThis; i++)
			{
				int baseIndex = i * channels;
				float l = _analysisBuffer[baseIndex + 0];
				sumL += (double)l * l;
				if (channels > 1)
				{
					float r = _analysisBuffer[baseIndex + 1];
					sumR += (double)r * r;
				}
				else
				{
					sumR += (double)l * l;
				}
			}

			float rmsL = Mathf.Sqrt((float)(sumL / framesThis));
			float rmsR = Mathf.Sqrt((float)(sumR / framesThis));
			float rms = 0.5f * (rmsL + rmsR);

			if (rmsL < _globalMinL) _globalMinL = rmsL;
			if (rmsL > _globalMaxL) _globalMaxL = rmsL;
			if (rmsR < _globalMinR) _globalMinR = rmsR;
			if (rmsR > _globalMaxR) _globalMaxR = rmsR;
			if (rms < _globalMinAll) _globalMinAll = rms;
			if (rms > _globalMaxAll) _globalMaxAll = rms;

			anyWindow = true;
		}

		if (!anyWindow)
		{
			_hasGlobalRmsStats = false;
			_analyzedClip = clip;
			return;
		}

		// Guard against nearly constant signals
		if (_globalMaxL <= _globalMinL + 1e-6f)
		{
			_globalMinL = 0f;
			_globalMaxL = Mathf.Max(1e-6f, _globalMaxL);
		}
		if (_globalMaxR <= _globalMinR + 1e-6f)
		{
			_globalMinR = 0f;
			_globalMaxR = Mathf.Max(1e-6f, _globalMaxR);
		}
		if (_globalMaxAll <= _globalMinAll + 1e-6f)
		{
			_globalMinAll = 0f;
			_globalMaxAll = Mathf.Max(1e-6f, _globalMaxAll);
		}

		_analyzedClip = clip;
		_hasGlobalRmsStats = true;

		if (enableDebugLogs)
		{
			Debug.Log($"[MusicDriver] Global RMS stats ready for clip '{clip.name}': " +
			          $"L[{_globalMinL:F4},{_globalMaxL:F4}] R[{_globalMinR:F4},{_globalMaxR:F4}] All[{_globalMinAll:F4},{_globalMaxAll:F4}]");
		}
	}

	float ToGlobalEnergy(float rms, float min, float max)
	{
		if (max <= min + 1e-6f)
		{
			return 0f;
		}

		float eLin = Mathf.Clamp01((rms - min) / (max - min));
		float t = Mathf.Clamp01(lowBandInputPortion);
		float b = Mathf.Clamp01(lowBandMaxOutput);

		if (t <= 0f)
		{
			// No dedicated low band: map entire [0,1] into [b,1]
			return Mathf.Clamp01(Mathf.Lerp(b, 1f, eLin));
		}

		if (eLin <= t)
		{
			// Low-energy band mapped into [0, b]
			float frac = eLin / Mathf.Max(1e-6f, t);
			return Mathf.Clamp01(b * frac);
		}

		// High-energy band mapped into [b, 1]
		float highPortion = 1f - t;
		if (highPortion <= 1e-6f) return Mathf.Clamp01(b);
		float highFrac = (eLin - t) / highPortion;
		return Mathf.Clamp01(b + (1f - b) * highFrac);
	}

	/// <summary>
	/// Maps a raw RMS value for the whole mix into a globally-normalized energy in [0,1],
	/// using the full-track RMS statistics computed in AnalyzeClipRms.
	/// </summary>
	/// <param name="rms">Windowed RMS value (combined channels) around a given time.</param>
	/// <returns>Global energy in [0,1]. Returns 0 if global stats are not yet available.</returns>
	public float MapRmsToGlobalEnergy(float rms)
	{
		if (!_hasGlobalRmsStats)
		{
			return 0f;
		}

		return ToGlobalEnergy(rms, _globalMinAll, _globalMaxAll);
	}

	/// <summary>
	/// Public accessor for the cached tempo analysis result.
	/// If no analysis has been performed yet, this will attempt to analyze the current clip.
	/// </summary>
	public TempoAnalysisResult GetOrAnalyzeTempo()
	{
		if (_src == null)
		{
			_src = GetComponent<AudioSource>();
		}

		var clip = _src != null ? _src.clip : null;
		if (clip == null)
		{
			if (enableDebugLogs)
			{
				Debug.LogWarning("[MusicDriver] GetOrAnalyzeTempo: no AudioClip on AudioSource.");
			}
			return default;
		}

		if (_tempoResult.hasResult && _tempoAnalyzedClip == clip)
		{
			return _tempoResult;
		}

		_tempoResult = AnalyzeClipTempo(clip);
		_tempoAnalyzedClip = clip;

		if (enableDebugLogs && _tempoResult.hasResult)
		{
			Debug.Log($"[MusicDriver] Tempo analysis complete for clip '{clip.name}': BPM={_tempoResult.bpm:F1}, firstBeat={_tempoResult.firstBeatTimeInClip:F3}s, beats={_tempoResult.beatTimesInClip?.Count ?? 0}");
		}

		return _tempoResult;
	}

	/// <summary>
	/// Perform an offline tempo analysis of the given AudioClip.
	/// Returns a TempoAnalysisResult with BPM, first beat time, and a full list of beat times (AudioClip time axis).
	/// </summary>
	TempoAnalysisResult AnalyzeClipTempo(AudioClip clip)
	{
		var result = new TempoAnalysisResult
		{
			hasResult = false,
			bpm = 0f,
			firstBeatTimeInClip = 0f,
			beatTimesInClip = new List<float>()
		};

		if (clip == null) return result;

		int channels = Mathf.Max(1, clip.channels);
		int sampleRate = Mathf.Max(1, clip.frequency);
		int totalFrames = Mathf.Max(0, clip.samples);
		if (totalFrames <= 0)
		{
			if (enableDebugLogs)
			{
				Debug.LogWarning($"[MusicDriver] AnalyzeClipTempo: clip '{clip.name}' has no samples.");
			}
			return result;
		}

		// Build an energy envelope using the same window as RMS analysis.
		int windowFrames = Mathf.Max(1, Mathf.RoundToInt(analysisWindowSeconds * sampleRate));
		List<float> envTimes = new List<float>();
		List<float> envValues = new List<float>();

		if (_analysisBuffer == null || _analysisBuffer.Length < windowFrames * channels)
		{
			_analysisBuffer = new float[windowFrames * channels];
		}

		for (int frameOffset = 0; frameOffset < totalFrames; frameOffset += windowFrames)
		{
			int framesThis = Mathf.Min(windowFrames, totalFrames - frameOffset);
			if (framesThis <= 0) break;

			int samplesThis = framesThis * channels;
			if (_analysisBuffer.Length < samplesThis)
			{
				_analysisBuffer = new float[samplesThis];
			}

			clip.GetData(_analysisBuffer, frameOffset);

			double sum = 0.0;
			for (int i = 0; i < framesThis; i++)
			{
				int baseIndex = i * channels;
				float l = _analysisBuffer[baseIndex + 0];
				sum += (double)l * l;
				if (channels > 1)
				{
					float r = _analysisBuffer[baseIndex + 1];
					sum += (double)r * r;
				}
				else
				{
					sum += (double)l * l;
				}
			}

			float rms = Mathf.Sqrt((float)(sum / (framesThis * channels)));
			float time = frameOffset / (float)sampleRate;

			envTimes.Add(time);
			envValues.Add(rms);
		}

		int n = envValues.Count;
		if (n < 4)
		{
			// Too few envelope samples to estimate tempo; fall back to a default.
			result.bpm = 120f;
			result.firstBeatTimeInClip = 0f;
			result.beatTimesInClip.Add(0f);
			result.hasResult = true;
			return result;
		}

		// Compute global mean and std of the envelope.
		double sumEnv = 0.0;
		for (int i = 0; i < n; i++)
		{
			sumEnv += envValues[i];
		}
		double meanEnv = sumEnv / n;

		double varEnv = 0.0;
		for (int i = 0; i < n; i++)
		{
			double d = envValues[i] - meanEnv;
			varEnv += d * d;
		}
		varEnv /= n;
		double stdEnv = Math.Sqrt(varEnv);

		// Detect onset candidates: local peaks that exceed mean + k * std.
		const float peakStdMultiplier = 0.7f;
		double threshold = meanEnv + peakStdMultiplier * stdEnv;
		List<float> onsetTimes = new List<float>();

		for (int i = 1; i < n - 1; i++)
		{
			float prev = envValues[i - 1];
			float curr = envValues[i];
			float next = envValues[i + 1];
			if (curr > prev && curr >= next && curr > threshold)
			{
				onsetTimes.Add(envTimes[i]);
			}
		}

		if (onsetTimes.Count < 2)
		{
			// Not enough onsets; fall back to default tempo.
			result.bpm = 120f;
			result.firstBeatTimeInClip = 0f;
			result.beatTimesInClip.Add(0f);
			result.hasResult = true;
			return result;
		}

		// Build BPM histogram from onset intervals.
		const float minBpm = 60f;
		const float maxBpm = 200f;
		Dictionary<int, int> bpmHistogram = new Dictionary<int, int>();

		for (int i = 0; i < onsetTimes.Count - 1; i++)
		{
			float dt = onsetTimes[i + 1] - onsetTimes[i];
			if (dt <= 1e-3f) continue;
			float bpmCandidate = 60f / dt;

			// Fold into [minBpm, maxBpm] by doubling/halving.
			while (bpmCandidate < minBpm) bpmCandidate *= 2f;
			while (bpmCandidate > maxBpm) bpmCandidate *= 0.5f;

			int bin = Mathf.RoundToInt(bpmCandidate);
			if (bin < (int)minBpm || bin > (int)maxBpm) continue;

			if (!bpmHistogram.TryGetValue(bin, out int count))
			{
				bpmHistogram[bin] = 1;
			}
			else
			{
				bpmHistogram[bin] = count + 1;
			}
		}

		if (bpmHistogram.Count == 0)
		{
			// Fallback if histogram is empty.
			result.bpm = 120f;
		}
		else
		{
			int bestBin = 0;
			int bestCount = -1;
			foreach (var kv in bpmHistogram)
			{
				if (kv.Value > bestCount)
				{
					bestCount = kv.Value;
					bestBin = kv.Key;
				}
			}
			result.bpm = bestBin;
		}

		float beatPeriod = 60f / Mathf.Max(1e-3f, result.bpm);

		// Estimate first beat phase (φ) in [0, beatPeriod) that best aligns grid beats to onset times.
		float clipLength = clip.length;
		float searchWindow = Mathf.Min(clipLength, 20f); // only use first 20 seconds for phase estimation

		const int phaseSamples = 64;
		float bestPhi = 0f;
		double bestError = double.MaxValue;

		for (int s = 0; s < phaseSamples; s++)
		{
			float phi = (beatPeriod * s) / phaseSamples;
			double totalError = 0.0;
			int used = 0;

			for (int i = 0; i < onsetTimes.Count; i++)
			{
				float t = onsetTimes[i];
				if (t < phi || t > searchWindow) continue;

				float k = Mathf.Round((t - phi) / beatPeriod);
				float gridTime = phi + k * beatPeriod;
				float err = Mathf.Abs(t - gridTime);
				totalError += err;
				used++;
			}

			if (used == 0) continue;

			double meanError = totalError / used;
			if (meanError < bestError)
			{
				bestError = meanError;
				bestPhi = phi;
			}
		}

		result.firstBeatTimeInClip = bestPhi;

		// Generate full beat grid from first beat until end of clip.
		List<float> beatTimes = new List<float>();
		float tBeat = bestPhi;
		while (tBeat <= clipLength + 0.5f * beatPeriod)
		{
			if (tBeat >= 0f && tBeat <= clipLength)
			{
				beatTimes.Add(tBeat);
			}
			tBeat += beatPeriod;
		}

		if (beatTimes.Count == 0)
		{
			beatTimes.Add(0f);
		}

		result.beatTimesInClip = beatTimes;
		result.hasResult = true;
		return result;
	}

	void OnDisable()
	{
		ActiveVisLayer = -1;

		// Unsubscribe from BeatDetection events
		if (_beatDetection != null)
		{
			_beatDetection.CallBackFunction -= OnBeatDetectionEvent;
		}
	}

	void Update()
	{
		// Drain frames generated on the audio thread and broadcast in order.
		int guard = 0;
		while (_queue.TryDequeue(out var rawFrame))
		{
			// Use BeatDetection events accumulated on this frame to describe beat information.
			bool isBeat = _kickThisFrame || _snareThisFrame || _energyBeatThisFrame;
			float flux = 0f;
			float bpm = 0f;
			float kickEnergy = _kickThisFrame ? 1f : 0f;
			float snareEnergy = _snareThisFrame ? 1f : 0f;

			// Construct the full frame
			var fullFrame = new MusicFrame(
				rawFrame.dspTime,
				rawFrame.bufferSamples,
				rawFrame.rmsL,
				rawFrame.rmsR,
				rawFrame.rms,
				rawFrame.normL,
				rawFrame.normR,
				rawFrame.norm,
				rawFrame.frameDuration,
				flux,
				bpm,
				isBeat,
				kickEnergy,
				snareEnergy
			);

			if (enableDebugLogs && isBeat)
			{
				string types = "";
				if (_kickThisFrame) types += "Kick ";
				if (_snareThisFrame) types += "Snare ";
				if (_energyBeatThisFrame) types += "Energy ";
				string clipName = _src != null && _src.clip != null ? _src.clip.name : "<none>";
				float audioTime = _src != null ? _src.time : 0f;
				Debug.Log($"[MusicDriver] Beat frame: types={types.Trim()} dsp={rawFrame.dspTime:F3} audioTime={audioTime:F3}s clip='{clipName}'");
			}

			OnMusicFrame?.Invoke(fullFrame);
			guard++;
			Interlocked.Decrement(ref _queuedFrames);
			if (guard > ringBufferFrames * 2) break;
		}

		// Periodic debug logs on main thread to avoid logging from audio thread
		// (moved into the block above so we don't double-use _logAccum)

		// Optional spectrum sampling for band-based visualizations (main thread only).
		if (enableSpectrumSampling && _src != null && _src.isPlaying && Spectrum512 != null && Spectrum512.Length == 512)
		{
			try
			{
				_src.GetSpectrumData(Spectrum512, 0, spectrumWindow);
			}
			catch (Exception ex)
			{
				if (enableDebugLogs)
				{
					Debug.LogError($"[MusicDriver] GetSpectrumData exception: {ex}");
				}
			}
		}

		// Clear beat flags for the next frame
		_kickThisFrame = false;
		_snareThisFrame = false;
		_energyBeatThisFrame = false;
	}

	void OnBeatDetectionEvent(BeatDetection.EventInfo eventInfo)
	{
		if (eventInfo == null) return;

		switch (eventInfo.messageInfo)
		{
			case BeatDetection.EventType.Kick:
				_kickThisFrame = true;
				break;
			case BeatDetection.EventType.Snare:
				_snareThisFrame = true;
				break;
			case BeatDetection.EventType.Energy:
				_energyBeatThisFrame = true;
				break;
			case BeatDetection.EventType.HitHat:
				// 当前不单独区分 HitHat，可按需要扩展。
				break;
		}

		if (!enableDebugLogs)
			return;

		string clipName = _src != null && _src.clip != null ? _src.clip.name : "<none>";
		float audioTime = _src != null ? _src.time : 0f;
		Debug.Log($"[MusicDriver] BeatDetection event: type={eventInfo.messageInfo} t={Time.time:F3}s audioTime={audioTime:F3}s clip='{clipName}'");
	}

	void OnAudioFilterRead(float[] data, int channels)
	{
		try
		{
			if (data == null || data.Length == 0) return;
			// Defensive init in case OnAudioFilterRead is called before Awake.
			if (_queue == null)
			{
				_queue = new ConcurrentQueue<MusicFrame>();
			}
			if (channels < 1) channels = 1;

			int frames = data.Length / channels;
			double dsp = AudioSettings.dspTime;
			// NOTE: Cannot call AudioSettings.outputSampleRate here (audio thread restriction),
			// so fall back to a sane default if Awake hasn't run yet to set _sampleRate.
			if (_sampleRate <= 0) _sampleRate = 44100;
			float dt = frames / (float)_sampleRate;

			// Audio-thread telemetry
			_lastChannels = channels;
			_lastDataLen = data.Length;
			_lastDt = dt;
			_lastDspTime = dsp;
			_audioTicks++;

			// Accumulate squared samples per channel
			double sumL = 0.0;
			double sumR = 0.0;
			for (int i = 0; i < frames; i++)
			{
				float l = data[i * channels + 0];
				sumL += (double)l * l;
				if (channels > 1)
				{
					float r = data[i * channels + 1];
					sumR += (double)r * r;
				}
				else
				{
					sumR += (double)l * l; // mono -> mirror
				}
			}

			float rmsL = Mathf.Sqrt((float)(sumL / frames));
			float rmsR = Mathf.Sqrt((float)(sumR / frames));
			float rms = 0.5f * (rmsL + rmsR);

		// Map per-buffer RMS values into globally-normalized energies in [0,1]
		float normL;
		float normR;
		float norm;

		if (_hasGlobalRmsStats)
		{
			normL = ToGlobalEnergy(rmsL, _globalMinL, _globalMaxL);
			normR = ToGlobalEnergy(rmsR, _globalMinR, _globalMaxR);
			norm = ToGlobalEnergy(rms, _globalMinAll, _globalMaxAll);
		}
		else
		{
			// Fallback: simple local scaling when global stats are not yet available.
			const float fallbackScale = 10f;
			normL = Mathf.Clamp01(rmsL * fallbackScale);
			normR = Mathf.Clamp01(rmsR * fallbackScale);
			norm = Mathf.Clamp01(rms * fallbackScale);
		}

			// Backpressure w/o using ConcurrentQueue.Count (which is expensive)
			while (Volatile.Read(ref _queuedFrames) > ringBufferFrames)
			{
				if (_queue.TryDequeue(out _))
				{
					Interlocked.Decrement(ref _queuedFrames);
					Interlocked.Increment(ref _droppedFrames);
				}
				else
				{
					break;
				}
			}

			_queue.Enqueue(new MusicFrame(
				dsp,
				frames,
				rmsL, rmsR, rms,
				normL, normR, norm,
				dt,
				0f, 0f, false, // placeholders for main-thread analysis
				0f, 0f        // kick/snare energies filled in Update
			));
			Interlocked.Increment(ref _queuedFrames);
		}
		catch (Exception ex)
		{
			// Log once per second via main thread would be better, but in emergencies we log here.
			if (enableDebugLogs) Debug.LogError($"[MusicDriver] OnAudioFilterRead exception: {ex}");
		}
	}

}


