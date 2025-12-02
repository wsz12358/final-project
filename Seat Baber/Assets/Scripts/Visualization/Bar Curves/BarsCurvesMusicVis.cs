using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a set of PerpendicularBarsCurve children:
/// - Creates a propagating displacement wave along each curve (index-cascaded delay).
/// - Creates a propagating per-bar rotation cascade around a WORLD axis using random pulses.
/// </summary>
public class BarsCurvesMusicVis : MusicVisualization
{
	[Header("Targets")]
	public PerpendicularBarsCurve[] curves;
	public bool autoCollectCurves = true;

	[Header("Propagation Offset")]
	[Tooltip("Propagation speed in meters per second.")]
	[Min(0.01f)] public float propagationSpeed = 10f;
	[Tooltip("Scale from normalized energy [0,1] to displacement in meters.")]
	[Min(0f)] public float displacementScale = 1.0f;
	[Tooltip("Clamp absolute displacement to this value (meters).")]
	[Min(0.001f)] public float maxDisplacement = 1.5f;

[Header("Rotation Pulse")]
public Vector3 axis = Vector3.up;
[Min(0f)] public float initAngularSpeedMin = 60f;  // deg/s
[Min(0f)] public float initAngularSpeedMax = 540f; // deg/s
[Min(0.001f)] public float decel = 360f;           // deg/s^2
[Range(0.0f, 1.0f)] public float probMin = 0.01f;
[Range(0.0f, 1.0f)] public float probMax = 0.10f;
[Tooltip("Time delay in seconds between the start of rotation for consecutive bars.")]
[Min(0f)] public float perBarDelaySeconds = 0.05f;
[Tooltip("Interval in seconds between speed-change decisions.")]
[Min(0.01f)] public float speedDecisionInterval = 0.5f;
[Tooltip("Energy exponent mapping for rotation trigger probability (1 = linear).")]
[Min(0f)] public float probEnergyExponent = 1.0f;
[Tooltip("Energy exponent mapping for initial angular speed (1 = linear).")]
[Min(0f)] public float speedEnergyExponent = 1.0f;

	[Header("Energy (RMS Mapping)")]
	[Tooltip("Scale raw RMS (L/R/combined based on Channel) into [0,1] visual energy via Clamp01(rms * rmsEnergyScale).")]
	[Min(0f)] public float rmsEnergyScale = 10f;

	float _debugAccum;

// Runtime state
readonly List<FloatRing> _history = new List<FloatRing>();      // energy per curve
readonly List<FloatRing> _angleHistory = new List<FloatRing>(); // rotation angle per curve
float _omegaDegPerSec;               // current angular speed (deg/s)
float _targetOmegaDegPerSec;         // target angular speed (deg/s) we accelerate toward
float _angleDeg;                     // integrated angle (deg)
float _timeSinceLastSpeedDecision;   // seconds since last speed-change decision

	protected override void OnEnable()
	{
		base.OnEnable();
		EnsureCurves();
		EnsureHistoryCapacity();
	}

	void Start()
	{
		EnsureCurves();
		EnsureHistoryCapacity();
	}

	void EnsureCurves()
	{
		if (autoCollectCurves || curves == null || curves.Length == 0)
		{
			curves = GetComponentsInChildren<PerpendicularBarsCurve>(includeInactive: false);
		}
	}

	void EnsureHistoryCapacity()
	{
		_history.Clear();
		_angleHistory.Clear();
		if (curves == null) return;
		for (int i = 0; i < curves.Length; i++)
		{
			_history.Add(new FloatRing(256)); // will expand as needed at runtime
			_angleHistory.Add(new FloatRing(256));
		}
	}

