using System.Collections.Generic;
using FigmaUiImporter;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaSceneBuilder
    {
        public GameObject BuildSceneObjects(
            FigmaImportSettings settings,
            FigmaNodeInfo selectedRoot,
            string referenceAssetPath,
            IList<ImportedLayerInfo> layers)
        {
            Canvas canvas = GetOrCreateCanvas(settings.TargetCanvas);
            string baseName = "FigmaImport_"
                + FigmaPathUtility.SafeObjectName(selectedRoot.Name)
                + "_"
                + FigmaPathUtility.SafeFileName(selectedRoot.Id);

            Transform oldRoot = canvas.transform.Find(baseName);
            if (oldRoot != null && settings.OverwriteExisting)
            {
                Object.DestroyImmediate(oldRoot.gameObject);
            }

            string rootName = settings.OverwriteExisting
                ? baseName
                : GameObjectUtility.GetUniqueNameForSibling(canvas.transform, baseName);

            GameObject rootObject = CreateRectObject(rootName, canvas.transform);
            Undo.RegisterCreatedObjectUndo(rootObject, "导入 Figma UI");

            RectTransform rootRect = rootObject.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = new Vector2(selectedRoot.AbsoluteBounds.width, selectedRoot.AbsoluteBounds.height);
            rootObject.AddComponent<RectMask2D>();

            if (!string.IsNullOrEmpty(referenceAssetPath))
            {
                CreateReference(settings, rootObject.transform, selectedRoot, referenceAssetPath);
            }

            GameObject layersRoot = CreateRectObject("Layers", rootObject.transform);
            MatchRootRect(layersRoot.GetComponent<RectTransform>(), selectedRoot);

            using (FigmaLayerGeometryResolver geometryResolver = new FigmaLayerGeometryResolver(settings, selectedRoot, referenceAssetPath))
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    ImportedLayerInfo layer = layers[i];
                    bool createTextLayer = ShouldCreateTextLayer(settings, layer);
                    if (!createTextLayer)
                    {
                        geometryResolver.Resolve(layer);
                    }

                    GameObject layerObject = CreateRectObject(
                        FigmaPathUtility.SafeObjectName(layer.NodeName),
                        layersRoot.transform);
                    RectTransform rectTransform = layerObject.GetComponent<RectTransform>();
                    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    rectTransform.anchoredPosition = layer.AnchoredPosition;
                    rectTransform.sizeDelta = layer.SizeDelta;

                    if (createTextLayer)
                    {
                        CreateTextLayer(settings, layerObject, layer);
                    }
                    else
                    {
                        CreateImageLayer(settings, layerObject, layer);
                    }

                    FigmaLayerBinding binding = layerObject.AddComponent<FigmaLayerBinding>();
                    binding.SetLayerInfo(layer.NodeId, layer.NodeName, layer.NodeType, layer.AssetPath, layer.SiblingIndex);

                    layerObject.transform.SetSiblingIndex(i);
                    layer.UnityObjectPath = GetHierarchyPath(layerObject.transform);
                }
            }

            EditorSceneManager.MarkSceneDirty(rootObject.scene);
            Selection.activeGameObject = rootObject;
            return rootObject;
        }

        private static bool ShouldCreateTextLayer(FigmaImportSettings settings, ImportedLayerInfo layer)
        {
            if (settings == null
                || layer == null
                || !layer.IsTextLayer
                || settings.TextImportMode == FigmaTextImportMode.Image)
            {
                return false;
            }

            if (settings.TextImportMode == FigmaTextImportMode.TextMeshPro)
            {
                return layer.TextStyle.HasUsableStyle;
            }

            return layer.TextStyle.IsSmartImportSafe;
        }

        private static void CreateImageLayer(FigmaImportSettings settings, GameObject layerObject, ImportedLayerInfo layer)
        {
            Image image = layerObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(layer.AssetPath);
            image.preserveAspect = false;
            image.raycastTarget = !settings.DisableRaycastTarget;
        }

        private static void CreateTextLayer(FigmaImportSettings settings, GameObject layerObject, ImportedLayerInfo layer)
        {
            FigmaTextStyleInfo style = layer.TextStyle;
            TextMeshProUGUI text = layerObject.AddComponent<TextMeshProUGUI>();
            text.text = style.Characters ?? string.Empty;
            text.richText = false;
            text.fontSize = style.FontSize;
            text.color = style.Color;
            text.alignment = ResolveTextAlignment(style.HorizontalAlign, style.VerticalAlign);
            text.enableWordWrapping = !string.Equals(style.AutoResize, "WIDTH_AND_HEIGHT", System.StringComparison.OrdinalIgnoreCase);
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = !settings.DisableRaycastTarget;
            text.margin = Vector4.zero;

            if (style.FontWeight >= 600)
            {
                text.fontStyle |= FontStyles.Bold;
            }

            if (style.LineHeightPx > style.FontSize + 0.01f)
            {
                text.lineSpacing = style.LineHeightPx - style.FontSize;
            }

            text.characterSpacing = style.LetterSpacing;
        }

        private static TextAlignmentOptions ResolveTextAlignment(string horizontal, string vertical)
        {
            bool isCenterX = string.Equals(horizontal, "CENTER", System.StringComparison.OrdinalIgnoreCase);
            bool isRight = string.Equals(horizontal, "RIGHT", System.StringComparison.OrdinalIgnoreCase);
            bool isCenterY = string.Equals(vertical, "CENTER", System.StringComparison.OrdinalIgnoreCase);
            bool isBottom = string.Equals(vertical, "BOTTOM", System.StringComparison.OrdinalIgnoreCase);

            if (isBottom)
            {
                if (isRight)
                {
                    return TextAlignmentOptions.BottomRight;
                }

                return isCenterX ? TextAlignmentOptions.Bottom : TextAlignmentOptions.BottomLeft;
            }

            if (isCenterY)
            {
                if (isRight)
                {
                    return TextAlignmentOptions.Right;
                }

                return isCenterX ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            }

            if (isRight)
            {
                return TextAlignmentOptions.TopRight;
            }

            return isCenterX ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;
        }

        private static void CreateReference(
            FigmaImportSettings settings,
            Transform root,
            FigmaNodeInfo selectedRoot,
            string referenceAssetPath)
        {
            GameObject referenceRoot = CreateRectObject("Reference", root);
            MatchRootRect(referenceRoot.GetComponent<RectTransform>(), selectedRoot);

            GameObject designReference = CreateRectObject("DesignReference", referenceRoot.transform);
            MatchRootRect(designReference.GetComponent<RectTransform>(), selectedRoot);

            Image image = designReference.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(referenceAssetPath);
            image.preserveAspect = false;
            image.raycastTarget = false;

            CanvasGroup canvasGroup = designReference.AddComponent<CanvasGroup>();
            canvasGroup.alpha = Mathf.Clamp01(settings.ReferenceAlpha);
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        private static Canvas GetOrCreateCanvas(Canvas targetCanvas)
        {
            if (targetCanvas != null)
            {
                return targetCanvas;
            }

#if UNITY_2023_1_OR_NEWER
            Canvas existing = Object.FindFirstObjectByType<Canvas>();
#else
            Canvas existing = Object.FindObjectOfType<Canvas>();
#endif
            if (existing != null)
            {
                return existing;
            }

            GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasObject, "创建 Canvas");
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            return canvas;
        }

        private static GameObject CreateRectObject(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            rectTransform.localScale = Vector3.one;
            return gameObject;
        }

        private static void MatchRootRect(RectTransform rectTransform, FigmaNodeInfo selectedRoot)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(selectedRoot.AbsoluteBounds.width, selectedRoot.AbsoluteBounds.height);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
