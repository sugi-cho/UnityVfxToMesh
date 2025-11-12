using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.VFX.Utility;

namespace VfxToMesh
{
    [AddComponentMenu("VFX Binders/VfxToMesh Sdf Volume")]
    [VFXBinder("VfxToMesh/Sdf Volume")]
    public class SdfVolumeBinder : VFXBinderBase
    {
        [Header("Targets")]
        [SerializeField] private SdfVolumeSource sdfSource;

        [Header("Required Exposed Names")]
        [SerializeField] private string sdfTextureProperty = "SdfVolumeTexture";
        [SerializeField] private string colorTextureProperty = "SdfColorTexture";
        [SerializeField] private string boundsMinProperty = "SdfBoundsMin";
        [SerializeField] private string boundsSizeProperty = "SdfBoundsSize";
        [SerializeField] private string worldToLocalProperty = "SdfWorldToLocal";

        [Header("Optional Exposed Names")]
        [SerializeField] private string boundsCenterProperty = "SdfBoundsCenter";
        [SerializeField] private string localToWorldProperty = "SdfLocalToWorld";
        [SerializeField] private string voxelSizeProperty = "SdfVoxelSize";
        [SerializeField] private string isoValueProperty = "SdfIsoValue";
        [SerializeField] private string sdfFarProperty = "SdfFar";
        [SerializeField] private string gridResolutionProperty = "SdfGridResolution";
        [SerializeField] private string cellResolutionProperty = "SdfCellResolution";

        private readonly HashSet<string> reportedMissingRequired = new();
        private uint cachedVolumeVersion = uint.MaxValue;

        public override bool IsValid(VisualEffect component)
        {
            if (sdfSource == null || component == null)
            {
                return false;
            }

            bool valid = true;
            valid &= EnsureRequiredProperty(component, sdfTextureProperty, c => c.HasTexture(sdfTextureProperty), "Texture3D");
            valid &= EnsureRequiredProperty(component, colorTextureProperty, c => c.HasTexture(colorTextureProperty), "Texture3D");
            valid &= EnsureRequiredProperty(component, boundsMinProperty, c => c.HasVector3(boundsMinProperty), "Vector3");
            valid &= EnsureRequiredProperty(component, boundsSizeProperty, c => c.HasVector3(boundsSizeProperty), "Vector3");
            valid &= EnsureRequiredProperty(component, worldToLocalProperty, c => c.HasMatrix4x4(worldToLocalProperty), "Matrix4x4");

            return valid;
        }

        public override void UpdateBinding(VisualEffect component)
        {
            if (sdfSource == null || component == null)
            {
                return;
            }

            if (!sdfSource.TryGetSdfVolume(out var volume) || !volume.IsValid)
            {
                cachedVolumeVersion = uint.MaxValue;
                ClearBinding(component);
                return;
            }

            if (cachedVolumeVersion == sdfSource.Version)
            {
                return;
            }

            cachedVolumeVersion = sdfSource.Version;
            SetRequiredBindings(component, volume);
            SetOptionalBindings(component, volume);
        }

        private void SetRequiredBindings(VisualEffect component, in SdfVolume volume)
        {
            component.SetTexture(sdfTextureProperty, volume.Texture);
            component.SetTexture(colorTextureProperty, volume.ColorTexture);
            component.SetVector3(boundsMinProperty, volume.BoundsMin);
            component.SetVector3(boundsSizeProperty, volume.BoundsSize);
            component.SetMatrix4x4(worldToLocalProperty, volume.WorldToLocal);
        }

        private void SetOptionalBindings(VisualEffect component, in SdfVolume volume)
        {
            TrySetVector3(component, boundsCenterProperty, volume.BoundsCenter);
            TrySetMatrix(component, localToWorldProperty, volume.LocalToWorld);
            TrySetFloat(component, voxelSizeProperty, volume.VoxelSize);
            TrySetFloat(component, isoValueProperty, volume.IsoValue);
            TrySetFloat(component, sdfFarProperty, volume.SdfFar);
            TrySetInt(component, gridResolutionProperty, volume.GridResolution);
            TrySetInt(component, cellResolutionProperty, volume.CellCount);
        }

        private void ClearBinding(VisualEffect component)
        {
            component.SetTexture(sdfTextureProperty, null);
            component.SetTexture(colorTextureProperty, null);
        }

        private bool EnsureRequiredProperty(
            VisualEffect component,
            string propertyName,
            System.Func<VisualEffect, bool> hasCheck,
            string propertyType)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                ReportMissingRequired($"{nameof(SdfVolumeBinder)} requires a {propertyType} property name to be set.", propertyName);
                return false;
            }

            if (hasCheck(component))
            {
                return true;
            }

            ReportMissingRequired(
                $"{nameof(SdfVolumeBinder)} needs a {propertyType} parameter named '{propertyName}' on VisualEffect '{component.name}'.",
                propertyName);
            return false;
        }

        private void ReportMissingRequired(string message, string propertyKey)
        {
            string key = string.IsNullOrEmpty(propertyKey) ? message : propertyKey;
            if (reportedMissingRequired.Add(key))
            {
                Debug.LogError(message, this);
            }
        }

        private static void TrySetFloat(VisualEffect component, string propertyName, float value)
        {
            if (!string.IsNullOrEmpty(propertyName) && component.HasFloat(propertyName))
            {
                component.SetFloat(propertyName, value);
            }
        }

        private static void TrySetInt(VisualEffect component, string propertyName, int value)
        {
            if (!string.IsNullOrEmpty(propertyName) && component.HasInt(propertyName))
            {
                component.SetInt(propertyName, value);
            }
        }

        private static void TrySetVector3(VisualEffect component, string propertyName, Vector3 value)
        {
            if (!string.IsNullOrEmpty(propertyName) && component.HasVector3(propertyName))
            {
                component.SetVector3(propertyName, value);
            }
        }

        private static void TrySetMatrix(VisualEffect component, string propertyName, Matrix4x4 value)
        {
            if (!string.IsNullOrEmpty(propertyName) && component.HasMatrix4x4(propertyName))
            {
                component.SetMatrix4x4(propertyName, value);
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (sdfSource == null)
            {
                sdfSource = GetComponentInParent<SdfVolumeSource>();
            }
        }

        public override void Reset()
        {
            base.Reset();
            if (sdfSource == null)
            {
                sdfSource = GetComponentInParent<SdfVolumeSource>();
            }
        }
    }
}
