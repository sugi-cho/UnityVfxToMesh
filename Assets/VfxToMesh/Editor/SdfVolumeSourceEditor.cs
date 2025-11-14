#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VfxToMesh
{
    [CustomEditor(typeof(SdfVolumeSource), true)]
    public class SdfVolumeSourceEditor : UnityEditor.Editor
    {
        private bool showVolumeInfo = true;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            showVolumeInfo = EditorGUILayout.Foldout(showVolumeInfo, "Current Volume");
            if (!showVolumeInfo)
            {
                return;
            }

            EditorGUI.BeginDisabledGroup(true);
            if (target is SdfVolumeSource source && source.TryGetSdfVolume(out var volume) && volume.IsValid)
            {
                EditorGUILayout.ObjectField("SDF Volume", volume.Texture, typeof(RenderTexture), false);
                EditorGUILayout.ObjectField("Color Volume", volume.ColorTexture, typeof(RenderTexture), false);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Resolution", $"{volume.GridResolution}³");
                    EditorGUILayout.LabelField("Bounds", volume.BoundsSize.ToString("F2"));
                }
                EditorGUILayout.LabelField("Iso Value", volume.IsoValue.ToString("F3"));
                EditorGUILayout.LabelField("SDF Far", volume.SdfFar.ToString("F3"));
            }
            else
            {
                EditorGUILayout.HelpBox("No valid SDF volume currently available.", MessageType.Info);
            }
            EditorGUI.EndDisabledGroup();
        }
    }
}
#endif
