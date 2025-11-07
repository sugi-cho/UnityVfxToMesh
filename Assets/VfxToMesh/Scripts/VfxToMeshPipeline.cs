using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

namespace VfxToMesh
{
    [ExecuteAlways]
    [RequireComponent(typeof(VisualEffect))]
    public class VfxToMeshPipeline : MonoBehaviour
    {
        private static readonly int ParticleBufferId = Shader.PropertyToID("_ParticlePositions");
        private static readonly int ParticleCountId = Shader.PropertyToID("_ParticleCount");
        private static readonly int SdfTextureId = Shader.PropertyToID("_SdfVolume");

        [Header("Compute Assets")]
        [SerializeField] private ComputeShader pipelineCompute = default!;
        [SerializeField] private Material surfaceMaterial = default!;
        [SerializeField] private Material sliceMaterial = default!;

        [Header("Simulation")]
        [SerializeField, Range(64, 160)] private int gridResolution = 96;
        [SerializeField, Range(512, 20000)] private int particleCount = 8192;
        [SerializeField] private Vector3 boundsSize = new(6f, 6f, 6f);
        [SerializeField, Range(0.01f, 0.25f)] private float particleRadius = 0.05f;
        [SerializeField] private float isoValue = 0f;
        [SerializeField] private float sdfFar = 5f;

        [Header("Flow Field")]
        [SerializeField] private float noiseFrequency = 0.8f;
        [SerializeField] private float noiseStrength = 1.0f;
        [SerializeField] private float velocityDamping = 0.35f;

        [Header("Debug & Rendering")]
        [SerializeField] private Color surfaceColor = new(0.4f, 0.85f, 1.0f, 1f);
        [SerializeField] private Color wireColor = new(0.1f, 0.1f, 0.1f, 1f);
        [SerializeField, Range(0.0f, 4.0f)] private float wireThickness = 1.25f;
        [SerializeField] private bool allowUpdateInEditMode = true;
        [SerializeField, Range(0, 2)] private int debugSliceAxis = 2;
        [SerializeField, Range(0f, 1f)] private float debugSliceDepth = 0.5f;

        private VisualEffect visualEffect = default!;
        private GraphicsBuffer particleBuffer;
        private GraphicsBuffer velocityBuffer;
        private GraphicsBuffer cellVertexBuffer;
        private GraphicsBuffer vertexBuffer;
        private GraphicsBuffer normalBuffer;
        private GraphicsBuffer indexBuffer;
        private GraphicsBuffer barycentricBuffer;
        private GraphicsBuffer counterBuffer;
        private GraphicsBuffer argsBuffer;
        private RenderTexture sdfTexture;

        private int kernelInitParticles;
        private int kernelIntegrateParticles;
        private int kernelClearSdf;
        private int kernelStampParticles;
        private int kernelClearCells;
        private int kernelBuildVertices;
        private int kernelBuildIndices;

        private uint[] counterReadback = new uint[2];
        private uint[] drawArgs = new uint[4];
        private Bounds drawBounds;

        private const int THREADS_1D = 256;
        private const int THREADS_3D = 8;

        private void Awake()
        {
            visualEffect = GetComponent<VisualEffect>();
            CacheKernelIds();
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

            if (pipelineCompute == null || surfaceMaterial == null)
            {
                return;
            }

            if (!EnsureResources())
            {
                return;
            }

            float deltaTime = Application.isPlaying ? Time.deltaTime : 1f / 60f;

            UpdateParticles(deltaTime);
            UpdateSdfAndMesh(deltaTime);
            IssueDrawCall();
            UpdateSliceDebug();
        }

        private void CacheKernelIds()
        {
            if (pipelineCompute == null)
            {
                return;
            }

            kernelInitParticles = pipelineCompute.FindKernel("InitParticles");
            kernelIntegrateParticles = pipelineCompute.FindKernel("IntegrateParticles");
            kernelClearSdf = pipelineCompute.FindKernel("ClearSdf");
            kernelStampParticles = pipelineCompute.FindKernel("StampParticles");
            kernelClearCells = pipelineCompute.FindKernel("ClearCells");
            kernelBuildVertices = pipelineCompute.FindKernel("BuildSurfaceVertices");
            kernelBuildIndices = pipelineCompute.FindKernel("BuildSurfaceIndices");
        }

