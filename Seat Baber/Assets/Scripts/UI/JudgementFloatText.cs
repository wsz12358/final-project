using UnityEngine;
using TMPro;
using System.Collections;

public class JudgementFloatText : MonoBehaviour
{
	[Header("Motion")]
	public float lifetime = 0.8f;
	public float floatDistance = 0.4f;

	private TMP_Text tmp;
	private Vector3 startLocalPos;

	void Awake()
	{
		tmp = GetComponent<TMP_Text>();
		if (tmp == null)
		{
			tmp = GetComponentInChildren<TMP_Text>();
		}
	}

	void OnEnable()
	{
		startLocalPos = transform.localPosition;
		StartCoroutine(PlayAndAutoDestroy());
	}

	private IEnumerator PlayAndAutoDestroy()
	{
		float t = 0f;
		while (t < lifetime)
		{
			float normalized = Mathf.Clamp01(t / lifetime);
			// 上浮
			transform.localPosition = startLocalPos + Vector3.up * (normalized * floatDistance);
			// 透明度
			if (tmp != null)
			{
				Color c = tmp.color;
				c.a = 1f - normalized;
				tmp.color = c;
			}
			t += Time.deltaTime;
			yield return null;
		}
		Destroy(gameObject);
	}
}





