using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central coordinator that:
/// - Requests offline tempo analysis from MusicDriver.
/// - Generates a full-song note chart based on beat times and global energy.
/// - Hands the chart to NoteGenerator.
/// - Aligns the first note's perfect hit with the music start time.
/// </summary>
public class RhythmConductor : MonoBehaviour
{
	[Header("References")]
	public MusicDriver musicDriver;
	public NoteGenerator noteGenerator;
	public AudioSource musicSource;

	[Header("Energy Segmentation")]
	[Tooltip("Threshold below which beats are considered low energy (0-1).")]
	[Range(0f, 1f)] public float lowEnergyThreshold = 0.33f;
	[Tooltip("Threshold above which beats are considered high energy (0-1).")]
	[Range(0f, 1f)] public float highEnergyThreshold = 0.66f;
	[Tooltip("Minimum duration (seconds) for any energy segment.")]
	[Min(0f)] public float minSegmentDurationSeconds = 5f;
	[Tooltip("Maximum duration (seconds) for any energy segment.")]
	[Min(0f)] public float maxSegmentDurationSeconds = 10f;
	[Tooltip("If true, logs each final energy segment with its time range and average energy.")]
	public bool logEnergySegments = true;

	[Header("Debug")]
	public bool autoBeginOnStart = true;
	public bool enableDebugLogs = false;

	MusicDriver.TempoAnalysisResult _tempoResult;
	List<NoteGenerator.ScheduledNote> _chart;

	float _worldBeginTime;
	float _musicStartWorldTime;
	bool _gameplayStarted;
	bool _musicStarted;

	enum EnergyBand
	{
		Low = 0,
		Mid = 1,
		High = 2
	}

	struct EnergySegment
	{
		public int startBeatIndex;
		public int endBeatIndex;
		public EnergyBand band;
		public float startTime;
		public float endTime;

		public float Duration => endTime - startTime;
	}

	void Awake()
	{
		// Try to auto-wire references if not set in the inspector.
		if (musicDriver == null)
		{
			musicDriver = FindObjectOfType<MusicDriver>();
		}
		if (noteGenerator == null)
		{
			noteGenerator = FindObjectOfType<NoteGenerator>();
		}
		if (musicSource == null && musicDriver != null)
		{
			musicSource = musicDriver.GetComponent<AudioSource>();
		}
	}

	void Start()
	{
		if (musicDriver == null || noteGenerator == null)
		{
			Debug.LogError("[RhythmConductor] Missing references to MusicDriver or NoteGenerator.");
			return;
		}

		_tempoResult = musicDriver.GetOrAnalyzeTempo();
		if (!_tempoResult.hasResult)
		{
			Debug.LogError("[RhythmConductor] Tempo analysis failed or has no result.");
			return;
		}

		GenerateFullChart();

		if (autoBeginOnStart)
		{
			BeginGame();
		}
	}

