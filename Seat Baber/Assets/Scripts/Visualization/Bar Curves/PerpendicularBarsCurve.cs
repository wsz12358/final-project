using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places many short perpendicular line segments along an editable curve (LineRenderer).
/// Works in Edit Mode for instant preview.
/// </summary>
[ExecuteAlways]
public class PerpendicularBarsCurve : MonoBehaviour
{
	const string BarNamePrefix = "Bar_";

	[Header("Curve (Editable)")]
	public LineRenderer curve;
	[Tooltip("If true, the curve will be created with a simple default when missing.")]
	public bool createDefaultCurveIfMissing = true;
	[Tooltip("If true, the editable curve LineRenderer is hidden (used only as a path).")]
	public bool hideCurveRenderer = true;

	[Header("Space")]
	[Tooltip("Bars are driven in local space so parent Transform position/rotation/scale affects the whole object.")]
	public bool barsUseLocalSpace = true;

	[Header("Bars")]
	[Min(0.05f)] public float spacing = 0.5f;
	[Min(0.01f)] public float barLength = 0.5f;
	[Min(0.001f)] public float barThickness = 0.02f;
	public Material barMaterial;
	public Color barColor = Color.white;
	public bool colorByGradient = false;
	public Gradient colorOverLength = new Gradient
	{
		colorKeys = new[]
		{
			new GradientColorKey(new Color(1f, 1f, 1f), 0f),
			new GradientColorKey(new Color(0.5f, 0.8f, 1f), 1f),
		},
		alphaKeys = new[]
		{
			new GradientAlphaKey(1f, 0f),
			new GradientAlphaKey(1f, 1f),
		}
	};

	[Header("Orientation")]
	[Tooltip("Perpendicular is computed as cross(Tangent, ReferenceUp). Choose the plane normal.")]
	public Vector3 perpendicularReferenceUp = Vector3.up;

	[Header("External Offsets")]
	[Tooltip("When enabled, external scripts can push per-bar offsets along bar direction (meters).")]
	public bool allowExternalOffsets = true;

	[Header("External Rotations")]
	[Tooltip("When enabled, external scripts can rotate each bar around a specified WORLD axis (degrees).")]
	public bool allowExternalRotations = true;
	[Tooltip("World-space axis to rotate bars around when external rotations are provided.")]
	public Vector3 externalRotationAxisWorld = Vector3.up;

	[Header("Debug")]
	public bool enableDebugLogs = false;
	float _debugAccum;

	// Internal state
	readonly List<LineRenderer> _bars = new List<LineRenderer>();
	Vector3[] _curvePositions = Array.Empty<Vector3>();
	float[] _curveSegmentLengths = Array.Empty<float>();
	float _curveTotalLength;
	int _targetBarCount;
	float[] _externalOffsets = Array.Empty<float>();
	float[] _externalAngles = Array.Empty<float>();

	struct BarEndpoints
	{
		public Vector3 p0;
		public Vector3 p1;
	}
	List<BarEndpoints> _targetEndpoints = new List<BarEndpoints>();

	// Defer structural rebuilds to Update to avoid OnValidate restrictions
	bool _pendingRebuild = false;

	void OnEnable()
	{
		_pendingRebuild = true;
	}

	public void OnValidate()
	{
		_pendingRebuild = true;
	}

	void Update()
	{
		if (_pendingRebuild)
		{
			EnsureCurve();
			ApplyCurveVisibility();
			ReconcileChildrenIfNeeded();
			_pendingRebuild = false;
		}

		// Always keep bars aligned to curve changes (Edit Mode or Play Mode)
		RebuildCacheFromCurve();
		RebuildBarsIfNeeded();
		PlaceBarsImmediate();

		if (enableDebugLogs)
		{
			_debugAccum += Application.isPlaying ? Time.deltaTime : Time.unscaledDeltaTime;
			if (_debugAccum >= 1.0f)
			{
				_debugAccum = 0f;
				Debug.Log($"[PerpendicularBarsCurve:{name}] bars={_bars.Count} allowOffsets={allowExternalOffsets} offLen={_externalOffsets?.Length ?? 0} allowAngles={allowExternalRotations} angLen={_externalAngles?.Length ?? 0} axisWorld={externalRotationAxisWorld}");
			}
		}
	}

