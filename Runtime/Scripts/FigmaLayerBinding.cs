using UnityEngine;

namespace FigmaUiImporter
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class FigmaLayerBinding : MonoBehaviour
    {
        [SerializeField] private string nodeId;
        [SerializeField] private string nodeName;
        [SerializeField] private string nodeType;
        [SerializeField] private string assetPath;
        [SerializeField] private int siblingIndex;

        public string NodeId { get { return nodeId; } }
        public string NodeName { get { return nodeName; } }
        public string NodeType { get { return nodeType; } }
        public string AssetPath { get { return assetPath; } }
        public int SiblingIndex { get { return siblingIndex; } }

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
    }
}