	/// <summary>
	/// Build a full-song chart using per-beat global energy to decide low/mid/high
	/// energy segments, then generate notes with different rhythmic patterns.
	/// hitTimeSong is measured from the first beat (first beat = 0s on music time axis).
	/// </summary>
	void GenerateFullChart()
	{
		_chart = new List<NoteGenerator.ScheduledNote>();
		if (_tempoResult.beatTimesInClip == null || _tempoResult.beatTimesInClip.Count == 0)
		{
			return;
		}

		if (musicDriver == null)
		{
			Debug.LogError("[RhythmConductor] GenerateFullChart: MusicDriver is null.");
			return;
		}

		if (musicSource == null)
		{
			musicSource = musicDriver.GetComponent<AudioSource>();
		}

		if (musicSource == null || musicSource.clip == null)
		{
			Debug.LogError("[RhythmConductor] GenerateFullChart: No AudioSource or AudioClip available.");
			return;
		}

		AudioClip clip = musicSource.clip;
		List<float> beatTimes = _tempoResult.beatTimesInClip;
		float firstBeat = _tempoResult.firstBeatTimeInClip;
		float bpm = Mathf.Max(1e-3f, _tempoResult.bpm);
		float beatPeriod = 60f / bpm;
		int laneCount = Mathf.Max(1, noteGenerator.trackCount);

		// ------------------------------------------------------------------
		// 1) Compute per-beat RMS and map to global energy [0,1]
		// ------------------------------------------------------------------
		int sampleRate = Mathf.Max(1, clip.frequency);
		int channels = Mathf.Max(1, clip.channels);
		int totalFrames = Mathf.Max(0, clip.samples);

		float analysisWindowSeconds = Mathf.Max(0.001f, musicDriver.analysisWindowSeconds);
		int windowFrames = Mathf.Max(1, Mathf.RoundToInt(analysisWindowSeconds * sampleRate));

		var beatEnergies = new List<float>(beatTimes.Count);
		float[] analysisBuffer = null;

		for (int i = 0; i < beatTimes.Count; i++)
		{
			float centerTime = beatTimes[i];
			int centerFrame = Mathf.RoundToInt(centerTime * sampleRate);
			int startFrame = Mathf.Clamp(centerFrame - windowFrames / 2, 0, Mathf.Max(0, totalFrames - 1));
			int framesThis = Mathf.Min(windowFrames, totalFrames - startFrame);
			if (framesThis <= 0)
			{
				beatEnergies.Add(0f);
				continue;
			}

			int samplesThis = framesThis * channels;
			if (analysisBuffer == null || analysisBuffer.Length < samplesThis)
			{
				analysisBuffer = new float[samplesThis];
			}

			clip.GetData(analysisBuffer, startFrame);

			double sum = 0.0;
			for (int s = 0; s < framesThis; s++)
			{
				int baseIndex = s * channels;
				for (int ch = 0; ch < channels; ch++)
				{
					float sample = analysisBuffer[baseIndex + ch];
					sum += (double)sample * sample;
				}
			}

			float rms = Mathf.Sqrt((float)(sum / (framesThis * channels)));
			float energy = musicDriver.MapRmsToGlobalEnergy(rms);
			beatEnergies.Add(Mathf.Clamp01(energy));
		}

		// ------------------------------------------------------------------
		// 2) Split beats into time-based segments with duration constraints
		//    and classify each segment into Low / Mid / High according to
		//    its *average* energy.
		// ------------------------------------------------------------------
		float lowThreshold = Mathf.Clamp01(lowEnergyThreshold);
		float highThreshold = Mathf.Clamp01(highEnergyThreshold);

		// Ensure ordering: low <= high
		if (lowThreshold > highThreshold)
		{
			float tmp = lowThreshold;
			lowThreshold = highThreshold;
			highThreshold = tmp;
		}

		float minSegmentDuration = Mathf.Max(0.001f, minSegmentDurationSeconds);
		float maxSegmentDuration = Mathf.Max(minSegmentDuration, maxSegmentDurationSeconds);
		float targetDuration = 0.5f * (minSegmentDuration + maxSegmentDuration);

		var segments = new List<EnergySegment>();

		if (beatTimes.Count > 0)
		{
			float clipLength = clip.length;
			int startBeatIndex = 0;

			while (startBeatIndex < beatTimes.Count)
			{
				int segStartBeat = startBeatIndex;
				float segStartTime = beatTimes[segStartBeat];

				// Choose an end beat so that segment duration is within [min, max]
				// and as close as possible to targetDuration.
				int bestEndBeat = segStartBeat;
				float bestEndTime = (segStartBeat + 1 < beatTimes.Count) ? beatTimes[segStartBeat + 1] : clipLength;
				float bestDuration = bestEndTime - segStartTime;

				bool foundInRange = false;
				float bestInRangeDiff = float.MaxValue;

				for (int endBeat = segStartBeat; endBeat < beatTimes.Count; endBeat++)
				{
					float endTime = (endBeat + 1 < beatTimes.Count) ? beatTimes[endBeat + 1] : clipLength;
					float duration = endTime - segStartTime;

					if (duration < minSegmentDuration)
					{
						// Not long enough yet, but keep track as fallback if we never reach min.
						bestEndBeat = endBeat;
						bestEndTime = endTime;
						bestDuration = duration;
						continue;
					}

					// duration >= min
					float diffToTarget = Mathf.Abs(duration - targetDuration);

					if (duration <= maxSegmentDuration)
					{
						// Valid candidate within [min,max]: pick the one closest to targetDuration.
						if (!foundInRange || diffToTarget < bestInRangeDiff)
						{
							foundInRange = true;
							bestInRangeDiff = diffToTarget;
							bestEndBeat = endBeat;
							bestEndTime = endTime;
							bestDuration = duration;
						}
					}
					else
					{
						// We exceeded max; stop extending this segment.
						break;
					}
				}

				// If we never found a candidate in [min,max], we fall back to the
				// last beat we tracked (which may have duration < min or > max,
				// typically only possible near the end of the song).
				var seg = new EnergySegment
				{
					startBeatIndex = segStartBeat,
					endBeatIndex = bestEndBeat,
					band = EnergyBand.Low, // temporary, will be set based on average energy
					startTime = segStartTime,
					endTime = bestEndTime
				};

				segments.Add(seg);

				startBeatIndex = bestEndBeat + 1;
			}
		}

		// ------------------------------------------------------------------
		// 3) Compute average energy per segment and classify band by segment
		//    average, not by individual beats. This avoids segments whose
		//    average energy is below the threshold still being marked High.
		// ------------------------------------------------------------------
		for (int i = 0; i < segments.Count; i++)
		{
			EnergySegment seg = segments[i];
			if (seg.startBeatIndex < 0 || seg.endBeatIndex >= beatEnergies.Count)
			{
				continue;
			}

			double sumE = 0.0;
			int countE = 0;
			for (int bi = seg.startBeatIndex; bi <= seg.endBeatIndex; bi++)
			{
				sumE += beatEnergies[bi];
				countE++;
			}
			float avgEnergy = (countE > 0) ? (float)(sumE / countE) : 0f;

			EnergyBand band;
			if (avgEnergy < lowThreshold)
			{
				band = EnergyBand.Low;
			}
			else if (avgEnergy < highThreshold)
			{
				band = EnergyBand.Mid;
			}
			else
			{
				band = EnergyBand.High;
			}

			seg.band = band;
			segments[i] = seg;

			if (enableDebugLogs && logEnergySegments)
			{
				Debug.Log(
					$"[RhythmConductor] Segment {i}: band={seg.band}, beats=[{seg.startBeatIndex}-{seg.endBeatIndex}], " +
					$"time=[{seg.startTime:F3},{seg.endTime:F3}]s, duration={seg.Duration:F3}s, avgEnergy={avgEnergy:F3}");
			}
		}

		// ------------------------------------------------------------------
		// 4) Generate notes for each segment based on its band
		// ------------------------------------------------------------------
		System.Random rng = new System.Random();

		foreach (var seg in segments)
		{
			switch (seg.band)
			{
				case EnergyBand.Low:
					GenerateLowEnergyNotes(seg, beatTimes, firstBeat, beatPeriod, laneCount, rng);
					break;
				case EnergyBand.Mid:
					GenerateMidEnergyNotes(seg, beatTimes, firstBeat, laneCount, rng);
					break;
				case EnergyBand.High:
					GenerateHighEnergyNotes(seg, beatTimes, firstBeat, beatPeriod, laneCount, rng);
					break;
			}
		}

		if (enableDebugLogs)
		{
			Debug.Log($"[RhythmConductor] Generated chart with {_chart.Count} notes from {beatTimes.Count} beats and {segments.Count} segments.");
		}
	}