        private bool EnsureResources()
        {
            if (particleBuffer != null &&
                particleBuffer.count == particleCount &&
                sdfTexture != null &&
                sdfTexture.width == gridResolution)
            {
                return true;
            }

            ReleaseResources();
            AllocateResources();
            return particleBuffer != null;
        }

        private void AllocateResources()
        {
            ReleaseResources();

            int cellsPerAxis = Mathf.Max(1, gridResolution - 1);
            int cellCount = cellsPerAxis * cellsPerAxis * cellsPerAxis;
            int maxVertices = cellCount;
            int maxIndices = cellCount * 6;

            particleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, sizeof(float) * 4);
            velocityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCount, sizeof(float) * 4);
            cellVertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(int));
            vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxVertices, sizeof(float) * 3);
            normalBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxVertices, sizeof(float) * 3);
            indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxIndices, sizeof(uint));
            barycentricBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxIndices, sizeof(float) * 3);
            counterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(uint));
            argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 4, sizeof(uint));

            sdfTexture = CreateSdfTexture(gridResolution);

            ConfigureComputeBindings();
            ConfigureVisualEffect();

            Dispatch(kernelInitParticles, Mathf.CeilToInt(particleCount / (float)THREADS_1D), 1, 1);
        }

        private void ConfigureComputeBindings()
        {
            if (pipelineCompute == null)
            {
                return;
            }

            pipelineCompute.SetBuffer(kernelInitParticles, "_Particles", particleBuffer);
            pipelineCompute.SetBuffer(kernelIntegrateParticles, "_Particles", particleBuffer);
            pipelineCompute.SetBuffer(kernelIntegrateParticles, "_ParticleVelocities", velocityBuffer);
            pipelineCompute.SetBuffer(kernelInitParticles, "_ParticleVelocities", velocityBuffer);
            pipelineCompute.SetBuffer(kernelStampParticles, "_Particles", particleBuffer);

            pipelineCompute.SetBuffer(kernelClearCells, "_CellVertexIndices", cellVertexBuffer);
            pipelineCompute.SetBuffer(kernelClearCells, "_Counters", counterBuffer);
            pipelineCompute.SetBuffer(kernelBuildVertices, "_CellVertexIndices", cellVertexBuffer);
            pipelineCompute.SetBuffer(kernelBuildVertices, "_VertexBuffer", vertexBuffer);
            pipelineCompute.SetBuffer(kernelBuildVertices, "_NormalBuffer", normalBuffer);
            pipelineCompute.SetBuffer(kernelBuildVertices, "_Counters", counterBuffer);
            pipelineCompute.SetBuffer(kernelBuildIndices, "_CellVertexIndices", cellVertexBuffer);
            pipelineCompute.SetBuffer(kernelBuildIndices, "_Counters", counterBuffer);
            pipelineCompute.SetBuffer(kernelBuildIndices, "_IndexBuffer", indexBuffer);
            pipelineCompute.SetBuffer(kernelBuildIndices, "_BarycentricBuffer", barycentricBuffer);

            pipelineCompute.SetTexture(kernelClearSdf, "_SdfVolumeRW", sdfTexture);
            pipelineCompute.SetTexture(kernelStampParticles, "_SdfVolumeRW", sdfTexture);
            pipelineCompute.SetTexture(kernelBuildVertices, "_SdfVolume", sdfTexture);
            pipelineCompute.SetTexture(kernelBuildIndices, "_SdfVolume", sdfTexture);
        }

        private void ConfigureVisualEffect()
        {
            if (visualEffect == null || particleBuffer == null)
            {
                return;
            }

            visualEffect.SetGraphicsBuffer(ParticleBufferId, particleBuffer);
            visualEffect.SetInt(ParticleCountId, particleCount);
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
            velocityBuffer?.Dispose();
            cellVertexBuffer?.Dispose();
            vertexBuffer?.Dispose();
            normalBuffer?.Dispose();
            indexBuffer?.Dispose();
            barycentricBuffer?.Dispose();
            counterBuffer?.Dispose();
            argsBuffer?.Dispose();

            particleBuffer = null;
            velocityBuffer = null;
            cellVertexBuffer = null;
            vertexBuffer = null;
            normalBuffer = null;
            indexBuffer = null;
            barycentricBuffer = null;
            counterBuffer = null;
            argsBuffer = null;

            if (sdfTexture != null)
            {
                sdfTexture.Release();
                DestroyImmediate(sdfTexture);
                sdfTexture = null;
            }
        }

        private void UpdateParticles(float deltaTime)
        {
            if (pipelineCompute == null)
            {
                return;
            }

            PushCommonParams(deltaTime);

            pipelineCompute.SetBuffer(kernelIntegrateParticles, "_Particles", particleBuffer);
            pipelineCompute.SetBuffer(kernelIntegrateParticles, "_ParticleVelocities", velocityBuffer);
            Dispatch(kernelIntegrateParticles, Mathf.CeilToInt(particleCount / (float)THREADS_1D), 1, 1);
        }

        private void UpdateSdfAndMesh(float deltaTime)
        {
            if (pipelineCompute == null)
            {
                return;
            }

            PushCommonParams(deltaTime);

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

            drawArgs[0] = indexCount;
            drawArgs[1] = 1;
            drawArgs[2] = 0;
            drawArgs[3] = 0;
            argsBuffer.SetData(drawArgs);
        }

        private void IssueDrawCall()
        {
            if (surfaceMaterial == null || vertexBuffer == null || indexBuffer == null)
            {
                return;
            }

            if (drawArgs[0] == 0)
            {
                return;
            }

            Vector3 halfBounds = boundsSize * 0.5f;
            Vector3 center = transform.position;
            drawBounds = new Bounds(center, boundsSize + Vector3.one);

            surfaceMaterial.SetBuffer("_VertexBuffer", vertexBuffer);
            surfaceMaterial.SetBuffer("_NormalBuffer", normalBuffer);
            surfaceMaterial.SetBuffer("_IndexBuffer", indexBuffer);
            surfaceMaterial.SetBuffer("_BarycentricBuffer", barycentricBuffer);
            surfaceMaterial.SetColor("_BaseColor", surfaceColor);
            surfaceMaterial.SetColor("_WireColor", wireColor);
            surfaceMaterial.SetFloat("_WireThickness", wireThickness);
            surfaceMaterial.SetVector("_BoundsCenter", center);
            surfaceMaterial.SetVector("_BoundsExtent", halfBounds);

            Graphics.DrawProceduralIndirect(surfaceMaterial, drawBounds, MeshTopology.Triangles, argsBuffer, 0, null, null,
                ShadowCastingMode.On, true, gameObject.layer);
        }

        private void UpdateSliceDebug()
        {
            if (sliceMaterial == null || sdfTexture == null)
            {
                return;
            }

            sliceMaterial.SetTexture(SdfTextureId, sdfTexture);
            sliceMaterial.SetFloat("_SliceDepth", debugSliceDepth);
            sliceMaterial.SetInt("_SliceAxis", debugSliceAxis);
        }

        private void PushCommonParams(float deltaTime)
        {
            if (pipelineCompute == null)
            {
                return;
            }

            int cellsPerAxis = Mathf.Max(1, gridResolution - 1);
            int cellCount = cellsPerAxis * cellsPerAxis * cellsPerAxis;
            Vector3 boundsMin = transform.position - boundsSize * 0.5f;
            Vector3 boundsExtent = boundsSize;

            pipelineCompute.SetInt("_ParticleCount", particleCount);
            pipelineCompute.SetInt("_GridResolution", gridResolution);
            pipelineCompute.SetInt("_CellResolution", cellCount);
            pipelineCompute.SetVector("_BoundsMin", new Vector4(boundsMin.x, boundsMin.y, boundsMin.z, 0f));
            pipelineCompute.SetVector("_BoundsSize", new Vector4(boundsExtent.x, boundsExtent.y, boundsExtent.z, 0f));
            pipelineCompute.SetFloat("_VoxelSize", boundsSize.x / gridResolution);
            pipelineCompute.SetFloat("_ParticleRadius", particleRadius);
            pipelineCompute.SetFloat("_IsoValue", isoValue);
            pipelineCompute.SetFloat("_DeltaTime", deltaTime);
            pipelineCompute.SetFloat("_Time", Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
            pipelineCompute.SetFloat("_NoiseFrequency", noiseFrequency);
            pipelineCompute.SetFloat("_NoiseStrength", noiseStrength);
            pipelineCompute.SetFloat("_VelocityDamping", velocityDamping);
            pipelineCompute.SetFloat("_SdfFar", sdfFar);
        }

        private void Dispatch(int kernel, int groupsX, int groupsY, int groupsZ)
        {
            pipelineCompute.Dispatch(kernel, groupsX, groupsY, groupsZ);
        }
    }
}
