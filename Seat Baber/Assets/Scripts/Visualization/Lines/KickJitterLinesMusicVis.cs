using UnityEngine;

/// <summary>
/// Music visualization that controls left/right groups of CustomLine objects:
/// - Width & area light intensity are driven only by kick (frame.isBeat).
/// - Between kicks, widths/lights decay smoothly back to base values.
/// - Optional small spatial jitter for a subtle floating effect.
/// </summary>
public class KickJitterLinesMusicVis : MusicVisualization
{
	[Header("Targets")]
	public CustomLine[] leftCustomLines;
	public CustomLine[] rightCustomLines;

	[Header("Width")]
	[Min(0f)] public float baseWidth = 0.05f;
	[Min(0f)] public float maxKickScale = 2.0f;
	[Min(0f)] public float minWidth = 0.01f;
	[Tooltip("Maps input energy [0,1] to a normalized scale [0,1] before applying kick/energy scales.")]
	public AnimationCurve energyToScaleCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

	[Header("Width Smoothing (seconds)")]
	[Min(0f)] public float kickAttack = 0.01f;
	[Min(0f)] public float kickRelease = 0.15f;

	[Header("Area Light (Kick Only)")]
	[Min(0f)] public float baseAreaLightIntensity = 2.0f;
	[Min(0f)] public float maxKickAreaLightScale = 2.5f;
	[Min(0f)] public float areaAttack = 0.01f;
	[Min(0f)] public float areaRelease = 0.15f;

	[Header("Energy Trigger (Energy Driven)")]
	[Range(0f, 1f)] public float energyTriggerThreshold = 0.3f;
	[Min(0f)] public float maxEnergyWidthScale = 1.0f;
	[Min(0f)] public float maxEnergyAreaLightScale = 1.5f;

	[Header("Jitter / Floating")]
	public bool enableJitter = true;
	[Min(0f)] public float positionJitterAmplitude = 0.05f;
	[Min(0f)] public float rotationJitterAmplitude = 2.0f;
	[Min(0f)] public float jitterSpeed = 1.5f;

	// Internal width state
	float _targetWidthL;
	float _targetWidthR;
	float _currentWidthL;
	float _currentWidthR;

	// Internal area light state
	float _targetAreaL;
	float _targetAreaR;
	float _currentAreaL;
	float _currentAreaR;

	// Jitter state
	Vector3[] _initialPosL;
	Vector3[] _initialPosR;
	Quaternion[] _initialRotL;
	Quaternion[] _initialRotR;
	float[] _jitterSeedL;
	float[] _jitterSeedR;

	// Debug
	float _debugAccum;

	protected override void OnEnable()
	{
		base.OnEnable();
		InitializeState();

		if (enableDebugLogs)
		{
			int lCount = leftCustomLines != null ? leftCustomLines.Length : 0;
			int rCount = rightCustomLines != null ? rightCustomLines.Length : 0;
			Debug.Log($"[KickJitterLinesMusicVis] OnEnable '{name}' | left={lCount}, right={rCount}, layer={gameObject.layer}, ActiveVisLayer={MusicDriver.ActiveVisLayer}");
		}
	}

	protected override void OnDisable()
	{
		ResetTransforms();
		base.OnDisable();
	}

	void InitializeState()
	{
		// Initialize width and area light states to base values
		_currentWidthL = _targetWidthL = Mathf.Max(minWidth, baseWidth);
		_currentWidthR = _targetWidthR = Mathf.Max(minWidth, baseWidth);

		_currentAreaL = _targetAreaL = Mathf.Max(0f, baseAreaLightIntensity);
		_currentAreaR = _targetAreaR = Mathf.Max(0f, baseAreaLightIntensity);

		// Initialize jitter caches
		InitSideJitter(leftCustomLines, ref _initialPosL, ref _initialRotL, ref _jitterSeedL);
		InitSideJitter(rightCustomLines, ref _initialPosR, ref _initialRotR, ref _jitterSeedR);
	}

	void InitSideJitter(CustomLine[] lines, ref Vector3[] initialPos, ref Quaternion[] initialRot, ref float[] seeds)
	{
		if (lines == null || lines.Length == 0)
		{
			initialPos = null;
			initialRot = null;
			seeds = null;
			return;
		}

		int len = lines.Length;
		initialPos = new Vector3[len];
		initialRot = new Quaternion[len];
		seeds = new float[len];

		for (int i = 0; i < len; i++)
		{
			var cl = lines[i];
			if (cl == null)
			{
				initialPos[i] = Vector3.zero;
				initialRot[i] = Quaternion.identity;
				seeds[i] = 0f;
				continue;
			}

			Transform t = cl.transform;
			initialPos[i] = t.localPosition;
			initialRot[i] = t.localRotation;
			// Use a stable but varied seed per index
			seeds[i] = 17.0f * i + 123.456f;
		}
	}

