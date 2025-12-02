using UnityEngine;
using System.Collections.Generic;
using RhythmGame.Core;

public class NoteTrack : MonoBehaviour
{
    [Header("配置")]
    public GameObject notePrefab;   // 在 Track Prefab 上赋值
    public Transform endTrigger;    // NoteGenerator 会给
    public float launchSpeed = 20f;
    public float destroyOffset = 50f;

    private Queue<Note> notes = new Queue<Note>();
    private Vector3 launchDir = Vector3.back;  // 固定 Negative Z

	/// <summary>
	/// 按窗口进行判定并消费队首 Note。返回是否发生了判定（true=有音符被消费）。
	/// </summary>
	public bool TryJudgeAndConsume(float perfectMs, float greatMs, float goodMs, out JudgeResult result, out float offsetMs)
	{
		result = JudgeResult.Miss;
		offsetMs = 0f;
		if (notes.Count == 0) return false;

		Note note = notes.Peek();
		if (note == null)
		{
			notes.Dequeue();
			return false;
		}

		// 带符号时间偏差：正=偏早，负=偏晚（由空间/速度换算）
		float dz = note.transform.position.z - endTrigger.position.z;
		float offsetSeconds = dz / launchSpeed;
		offsetMs = offsetSeconds * 1000f;

		float absMs = Mathf.Abs(offsetMs);
		if (absMs <= perfectMs)
		{
			result = JudgeResult.Perfect;
			notes.Dequeue();
			Destroy(note.gameObject);
			return true;
		}
		else if (absMs <= greatMs)
		{
			result = JudgeResult.Great;
			notes.Dequeue();
			Destroy(note.gameObject);
			return true;
		}
		else if (absMs <= goodMs)
		{
			result = JudgeResult.Great; // will be overridden to Good below
			result = JudgeResult.Good;
			notes.Dequeue();
			Destroy(note.gameObject);
			return true;
		}

		// 超出 Good 窗口：区分过早与过晚
		// 过晚（offsetMs < -goodMs）：Miss 并消费
		if (offsetMs < -goodMs)
		{
			result = JudgeResult.Miss;
			notes.Dequeue();
			Destroy(note.gameObject);
			return true;
		}

		// 过早（offsetMs > goodMs）：不判定不消费
		return false;
	}

    /// <summary>
    /// NoteGenerator 调用，发射一颗 note。
    /// </summary>
    public void SpawnAndLaunchNote()
    {
        if (notePrefab == null)
        {
            Debug.LogError($"{name}: notePrefab 未设置！");
            return;
        }

        GameObject go = Instantiate(notePrefab, transform.position, Quaternion.identity);
        Note n = go.GetComponent<Note>();
        n.SetSpeed(launchSpeed);
        n.Launch(launchDir);
        n.transform.position = transform.position;
        n.transform.parent = transform;
        notes.Enqueue(n);
    }

    void Update()
    {
        CleanupNotes();
    }

    /// <summary>
    /// 超过 endTrigger 后面的 note 自动销毁。
    /// </summary>
    void CleanupNotes()
    {
        while (notes.Count > 0)
        {
            Note n = notes.Peek();
            if (n == null)
            {
                notes.Dequeue();
                continue;
            }

            if (n.transform.position.z < endTrigger.position.z - destroyOffset)
            {
                Destroy(n.gameObject);
                notes.Dequeue();
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>
    /// 输入事件击打 note（按键系统会调用）
    /// </summary>
    public void OnInputHit()
    {
		if (TryJudgeAndConsume(33f, 67f, 200f, out var result, out var offsetMs))
		{
			Debug.Log($"[OnInputHit] {result} offset={offsetMs:F1}ms");
		}
    }
}
