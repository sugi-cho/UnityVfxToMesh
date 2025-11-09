#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;

namespace VfxToMesh.Editor
{
    public static class PipelineBootstrap
    {
        private const string ScenePath = "Assets/VfxToMesh/Scenes/VfxToMesh.unity";
        private const string VfxAssetPath = "Assets/VfxToMesh/VFX/ParticleField.vfx";
        private const string ComputePath = "Assets/VfxToMesh/Shaders/VfxToMesh.compute";

        [MenuItem("Tools/Vfx To Mesh/Rebuild Playground", priority = 0)]
        public static void RebuildPlayground()
        {
            if (!EditorUtility.DisplayDialog("Rebuild Playground",
                    "This will overwrite the scene at\n" + ScenePath + "\nContinue?", "OK", "Cancel"))
            {
                return;
            }

            BuildScene();
        }

        // Used for CI / batchmode: Unity.exe -batchmode -quit -projectPath <path> -executeMethod VfxToMesh.Editor.PipelineBootstrap.BuildSceneHeadless
        public static void BuildSceneHeadless()
        {
            BuildScene();
        }

        private static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 2.5f, -7f);
            camera.transform.LookAt(Vector3.zero);
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraGo.tag = "MainCamera";
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<UniversalAdditionalCameraData>();

            var lightGo = new GameObject("Directional Light");
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.9f);
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            lightGo.AddComponent<UniversalAdditionalLightData>();

            var rig = new GameObject("VfxToMeshRig");
            SceneManager.MoveGameObjectToScene(rig, scene);
            rig.transform.position = Vector3.zero;

            var vfx = rig.AddComponent<VisualEffect>();
            vfx.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(VfxAssetPath);

            var pipeline = rig.AddComponent<VfxToMeshPipeline>();
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            var meshFilter = rig.AddComponent<MeshFilter>();
            var meshRenderer = rig.AddComponent<MeshRenderer>();

            var so = new SerializedObject(pipeline);
            so.FindProperty("pipelineCompute").objectReferenceValue = compute;
            so.FindProperty("targetVfx").objectReferenceValue = vfx;
            var renderersProp = so.FindProperty("targetRenderers");
            renderersProp.ClearArray();
            renderersProp.InsertArrayElementAtIndex(0);
            var element = renderersProp.GetArrayElementAtIndex(0);
            element.FindPropertyRelative("renderer").objectReferenceValue = meshRenderer;
            element.FindPropertyRelative("meshFilter").objectReferenceValue = meshFilter;
            so.ApplyModifiedProperties();

            EditorSceneManager.SaveScene(scene, ScenePath, true);
            AssetDatabase.Refresh();
            Debug.Log("VfxToMesh scene rebuilt.");
        }
    }
}
#endif
