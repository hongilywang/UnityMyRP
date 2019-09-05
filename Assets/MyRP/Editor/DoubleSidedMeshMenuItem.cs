using UnityEditor;
using UnityEngine;

public class DoubleSidedMeshMenuItem
{
    [MenuItem("Assets/Create/Double-Sided Mesh")]
    static void MakeDoubleSidedMeshAsset()
    {
        var sourceMesh = Selection.activeObject as Mesh;
        if (sourceMesh == null)
        {
            var selectGO = Selection.activeGameObject;
            if (selectGO != null)
            {
                var meshFilter = selectGO.GetComponent<MeshFilter>();
                sourceMesh = meshFilter.sharedMesh;
            }

            if (sourceMesh == null)
            {
                Debug.Log("You msut have a mesh asset selected.");
                return;
            }
        }

        Mesh insideMesh = Object.Instantiate(sourceMesh);
        int[] traingles = insideMesh.triangles;
        System.Array.Reverse(traingles);
        insideMesh.triangles = traingles;

        Vector3[] normals = insideMesh.normals;
        for (int i = 0; i < normals.Length; ++i)
            normals[i] = -normals[i];

        insideMesh.normals = normals;

        var combinedMesh = new Mesh();
        combinedMesh.CombineMeshes(
            new CombineInstance[]
            {
                new CombineInstance{mesh = insideMesh},
                new CombineInstance{mesh = sourceMesh}
            },
            true, false, false
            );
        Object.DestroyImmediate(insideMesh);
        AssetDatabase.CreateAsset(combinedMesh, System.IO.Path.Combine("Assets/Meshes", sourceMesh.name + " Double-Sided.asset"));
    }
}
