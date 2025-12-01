using UnityEngine;

namespace VfxToMesh
{
    /// <summary>
    /// SDF グリッドの共通定義（テクスチャや生成物は持たない）。
    /// </summary>
    public readonly struct SdfDefinition
    {
        public int GridResolution { get; }
        public Vector3 BoundsSize { get; }
        public Vector3 BoundsCenter { get; }
        public float IsoValue { get; }
        public float SdfFar { get; }
        public float DistanceScale { get; }
        public Matrix4x4 LocalToWorld { get; }
        public Matrix4x4 WorldToLocal { get; }

        public Vector3 BoundsMin => BoundsCenter - BoundsSize * 0.5f;

        public SdfDefinition(
            int gridResolution,
            Vector3 boundsSize,
            Vector3 boundsCenter,
            float isoValue,
            float sdfFar,
            Matrix4x4 localToWorld,
            Matrix4x4 worldToLocal)
        {
            GridResolution = gridResolution;
            BoundsSize = boundsSize;
            BoundsCenter = boundsCenter;
            IsoValue = isoValue;
            SdfFar = sdfFar;
            LocalToWorld = localToWorld;
            WorldToLocal = worldToLocal;
            DistanceScale = ComputeDistanceScale(boundsSize);
        }

        public static float ComputeDistanceScale(Vector3 boundsSize)
        {
            float maxDimension = Mathf.Max(boundsSize.x, boundsSize.y, boundsSize.z);
            return maxDimension > 0f ? 1f / maxDimension : 1f;
        }
    }
}
