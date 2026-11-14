using FigmaUiImporter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static FigmaUiImporter.Editor.FigmaLocalization;

namespace FigmaUiImporter.Editor
{
    /// <summary>
    /// 在 FigmaLayerBinding 上按需创建/移除 Blur Group 和 Blur Backdrop。
    /// 默认导入不生成模糊节点，用户点击模式按钮时再生成。
    /// </summary>
    internal static class FigmaBlurHierarchyService
    {
        public static bool SwitchMode(FigmaLayerBinding binding, FigmaBlurMode mode)
        {
            if (binding == null || !binding.HasBlurInfo)
            {
                return false;
            }

            if (mode == FigmaBlurMode.None)
            {
                RemoveBlurGroup(binding);
                binding.SetBlurMode(FigmaBlurMode.None);
                MarkDirty(binding);
                return true;
            }

            FigmaGeneratedBlurAssets assets = new FigmaGeneratedAssetService()
                .GenerateForBinding(binding.NodeId, binding.BlurInfo);
            if (assets == null)
            {
                EditorUtility.DisplayDialog(
                    L("Figma 背景模糊", "Figma Background Blur"),
                    L("模糊资源生成失败，请检查 backdrop 图层和输出目录是否存在。",
                        "Failed to generate blur assets. Check the backdrop layer and output folder."),
                    L("确定", "OK"));
                return false;
            }

            FigmaBackgroundBlur component = binding.BlurComponent;
            GameObject group = binding.BlurGroup;
            if (component == null || group == null)
            {
                CreateBlurGroup(binding, assets, mode, out group, out component);
            }

            ConfigureComponent(binding, component, assets, mode);
            binding.SetBlurReferences(component, group, component.gameObject);
            binding.SetBlurMode(mode);
            MarkDirty(binding);
            return true;
        }

