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

    public sealed class FigmaImportSettings
    {
        public string FileKey;
        public string AccessToken;
        public string OutputFolder = "Assets/FigmaImports";
        public Canvas TargetCanvas;
        public FigmaSliceMode SliceMode = FigmaSliceMode.ExportSettingsThenAutoSlice;
        public float ImageScale = 1f;
        public bool CreateReference = true;
        public float ReferenceAlpha = 0.5f;
        public bool DisableRaycastTarget = true;
        public bool OverwriteExisting;
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
        public bool Visible = true;
        public Rect AbsoluteBounds;
        public bool HasAbsoluteBounds;
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
    }

    internal sealed class FigmaImportResult
    {
        public string OutputFolder;
        public string ManifestPath;
        public GameObject RootObject;
        public int LayerCount;
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
