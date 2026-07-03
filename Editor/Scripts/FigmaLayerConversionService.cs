using System;
using System.Collections.Generic;
using System.IO;
using FigmaUiImporter;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FigmaUiImporter.Editor
{
    internal static class FigmaLayerConversionService
    {
        public static bool IsTextNode(FigmaLayerBinding binding)
        {
            return binding != null
                && string.Equals(binding.NodeType, "TEXT", StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanConvertToImage(FigmaLayerBinding binding, out string reason)
        {
            reason = string.Empty;
            if (binding == null)
            {
                reason = "缺少 FigmaLayerBinding。";
                return false;
            }

            if (!IsTextNode(binding))
            {
                reason = "当前节点不是 Figma TEXT。";
                return false;
            }

            if (binding.GetComponent<TextMeshProUGUI>() == null)
            {
                reason = "当前节点没有 TextMeshProUGUI 组件。";
                return false;
            }

            if (!AssetFileExists(binding.AssetPath))
            {
                reason = "找不到 fallback PNG：" + SafeAssetPath(binding.AssetPath);
                return false;
            }

            return true;
        }

        public static bool CanConvertToText(FigmaLayerBinding binding, out string reason)
        {
            reason = string.Empty;
            if (binding == null)
            {
                reason = "缺少 FigmaLayerBinding。";
                return false;
            }

            if (!IsTextNode(binding))
            {
                reason = "当前节点不是 Figma TEXT。";
                return false;
            }

            if (binding.GetComponent<Image>() == null)
            {
                reason = "当前节点没有 Image 组件。";
                return false;
            }

            FigmaTextStyleInfo style;
            if (!TryLoadTextStyle(binding, out style, out reason))
            {
                return false;
            }

            if (!style.HasUsableStyle)
            {
                reason = "文本样式缺少内容或字号，无法创建 TMP。";
                return false;
            }

            return true;
        }

        public static bool ConvertToImage(FigmaLayerBinding binding, out string message)
        {
            if (!CanConvertToImage(binding, out message))
            {
                return false;
            }

            Sprite sprite = LoadOrImportSprite(binding.AssetPath);
            if (sprite == null)
            {
                message = "PNG 资源未能作为 Sprite 载入：" + SafeAssetPath(binding.AssetPath);
                return false;
            }

            GameObject gameObject = binding.gameObject;
            TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
            Image image = gameObject.GetComponent<Image>();
            bool raycastTarget = text != null ? text.raycastTarget : image != null && image.raycastTarget;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Figma 文本转 PNG");

            if (text != null)
            {
                Undo.DestroyObjectImmediate(text);
            }

            if (image == null)
            {
                image = Undo.AddComponent<Image>(gameObject);
            }
            else
            {
                Undo.RecordObject(image, "Figma 文本转 PNG");
            }

            image.sprite = sprite;
            image.preserveAspect = false;
            image.raycastTarget = raycastTarget;
            ApplyImageRect(binding, sprite);
            EditorUtility.SetDirty(image);
            MarkDirty(gameObject);
            Undo.CollapseUndoOperations(undoGroup);

            message = "已转为 PNG 图片。";
            return true;
        }

        public static bool ConvertToText(FigmaLayerBinding binding, out string message)
        {
            FigmaTextStyleInfo style;
            if (!TryLoadTextStyle(binding, out style, out message))
            {
                return false;
            }

            if (!style.HasUsableStyle)
            {
                message = "文本样式缺少内容或字号，无法创建 TMP。";
                return false;
            }

            GameObject gameObject = binding.gameObject;
            Image image = gameObject.GetComponent<Image>();
            TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
            bool raycastTarget = image != null ? image.raycastTarget : text != null && text.raycastTarget;
            Sprite restoreSprite = image == null ? null : image.sprite;
            bool restorePreserveAspect = image != null && image.preserveAspect;

            FigmaFontMappingAsset mapping;
            if (!TryGetFontMapping(out mapping, out message))
            {
                return false;
            }

            FigmaFontMappingService.UpdateFromTextStyles(mapping, new List<FigmaTextStyleInfo> { style });
            TMP_FontAsset resolvedFont = FigmaTmpFontUtility.ResolveFont(text, style, mapping);
            if (text == null
                && !FigmaTmpFontUtility.TryEnsureProjectDefaultFont(
                    resolvedFont,
                    out resolvedFont,
                    out message))
            {
                return false;
            }

            if (resolvedFont == null)
            {
                message = "未找到可用 TMP 字体。请先在 Assets/FigmaImports/FigmaFontMapping.asset 的默认 TMP 字体或匹配条目中配置 FontAsset。";
                return false;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Figma PNG 转 TMP");

            if (image != null)
            {
                Undo.DestroyObjectImmediate(image);
                image = null;
            }

            bool addedText = false;
            if (text == null)
            {
                try
                {
                    text = Undo.AddComponent<TextMeshProUGUI>(gameObject);
                    addedText = true;
                    text.font = resolvedFont;
                }
                catch (Exception ex)
                {
                    RestoreImage(gameObject, restoreSprite, restorePreserveAspect, raycastTarget);
                    message = "创建 TMP 组件失败：" + ex.Message;
                    Undo.CollapseUndoOperations(undoGroup);
                    return false;
                }
            }
            else
            {
                Undo.RecordObject(text, "Figma PNG 转 TMP");
                text.font = resolvedFont;
            }

            try
            {
                FigmaTextLayerUtility.ApplyTextStyle(text, style, mapping, raycastTarget);
                ApplyTextRect(binding);
            }
            catch (Exception ex)
            {
                if (addedText && text != null)
                {
                    Undo.DestroyObjectImmediate(text);
                }

                RestoreImage(gameObject, restoreSprite, restorePreserveAspect, raycastTarget);
                message = "应用 TMP 样式失败：" + ex.Message;
                Undo.CollapseUndoOperations(undoGroup);
                return false;
            }

            EditorUtility.SetDirty(text);
            MarkDirty(gameObject);
            Undo.CollapseUndoOperations(undoGroup);

            message = "已转为 TMP 文本。";
            return true;
        }

        private static void RestoreImage(
            GameObject gameObject,
            Sprite sprite,
            bool preserveAspect,
            bool raycastTarget)
        {
            if (gameObject == null || gameObject.GetComponent<Image>() != null)
            {
                return;
            }

            Image image = Undo.AddComponent<Image>(gameObject);
            image.sprite = sprite;
            image.preserveAspect = preserveAspect;
            image.raycastTarget = raycastTarget;
            EditorUtility.SetDirty(image);
        }

        internal static bool TryLoadTextStyle(
            FigmaLayerBinding binding,
            out FigmaTextStyleInfo style,
            out string reason)
        {
            style = null;
            reason = string.Empty;
            if (binding == null)
            {
                reason = "缺少 FigmaLayerBinding。";
                return false;
            }

            string manifestPath;
            string manifestError = string.Empty;
            if (TryFindManifestPath(binding.AssetPath, out manifestPath))
            {
                try
                {
                    FigmaImportManifestService manifestService = new FigmaImportManifestService();
                    if (manifestService.TryLoadTextStyleForLayer(manifestPath, binding.NodeId, out style))
                    {
                        if (style != null && style.HasUsableStyle)
                        {
                            return true;
                        }

                        manifestError = "manifest 中该节点的 text 元数据缺少内容或字号：" + binding.NodeId;
                    }
                    else
                    {
                        manifestError = "manifest 中没有找到该节点的 text 元数据：" + binding.NodeId;
                    }
                }
                catch (Exception ex)
                {
                    manifestError = ex.Message;
                }
            }

            reason = string.IsNullOrEmpty(manifestError)
                ? "未找到可用的文本元数据。请确认 PNG 旁边仍有导入生成的 manifest.json。"
                : "未能从 manifest 读取文本元数据：" + manifestError;
            return false;
        }

        internal static bool TryFindManifestPath(string assetPath, out string manifestPath)
        {
            manifestPath = string.Empty;
            string folder = GetAssetDirectory(NormalizeAssetPath(assetPath));
            while (!string.IsNullOrEmpty(folder))
            {
                string candidate = FigmaPathUtility.CombineAssetPath(folder, "manifest.json");
                if (File.Exists(FigmaPathUtility.ToFullPath(candidate)))
                {
                    manifestPath = candidate;
                    return true;
                }

                if (string.Equals(folder, "Assets", StringComparison.Ordinal))
                {
                    break;
                }

                folder = GetAssetDirectory(folder);
            }

            return false;
        }

        private static void ApplyImageRect(FigmaLayerBinding binding, Sprite sprite)
        {
            if (binding == null || sprite == null)
            {
                return;
            }

            RectTransform rectTransform = binding.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                return;
            }

            Vector2 sizeDelta = Vector2.zero;
            Vector2 rootCenteredPosition = rectTransform.anchoredPosition;
            FigmaNodeInfo rootNode = null;

            FigmaManifestRebuildData data;
            if (TryLoadConversionData(binding, out data) && data.Layers.Count > 0)
            {
                rootNode = data.RootNode;
                ImportedLayerInfo layer = data.Layers[0];
                layer.Kind = ImportedLayerKind.Image;

                string referenceAssetPath = AssetFileExists(data.ReferenceAssetPath)
                    ? data.ReferenceAssetPath
                    : string.Empty;
                FigmaImportSettings settings = new FigmaImportSettings();
                settings.ImageScale = data.ImageScale;
                using (FigmaLayerGeometryResolver resolver = new FigmaLayerGeometryResolver(
                    settings,
                    data.RootNode,
                    referenceAssetPath))
                {
                    resolver.Resolve(layer);
                }

                sizeDelta = layer.SizeDelta;
                rootCenteredPosition = layer.AnchoredPosition;
            }

            if (sizeDelta.x <= 0.01f || sizeDelta.y <= 0.01f)
            {
                sizeDelta = GetSpriteSize(sprite);
            }

            if (sizeDelta.x <= 0.01f || sizeDelta.y <= 0.01f)
            {
                return;
            }

            ApplyResolvedRect(rectTransform, rootNode, rootCenteredPosition, sizeDelta, "Figma 文本转 PNG");
        }

        private static void ApplyTextRect(FigmaLayerBinding binding)
        {
            if (binding == null)
            {
                return;
            }

            RectTransform rectTransform = binding.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                return;
            }

            FigmaManifestRebuildData data;
            if (!TryLoadConversionData(binding, out data) || data.Layers.Count == 0)
            {
                return;
            }

            ImportedLayerInfo layer = data.Layers[0];
            if (!layer.IsTextLayer)
            {
                return;
            }

            if (layer.SizeDelta.x <= 0.01f || layer.SizeDelta.y <= 0.01f)
            {
                return;
            }

            ApplyResolvedRect(rectTransform, data.RootNode, layer.AnchoredPosition, layer.SizeDelta, "Figma PNG 转 TMP");
        }

        private static void ApplyResolvedRect(
            RectTransform rectTransform,
            FigmaNodeInfo rootNode,
            Vector2 rootCenteredPosition,
            Vector2 size,
            string undoName)
        {
            if (rectTransform == null || size.x <= 0.01f || size.y <= 0.01f)
            {
                return;
            }

            Vector3 fallbackCenter = GetWorldCenter(rectTransform);
            Undo.RecordObject(rectTransform, undoName);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);

            RectTransform coordinateRoot;
            if (TryFindCoordinateRoot(rectTransform, rootNode, out coordinateRoot))
            {
                Vector3 rootLocalCenter = ToRootLocalCenter(coordinateRoot, rootCenteredPosition);
                SetWorldCenter(rectTransform, coordinateRoot.TransformPoint(rootLocalCenter));
            }
            else
            {
                SetWorldCenter(rectTransform, fallbackCenter);
            }

            EditorUtility.SetDirty(rectTransform);
        }

        private static bool TryFindCoordinateRoot(
            RectTransform rectTransform,
            FigmaNodeInfo rootNode,
            out RectTransform coordinateRoot)
        {
            coordinateRoot = null;
            if (rectTransform == null)
            {
                return false;
            }

            Transform current = rectTransform.transform.parent;
            while (current != null)
            {
                RectTransform candidate = current as RectTransform;
                if (candidate != null && string.Equals(current.name, "Layers", StringComparison.OrdinalIgnoreCase))
                {
                    coordinateRoot = candidate;
                    return true;
                }

                current = current.parent;
            }

            string safeRootId = rootNode == null || string.IsNullOrEmpty(rootNode.Id)
                ? string.Empty
                : FigmaPathUtility.SafeFileName(rootNode.Id);
            current = rectTransform.transform.parent;
            while (current != null)
            {
                RectTransform candidate = current as RectTransform;
                if (candidate != null && IsImportRootName(current.name, safeRootId))
                {
                    coordinateRoot = candidate;
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool IsImportRootName(string name, string safeRootId)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(safeRootId)
                && name.IndexOf(safeRootId, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return name.StartsWith("FigmaImport_", StringComparison.OrdinalIgnoreCase);
        }

        private static Vector3 ToRootLocalCenter(RectTransform root, Vector2 centerRelativeToRoot)
        {
            Rect rect = root.rect;
            Vector2 centerOffset = new Vector2(
                (0.5f - root.pivot.x) * rect.width,
                (0.5f - root.pivot.y) * rect.height);
            Vector2 localCenter = centerOffset + centerRelativeToRoot;
            return new Vector3(localCenter.x, localCenter.y, 0f);
        }

        private static Vector3 GetWorldCenter(RectTransform rectTransform)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        private static void SetWorldCenter(RectTransform rectTransform, Vector3 targetWorldCenter)
        {
            Vector3 currentCenter = GetWorldCenter(rectTransform);
            rectTransform.position += targetWorldCenter - currentCenter;
        }

        private static bool TryLoadConversionData(
            FigmaLayerBinding binding,
            out FigmaManifestRebuildData data)
        {
            data = null;
            if (binding == null)
            {
                return false;
            }

            string manifestPath;
            if (!TryFindManifestPath(binding.AssetPath, out manifestPath))
            {
                return false;
            }

            FigmaImportManifestService manifestService = new FigmaImportManifestService();
            try
            {
                return manifestService.TryLoadLayerForConversion(manifestPath, binding.NodeId, out data);
            }
            catch
            {
                data = null;
                return false;
            }
        }

        private static Vector2 GetSpriteSize(Sprite sprite)
        {
            if (sprite == null)
            {
                return Vector2.zero;
            }

            Rect rect = sprite.rect;
            return new Vector2(rect.width, rect.height);
        }

        private static bool TryGetFontMapping(out FigmaFontMappingAsset mapping, out string reason)
        {
            mapping = null;
            reason = string.Empty;
            try
            {
                mapping = FigmaFontMappingService.GetOrCreateGlobalMapping();
                return true;
            }
            catch (Exception ex)
            {
                reason = "读取或创建 FigmaFontMapping.asset 失败：" + ex.Message;
                return false;
            }
        }

        private static Sprite LoadOrImportSprite(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(normalized);
            if (sprite != null || !AssetFileExists(normalized))
            {
                return sprite;
            }

            UnitySpriteAssetImporter importer = new UnitySpriteAssetImporter();
            importer.ImportSprites(new[] { normalized }, TextureImporterCompressionMode.None);
            return AssetDatabase.LoadAssetAtPath<Sprite>(normalized);
        }

        private static bool AssetFileExists(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            return !string.IsNullOrEmpty(normalized)
                && (normalized.StartsWith("Assets/", StringComparison.Ordinal) || string.Equals(normalized, "Assets", StringComparison.Ordinal))
                && File.Exists(FigmaPathUtility.ToFullPath(normalized));
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return string.IsNullOrWhiteSpace(assetPath)
                ? string.Empty
                : assetPath.Trim().Replace('\\', '/');
        }

        private static string SafeAssetPath(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            return string.IsNullOrEmpty(normalized) ? "(空路径)" : normalized;
        }

        private static string GetAssetDirectory(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            int slashIndex = normalized.LastIndexOf("/", StringComparison.Ordinal);
            if (slashIndex <= 0)
            {
                return string.Empty;
            }

            return normalized.Substring(0, slashIndex);
        }

        private static void MarkDirty(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            EditorUtility.SetDirty(gameObject);
            if (gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
        }
    }
}
