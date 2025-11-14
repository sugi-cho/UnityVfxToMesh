#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VfxToMesh
{
    [CustomEditor(typeof(DepthToSdfVolume))]
    public class DepthToSdfVolumeEditor : UnityEditor.Editor
    {
        private SerializedProperty depthToSdfCompute;
        private SerializedProperty sourceMode;
        private SerializedProperty depthCamera;
        private SerializedProperty cameraTextureSize;
        private SerializedProperty cameraViewSizeOverride;
        private SerializedProperty manualDepthTexture;
        private SerializedProperty manualViewTransform;
        private SerializedProperty manualViewSize;
        private SerializedProperty manualNearClip;
        private SerializedProperty manualFarClip;
        private SerializedProperty gridResolution;
        private SerializedProperty boundsSize;
        private SerializedProperty isoValue;
        private SerializedProperty sdfFar;
        private SerializedProperty allowUpdateInEditMode;

        private void OnEnable()
        {
            depthToSdfCompute = serializedObject.FindProperty("depthToSdfCompute");
            sourceMode = serializedObject.FindProperty("sourceMode");
            depthCamera = serializedObject.FindProperty("depthCamera");
            cameraTextureSize = serializedObject.FindProperty("cameraTextureSize");
            cameraViewSizeOverride = serializedObject.FindProperty("cameraViewSizeOverride");
            manualDepthTexture = serializedObject.FindProperty("manualDepthTexture");
            manualViewTransform = serializedObject.FindProperty("manualViewTransform");
            manualViewSize = serializedObject.FindProperty("manualViewSize");
            manualNearClip = serializedObject.FindProperty("manualNearClip");
            manualFarClip = serializedObject.FindProperty("manualFarClip");
            gridResolution = serializedObject.FindProperty("gridResolution");
            boundsSize = serializedObject.FindProperty("boundsSize");
            isoValue = serializedObject.FindProperty("isoValue");
            sdfFar = serializedObject.FindProperty("sdfFar");
            allowUpdateInEditMode = serializedObject.FindProperty("allowUpdateInEditMode");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(depthToSdfCompute);
            EditorGUILayout.PropertyField(sourceMode);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            if (sourceMode.enumValueIndex == 0)
            {
                EditorGUILayout.PropertyField(depthCamera);
                EditorGUILayout.PropertyField(cameraTextureSize);
                EditorGUILayout.PropertyField(cameraViewSizeOverride);
            }
            else
            {
                EditorGUILayout.PropertyField(manualDepthTexture);
                EditorGUILayout.PropertyField(manualViewTransform);
                EditorGUILayout.PropertyField(manualViewSize);
                EditorGUILayout.PropertyField(manualNearClip);
                EditorGUILayout.PropertyField(manualFarClip);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Volume", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(gridResolution);
            EditorGUILayout.PropertyField(boundsSize);
            EditorGUILayout.PropertyField(isoValue);
            EditorGUILayout.PropertyField(sdfFar);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(allowUpdateInEditMode);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
