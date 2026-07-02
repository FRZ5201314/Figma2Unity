using UnityEditor;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    [CustomPropertyDrawer(typeof(FigmaFontMappingAsset.Entry))]
    public sealed class FigmaFontMappingEntryDrawer : PropertyDrawer
    {
        private const float Gap = 8f;
        private const float DetailIndent = 14f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty familyProperty = property.FindPropertyRelative("FigmaFontFamily");
            SerializedProperty styleProperty = property.FindPropertyRelative("FigmaFontStyle");
            SerializedProperty postScriptProperty = property.FindPropertyRelative("FigmaFontPostScriptName");
            SerializedProperty minWeightProperty = property.FindPropertyRelative("MinWeight");
            SerializedProperty maxWeightProperty = property.FindPropertyRelative("MaxWeight");
            SerializedProperty usageCountProperty = property.FindPropertyRelative("UsageCount");
            SerializedProperty sampleTextProperty = property.FindPropertyRelative("SampleText");
            SerializedProperty lastSeenAtProperty = property.FindPropertyRelative("LastSeenAt");
            SerializedProperty fontAssetProperty = property.FindPropertyRelative("FontAsset");
            SerializedProperty fontStyleProperty = property.FindPropertyRelative("FontStyle");

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            Rect row = new Rect(position.x, position.y + 2f, position.width, line);

            EditorGUI.BeginProperty(position, label, property);

            property.isExpanded = EditorGUI.Foldout(
                row,
                property.isExpanded,
                new GUIContent(BuildSummary(
                    familyProperty,
                    styleProperty,
                    usageCountProperty,
                    fontAssetProperty)),
                true);

            row.y += line + spacing;
            DrawFullField(row, fontAssetProperty, "TMP 字体");

            if (property.isExpanded)
            {
                row.y += line + spacing;
                Rect contentRow = new Rect(row.x + DetailIndent, row.y, row.width - DetailIndent, line);
                DrawFullField(contentRow, fontStyleProperty, "TMP 样式");

                row.y += line + spacing;
                contentRow.y = row.y;
                DrawFullField(contentRow, familyProperty, "Figma 字体族");

                row.y += line + spacing;
                contentRow.y = row.y;
                DrawFullField(contentRow, styleProperty, "Figma 样式");

                row.y += line + spacing;
                contentRow.y = row.y;
                DrawFullField(contentRow, postScriptProperty, "PostScript 名称");

                row.y += line + spacing;
                contentRow.y = row.y;
                DrawTwoFields(
                    contentRow,
                    minWeightProperty,
                    "最小字重",
                    maxWeightProperty,
                    "最大字重",
                    0.5f);

                row.y += line + spacing;
                contentRow.y = row.y;
                DrawFullField(contentRow, usageCountProperty, "使用次数");

                row.y += line + spacing;
                contentRow.y = row.y;
                DrawFullField(contentRow, lastSeenAtProperty, "最后发现");

                row.y += line + spacing;
                contentRow.y = row.y;
                DrawFullField(contentRow, sampleTextProperty, "文本样本");
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            int lineCount = property.isExpanded ? 10 : 2;
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            return 4f + (line * lineCount) + (spacing * (lineCount - 1));
        }

        private static void DrawFullField(Rect row, SerializedProperty property, string label)
        {
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(100f, row.width * 0.36f);
            EditorGUI.PropertyField(row, property, new GUIContent(label));
            EditorGUIUtility.labelWidth = oldLabelWidth;
        }

        private static void DrawTwoFields(
            Rect row,
            SerializedProperty leftProperty,
            string leftLabel,
            SerializedProperty rightProperty,
            string rightLabel,
            float leftRatio)
        {
            float leftWidth = Mathf.Max(80f, (row.width - Gap) * leftRatio);
            Rect left = new Rect(row.x, row.y, leftWidth, row.height);
            Rect right = new Rect(row.x + leftWidth + Gap, row.y, row.width - leftWidth - Gap, row.height);
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(72f, left.width * 0.48f);
            EditorGUI.PropertyField(left, leftProperty, new GUIContent(leftLabel));
            EditorGUIUtility.labelWidth = Mathf.Min(72f, right.width * 0.48f);
            EditorGUI.PropertyField(right, rightProperty, new GUIContent(rightLabel));
            EditorGUIUtility.labelWidth = oldLabelWidth;
        }

        private static string BuildSummary(
            SerializedProperty familyProperty,
            SerializedProperty styleProperty,
            SerializedProperty usageCountProperty,
            SerializedProperty fontAssetProperty)
        {
            string prefix = fontAssetProperty.objectReferenceValue == null ? "未配置" : "已映射";
            return prefix
                + " | 使用 "
                + Mathf.Max(0, usageCountProperty.intValue)
                + " | "
                + BuildFigmaName(familyProperty, styleProperty);
        }

        private static string BuildFigmaName(
            SerializedProperty familyProperty,
            SerializedProperty styleProperty)
        {
            string family = string.IsNullOrWhiteSpace(familyProperty.stringValue) ? "(未知字体族)" : familyProperty.stringValue;
            string style = string.IsNullOrWhiteSpace(styleProperty.stringValue) ? string.Empty : " " + styleProperty.stringValue;
            return family + style;
        }
    }
}
