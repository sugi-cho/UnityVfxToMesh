using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

namespace VfxToMesh
{
    [ExecuteAlways]
    public class VfxToMeshPipeline : MonoBehaviour
    {
        private static readonly int ParticleBufferId = Shader.PropertyToID("ParticlePositions");
        private static readonly int ParticleCountId = Shader.PropertyToID("ParticleCount");

        [Header("Compute Assets")]
        [SerializeField] private ComputeShader pipelineCompute = default!;
        [SerializeField] private VisualEffect targetVfx = default!;

        [Header("Simulation")]
        [SerializeField, Range(64, 160)] private int gridResolution = 96;
        [SerializeField, Range(512, 20000)] private int particleCount = 8192;
        [SerializeField] private Vector3 boundsSize = new(6f, 6f, 6f);
        [SerializeField] private float isoValue = 0f;
        [SerializeField] private float sdfFar = 5f;

        [Header("Debug & Rendering")]
        [SerializeField] private bool allowUpdateInEditMode = true;

        [System.Serializable]
        private struct RendererBinding
        {
            public MeshRenderer renderer;
            public MeshFilter meshFilter;
        }

        [SerializeField] private List<RendererBinding> targetRenderers = new List<RendererBinding>();

        private Mesh generatedMesh;
        private GraphicsBuffer particleBuffer;
        private GraphicsBuffer cellVertexBuffer;
        private GraphicsBuffer counterBuffer;
        private RenderTexture sdfTexture;
        private GraphicsBuffer meshPositionBuffer;
        private GraphicsBuffer meshNormalBuffer;
        private GraphicsBuffer meshIndexBuffer;
        private int meshVertexCapacity;
        private int meshIndexCapacity;
        private SubMeshDescriptor subMeshDescriptor = new SubMeshDescriptor(0, 0, MeshTopology.Triangles);

        private int kernelClearSdf;
        private int kernelStampParticles;
        private int kernelClearCells;
        private int kernelBuildVertices;
        private int kernelBuildIndices;

        private uint[] counterReadback = new uint[2];

        private const int THREADS_1D = 256;
        private const int THREADS_3D = 8;

        private MeshFilter PrimaryMeshFilter => targetRenderers.Count > 0 ? targetRenderers[0].meshFilter : null;

        private void Awake()
        {
            ValidateRendererBindings();
            CacheKernelIds();
        }

        private void OnValidate()
        {
            ValidateRendererBindings();
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
            if (!Application.isPlaying && !allowUpdateInEditMode)
            {
                return;
            }

            if (pipelineCompute == null)
            {
                return;
            }

            if (!EnsureResources())
            {
                return;
            }

            UpdateSdfAndMesh();
            UpdateRendererProperties();
        }

        private void CacheKernelIds()
        {
            if (pipelineCompute == null)
            {
                return;
            }

            kernelClearSdf = pipelineCompute.FindKernel("ClearSdf");
            kernelStampParticles = pipelineCompute.FindKernel("StampParticles");
            kernelClearCells = pipelineCompute.FindKernel("ClearCells");
            kernelBuildVertices = pipelineCompute.FindKernel("BuildSurfaceVertices");
            kernelBuildIndices = pipelineCompute.FindKernel("BuildSurfaceIndices");
        }

        private bool EnsureResources()
        {
            ValidateRendererBindings();
            if (PrimaryMeshFilter == null)
            {
                return false;
            }
            int cellsPerAxis = Mathf.Max(1, gridResolution - 1);
            int cellCount = cellsPerAxis * cellsPerAxis * cellsPerAxis;
            int maxVertices = cellCount;
            int maxIndices = cellCount * 6;

            if (particleBuffer != null &&
                particleBuffer.count == particleCount &&
                sdfTexture != null &&
                sdfTexture.width == gridResolution &&
                meshVertexCapacity == maxVertices &&
                meshIndexCapacity == maxIndices)
            {
                return true;
            }

            ReleaseResources();
            AllocateResources();
            return particleBuffer != null && generatedMesh != null;
        }

