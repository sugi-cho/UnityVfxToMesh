using UnityEngine;

namespace VfxToMesh
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SdfFarGapFill : SdfVolumeSource
    {
        private static readonly string KernelName = "FillSdfFar";

        [Header("Source")]
        [SerializeField] private SdfVolumeSource source = default!;

        [Header("Compute Settings")]
        [SerializeField] private ComputeShader fillCompute = default!;
        [SerializeField] private bool allowUpdateInEditMode = true;

        private RenderTexture filledTexture;
        private int fillKernel = -1;

        private bool KernelsReady => fillCompute != null && fillKernel >= 0;

        public SdfVolumeSource Source
        {
            get => source;
            set => source = value;
        }

        private uint cachedSourceVersion = uint.MaxValue;
        private bool ShouldUpdate => Application.isPlaying || allowUpdateInEditMode;

        public override bool TryGetSdfVolume(out SdfVolume volume)
        {
            volume = default;
            if (!KernelsReady || source == null || fillCompute == null)
            {
                return false;
            }

            if (!source.TryGetSdfVolume(out var original) || !original.IsValid)
            {
                return false;
            }

            if (!EnsureFilledTexture(original.GridResolution))
            {
                return false;
            }

            EnsureFilled(original);

            volume = new SdfVolume(
                filledTexture,
                original.GridResolution,
                original.BoundsSize,
                original.BoundsCenter,
                original.IsoValue,
                original.SdfFar,
                original.LocalToWorld,
                original.WorldToLocal,
                original.ColorTexture);

            return volume.IsValid;
        }

        private void EnsureFilled(in SdfVolume original)
        {
            if (cachedSourceVersion == source.Version)
            {
                return;
            }

            fillCompute.SetInt("_GridResolution", original.GridResolution);
            fillCompute.SetFloat("_SdfFar", original.SdfFar);
            fillCompute.SetFloat("_VoxelSize", original.VoxelSize);
            fillCompute.SetTexture(fillKernel, "_SourceSdf", original.Texture);
            fillCompute.SetTexture(fillKernel, "_DestSdf", filledTexture);

            int groups = Mathf.CeilToInt(original.GridResolution / 8f);
            fillCompute.Dispatch(fillKernel, groups, groups, groups);

            cachedSourceVersion = source.Version;
        }

        private bool EnsureFilledTexture(int resolution)
        {
            if (filledTexture != null && filledTexture.width == resolution)
            {
                return true;
            }

            ReleaseTextures();
            filledTexture = CreateVolumeTexture(resolution);
            return filledTexture != null;
        }

        private RenderTexture CreateVolumeTexture(int resolution)
        {
            var tex = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat)
            {
                dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
                volumeDepth = resolution,
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SdfFarFilledVolume"
            };

            tex.Create();
            return tex;
        }

        private void ReleaseTextures()
        {
            if (filledTexture != null)
            {
                filledTexture.Release();
                if (Application.isPlaying)
                {
                    Destroy(filledTexture);
                }
                else
                {
                    DestroyImmediate(filledTexture);
                }
                filledTexture = null;
            }
        }

        private void CacheKernel()
        {
            fillKernel = fillCompute != null ? fillCompute.FindKernel(KernelName) : -1;
        }

        private void OnEnable()
        {
            CacheKernel();
        }

        private void Update()
        {
            if (!ShouldUpdate || !KernelsReady || source == null || fillCompute == null)
            {
                return;
            }

            if (!source.TryGetSdfVolume(out var original) || !original.IsValid)
            {
                return;
            }

            if (!EnsureFilledTexture(original.GridResolution))
            {
                return;
            }

            EnsureFilled(original);
        }

        private void OnValidate()
        {
            CacheKernel();
        }

        private void OnDisable()
        {
            ReleaseTextures();
        }
    }
}
