using System;
using UnityEngine;
using RhythmGame.Core;
using RhythmGame.Input;

public class JudgementBillboard : MonoBehaviour
{
	[Header("Binding")]
	public int laneIndex = -1;
	public RhythmInputRouter inputRouter;
	public Camera mainCamera;

	[Header("Prefabs (World Space)")]
	public GameObject perfectPrefabWS;
	public GameObject greatPrefabWS;
	public GameObject goodPrefabWS;

	[Header("Spawn")]
	public Vector3 spawnOffset = new Vector3(0f, 0.5f, 0f);

	void Awake()
	{
		if (inputRouter == null)
		{
			inputRouter = FindAnyObjectByType<RhythmInputRouter>();
		}
		if (mainCamera == null)
		{
			mainCamera = Camera.main;
		}
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

	private void HandleJudge(int lane, JudgeResult result, float offsetMs)
	{
		if (lane != laneIndex) return;
		GameObject prefab = null;
		switch (result)
		{
			case JudgeResult.Perfect: prefab = perfectPrefabWS; break;
			case JudgeResult.Great:   prefab = greatPrefabWS;   break;
			case JudgeResult.Good:    prefab = goodPrefabWS;    break;
			default: return; // Miss 忽略
		}
		if (prefab == null) return;

		GameObject go = Instantiate(prefab, transform);
		Transform t = go.transform;
		t.localPosition = spawnOffset;
		t.localRotation = Quaternion.identity;

		// 确保文字 Canvas 为 World Space 且绑定相机
		var canvas = go.GetComponentInChildren<Canvas>();
		if (canvas != null)
		{
			canvas.renderMode = RenderMode.WorldSpace;
			if (canvas.worldCamera == null) canvas.worldCamera = mainCamera;
		}

		// 确保有上浮+淡出逻辑
		if (go.GetComponent<JudgementFloatText>() == null)
		{
			go.AddComponent<JudgementFloatText>();
		}
	}
}





