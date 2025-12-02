using UnityEngine;

/// <summary>
/// At scene start, applies the selected AudioClip from GameSession (if any)
/// to the MusicDriver's AudioSource so that RhythmConductor generates a chart
/// for the chosen song.
/// 
/// Place this in the gameplay scene on a suitable GameObject.
/// </summary>
public class MusicLoader : MonoBehaviour
{
	public MusicDriver musicDriver;
	public AudioSource musicSource;

	void Awake()
	{
		if (musicDriver == null)
		{
			musicDriver = FindObjectOfType<MusicDriver>();
		}

		if (musicSource == null && musicDriver != null)
		{
			musicSource = musicDriver.GetComponent<AudioSource>();
		}

		var session = GameSession.Instance;
		if (session != null && session.selectedClip != null && musicSource != null)
		{
			musicSource.clip = session.selectedClip;
		}
	}
}


