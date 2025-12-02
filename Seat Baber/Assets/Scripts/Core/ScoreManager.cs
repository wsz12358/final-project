using RhythmGame.Core;
using RhythmGame.Input;
using TMPro;
using UnityEngine;

/// <summary>
/// Session-local score manager.
/// Distributes a fixed max score across all notes and awards
/// different percentages based on JudgeResult.
/// 
/// Attach this in the gameplay scene and wire:
/// - inputRouter: RhythmInputRouter in the scene
/// - scoreText: TextMeshProUGUI used to display the current score
/// 
/// RhythmConductor should call InitializeForChart once the chart is ready.
/// </summary>
public class ScoreManager : MonoBehaviour
{
	[Header("Scoring")]
	[Tooltip("Maximum theoretical score for a full combo of Perfect hits.")]
	public float totalScoreMax = 1_000_000f;

	[Tooltip("Weight for Perfect (1.0 = 100%).")]
	public float perfectWeight = 1.0f;

	[Tooltip("Weight for Great (0.8 = 80%).")]
	public float greatWeight = 0.8f;

	[Tooltip("Weight for Good (0.5 = 50%).")]
	public float goodWeight = 0.5f;

	[Tooltip("Weight for Miss (0.0 = 0%).")]
	public float missWeight = 0.0f;

	[Header("References")]
	public RhythmInputRouter inputRouter;
	public TMP_Text scoreText;

	int _totalNotes;
	float _baseScorePerNote;
	float _currentScore;

	int _judgedNotes;
	int _perfectCount;
	int _greatCount;
	int _goodCount;
	int _missCount;

	void Awake()
	{
		if (inputRouter == null)
		{
			inputRouter = FindObjectOfType<RhythmInputRouter>();
		}
		UpdateScoreText();
	}

	void OnEnable()
	{
		if (inputRouter != null)
		{
			inputRouter.OnJudge += HandleJudge;
		}
	}

	void OnDisable()
	{
		if (inputRouter != null)
		{
			inputRouter.OnJudge -= HandleJudge;
		}
	}

	/// <summary>
	/// Initialize scoring for a newly generated chart.
	/// Should be called once per gameplay session by RhythmConductor.
	/// </summary>
	/// <param name="totalNotes">Number of notes in the chart.</param>
	/// <param name="maxScore">Maximum score for all-Perfect run.</param>
	public void InitializeForChart(int totalNotes, float maxScore)
	{
		_totalNotes = Mathf.Max(1, totalNotes);
		totalScoreMax = Mathf.Max(1f, maxScore);

		_baseScorePerNote = totalScoreMax / _totalNotes;

		_currentScore = 0f;
		_judgedNotes = 0;
		_perfectCount = 0;
		_greatCount = 0;
		_goodCount = 0;
		_missCount = 0;

		UpdateScoreText();
	}

	void HandleJudge(int lane, JudgeResult result, float offsetMs)
	{
		if (_totalNotes <= 0)
		{
			// Not initialized yet; ignore scoring.
			return;
		}

		float weight = 0f;
		switch (result)
		{
			case JudgeResult.Perfect:
				weight = perfectWeight;
				_perfectCount++;
				break;
			case JudgeResult.Great:
				weight = greatWeight;
				_greatCount++;
				break;
			case JudgeResult.Good:
				weight = goodWeight;
				_goodCount++;
				break;
			case JudgeResult.Miss:
				weight = missWeight;
				_missCount++;
				break;
		}

		_judgedNotes++;
		_currentScore += _baseScorePerNote * weight;
		_currentScore = Mathf.Min(_currentScore, totalScoreMax);

		UpdateScoreText();
	}

	void UpdateScoreText()
	{
		if (scoreText == null)
		{
			return;
		}

		// Display as integer with thousand separators.
		int displayScore = Mathf.RoundToInt(_currentScore);
		scoreText.text = displayScore.ToString("N0");
	}
}


