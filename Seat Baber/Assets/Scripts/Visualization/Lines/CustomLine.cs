using System;
using UnityEngine;

[ExecuteAlways]
public class CustomLine : MonoBehaviour
{
	[Header("Mode")]
	public bool isInfinite = false;
	[Min(0.01f)] public float finiteLength = 1.0f;
	[Min(10f), Tooltip("Practical length to approximate infinity")]
	public float infiniteLength = 10000f;

	[Header("Appearance")]
	[Min(0.0001f)] public float width = 0.05f;
	public Material lineMaterial;

	[Header("Space")]
	[Tooltip("When false, you can position/rotate/scale the GameObject to control the line.")]
	public bool useWorldSpace = false;

	LineRenderer _lr;
	Light _areaLight;

	void OnEnable()
	{
		EnsureLineRenderer();
		EnsureAreaLight();
		ApplyAll();
	}

	void OnValidate()
	{
		EnsureLineRenderer();
		EnsureAreaLight();
		ApplyAll();
	}

	void Update()
	{
		// Keep previewing in Edit Mode and react to transform changes
		ApplyAppearance();
		ApplyPositions();
	}

	void EnsureLineRenderer()
	{
		if (_lr == null) _lr = GetComponent<LineRenderer>();
		if (_lr == null) _lr = gameObject.AddComponent<LineRenderer>();

		_lr.positionCount = 2;
		_lr.alignment = LineAlignment.View; // face camera using LineRenderer's built-in
		_lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		_lr.receiveShadows = false;
		_lr.useWorldSpace = useWorldSpace;
		if (lineMaterial != null && _lr.sharedMaterial != lineMaterial)
		{
			_lr.sharedMaterial = lineMaterial;
		}
	}

	[Header("Area Light")]
	public bool enableAreaLight = true;
	[Min(0f)] public float areaLightIntensity = 2.0f;

	void EnsureAreaLight()
	{
		// Find existing
		if (_areaLight == null)
		{
			var existing = transform.Find("AreaLight");
			if (existing != null) _areaLight = existing.GetComponent<Light>();
		}
		// Create if missing
		if (_areaLight == null)
		{
			var go = new GameObject("AreaLight");
			go.transform.SetParent(transform, false);
			_areaLight = go.AddComponent<Light>();
		}

		// Configure as rectangle/area (falls back if pipeline doesn't support realtime)
#if UNITY_2020_2_OR_NEWER
		_areaLight.type = LightType.Tube;
#else
		_areaLight.type = LightType.Area;
#endif
		_areaLight.shadows = LightShadows.None;
		_areaLight.intensity = areaLightIntensity;
		_areaLight.color = GetTargetColor();
		_areaLight.enabled = enableAreaLight;
	}

	void ApplyAll()
	{
		if (_lr == null) return;

		_lr.useWorldSpace = useWorldSpace;
		_lr.startWidth = width;
		_lr.endWidth = width;
		ApplyAppearance();
		ApplyPositions();
	}

	Color GetTargetColor()
	{
		if (lineMaterial != null)
		{
			// Check for custom shader property used in this project
			if (lineMaterial.HasProperty("_lineColor")) return lineMaterial.GetColor("_lineColor");

			if (lineMaterial.HasProperty("_BaseColor")) return lineMaterial.GetColor("_BaseColor");
			if (lineMaterial.HasProperty("_Color")) return lineMaterial.GetColor("_Color");

			// Try Emission if the keyword is enabled or global illumination says so
			if (lineMaterial.IsKeywordEnabled("_EMISSION") && lineMaterial.HasProperty("_EmissionColor"))
			{
				Color emission = lineMaterial.GetColor("_EmissionColor");
				if (emission.maxColorComponent > 0.001f) return emission;
			}
		}
		return Color.cyan;
	}

	void ApplyAppearance()
	{
		if (_lr == null) return;
		Color c = GetTargetColor();
		_lr.startColor = c;
		_lr.endColor = c;
		if (lineMaterial != null && _lr.sharedMaterial != lineMaterial) _lr.sharedMaterial = lineMaterial;
	}

	void ApplyPositions()
	{
		if (_lr == null) return;

		float targetLength = isInfinite ? infiniteLength : finiteLength;
		targetLength = Mathf.Max(0.0001f, targetLength);

		// Define endpoints along local X axis (centered), so transform controls orientation/position/scale
		Vector3 a = new Vector3(-0.5f * targetLength, 0f, 0f);
		Vector3 b = new Vector3(+0.5f * targetLength, 0f, 0f);

		Vector3 aWorld, bWorld;
		if (_lr.useWorldSpace)
		{
			// Convert local endpoints to world if using world space
			aWorld = transform.TransformPoint(a);
			bWorld = transform.TransformPoint(b);
			_lr.SetPosition(0, aWorld);
			_lr.SetPosition(1, bWorld);
		}
		else
		{
			aWorld = transform.TransformPoint(a);
			bWorld = transform.TransformPoint(b);
			_lr.SetPosition(0, a);
			_lr.SetPosition(1, b);
		}

		UpdateAreaLight(aWorld, bWorld);
	}

