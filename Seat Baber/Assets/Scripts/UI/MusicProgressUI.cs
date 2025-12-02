using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple UI driver for displaying current music playback progress.
/// 
/// Attach this in the gameplay scene and wire:
/// - conductor (optional): RhythmConductor providing musicSource
/// - musicSource (optional): explicit AudioSource reference
/// - progressSlider or fillImage: one of them will be driven by normalized progress
/// - currentTimeText / totalTimeText (optional): mm:ss display
/// </summary>
public class MusicProgressUI : MonoBehaviour
{
	[Header("References")]
	public RhythmConductor conductor;
	public AudioSource musicSource;

	[Header("UI")]
	public Slider progressSlider;
	public Image fillImage;
	public TMP_Text currentTimeText;
	public TMP_Text totalTimeText;

	void Awake()
	{
		if (conductor == null)
		{
			conductor = FindObjectOfType<RhythmConductor>();
		}

		if (musicSource == null && conductor != null)
		{
			musicSource = conductor.musicSource;
		}
	}

	void Update()
	{
		AudioSource src = musicSource;
		if (src == null && conductor != null)
		{
			src = conductor.musicSource;
		}

		float progress = 0f;
		float curTime = 0f;
		float totalTime = 0f;

		if (src != null && src.clip != null)
		{
			curTime = src.time;
			totalTime = Mathf.Max(0.001f, src.clip.length);
			progress = Mathf.Clamp01(curTime / totalTime);
		}

		if (progressSlider != null)
		{
			progressSlider.normalizedValue = progress;
		}

		if (fillImage != null)
		{
			fillImage.fillAmount = progress;
		}

		if (currentTimeText != null)
		{
			currentTimeText.text = FormatTime(curTime);
		}

		if (totalTimeText != null)
		{
			totalTimeText.text = FormatTime(totalTime);
		}
	}

	static string FormatTime(float seconds)
	{
		if (seconds < 0f) seconds = 0f;
		int totalSeconds = Mathf.FloorToInt(seconds);
		int mins = totalSeconds / 60;
		int secs = totalSeconds % 60;
		return $"{mins:00}:{secs:00}";
	}
}


