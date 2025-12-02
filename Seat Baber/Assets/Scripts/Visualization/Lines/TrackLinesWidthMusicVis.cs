using UnityEngine;

/// <summary>
/// Controls widths of left/right track LineRenderers driven by L/R channel energies.
/// </summary>
public class TrackLinesWidthMusicVis : MusicVisualization
{
	[Header("Targets")]

	public CustomLine[] leftCustomLines;
	public CustomLine[] rightCustomLines;

	[Header("Energy (RMS Mapping)")]
	[Tooltip("Scale raw RMS (rmsL/rmsR) into [0,1] visual energy via Clamp01(rms * rmsEnergyScaleX).")]
	[Min(0f)] public float rmsEnergyScaleL = 10f;
	[Min(0f)] public float rmsEnergyScaleR = 10f;

	[Header("Width Mapping")]
	[Min(0f)] public float minWidth = 0.02f;
	[Min(0f)] public float baseWidth = 0.05f;
	[Min(0f)] public float maxWidth = 0.30f;

	[Header("Smoothing (seconds)")]
	[Min(0f)] public float attack = 0.01f;
	[Min(0f)] public float release = 0.10f;

	float _debugAccum;

	float _sL;
	float _sR;

	protected override void MusicVisualize(in MusicFrame frame)
	{
		float dt = Mathf.Max(0f, frame.frameDuration);

		// Target energies per channel: use globally-normalized L/R energies from MusicDriver.
		float el = Mathf.Clamp01(frame.normL);
		float er = Mathf.Clamp01(frame.normR);

		// AR smoothing
		_sL = SmoothAR(_sL, el, attack, release, dt);
		_sR = SmoothAR(_sR, er, attack, release, dt);

		// Map to widths
		float wl = Mathf.Max(minWidth, baseWidth + (maxWidth - baseWidth) * _sL);
		float wr = Mathf.Max(minWidth, baseWidth + (maxWidth - baseWidth) * _sR);

		ApplyWidth(leftCustomLines, wl);
		ApplyWidth(rightCustomLines, wr);

		if (enableDebugLogs)
		{
			_debugAccum += Mathf.Max(0f, frame.frameDuration);
			if (_debugAccum >= 0.5f)
			{
				_debugAccum = 0f;
				int lcl = leftCustomLines != null ? leftCustomLines.Length : 0;
				int rcl = rightCustomLines != null ? rightCustomLines.Length : 0;
				string sample = "";
				if (lcl > 0 && leftCustomLines[0] != null) sample += $" CL0.w={leftCustomLines[0].width:F3}";
			}
		}
	}

	float SmoothAR(float current, float target, float atk, float rel, float dt)
	{
		if (target > current)
		{
			if (atk <= 1e-6f) return target;
			float k = 1f - Mathf.Exp(-dt / atk);
			return current + (target - current) * k;
		}
		else
		{
			if (rel <= 1e-6f) return target;
			float k = 1f - Mathf.Exp(-dt / rel);
			return current + (target - current) * k;
		}
	}

	void ApplyWidth(CustomLine[] lines, float width)
	{
		if (lines == null) return;
		for (int i = 0; i < lines.Length; i++)
		{
			var cl = lines[i];
			if (cl == null) continue;
			cl.width = width;
			// Ensure underlying LineRenderer receives new width immediately
			cl.SendMessage("ApplyAll", SendMessageOptions.DontRequireReceiver);
		}
	}
}