        private void AllocateResources()
        {
            int cellsPerAxis = Mathf.Max(1, gridResolution - 1);
            int cellCount = cellsPerAxis * cellsPerAxis * cellsPerAxis;
            int maxVertices = cellCount;
            int maxIndices = cellCount * 6;

            particleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, sizeof(float) * 4);
            cellVertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(int));
            counterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(uint));

            sdfTexture = CreateSdfTexture(gridResolution);
            EnsureMeshBuffers(maxVertices, maxIndices);

            ConfigureComputeBindings();
            ConfigureVisualEffect();
        }

        private void ConfigureComputeBindings()
        {
            if (pipelineCompute == null ||
                meshPositionBuffer == null ||
                meshNormalBuffer == null ||
                meshIndexBuffer == null)
            {
                return;
            }

            pipelineCompute.SetBuffer(kernelStampParticles, "_Particles", particleBuffer);

            pipelineCompute.SetBuffer(kernelClearCells, "_CellVertexIndices", cellVertexBuffer);
            pipelineCompute.SetBuffer(kernelClearCells, "_Counters", counterBuffer);
            pipelineCompute.SetBuffer(kernelBuildVertices, "_CellVertexIndices", cellVertexBuffer);
            pipelineCompute.SetBuffer(kernelBuildVertices, "_VertexBuffer", meshPositionBuffer);
            pipelineCompute.SetBuffer(kernelBuildVertices, "_NormalBuffer", meshNormalBuffer);
            pipelineCompute.SetBuffer(kernelBuildVertices, "_Counters", counterBuffer);
            pipelineCompute.SetBuffer(kernelBuildIndices, "_CellVertexIndices", cellVertexBuffer);
            pipelineCompute.SetBuffer(kernelBuildIndices, "_Counters", counterBuffer);
            pipelineCompute.SetBuffer(kernelBuildIndices, "_IndexBuffer", meshIndexBuffer);

            pipelineCompute.SetTexture(kernelClearSdf, "_SdfVolumeRW", sdfTexture);
            pipelineCompute.SetTexture(kernelStampParticles, "_SdfVolumeRW", sdfTexture);
            pipelineCompute.SetTexture(kernelBuildVertices, "_SdfVolume", sdfTexture);
            pipelineCompute.SetTexture(kernelBuildIndices, "_SdfVolume", sdfTexture);
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

        private void ValidateRendererBindings()
        {
            targetRenderers ??= new List<RendererBinding>();

            for (int i = targetRenderers.Count - 1; i >= 0; --i)
            {
                if (targetRenderers[i].renderer == null || targetRenderers[i].meshFilter == null)
                {
                    targetRenderers.RemoveAt(i);
                }
            }
        }

        private void ApplyMeshToRenderers()
        {
            ValidateRendererBindings();
            if (generatedMesh == null)
            {
                foreach (var binding in targetRenderers)
                {
                    if (binding.renderer != null)
                    {
                        binding.renderer.enabled = false;
                    }
                }
                return;
            }

            bool hasGeometry = subMeshDescriptor.indexCount > 0;
            foreach (var binding in targetRenderers)
            {
                if (binding.renderer == null || binding.meshFilter == null)
                {
                    continue;
                }

                if (binding.meshFilter.sharedMesh != generatedMesh)
                {
                    binding.meshFilter.sharedMesh = generatedMesh;
                }

                binding.renderer.enabled = hasGeometry;
            }
        }

        private void EnsureMeshBuffers(int vertexCapacity, int indexCapacity)
        {
            ValidateRendererBindings();
            var primaryFilter = PrimaryMeshFilter;
            if (primaryFilter == null)
            {
                Debug.LogWarning($"{nameof(VfxToMeshPipeline)} on {name} has no MeshFilter assigned in targetRenderers. Skipping mesh allocation.", this);
                return;
            }

            if (generatedMesh == null)
            {
                generatedMesh = new Mesh { name = "VfxToMesh.GeneratedMesh" };
                generatedMesh.indexFormat = IndexFormat.UInt32;
                generatedMesh.MarkDynamic();
                primaryFilter.sharedMesh = generatedMesh;
            }

            if (meshVertexCapacity == vertexCapacity &&
                meshIndexCapacity == indexCapacity &&
                meshPositionBuffer != null &&
                meshNormalBuffer != null &&
                meshIndexBuffer != null)
            {
                return;
            }

            meshVertexCapacity = vertexCapacity;
            meshIndexCapacity = indexCapacity;

            generatedMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            generatedMesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;

            generatedMesh.SetVertexBufferParams(vertexCapacity,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1));
            generatedMesh.SetIndexBufferParams(indexCapacity, IndexFormat.UInt32);

            subMeshDescriptor.indexStart = 0;
            subMeshDescriptor.indexCount = 0;
            subMeshDescriptor.topology = MeshTopology.Triangles;
            generatedMesh.SetSubMesh(0, subMeshDescriptor,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            generatedMesh.bounds = new Bounds(Vector3.zero, boundsSize + Vector3.one);

            meshPositionBuffer = generatedMesh.GetVertexBuffer(0);
            meshNormalBuffer = generatedMesh.GetVertexBuffer(1);
            meshIndexBuffer = generatedMesh.GetIndexBuffer();

            ApplyMeshToRenderers();
        }

        private RenderTexture CreateSdfTexture(int resolution)
        {
            var tex = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat)
            {
                volumeDepth = resolution,
                enableRandomWrite = true,
                dimension = TextureDimension.Tex3D,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SdfVolume"
            };

            tex.Create();
            return tex;
        }

        private void ReleaseResources()
        {
            particleBuffer?.Dispose();
            cellVertexBuffer?.Dispose();
            counterBuffer?.Dispose();

            particleBuffer = null;
            cellVertexBuffer = null;
            counterBuffer = null;
            meshPositionBuffer = null;
            meshNormalBuffer = null;
            meshIndexBuffer = null;
            meshVertexCapacity = 0;
            meshIndexCapacity = 0;
            subMeshDescriptor.indexCount = 0;

            generatedMesh?.Clear(false);
            ApplyMeshToRenderers();

            if (sdfTexture != null)
            {
                sdfTexture.Release();
                DestroyImmediate(sdfTexture);
                sdfTexture = null;
            }
        }

        private void UpdateSdfAndMesh()
        {
            if (pipelineCompute == null)
            {
                return;
            }

            PushCommonParams();

            int group3d = Mathf.CeilToInt(gridResolution / (float)THREADS_3D);
            Dispatch(kernelClearSdf, group3d, group3d, group3d);

            Dispatch(kernelStampParticles, Mathf.CeilToInt(particleCount / (float)THREADS_1D), 1, 1);

            int cellsPerAxis = Mathf.Max(1, gridResolution - 1);
            int cellCount = cellsPerAxis * cellsPerAxis * cellsPerAxis;
            Dispatch(kernelClearCells, Mathf.CeilToInt(cellCount / (float)THREADS_1D), 1, 1);

            Dispatch(kernelBuildVertices, group3d, group3d, group3d);
            Dispatch(kernelBuildIndices, group3d, group3d, group3d);

            counterBuffer.GetData(counterReadback);
            uint indexCount = counterReadback[1];

            if (generatedMesh != null)
            {
                subMeshDescriptor.indexCount = Mathf.Min((int)indexCount, meshIndexCapacity);
                generatedMesh.SetSubMesh(0, subMeshDescriptor,
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }
        }

        private void UpdateRendererProperties()
        {
            if (generatedMesh == null)
            {
                ApplyMeshToRenderers();
                return;
            }

            generatedMesh.bounds = new Bounds(Vector3.zero, boundsSize + Vector3.one);
            ApplyMeshToRenderers();
        }

        private void PushCommonParams()
        {
            if (pipelineCompute == null)
            {
                return;
            }

            int cellsPerAxis = Mathf.Max(1, gridResolution - 1);
            int cellCount = cellsPerAxis * cellsPerAxis * cellsPerAxis;
            Vector3 boundsMin = transform.position - boundsSize * 0.5f;
            Vector3 boundsExtent = boundsSize;
            Vector3 center = transform.position;

            pipelineCompute.SetInt("_ParticleCount", particleCount);
            pipelineCompute.SetInt("_GridResolution", gridResolution);
            pipelineCompute.SetInt("_CellResolution", cellCount);
            pipelineCompute.SetVector("_BoundsMin", new Vector4(boundsMin.x, boundsMin.y, boundsMin.z, 0f));
            pipelineCompute.SetVector("_BoundsSize", new Vector4(boundsExtent.x, boundsExtent.y, boundsExtent.z, 0f));
            pipelineCompute.SetFloat("_VoxelSize", boundsSize.x / gridResolution);
            pipelineCompute.SetFloat("_IsoValue", isoValue);
            pipelineCompute.SetFloat("_SdfFar", sdfFar);
            pipelineCompute.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
            pipelineCompute.SetMatrix("_WorldToLocal", transform.worldToLocalMatrix);
        }

        private void Dispatch(int kernel, int groupsX, int groupsY, int groupsZ)
        {
            pipelineCompute.Dispatch(kernel, groupsX, groupsY, groupsZ);
        }
    }
}
