using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls a full-screen black Image for fade-in / fade-out transitions.
/// 
/// Attach this to a UI Image (black, full screen) and use FadeToBlack / FadeFromBlack.
/// </summary>
public class BlackScreenFader : MonoBehaviour
{
	public Image blackImage;
	public float defaultFadeDuration = 1f;

	Coroutine _currentFade;

	void Awake()
	{
		if (blackImage == null)
		{
			blackImage = GetComponent<Image>();
		}

		if (blackImage != null)
		{
			Color c = blackImage.color;
			c.a = 0f;
			blackImage.color = c;
		}
	}

	public void FadeToBlack(float duration, Action onComplete = null)
	{
		StartFade(1f, duration <= 0f ? defaultFadeDuration : duration, onComplete);
	}

	public void FadeFromBlack(float duration, Action onComplete = null)
	{
		StartFade(0f, duration <= 0f ? defaultFadeDuration : duration, onComplete);
	}

	void StartFade(float targetAlpha, float duration, Action onComplete)
	{
		if (blackImage == null)
		{
			onComplete?.Invoke();
			return;
		}

		if (_currentFade != null)
		{
			StopCoroutine(_currentFade);
		}

		if (duration <= 0f)
		{
			Color c = blackImage.color;
			c.a = targetAlpha;
			blackImage.color = c;
			onComplete?.Invoke();
			return;
		}

		_currentFade = StartCoroutine(FadeRoutine(targetAlpha, duration, onComplete));
	}

	IEnumerator FadeRoutine(float targetAlpha, float duration, Action onComplete)
	{
		float startAlpha = blackImage.color.a;
		float t = 0f;

		while (t < duration)
		{
			t += Time.deltaTime;
			float lerp = Mathf.Clamp01(t / duration);
			float a = Mathf.Lerp(startAlpha, targetAlpha, lerp);

			Color c = blackImage.color;
			c.a = a;
			blackImage.color = c;

			yield return null;
		}

		onComplete?.Invoke();
		_currentFade = null;
	}
}


