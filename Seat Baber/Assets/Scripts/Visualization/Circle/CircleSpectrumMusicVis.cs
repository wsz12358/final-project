using UnityEngine;

/// <summary>
/// Drives a circle of VisableGroup points using per-band spectrum data from MusicDriver.
/// This lets the imported circlevisualization setup participate in the unified MusicDriver/MusicVisualization pipeline.
/// </summary>
public class CircleSpectrumMusicVis : MusicVisualization
{
	[Header("Targets")]
	[Tooltip("If empty, all VisableGroup components in children will be auto-collected.")]
	public VisableGroup[] groups;
	public bool autoCollectGroups = true;

	[Header("Spectrum Mapping")]
	[Tooltip("Maximum spectrum index to sample (original demo used values up to ~300).")]
	[Min(1)] public int maxSpectrumIndex = 300;
	[Tooltip("Extra multiplier applied on raw spectrum magnitude before passing into VisableGroup.")]
	[Min(0f)] public float spectrumScale = 1.0f;

	[Header("Energy Gating")]
	[Tooltip("Raise normalized energy to this power before applying to spectrum (1 = linear).")]
	[Min(0f)] public float energyExponent = 1.0f;
	[Tooltip("Global gain applied after energy gating.")]
	[Min(0f)] public float globalGain = 1.0f;

	void EnsureGroups()
	{
		if (!autoCollectGroups)
		{
			return;
		}

		if (groups == null || groups.Length == 0)
		{
			groups = GetComponentsInChildren<VisableGroup>(includeInactive: false);
		}
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		EnsureGroups();
	}

	protected override void MusicVisualize(in MusicFrame frame)
	{
		EnsureGroups();
		if (groups == null || groups.Length == 0)
		{
			return;
		}

		var spectrum = MusicDriver.Spectrum512;
		if (spectrum == null || spectrum.Length == 0)
		{
			return;
		}

		// Use the base class helper to respect selected channel.
		float energy = Mathf.Clamp01(GetEnergy(in frame));
		if (energyExponent > 0f)
		{
			energy = Mathf.Pow(energy, energyExponent);
		}

		float energyGain = energy * globalGain;
		int spectrumLen = spectrum.Length;

		for (int i = 0; i < groups.Length; i++)
		{
			var g = groups[i];
			if (g == null) continue;

			int safeGroup = Mathf.Max(1, g.groupIndex);

			// Match original behavior: index = 300 / groupIndex (clamped and scaled).
			int rawIndex = maxSpectrumIndex / safeGroup;
			int index = Mathf.Clamp(rawIndex, 0, spectrumLen - 1);

			float bandValue = spectrum[index] * spectrumScale;
			// Combine band magnitude with overall RMS-based energy gate.
			float sample = bandValue * energyGain;

			g.ApplySample(sample);
		}
	}
}