	protected override void MusicVisualize(in MusicFrame frame)
	{
		EnsureCurves();
		if (curves == null || curves.Length == 0) return;

		// Sample current visual energy once from raw RMS, then fan it out to displacement + rotation logic.
		float energy = GetRmsEnergy(in frame);

		// 1) Ensure ring capacities + compute max propagation time across curves
		float maxPropagationTime = 0f;
		for (int c = 0; c < curves.Length; c++)
		{
			var curve = curves[c];
			if (curve == null) continue;

			// Ensure ring capacity can cover full delay for the farthest bar
			float spacing = curve.GetBarSpacingMeters();
			int bars = curve.GetBarCount();
			int barCountMinusOne = Mathf.Max(0, bars - 1);
			float totalDist = spacing * barCountMinusOne;
			// Displacement propagation (meters / second).
			float totalOffsetTime = totalDist / Mathf.Max(0.01f, propagationSpeed);
			// Rotation cascade propagation (seconds between bars).
			float totalAngleTime = barCountMinusOne * Mathf.Max(0f, perBarDelaySeconds);
			float totalTime = Mathf.Max(totalOffsetTime, totalAngleTime);
			if (totalTime > maxPropagationTime) maxPropagationTime = totalTime;
			int maxDelayFrames = Mathf.CeilToInt(totalTime / Mathf.Max(1e-6f, frame.frameDuration)) + 4;
			if (_history[c].Capacity < maxDelayFrames + 8)
			{
				_history[c].Resize(Mathf.NextPowerOfTwo(maxDelayFrames + 8));
			}
			if (_angleHistory[c].Capacity < maxDelayFrames + 8)
			{
				_angleHistory[c].Resize(Mathf.NextPowerOfTwo(maxDelayFrames + 8));
			}

			// Keep curves aware of world rotation axis
			curve.allowExternalRotations = true;
			curve.externalRotationAxisWorld = axis;
		}

		// 2) Update rotation state (continuous rotation with energy-driven speed changes)
		UpdateRotationState(in frame);

		if (enableDebugLogs)
		{
			_debugAccum += Mathf.Max(0f, frame.frameDuration);
			if (_debugAccum >= 0.5f)
			{
				_debugAccum = 0f;
				int c0 = curves.Length > 0 && curves[0] != null ? curves[0].GetBarCount() : -1;
				float dbgEnergy = GetRmsEnergy(in frame);
				Debug.Log($"[BarsCurvesMusicVis] energy~{dbgEnergy:F2} omega={_omegaDegPerSec:F1} targetOmega={_targetOmegaDegPerSec:F1} angle={_angleDeg:F1} barsInFirstCurve={c0} axisWorld={axis}");
			}
		}

		// 3) Propagating displacement and rotation per curve
		for (int c = 0; c < curves.Length; c++)
		{
			var curve = curves[c];
			if (curve == null) continue;
			float spacing = curve.GetBarSpacingMeters();
			int count = curve.GetBarCount();
			if (count <= 0) continue;

			_history[c].Push(energy);
			// Push current angle sample for rotation cascade (always rotating, possibly very slowly).
			float currentAngleSample = _angleDeg;
			_angleHistory[c].Push(currentAngleSample);

			// Prepare arrays
			float[] offsets = ArrayPool<float>.Shared.Rent(count);
			float[] angles = ArrayPool<float>.Shared.Rent(count);

			float maxAbsOffset = 0f;
			float maxAbsAngle = 0f;

			for (int i = 0; i < count; i++)
			{
				// --- Displacement along curve (music-driven with propagation) ---
				float distance = spacing * i;
				// Displacement delay based on physical distance and propagation speed.
				float delayTimeOffset = distance / Mathf.Max(0.01f, propagationSpeed);
				int delayFramesOffset = Mathf.RoundToInt(delayTimeOffset / Mathf.Max(1e-6f, frame.frameDuration));
				float e = _history[c].GetAgo(delayFramesOffset);
				float disp = Mathf.Clamp(e * displacementScale, -maxDisplacement, maxDisplacement);
				offsets[i] = disp;
				if (Mathf.Abs(disp) > maxAbsOffset) maxAbsOffset = Mathf.Abs(disp);

				// --- Rotation cascade (deg) with fixed per-bar time delay ---
				float delayTimeAngle = Mathf.Max(0f, perBarDelaySeconds) * i;
				int delayFramesAngle = Mathf.RoundToInt(delayTimeAngle / Mathf.Max(1e-6f, frame.frameDuration));
				float a = _angleHistory[c].GetAgo(delayFramesAngle);
				angles[i] = a;
				if (Mathf.Abs(a) > maxAbsAngle) maxAbsAngle = Mathf.Abs(a);
			}

			curve.SetPerBarOffsets(offsets);
			curve.SetPerBarAngles(angles);
			ArrayPool<float>.Shared.Return(offsets);
			ArrayPool<float>.Shared.Return(angles);

			if (enableDebugLogs && c == 0)
			{
				float firstOffset = count > 0 ? offsets[0] : 0f;
				float firstAngle = count > 0 ? angles[0] : 0f;
				Debug.Log(
					$"[BarsCurvesMusicVis] Applied offsets/angles to curve[0]: " +
					$"count={count}, spacing={spacing:F3}, energy={energy:F3}, " +
					$"firstOffset={firstOffset:F4}, firstAngle={firstAngle:F2}, " +
					$"maxAbsOffset={maxAbsOffset:F4}, maxAbsAngle={maxAbsAngle:F2}");
			}
		}
	}

