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
            FigmaReferenceGeometry referenceGeometry = null,
            IReadOnlyDictionary<string, FigmaGeneratedBlurAssets> blurAssets = null)
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

                    // 背景模糊节点：用一个 Blur Group 同时容纳模糊背景和卡片，
                    // 锚点/弹窗动画只需操作这个父节点，两者会一起移动。
                    FigmaGeneratedBlurAssets blurAsset = null;
                    bool hasBlur = blurAssets != null
                        && blurAssets.TryGetValue(layer.NodeId, out blurAsset)
                        && blurAsset != null;
                    Rect blurBounds = new Rect();
                    Transform layerParent = layersRoot.transform;
                    GameObject blurGroup = null;
                    FigmaBackgroundBlur blurComponent = null;
                    if (hasBlur)
                    {
                        blurBounds = blurAsset.BlurBounds.width > 0.01f && blurAsset.BlurBounds.height > 0.01f
                            ? blurAsset.BlurBounds
                            : layer.HasUsableLayoutBounds ? layer.LayoutBounds : layer.FigmaBounds;

                        blurGroup = CreateRectObject(
                            FigmaPathUtility.SafeObjectName(layer.NodeName) + " Blur Group",
                            layersRoot.transform);
                        RectTransform groupRect = blurGroup.GetComponent<RectTransform>();
                        SetRectFromFigmaBounds(groupRect, selectedRoot.AbsoluteBounds, blurBounds);
                        layerParent = blurGroup.transform;
                        blurComponent = CreateBlurBackdrop(
                            layerParent,
                            layersRoot.transform,
                            selectedRoot,
                            layer,
                            blurAsset,
                            settings);
                    }

                    GameObject layerObject = CreateRectObject(
                        FigmaPathUtility.SafeObjectName(layer.NodeName),
                        layerParent);
                    RectTransform rectTransform = layerObject.GetComponent<RectTransform>();
                    rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                    rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    if (hasBlur)
                    {
                        Rect cardBounds = layer.HasUsableExportBounds ? layer.ExportBounds : layer.FigmaBounds;
                        SetRectRelativeToBounds(rectTransform, blurBounds, cardBounds);
                    }
                    else
                    {
                        rectTransform.anchoredPosition = layer.AnchoredPosition;
                        rectTransform.sizeDelta = layer.SizeDelta;
                    }

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

                    if (layer.BackgroundBlur != null && layer.BackgroundBlur.Enabled)
                    {
                        ImportedLayerInfo backdropLayer = FindLayerById(layers, layer.BackgroundBlur.BackdropNodeId);
                        FigmaBlurLayerInfo blurInfo = new FigmaBlurLayerInfo
                        {
                            enabled = true,
                            radius = layer.BackgroundBlur.Radius,
                            cornerRadii = layer.BackgroundBlur.CornerRadii,
                            bounds = layer.BackgroundBlur.HasBounds
                                ? layer.BackgroundBlur.Bounds
                                : layer.HasUsableLayoutBounds ? layer.LayoutBounds : layer.FigmaBounds,
                            layoutBounds = layer.HasUsableLayoutBounds ? layer.LayoutBounds : layer.FigmaBounds,
                            exportBounds = layer.HasUsableExportBounds ? layer.ExportBounds : layer.FigmaBounds,
                            rootBounds = selectedRoot.AbsoluteBounds,
                            backdropNodeId = layer.BackgroundBlur.BackdropNodeId ?? string.Empty,
                            backdropAssetPath = backdropLayer != null ? backdropLayer.AssetPath : string.Empty,
                            backdropExportBounds = backdropLayer != null && backdropLayer.HasUsableExportBounds
                                ? backdropLayer.ExportBounds
                                : new Rect(),
                            generatedFolder = settings != null
                                ? FigmaPathUtility.CombineAssetPath(settings.OutputFolder, "generated")
                                : string.Empty
                        };

                        FigmaBlurMode bindingMode = hasBlur
                            ? (settings != null && settings.BackgroundBlurMode == FigmaBackgroundBlurMode.Dynamic
                                ? FigmaBlurMode.Dynamic
                                : FigmaBlurMode.Baked)
                            : FigmaBlurMode.None;
                        binding.SetBlurInfo(blurInfo, bindingMode);
                        if (hasBlur && blurComponent != null)
                        {
                            binding.SetBlurReferences(blurComponent, blurGroup, blurComponent.gameObject);
                        }
                    }

                    // 按创建顺序排列；模糊背景会插在对应卡片之前，不能再用 manifest 索引
                    // 直接 SetSiblingIndex，否则会打乱后续图层顺序。
                    layer.UnityObjectPath = GetHierarchyPath(layerObject.transform);
                }
            }

            EditorSceneManager.MarkSceneDirty(rootObject.scene);
            Selection.activeGameObject = rootObject;
            return rootObject;
        }

        private static ImportedLayerInfo FindLayerById(IList<ImportedLayerInfo> layers, string nodeId)
        {
            if (layers == null || string.IsNullOrEmpty(nodeId))
            {
                return null;
            }

            for (int i = 0; i < layers.Count; i++)
            {
                ImportedLayerInfo layer = layers[i];
                if (layer != null && string.Equals(layer.NodeId, nodeId, System.StringComparison.Ordinal))
                {
                    return layer;
                }
            }

            return null;
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

        private static FigmaBackgroundBlur CreateBlurBackdrop(
            Transform parent,
            Transform layersRoot,
            FigmaNodeInfo selectedRoot,
            ImportedLayerInfo layer,
            FigmaGeneratedBlurAssets assets,
            FigmaImportSettings settings)
        {
            GameObject backdropObject = CreateRectObject(
                FigmaPathUtility.SafeObjectName(layer.NodeName) + " Blur Backdrop",
                parent);
            RectTransform backdropRect = backdropObject.GetComponent<RectTransform>();
            StretchToParent(backdropRect);

            GameObject maskObject = CreateRectObject("Mask", backdropObject.transform);
            StretchToParent(maskObject.GetComponent<RectTransform>());
            Image maskImage = maskObject.AddComponent<Image>();
            maskImage.sprite = assets.MaskSprite;
            maskImage.preserveAspect = false;
            maskImage.raycastTarget = false;
            Mask mask = maskObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject blurObject = CreateRectObject("Blurred Backdrop", maskObject.transform);
            StretchToParent(blurObject.GetComponent<RectTransform>());
            RawImage rawImage = blurObject.AddComponent<RawImage>();
            rawImage.texture = assets.BlurredTexture;
            rawImage.uvRect = assets.SourceUvRect;
            rawImage.raycastTarget = false;

            FigmaBackgroundBlur runtimeBlur = backdropObject.AddComponent<FigmaBackgroundBlur>();
            runtimeBlur.targetImage = rawImage;
            runtimeBlur.sourceMode = FigmaBackgroundBlur.SourceMode.StaticTexture;
            runtimeBlur.sourceTexture = assets.BackdropTexture;
            runtimeBlur.sourceDesignSize = new Vector2(
                assets.BackdropExportBounds.width,
                assets.BackdropExportBounds.height);
            runtimeBlur.sourceRoot = layersRoot as RectTransform;
            runtimeBlur.sourceRootDesignBounds = selectedRoot.AbsoluteBounds;
            runtimeBlur.sourceExportBounds = assets.BackdropExportBounds;
            runtimeBlur.trackedRect = parent as RectTransform;
            runtimeBlur.trackSourceUv = true;
            runtimeBlur.radius = Mathf.Max(0.01f, assets.BlurRadius);
            runtimeBlur.downsample = 2;
            runtimeBlur.iterations = 3;
            FigmaBackgroundBlurMode blurMode = settings != null
                ? settings.BackgroundBlurMode
                : FigmaBackgroundBlurMode.None;
            runtimeBlur.useRuntimeBlur = blurMode == FigmaBackgroundBlurMode.Dynamic;
            runtimeBlur.updateEveryFrame = blurMode == FigmaBackgroundBlurMode.Dynamic;
            runtimeBlur.sourceMode = blurMode == FigmaBackgroundBlurMode.Dynamic
                ? FigmaBackgroundBlur.SourceMode.Camera
                : FigmaBackgroundBlur.SourceMode.StaticTexture;
            runtimeBlur.sourceCamera = blurMode == FigmaBackgroundBlurMode.Dynamic
                ? Camera.main
                : null;
            runtimeBlur.blurShader = Resources.Load<ComputeShader>("FigmaBackgroundBlur");
            return runtimeBlur;
        }

        private static void SetRectFromFigmaBounds(RectTransform rectTransform, Rect rootBounds, Rect bounds)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = FigmaCoordinateUtility.ToUnityAnchoredPosition(rootBounds, bounds);
            rectTransform.sizeDelta = new Vector2(bounds.width, bounds.height);
        }

        private static void SetRectRelativeToBounds(RectTransform rectTransform, Rect parentBounds, Rect childBounds)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            float localLeft = childBounds.x - parentBounds.x;
            float localTop = childBounds.y - parentBounds.y;
            rectTransform.anchoredPosition = new Vector2(
                localLeft + childBounds.width * 0.5f - parentBounds.width * 0.5f,
                parentBounds.height * 0.5f - localTop - childBounds.height * 0.5f);
            rectTransform.sizeDelta = new Vector2(childBounds.width, childBounds.height);
        }

        private static void StretchToParent(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
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