	void GenerateLowEnergyNotes(EnergySegment seg, List<float> beatTimes, float firstBeat, float beatPeriod, int laneCount, System.Random rng)
	{
		// 二分音符：每 2 拍一个落点，从该段内随机选择一个 offset（0 或 1）开始
		int length = seg.endBeatIndex - seg.startBeatIndex + 1;
		if (length <= 0) return;

		int offset = rng.Next(0, 2); // 0 or 1

		for (int i = seg.startBeatIndex + offset; i <= seg.endBeatIndex; i += 2)
		{
			float beatTime = beatTimes[i];
			float hitTimeSong = beatTime - firstBeat;
			if (hitTimeSong < 0f) continue;

			int laneIndex = rng.Next(0, laneCount);

			var sn = new NoteGenerator.ScheduledNote
			{
				hitTimeSong = hitTimeSong,
				laneIndex = laneIndex,
				spawnTimeWorld = 0f
			};
			_chart.Add(sn);
		}
	}

	void GenerateMidEnergyNotes(EnergySegment seg, List<float> beatTimes, float firstBeat, int laneCount, System.Random rng)
	{
		// 四分音符：8 拍为一块，四种 pattern 交替
		int totalBeats = seg.endBeatIndex - seg.startBeatIndex + 1;
		if (totalBeats <= 0) return;

		int blockStart = seg.startBeatIndex;

		while (blockStart + 7 <= seg.endBeatIndex)
		{
			// 0: Interaction, 1: Vertical, 2: Random8, 3: Cut
			int patternType = rng.Next(0, 4);

			int laneA = rng.Next(0, laneCount);
			int laneB = rng.Next(0, laneCount);
			if (laneB == laneA && laneCount > 1)
			{
				laneB = (laneA + 1) % laneCount;
			}

			int laneV = rng.Next(0, laneCount);

			for (int k = 0; k < 8; k++)
			{
				int beatIndex = blockStart + k;
				float beatTime = beatTimes[beatIndex];
				float hitTimeSong = beatTime - firstBeat;
				if (hitTimeSong < 0f) continue;

				int laneIndex = 0;

				switch (patternType)
				{
					case 0: // Interaction A,B,A,B,...
						laneIndex = (k % 2 == 0) ? laneA : laneB;
						break;
					case 1: // Vertical chain
						laneIndex = laneV;
						break;
					case 2: // Random 8
						laneIndex = rng.Next(0, laneCount);
						break;
					case 3: // Cut: 0,1,2,3,0,1,2,3 (mod laneCount)
						laneIndex = k % laneCount;
						break;
				}

				var sn = new NoteGenerator.ScheduledNote
				{
					hitTimeSong = hitTimeSong,
					laneIndex = laneIndex,
					spawnTimeWorld = 0f
				};
				_chart.Add(sn);
			}

			blockStart += 8;
		}

		// 尾部不足 8 拍：使用简化的随机四分音符填充
		for (int i = blockStart; i <= seg.endBeatIndex; i++)
		{
			float beatTime = beatTimes[i];
			float hitTimeSong = beatTime - firstBeat;
			if (hitTimeSong < 0f) continue;

			int laneIndex = rng.Next(0, laneCount);

			var sn = new NoteGenerator.ScheduledNote
			{
				hitTimeSong = hitTimeSong,
				laneIndex = laneIndex,
				spawnTimeWorld = 0f
			};
			_chart.Add(sn);
		}
	}

