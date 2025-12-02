using UnityEngine;
using System.Collections.Generic;

public class NoteGenerator : MonoBehaviour
{
    [Header("轨道设置")]
    public GameObject mainTrack;        // 场景中的立方体
    public GameObject noteTrackPrefab;  // NoteTrack prefab
    public GameObject noteTrackParent;
    public Transform endTrigger;        // 场景中的 NoteEndTrigger
    public int trackCount = 4;

    public float trackWidth = 2.5f;

    [Header("随机谱面（已弃用：节奏模式会使用预生成谱面）")]
    public float minInterval = 0.3f;
    public float maxInterval = 1.0f;

	[Header("Judgement Billboards")]
	public GameObject judgementBillboardPrefab;		// 在 Inspector 指定
	public GameObject judgementBillboardParent;	// 可选父物体；为空则放到 noteTrackParent
	public Vector3 billboardOffset = new Vector3(0f, 0.5f, 0f);

    private List<NoteTrack> tracks = new List<NoteTrack>();

    /// <summary>
    /// Pre-generated chart note entry.
    /// hitTimeSong is on the music time axis (0 at music start).
    /// spawnTimeWorld is on the world/game time axis (0 at scene start).
    /// </summary>
    public struct ScheduledNote
    {
        public float hitTimeSong;
        public int laneIndex;
        public float spawnTimeWorld;
    }

    private readonly List<ScheduledNote> scheduledNotes = new List<ScheduledNote>();
    private int nextNoteIndex = 0;
    private float worldBeginTime = 0f;
    private bool chartLoaded = false;

	public IReadOnlyList<NoteTrack> Tracks => tracks;

    void OnEnable()
    {
        GenerateTracks();
		GenerateJudgementBillboards();
    }

    /// <summary>
    /// 生成多个 NoteTrack
    /// </summary>
    void GenerateTracks()
    {
        mainTrack.transform.localScale = new Vector3(trackWidth * trackCount, mainTrack.transform.localScale.y, mainTrack.transform.localScale.z);
        Vector3 basePos = mainTrack.transform.position;
        for (int i = 0; i < trackCount; i++)
        {
            Vector3 pos = basePos + new Vector3((i - trackCount / 2f + 0.5f) * trackWidth, -basePos.y / 2, 0);
            pos.z = transform.position.z;
            GameObject tObj = Instantiate(noteTrackPrefab, pos, Quaternion.identity);
            NoteTrack track = tObj.GetComponent<NoteTrack>();
            track.endTrigger = endTrigger;

            tracks.Add(track);
            tObj.transform.parent = noteTrackParent.transform;
        }
    }

	/// <summary>
	/// 为每条轨道在 endTrigger 附近生成一个 JudgementBillboard，并初始化 laneIndex 与 Canvas/相机。
	/// </summary>
	void GenerateJudgementBillboards()
	{
		if (judgementBillboardPrefab == null)
		{
			Debug.LogWarning("[NoteGenerator] judgementBillboardPrefab 未设置，跳过生成。");
			return;
		}
		if (endTrigger == null)
		{
			Debug.LogWarning("[NoteGenerator] endTrigger 未设置，跳过生成 JudgementBillboard。");
			return;
		}

		Vector3 basePos = mainTrack.transform.position;
		Transform parent = (judgementBillboardParent != null ? judgementBillboardParent.transform : noteTrackParent.transform);
		Camera cam = Camera.main;

		for (int i = 0; i < trackCount; i++)
		{
			// 与 NoteTrack 相同的左右排布（x/y），但 z 放在 endTrigger 位置，并加上可调偏移
			Vector3 pos = basePos + new Vector3((i - trackCount / 2f + 0.5f) * trackWidth, -basePos.y / 2, 0);
			pos.z = endTrigger.position.z;
			pos += billboardOffset;

			GameObject go = Instantiate(judgementBillboardPrefab, pos, Quaternion.identity);
			go.transform.parent = parent;

			// 面向相机（如果预制体未自带组件，则添加）
			var face = go.GetComponent<FaceCameraBillboard>();
			if (face == null) face = go.AddComponent<FaceCameraBillboard>();
			if (face.mainCamera == null) face.mainCamera = cam;

			// 初始化 laneIndex、相机引用
			var jb = go.GetComponent<JudgementBillboard>();
			if (jb != null)
			{
				jb.laneIndex = i;
				if (jb.mainCamera == null) jb.mainCamera = cam;
			}

			// 若 Billboard 预制体内含有 Canvas，强制设为 World Space 并指定相机
			var canvas = go.GetComponentInChildren<Canvas>();
			if (canvas != null)
			{
				canvas.renderMode = RenderMode.WorldSpace;
				if (canvas.worldCamera == null) canvas.worldCamera = cam;
			}
		}
	}

    void Update()
    {
        if (!chartLoaded || tracks.Count == 0)
        {
            return;
        }

        float now = Time.time;

        // Spawn all notes whose spawn time has passed.
        while (nextNoteIndex < scheduledNotes.Count &&
               scheduledNotes[nextNoteIndex].spawnTimeWorld <= now)
        {
            var sn = scheduledNotes[nextNoteIndex];
            int lane = Mathf.Clamp(sn.laneIndex, 0, tracks.Count - 1);
            tracks[lane].SpawnAndLaunchNote();
            nextNoteIndex++;
        }
    }

    /// <summary>
    /// Called by RhythmConductor to load a full pre-generated chart.
    /// worldBeginTime defines the world time at which hitTimeSong=0 (first beat) should be considered.
    /// </summary>
    public void LoadChart(IReadOnlyList<ScheduledNote> notes, float worldBeginTime)
    {
        this.worldBeginTime = worldBeginTime;
        scheduledNotes.Clear();
        nextNoteIndex = 0;

        if (notes == null || notes.Count == 0)
        {
            chartLoaded = false;
            return;
        }

        // Convert music-time hit times to world-time spawn times.
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            n.spawnTimeWorld = worldBeginTime + n.hitTimeSong;
            scheduledNotes.Add(n);
        }

        // Ensure notes are sorted by spawn time.
        scheduledNotes.Sort((a, b) => a.spawnTimeWorld.CompareTo(b.spawnTimeWorld));

        chartLoaded = true;
    }

    /// <summary>
    /// Compute the travel time from the track spawn position to the endTrigger based on launchSpeed.
    /// Used by RhythmConductor to align the first note's perfect time with music start.
    /// </summary>
    public float GetTravelTime()
    {
        if (endTrigger == null || tracks.Count == 0)
        {
            return 0.5f;
        }

        NoteTrack track = tracks[0];
        float spawnZ = track.transform.position.z;
        float judgeZ = endTrigger.position.z;
        float speed = Mathf.Max(0.01f, track.launchSpeed);

        float distance = spawnZ - judgeZ;
        float travelTime = distance / speed;

        // Ensure non-negative travel time.
        if (travelTime < 0f) travelTime = -travelTime;
        return travelTime;
    }
}