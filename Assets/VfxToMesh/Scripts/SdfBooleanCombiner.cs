using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VfxToMesh
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SdfBooleanCombiner : SdfVolumeSource
    {
        public enum BooleanOperation
        {
            Union,
            Intersection,
            Difference
        }

        [Header("Compute Assets")]
        [SerializeField] private ComputeShader booleanCompute;

        [Header("Sources")]
        [SerializeField] private List<SdfVolumeSource> sources = new();

        [Header("Operation")]
        [SerializeField] private BooleanOperation operation = BooleanOperation.Union;
        [SerializeField, Range(0.1f, 20f)] private float smoothBeta = 4f;

        [Header("Runtime")]
        [SerializeField] private bool allowUpdateInEditMode = true;

        private RenderTexture combinedTexture;
        private RenderTexture tempTexture;
        private int blendKernel = -1;
        private uint cachedVersionHash;

        private bool KernelsReady => booleanCompute != null && blendKernel >= 0;

        private void Awake()
        {
            CacheKernel();
        }

        private void OnEnable()
        {
            CacheKernel();
        }

        private void OnDisable()
        {
            ReleaseTextures();
        }

        private void OnValidate()
        {
            CacheKernel();
        }

        private void CacheKernel()
        {
            blendKernel = booleanCompute != null ? booleanCompute.FindKernel("BlendSdf") : -1;
        }

        public override bool TryGetSdfVolume(out SdfVolume volume)
        {
            volume = default;
            if (!KernelsReady || sources == null || sources.Count == 0 || (!allowUpdateInEditMode && !Application.isPlaying))
            {
                return false;
            }

            var gathered = new List<SdfVolume>(sources.Count);
            var readySources = new List<SdfVolumeSource>(sources.Count);
            foreach (var source in sources)
            {
                if (source == null || !source.TryGetSdfVolume(out var vol) || !vol.IsValid)
                {
                    continue;
                }

                gathered.Add(vol);
                readySources.Add(source);
            }

            if (gathered.Count == 0)
            {
                return false;
            }

            var primary = gathered[0];
            if (!EnsureTextures(primary))
            {
                return false;
            }

            if (NeedsRebuild(gathered, readySources))
            {
                if (!BuildCombined(gathered))
                {
                    return false;
                }

                cachedVersionHash = ComputeVersionHash(readySources);
                Version++;
            }

            volume = new SdfVolume(
                combinedTexture,
                primary.GridResolution,
                primary.BoundsSize,
                primary.BoundsCenter,
                primary.IsoValue,
                primary.SdfFar,
                primary.LocalToWorld,
                primary.WorldToLocal,
                primary.ColorTexture,
                primary.DistanceScale);

            return volume.IsValid;
        }

        private bool EnsureTextures(in SdfVolume template)
        {
            if (combinedTexture != null && combinedTexture.width == template.GridResolution)
            {
                return true;
            }

            ReleaseTextures();
            combinedTexture = CreateVolumeTexture(template.GridResolution, RenderTextureFormat.RFloat, "BooleanCombinedSdf");
            tempTexture = CreateVolumeTexture(template.GridResolution, RenderTextureFormat.RFloat, "BooleanCombineTemp");
            return combinedTexture != null && tempTexture != null;
        }

        private RenderTexture CreateVolumeTexture(int resolution, RenderTextureFormat format, string name)
        {
            var texture = new RenderTexture(resolution, resolution, 0, format)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = resolution,
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name
            };
            texture.Create();
            return texture;
        }

        private bool BuildCombined(IReadOnlyList<SdfVolume> volumes)
        {
            if (volumes.Count == 0)
            {
                return false;
            }

            Graphics.CopyTexture(volumes[0].Texture, combinedTexture);
            RenderTexture read = combinedTexture;
            RenderTexture write = tempTexture;

            int groups = Mathf.CeilToInt(volumes[0].GridResolution / 8f);

            for (int i = 1; i < volumes.Count; ++i)
            {
              booleanCompute.SetInt("_GridResolution", volumes[0].GridResolution);
              booleanCompute.SetInt("_Operation", (int)operation);
              booleanCompute.SetFloat("_SmoothBeta", smoothBeta);
                booleanCompute.SetTexture(blendKernel, "_InputA", read);
                booleanCompute.SetTexture(blendKernel, "_InputB", volumes[i].Texture);
                booleanCompute.SetTexture(blendKernel, "_Output", write);
                booleanCompute.Dispatch(blendKernel, groups, groups, groups);

                var swap = read;
                read = write;
                write = swap;
            }

            if (read != combinedTexture)
            {
                Graphics.CopyTexture(read, combinedTexture);
            }

            return true;
        }

        private bool NeedsRebuild(IReadOnlyList<SdfVolume> volumes, IReadOnlyList<SdfVolumeSource> sources)
        {
            if (cachedVersionHash == 0)
            {
                return true;
            }

            return cachedVersionHash != ComputeVersionHash(sources);
        }

        private uint ComputeVersionHash(IReadOnlyList<SdfVolumeSource> sources)
        {
            uint hash = 0;
            for (int i = 0; i < sources.Count; ++i)
            {
                hash = hash * 31 + sources[i].Version;
            }
            hash += (uint)operation;
            return hash;
        }

        private void ReleaseTextures()
        {
            ReleaseTexture(ref combinedTexture);
            ReleaseTexture(ref tempTexture);
        }

        private void ReleaseTexture(ref RenderTexture target)
        {
            if (target == null)
            {
                return;
            }

            target.Release();
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }

            target = null;
        }
    }
}
