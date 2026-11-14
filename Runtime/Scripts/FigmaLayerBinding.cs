using System;
using UnityEngine;

namespace FigmaUiImporter
{
    /// <summary>Figma 背景模糊节点在 Unity 中的表现模式。</summary>
    public enum FigmaBlurMode
    {
        None,
        Baked,
        Dynamic
    }

    /// <summary>按需生成模糊资源所需的 Figma 元数据，保存在 FigmaLayerBinding 上。</summary>
    [Serializable]
    public sealed class FigmaBlurLayerInfo
    {
        public bool enabled;
        public float radius;
        public Vector4 cornerRadii;
        public Rect bounds;
        public Rect layoutBounds;
        public Rect exportBounds;
        public Rect rootBounds;
        public string backdropNodeId;
        public string backdropAssetPath;
        public Rect backdropExportBounds;
        public string generatedFolder;
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class FigmaLayerBinding : MonoBehaviour
    {
        [SerializeField] private string nodeId;
        [SerializeField] private string nodeName;
        [SerializeField] private string nodeType;
        [SerializeField] private string assetPath;
        [SerializeField] private int siblingIndex;
        [SerializeField] private FigmaBlurMode blurMode = FigmaBlurMode.None;
        [SerializeField] private FigmaBlurLayerInfo blurInfo;
        [SerializeField] private FigmaBackgroundBlur blurComponent;
        [SerializeField] private GameObject blurGroup;
        [SerializeField] private GameObject blurBackdrop;

        public string NodeId { get { return nodeId; } }
        public string NodeName { get { return nodeName; } }
        public string NodeType { get { return nodeType; } }
        public string AssetPath { get { return assetPath; } }
        public int SiblingIndex { get { return siblingIndex; } }
        public FigmaBlurMode BlurMode { get { return blurMode; } }
        public FigmaBlurLayerInfo BlurInfo { get { return blurInfo; } }
        public FigmaBackgroundBlur BlurComponent { get { return blurComponent; } }
        public GameObject BlurGroup { get { return blurGroup; } }
        public GameObject BlurBackdrop { get { return blurBackdrop; } }

        public bool HasBlurInfo
        {
            get { return blurInfo != null && blurInfo.enabled; }
        }

        public void SetLayerInfo(
            string layerNodeId,
            string layerNodeName,
            string layerNodeType,
            string layerAssetPath,
            int layerSiblingIndex)
        {
            nodeId = layerNodeId ?? string.Empty;
            nodeName = layerNodeName ?? string.Empty;
            nodeType = layerNodeType ?? string.Empty;
            assetPath = layerAssetPath ?? string.Empty;
            siblingIndex = layerSiblingIndex;
        }

        public void SetBlurInfo(FigmaBlurLayerInfo info, FigmaBlurMode mode)
        {
            blurInfo = info;
            blurMode = mode;
        }

        public void SetBlurMode(FigmaBlurMode mode)
        {
            blurMode = mode;
        }

        public void SetBlurReferences(
            FigmaBackgroundBlur component,
            GameObject group,
            GameObject backdrop)
        {
            blurComponent = component;
            blurGroup = group;
            blurBackdrop = backdrop;
        }

        public void ClearBlurReferences()
        {
            blurComponent = null;
            blurGroup = null;
            blurBackdrop = null;
        }
    }
}
