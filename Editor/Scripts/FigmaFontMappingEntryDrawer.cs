using TMPro;
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

            float fontFieldWidth = Mathf.Clamp(row.width * 0.42f, 180f, 300f);
            Rect summaryRect = new Rect(row.x, row.y, row.width - fontFieldWidth - Gap, line);
            Rect fontRect = new Rect(summaryRect.xMax + Gap, row.y, fontFieldWidth, line);
            property.isExpanded = EditorGUI.Foldout(
                summaryRect,
                property.isExpanded,
                new GUIContent(BuildSummary(familyProperty, styleProperty, minWeightProperty, maxWeightProperty, fontAssetProperty)),
                true);
            EditorGUI.PropertyField(fontRect, fontAssetProperty, GUIContent.none);

            if (property.isExpanded)
            {
                row.y += line + spacing;
                Rect contentRow = new Rect(row.x + DetailIndent, row.y, row.width - DetailIndent, line);
                EditorGUI.PropertyField(contentRow, fontStyleProperty, new GUIContent("TMP 样式"));

                row.y += line + spacing;
                contentRow.y = row.y;
                DrawTwoFields(
                    contentRow,
                    familyProperty,
                    "Figma 字体族",
                    styleProperty,
                    "Figma 样式",
                    0.58f);

                row.y += line + spacing;
                contentRow.y = row.y;
                EditorGUI.PropertyField(contentRow, postScriptProperty, new GUIContent("PostScript 名称"));

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
                DrawTwoFields(
                    contentRow,
                    usageCountProperty,
                    "使用次数",
                    lastSeenAtProperty,
                    "最后发现",
                    0.34f);

                row.y += line + spacing;
                contentRow.y = row.y;
                EditorGUI.PropertyField(contentRow, sampleTextProperty, new GUIContent("文本样本"));
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            int lineCount = property.isExpanded ? 7 : 1;
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            return 4f + (line * lineCount) + (spacing * (lineCount - 1));
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
            EditorGUI.PropertyField(left, leftProperty, new GUIContent(leftLabel));
            EditorGUI.PropertyField(right, rightProperty, new GUIContent(rightLabel));
        }

        private static string BuildSummary(
            SerializedProperty familyProperty,
            SerializedProperty styleProperty,
            SerializedProperty minWeightProperty,
            SerializedProperty maxWeightProperty,
            SerializedProperty fontAssetProperty)
        {
            TMP_FontAsset fontAsset = fontAssetProperty.objectReferenceValue as TMP_FontAsset;
            string prefix = fontAsset == null ? "未配置" : "已映射";
            return prefix + " | " + BuildFigmaName(familyProperty, styleProperty, minWeightProperty, maxWeightProperty);
        }

        private static string BuildFigmaName(
            SerializedProperty familyProperty,
            SerializedProperty styleProperty,
            SerializedProperty minWeightProperty,
            SerializedProperty maxWeightProperty)
        {
            string family = string.IsNullOrWhiteSpace(familyProperty.stringValue) ? "(未知字体族)" : familyProperty.stringValue;
            string style = string.IsNullOrWhiteSpace(styleProperty.stringValue) ? string.Empty : " " + styleProperty.stringValue;
            string weight = BuildWeightLabel(minWeightProperty.intValue, maxWeightProperty.intValue);
            return family + style + weight;
        }

        private static string BuildWeightLabel(int minWeight, int maxWeight)
        {
            if (minWeight <= 0 && maxWeight <= 0)
            {
                return string.Empty;
            }

            if (minWeight == maxWeight || maxWeight <= 0)
            {
                return " / W" + minWeight;
            }

            return " / W" + Mathf.Min(minWeight, maxWeight) + "-" + Mathf.Max(minWeight, maxWeight);
        }
    }
}
