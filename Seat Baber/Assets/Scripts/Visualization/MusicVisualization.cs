using UnityEngine;

/// <summary>
/// Base class for all music-driven visualizations. Automatically subscribes to MusicDriver frames.
/// </summary>
public abstract class MusicVisualization : MonoBehaviour
{
	public enum Channel
	{
		Both,
		Left,
		Right
	}

	[Header("Music Input")]
	public Channel channel = Channel.Both;

	[Header("Debug")]
	public bool enableDebugLogs = false;
	float _debugGateAccum;

	protected virtual void OnEnable()
	{
		MusicDriver.OnMusicFrame += HandleMusicFrame;
	}

	protected virtual void OnDisable()
	{
		MusicDriver.OnMusicFrame -= HandleMusicFrame;
	}

	void HandleMusicFrame(MusicFrame frame)
	{
		// Optional layer gating: only process when on the active visualization layer (if set)
		if (MusicDriver.ActiveVisLayer >= 0 && gameObject.layer != MusicDriver.ActiveVisLayer)
		{
			if (enableDebugLogs)
			{
				_debugGateAccum += Time.unscaledDeltaTime;
				if (_debugGateAccum >= 1.0f)
				{
					_debugGateAccum = 0f;
					Debug.Log($"[{GetType().Name}] Skipping frame due to layer mismatch. ActiveVisLayer={MusicDriver.ActiveVisLayer}, gameObject.layer={gameObject.layer}, name='{name}'");
				}
			}
			return;
		}
		MusicVisualize(in frame);
	}

	protected float GetEnergy(in MusicFrame frame)
	{
		switch (channel)
		{
			case Channel.Left: return frame.normL;
			case Channel.Right: return frame.normR;
			default: return frame.norm;
		}
	}

	/// <summary>
	/// Implement the specific visualization logic using the provided per-buffer frame.
	/// </summary>
	protected abstract void MusicVisualize(in MusicFrame frame);
}


