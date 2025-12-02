using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VfxToMesh
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SdfToMesh : MonoBehaviour
    {
        [Header("Compute Assets")]
        [SerializeField] private ComputeShader meshCompute = default!;
        [SerializeField] private SdfVolumeSource sdfSource = default!;

        [Header("Rendering")]
        [SerializeField] private bool allowUpdateInEditMode = true;
        [SerializeField] private List<MeshFilter> targetMeshes = new List<MeshFilter>();

        private Mesh generatedMesh;
        private GraphicsBuffer cellVertexBuffer;
        private GraphicsBuffer counterBuffer;
        private GraphicsBuffer meshPositionBuffer;
        private GraphicsBuffer meshNormalBuffer;
        private GraphicsBuffer meshColorBuffer;
        private GraphicsBuffer meshIndexBuffer;
        private int meshVertexCapacity;
        private int meshIndexCapacity;
        private SubMeshDescriptor subMeshDescriptor = new SubMeshDescriptor(0, 0, MeshTopology.Triangles);

        private int kernelClearCells = -1;
        private int kernelBuildVertices = -1;
        private int kernelBuildIndices = -1;

        private readonly uint[] counterReadback = new uint[2];

        private const int THREADS_1D = 256;
        private const int THREADS_3D = 8;

        public SdfVolumeSource SdfSource
        {
            get => sdfSource;
            set => sdfSource = value;
        }

        private MeshFilter PrimaryMeshFilter => targetMeshes.Count > 0 ? targetMeshes[0] : null;

#if UNITY_EDITOR
        public bool CanCaptureMesh =>
            generatedMesh != null &&
            meshPositionBuffer != null &&
            meshNormalBuffer != null &&
            meshColorBuffer != null &&
            meshIndexBuffer != null &&
            counterBuffer != null;
#endif

        private void Awake()
        {
            ValidateTargetMeshes();
            CacheKernelIds();
        }

        private void OnValidate()
        {
            ValidateTargetMeshes();
            CacheKernelIds();
        }

        private void OnDisable()
        {
            ReleaseResources();
        }

        private void Update()
        {
            if (!ShouldUpdate() || meshCompute == null || sdfSource == null)
            {
                return;
            }

            if (!sdfSource.TryGetSdfVolume(out var volume) || !volume.IsValid)
            {
                ResetMeshOutputs();
                return;
            }

            if (!EnsureResources(volume))
            {
                return;
            }

            ConfigureComputeBindings(volume);
            BuildMesh(volume);
            UpdateRendererProperties(volume);
        }

        private bool ShouldUpdate()
        {
            return Application.isPlaying || allowUpdateInEditMode;
        }

        private void CacheKernelIds()
        {
            if (meshCompute == null)
            {
                kernelClearCells = -1;
                kernelBuildVertices = -1;
                kernelBuildIndices = -1;
                return;
            }

            kernelClearCells = meshCompute.FindKernel("ClearCells");
            kernelBuildVertices = meshCompute.FindKernel("BuildSurfaceVertices");
            kernelBuildIndices = meshCompute.FindKernel("BuildSurfaceIndices");
        }

        private bool EnsureResources(in SdfVolume volume)
        {
            if (meshCompute == null)
            {
                return false;
            }

            int cellCount = volume.CellCount;
            int maxVertices = cellCount;
            int maxIndices = cellCount * 6;

            if (cellVertexBuffer != null &&
                cellVertexBuffer.count == cellCount &&
                meshVertexCapacity == maxVertices &&
                meshIndexCapacity == maxIndices)
            {
                return true;
            }

            ReleaseResources();
            AllocateResources(cellCount, maxVertices, maxIndices);
            return cellVertexBuffer != null && generatedMesh != null;
        }

        private void AllocateResources(int cellCount, int maxVertices, int maxIndices)
        {
            cellVertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(int));
            counterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, sizeof(uint));

            EnsureMeshBuffers(maxVertices, maxIndices);

            if (generatedMesh == null)
            {
                cellVertexBuffer?.Dispose();
                counterBuffer?.Dispose();
                cellVertexBuffer = null;
                counterBuffer = null;
            }
        }

        private void EnsureMeshBuffers(int vertexCapacity, int indexCapacity)
        {
            ValidateTargetMeshes();
            var primaryFilter = PrimaryMeshFilter;
            if (primaryFilter == null)
            {
                Debug.LogWarning($"{nameof(SdfToMesh)} on {name} has no MeshFilter assigned in targetMeshes. Skipping mesh allocation.", this);
                return;
            }

            if (generatedMesh == null)
            {
                generatedMesh = new Mesh { name = "VfxToMesh.GeneratedMesh" };
                generatedMesh.hideFlags = HideFlags.HideAndDontSave;
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
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 2));
            generatedMesh.SetIndexBufferParams(indexCapacity, IndexFormat.UInt32);

            subMeshDescriptor.indexStart = 0;
            subMeshDescriptor.indexCount = 0;
            subMeshDescriptor.topology = MeshTopology.Triangles;
            generatedMesh.SetSubMesh(0, subMeshDescriptor,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            meshPositionBuffer = generatedMesh.GetVertexBuffer(0);
            meshNormalBuffer = generatedMesh.GetVertexBuffer(1);
            meshColorBuffer = generatedMesh.GetVertexBuffer(2);
            meshIndexBuffer = generatedMesh.GetIndexBuffer();

            ApplyMeshToTargets();
        }

        private void ConfigureComputeBindings(in SdfVolume volume)
        {
            if (meshCompute == null ||
                cellVertexBuffer == null ||
                counterBuffer == null ||
                meshPositionBuffer == null ||
                meshNormalBuffer == null ||
                meshColorBuffer == null ||
                meshIndexBuffer == null)
            {
                return;
            }

            meshCompute.SetBuffer(kernelClearCells, "_CellVertexIndices", cellVertexBuffer);
            meshCompute.SetBuffer(kernelClearCells, "_Counters", counterBuffer);

            meshCompute.SetBuffer(kernelBuildVertices, "_CellVertexIndices", cellVertexBuffer);
            meshCompute.SetBuffer(kernelBuildVertices, "_VertexBuffer", meshPositionBuffer);
            meshCompute.SetBuffer(kernelBuildVertices, "_NormalBuffer", meshNormalBuffer);
            meshCompute.SetBuffer(kernelBuildVertices, "_VertexColorBuffer", meshColorBuffer);
            meshCompute.SetBuffer(kernelBuildVertices, "_Counters", counterBuffer);

            meshCompute.SetBuffer(kernelBuildIndices, "_CellVertexIndices", cellVertexBuffer);
            meshCompute.SetBuffer(kernelBuildIndices, "_Counters", counterBuffer);
            meshCompute.SetBuffer(kernelBuildIndices, "_IndexBuffer", meshIndexBuffer);

            meshCompute.SetTexture(kernelBuildVertices, "_SdfVolume", volume.Texture);
            meshCompute.SetTexture(kernelBuildVertices, "_ColorVolume", volume.ColorTexture);
            meshCompute.SetTexture(kernelBuildIndices, "_SdfVolume", volume.Texture);
            meshCompute.SetTexture(kernelBuildIndices, "_ColorVolume", volume.ColorTexture);
        }

        private void BuildMesh(in SdfVolume volume)
        {
            SdfShaderParams.Push(meshCompute, volume);

            int group3d = Mathf.CeilToInt(volume.GridResolution / (float)THREADS_3D);
            int cellsPerDispatch = Mathf.CeilToInt(volume.CellCount / (float)THREADS_1D);

            Dispatch(kernelClearCells, cellsPerDispatch, 1, 1);
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

        private void UpdateRendererProperties(in SdfVolume volume)
        {
            if (generatedMesh == null)
            {
                ApplyMeshToTargets();
                return;
            }

            generatedMesh.bounds = new Bounds(Vector3.zero, volume.BoundsSize + Vector3.one);
            ApplyMeshToTargets();
        }

        private void ValidateTargetMeshes()
        {
            targetMeshes ??= new List<MeshFilter>();

            for (int i = targetMeshes.Count - 1; i >= 0; --i)
            {
                if (targetMeshes[i] == null)
                {
                    targetMeshes.RemoveAt(i);
                }
            }
        }

        private void ApplyMeshToTargets()
        {
            ValidateTargetMeshes();
            if (generatedMesh == null)
            {
                foreach (var filter in targetMeshes)
                {
                    if (filter == null)
                    {
                        continue;
                    }

                    if (filter.sharedMesh != null)
                    {
                        filter.sharedMesh = null;
                    }
                }
                return;
            }

            foreach (var filter in targetMeshes)
            {
                if (filter == null)
                {
                    continue;
                }

                if (filter.sharedMesh != generatedMesh)
                {
                    filter.sharedMesh = generatedMesh;
                }
            }
        }

        private void ResetMeshOutputs()
        {
            subMeshDescriptor.indexCount = 0;
            if (generatedMesh != null)
            {
                generatedMesh.SetSubMesh(0, subMeshDescriptor,
                    MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }

            ApplyMeshToTargets();
        }

        private void ReleaseResources()
        {
            cellVertexBuffer?.Dispose();
            counterBuffer?.Dispose();

            cellVertexBuffer = null;
            counterBuffer = null;
            meshPositionBuffer = null;
            meshNormalBuffer = null;
            meshColorBuffer = null;
            meshIndexBuffer = null;
            meshVertexCapacity = 0;
            meshIndexCapacity = 0;
            subMeshDescriptor.indexCount = 0;

            if (generatedMesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(generatedMesh);
                }
                else
                {
                    DestroyImmediate(generatedMesh);
                }

                generatedMesh = null;
            }

            ApplyMeshToTargets();
        }

        private void Dispatch(int kernel, int groupsX, int groupsY, int groupsZ)
        {
            if (meshCompute == null || kernel < 0)
            {
                return;
            }

            meshCompute.Dispatch(kernel, groupsX, groupsY, groupsZ);
        }
#if UNITY_EDITOR

        public bool TryCaptureMesh(out Mesh capturedMesh, out CaptureStats stats)
        {
            capturedMesh = null;
            stats = default;
            if (!CanCaptureMesh)
            {
                return false;
            }

            counterBuffer.GetData(counterReadback);
            int rawVertexCount = Mathf.Clamp((int)counterReadback[0], 0, meshVertexCapacity);
            int rawIndexCount = Mathf.Clamp((int)counterReadback[1], 0, meshIndexCapacity);
            rawIndexCount -= rawIndexCount % 3;

            if (rawVertexCount == 0 || rawIndexCount == 0)
            {
                return false;
            }

            var positions = new Vector3[rawVertexCount];
            var normals = new Vector3[rawVertexCount];
            var colors = new Color[rawVertexCount];
            var indices = new uint[rawIndexCount];

            meshPositionBuffer.GetData(positions, 0, 0, rawVertexCount);
            meshNormalBuffer.GetData(normals, 0, 0, rawVertexCount);
            meshColorBuffer.GetData(colors, 0, 0, rawVertexCount);
            meshIndexBuffer.GetData(indices, 0, 0, rawIndexCount);

            var remap = new Dictionary<uint, int>(rawVertexCount);
            var trimmedPositions = new List<Vector3>();
            var trimmedNormals = new List<Vector3>();
            var trimmedColors = new List<Color>();
            var trimmedIndices = new int[rawIndexCount];

            for (int i = 0; i < rawIndexCount; ++i)
            {
                uint originalIndex = indices[i];
                if (originalIndex >= positions.Length)
                {
                    Debug.LogWarning($"Index {originalIndex} is out of range for {positions.Length} vertices. Capture aborted.", this);
                    return false;
                }

                if (!remap.TryGetValue(originalIndex, out int newIndex))
                {
                    newIndex = remap.Count;
                    remap.Add(originalIndex, newIndex);
                    trimmedPositions.Add(positions[originalIndex]);
                    trimmedNormals.Add(normals[originalIndex]);
                    trimmedColors.Add(colors[originalIndex]);
                }

                trimmedIndices[i] = newIndex;
            }

            stats = new CaptureStats(rawVertexCount, rawIndexCount, trimmedPositions.Count);

            var mesh = new Mesh { name = $"{generatedMesh.name}.Captured" };
            mesh.indexFormat = trimmedPositions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(trimmedPositions);
            mesh.SetNormals(trimmedNormals);
            mesh.SetColors(trimmedColors);
            mesh.SetIndices(trimmedIndices, MeshTopology.Triangles, 0, true);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            capturedMesh = mesh;
            return true;
        }

        public readonly struct CaptureStats
        {
            public readonly int RawVertexCount;
            public readonly int RawIndexCount;
            public readonly int UsedVertexCount;

            public CaptureStats(int rawVertexCount, int rawIndexCount, int usedVertexCount)
            {
                RawVertexCount = rawVertexCount;
                RawIndexCount = rawIndexCount;
                UsedVertexCount = usedVertexCount;
            }
        }
#endif
    }
}
