using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FigmaUiImporter.Editor
{
    public enum FigmaSliceMode
    {
        ExportSettingsThenAutoSlice,
        ExportMarkedNodesOnly,
        AutoSliceVisibleLayers,
        FlattenSelectedFrameAsSingleImage
    }

    public enum FigmaContainerSliceStrategy
    {
        Auto,
        StrictExportSettings,
        ExpandContainers
    }

    public sealed class FigmaImportSettings
    {
        public string FileKey;
        public string AccessToken;
        public string OutputFolder = "Assets/FigmaImports";
        public Canvas TargetCanvas;
        public FigmaSliceMode SliceMode = FigmaSliceMode.ExportSettingsThenAutoSlice;
        public FigmaContainerSliceStrategy ContainerSliceStrategy = FigmaContainerSliceStrategy.Auto;
        public float ImageScale = 1f;
        public bool CreateReference = true;
        public float ReferenceAlpha = 0.5f;
        public bool DisableRaycastTarget = true;
        public bool OverwriteExisting;
        public bool SavePrefab;
        public string PrefabFolder = "Assets/FigmaImports/Prefabs";
        public bool IgnoreSslCertificateErrors;
        public bool UseLoadedNodeTree = true;
        public TextureImporterCompressionMode CompressionMode = TextureImporterCompressionMode.None;
    }

    public enum TextureImporterCompressionMode
    {
        None,
        Normal
    }

    public sealed class FigmaNodeInfo
    {
        public string Id;
        public string Name;
        public string Type;
        public string LayoutMode;
        public bool Visible = true;
        public Rect AbsoluteBounds;
        public bool HasAbsoluteBounds;
        public Rect AbsoluteRenderBounds;
        public bool HasAbsoluteRenderBounds;
        public bool HasExportSettings;
        public bool HasVisibleFills;
        public bool HasVisibleStrokes;
        public bool HasEffects;
        public bool HasTextCharacters;
        public readonly List<FigmaNodeInfo> Children = new List<FigmaNodeInfo>();
        public FigmaNodeInfo Parent;

        public bool HasUsableBounds
        {
            get { return HasAbsoluteBounds && AbsoluteBounds.width > 0.01f && AbsoluteBounds.height > 0.01f; }
        }

        public Rect SliceBounds
        {
            get { return HasAbsoluteRenderBounds ? AbsoluteRenderBounds : AbsoluteBounds; }
        }

        public bool HasUsableSliceBounds
        {
            get { return SliceBounds.width > 0.01f && SliceBounds.height > 0.01f; }
        }

        public bool IsPage
        {
            get { return string.Equals(Type, "CANVAS", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsFrameLike
        {
            get
            {
                return string.Equals(Type, "FRAME", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Type, "COMPONENT", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Type, "INSTANCE", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool IsAutoLayout
        {
            get
            {
                return string.Equals(LayoutMode, "HORIZONTAL", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(LayoutMode, "VERTICAL", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string DisplayName
        {
            get
            {
                string name = string.IsNullOrEmpty(Name) ? "(未命名)" : Name;
                string id = string.IsNullOrEmpty(Id) ? "-" : Id;
                string type = string.IsNullOrEmpty(Type) ? "UNKNOWN" : Type;
                return string.Format("{0}  [{1}]  {2}", name, type, id);
            }
        }
    }

    public sealed class ImportedLayerInfo
    {
        public string NodeId;
        public string NodeName;
        public string NodeType;
        public string AssetPath;
        public Rect FigmaBounds;
        public Vector2 AnchoredPosition;
        public Vector2 SizeDelta;
        public int SiblingIndex;
        public string UnityObjectPath;
        public readonly List<string> ClearChildNodeIds = new List<string>();
    }

    internal sealed class FigmaImportResult
    {
        public string OutputFolder;
        public string ManifestPath;
        public string DiagnosticReportPath;
        public string PrefabPath;
        public GameObject RootObject;
        public int LayerCount;
    }

    internal sealed class FigmaImportPlan
    {
        public readonly List<FigmaNodeInfo> Nodes = new List<FigmaNodeInfo>();
        public readonly List<FigmaImportDiagnosticEntry> Diagnostics = new List<FigmaImportDiagnosticEntry>();
    }

    internal sealed class FigmaImportDiagnosticEntry
    {
        public string NodeId;
        public string NodeName;
        public string NodeType;
        public string LayoutMode;
        public string Action;
        public string Reason;
    }

    internal sealed class FigmaPluginPackageInfo
    {
        public string PackagePath;
        public string PackageFileName;
        public long PackageSizeBytes;
        public int SchemaVersion;
        public string ExportedAt;
        public string FileKey;
        public float ImageScale;
        public string SelectedNodeId;
        public string SelectedNodeName;
        public string SelectedNodeType;
        public string ContainerSliceStrategy;
        public float RootWidth;
        public float RootHeight;
        public int LayerCount;
        public int AssetCount;
        public bool HasReferenceImage;
    }

    internal sealed class FigmaManifestRebuildData
    {
        public string FileKey;
        public float ImageScale = 1f;
        public string ContainerSliceStrategy;
        public string ManifestPath;
        public string OutputFolder;
        public FigmaNodeInfo RootNode;
        public string ReferenceAssetPath;
        public readonly List<ImportedLayerInfo> Layers = new List<ImportedLayerInfo>();
    }

    internal sealed class FigmaImportProgress
    {
        public readonly string Message;
        public readonly float Value;

        public FigmaImportProgress(string message, float value)
        {
            Message = message;
            Value = Mathf.Clamp01(value);
        }
    }
}
