using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class WireframeMeshGenerator
{
    [MenuItem("Assets/Create/Wireframe/Cube (Barycentric)")]
    public static void CreateWireframeCubeAsset()
    {
        // 生成 Unity 内置 Cube Mesh
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh srcMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temp);

        // 复制并转为独立三角形
        Mesh mesh = CopyMeshAsTriangles(srcMesh);

        // 写入 barycentric 到 UV1
        Vector3[] bary = new Vector3[mesh.vertexCount];
        for (int i = 0; i < bary.Length; i += 3)
        {
            bary[i]     = new Vector3(1, 0, 0);
            bary[i + 1] = new Vector3(0, 1, 0);
            bary[i + 2] = new Vector3(0, 0, 1);
        }

        mesh.SetUVs(1, new List<Vector3>(bary));
        mesh.RecalculateNormals();

        // 保存为 asset
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Wireframe Cube Mesh",
            "Cube_Barycentric",
            "asset",
            "Choose location to save the wireframe cube mesh."
        );

        if (!string.IsNullOrEmpty(path))
        {
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Success", "Wireframe Cube Mesh created!", "OK");
        }
    }

    static Mesh CopyMeshAsTriangles(Mesh src)
    {
        Mesh mesh = new Mesh();
        int[] tris = src.triangles;

        // 每个三角形都复制成独立的3个顶点
        Vector3[] newVerts = new Vector3[tris.Length];
        for (int i = 0; i < tris.Length; i++)
            newVerts[i] = src.vertices[tris[i]];

        mesh.vertices = newVerts;

        int[] newTris = new int[tris.Length];
        for (int i = 0; i < newTris.Length; i++)
            newTris[i] = i;

        mesh.triangles = newTris;

        return mesh;
    }
}