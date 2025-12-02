#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VfxToMesh
{
    [CustomEditor(typeof(SdfToMesh))]
    public class SdfToMeshEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var sdfToMesh = (SdfToMesh)target;
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!sdfToMesh.CanCaptureMesh))
            {
                if (GUILayout.Button("Save Generated Mesh Asset"))
                {
                    SaveMeshAsset(sdfToMesh);
                }
            }
        }

        private static void SaveMeshAsset(SdfToMesh sdfToMesh)
        {
            if (!sdfToMesh.TryCaptureMesh(out var mesh, out var stats))
            {
                EditorUtility.DisplayDialog("Save Mesh", "No valid mesh data is available to save.", "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Generated Mesh",
                mesh.name,
                "asset",
                "Choose a location to save the generated mesh asset.");

            if (string.IsNullOrEmpty(path))
            {
                Object.DestroyImmediate(mesh);
                return;
            }

            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(mesh);

            Debug.Log($"Saved generated mesh to {path}. Indices: {stats.RawIndexCount}, vertices: {stats.UsedVertexCount}/{stats.RawVertexCount}.", sdfToMesh);
        }
    }
}
#endif
