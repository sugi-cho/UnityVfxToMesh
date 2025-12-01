using UnityEngine;

namespace VfxToMesh
{
    /// <summary>
    /// SDF の解像度・バウンズなどの定義だけを提供するコンポーネント。SDF を自前では生成しない。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SdfDefinitionSource : MonoBehaviour
    {
        [Header("Definition")]
        [SerializeField, Range(32, 192)] private int gridResolution = 96;
        [SerializeField] private Vector3 boundsSize = new(6f, 6f, 6f);
        [SerializeField] private float isoValue = 0f;
        [SerializeField] private float sdfFar = 5f;

        public bool TryGetDefinition(out SdfDefinition definition)
        {
            definition = new SdfDefinition(
                gridResolution,
                boundsSize,
                transform.position,
                isoValue,
                sdfFar,
                transform.localToWorldMatrix,
                transform.worldToLocalMatrix);

            return gridResolution > 0 && boundsSize.sqrMagnitude > 0.0f;
        }

        private void OnValidate()
        {
            gridResolution = Mathf.Clamp(gridResolution, 32, 192);
            boundsSize = new Vector3(
                Mathf.Max(0.01f, boundsSize.x),
                Mathf.Max(0.01f, boundsSize.y),
                Mathf.Max(0.01f, boundsSize.z));
            sdfFar = Mathf.Max(0.01f, sdfFar);
        }

        private void OnDrawGizmosSelected()
        {
            if (!TryGetDefinition(out var def))
            {
                return;
            }

            DrawBoundsGizmo(def);
        }

        private static void DrawBoundsGizmo(in SdfDefinition def)
        {
            Color fill = new Color(0f, 0.5f, 1f, 0.05f);
            Color line = new Color(0f, 0.5f, 1f, 0.8f);
            Gizmos.color = fill;
            Gizmos.DrawCube(def.BoundsCenter, def.BoundsSize);
            Gizmos.color = line;
            Gizmos.DrawWireCube(def.BoundsCenter, def.BoundsSize);
        }
    }
}
