using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles in-game flow:
/// - Long-press R to return to main menu with fade-out.
/// - Auto return to main menu a few seconds after the song ends.
/// </summary>
public class GameFlowController : MonoBehaviour
{
	[Header("References")]
	public RhythmConductor conductor;
	public AudioSource musicSource;
	public BlackScreenFader blackScreenFader;

	[Header("Settings")]
	[Tooltip("Seconds the R key must be held to trigger return.")]
	public float rHoldThresholdSeconds = 1.0f;

	[Tooltip("Seconds to wait after the song ends before auto-return.")]
	public float songEndAutoReturnDelaySeconds = 3.0f;

	[Tooltip("Name of the main menu scene to load when returning.")]
	public string mainMenuSceneName = "MainMenu";

	bool _isReturning;
	bool _songHasEnded;
	bool _musicStarted;
	float _songEndTimer;
	float _rHoldTimer;

	void Awake()
	{
		if (conductor == null)
		{
			conductor = FindObjectOfType<RhythmConductor>();
		}

		if (musicSource == null)
		{
			if (conductor != null && conductor.musicSource != null)
			{
				musicSource = conductor.musicSource;
			}
			else
			{
				var driver = FindObjectOfType<MusicDriver>();
				if (driver != null)
				{
					musicSource = driver.GetComponent<AudioSource>();
				}
			}
		}
	}

	void Update()
	{
		if (_isReturning)
		{
			return;
		}

		HandleRKeyHold();
		HandleSongEnd();
	}

	void HandleRKeyHold()
	{
		if (Input.GetKey(KeyCode.R))
		{
			_rHoldTimer += Time.deltaTime;
			if (_rHoldTimer >= rHoldThresholdSeconds && !_isReturning)
			{
				StartReturnToMainMenu();
			}
		}
		else
		{
			_rHoldTimer = 0f;
		}
	}

	void HandleSongEnd()
	{
		if (musicSource == null || musicSource.clip == null)
		{
			return;
		}

		if (!_musicStarted && musicSource.isPlaying)
		{
			_musicStarted = true;
		}

		if (_musicStarted && !_songHasEnded && !musicSource.isPlaying)
		{
			_songHasEnded = true;
			_songEndTimer = 0f;
		}

		if (_songHasEnded)
		{
			_songEndTimer += Time.deltaTime;
			if (_songEndTimer >= songEndAutoReturnDelaySeconds && !_isReturning)
			{
				StartReturnToMainMenu();
			}
		}
	}

	void StartReturnToMainMenu()
	{
		_isReturning = true;

		if (string.IsNullOrEmpty(mainMenuSceneName))
		{
			Debug.LogError("[GameFlowController] mainMenuSceneName is not set.");
			return;
		}

		if (blackScreenFader != null)
		{
			blackScreenFader.FadeToBlack(1f, () =>
			{
				SceneManager.LoadScene(mainMenuSceneName);
			});
		}
		else
		{
			SceneManager.LoadScene(mainMenuSceneName);
		}
	}
}


