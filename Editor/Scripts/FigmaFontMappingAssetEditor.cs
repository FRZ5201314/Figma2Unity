using UnityEditor;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    [CustomEditor(typeof(FigmaFontMappingAsset))]
    public sealed class FigmaFontMappingAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("导入字体发现", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "扫描 Assets/FigmaImports 下所有已导入页面的 manifest.json，并把其中的 Figma Text 字体信息补充到当前映射表。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
            {
                if (GUILayout.Button("更新所有已导入 Figma 页面", GUILayout.Height(28f)))
                {
                    UpdateFromImportedManifests();
                }
            }
        }

        private void UpdateFromImportedManifests()
        {
            FigmaFontMappingAsset mapping = (FigmaFontMappingAsset)target;
            Undo.RecordObject(mapping, "更新 Figma 字体映射");

            FontMappingUpdateResult result = FigmaFontMappingService.UpdateFromImportedManifests(mapping);
            EditorUtility.SetDirty(mapping);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog(
                "字体映射已更新",
                "扫描 manifest：" + result.SourceFileCount
                + " 个\n文本样式：" + result.TextStyleCount
                + " 个\n新增条目：" + result.AddedEntries
                + " 个\n更新条目：" + result.UpdatedEntries
                + " 个\n当前总条目：" + result.TotalEntries
                + " 个",
                "确定");
        }
    }
}
