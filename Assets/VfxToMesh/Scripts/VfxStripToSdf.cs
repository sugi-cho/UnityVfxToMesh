using UnityEngine;
using UnityEngine.VFX;

namespace VfxToMesh
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class VfxStripToSdf : SdfVolumeSource
    {
        private static readonly int StripBufferId = Shader.PropertyToID("StripPoints");
        private static readonly int PointCapacityId = Shader.PropertyToID("StripPointCapacity");

        [Header("Compute Assets")]
        [SerializeField] private ComputeShader sdfCompute = default!;
        [SerializeField] private VisualEffect targetVfx = default!;

        [Header("Strip")]
        [SerializeField, Range(1, 512)] private int stripCount = 128;
        [SerializeField, Range(2, 4096)] private int particleCountPerStrip = 1024;

        [Header("Simulation")]
        [SerializeField, Range(64, 160)] private int gridResolution = 96;
        [SerializeField] private Vector3 boundsSize = new(6f, 6f, 6f);
        [SerializeField] private float isoValue = 0f;
        [SerializeField] private float sdfFar = 5f;
        [SerializeField, Range(1f, 3f)] private float sdfRadiusMultiplier = 2f;
        [SerializeField, Range(1.1f, 5f)] private float sdfFadeMultiplier = 3f;
        [SerializeField, Range(0.5f, 3f)] private float colorRadiusMultiplier = 1f;
        [SerializeField, Range(1f, 5f)] private float colorFadeMultiplier = 1.5f;
        [SerializeField, Range(0.0f, 3f)] private float smoothUnionStrength = 0.25f;
        [SerializeField] private ColorBlendMode colorBlendMode = ColorBlendMode.Normalized;

        [Header("Debug")]
        [SerializeField] private bool allowUpdateInEditMode = true;
        private enum ColorBlendMode
        {
            Normalized,
            Accumulated
        }

        private GraphicsBuffer pointBuffer;
        private RenderTexture sdfTexture;
        private RenderTexture colorTexture;
        private int kernelClearSdf;
        private int kernelStampStrips;
        private int kernelNormalizeColorVolume;

        private const int THREADS_1D = 256;
        private const int THREADS_3D = 8;
        private const int PointStride = sizeof(float) * 4; // using float4 packing (2 slots per point)

        private int MaxPointCapacity => stripCount * particleCountPerStrip;

        private SdfDefinition currentDefinition;
        private bool hasDefinition;

        private bool KernelsReady =>
            sdfCompute != null &&
            kernelClearSdf >= 0 &&
            kernelStampStrips >= 0 &&
            kernelNormalizeColorVolume >= 0;

        public override bool TryGetSdfVolume(out SdfVolume volume)
        {
            if (sdfTexture == null || colorTexture == null)
            {
                volume = default;
                return false;
            }

            var def = hasDefinition ? currentDefinition : BuildLocalDefinition();
            if (sdfTexture.width != def.GridResolution)
            {
                volume = default;
                return false;
            }

            volume = new SdfVolume(
                sdfTexture,
                def.GridResolution,
                def.BoundsSize,
                def.BoundsCenter,
                def.IsoValue,
                def.SdfFar,
                def.LocalToWorld,
                def.WorldToLocal,
                colorTexture,
                def.DistanceScale);
            return volume.IsValid;
        }

        private void Awake()
        {
            CacheKernelIds();
        }

        private void OnValidate()
        {
            gridResolution = Mathf.Clamp(gridResolution, 64, 160);
            stripCount = Mathf.Clamp(stripCount, 1, 512);
            particleCountPerStrip = Mathf.Clamp(particleCountPerStrip, 2, 4096);
            boundsSize = new Vector3(
                Mathf.Max(0.01f, boundsSize.x),
                Mathf.Max(0.01f, boundsSize.y),
                Mathf.Max(0.01f, boundsSize.z));
            sdfRadiusMultiplier = Mathf.Clamp(sdfRadiusMultiplier, 1f, 3f);
            sdfFadeMultiplier = Mathf.Max(sdfFadeMultiplier, sdfRadiusMultiplier + 0.01f);
            colorRadiusMultiplier = Mathf.Clamp(colorRadiusMultiplier, 0.5f, 3f);
            colorFadeMultiplier = Mathf.Max(colorFadeMultiplier, colorRadiusMultiplier + 0.01f);
            smoothUnionStrength = Mathf.Clamp(smoothUnionStrength, 0f, 3f);

            CacheKernelIds();
            ConfigureVisualEffect();
            if (pointBuffer != null && pointBuffer.count != MaxPointCapacity * 2)
            {
                ReleaseResources();
            }
        }

        private void OnEnable()
        {
            var localDefinition = BuildLocalDefinition();
            TryResolveDefinition(localDefinition, out var resolvedDefinition, logWarning: false);
            EnsureResources(resolvedDefinition);
        }

        private void OnDrawGizmosSelected()
        {
            if (useSharedDefinition && sharedDefinition != null && sharedDefinition.TryGetDefinition(out _))
            {
                return; // Definition 側に任せる
            }

            var def = BuildLocalDefinition();
            DrawBoundsGizmo(def);
        }

        private void OnDisable()
        {
            ReleaseResources();
        }

        private void Update()
        {
            if (!ShouldUpdate() || sdfCompute == null || targetVfx == null)
            {
                return;
            }

            var localDefinition = BuildLocalDefinition();
            TryResolveDefinition(localDefinition, out var resolvedDefinition);

            if (!EnsureResources(resolvedDefinition))
            {
                return;
            }

            currentDefinition = resolvedDefinition;
            hasDefinition = true;

            if (UpdateSdf(resolvedDefinition))
            {
                Version++;
            }
        }

        private bool ShouldUpdate()
        {
            return Application.isPlaying || allowUpdateInEditMode;
        }

        private static void DrawBoundsGizmo(in SdfDefinition def)
        {
            Color fill = new Color(1f, 0.6f, 0f, 0.05f);
            Color line = new Color(1f, 0.6f, 0f, 0.8f);
            Gizmos.color = fill;
            Gizmos.DrawCube(def.BoundsCenter, def.BoundsSize);
            Gizmos.color = line;
            Gizmos.DrawWireCube(def.BoundsCenter, def.BoundsSize);
        }

        private SdfDefinition BuildLocalDefinition()
        {
            return new SdfDefinition(
                gridResolution,
                boundsSize,
                transform.position,
                isoValue,
                sdfFar,
                transform.localToWorldMatrix,
                transform.worldToLocalMatrix);
        }

        private void CacheKernelIds()
        {
            if (sdfCompute == null)
            {
                kernelClearSdf = -1;
                kernelStampStrips = -1;
                kernelNormalizeColorVolume = -1;
                return;
            }

            kernelClearSdf = sdfCompute.FindKernel("ClearSdf");
            kernelStampStrips = sdfCompute.FindKernel("StampStripSegments");
            kernelNormalizeColorVolume = sdfCompute.FindKernel("NormalizeColorVolume");
        }

        private bool EnsureResources(in SdfDefinition definition)
        {
            if (!KernelsReady)
            {
                return false;
            }

            if (pointBuffer != null &&
                pointBuffer.count == MaxPointCapacity * 2 &&
                sdfTexture != null &&
                sdfTexture.width == definition.GridResolution &&
                colorTexture != null &&
                colorTexture.width == definition.GridResolution)
            {
                ConfigureComputeBindings(definition);
                ConfigureVisualEffect();
                return true;
            }

            ReleaseResources();
            AllocateResources(definition);
            return pointBuffer != null && sdfTexture != null;
        }

        private void AllocateResources(in SdfDefinition definition)
        {
            if (!KernelsReady || targetVfx == null)
            {
                return;
            }

            int capacity = Mathf.Max(1, MaxPointCapacity * 2); // float4×2 per point
            pointBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, PointStride);

            sdfTexture = CreateSdfTexture(definition.GridResolution);
            colorTexture = CreateColorTexture(definition.GridResolution);

            ConfigureComputeBindings(definition);
            ConfigureVisualEffect();
        }

        private void ConfigureComputeBindings(in SdfDefinition definition)
        {
            if (!KernelsReady || pointBuffer == null || sdfTexture == null)
            {
                return;
            }

            sdfCompute.SetBuffer(kernelStampStrips, "_StripPoints", pointBuffer);
            sdfCompute.SetTexture(kernelClearSdf, "_SdfVolumeRW", sdfTexture);
            sdfCompute.SetTexture(kernelStampStrips, "_SdfVolumeRW", sdfTexture);

            sdfCompute.SetFloat("_ColorRadiusMultiplier", colorRadiusMultiplier);
            sdfCompute.SetFloat("_ColorFadeMultiplier", colorFadeMultiplier);
            sdfCompute.SetFloat("_SdfRadiusMultiplier", sdfRadiusMultiplier);
            sdfCompute.SetFloat("_SdfFadeMultiplier", sdfFadeMultiplier);
            sdfCompute.SetFloat("_SmoothFactor", smoothUnionStrength);
            sdfCompute.SetFloat("_DistanceScale", definition.DistanceScale);
            sdfCompute.SetInt("_PointsPerStrip", particleCountPerStrip);

            if (colorTexture != null)
            {
                sdfCompute.SetTexture(kernelClearSdf, "_ColorVolumeRW", colorTexture);
                sdfCompute.SetTexture(kernelStampStrips, "_ColorVolumeRW", colorTexture);
                sdfCompute.SetTexture(kernelNormalizeColorVolume, "_ColorVolumeRW", colorTexture);
            }
        }

        private void ConfigureVisualEffect()
        {
            if (targetVfx == null || pointBuffer == null)
            {
                return;
            }

            targetVfx.SetGraphicsBuffer(StripBufferId, pointBuffer);
            targetVfx.SetInt(PointCapacityId, MaxPointCapacity);
        }

        private bool UpdateSdf(in SdfDefinition definition)
        {
            if (!KernelsReady || pointBuffer == null || sdfTexture == null)
            {
                return false;
            }

            if (!TryGetSdfVolume(out var volume))
            {
                return false;
            }

            SdfShaderParams.Push(sdfCompute, volume, MaxPointCapacity);

            int group3d = Mathf.CeilToInt(definition.GridResolution / (float)THREADS_3D);
            Dispatch(kernelClearSdf, group3d, group3d, group3d);

            int pointGroups = Mathf.CeilToInt(MaxPointCapacity / (float)THREADS_1D);
            Dispatch(kernelStampStrips, pointGroups, 1, 1);
            NormalizeColorVolume();
            return true;
        }

        private RenderTexture CreateSdfTexture(int resolution)
        {
            var tex = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat)
            {
                volumeDepth = resolution,
                dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SdfVolume"
            };

            tex.Create();
            return tex;
        }

        private RenderTexture CreateColorTexture(int resolution)
        {
            var tex = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat)
            {
                volumeDepth = resolution,
                dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "ColorVolume"
            };

            tex.Create();
            return tex;
        }

        private void Dispatch(int kernel, int groupsX, int groupsY, int groupsZ)
        {
            sdfCompute.Dispatch(kernel, groupsX, groupsY, groupsZ);
        }

        private void NormalizeColorVolume()
        {
            if (colorBlendMode != ColorBlendMode.Normalized || !KernelsReady || colorTexture == null)
            {
                return;
            }

            int activeResolution = hasDefinition ? currentDefinition.GridResolution : gridResolution;
            int group3d = Mathf.CeilToInt(activeResolution / (float)THREADS_3D);
            Dispatch(kernelNormalizeColorVolume, group3d, group3d, group3d);
        }

        private void ReleaseResources()
        {
            pointBuffer?.Dispose();
            pointBuffer = null;

            if (sdfTexture != null)
            {
                sdfTexture.Release();
                if (Application.isPlaying)
                {
                    Destroy(sdfTexture);
                }
                else
                {
                    DestroyImmediate(sdfTexture);
                }

                sdfTexture = null;
            }

            if (colorTexture != null)
            {
                colorTexture.Release();
                if (Application.isPlaying)
                {
                    Destroy(colorTexture);
                }
                else
                {
                    DestroyImmediate(colorTexture);
                }

                colorTexture = null;
            }
        }
    }
}
