using UnityEngine;
using UnityEngine.VFX;

namespace VfxToMesh
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class VfxToSdf : SdfVolumeSource
    {
        private static readonly int ParticleBufferId = Shader.PropertyToID("ParticlePositions");
        private static readonly int ParticleCountId = Shader.PropertyToID("ParticleCount");
        private static readonly int ParticleColorBufferId = Shader.PropertyToID("ParticleColors");

        [Header("Compute Assets")]
        [SerializeField] private ComputeShader sdfCompute = default!;
        [SerializeField] private VisualEffect targetVfx = default!;

        [Header("Simulation")]
        [SerializeField, Range(64, 160)] private int gridResolution = 96;
        [SerializeField, Range(512, 20000)] private int particleCount = 8192;
        [SerializeField] private Vector3 boundsSize = new(6f, 6f, 6f);
        [SerializeField] private float isoValue = 0f;
        [SerializeField] private float sdfFar = 5f;
        [SerializeField, Range(1f, 3f)] private float sdfRadiusMultiplier = 2f;
        [SerializeField, Range(1.1f, 5f)] private float sdfFadeMultiplier = 3f;
        [SerializeField, Range(0.5f, 3f)] private float colorRadiusMultiplier = 1f;
        [SerializeField, Range(1f, 5f)] private float colorFadeMultiplier = 1.5f;
        [SerializeField] private bool useSmoothUnion = false;
        [SerializeField, Range(0.01f, 3f)] private float smoothUnionStrength = 0.25f;

        [Header("Debug")]
        [SerializeField] private bool allowUpdateInEditMode = true;

        private GraphicsBuffer particleBuffer;
        private GraphicsBuffer particleColorBuffer;
        private RenderTexture sdfTexture;
        private RenderTexture colorTexture;
        private int kernelClearSdf;
        private int kernelStampParticles;
        private int kernelClearParticles;
        private int kernelNormalizeColorVolume;

        private const int THREADS_1D = 256;
        private const int THREADS_3D = 8;

        private bool KernelsReady =>
            sdfCompute != null &&
            kernelClearSdf >= 0 &&
            kernelStampParticles >= 0 &&
            kernelClearParticles >= 0 &&
            kernelNormalizeColorVolume >= 0;

        public override bool TryGetSdfVolume(out SdfVolume volume)
        {
            if (sdfTexture == null || colorTexture == null)
            {
                volume = default;
                return false;
            }

            volume = new SdfVolume(
                sdfTexture,
                gridResolution,
                boundsSize,
                transform.position,
                isoValue,
                sdfFar,
                transform.localToWorldMatrix,
                transform.worldToLocalMatrix,
                colorTexture);
            return volume.IsValid;
        }

        private void Awake()
        {
            CacheKernelIds();
        }

        private void OnValidate()
        {
            gridResolution = Mathf.Clamp(gridResolution, 64, 160);
            particleCount = Mathf.Clamp(particleCount, 512, 20000);
            boundsSize = new Vector3(
                Mathf.Max(0.01f, boundsSize.x),
                Mathf.Max(0.01f, boundsSize.y),
                Mathf.Max(0.01f, boundsSize.z));
            sdfRadiusMultiplier = Mathf.Clamp(sdfRadiusMultiplier, 1f, 3f);
            sdfFadeMultiplier = Mathf.Max(sdfFadeMultiplier, sdfRadiusMultiplier + 0.01f);
            colorRadiusMultiplier = Mathf.Clamp(colorRadiusMultiplier, 0.5f, 3f);
            colorFadeMultiplier = Mathf.Max(colorFadeMultiplier, colorRadiusMultiplier + 0.01f);

            CacheKernelIds();
            ConfigureVisualEffect();
            if (particleBuffer != null && particleBuffer.count != particleCount)
            {
                ReleaseResources();
            }
        }

        private void OnEnable()
        {
            AllocateResources();
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

            if (!EnsureResources())
            {
                return;
            }

            if (UpdateSdf())
            {
                Version++;
            }
        }

        private bool ShouldUpdate()
        {
            return Application.isPlaying || allowUpdateInEditMode;
        }

        private void CacheKernelIds()
        {
            if (sdfCompute == null)
            {
                kernelClearSdf = -1;
                kernelStampParticles = -1;
                kernelClearParticles = -1;
                kernelNormalizeColorVolume = -1;
                return;
            }

            kernelClearSdf = sdfCompute.FindKernel("ClearSdf");
            kernelStampParticles = sdfCompute.FindKernel("StampParticles");
            kernelClearParticles = sdfCompute.FindKernel("ClearParticleBuffers");
            kernelNormalizeColorVolume = sdfCompute.FindKernel("NormalizeColorVolume");
        }

        private bool EnsureResources()
        {
            if (!KernelsReady)
            {
                return false;
            }

            if (particleBuffer != null &&
                particleBuffer.count == particleCount &&
                particleColorBuffer != null &&
                particleColorBuffer.count == particleCount &&
                sdfTexture != null &&
                sdfTexture.width == gridResolution &&
                colorTexture != null &&
                colorTexture.width == gridResolution)
            {
                ConfigureComputeBindings();
                ConfigureVisualEffect();
                return true;
            }

            ReleaseResources();
            AllocateResources();
            return particleBuffer != null && sdfTexture != null;
        }

        private void AllocateResources()
        {
            if (!KernelsReady || targetVfx == null)
            {
                return;
            }

            particleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, sizeof(float) * 4);
            particleColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, sizeof(float) * 4);
            sdfTexture = CreateSdfTexture(gridResolution);
            colorTexture = CreateColorTexture(gridResolution);

            ConfigureComputeBindings();
            ConfigureVisualEffect();
            ClearParticleBuffers();
        }

        private void ConfigureComputeBindings()
        {
            if (!KernelsReady || particleBuffer == null || sdfTexture == null)
            {
                return;
            }

            sdfCompute.SetBuffer(kernelStampParticles, "_Particles", particleBuffer);
            sdfCompute.SetBuffer(kernelClearParticles, "_Particles", particleBuffer);
            sdfCompute.SetTexture(kernelClearSdf, "_SdfVolumeRW", sdfTexture);
            sdfCompute.SetTexture(kernelStampParticles, "_SdfVolumeRW", sdfTexture);
            sdfCompute.SetFloat("_ColorRadiusMultiplier", colorRadiusMultiplier);
            sdfCompute.SetFloat("_ColorFadeMultiplier", colorFadeMultiplier);
            sdfCompute.SetFloat("_SdfRadiusMultiplier", sdfRadiusMultiplier);
            sdfCompute.SetFloat("_SdfFadeMultiplier", sdfFadeMultiplier);
            sdfCompute.SetInt("_UseSmoothUnion", useSmoothUnion ? 1 : 0);
            sdfCompute.SetFloat("_SmoothFactor", smoothUnionStrength);

            if (particleColorBuffer != null && colorTexture != null)
            {
                sdfCompute.SetBuffer(kernelStampParticles, "_ParticleColors", particleColorBuffer);
                sdfCompute.SetBuffer(kernelClearParticles, "_ParticleColors", particleColorBuffer);
                sdfCompute.SetTexture(kernelClearSdf, "_ColorVolumeRW", colorTexture);
                sdfCompute.SetTexture(kernelStampParticles, "_ColorVolumeRW", colorTexture);
                sdfCompute.SetTexture(kernelNormalizeColorVolume, "_ColorVolumeRW", colorTexture);
            }
        }

        private void ConfigureVisualEffect()
        {
            if (targetVfx == null || particleBuffer == null || particleColorBuffer == null)
            {
                return;
            }

            targetVfx.SetGraphicsBuffer(ParticleBufferId, particleBuffer);
            targetVfx.SetGraphicsBuffer(ParticleColorBufferId, particleColorBuffer);
            targetVfx.SetInt(ParticleCountId, particleCount);
        }

        private bool UpdateSdf()
        {
            if (!KernelsReady || particleBuffer == null || sdfTexture == null)
            {
                return false;
            }

            if (targetVfx != null)
            {
                targetVfx.SetInt(ParticleCountId, particleCount);
            }

            if (!TryGetSdfVolume(out var volume))
            {
                return false;
            }

            SdfShaderParams.Push(sdfCompute, volume, particleCount);

            int group3d = Mathf.CeilToInt(gridResolution / (float)THREADS_3D);
            Dispatch(kernelClearSdf, group3d, group3d, group3d);

            int particleGroups = Mathf.CeilToInt(particleCount / (float)THREADS_1D);
            Dispatch(kernelStampParticles, particleGroups, 1, 1);
            NormalizeColorVolume();
            ClearParticleBuffers();
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
            if (!KernelsReady || colorTexture == null)
            {
                return;
            }

            int group3d = Mathf.CeilToInt(gridResolution / (float)THREADS_3D);
            Dispatch(kernelNormalizeColorVolume, group3d, group3d, group3d);
        }

        private void ClearParticleBuffers()
        {
            if (!KernelsReady || particleBuffer == null || particleColorBuffer == null)
            {
                return;
            }

            int particleGroups = Mathf.CeilToInt(particleCount / (float)THREADS_1D);
            Dispatch(kernelClearParticles, particleGroups, 1, 1);
        }

        private void ReleaseResources()
        {
            particleBuffer?.Dispose();
            particleColorBuffer?.Dispose();
            particleBuffer = null;
            particleColorBuffer = null;

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
