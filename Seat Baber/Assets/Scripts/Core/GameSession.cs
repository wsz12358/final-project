using UnityEngine;

/// <summary>
/// Simple cross-scene session object used to store the currently selected music clip.
/// 
/// Place one instance in the main menu scene and mark it as persistent via DontDestroyOnLoad.
/// </summary>
public class GameSession : MonoBehaviour
{
	public static GameSession Instance { get; private set; }

	[Header("Selection")]
	public AudioClip selectedClip;
	public string selectedClipName;

	void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
		DontDestroyOnLoad(gameObject);
	}

	public void SetSelectedClip(AudioClip clip)
	{
		selectedClip = clip;
		selectedClipName = clip != null ? clip.name : string.Empty;
	}
}


