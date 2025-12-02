#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class VisualizationMenu
{
	[MenuItem("GameObject/Visualization/Custom Line", false, 10)]
	public static void CreateCustomLine()
	{
		var go = new GameObject("Custom Line");
		Undo.RegisterCreatedObjectUndo(go, "Create Custom Line");
		var comp = go.AddComponent<CustomLine>();
		comp.isInfinite = false;
		comp.finiteLength = 1.0f;
		comp.width = 0.05f;

		Selection.activeObject = go;
		SceneView.lastActiveSceneView?.FrameSelected();
	}

	[MenuItem("GameObject/Visualization/Perpendicular Bars Curve", false, 11)]
	public static void CreatePerpendicularBarsCurve()
	{
		var go = new GameObject("Perpendicular Bars Curve");
		Undo.RegisterCreatedObjectUndo(go, "Create Perpendicular Bars Curve");
		var comp = go.AddComponent<PerpendicularBarsCurve>();
		comp.spacing = 0.5f;
		comp.barLength = 0.5f;
		comp.barThickness = 0.02f;
		comp.hideCurveRenderer = true;
		comp.perpendicularReferenceUp = Vector3.up;

		// Ensure the curve exists with defaults
		if (comp.curve == null)
		{
			comp.createDefaultCurveIfMissing = true;
			comp.OnValidate();
		}

		Selection.activeObject = go;
		SceneView.lastActiveSceneView?.FrameSelected();
	}
}
#endif


