using System;
using System.Collections.Generic;
using RhythmGame.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RhythmGame.Input
{
	public class RhythmInputRouter : MonoBehaviour
	{
		[Header("Refs")]
		[SerializeField] private NoteGenerator noteGenerator;

		[Header("Judge Windows (ms)")]
		[SerializeField] private float perfectWindowMs = 33f;
		[SerializeField] private float greatWindowMs = 67f;
		[SerializeField] private float goodWindowMs = 200f;

		public event Action<int, JudgeResult, float> OnJudge;

		private readonly Dictionary<string, int> keyToLaneIndex = new Dictionary<string, int>();
		private readonly List<InputAction> actions = new List<InputAction>();

		private static readonly string[] LeftKeys = { "1", "2", "3", "4", "5" };
		// Right pool in left->right physical order: 6,7,8,9,0
		private static readonly string[] RightKeys = { "6", "7", "8", "9", "0" };

		private bool initialized = false;

		private void OnEnable()
		{
			if (initialized)
			{
				EnableActions(true);
			}
		}

		private void Start()
		{
			if (!initialized)
			{
				SetupBindings();
				initialized = true;
			}
			EnableActions(true);
		}

		private void OnDisable()
		{
			EnableActions(false);
		}

		private void OnDestroy()
		{
			EnableActions(false);
			CleanupActions();
			keyToLaneIndex.Clear();
		}

		private void SetupBindings()
		{
			if (noteGenerator == null)
			{
				Debug.LogError("[RhythmInputRouter] noteGenerator not assigned.");
				return;
			}

			var tracks = noteGenerator.Tracks;
			if (tracks == null || tracks.Count == 0)
			{
				Debug.LogWarning("[RhythmInputRouter] No tracks available to bind.");
				return;
			}

			int trackCount = tracks.Count;
			if (trackCount > 10)
			{
				Debug.LogWarning($"[RhythmInputRouter] trackCount {trackCount} > 10, only first 10 lanes will be bind.");
				trackCount = 10;
			}

			// Compute left/right counts
			int leftCount = trackCount / 2;
			int rightCount = trackCount - leftCount;

			// Build left side (from LeftKeys start)
			List<string> laneKeys = new List<string>(trackCount);
			for (int i = 0; i < leftCount; i++)
			{
				laneKeys.Add(LeftKeys[i]);
			}

			// Build right side: take the last 'rightCount' of RightKeys to make the right-most be '0'
			// Keep them in left->right order.
			int start = Math.Max(0, RightKeys.Length - rightCount);
			for (int i = start; i < RightKeys.Length; i++)
			{
				laneKeys.Add(RightKeys[i]);
			}

			Debug.Log($"[RhythmInputRouter] Initializing {trackCount} lanes. Keys: {string.Join(",", laneKeys)}");

			// Create InputActions for each lane
			for (int laneIndex = 0; laneIndex < trackCount; laneIndex++)
			{
				string key = laneKeys[laneIndex];
				string binding = $"<Keyboard>/{key}";

				var action = new InputAction(name: $"Lane{laneIndex}", type: InputActionType.Button);
				// Ensure we get performed on key down
				action.AddBinding(binding).WithInteraction("Press(behavior=1)");

				int capturedIndex = laneIndex;
				action.performed += _ => OnLanePressed(capturedIndex);

				actions.Add(action);
				keyToLaneIndex[key] = capturedIndex;

				// Debug.Log($"[RhythmInputRouter] Bind Lane {capturedIndex} to key '{key}'");
			}
		}

		private void EnableActions(bool enable)
		{
			for (int i = 0; i < actions.Count; i++)
			{
				if (enable) actions[i].Enable();
				else actions[i].Disable();
			}
		}

		private void CleanupActions()
		{
			for (int i = 0; i < actions.Count; i++)
			{
				actions[i].Dispose();
			}
			actions.Clear();
		}

		private void OnLanePressed(int laneIndex)
		{
			// Debug.Log("[RhythmInputRouter] Lane " + laneIndex);
			var tracks = noteGenerator.Tracks;
			if (laneIndex < 0 || laneIndex >= tracks.Count) return;

			var track = tracks[laneIndex];
			if (track == null) return;

			if (track.TryJudgeAndConsume(perfectWindowMs, greatWindowMs, goodWindowMs, out var result, out var offsetMs))
			{
				OnJudge?.Invoke(laneIndex, result, offsetMs);
			}
		}
	}
}