	void GenerateHighEnergyNotes(EnergySegment seg, List<float> beatTimes, float firstBeat, float beatPeriod, int laneCount, System.Random rng)
	{
		// 高能量：4 拍为一块，块内部使用八分音符及多押 pattern
		int totalBeats = seg.endBeatIndex - seg.startBeatIndex + 1;
		if (totalBeats <= 0) return;

		int blockStart = seg.startBeatIndex;

		while (blockStart + 3 <= seg.endBeatIndex)
		{
			// Pattern types:
			// 0-3: high-density single-note patterns (interaction / vertical / random8 / cut) on 8 eighth notes
			// 4: continuous double chords (4 groups, quarter-note apart)
			// 5: continuous triple chords (4 groups, quarter-note apart)
			// 6: random multi-chords (each of 4 groups is 2 or 3 lanes)
			int patternType = rng.Next(0, 7);

			switch (patternType)
			{
				case 0:
				case 1:
				case 2:
				case 3:
					GenerateHighSingleNotePattern(blockStart, beatTimes, firstBeat, beatPeriod, laneCount, patternType, rng);
					break;
				case 4:
					GenerateChordPattern(blockStart, beatTimes, firstBeat, laneCount, 2, rng);
					break;
				case 5:
					GenerateChordPattern(blockStart, beatTimes, firstBeat, laneCount, 3, rng);
					break;
				case 6:
					GenerateRandomMultiChordPattern(blockStart, beatTimes, firstBeat, laneCount, rng);
					break;
			}

			blockStart += 4;
		}

		// 尾部不足 4 拍：用中能量的随机四分音符规则填充
		for (int i = blockStart; i <= seg.endBeatIndex; i++)
		{
			float beatTime = beatTimes[i];
			float hitTimeSong = beatTime - firstBeat;
			if (hitTimeSong < 0f) continue;

			int laneIndex = rng.Next(0, laneCount);

			var sn = new NoteGenerator.ScheduledNote
			{
				hitTimeSong = hitTimeSong,
				laneIndex = laneIndex,
				spawnTimeWorld = 0f
			};
			_chart.Add(sn);
		}
	}