	void UpdateRotationState(in MusicFrame frame)
	{
		float dt = Mathf.Max(0f, frame.frameDuration);

		// Sample current energy once for this frame to drive both probability and acceleration.
		float energy = GetRmsEnergy(in frame);

		// 1) Periodically decide whether to change target speed based on energy.
		_timeSinceLastSpeedDecision += dt;
		if (speedDecisionInterval > 0.0001f)
		{
			while (_timeSinceLastSpeedDecision >= speedDecisionInterval)
			{
				_timeSinceLastSpeedDecision -= speedDecisionInterval;

				// Map energy -> trigger probability with adjustable exponent.
				float eProb = probEnergyExponent > 0f
					? Mathf.Pow(energy, probEnergyExponent)
					: (energy > 0f ? 1f : 0f);
				float p = Mathf.Lerp(probMin, probMax, eProb);

				if (UnityEngine.Random.value < p)
				{
					// Map energy -> target angular speed magnitude with adjustable exponent.
					float eSpeed = speedEnergyExponent > 0f
						? Mathf.Pow(energy, speedEnergyExponent)
						: (energy > 0f ? 1f : 0f);
					float targetMag = Mathf.Lerp(initAngularSpeedMin, initAngularSpeedMax, eSpeed);

					// Base direction on current direction; flip with 50% probability.
					float direction = Mathf.Sign(_omegaDegPerSec);
					if (direction == 0f)
					{
						direction = UnityEngine.Random.value < 0.5f ? -1f : 1f;
					}
					if (UnityEngine.Random.value < 0.5f)
					{
						direction = -direction;
					}

					_targetOmegaDegPerSec = direction * targetMag;

					if (enableDebugLogs)
					{
						Debug.Log($"[BarsCurvesMusicVis] New target omega={_targetOmegaDegPerSec:F1}deg/s (energy={energy:F2})");
					}
				}
			}
		}

		// 2) Move current omega toward target with constant-acceleration magnitude (decel).
		float diff = _targetOmegaDegPerSec - _omegaDegPerSec;
		if (Mathf.Abs(diff) > 1e-3f && decel > 0f)
		{
			// Acceleration magnitude scales with energy (higher energy -> faster change),
			// but keeps a small baseline so motion can still evolve at low energy.
			float accelMag = decel * Mathf.Lerp(0.1f, 1f, Mathf.Clamp01(energy));
			float maxDelta = accelMag * dt;
			float delta = Mathf.Clamp(diff, -maxDelta, maxDelta);
			_omegaDegPerSec += delta;
		}
		else
		{
			_omegaDegPerSec = _targetOmegaDegPerSec;
		}

		// 3) Integrate angle (always rotating, possibly very slowly).
		_angleDeg += _omegaDegPerSec * dt;

		// Keep angle within a reasonable range to avoid overflow.
		if (_angleDeg > 360f || _angleDeg < -360f)
		{
			_angleDeg = Mathf.Repeat(_angleDeg, 360f);
		}
	}

	// --- Helpers ---
	float GetRmsEnergy(in MusicFrame frame)
	{
		// Use globally-normalized energy from MusicDriver (frame.norm*), respecting selected channel.
		return GetEnergy(in frame);
	}

	class FloatRing
	{
		float[] _buf;
		int _head;
		int _count;
		public int Capacity => _buf.Length;

		public FloatRing(int capacity)
		{
			_buf = new float[Mathf.Max(8, capacity)];
			_head = 0;
			_count = 0;
		}

		public void Resize(int newCapacity)
		{
			newCapacity = Mathf.Max(8, newCapacity);
			if (newCapacity == _buf.Length) return;
			var newBuf = new float[newCapacity];
			// Copy most recent data fitting into new buffer
			int n = Mathf.Min(_count, newCapacity);
			for (int i = 0; i < n; i++)
			{
				newBuf[i] = GetAgo(i);
			}
			_buf = newBuf;
			_head = n % _buf.Length;
			_count = n;
		}

		public void Push(float v)
		{
			_buf[_head] = v;
			_head = (_head + 1) % _buf.Length;
			if (_count < _buf.Length) _count++;
		}

		public float GetAgo(int framesAgo)
		{
			if (_count == 0) return 0f;
			if (framesAgo <= 0) return _buf[(_head - 1 + _buf.Length) % _buf.Length];
			if (framesAgo >= _count) return _buf[(_head - _count + _buf.Length) % _buf.Length];
			int idx = (_head - 1 - framesAgo) % _buf.Length;
			if (idx < 0) idx += _buf.Length;
			return _buf[idx];
		}
	}
}

/// <summary>
/// Very small ArrayPool for float[] to avoid allocations each frame. Not thread-safe (main thread only).
/// </summary>
static class ArrayPool<T>
{
	public static class Shared
	{
		static readonly Stack<T[]> _pool = new Stack<T[]>();

		public static T[] Rent(int length)
		{
			if (_pool.Count > 0)
			{
				var arr = _pool.Pop();
				if (arr != null && arr.Length >= length) return arr;
			}
			return new T[length];
		}

		public static void Return(T[] arr)
		{
			if (arr == null) return;
			_pool.Push(arr);
		}
	}
}