	// Remove any legacy/orphaned Bar_* children that are not tracked
	void ReconcileChildrenIfNeeded()
	{
		// Only perform destructive cleanup when our runtime list is empty but children exist.
		// This covers script reloads, Reset() calls, or when users duplicated objects.
		if (_bars.Count > 0) return;

		bool hasBarChildren = false;
		for (int i = 0; i < transform.childCount; i++)
		{
			var child = transform.GetChild(i);
			if (child != null && child.name.StartsWith(BarNamePrefix, StringComparison.Ordinal))
			{
				hasBarChildren = true;
				break;
			}
		}

		if (!hasBarChildren) return;

		// Clear any previous tracking lists
		_bars.Clear();
		_targetEndpoints.Clear();

		// Delete orphaned Bar_* children so we can rebuild cleanly
		for (int i = transform.childCount - 1; i >= 0; i--)
		{
			var child = transform.GetChild(i);
			if (child != null && child.name.StartsWith(BarNamePrefix, StringComparison.Ordinal))
			{
				SafeDestroy(child.gameObject);
			}
		}
	}

	void SafeDestroy(UnityEngine.Object obj)
	{
#if UNITY_EDITOR
		if (!Application.isPlaying)
		{
			DestroyImmediate(obj);
			return;
		}
#endif
		Destroy(obj);
	}

	void EnsureCurve()
	{
		if (curve != null) return;
		if (!createDefaultCurveIfMissing) return;

		curve = GetComponent<LineRenderer>();
		if (curve == null) curve = gameObject.AddComponent<LineRenderer>();

		curve.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		curve.receiveShadows = false;
		curve.alignment = LineAlignment.View;
		curve.textureMode = LineTextureMode.Stretch;
		curve.widthMultiplier = 0.01f;
		curve.numCornerVertices = 4;
		curve.numCapVertices = 4;
		// Keep the editable curve in LOCAL space so moving/rotating/scaling the parent moves the whole object.
		curve.useWorldSpace = false;
		if (curve.positionCount < 2)
		{
			curve.positionCount = 4;
			// Default points in LOCAL coordinates
			curve.SetPosition(0, new Vector3(0f, 0f, 0f));
			curve.SetPosition(1, new Vector3(1f, 0f, 0f));
			curve.SetPosition(2, new Vector3(2f, 0.5f, 0f));
			curve.SetPosition(3, new Vector3(3f, 0f, 0f));
		}
	}

	void ApplyCurveVisibility()
	{
		if (curve == null) return;
		// Prevent the curve LineRenderer from rendering if requested
		curve.forceRenderingOff = hideCurveRenderer;
	}

	void RebuildCacheFromCurve()
	{
		if (curve == null || curve.positionCount < 2)
		{
			_curvePositions = Array.Empty<Vector3>();
			_curveSegmentLengths = Array.Empty<float>();
			_curveTotalLength = 0f;
			_targetBarCount = 0;
			return;
		}

		if (_curvePositions.Length != curve.positionCount)
		{
			_curvePositions = new Vector3[curve.positionCount];
		}
		// Get curve positions, convert to LOCAL space of this component so Transform affects the whole object
		var tmp = new Vector3[curve.positionCount];
		curve.GetPositions(tmp);
		bool lrWorld = curve.useWorldSpace;
		for (int i = 0; i < tmp.Length; i++)
		{
			Vector3 worldPos = lrWorld ? tmp[i] : curve.transform.TransformPoint(tmp[i]);
			_curvePositions[i] = transform.InverseTransformPoint(worldPos);
		}

		if (_curveSegmentLengths.Length != curve.positionCount - 1)
		{
			_curveSegmentLengths = new float[curve.positionCount - 1];
		}

		_curveTotalLength = 0f;
		for (int i = 0; i < _curveSegmentLengths.Length; i++)
		{
			float segLen = Vector3.Distance(_curvePositions[i], _curvePositions[i + 1]);
			_curveSegmentLengths[i] = segLen;
			_curveTotalLength += segLen;
		}

		_targetBarCount = _curveTotalLength > 0.0001f
			? Mathf.Max(1, Mathf.FloorToInt(_curveTotalLength / Mathf.Max(0.01f, spacing)) + 1)
			: 0;
	}

