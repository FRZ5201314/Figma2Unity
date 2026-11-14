using System.Collections.Generic;
using FigmaUiImporter;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static FigmaUiImporter.Editor.FigmaLocalization;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaSceneBuilder
    {
        public GameObject BuildSceneObjects(
            FigmaImportSettings settings,
            FigmaNodeInfo selectedRoot,
            string referenceAssetPath,
            IList<ImportedLayerInfo> layers,
            FigmaReferenceGeometry referenceGeometry = null)
        {
            Canvas canvas = GetOrCreateCanvas(settings.TargetCanvas);
            string baseName = "FigmaImport_"
                + FigmaPathUtility.SafeObjectName(selectedRoot.Name)
                + "_"
                + FigmaPathUtility.SafeFileName(selectedRoot.Id);

            Transform oldRoot = canvas.transform.Find(baseName);
            if (oldRoot != null && settings.OverwriteExisting)
            {
                // 走 Undo 销毁，保证覆盖导入也可以撤销。
                Undo.DestroyObjectImmediate(oldRoot.gameObject);
            }

            string rootName = settings.OverwriteExisting
                ? baseName
                : GameObjectUtility.GetUniqueNameForSibling(canvas.transform, baseName);

            GameObject rootObject = CreateRectObject(rootName, canvas.transform);

            RectTransform rootRect = rootObject.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = new Vector2(selectedRoot.AbsoluteBounds.width, selectedRoot.AbsoluteBounds.height);
            if (selectedRoot.ClipsContent)
            {
                // 只有 Figma 明确裁剪时才加 RectMask2D，避免 GROUP 或 clipsContent=false 的 Frame 被误裁。
                rootObject.AddComponent<RectMask2D>();
            }

            if (!string.IsNullOrEmpty(referenceAssetPath))
            {
                CreateReference(settings, rootObject.transform, selectedRoot, referenceAssetPath, referenceGeometry);
            }

            GameObject layersRoot = CreateRectObject("Layers", rootObject.transform);
            MatchRootRect(layersRoot.GetComponent<RectTransform>(), selectedRoot);

            using (FigmaLayerGeometryResolver geometryResolver = new FigmaLayerGeometryResolver(settings, selectedRoot, referenceAssetPath))
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    ImportedLayerInfo layer = layers[i];
                    bool createTextLayer = ShouldCreateTextLayer(settings, layer);
                    layer.Kind = createTextLayer ? ImportedLayerKind.Text : ImportedLayerKind.Image;
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
                || !layer.HasTextStyle
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
            TMP_FontAsset resolvedFont = FigmaTmpFontUtility.ResolveFont(null, style, settings.FontMapping);
            string reason;
            FigmaTmpFontUtility.TryResolveFallbackFont(resolvedFont, out resolvedFont, out reason);

            TextMeshProUGUI text = layerObject.AddComponent<TextMeshProUGUI>();
            if (resolvedFont != null)
            {
                text.font = resolvedFont;
            }

            FigmaTextLayerUtility.ApplyTextStyle(
                text,
                style,
                settings.FontMapping,
                !settings.DisableRaycastTarget);
        }

        private static void CreateReference(
            FigmaImportSettings settings,
            Transform root,
            FigmaNodeInfo selectedRoot,
            string referenceAssetPath,
            FigmaReferenceGeometry referenceGeometry)
        {
            GameObject referenceRoot = CreateRectObject("Reference", root);
            MatchRootRect(referenceRoot.GetComponent<RectTransform>(), selectedRoot);

            GameObject designReference = CreateRectObject("DesignReference", referenceRoot.transform);
            MatchRootRect(designReference.GetComponent<RectTransform>(), selectedRoot);

            // v2 manifest 记录了参考图实际导出画布，用 uvRect 只采样 rootBounds 对应区域，
            // 避免把带透明边距的整张 PNG 拉伸进 root 尺寸。
            if (CreateReferenceFromExportBounds(designReference, selectedRoot, referenceAssetPath, referenceGeometry))
            {
                AddReferenceCanvasGroup(settings, designReference);
                return;
            }

            Image image = designReference.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(referenceAssetPath);
            image.preserveAspect = false;
            image.raycastTarget = false;

            AddReferenceCanvasGroup(settings, designReference);
        }

        private static bool CreateReferenceFromExportBounds(
            GameObject designReference,
            FigmaNodeInfo selectedRoot,
            string referenceAssetPath,
            FigmaReferenceGeometry referenceGeometry)
        {
            if (designReference == null
                || selectedRoot == null
                || referenceGeometry == null
                || !referenceGeometry.HasUsableExportBounds)
            {
                return false;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(referenceAssetPath);
            if (texture == null)
            {
                return false;
            }

            Rect rootBounds = selectedRoot.AbsoluteBounds;
            Rect exportBounds = referenceGeometry.ExportBounds;
            // 用 Unity 里实际导入的纹理尺寸计算 UV；即使纹理被 maxTextureSize 缩小也能对齐。
            float scaleX = texture.width / exportBounds.width;
            float scaleY = texture.height / exportBounds.height;

            // rootBounds 在参考图像素坐标中的内容区
            float pixelX = (rootBounds.x - exportBounds.x) * scaleX;
            float pixelY = (rootBounds.y - exportBounds.y) * scaleY;
            float pixelWidth = rootBounds.width * scaleX;
            float pixelHeight = rootBounds.height * scaleY;

            // 导出边界可能是估算值，或内容区有 1px 抗锯齿，做一次夹取避免 UV 越界。
            pixelX = Mathf.Clamp(pixelX, 0f, texture.width);
            pixelY = Mathf.Clamp(pixelY, 0f, texture.height);
            pixelWidth = Mathf.Clamp(pixelWidth, 0f, texture.width - pixelX);
            pixelHeight = Mathf.Clamp(pixelHeight, 0f, texture.height - pixelY);
            if (pixelWidth <= 0.01f || pixelHeight <= 0.01f)
            {
                return false;
            }

            RawImage rawImage = designReference.AddComponent<RawImage>();
            rawImage.texture = texture;
            rawImage.uvRect = new Rect(
                pixelX / texture.width,
                (texture.height - pixelY - pixelHeight) / texture.height,
                pixelWidth / texture.width,
                pixelHeight / texture.height);
            rawImage.raycastTarget = false;
            return true;
        }

        private static void AddReferenceCanvasGroup(FigmaImportSettings settings, GameObject designReference)
        {
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
            Undo.RegisterCreatedObjectUndo(canvasObject, L("创建 Canvas", "Create Canvas"));
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
            // 每个新建节点都注册 Undo，避免撤销创建后重做时只剩根节点。
            Undo.RegisterCreatedObjectUndo(gameObject, L("导入 Figma UI", "Import Figma UI"));
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
