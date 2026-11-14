using FigmaUiImporter;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FigmaUiImporter.Editor.FigmaLocalization;

namespace FigmaUiImporter.Editor
{
    [CustomEditor(typeof(FigmaLayerBinding))]
    public sealed class FigmaLayerBindingEditor : UnityEditor.Editor
    {
        private SerializedProperty _nodeIdProperty;
        private SerializedProperty _nodeNameProperty;
        private SerializedProperty _nodeTypeProperty;
        private SerializedProperty _assetPathProperty;
        private SerializedProperty _siblingIndexProperty;

        private void OnEnable()
        {
            _nodeIdProperty = serializedObject.FindProperty("nodeId");
            _nodeNameProperty = serializedObject.FindProperty("nodeName");
            _nodeTypeProperty = serializedObject.FindProperty("nodeType");
            _assetPathProperty = serializedObject.FindProperty("assetPath");
            _siblingIndexProperty = serializedObject.FindProperty("siblingIndex");
            FigmaLocalization.LanguageChanged += Repaint;
        }

        private void OnDisable()
        {
            FigmaLocalization.LanguageChanged -= Repaint;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(_nodeIdProperty, new GUIContent("Figma Node Id"));
                EditorGUILayout.PropertyField(_nodeNameProperty, new GUIContent(L("Figma 节点名", "Figma Node Name")));
                EditorGUILayout.PropertyField(_nodeTypeProperty, new GUIContent(L("Figma 类型", "Figma Type")));
                EditorGUILayout.PropertyField(_assetPathProperty, new GUIContent(L("PNG 资源", "PNG Asset")));
                EditorGUILayout.PropertyField(_siblingIndexProperty, new GUIContent("Sibling Index"));
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8f);
            DrawTextConversionControls((FigmaLayerBinding)target);

            EditorGUILayout.Space(8f);
            DrawBlurControls((FigmaLayerBinding)target);
        }

        private void DrawBlurControls(FigmaLayerBinding binding)
        {
            if (binding == null || !binding.HasBlurInfo)
            {
                return;
            }

            EditorGUILayout.LabelField(L("背景模糊", "Background Blur"), EditorStyles.boldLabel);
            string status;
            switch (binding.BlurMode)
            {
                case FigmaBlurMode.Baked:
                    status = L("当前：烘焙模糊", "Current: Baked Blur");
                    break;
                case FigmaBlurMode.Dynamic:
                    status = L("当前：动态模糊（Camera.main）", "Current: Dynamic Blur (Camera.main)");
                    break;
                default:
                    status = L("当前：不使用模糊", "Current: No Blur");
                    break;
            }

            EditorGUILayout.HelpBox(status, MessageType.None);

            if (binding.BlurMode == FigmaBlurMode.Dynamic && Camera.main == null)
            {
                EditorGUILayout.HelpBox(
                    L("动态模糊使用 Camera.main；当前场景没有 MainCamera 标记的相机，运行时会回退为无实时背景。",
                        "Dynamic blur uses Camera.main; no MainCamera-tagged camera is present, so runtime blur may not update."),
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("不使用模糊效果", "No Blur")))
                {
                    SwitchBlur(binding, FigmaBlurMode.None);
                }

                if (GUILayout.Button(L("使用烘焙模糊效果", "Use Baked Blur")))
                {
                    SwitchBlur(binding, FigmaBlurMode.Baked);
                }

                if (GUILayout.Button(L("使用动态模糊", "Use Dynamic Blur")))
                {
                    SwitchBlur(binding, FigmaBlurMode.Dynamic);
                }
            }
        }

        private void SwitchBlur(FigmaLayerBinding binding, FigmaBlurMode mode)
        {
            if (!FigmaBlurHierarchyService.SwitchMode(binding, mode))
            {
                return;
            }

            serializedObject.Update();
            Repaint();
            SceneView.RepaintAll();
        }

        private static void DrawTextConversionControls(FigmaLayerBinding binding)
        {
            EditorGUILayout.LabelField(L("文本表现", "Text Appearance"), EditorStyles.boldLabel);

            if (!FigmaLayerConversionService.IsTextNode(binding))
            {
                EditorGUILayout.HelpBox(L("只有 Figma TEXT 节点支持 TMP / PNG 切换。", "Only Figma TEXT nodes support TMP / PNG switching."), MessageType.Info);
                return;
            }

            TextMeshProUGUI text = binding.GetComponent<TextMeshProUGUI>();
            Image image = binding.GetComponent<Image>();
            string status = text != null
                ? L("当前：TextMeshProUGUI", "Current: TextMeshProUGUI")
                : image != null
                    ? L("当前：PNG Image", "Current: PNG Image")
                    : L("当前：未找到 TMP 或 Image 组件", "Current: no TMP or Image component");
            EditorGUILayout.HelpBox(status, MessageType.None);

            string toImageReason;
            bool canConvertToImage = FigmaLayerConversionService.CanConvertToImage(binding, out toImageReason);
            using (new EditorGUI.DisabledScope(!canConvertToImage))
            {
                if (GUILayout.Button(L("将 TMP 文本转为 PNG 图片", "Convert TMP Text to PNG Image")))
                {
                    Convert(binding, true);
                }
            }

            string toTextReason;
            bool canConvertToText = FigmaLayerConversionService.CanConvertToText(binding, out toTextReason);
            using (new EditorGUI.DisabledScope(!canConvertToText))
            {
                if (GUILayout.Button(L("将 PNG 图片转为 TMP 文本", "Convert PNG Image to TMP Text")))
                {
                    Convert(binding, false);
                }
            }

            if (!canConvertToImage && text != null)
            {
                EditorGUILayout.HelpBox(toImageReason, MessageType.Warning);
            }

            if (!canConvertToText && image != null)
            {
                EditorGUILayout.HelpBox(toTextReason, MessageType.Warning);
            }
        }

        private static void Convert(FigmaLayerBinding binding, bool toImage)
        {
            string message;
            bool ok = toImage
                ? FigmaLayerConversionService.ConvertToImage(binding, out message)
                : FigmaLayerConversionService.ConvertToText(binding, out message);

            if (!ok)
            {
                EditorUtility.DisplayDialog(L("Figma 文本转换", "Figma Text Conversion"), message, L("确定", "OK"));
                return;
            }

                Debug.Log("[Figma2Unity] " + message, binding);
        }
    }
}