	void GenerateHighSingleNotePattern(int blockStartBeat, List<float> beatTimes, float firstBeat, float beatPeriod, int laneCount, int patternType, System.Random rng)
	{
		// High single-note patterns on 8 eighth notes within 4 beats.
		int laneA = rng.Next(0, laneCount);
		int laneB = rng.Next(0, laneCount);
		if (laneB == laneA && laneCount > 1)
		{
			laneB = (laneA + 1) % laneCount;
		}

		int laneV = rng.Next(0, laneCount);

		for (int k = 0; k < 8; k++)
		{
			int beatOffset = k / 2; // 0..3
			bool secondEighth = (k % 2) == 1;

			int beatIndex = blockStartBeat + beatOffset;
			float baseTime = beatTimes[beatIndex];
			float tAbs = baseTime + (secondEighth ? beatPeriod * 0.5f : 0f);
			float hitTimeSong = tAbs - firstBeat;
			if (hitTimeSong < 0f) continue;

			int laneIndex = 0;

			switch (patternType)
			{
				case 0: // High Interaction: A,B,A,B,...
					laneIndex = (k % 2 == 0) ? laneA : laneB;
					break;
				case 1: // High Vertical
					laneIndex = laneV;
					break;
				case 2: // High Random8
					laneIndex = rng.Next(0, laneCount);
					break;
				case 3: // High Cut
					laneIndex = k % laneCount;
					break;
			}

			var sn = new NoteGenerator.ScheduledNote
			{
				hitTimeSong = hitTimeSong,
				laneIndex = laneIndex,
				spawnTimeWorld = 0f
			};
			_chart.Add(sn);
		}
	}

	void GenerateChordPattern(int blockStartBeat, List<float> beatTimes, float firstBeat, int laneCount, int chordSize, System.Random rng)
	{
		// chordSize: 2 for double, 3 for triple. 4 groups, each on quarter-note positions.
		for (int g = 0; g < 4; g++)
		{
			int beatIndex = blockStartBeat + g;
			float tAbs = beatTimes[beatIndex];
			float hitTimeSong = tAbs - firstBeat;
			if (hitTimeSong < 0f) continue;

			// Randomly pick chordSize distinct lanes
			if (laneCount <= 0) continue;

			// Simple approach: shuffle lane indices and take first N
			int[] lanes = new int[laneCount];
			for (int i = 0; i < laneCount; i++) lanes[i] = i;

			// Fisher-Yates shuffle (partial: we only need first chordSize elements)
			for (int i = 0; i < chordSize && i < laneCount; i++)
			{
				int swapIndex = rng.Next(i, laneCount);
				int tmp = lanes[i];
				lanes[i] = lanes[swapIndex];
				lanes[swapIndex] = tmp;
			}

			int actualSize = Mathf.Min(chordSize, laneCount);
			for (int j = 0; j < actualSize; j++)
			{
				var sn = new NoteGenerator.ScheduledNote
				{
					hitTimeSong = hitTimeSong,
					laneIndex = lanes[j],
					spawnTimeWorld = 0f
				};
				_chart.Add(sn);
			}
		}
	}