	void ResetTransforms()
	{
		ResetSideTransforms(leftCustomLines, _initialPosL, _initialRotL);
		ResetSideTransforms(rightCustomLines, _initialPosR, _initialRotR);
	}

	void ResetSideTransforms(CustomLine[] lines, Vector3[] initialPos, Quaternion[] initialRot)
	{
		if (lines == null || initialPos == null || initialRot == null) return;
		int len = Mathf.Min(lines.Length, initialPos.Length, initialRot.Length);
		for (int i = 0; i < len; i++)
		{
			var cl = lines[i];
			if (cl == null) continue;
			var t = cl.transform;
			t.localPosition = initialPos[i];
			t.localRotation = initialRot[i];
		}
	}

	protected override void MusicVisualize(in MusicFrame frame)
	{
		// Determine dt from frame or fall back to Time.deltaTime
		float dt = frame.frameDuration;
		if (dt <= 0f)
		{
			float fallbackDt = Time.deltaTime;
			if (fallbackDt <= 0f) fallbackDt = 1f / 60f;
			dt = fallbackDt;
		}

		// 1. Compute energies and trigger flags
		float eL = Mathf.Clamp01(frame.normL);
		float eR = Mathf.Clamp01(frame.normR);

		bool isBeatFrame = frame.isBeat;
		bool hasLeft = leftCustomLines != null && leftCustomLines.Length > 0;
		bool hasRight = rightCustomLines != null && rightCustomLines.Length > 0;

		bool energyTriggerL = hasLeft && eL >= energyTriggerThreshold;
		bool energyTriggerR = hasRight && eR >= energyTriggerThreshold;

		if (enableDebugLogs && isBeatFrame)
		{
			Debug.Log($"[KickJitterLinesMusicVis] BEAT '{name}' | eL={eL:F3}, eR={eR:F3}, flux={frame.flux:F3}, bpm={frame.bpm:F1}, dt={dt:F4}");
		}

		// 2. Initialize targets to base values each frame
		float baseWidthClamped = Mathf.Max(minWidth, baseWidth);
		float baseAreaClamped = Mathf.Max(0f, baseAreaLightIntensity);

		_targetWidthL = baseWidthClamped;
		_targetWidthR = baseWidthClamped;
		_targetAreaL = baseAreaClamped;
		_targetAreaR = baseAreaClamped;

		// 3. Accumulate kick + energy contributions for left side
		float scaleWidthL = 0f;
		float scaleAreaL = 0f;
		if (hasLeft)
		{
			if (isBeatFrame)
			{
				float beatWidthScaleL = EvaluateEnergyScale(eL, maxKickScale);
				float beatAreaScaleL = EvaluateEnergyScale(eL, maxKickAreaLightScale);
				scaleWidthL += beatWidthScaleL;
				scaleAreaL += beatAreaScaleL;
			}

			if (energyTriggerL)
			{
				float energyWidthScaleL = EvaluateEnergyScale(eL, maxEnergyWidthScale);
				float energyAreaScaleL = EvaluateEnergyScale(eL, maxEnergyAreaLightScale);
				scaleWidthL += energyWidthScaleL;
				scaleAreaL += energyAreaScaleL;
			}

			_targetWidthL = baseWidthClamped * (1f + scaleWidthL);
			_targetAreaL = baseAreaClamped * (1f + scaleAreaL);
		}

		// 4. Accumulate kick + energy contributions for right side
		float scaleWidthR = 0f;
		float scaleAreaR = 0f;
		if (hasRight)
		{
			if (isBeatFrame)
			{
				float beatWidthScaleR = EvaluateEnergyScale(eR, maxKickScale);
				float beatAreaScaleR = EvaluateEnergyScale(eR, maxKickAreaLightScale);
				scaleWidthR += beatWidthScaleR;
				scaleAreaR += beatAreaScaleR;
			}

			if (energyTriggerR)
			{
				float energyWidthScaleR = EvaluateEnergyScale(eR, maxEnergyWidthScale);
				float energyAreaScaleR = EvaluateEnergyScale(eR, maxEnergyAreaLightScale);
				scaleWidthR += energyWidthScaleR;
				scaleAreaR += energyAreaScaleR;
			}

			_targetWidthR = baseWidthClamped * (1f + scaleWidthR);
			_targetAreaR = baseAreaClamped * (1f + scaleAreaR);
		}

		// 5. Smooth current values toward targets with attack/release
		_currentWidthL = SmoothAR(_currentWidthL, _targetWidthL, kickAttack, kickRelease, dt);
		_currentWidthR = SmoothAR(_currentWidthR, _targetWidthR, kickAttack, kickRelease, dt);
		_currentAreaL = SmoothAR(_currentAreaL, _targetAreaL, areaAttack, areaRelease, dt);
		_currentAreaR = SmoothAR(_currentAreaR, _targetAreaR, areaAttack, areaRelease, dt);

		// 3. Apply to CustomLines
		ApplySide(leftCustomLines, _currentWidthL, _currentAreaL);
		ApplySide(rightCustomLines, _currentWidthR, _currentAreaR);

		// 4. Apply jitter / floating offsets
		if (enableJitter)
		{
			float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
			ApplyJitter(leftCustomLines, _initialPosL, _initialRotL, _jitterSeedL, time);
			ApplyJitter(rightCustomLines, _initialPosR, _initialRotR, _jitterSeedR, time);
		}

		// 5. Periodic debug of current state (first line on each side)
		if (enableDebugLogs)
		{
			_debugAccum += dt;
			if (_debugAccum >= 0.25f)
			{
				_debugAccum = 0f;

				var l0 = (leftCustomLines != null && leftCustomLines.Length > 0) ? leftCustomLines[0] : null;
				var r0 = (rightCustomLines != null && rightCustomLines.Length > 0) ? rightCustomLines[0] : null;

				float lWidth = l0 != null ? l0.width : -1f;
				float rWidth = r0 != null ? r0.width : -1f;
				float lArea = l0 != null ? l0.areaLightIntensity : -1f;
				float rArea = r0 != null ? r0.areaLightIntensity : -1f;

				Debug.Log(
					$"[KickJitterLinesMusicVis] State '{name}' | beat={frame.isBeat} " +
					$"| targetWidthL={_targetWidthL:F3}, currentWidthL={_currentWidthL:F3}, line0L={lWidth:F3} " +
					$"| targetWidthR={_targetWidthR:F3}, currentWidthR={_currentWidthR:F3}, line0R={rWidth:F3} " +
					$"| targetAreaL={_targetAreaL:F3}, currentAreaL={_currentAreaL:F3}, area0L={lArea:F3} " +
					$"| targetAreaR={_targetAreaR:F3}, currentAreaR={_currentAreaR:F3}, area0R={rArea:F3}");
			}
		}
	}

