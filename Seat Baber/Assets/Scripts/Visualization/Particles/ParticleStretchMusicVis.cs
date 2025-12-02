using UnityEngine;

/// <summary>
/// Music-driven particle visualization that controls:
/// - Start speed and lifetime speed multiplier based on global music energy.
/// - Stretch Billboard length/velocity scale so faster particles appear more elongated.
/// - Per-particle lifetime color gradient from two reference materials.
/// </summary>
public class ParticleStretchMusicVis : MusicVisualization
{
	[Header("Targets")]
	[Tooltip("Target ParticleSystem. If left null, will auto-fetch from the same GameObject.")]
	public ParticleSystem particleSystem;

	[Header("Materials / Colors")]
	[Tooltip("Material providing the start color (lifetime t = 0).")]
	public Material fromMaterial;
	[Tooltip("Material providing the end color (lifetime t = 1).")]
	public Material toMaterial;
	[Tooltip("If true, colors are fetched from materials. Otherwise, manual colors are used.")]
	public bool autoFetchColorsFromMaterials = true;
	[Tooltip("Manual start color when autoFetchColorsFromMaterials is false.")]
	public Color manualStartColor = Color.white;
	[Tooltip("Manual end color when autoFetchColorsFromMaterials is false.")]
	public Color manualEndColor = Color.white;

	[Header("Speed (Energy Driven)")]
	[Min(0f)] public float baseStartSpeed = 1f;
	[Min(0f)] public float maxStartSpeedScale = 3f;
	[Tooltip("Base multiplier applied to lifetime speed (via Velocity over Lifetime speedModifier).")]
	[Min(0f)] public float baseLifetimeSpeedMultiplier = 1f;
	[Tooltip("Max additional lifetime speed multiplier at energy = 1.")]
	[Min(0f)] public float maxLifetimeSpeedMultiplierBoost = 2f;
	[Tooltip("Optional curve mapping energy [0,1] to a normalized speed scale [0,1].")]
	public AnimationCurve energyToSpeedCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

	[Header("Smoothing")]
	public bool useSmoothing = true;
	[Min(0f)] public float attack = 0.02f;
	[Min(0f)] public float release = 0.15f;

	// Cached modules
	ParticleSystem.MainModule _main;
	ParticleSystem.VelocityOverLifetimeModule _velLifetime;
	ParticleSystem.EmissionModule _emission;
	ParticleSystemRenderer _renderer;
	[Header("Emission (Energy Driven)")]
	[Tooltip("Base emission rate over time when energy is 0.")]
	[Min(0f)] public float baseEmissionRateOverTime = 10f;
	[Tooltip("Max additional emission rate at energy = 1.")]
	[Min(0f)] public float maxEmissionRateBoost = 40f;
	[Tooltip("Optional curve mapping energy [0,1] to a normalized emission scale [0,1].")]
	public AnimationCurve energyToEmissionCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);


