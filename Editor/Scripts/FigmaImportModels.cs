using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static FigmaUiImporter.Editor.FigmaLocalization;

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

    public enum FigmaTextImportMode
    {
        Image,
        TextMeshPro,
        Smart
    }

    /// <summary>背景模糊节点的 Unity 生成模式。</summary>
    public enum FigmaBackgroundBlurMode
    {
        None,
        Baked,
        Dynamic
    }

    public enum ImportedLayerKind
    {
        Image,
        Text
    }

    public sealed class FigmaImportSettings
    {
        public string FileKey;
        public string AccessToken;
        public string OutputFolder = "Assets/FigmaImports";
        public Canvas TargetCanvas;
        public FigmaSliceMode SliceMode = FigmaSliceMode.ExportSettingsThenAutoSlice;
        public FigmaContainerSliceStrategy ContainerSliceStrategy = FigmaContainerSliceStrategy.Auto;
        public FigmaTextImportMode TextImportMode = FigmaTextImportMode.Smart;
        public float ImageScale = 1f;
        public FigmaBackgroundBlurMode BackgroundBlurMode = FigmaBackgroundBlurMode.None;
        public bool CreateReference = true;
        public float ReferenceAlpha = 0.5f;
        public bool DisableRaycastTarget = true;
        public bool OverwriteExisting;
        public bool SavePrefab;
        public string PrefabFolder = "Assets/FigmaImports/Prefabs";
        public bool IgnoreSslCertificateErrors;
        public bool UseLoadedNodeTree = true;
        public TextureImporterCompressionMode CompressionMode = TextureImporterCompressionMode.None;
        public FigmaFontMappingAsset FontMapping;
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
        /// <summary>Figma 节点是否裁剪超出边界的内容；GROUP 等非 Frame 类型通常不裁剪。</summary>
        public bool ClipsContent = true;
        public Rect AbsoluteBounds;
        public bool HasAbsoluteBounds;
        public Rect AbsoluteRenderBounds;
        public bool HasAbsoluteRenderBounds;
        public bool HasExportSettings;
        public bool HasVisibleFills;
        public bool HasVisibleStrokes;
        public bool HasEffects;
        public bool HasTextCharacters;
        public string TextCharacters;
        public FigmaTextStyleInfo TextStyle;
        public bool HasMixedTextStyles;
        public bool HasUnsupportedTextVisuals;
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
                string name = string.IsNullOrEmpty(Name) ? L("(未命名)", "(Unnamed)") : Name;
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
        public ImportedLayerKind Kind = ImportedLayerKind.Image;
        public string AssetPath;
        public Rect FigmaBounds;
        public Vector2 AnchoredPosition;
        public Vector2 SizeDelta;
        public int SiblingIndex;
        public string UnityObjectPath;
        public FigmaTextStyleInfo TextStyle;

        /// <summary>PNG 实际导出画布在 Figma 绝对坐标中的矩形；v2 离线包提供。</summary>
        public Rect ExportBounds;
        public bool HasExportBounds;
        /// <summary>false 表示导出边界由 PNG 尺寸对称估算，阴影偏移时可能有几像素误差。</summary>
        public bool ExportBoundsExact;
        /// <summary>节点自身布局边界（不含阴影/effects），用于背景模糊的裁剪形状。</summary>
        public Rect LayoutBounds;
        public bool HasLayoutBounds;
        /// <summary>Figma background blur 信息；为空表示普通图层。</summary>
        public FigmaBackgroundBlurInfo BackgroundBlur;

        public bool HasUsableExportBounds
        {
            get { return HasExportBounds && ExportBounds.width > 0.01f && ExportBounds.height > 0.01f; }
        }

        public bool HasUsableLayoutBounds
        {
            get { return HasLayoutBounds && LayoutBounds.width > 0.01f && LayoutBounds.height > 0.01f; }
        }

        public bool HasTextStyle
        {
            get { return TextStyle != null && string.Equals(NodeType, "TEXT", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsTextLayer
        {
            get { return Kind == ImportedLayerKind.Text && HasTextStyle; }
        }
    }

    public sealed class FigmaTextStyleInfo
    {
        public string Characters;
        public string FontFamily;
        public string FontStyleName;
        public string FontPostScriptName;
        public int FontWeight;
        public float FontSize;
        public float LineHeightPx;
        public float LetterSpacing;
        public Color Color = Color.white;
        public string HorizontalAlign;
        public string VerticalAlign;
        public string AutoResize;
        public bool HasMixedStyles;
        public bool HasUnsupportedVisuals;

        public bool HasUsableStyle
        {
            get { return !string.IsNullOrEmpty(Characters) && FontSize > 0.01f; }
        }

        public bool IsSmartImportSafe
        {
            get { return HasUsableStyle && !HasMixedStyles && !HasUnsupportedVisuals; }
        }
    }

    internal sealed class FigmaImportResult
    {
        public string OutputFolder;
        public string ManifestPath;
        public string DiagnosticReportPath;
        public string PrefabPath;
        public string FontMappingPath;
        public int FontMappingTextStyleCount;
        public int FontMappingAddedEntries;
        public int FontMappingUpdatedEntries;
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
        public string TextImportMode;
        public float RootWidth;
        public float RootHeight;
        public int LayerCount;
        public int AssetCount;
        public bool HasReferenceImage;
    }

    /// <summary>Figma background blur 参数；导入端据此生成模糊背景。</summary>
    public sealed class FigmaBackgroundBlurInfo
    {
        public float Radius;
        /// <summary>左上、右上、右下、左下圆角，单位与 Figma 设计像素一致。</summary>
        public Vector4 CornerRadii;
        public string BackdropNodeId;
        /// <summary>实际带 background blur 的节点边界（可能是卡片的子层）。</summary>
        public Rect Bounds;
        public bool HasBounds;

        public bool Enabled
        {
            get { return Radius > 0.01f; }
        }
    }

    /// <summary>导入时生成的背景模糊资源（烘焙贴图、圆角遮罩、运行时模糊源）。</summary>
    internal sealed class FigmaGeneratedBlurAssets
    {
        public Texture2D BlurredTexture;
        public Sprite MaskSprite;
        public Texture2D BackdropTexture;
        public Rect BackdropExportBounds;
        public Rect SourceUvRect;
        public Rect BlurBounds;
        public float BlurRadius;
    }

    /// <summary>参考图（reference.png）的实际导出画布与纹理尺寸，用于精确对齐和裁剪。</summary>
    internal sealed class FigmaReferenceGeometry
    {
        public Rect ExportBounds;
        public bool HasExportBounds;
        public bool ExportBoundsExact;
        public Vector2Int TextureSize;

        public bool HasUsableExportBounds
        {
            get { return HasExportBounds && ExportBounds.width > 0.01f && ExportBounds.height > 0.01f; }
        }

        public bool HasUsableTextureSize
        {
            get { return TextureSize.x > 0 && TextureSize.y > 0; }
        }
    }

    internal sealed class FigmaManifestRebuildData
    {
        public string FileKey;
        public float ImageScale = 1f;
        public string ContainerSliceStrategy;
        public string TextImportMode;
        public string ManifestPath;
        public string OutputFolder;
        public FigmaNodeInfo RootNode;
        public string ReferenceAssetPath;
        public readonly FigmaReferenceGeometry ReferenceGeometry = new FigmaReferenceGeometry();
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