	float EvaluateEnergyScale(float energy, float maxScale)
	{
		if (maxScale <= 0f) return 0f;
		float t = Mathf.Clamp01(energy);
		if (energyToScaleCurve != null && energyToScaleCurve.keys != null && energyToScaleCurve.keys.Length > 0)
		{
			t = Mathf.Clamp01(energyToScaleCurve.Evaluate(t));
		}
		return Mathf.Clamp(t * maxScale, 0f, maxScale);
	}

	float SmoothAR(float current, float target, float attack, float release, float dt)
	{
		if (dt <= 0f) return target;

		if (target > current)
		{
			if (attack <= 1e-6f) return target;
			float k = 1f - Mathf.Exp(-dt / Mathf.Max(attack, 1e-6f));
			return current + (target - current) * k;
		}
		else
		{
			if (release <= 1e-6f) return target;
			float k = 1f - Mathf.Exp(-dt / Mathf.Max(release, 1e-6f));
			return current + (target - current) * k;
		}
	}

	void ApplySide(CustomLine[] lines, float width, float areaIntensity)
	{
		if (lines == null) return;

		float clampedWidth = Mathf.Max(minWidth, width);
		float clampedArea = Mathf.Max(0f, areaIntensity);

		for (int i = 0; i < lines.Length; i++)
		{
			var cl = lines[i];
			if (cl == null) continue;

			cl.width = clampedWidth;
			cl.areaLightIntensity = clampedArea;

			// Ensure underlying LineRenderer and AreaLight are updated
			cl.SendMessage("ApplyAll", SendMessageOptions.DontRequireReceiver);
		}
	}

	void ApplyJitter(CustomLine[] lines, Vector3[] initialPos, Quaternion[] initialRot, float[] seeds, float time)
	{
		if (lines == null || initialPos == null || initialRot == null || seeds == null) return;
		if (positionJitterAmplitude <= 0f && rotationJitterAmplitude <= 0f) return;

		int len = Mathf.Min(lines.Length, initialPos.Length, initialRot.Length, seeds.Length);
		for (int i = 0; i < len; i++)
		{
			var cl = lines[i];
			if (cl == null) continue;

			var t = cl.transform;
			float seed = seeds[i];

			// Simple Perlin-noise based jitter in local space
			float nPosX = Mathf.PerlinNoise(seed, time * jitterSpeed);
			float nPosY = Mathf.PerlinNoise(seed + 31.416f, time * jitterSpeed);
			float nAng = Mathf.PerlinNoise(seed + 73.0f, time * jitterSpeed);

			var offset = new Vector3(
				(nPosX - 0.5f) * 2f * positionJitterAmplitude,
				(nPosY - 0.5f) * 2f * positionJitterAmplitude,
				0f
			);

			float angle = (nAng - 0.5f) * 2f * rotationJitterAmplitude;

			t.localPosition = initialPos[i] + offset;
			t.localRotation = initialRot[i] * Quaternion.AngleAxis(angle, Vector3.forward);
		}
	}
}