// Smoothed state
float _currentStartSpeed;
float _currentLifetimeSpeedMult;
float _currentEmissionRate;
bool _initializedState;

	protected override void OnEnable()
	{
		base.OnEnable();

		if (particleSystem == null)
		{
			particleSystem = GetComponent<ParticleSystem>();
		}

		if (particleSystem != null)
		{
			_main = particleSystem.main;
			_velLifetime = particleSystem.velocityOverLifetime;
			_velLifetime.enabled = true; // ensure lifetime speed is actually applied
			_emission = particleSystem.emission;
			_emission.enabled = true;
		}

		_renderer = GetComponent<ParticleSystemRenderer>();

		InitializeStateFromCurrent();
		ApplyGradientFromMaterialsOrManual();

		// Ensure Stretch Billboard so lengthScale/velocityScale have visible effect.
		if (_renderer != null)
		{
			_renderer.renderMode = ParticleSystemRenderMode.Stretch;
		}
	}

	protected override void OnDisable()
	{
		// Reset to base values so editor/other scripts see sensible defaults when disabled.
		ResetToBaseValues();

		base.OnDisable();
	}

	void InitializeStateFromCurrent()
	{
	if (particleSystem == null)
	{
		_currentStartSpeed = baseStartSpeed;
		_currentLifetimeSpeedMult = baseLifetimeSpeedMultiplier;
		_currentEmissionRate = baseEmissionRateOverTime;
	}
	else
	{
		var main = particleSystem.main;
		var startSpeed = main.startSpeed;
		_currentStartSpeed = startSpeed.mode == ParticleSystemCurveMode.Constant
			? startSpeed.constant
			: baseStartSpeed;

		var vel = particleSystem.velocityOverLifetime;
		var speedMod = vel.speedModifier;
		_currentLifetimeSpeedMult = speedMod.mode == ParticleSystemCurveMode.Constant
			? speedMod.constant
			: baseLifetimeSpeedMultiplier;

		var emission = particleSystem.emission;
		var rate = emission.rateOverTime;
		_currentEmissionRate = rate.mode == ParticleSystemCurveMode.Constant
			? rate.constant
			: baseEmissionRateOverTime;
	}

	_initializedState = true;
	}

	void ApplyGradientFromMaterialsOrManual()
	{
		if (particleSystem == null)
		{
			return;
		}

		Color startColor;
		Color endColor;

		if (autoFetchColorsFromMaterials && fromMaterial != null && toMaterial != null)
		{
			startColor = GetColorFromMaterial(fromMaterial, manualStartColor);
			endColor = GetColorFromMaterial(toMaterial, manualEndColor);
		}
		else
		{
			startColor = manualStartColor;
			endColor = manualEndColor;
		}

		var g = new Gradient();
		g.SetKeys(
			new[]
			{
				new GradientColorKey(startColor, 0f),
				new GradientColorKey(endColor, 1f)
			},
			new[]
			{
				new GradientAlphaKey(startColor.a, 0f),
				new GradientAlphaKey(endColor.a, 1f)
			}
		);

		var main = particleSystem.main;
		var gradient = new ParticleSystem.MinMaxGradient(g);
		main.startColor = gradient;

		// Ensure color over lifetime uses the same gradient so particles change color across their lifetime.
		var colorOverLifetime = particleSystem.colorOverLifetime;
		colorOverLifetime.enabled = true;
		colorOverLifetime.color = gradient;
	}

	static Color GetColorFromMaterial(Material mat, Color fallback)
	{
		if (mat == null) return fallback;

		// Try shader-specific and common color properties without touching mat.color
		// to avoid accessing a non-existent _Color property.
		if (mat.HasProperty("_lineColor"))
		{
			return mat.GetColor("_lineColor");
		}
		if (mat.HasProperty("_BaseColor"))
		{
			return mat.GetColor("_BaseColor");
		}
		if (mat.HasProperty("_Color"))
		{
			return mat.GetColor("_Color");
		}

		return fallback;
	}

	void ResetToBaseValues()
	{
		if (particleSystem != null)
		{
			var main = particleSystem.main;
			main.startSpeed = new ParticleSystem.MinMaxCurve(baseStartSpeed);

			var vel = particleSystem.velocityOverLifetime;
			var speedMod = vel.speedModifier;
			speedMod.mode = ParticleSystemCurveMode.Constant;
			speedMod.constant = baseLifetimeSpeedMultiplier;
			vel.speedModifier = speedMod;

			var emission = particleSystem.emission;
			var rate = emission.rateOverTime;
			rate.mode = ParticleSystemCurveMode.Constant;
			rate.constant = baseEmissionRateOverTime;
			emission.rateOverTime = rate;
		}

	_initializedState = false;
	}

	protected override void MusicVisualize(in MusicFrame frame)
	{
		if (particleSystem == null)
		{
			return;
		}

		// Determine dt from frame or fall back to Time.deltaTime.
		float dt = frame.frameDuration;
		if (dt <= 0f)
		{
			float fallbackDt = Time.deltaTime;
			if (fallbackDt <= 0f) fallbackDt = 1f / 60f;
			dt = fallbackDt;
		}

		// Global energy from MusicDriver (norm, respecting channel selection).
		float energy = Mathf.Clamp01(GetEnergy(in frame));

		// Map energy -> normalized speed/emission scales via optional curves.
		float speedT = EvaluateCurve01(energyToSpeedCurve, energy);
		float emissionT = EvaluateCurve01(energyToEmissionCurve, energy);

		// Target values based on current energy.
		float targetStartSpeed = baseStartSpeed * (1f + maxStartSpeedScale * speedT);
		float targetLifetimeSpeedMult = baseLifetimeSpeedMultiplier * (1f + maxLifetimeSpeedMultiplierBoost * speedT);
		float targetEmissionRate = baseEmissionRateOverTime * (1f + maxEmissionRateBoost * emissionT);

		if (!_initializedState)
		{
			_currentStartSpeed = targetStartSpeed;
			_currentLifetimeSpeedMult = targetLifetimeSpeedMult;
			_currentEmissionRate = targetEmissionRate;
			_initializedState = true;
		}
		else
		{
			_currentStartSpeed = SmoothAR(_currentStartSpeed, targetStartSpeed, dt);
			_currentLifetimeSpeedMult = SmoothAR(_currentLifetimeSpeedMult, targetLifetimeSpeedMult, dt);
			_currentEmissionRate = SmoothAR(_currentEmissionRate, targetEmissionRate, dt);
		}

		// Apply to ParticleSystem.
		var main = _main;
		var startSpeedCurve = main.startSpeed;
		startSpeedCurve.mode = ParticleSystemCurveMode.Constant;
		startSpeedCurve.constant = Mathf.Max(0f, _currentStartSpeed);
		main.startSpeed = startSpeedCurve;
		_main = main;

		var vel = _velLifetime;
		var speedMod = vel.speedModifier;
		speedMod.mode = ParticleSystemCurveMode.Constant;
		speedMod.constant = Mathf.Max(0f, _currentLifetimeSpeedMult);
		vel.speedModifier = speedMod;
		_velLifetime = vel;

		// Emission driven by energy.
		var emission = _emission;
		var rate = emission.rateOverTime;
		rate.mode = ParticleSystemCurveMode.Constant;
		rate.constant = Mathf.Max(0f, _currentEmissionRate);
		emission.rateOverTime = rate;
		_emission = emission;

		// No dynamic stretch: renderer properties are left as configured in the ParticleSystem.
	}

	float SmoothAR(float current, float target, float dt)
	{
		if (!useSmoothing || dt <= 0f)
		{
			return target;
		}

		if (Mathf.Approximately(current, target))
		{
			return target;
		}

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

	static float EvaluateCurve01(AnimationCurve curve, float t)
	{
		float clampedT = Mathf.Clamp01(t);
		if (curve == null || curve.keys == null || curve.keys.Length == 0)
		{
			return clampedT;
		}
		return Mathf.Clamp01(curve.Evaluate(clampedT));
	}
}


