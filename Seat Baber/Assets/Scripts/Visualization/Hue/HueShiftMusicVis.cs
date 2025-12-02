using UnityEngine;

/// <summary>
/// Changes the color of two materials when the music flux (energy difference) exceeds a threshold.
/// Enforces a cooldown based on BPM and a specified number of notes.
/// </summary>
public class HueShiftMusicVis : MusicVisualization
{
    [Header("Targets")]
    public Material targetMaterial1;
    public Material targetMaterial2;

    [Header("Trigger Settings")]
    [Tooltip("Minimum normalized energy (0-1) required to trigger a color shift when no kick is present.")]
    [Range(0f, 1f)] public float energyThreshold = 0.3f;
    [Tooltip("If true, any detected kick (frame.isBeat) can also trigger a color shift.")]
    public bool useKick = true;

    [Header("Cooldown Settings")]
    [Tooltip("Number of beats (quarter notes) to wait before allowing another trigger.")]
    [Min(1)] public int cooldownNotes = 4;
    
    [Header("Color Settings")]
    [Tooltip("Amount to shift the hue (0-1) each trigger.")]
    [Range(0f, 1f)] public float hueShiftAmount = 0.15f;
    [Tooltip("Saturation for the new color.")]
    [Range(0f, 1f)] public float saturation = 0.8f;
    [Tooltip("Value/Brightness for the new color.")]
    [Range(0f, 1f)] public float value = 1.0f;

    private float _lastTriggerTime;
    private float _currentHue1;
    private float _currentHue2;

	Color GetMaterialColor(Material mat)
	{
		if (mat == null) return Color.white;

		// Try custom/project-specific properties first
		if (mat.HasProperty("_lineColor")) return mat.GetColor("_lineColor");

		// Then common Universal/Standard properties
		if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
		if (mat.HasProperty("_Color")) return mat.GetColor("_Color");

		// Fallback to emission if enabled
		if (mat.IsKeywordEnabled("_EMISSION") && mat.HasProperty("_EmissionColor"))
		{
			return mat.GetColor("_EmissionColor");
		}

		return Color.white;
	}

    protected override void OnEnable()
    {
        base.OnEnable();
        // Initialize current hues from materials if possible
        if (targetMaterial1 != null)
        {
			Color c1 = GetMaterialColor(targetMaterial1);
			Color.RGBToHSV(c1, out _currentHue1, out _, out _);
        }
        if (targetMaterial2 != null)
        {
			Color c2 = GetMaterialColor(targetMaterial2);
			Color.RGBToHSV(c2, out _currentHue2, out _, out _);
        }
    }

    protected override void MusicVisualize(in MusicFrame frame)
    {
		if (enableDebugLogs)
		{
			Debug.Log($"[HueShiftMusicVis] name={name} norm={frame.norm:F3} isBeat={frame.isBeat} " +
			          $"energyThreshold={energyThreshold:F3} bpm={frame.bpm:F1} " +
			          $"time={Time.time:F2}");
		}

        // 1. Check Trigger: energy OR kick
        float energy = Mathf.Clamp01(frame.norm);
        bool energyTrigger = energy >= energyThreshold;
        bool kickTrigger = useKick && frame.isBeat;

        if (!energyTrigger && !kickTrigger)
        {
			if (enableDebugLogs)
			{
				Debug.Log($"[HueShiftMusicVis] no trigger (energy/kick). " +
				          $"norm={energy:F3}, isBeat={frame.isBeat}, energyThreshold={energyThreshold:F3}");
			}
            return;
        }

        // 2. Check Cooldown
        // Calculate dynamic cooldown based on current BPM
        float bpm = frame.bpm;
        if (bpm <= 0.1f) bpm = 120f; // Fallback to 120 if BPM detection isn't ready

        float secondsPerBeat = 60f / bpm;
        float cooldownDuration = secondsPerBeat * cooldownNotes;

        if (Time.time - _lastTriggerTime < cooldownDuration)
        {
            return;
        }

        // 3. Trigger Color Shift
        ShiftColor(ref _currentHue1, targetMaterial1);
        ShiftColor(ref _currentHue2, targetMaterial2);

        _lastTriggerTime = Time.time;
    }

    private void ShiftColor(ref float currentHue, Material mat)
    {
        if (mat == null) return;

        // Shift hue
        currentHue = (currentHue + hueShiftAmount) % 1.0f;

        // Create new color
        Color newColor = Color.HSVToRGB(currentHue, saturation, value);

		// Apply to material
		// Try custom property used by GlowLine shader, then common ones.
		if (mat.HasProperty("_lineColor"))
		{
			mat.SetColor("_lineColor", newColor);
		}
		else if (mat.HasProperty("_BaseColor"))
		{
			mat.SetColor("_BaseColor", newColor);
		}
		else if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", newColor);
        }
    }
}

