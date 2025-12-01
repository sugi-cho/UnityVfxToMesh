using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VfxToMesh
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class DepthToSdfVolume : SdfVolumeSource
    {
        public enum SourceMode
        {
            Camera,
            Manual
        }

        private const int THREADS_3D = 8;
        private const float AxisAlignmentThreshold = 0.999f;

        [Header("Compute Assets")]
        [SerializeField] private ComputeShader depthToSdfCompute;

        [Header("Source")]
        [SerializeField] private SourceMode sourceMode = SourceMode.Camera;
        [SerializeField] private Camera depthCamera;
        [SerializeField] private Vector2Int cameraTextureSize = new(256, 256);
        [SerializeField] private Vector2 cameraViewSizeOverride;
        [SerializeField] private Texture manualDepthTexture;
        [SerializeField] private Transform manualViewTransform;
        [SerializeField] private Vector2 manualViewSize = Vector2.one * 2f;
        [SerializeField] private float manualNearClip = 0f;
        [SerializeField] private float manualFarClip = 5f;

        [Header("Volume")]
        [SerializeField, Range(32, 192)] private int gridResolution = 96;
        [SerializeField] private Vector3 boundsSize = Vector3.one * 3f;
        [SerializeField] private float isoValue = 0f;
        [SerializeField] private float sdfFar = 5f;

        [Header("Runtime")]
        [SerializeField] private bool allowUpdateInEditMode = true;

        private RenderTexture sdfTexture;
        private RenderTexture colorTexture;
        private RenderTexture cameraDepthTarget;
        private int bakeKernel = -1;

        private bool KernelsReady => depthToSdfCompute != null && bakeKernel >= 0;

        private uint cachedVersion;
        private SdfDefinition currentDefinition;
        private bool hasDefinition;

        public SourceMode Source => sourceMode;

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
            CacheKernel();
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
                return; // Definition 側で表示
            }

            var def = BuildLocalDefinition();
            DrawBoundsGizmo(def);
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
            cameraTextureSize = new Vector2Int(
                Mathf.Max(1, cameraTextureSize.x),
                Mathf.Max(1, cameraTextureSize.y));

            CacheKernel();
        }

        private void Update()
        {
            if (!ShouldUpdate() || !KernelsReady)
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

            if (BakeDepthSdf(resolvedDefinition))
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
            Color fill = new Color(0.6f, 0.6f, 1f, 0.05f);
            Color line = new Color(0.6f, 0.6f, 1f, 0.8f);
            Gizmos.color = fill;
            Gizmos.DrawCube(def.BoundsCenter, def.BoundsSize);
            Gizmos.color = line;
            Gizmos.DrawWireCube(def.BoundsCenter, def.BoundsSize);
        }

        private void CacheKernel()
        {
            bakeKernel = depthToSdfCompute != null ? depthToSdfCompute.FindKernel("BakeDepthSdf") : -1;
        }

        private bool EnsureResources(in SdfDefinition definition)
        {
            if (!KernelsReady)
            {
                return false;
            }

            if (sdfTexture == null || sdfTexture.width != definition.GridResolution)
            {
                ReleaseTexture(ref sdfTexture);
                sdfTexture = CreateVolumeTexture(definition.GridResolution, RenderTextureFormat.RFloat, "DepthSdf" + name);
            }

            if (colorTexture == null || colorTexture.width != definition.GridResolution)
            {
                ReleaseTexture(ref colorTexture);
                colorTexture = CreateVolumeTexture(definition.GridResolution, RenderTextureFormat.ARGBFloat, "DepthColor" + name);
            }

            if (sdfTexture == null || colorTexture == null)
            {
                return false;
            }

            depthToSdfCompute.SetTexture(bakeKernel, "_SdfVolumeRW", sdfTexture);
            return true;
        }

        private bool BakeDepthSdf(in SdfDefinition definition)
        {
            if (!PrepareViewData(out var origin, out var right, out var up, out var forward,
                out var viewSize, out var nearClip, out var farClip, out var axis, out var depthTex))
            {
                return false;
            }

            depthToSdfCompute.SetInt("_GridResolution", definition.GridResolution);
            depthToSdfCompute.SetFloat("_VoxelSize", definition.BoundsSize.x / Mathf.Max(definition.GridResolution, 1));
            Vector3 boundsMin = definition.BoundsMin;
            depthToSdfCompute.SetVector("_BoundsMin", new Vector4(boundsMin.x, boundsMin.y, boundsMin.z, 0f));
            depthToSdfCompute.SetVector("_BoundsSize", new Vector4(definition.BoundsSize.x, definition.BoundsSize.y, definition.BoundsSize.z, 0f));
            depthToSdfCompute.SetFloat("_SdfFar", definition.SdfFar * definition.DistanceScale);
            depthToSdfCompute.SetFloat("_DistanceScale", definition.DistanceScale);
            depthToSdfCompute.SetVector("_ViewOrigin", new Vector4(origin.x, origin.y, origin.z, 0f));
            depthToSdfCompute.SetVector("_ViewRight", new Vector4(right.x, right.y, right.z, 0f));
            depthToSdfCompute.SetVector("_ViewUp", new Vector4(up.x, up.y, up.z, 0f));
            depthToSdfCompute.SetVector("_ViewForward", new Vector4(forward.x, forward.y, forward.z, 0f));
            depthToSdfCompute.SetVector("_ViewSize", new Vector4(viewSize.x, viewSize.y, 0f, 0f));
            depthToSdfCompute.SetFloat("_ViewNearClip", nearClip);
            depthToSdfCompute.SetFloat("_ViewFarClip", farClip);
            depthToSdfCompute.SetInt("_ViewAxis", (int)axis);

            depthToSdfCompute.SetTexture(bakeKernel, "_DepthTexture", depthTex);

            int group3d = Mathf.CeilToInt(definition.GridResolution / (float)THREADS_3D);
            depthToSdfCompute.Dispatch(bakeKernel, group3d, group3d, group3d);
            return true;
        }

        private bool PrepareViewData(out Vector3 origin, out Vector3 right, out Vector3 up, out Vector3 forward,
            out Vector2 size, out float nearClip, out float farClip, out uint axis, out Texture depthTex)
        {
            origin = Vector3.zero;
            right = Vector3.right;
            up = Vector3.up;
            forward = Vector3.forward;
            size = Vector2.zero;
            nearClip = 0f;
            farClip = 0f;
            axis = 0;
            depthTex = null;

            if (sourceMode == SourceMode.Camera)
            {
                if (depthCamera == null)
                {
                    return false;
                }

                depthTex = EnsureCameraDepthTexture(depthCamera);
                if (depthTex == null)
                {
                    return false;
                }

                Transform t = depthCamera.transform;
                origin = t.position;
                right = t.right.normalized;
                up = t.up.normalized;
                forward = t.forward.normalized;
                nearClip = depthCamera.nearClipPlane;
                farClip = depthCamera.farClipPlane;
                size = ComputeCameraViewSize(depthCamera, cameraViewSizeOverride);
            }
            else
            {
                if (manualViewTransform == null || manualDepthTexture == null)
                {
                    return false;
                }

                depthTex = manualDepthTexture;
                Transform t = manualViewTransform;
                origin = t.position;
                right = t.right.normalized;
                up = t.up.normalized;
                forward = t.forward.normalized;
                nearClip = manualNearClip;
                farClip = manualFarClip;
                size = manualViewSize;
            }

            if (size.x <= 0f || size.y <= 0f || farClip <= nearClip)
            {
                return false;
            }

            if (!TryGetPrincipalAxis(forward, out axis))
            {
                Debug.LogWarning("Depth view is not aligned to a primary axis; snap the camera to 90-degree increments.", this);
                return false;
            }

            return true;
        }

        private RenderTexture EnsureCameraDepthTexture(Camera camera)
        {
            Vector2Int size = new(
                Mathf.Max(1, cameraTextureSize.x),
                Mathf.Max(1, cameraTextureSize.y));

            if (cameraDepthTarget == null || cameraDepthTarget.width != size.x || cameraDepthTarget.height != size.y)
            {
                ReleaseTexture(ref cameraDepthTarget);
                cameraDepthTarget = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.Depth)
                {
                    dimension = TextureDimension.Tex2D,
                    enableRandomWrite = false,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name = "DepthViewTarget" + name
                };
                cameraDepthTarget.Create();
            }

            if (camera.targetTexture != cameraDepthTarget)
            {
                camera.targetTexture = cameraDepthTarget;
            }

            camera.depthTextureMode |= DepthTextureMode.Depth;

            return cameraDepthTarget;
        }

        private Vector2 ComputeCameraViewSize(Camera camera, Vector2 overrideSize)
        {
            if (overrideSize.x > 0f && overrideSize.y > 0f)
            {
                return overrideSize;
            }

            if (!camera.orthographic)
            {
                return Vector2.zero;
            }

            float height = camera.orthographicSize * 2f;
            return new Vector2(height * camera.aspect, height);
        }

        private bool TryGetPrincipalAxis(Vector3 direction, out uint axis)
        {
            Vector3 normalized = direction.normalized;
            float absX = Mathf.Abs(normalized.x);
            float absY = Mathf.Abs(normalized.y);
            float absZ = Mathf.Abs(normalized.z);

            axis = 0;
            float max = absX;
            if (absY > max)
            {
                axis = 1;
                max = absY;
            }
            if (absZ > max)
            {
                axis = 2;
                max = absZ;
            }

            return max >= AxisAlignmentThreshold;
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

        private void ReleaseResources()
        {
            ReleaseTexture(ref sdfTexture);
            ReleaseTexture(ref colorTexture);
            if (depthCamera != null && depthCamera.targetTexture == cameraDepthTarget)
            {
                depthCamera.targetTexture = null;
            }
            ReleaseTexture(ref cameraDepthTarget);
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