	void RebuildBarsIfNeeded()
	{
		// Grow
		while (_bars.Count < _targetBarCount)
		{
			var go = new GameObject($"Bar_{_bars.Count:D3}");
			// Parent with identity local transform so bar points (in local space) align with this component's local space
			go.transform.SetParent(transform, worldPositionStays: false);
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;
			var lr = go.AddComponent<LineRenderer>();
			lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			lr.receiveShadows = false;
			lr.alignment = LineAlignment.View;
			lr.textureMode = LineTextureMode.Stretch;
			lr.numCornerVertices = 2;
			lr.numCapVertices = 2;
			lr.useWorldSpace = !barsUseLocalSpace;
			lr.positionCount = 2;
			lr.widthMultiplier = barThickness;
			if (barMaterial != null) lr.sharedMaterial = barMaterial;
			_bars.Add(lr);
			_targetEndpoints.Add(new BarEndpoints());
		}

		// Shrink
		for (int i = _bars.Count - 1; i >= _targetBarCount; i--)
		{
			if (_bars[i] != null)
			{
				SafeDestroy(_bars[i].gameObject);
			}
			_bars.RemoveAt(i);
			_targetEndpoints.RemoveAt(i);
		}
	}

	void PlaceBarsImmediate()
	{
		if (_bars.Count == 0) return;
		EnsureTargetEndpointsCurrent();
		for (int i = 0; i < _bars.Count; i++)
		{
			// Keep LR space mode in sync with the toggle even at runtime
			if (_bars[i].useWorldSpace != !barsUseLocalSpace)
			{
				_bars[i].useWorldSpace = !barsUseLocalSpace;
			}
			ApplyBarAppearance(i, normalized: GetNormalizedAtIndex(i));
			// Apply external per-bar offsets along the bar direction if present
			BarEndpoints ep = _targetEndpoints[i];
			if (allowExternalOffsets && _externalOffsets != null && i < _externalOffsets.Length)
			{
				float off = _externalOffsets[i];
				if (Mathf.Abs(off) > 1e-6f)
				{
					Vector3 dir = (ep.p1 - ep.p0);
					if (dir.sqrMagnitude > 1e-12f) dir = dir.normalized;
					ep.p0 += dir * off;
					ep.p1 += dir * off;
				}
			}
			if (barsUseLocalSpace)
			{
				_bars[i].SetPosition(0, ep.p0);
				_bars[i].SetPosition(1, ep.p1);
			}
			else
			{
				_bars[i].SetPosition(0, transform.TransformPoint(ep.p0));
				_bars[i].SetPosition(1, transform.TransformPoint(ep.p1));
			}
		}
	}

