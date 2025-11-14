using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VfxToMesh
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class DepthToSdfVolume : SdfVolumeSource
    {
        private const int MaxDepthViews = 6;
        private const int ThreadsPerAxis = 8;
        private static readonly string[] DepthTexturePropertyNames =
        {
            "_DepthTexture0",
            "_DepthTexture1",
            "_DepthTexture2",
            "_DepthTexture3",
            "_DepthTexture4",
            "_DepthTexture5",
        };

        [Serializable]
        public sealed class DepthViewDescriptor
        {
            public enum SourceMode
            {
                Manual,
                Camera,
            }

            public bool enabled = true;
            public SourceMode source = SourceMode.Manual;

            [Header("Manual")]
            public Texture depthTexture;
            public Transform viewTransform;
            public Vector2 viewSize = Vector2.one * 2f;
            public float nearClip = 0f;
            public float farClip = 5f;

            [Header("Camera")]
            public Camera depthCamera;
            public Vector2Int cameraTextureSize = new(256, 256);
            public Vector2 cameraViewSizeOverride;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DepthViewData
        {
            public Vector4 origin;
            public Vector4 right;
            public Vector4 up;
            public Vector4 forward;
            public Vector2 size;
            public float nearClip;
            public float farClip;
            public float padding;
        }

        [Header("Compute Assets")]
        [SerializeField] private ComputeShader depthToSdfCompute;

        [Header("Volume")]
        [SerializeField, Range(32, 192)] private int gridResolution = 96;
        [SerializeField] private Vector3 boundsSize = Vector3.one * 3f;
        [SerializeField] private float isoValue = 0f;
        [SerializeField] private float sdfFar = 5f;
        [SerializeField, Tooltip("Depth capture views used to reconstruct the surface.")] private DepthViewDescriptor[] depthViews = Array.Empty<DepthViewDescriptor>();

        [Header("Runtime")]
        [SerializeField] private bool allowUpdateInEditMode = true;

        private RenderTexture sdfTexture;
        private RenderTexture colorTexture;
        private ComputeBuffer depthViewBuffer;
        private int bakeKernel = -1;

        private readonly RenderTexture[] cameraDepthTargets = new RenderTexture[MaxDepthViews];
        private readonly Camera[] cameraOwners = new Camera[MaxDepthViews];

        private readonly DepthViewData[] depthViewData = new DepthViewData[MaxDepthViews];
        private readonly Texture[] depthTextures = new Texture[MaxDepthViews];
        private static Texture2D fallbackDepthTexture;

        private bool KernelsReady => depthToSdfCompute != null && bakeKernel >= 0;

        public override bool TryGetSdfVolume(out SdfVolume volume)
        {
            if (sdfTexture == null || colorTexture == null)
            {
                volume = default;
                return false;
            }

            float distanceScale = ComputeDistanceScale();
            volume = new SdfVolume(
                sdfTexture,
                gridResolution,
                boundsSize,
                transform.position,
                isoValue,
                sdfFar,
                transform.localToWorldMatrix,
                transform.worldToLocalMatrix,
                colorTexture,
                distanceScale);
            return volume.IsValid;
        }

        private void Awake()
        {
            CacheKernel();
        }

        private void OnEnable()
        {
            EnsureResources();
        }

        private void OnDisable()
        {
            ReleaseResources();
        }

        private void OnValidate()
        {
            gridResolution = Mathf.Clamp(gridResolution, 32, 192);
            boundsSize = new Vector3(
                Mathf.Max(0.01f, boundsSize.x),
                Mathf.Max(0.01f, boundsSize.y),
                Mathf.Max(0.01f, boundsSize.z));
            sdfFar = Mathf.Max(0.01f, sdfFar);

            CacheKernel();
        }

        private void Update()
        {
            if (!ShouldUpdate() || !KernelsReady)
            {
                return;
            }

            if (!EnsureResources())
            {
                return;
            }

            if (BakeDepthSdf())
            {
                Version++;
            }
        }

        private bool ShouldUpdate()
        {
            return Application.isPlaying || allowUpdateInEditMode;
        }

        private void CacheKernel()
        {
            bakeKernel = depthToSdfCompute != null ? depthToSdfCompute.FindKernel("BakeDepthSdf") : -1;
        }

        private bool EnsureResources()
        {
            if (!KernelsReady)
            {
                return false;
            }

            if (sdfTexture == null || sdfTexture.width != gridResolution)
            {
                ReleaseTexture(ref sdfTexture);
                sdfTexture = CreateVolumeTexture(gridResolution, RenderTextureFormat.RFloat, "DepthSdf" + name);
            }

            if (colorTexture == null || colorTexture.width != gridResolution)
            {
                ReleaseTexture(ref colorTexture);
                colorTexture = CreateVolumeTexture(gridResolution, RenderTextureFormat.ARGBFloat, "DepthColor" + name);
            }

            if (depthViewBuffer == null)
            {
                depthViewBuffer = new ComputeBuffer(MaxDepthViews, Marshal.SizeOf<DepthViewData>());
            }

            if (sdfTexture == null || colorTexture == null || depthViewBuffer == null)
            {
                return false;
            }

            depthToSdfCompute.SetTexture(bakeKernel, "_SdfVolumeRW", sdfTexture);
            depthToSdfCompute.SetBuffer(bakeKernel, "_DepthViews", depthViewBuffer);
            return true;
        }

        private bool BakeDepthSdf()
        {
            int viewCount = PopulateDepthViews();
            if (viewCount == 0)
            {
                return false;
            }

            Vector3 boundsMin = transform.position - boundsSize * 0.5f;
            float voxelSize = boundsSize.x / Mathf.Max(gridResolution, 1);
            float distanceScale = ComputeDistanceScale();
            float normalizedSdfFar = Mathf.Max(0.01f, sdfFar) * distanceScale;

            depthToSdfCompute.SetInt("_GridResolution", gridResolution);
            depthToSdfCompute.SetFloat("_VoxelSize", voxelSize);
            depthToSdfCompute.SetVector("_BoundsMin", new Vector4(boundsMin.x, boundsMin.y, boundsMin.z, 0f));
            depthToSdfCompute.SetVector("_BoundsSize", new Vector4(boundsSize.x, boundsSize.y, boundsSize.z, 0f));
            depthToSdfCompute.SetFloat("_SdfFar", normalizedSdfFar);
            depthToSdfCompute.SetFloat("_DistanceScale", distanceScale);
            depthToSdfCompute.SetInt("_DepthViewCount", viewCount);

            Texture fallback = EnsureFallbackDepthTexture();
            for (int i = 0; i < MaxDepthViews; ++i)
            {
                Texture depthInput = i < viewCount && depthTextures[i] != null ? depthTextures[i] : fallback;
                depthToSdfCompute.SetTexture(bakeKernel, DepthTexturePropertyNames[i], depthInput);
            }

            int groups = Mathf.CeilToInt(gridResolution / (float)ThreadsPerAxis);
            depthToSdfCompute.Dispatch(bakeKernel, groups, groups, groups);
            return true;
        }

        private int PopulateDepthViews()
        {
            if (depthViewBuffer == null)
            {
                return 0;
            }

            int count = 0;
            if (depthViews != null)
            {
                for (int i = 0; i < depthViews.Length && count < MaxDepthViews; ++i)
                {
                    DepthViewDescriptor source = depthViews[i];
                    if (source == null || !source.enabled)
                    {
                        continue;
                    }

                    Texture depthTexture = null;
                    Transform viewTransform = null;
                    Vector2 size = Vector2.zero;
                    float nearClip = 0f;
                    float farClip = 0f;

                    if (source.source == DepthViewDescriptor.SourceMode.Camera)
                    {
                        Camera depthCamera = source.depthCamera;
                        if (depthCamera == null)
                        {
                            continue;
                        }

                        depthTexture = EnsureCameraDepthTexture(source, depthCamera, count);
                        if (depthTexture == null)
                        {
                            continue;
                        }

                        viewTransform = depthCamera.transform;
                        nearClip = depthCamera.nearClipPlane;
                        farClip = depthCamera.farClipPlane;
                        size = ComputeCameraViewSize(depthCamera, source.cameraViewSizeOverride);
                        if (size.x <= 0f || size.y <= 0f || farClip <= nearClip)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (source.viewTransform == null || source.depthTexture == null)
                        {
                            continue;
                        }

                        depthTexture = source.depthTexture;
                        viewTransform = source.viewTransform;
                        size = new Vector2(Mathf.Max(0.01f, source.viewSize.x), Mathf.Max(0.01f, source.viewSize.y));
                        nearClip = Mathf.Max(0f, source.nearClip);
                        farClip = Mathf.Max(nearClip + 0.01f, source.farClip);
                        if (farClip <= nearClip)
                        {
                            continue;
                        }
                    }

                    Vector3 origin = viewTransform.position;
                    Vector3 right = viewTransform.right.normalized;
                    Vector3 up = viewTransform.up.normalized;
                    Vector3 forward = viewTransform.forward.normalized;

                    depthViewData[count] = new DepthViewData
                    {
                        origin = new Vector4(origin.x, origin.y, origin.z, 0f),
                        right = new Vector4(right.x, right.y, right.z, 0f),
                        up = new Vector4(up.x, up.y, up.z, 0f),
                        forward = new Vector4(forward.x, forward.y, forward.z, 0f),
                        size = size,
                        nearClip = nearClip,
                        farClip = farClip,
                        padding = 0f,
                    };
                    depthTextures[count] = depthTexture;
                    count++;
                }
            }

            for (int i = count; i < MaxDepthViews; ++i)
            {
                depthViewData[i] = default;
                depthTextures[i] = null;
                ReleaseCameraDepthTexture(i);
            }

            depthViewBuffer.SetData(depthViewData);
            return count;
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

        private float ComputeDistanceScale()
        {
            float maxDimension = Mathf.Max(boundsSize.x, boundsSize.y, boundsSize.z);
            return maxDimension > 0f ? 1f / maxDimension : 1f;
        }

        private void ReleaseResources()
        {
            ReleaseTexture(ref sdfTexture);
            ReleaseTexture(ref colorTexture);
            ReleaseCameraDepthTextures();
            if (depthViewBuffer != null)
            {
                depthViewBuffer.Release();
                depthViewBuffer = null;
            }
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

        private Vector2 ComputeCameraViewSize(Camera camera, Vector2 overrideSize)
        {
            if (overrideSize.x > 0f && overrideSize.y > 0f)
            {
                return overrideSize;
            }

            if (camera == null)
            {
                return Vector2.zero;
            }

            if (camera.orthographic)
            {
                float height = camera.orthographicSize * 2f;
                return new Vector2(height * camera.aspect, height);
            }

            return Vector2.zero;
        }

        private RenderTexture EnsureCameraDepthTexture(DepthViewDescriptor descriptor, Camera camera, int slot)
        {
            if (camera == null || descriptor == null || slot < 0 || slot >= MaxDepthViews)
            {
                return null;
            }

            Vector2Int size = new Vector2Int(
                Mathf.Max(1, descriptor.cameraTextureSize.x),
                Mathf.Max(1, descriptor.cameraTextureSize.y));

            RenderTexture current = cameraDepthTargets[slot];
            if (current == null || current.width != size.x || current.height != size.y)
            {
                ReleaseCameraDepthTexture(slot);
                current = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.Depth)
                {
                    dimension = TextureDimension.Tex2D,
                    enableRandomWrite = false,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name = $"DepthViewTarget_{slot}_{name}"
                };
                current.Create();
                cameraDepthTargets[slot] = current;
            }

            if (camera.targetTexture != current)
            {
                camera.targetTexture = current;
            }

            camera.depthTextureMode |= DepthTextureMode.Depth;
            cameraOwners[slot] = camera;
            return current;
        }

        private void ReleaseCameraDepthTextures()
        {
            for (int i = 0; i < MaxDepthViews; ++i)
            {
                ReleaseCameraDepthTexture(i);
            }
        }

        private void ReleaseCameraDepthTexture(int index)
        {
            if (index < 0 || index >= MaxDepthViews)
            {
                return;
            }

            RenderTexture texture = cameraDepthTargets[index];
            if (texture == null)
            {
                cameraOwners[index] = null;
                return;
            }

            Camera owner = cameraOwners[index];
            if (owner != null && owner.targetTexture == texture)
            {
                owner.targetTexture = null;
            }

            ReleaseTexture(ref texture);
            cameraDepthTargets[index] = null;
            cameraOwners[index] = null;
        }

        private static Texture2D EnsureFallbackDepthTexture()
        {
            if (fallbackDepthTexture == null)
            {
                fallbackDepthTexture = new Texture2D(1, 1, TextureFormat.RFloat, false, true)
                {
                    name = "DepthViewFallback"
                };
                fallbackDepthTexture.SetPixel(0, 0, Color.white);
                fallbackDepthTexture.Apply(false, true);
            }

            return fallbackDepthTexture;
        }
    }
}