	void UpdateAreaLight(Vector3 aWorld, Vector3 bWorld)
	{
		if (_areaLight == null) return;

		// Visibility and appearance
		_areaLight.enabled = enableAreaLight;
		if (!enableAreaLight) return;
		_areaLight.intensity = areaLightIntensity;
		_areaLight.color = GetTargetColor();

		// Size to match world-space line length and approximate thickness from width
		float worldLength = Mathf.Max(0.0001f, Vector3.Distance(aWorld, bWorld));
		float worldThickness = Mathf.Max(0.0001f, width * transform.lossyScale.y);

		// Try to configure HDRP Tube light if available; otherwise fallback to rectangle size
		if (!TryConfigureHdrpTube(_areaLight, worldLength, worldThickness))
		{
			// areaSize is in meters; keep child at identity scale
			_areaLight.areaSize = new Vector2(worldLength, worldThickness);
		}

		// Place at midpoint and orient so local X aligns with the line direction
		Vector3 mid = 0.5f * (aWorld + bWorld);
		Vector3 xAxis = (bWorld - aWorld);
		if (xAxis.sqrMagnitude < 1e-8f) xAxis = transform.right;
		xAxis.Normalize();
		Vector3 zAxis = Vector3.Cross(xAxis, Vector3.up);
		if (zAxis.sqrMagnitude < 1e-6f) zAxis = Vector3.Cross(xAxis, Vector3.forward);
		zAxis.Normalize();
		Vector3 yAxis = Vector3.Cross(zAxis, xAxis);

		var rot = Quaternion.LookRotation(zAxis, yAxis);
		_areaLight.transform.position = mid;
		_areaLight.transform.rotation = rot;
	}

	// Configure Tube light via HDRP (reflection to avoid hard dependency). Returns true if applied.
	bool TryConfigureHdrpTube(Light unityLight, float length, float thickness)
	{
		try
		{
			// Locate HDRP types
			var hdAsmName = "Unity.RenderPipelines.HighDefinition.Runtime";
			var hdType = Type.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalLightData, " + hdAsmName);
			if (hdType == null) return false;

			// Ensure component
			var comp = unityLight.GetComponent(hdType);
			if (comp == null) comp = unityLight.gameObject.AddComponent(hdType);
			if (comp == null) return false;

			// Prefer AreaLightShape if present (Tube)
			var areaShapeEnum = Type.GetType("UnityEngine.Rendering.HighDefinition.AreaLightShape, " + hdAsmName);
			var lightTypeExtentEnum = Type.GetType("UnityEngine.Rendering.HighDefinition.LightTypeExtent, " + hdAsmName);

			// Helper local functions
			bool SetEnumProperty(object target, string propName, Type enumType, string enumValue)
			{
				if (enumType == null) return false;
				var prop = hdType.GetProperty(propName);
				if (prop == null || !prop.CanWrite) return false;
				var val = Enum.Parse(enumType, enumValue);
				prop.SetValue(target, val, null);
				return true;
			}

			bool SetFloatProperty(object target, string propName, float value)
			{
				var prop = hdType.GetProperty(propName);
				if (prop == null || !prop.CanWrite) return false;
				prop.SetValue(target, value, null);
				return true;
			}

			// Set shape to Tube if available, otherwise ensure it's some area type
			bool shapeSet = SetEnumProperty(comp, "areaLightShape", areaShapeEnum, "Tube");
			if (!shapeSet && lightTypeExtentEnum != null)
			{
				// Older HDRP API
				try { SetEnumProperty(comp, "lightTypeExtent", lightTypeExtentEnum, "Tube"); } catch {}
			}

			// Set dimensions
			// Common HDRP properties: shapeWidth = length, shapeHeight = thickness
			bool widthSet = SetFloatProperty(comp, "shapeWidth", length);
			bool heightSet = SetFloatProperty(comp, "shapeHeight", thickness);

			// Some HDRP versions use sizeX/sizeY
			if (!widthSet) SetFloatProperty(comp, "sizeX", length);
			if (!heightSet) SetFloatProperty(comp, "sizeY", thickness);

			// Ensure Unity Light set to Rectangle/Area mode for HDRP area lights
#if UNITY_2020_2_OR_NEWER
			unityLight.type = LightType.Tube;
#else
			unityLight.type = LightType.Area;
#endif
			return true;
		}
		catch
		{
			return false;
		}
	}
}


