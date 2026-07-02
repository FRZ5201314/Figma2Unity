using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    [CustomEditor(typeof(FigmaFontMappingAsset))]
    public sealed class FigmaFontMappingAssetEditor : UnityEditor.Editor
    {
        private SerializedProperty _defaultFontProperty;
        private SerializedProperty _entriesProperty;
        private ReorderableList _entriesList;

        private void OnEnable()
        {
            _defaultFontProperty = serializedObject.FindProperty("DefaultFont");
            _entriesProperty = serializedObject.FindProperty("Entries");
            _entriesList = new ReorderableList(serializedObject, _entriesProperty, true, true, true, true);
            _entriesList.drawHeaderCallback = DrawEntriesHeader;
            _entriesList.drawElementCallback = DrawEntryElement;
            _entriesList.elementHeightCallback = GetEntryHeight;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(_defaultFontProperty, new GUIContent("默认 TMP 字体"));
            EditorGUILayout.Space(4f);
            _entriesList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();

            DrawActionPanel();
        }

        private void DrawActionPanel()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("字体操作", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "更新按钮会扫描 Assets/FigmaImports 下所有已导入页面的 manifest.json。应用按钮会按当前映射表更新场景中的 Figma TMP 文字。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
            {
                if (GUILayout.Button("更新所有已导入 Figma 页面", GUILayout.Height(28f)))
                {
                    UpdateFromImportedManifests();
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("应用到选中项", GUILayout.Height(26f)))
                {
                    ApplyToSelection();
                }

                if (GUILayout.Button("应用到打开场景", GUILayout.Height(26f)))
                {
                    ApplyToOpenScenes();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEntriesHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "映射条目");
        }

        private float GetEntryHeight(int index)
        {
            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(index);
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            return entry.isExpanded ? (line * 7f) + (spacing * 8f) + 6f : line + 6f;
        }

        private void DrawEntryElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(index);
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            Rect row = new Rect(rect.x, rect.y + 3f, rect.width, line);

            entry.isExpanded = EditorGUI.Foldout(row, entry.isExpanded, BuildEntrySummary(entry), true);
            if (!entry.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            row.y += line + spacing;
            DrawTwoFields(
                row,
                entry.FindPropertyRelative("FigmaFontFamily"),
                "Figma 字体族",
                entry.FindPropertyRelative("FigmaFontStyle"),
                "样式");

            row.y += line + spacing;
            EditorGUI.PropertyField(
                row,
                entry.FindPropertyRelative("FigmaFontPostScriptName"),
                new GUIContent("PostScript 名称"));

            row.y += line + spacing;
            DrawTwoFields(
                row,
                entry.FindPropertyRelative("MinWeight"),
                "最小字重",
                entry.FindPropertyRelative("MaxWeight"),
                "最大字重");

            row.y += line + spacing;
            DrawTwoFields(
                row,
                entry.FindPropertyRelative("FontAsset"),
                "TMP 字体",
                entry.FindPropertyRelative("FontStyle"),
                "TMP 样式");

            row.y += line + spacing;
            DrawTwoFields(
                row,
                entry.FindPropertyRelative("UsageCount"),
                "使用次数",
                entry.FindPropertyRelative("LastSeenAt"),
                "最后发现");

            row.y += line + spacing;
            EditorGUI.PropertyField(row, entry.FindPropertyRelative("SampleText"), new GUIContent("文本样本"));
            EditorGUI.indentLevel--;
        }

        private static void DrawTwoFields(
            Rect row,
            SerializedProperty leftProperty,
            string leftLabel,
            SerializedProperty rightProperty,
            string rightLabel)
        {
            const float Gap = 8f;
            float width = (row.width - Gap) * 0.5f;
            Rect left = new Rect(row.x, row.y, width, row.height);
            Rect right = new Rect(row.x + width + Gap, row.y, width, row.height);
            EditorGUI.PropertyField(left, leftProperty, new GUIContent(leftLabel));
            EditorGUI.PropertyField(right, rightProperty, new GUIContent(rightLabel));
        }

        private static string BuildEntrySummary(SerializedProperty entry)
        {
            string family = entry.FindPropertyRelative("FigmaFontFamily").stringValue;
            string style = entry.FindPropertyRelative("FigmaFontStyle").stringValue;
            string postScriptName = entry.FindPropertyRelative("FigmaFontPostScriptName").stringValue;
            Object fontAsset = entry.FindPropertyRelative("FontAsset").objectReferenceValue;

            string figmaName = string.IsNullOrEmpty(family) ? "(未命名字体)" : family;
            if (!string.IsNullOrEmpty(style))
            {
                figmaName += " " + style;
            }

            if (!string.IsNullOrEmpty(postScriptName))
            {
                figmaName += " / " + postScriptName;
            }

            string unityName = fontAsset == null ? "未映射" : fontAsset.name;
            return figmaName + "  ->  " + unityName;
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

        private void ApplyToSelection()
        {
            int count = FigmaFontMappingService.ApplyMappingToSelection((FigmaFontMappingAsset)target);
            EditorUtility.DisplayDialog("字体映射已应用", "已更新选中项中的 TMP 文字：" + count + " 个。", "确定");
        }

        private void ApplyToOpenScenes()
        {
            int count = FigmaFontMappingService.ApplyMappingToOpenScenes((FigmaFontMappingAsset)target);
            EditorUtility.DisplayDialog("字体映射已应用", "已更新打开场景中的 TMP 文字：" + count + " 个。", "确定");
        }
    }
}
