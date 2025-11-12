
using UnityEngine;

namespace VfxToMesh
{
    public readonly struct SdfVolume
    {
        public RenderTexture Texture { get; }
        public RenderTexture ColorTexture { get; }
        public int GridResolution { get; }
        public Vector3 BoundsSize { get; }
        public Vector3 BoundsCenter { get; }
        public float IsoValue { get; }
        public float SdfFar { get; }
        public Matrix4x4 LocalToWorld { get; }
        public Matrix4x4 WorldToLocal { get; }

        public bool IsValid => Texture != null && ColorTexture != null;
        public Vector3 BoundsMin => BoundsCenter - BoundsSize * 0.5f;
        public int CellResolution => Mathf.Max(1, GridResolution - 1);
        public int CellCount => CellResolution * CellResolution * CellResolution;
        public float VoxelSize => GridResolution > 0 ? BoundsSize.x / GridResolution : 0f;

        public SdfVolume(
            RenderTexture texture,
            int gridResolution,
            Vector3 boundsSize,
            Vector3 boundsCenter,
            float isoValue,
            float sdfFar,
            Matrix4x4 localToWorld,
            Matrix4x4 worldToLocal,
            RenderTexture colorTexture)
        {
            Texture = texture;
            GridResolution = gridResolution;
            BoundsSize = boundsSize;
            BoundsCenter = boundsCenter;
            IsoValue = isoValue;
            SdfFar = sdfFar;
            LocalToWorld = localToWorld;
            WorldToLocal = worldToLocal;
            ColorTexture = colorTexture;
        }
    }

    public abstract class SdfVolumeSource : MonoBehaviour
    {
        public uint Version { get; protected set; }

        public abstract bool TryGetSdfVolume(out SdfVolume volume);
    }

    internal static class SdfShaderParams
    {
        public static void Push(ComputeShader shader, in SdfVolume volume, int particleCount = 0)
        {
            if (shader == null || !volume.IsValid)
            {
                return;
            }

            shader.SetInt("_ParticleCount", particleCount);
            shader.SetInt("_GridResolution", volume.GridResolution);
            shader.SetInt("_CellResolution", volume.CellCount);

            Vector3 boundsMin = volume.BoundsMin;
            shader.SetVector("_BoundsMin", new Vector4(boundsMin.x, boundsMin.y, boundsMin.z, 0f));
            Vector3 boundsSize = volume.BoundsSize;
            shader.SetVector("_BoundsSize", new Vector4(boundsSize.x, boundsSize.y, boundsSize.z, 0f));

            shader.SetFloat("_VoxelSize", volume.VoxelSize);
            shader.SetFloat("_IsoValue", volume.IsoValue);
            shader.SetFloat("_SdfFar", volume.SdfFar);
            shader.SetMatrix("_LocalToWorld", volume.LocalToWorld);
            shader.SetMatrix("_WorldToLocal", volume.WorldToLocal);
        }
    }
}