        private static void CreateBlurGroup(
            FigmaLayerBinding binding,
            FigmaGeneratedBlurAssets assets,
            FigmaBlurMode mode,
            out GameObject group,
            out FigmaBackgroundBlur component)
        {
            FigmaBlurLayerInfo info = binding.BlurInfo;
            Transform imageTransform = binding.transform;
            Transform parent = imageTransform.parent;
            int siblingIndex = imageTransform.GetSiblingIndex();

            group = new GameObject(
                FigmaPathUtility.SafeObjectName(binding.NodeName) + " Blur Group",
                typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(group, L("创建模糊分组", "Create Blur Group"));
            group.transform.SetParent(parent, false);
            group.transform.SetSiblingIndex(siblingIndex);
            SetRectFromFigmaBounds(
                group.GetComponent<RectTransform>(),
                info.rootBounds,
                info.bounds);

            GameObject backdrop = new GameObject(
                FigmaPathUtility.SafeObjectName(binding.NodeName) + " Blur Backdrop",
                typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(backdrop, L("创建模糊背景", "Create Blur Backdrop"));
            backdrop.transform.SetParent(group.transform, false);
            StretchToParent(backdrop.GetComponent<RectTransform>());

            GameObject maskObject = new GameObject("Mask", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(maskObject, L("创建模糊遮罩", "Create Blur Mask"));
            maskObject.transform.SetParent(backdrop.transform, false);
            StretchToParent(maskObject.GetComponent<RectTransform>());
            Image maskImage = maskObject.AddComponent<Image>();
            maskImage.sprite = assets.MaskSprite;
            maskImage.preserveAspect = false;
            maskImage.raycastTarget = false;
            Mask mask = maskObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject blurredObject = new GameObject("Blurred Backdrop", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(blurredObject, L("创建模糊贴图", "Create Blurred Backdrop"));
            blurredObject.transform.SetParent(maskObject.transform, false);
            StretchToParent(blurredObject.GetComponent<RectTransform>());
            RawImage rawImage = blurredObject.AddComponent<RawImage>();
            rawImage.texture = assets.BlurredTexture;
            rawImage.uvRect = assets.SourceUvRect;
            rawImage.raycastTarget = false;

            Undo.SetTransformParent(imageTransform, group.transform, L("移入模糊分组", "Move into Blur Group"));
            imageTransform.SetAsLastSibling();
            RectTransform imageRect = imageTransform as RectTransform;
            if (imageRect != null)
            {
                SetRectRelativeToBounds(imageRect, info.bounds, info.exportBounds);
            }

            component = backdrop.AddComponent<FigmaBackgroundBlur>();
            component.targetImage = rawImage;
            component.sourceTexture = assets.BackdropTexture;
            component.sourceDesignSize = new Vector2(
                assets.BackdropExportBounds.width,
                assets.BackdropExportBounds.height);
            component.sourceRoot = parent as RectTransform;
            component.sourceRootDesignBounds = info.rootBounds;
            component.sourceExportBounds = info.backdropExportBounds;
            component.trackedRect = group.GetComponent<RectTransform>();
            component.trackSourceUv = true;
            component.radius = Mathf.Max(0.01f, assets.BlurRadius);
            component.downsample = 2;
            component.iterations = 3;
            component.blurShader = Resources.Load<ComputeShader>("FigmaBackgroundBlur");
        }

        private static void ConfigureComponent(
            FigmaLayerBinding binding,
            FigmaBackgroundBlur component,
            FigmaGeneratedBlurAssets assets,
            FigmaBlurMode mode)
        {
            component.gameObject.SetActive(true);
            RawImage rawImage = component.targetImage;
            if (rawImage == null)
            {
                rawImage = component.GetComponentInChildren<RawImage>(true);
                component.targetImage = rawImage;
            }

            if (rawImage != null)
            {
                // 编辑模式先显示烘焙图；运行时组件会在 Play Mode 替换为 RT。
                rawImage.texture = assets.BlurredTexture;
                rawImage.uvRect = assets.SourceUvRect;
            }

            Transform maskTransform = component.transform.Find("Mask");
            if (maskTransform != null)
            {
                Image maskImage = maskTransform.GetComponent<Image>();
                if (maskImage != null)
                {
                    maskImage.sprite = assets.MaskSprite;
                }
            }

            component.sourceMode = mode == FigmaBlurMode.Dynamic
                ? FigmaBackgroundBlur.SourceMode.Camera
                : FigmaBackgroundBlur.SourceMode.StaticTexture;
            component.useRuntimeBlur = mode == FigmaBlurMode.Dynamic;
            component.updateEveryFrame = mode == FigmaBlurMode.Dynamic;
            component.sourceCamera = mode == FigmaBlurMode.Dynamic ? Camera.main : null;
            component.trackSourceUv = true;
            component.blurShader = Resources.Load<ComputeShader>("FigmaBackgroundBlur");
        }

        private static void RemoveBlurGroup(FigmaLayerBinding binding)
        {
            GameObject group = binding.BlurGroup;
            if (group == null)
            {
                binding.ClearBlurReferences();
                return;
            }

            FigmaBlurLayerInfo info = binding.BlurInfo;
            Transform imageTransform = binding.transform;
            Transform parent = group.transform.parent;
            int siblingIndex = group.transform.GetSiblingIndex();

            Undo.SetTransformParent(imageTransform, parent, L("移出模糊分组", "Move out of Blur Group"));
            imageTransform.SetSiblingIndex(siblingIndex);
            RectTransform imageRect = imageTransform as RectTransform;
            if (imageRect != null)
            {
                SetRectFromFigmaBounds(imageRect, info.rootBounds, info.exportBounds);
            }

            Undo.DestroyObjectImmediate(group);
            binding.ClearBlurReferences();
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

        private static void MarkDirty(FigmaLayerBinding binding)
        {
            EditorUtility.SetDirty(binding);
            EditorUtility.SetDirty(binding.gameObject);
            if (binding.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(binding.gameObject.scene);
            }
        }
    }
}
