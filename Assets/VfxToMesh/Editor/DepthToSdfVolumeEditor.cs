#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace VfxToMesh
{
    [CustomEditor(typeof(DepthToSdfVolume))]
    public class DepthToSdfVolumeEditor : UnityEditor.Editor
    {
        private SerializedProperty depthToSdfComputeProp;
        private SerializedProperty gridResolutionProp;
        private SerializedProperty boundsSizeProp;
        private SerializedProperty isoValueProp;
        private SerializedProperty sdfFarProp;
        private SerializedProperty depthViewsProp;
        private SerializedProperty allowUpdateInEditModeProp;

        private ReorderableList depthViewsList;

        private void OnEnable()
        {
            depthToSdfComputeProp = serializedObject.FindProperty("depthToSdfCompute");
            gridResolutionProp = serializedObject.FindProperty("gridResolution");
            boundsSizeProp = serializedObject.FindProperty("boundsSize");
            isoValueProp = serializedObject.FindProperty("isoValue");
            sdfFarProp = serializedObject.FindProperty("sdfFar");
            depthViewsProp = serializedObject.FindProperty("depthViews");
            allowUpdateInEditModeProp = serializedObject.FindProperty("allowUpdateInEditMode");

            depthViewsList = new ReorderableList(serializedObject, depthViewsProp, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Depth Views"),
                elementHeightCallback = GetDepthViewHeight,
                drawElementCallback = DrawDepthViewElement
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(depthToSdfComputeProp);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Volume Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(gridResolutionProp);
            EditorGUILayout.PropertyField(boundsSizeProp);
            EditorGUILayout.PropertyField(isoValueProp);
            EditorGUILayout.PropertyField(sdfFarProp);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(allowUpdateInEditModeProp);

            EditorGUILayout.Space();
            depthViewsList.DoLayoutList();

            serializedObject.ApplyModifiedProperties();
        }

        private float GetDepthViewHeight(int index)
        {
            var element = depthViewsProp.GetArrayElementAtIndex(index);
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float height = 2 * (EditorGUIUtility.singleLineHeight + spacing);

            int sourceMode = element.FindPropertyRelative("source").enumValueIndex;
            if (sourceMode == 0)
            {
                height += EditorGUI.GetPropertyHeight(element.FindPropertyRelative("depthTexture"), true) + spacing;
                height += EditorGUI.GetPropertyHeight(element.FindPropertyRelative("viewTransform"), true) + spacing;
                height += EditorGUI.GetPropertyHeight(element.FindPropertyRelative("viewSize"), true) + spacing;
                height += EditorGUI.GetPropertyHeight(element.FindPropertyRelative("nearClip"), true) + spacing;
                height += EditorGUI.GetPropertyHeight(element.FindPropertyRelative("farClip"), true) + spacing;
            }
            else
            {
                height += EditorGUI.GetPropertyHeight(element.FindPropertyRelative("depthCamera"), true) + spacing;
                height += EditorGUI.GetPropertyHeight(element.FindPropertyRelative("cameraTextureSize"), true) + spacing;
                height += EditorGUI.GetPropertyHeight(element.FindPropertyRelative("cameraViewSizeOverride"), true) + spacing;
            }

            return height + 8f;
        }

        private void DrawDepthViewElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = depthViewsProp.GetArrayElementAtIndex(index);
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            var enabledProp = element.FindPropertyRelative("enabled");
            var sourceProp = element.FindPropertyRelative("source");

            Rect fieldRect = new Rect(rect.x, rect.y + 2f, rect.width, EditorGUI.GetPropertyHeight(enabledProp, true));
            EditorGUI.PropertyField(fieldRect, enabledProp);

            fieldRect.y += fieldRect.height + spacing;
            fieldRect.height = EditorGUI.GetPropertyHeight(sourceProp, true);
            EditorGUI.PropertyField(fieldRect, sourceProp);

            fieldRect.y += fieldRect.height + spacing;
            EditorGUI.indentLevel++;

            if (sourceProp.enumValueIndex == 0)
            {
                fieldRect.height = EditorGUI.GetPropertyHeight(element.FindPropertyRelative("depthTexture"), true);
                EditorGUI.PropertyField(fieldRect, element.FindPropertyRelative("depthTexture"));
                fieldRect.y += fieldRect.height + spacing;

                fieldRect.height = EditorGUI.GetPropertyHeight(element.FindPropertyRelative("viewTransform"), true);
                EditorGUI.PropertyField(fieldRect, element.FindPropertyRelative("viewTransform"));
                fieldRect.y += fieldRect.height + spacing;

                fieldRect.height = EditorGUI.GetPropertyHeight(element.FindPropertyRelative("viewSize"), true);
                EditorGUI.PropertyField(fieldRect, element.FindPropertyRelative("viewSize"));
                fieldRect.y += fieldRect.height + spacing;

                fieldRect.height = EditorGUI.GetPropertyHeight(element.FindPropertyRelative("nearClip"), true);
                EditorGUI.PropertyField(fieldRect, element.FindPropertyRelative("nearClip"));
                fieldRect.y += fieldRect.height + spacing;

                fieldRect.height = EditorGUI.GetPropertyHeight(element.FindPropertyRelative("farClip"), true);
                EditorGUI.PropertyField(fieldRect, element.FindPropertyRelative("farClip"));
                fieldRect.y += fieldRect.height + spacing;
            }
            else
            {
                fieldRect.height = EditorGUI.GetPropertyHeight(element.FindPropertyRelative("depthCamera"), true);
                EditorGUI.PropertyField(fieldRect, element.FindPropertyRelative("depthCamera"));
                fieldRect.y += fieldRect.height + spacing;

                fieldRect.height = EditorGUI.GetPropertyHeight(element.FindPropertyRelative("cameraTextureSize"), true);
                EditorGUI.PropertyField(fieldRect, element.FindPropertyRelative("cameraTextureSize"));
                fieldRect.y += fieldRect.height + spacing;

                fieldRect.height = EditorGUI.GetPropertyHeight(element.FindPropertyRelative("cameraViewSizeOverride"), true);
                EditorGUI.PropertyField(fieldRect, element.FindPropertyRelative("cameraViewSizeOverride"));
                fieldRect.y += fieldRect.height + spacing;
            }

            EditorGUI.indentLevel--;
        }
    }
}
#endif
