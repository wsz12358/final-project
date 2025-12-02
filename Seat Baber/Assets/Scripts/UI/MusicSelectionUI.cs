using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Main-menu music selection UI.
/// 
/// Configure in the main menu scene:
/// - availableClips: list of AudioClips (e.g., believer, hanatachini kibouwo, pets)
/// - clipButtons: corresponding UI Buttons
/// - selectedLabel (optional): text showing the currently selected song name
/// - gameplaySceneName: name of the gameplay scene (e.g., "MusicGame")
/// 
/// Each clip button should call SelectClip(index) via its OnClick.
/// The "Start" button should call StartGame().
/// </summary>
public class MusicSelectionUI : MonoBehaviour
{
	[Header("Auto Scan")]
	[Tooltip("If true, automatically scan Resources/[resourcesFolderName] for AudioClips.")]
	public bool autoScanResourcesFolder = true;

	[Tooltip("Subfolder name under a Resources folder, e.g. \"Music\" -> Resources/Music.")]
	public string resourcesFolderName = "Music";

	[Header("Music Options")]
	public List<AudioClip> availableClips = new List<AudioClip>();

	[Header("UI")]
	public TMP_Dropdown dropdown;
	public TMP_Text selectedLabel;

	[Header("Scenes")]
	public string gameplaySceneName = "MusicGame";

	[Header("Colors")]
	public Color normalColor = Color.white;
	public Color selectedColor = Color.yellow;

	int _selectedIndex = 0;

	void Start()
	{
		if (autoScanResourcesFolder)
		{
			AutoScanResources();
		}

		// Ensure a valid initial selection.
		if (availableClips.Count > 0)
		{
			_selectedIndex = Mathf.Clamp(_selectedIndex, 0, availableClips.Count - 1);
		}

		BuildDropdownOptions();
		UpdateSelectedLabel();

		if (dropdown != null)
		{
			dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
			if (availableClips.Count > 0)
			{
				dropdown.value = _selectedIndex;
				dropdown.RefreshShownValue();
			}
		}
	}

	void AutoScanResources()
	{
		if (string.IsNullOrEmpty(resourcesFolderName))
		{
			Debug.LogWarning("[MusicSelectionUI] resourcesFolderName is empty, cannot auto-scan Resources.");
			return;
		}

		AudioClip[] clips = Resources.LoadAll<AudioClip>(resourcesFolderName);
		if (clips == null || clips.Length == 0)
		{
			Debug.LogWarning($"[MusicSelectionUI] No AudioClips found in Resources/{resourcesFolderName}.");
			return;
		}

		availableClips.Clear();
		availableClips.AddRange(clips);
	}

	public void SelectClip(int index)
	{
		if (index < 0 || index >= availableClips.Count)
		{
			return;
		}

		_selectedIndex = index;
		if (dropdown != null && index != dropdown.value)
		{
			dropdown.value = index;
			dropdown.RefreshShownValue();
		}

		UpdateSelectedLabel();
	}

	public void StartGame()
	{
		if (availableClips.Count == 0)
		{
			Debug.LogWarning("[MusicSelectionUI] No clips configured.");
			return;
		}

		var session = GameSession.Instance;
		if (session == null)
		{
			Debug.LogError("[MusicSelectionUI] GameSession.Instance is null. Make sure a GameSession object exists in the scene.");
			return;
		}

		var clip = availableClips[Mathf.Clamp(_selectedIndex, 0, availableClips.Count - 1)];
		session.SetSelectedClip(clip);

		if (string.IsNullOrEmpty(gameplaySceneName))
		{
			Debug.LogError("[MusicSelectionUI] gameplaySceneName is not set.");
			return;
		}

		SceneManager.LoadScene(gameplaySceneName);
	}

	void BuildDropdownOptions()
	{
		if (dropdown == null)
		{
			return;
		}

		dropdown.ClearOptions();

		var options = new List<TMP_Dropdown.OptionData>();
		for (int i = 0; i < availableClips.Count; i++)
		{
			var clip = availableClips[i];
			string name = clip != null ? clip.name : $"Clip {i}";
			options.Add(new TMP_Dropdown.OptionData(name));
		}

		dropdown.AddOptions(options);
		dropdown.interactable = availableClips.Count > 0;
	}

	void OnDropdownValueChanged(int index)
	{
		_selectedIndex = Mathf.Clamp(index, 0, availableClips.Count - 1);
		UpdateSelectedLabel();
	}

	void UpdateSelectedLabel()
	{
		if (selectedLabel == null) return;

		if (_selectedIndex >= 0 && _selectedIndex < availableClips.Count && availableClips[_selectedIndex] != null)
		{
			selectedLabel.text = availableClips[_selectedIndex].name;
		}
		else
		{
			selectedLabel.text = "-";
		}
	}
}