	void GenerateRandomMultiChordPattern(int blockStartBeat, List<float> beatTimes, float firstBeat, int laneCount, System.Random rng)
	{
		// 4 groups; each group is either a double or triple chord at quarter-note positions.
		for (int g = 0; g < 4; g++)
		{
			int beatIndex = blockStartBeat + g;
			float tAbs = beatTimes[beatIndex];
			float hitTimeSong = tAbs - firstBeat;
			if (hitTimeSong < 0f) continue;

			if (laneCount <= 0) continue;

			int chordSize = (laneCount >= 3) ? rng.Next(2, 4) : 2; // 2 or 3 if possible, otherwise 2

			int[] lanes = new int[laneCount];
			for (int i = 0; i < laneCount; i++) lanes[i] = i;

			for (int i = 0; i < chordSize && i < laneCount; i++)
			{
				int swapIndex = rng.Next(i, laneCount);
				int tmp = lanes[i];
				lanes[i] = lanes[swapIndex];
				lanes[swapIndex] = tmp;
			}

			int actualSize = Mathf.Min(chordSize, laneCount);
			for (int j = 0; j < actualSize; j++)
			{
				var sn = new NoteGenerator.ScheduledNote
				{
					hitTimeSong = hitTimeSong,
					laneIndex = lanes[j],
					spawnTimeWorld = 0f
				};
				_chart.Add(sn);
			}
		}
	}

	/// <summary>
	/// External entry point to start the game.
	/// Records the world begin time, calculates travel time, and hands the chart to NoteGenerator.
	/// </summary>
	public void BeginGame()
	{
		if (_gameplayStarted)
		{
			return;
		}

		if (_chart == null || _chart.Count == 0)
		{
			Debug.LogWarning("[RhythmConductor] BeginGame called but chart is empty.");
		}

		if (noteGenerator == null)
		{
			Debug.LogError("[RhythmConductor] BeginGame: NoteGenerator is null.");
			return;
		}

		_worldBeginTime = Time.time;

		float travelTime = noteGenerator.GetTravelTime();
		if (travelTime < 0f) travelTime = 0f;

		_musicStartWorldTime = _worldBeginTime + travelTime;

		noteGenerator.LoadChart(_chart, _worldBeginTime);

		// Initialize scoring for this chart if a ScoreManager is present.
		var scoreManager = FindObjectOfType<ScoreManager>();
		if (scoreManager != null)
		{
			scoreManager.InitializeForChart(_chart != null ? _chart.Count : 0, scoreManager.totalScoreMax);
		}

		_gameplayStarted = true;
		_musicStarted = false;

		if (enableDebugLogs)
		{
			Debug.Log($"[RhythmConductor] BeginGame: worldBegin={_worldBeginTime:F3}, travelTime={travelTime:F3}, musicStartWorld={_musicStartWorldTime:F3}");
		}
	}

	void Update()
	{
		if (!_gameplayStarted || _musicStarted)
		{
			return;
		}

		if (musicSource == null)
		{
			musicSource = musicDriver != null ? musicDriver.GetComponent<AudioSource>() : null;
		}

		if (musicSource == null)
		{
			Debug.LogError("[RhythmConductor] No AudioSource available to start music.");
			_musicStarted = true;
			return;
		}

		if (Time.time >= _musicStartWorldTime)
		{
			musicSource.time = 0f;
			musicSource.Play();
			_musicStarted = true;

			if (enableDebugLogs)
			{
				Debug.Log($"[RhythmConductor] Music started at worldTime={Time.time:F3}s (scheduled start={_musicStartWorldTime:F3}s).");
			}
		}
	}

	/// <summary>
	/// Current music time in seconds relative to the first beat (0 at music start), or 0 if musicSource is null.
	/// </summary>
	public float CurrentMusicTime
	{
		get
		{
			if (musicSource == null) return 0f;
			return musicSource.time;
		}
	}
}