	void EnsureTargetEndpointsCurrent()
	{
		if (_bars.Count == 0 || _curveTotalLength <= 0.0001f) return;

		// Convert world axis and world pivot (0,0,0) to our local space for math performed in local coordinates.
		Vector3 axisLocal = externalRotationAxisWorld.sqrMagnitude < 1e-8f
			? Vector3.up
			: transform.InverseTransformDirection(externalRotationAxisWorld).normalized;
		// World-space pivot is fixed at (0,0,0).
		Vector3 pivotLocal = transform.InverseTransformPoint(Vector3.zero);

		for (int i = 0; i < _bars.Count; i++)
		{
			float dist = Mathf.Min(i * Mathf.Max(0.01f, spacing), _curveTotalLength);
			float segT;
			int segIdx = FindSegmentAtDistance(dist, out segT);
			Vector3 pos = InterpSegment(segIdx, segT);
			Vector3 tangent = GetTangent(segIdx, segT);

			// Robust perpendicular selection: if reference up is parallel to tangent, choose an alternative axis
			Vector3 refUp = perpendicularReferenceUp.sqrMagnitude < 1e-6f ? Vector3.up : perpendicularReferenceUp.normalized;
			Vector3 perp = Vector3.Cross(tangent, refUp);
			if (perp.sqrMagnitude < 1e-6f)
			{
				// Pick an axis not parallel to tangent
				Vector3 alt =
					Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) < 0.9f ? Vector3.up :
					Mathf.Abs(Vector3.Dot(tangent, Vector3.right)) < 0.9f ? Vector3.right : Vector3.forward;
				perp = Vector3.Cross(tangent, alt);
			}
			perp = perp.normalized;

			Vector3 half = perp * (barLength * 0.5f);
			Vector3 p0 = pos - half;
			Vector3 p1 = pos + half;

			// Optional per-bar rotation around WORLD axis (implemented in local space via converted axis and pivot).
			if (allowExternalRotations && _externalAngles != null && i < _externalAngles.Length)
			{
				float ang = _externalAngles[i];
				if (Mathf.Abs(ang) > 1e-6f && axisLocal.sqrMagnitude > 1e-6f)
				{
					Quaternion q = Quaternion.AngleAxis(ang, axisLocal);
					p0 = pivotLocal + q * (p0 - pivotLocal);
					p1 = pivotLocal + q * (p1 - pivotLocal);
				}
			}

			_targetEndpoints[i] = new BarEndpoints
			{
				p0 = p0,
				p1 = p1
			};
		}
	}

	void ApplyBarAppearance(int index, float normalized)
	{
		var lr = _bars[index];
		lr.widthMultiplier = barThickness;
		if (barMaterial != null && lr.sharedMaterial != barMaterial)
		{
			lr.sharedMaterial = barMaterial;
		}

		if (colorByGradient && colorOverLength != null)
		{
			var c = colorOverLength.Evaluate(Mathf.Clamp01(normalized));
			lr.startColor = c;
			lr.endColor = c;
		}
		else
		{
			lr.startColor = barColor;
			lr.endColor = barColor;
		}
	}

	float GetNormalizedAtIndex(int index)
	{
		if (_curveTotalLength <= 0.0001f || _bars.Count <= 1) return 0f;
		float dist = Mathf.Min(index * Mathf.Max(0.01f, spacing), _curveTotalLength);
		return Mathf.Clamp01(dist / _curveTotalLength);
	}

	int FindSegmentAtDistance(float distance, out float t)
	{
		float d = distance;
		for (int i = 0; i < _curveSegmentLengths.Length; i++)
		{
			float seg = _curveSegmentLengths[i];
			if (d <= seg || i == _curveSegmentLengths.Length - 1)
			{
				t = seg > 0.0001f ? d / seg : 0f;
				return i;
			}
			d -= seg;
		}
		t = 0f;
		return Mathf.Max(0, _curveSegmentLengths.Length - 1);
	}

	Vector3 InterpSegment(int segIndex, float t)
	{
		segIndex = Mathf.Clamp(segIndex, 0, _curvePositions.Length - 2);
		Vector3 a = _curvePositions[segIndex];
		Vector3 b = _curvePositions[segIndex + 1];
		return Vector3.Lerp(a, b, Mathf.Clamp01(t));
	}

	Vector3 GetTangent(int segIndex, float t)
	{
		segIndex = Mathf.Clamp(segIndex, 0, _curvePositions.Length - 2);
		Vector3 a = _curvePositions[segIndex];
		Vector3 b = _curvePositions[segIndex + 1];
		var tan = (b - a);
		if (tan.sqrMagnitude < 1e-6f)
		{
			// Degenerate segment, try neighbors
			if (segIndex > 0) tan += (_curvePositions[segIndex] - _curvePositions[segIndex - 1]);
			if (segIndex + 2 < _curvePositions.Length) tan += (_curvePositions[segIndex + 2] - _curvePositions[segIndex + 1]);
		}
		return tan.sqrMagnitude > 1e-6f ? tan.normalized : Vector3.right;
	}

	Vector3 GetCurveStartWorld()
	{
		if (_curvePositions.Length == 0) return transform.position;
		return _curvePositions[0];
	}

	// --- External control API ---

	/// <summary>
	/// Set per-bar offsets along each bar's direction (meters). Length can be less than bar count; missing entries are treated as zero.
	/// </summary>
	public void SetPerBarOffsets(float[] offsets)
	{
			if (!allowExternalOffsets)
			{
				if (enableDebugLogs)
				{
					Debug.Log($"[PerpendicularBarsCurve:{name}] Ignored external offsets because allowExternalOffsets=false");
				}
				return;
			}
		if (offsets == null)
		{
			_externalOffsets = Array.Empty<float>();
			if (enableDebugLogs) Debug.Log($"[PerpendicularBarsCurve:{name}] Cleared external offsets");
			return;
		}
		// Avoid reallocations when sizes match
		if (_externalOffsets == null || _externalOffsets.Length != offsets.Length)
		{
			_externalOffsets = new float[offsets.Length];
		}
		Array.Copy(offsets, _externalOffsets, offsets.Length);
			if (enableDebugLogs)
			{
				float first = _externalOffsets.Length > 0 ? _externalOffsets[0] : 0f;
				float maxAbs = 0f;
				for (int i = 0; i < _externalOffsets.Length; i++)
				{
					float v = Mathf.Abs(_externalOffsets[i]);
					if (v > maxAbs) maxAbs = v;
				}
				Debug.Log(
					$"[PerpendicularBarsCurve:{name}] Set external offsets length={_externalOffsets.Length}, " +
					$"first={first:F4}, maxAbs={maxAbs:F4}");
			}
	}

	/// <summary>
	/// Set per-bar rotation angles in degrees around <see cref="externalRotationAxisWorld"/> (world axis).
	/// Length can be less than bar count; missing entries are treated as zero.
	/// </summary>
	public void SetPerBarAngles(float[] angles)
	{
			if (!allowExternalRotations)
			{
				if (enableDebugLogs)
				{
					Debug.Log($"[PerpendicularBarsCurve:{name}] Ignored external angles because allowExternalRotations=false");
				}
				return;
			}
		if (angles == null)
		{
			_externalAngles = Array.Empty<float>();
			if (enableDebugLogs) Debug.Log($"[PerpendicularBarsCurve:{name}] Cleared external angles");
			return;
		}
		if (_externalAngles == null || _externalAngles.Length != angles.Length)
		{
			_externalAngles = new float[angles.Length];
		}
		Array.Copy(angles, _externalAngles, angles.Length);
			if (enableDebugLogs)
			{
				float first = _externalAngles.Length > 0 ? _externalAngles[0] : 0f;
				float maxAbs = 0f;
				for (int i = 0; i < _externalAngles.Length; i++)
				{
					float v = Mathf.Abs(_externalAngles[i]);
					if (v > maxAbs) maxAbs = v;
				}
				Debug.Log(
					$"[PerpendicularBarsCurve:{name}] Set external angles length={_externalAngles.Length}, " +
					$"first={first:F2}, maxAbs={maxAbs:F2}, worldAxis={externalRotationAxisWorld}");
			}
	}

	/// <summary>
	/// Spacing between bars in meters (local space).
	/// </summary>
	public float GetBarSpacingMeters() => Mathf.Max(0.01f, spacing);

	/// <summary>
	/// Current runtime bar count.
	/// </summary>
	public int GetBarCount() => _bars.Count;

}


