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

        [Header("Compute Assets")]
        [SerializeField] private ComputeShader sdfCompute = default!;
        [SerializeField] private VisualEffect targetVfx = default!;

        [Header("Simulation")]
        [SerializeField, Range(64, 160)] private int gridResolution = 96;
        [SerializeField, Range(512, 20000)] private int particleCount = 8192;
        [SerializeField] private Vector3 boundsSize = new(6f, 6f, 6f);
        [SerializeField] private float isoValue = 0f;
        [SerializeField] private float sdfFar = 5f;

        [Header("Debug")]
        [SerializeField] private bool allowUpdateInEditMode = true;

        private GraphicsBuffer particleBuffer;
        private RenderTexture sdfTexture;
        private int kernelClearSdf;
        private int kernelStampParticles;

        private const int THREADS_1D = 256;
        private const int THREADS_3D = 8;

        private bool KernelsReady => sdfCompute != null && kernelClearSdf >= 0 && kernelStampParticles >= 0;

        public override bool TryGetSdfVolume(out SdfVolume volume)
        {
            if (sdfTexture == null)
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
                transform.worldToLocalMatrix);
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
                return;
            }

            kernelClearSdf = sdfCompute.FindKernel("ClearSdf");
            kernelStampParticles = sdfCompute.FindKernel("StampParticles");
        }

        private bool EnsureResources()
        {
            if (!KernelsReady)
            {
                return false;
            }

            if (particleBuffer != null &&
                particleBuffer.count == particleCount &&
                sdfTexture != null &&
                sdfTexture.width == gridResolution)
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
            sdfTexture = CreateSdfTexture(gridResolution);

            ConfigureComputeBindings();
            ConfigureVisualEffect();
        }

        private void ConfigureComputeBindings()
        {
            if (!KernelsReady || particleBuffer == null || sdfTexture == null)
            {
                return;
            }

            sdfCompute.SetBuffer(kernelStampParticles, "_Particles", particleBuffer);
            sdfCompute.SetTexture(kernelClearSdf, "_SdfVolumeRW", sdfTexture);
            sdfCompute.SetTexture(kernelStampParticles, "_SdfVolumeRW", sdfTexture);
        }

        private void ConfigureVisualEffect()
        {
            if (targetVfx == null || particleBuffer == null)
            {
                return;
            }

            targetVfx.SetGraphicsBuffer(ParticleBufferId, particleBuffer);
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

        private void Dispatch(int kernel, int groupsX, int groupsY, int groupsZ)
        {
            sdfCompute.Dispatch(kernel, groupsX, groupsY, groupsZ);
        }

        private void ReleaseResources()
        {
            particleBuffer?.Dispose();
            particleBuffer = null;

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
        }
    }
}
