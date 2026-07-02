using FigmaUiImporter;
using TMPro;
using UnityEditor;

namespace FigmaUiImporter.Editor
{
    internal static class FigmaTmpFontUtility
    {
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string DefaultFontPropertyName = "m_defaultFontAsset";

        public static TMP_FontAsset ResolveFont(
            TextMeshProUGUI text,
            FigmaTextStyleInfo style,
            FigmaFontMappingAsset fontMapping)
        {
            FontStyles mappedStyle;
            bool matchedMappedFont;
            return ResolveFont(text, style, fontMapping, out mappedStyle, out matchedMappedFont);
        }

        public static TMP_FontAsset ResolveFont(
            TextMeshProUGUI text,
            FigmaTextStyleInfo style,
            FigmaFontMappingAsset fontMapping,
            out FontStyles mappedStyle,
            out bool matchedMappedFont)
        {
            mappedStyle = FontStyles.Normal;
            matchedMappedFont = false;
            TMP_FontAsset mappedFont = fontMapping == null
                ? null
                : fontMapping.ResolveFont(style, out mappedStyle, out matchedMappedFont);
            if (mappedFont != null && matchedMappedFont)
            {
                return mappedFont;
            }

            TMP_FontAsset looseMappedFont = FindLooseMappedFont(fontMapping, style, out mappedStyle, out matchedMappedFont);
            if (looseMappedFont != null)
            {
                return looseMappedFont;
            }

            if (mappedFont != null)
            {
                mappedStyle = FontStyles.Normal;
                matchedMappedFont = false;
                return mappedFont;
            }

            if (text != null && text.font != null)
            {
                return text.font;
            }

            return GetProjectDefaultFont();
        }

        private static TMP_FontAsset FindLooseMappedFont(
            FigmaFontMappingAsset fontMapping,
            FigmaTextStyleInfo style,
            out FontStyles mappedStyle,
            out bool matchedMappedFont)
        {
            mappedStyle = FontStyles.Normal;
            matchedMappedFont = false;
            if (fontMapping == null || fontMapping.Entries == null)
            {
                return null;
            }

            TMP_FontAsset firstConfiguredFont = null;
            FontStyles firstConfiguredStyle = FontStyles.Normal;
            for (int i = 0; i < fontMapping.Entries.Count; i++)
            {
                FigmaFontMappingAsset.Entry entry = fontMapping.Entries[i];
                if (entry == null || entry.FontAsset == null)
                {
                    continue;
                }

                if (firstConfiguredFont == null)
                {
                    firstConfiguredFont = entry.FontAsset;
                    firstConfiguredStyle = entry.FontStyle;
                }

                if (HasSamePostScriptName(entry, style)
                    || HasSameFamilyAndStyle(entry, style)
                    || HasSameFamilyAndWeight(entry, style)
                    || HasSameFamily(entry, style))
                {
                    mappedStyle = entry.FontStyle;
                    matchedMappedFont = true;
                    return entry.FontAsset;
                }
            }

            if (firstConfiguredFont != null)
            {
                mappedStyle = firstConfiguredStyle;
                matchedMappedFont = true;
            }

            return firstConfiguredFont;
        }

        private static bool HasSamePostScriptName(
            FigmaFontMappingAsset.Entry entry,
            FigmaTextStyleInfo style)
        {
            return !string.IsNullOrEmpty(Normalize(entry.FigmaFontPostScriptName))
                && string.Equals(
                    Normalize(entry.FigmaFontPostScriptName),
                    Normalize(style == null ? string.Empty : style.FontPostScriptName),
                    System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSameFamilyAndStyle(
            FigmaFontMappingAsset.Entry entry,
            FigmaTextStyleInfo style)
        {
            return HasSameFamily(entry, style)
                && !string.IsNullOrEmpty(Normalize(entry.FigmaFontStyle))
                && string.Equals(
                    Normalize(entry.FigmaFontStyle),
                    Normalize(style == null ? string.Empty : style.FontStyleName),
                    System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSameFamilyAndWeight(
            FigmaFontMappingAsset.Entry entry,
            FigmaTextStyleInfo style)
        {
            if (!HasSameFamily(entry, style)
                || style == null
                || style.FontWeight <= 0
                || entry.MinWeight <= 0
                || entry.MaxWeight <= 0)
            {
                return false;
            }

            int min = UnityEngine.Mathf.Min(entry.MinWeight, entry.MaxWeight);
            int max = UnityEngine.Mathf.Max(entry.MinWeight, entry.MaxWeight);
            return style.FontWeight >= min && style.FontWeight <= max;
        }

        private static bool HasSameFamily(
            FigmaFontMappingAsset.Entry entry,
            FigmaTextStyleInfo style)
        {
            return style != null
                && !string.IsNullOrEmpty(Normalize(entry.FigmaFontFamily))
                && string.Equals(
                    Normalize(entry.FigmaFontFamily),
                    Normalize(style.FontFamily),
                    System.StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        public static bool TryEnsureProjectDefaultFont(
            TMP_FontAsset preferredFont,
            out TMP_FontAsset defaultFont,
            out string reason)
        {
            reason = string.Empty;
            defaultFont = GetProjectDefaultFont();
            if (defaultFont != null)
            {
                return true;
            }

            if (preferredFont == null)
            {
                preferredFont = FindAnyFontAsset();
            }

            if (preferredFont == null)
            {
                reason = "未找到可用 TMP 字体。请先导入 TextMeshPro Essential Resources，并在 FigmaFontMapping.asset 中配置默认 TMP 字体或对应 FontAsset。";
                return false;
            }

            SerializedObject settingsObject;
            SerializedProperty defaultFontProperty;
            if (!TryGetDefaultFontProperty(out settingsObject, out defaultFontProperty))
            {
                reason = "未找到 TMP Settings.asset。请先执行 Window/TextMeshPro/Import TMP Essential Resources。";
                return false;
            }

            defaultFontProperty.objectReferenceValue = preferredFont;
            settingsObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(settingsObject.targetObject);
            AssetDatabase.SaveAssets();
            defaultFont = preferredFont;
            return true;
        }

        public static TMP_FontAsset GetProjectDefaultFont()
        {
            SerializedObject settingsObject;
            SerializedProperty defaultFontProperty;
            if (!TryGetDefaultFontProperty(out settingsObject, out defaultFontProperty))
            {
                return null;
            }

            return defaultFontProperty.objectReferenceValue as TMP_FontAsset;
        }

        private static bool TryGetDefaultFontProperty(
            out SerializedObject settingsObject,
            out SerializedProperty defaultFontProperty)
        {
            settingsObject = null;
            defaultFontProperty = null;

            TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:TMP_Settings");
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(path);
                    if (settings != null)
                    {
                        break;
                    }
                }
            }

            if (settings == null)
            {
                return false;
            }

            settingsObject = new SerializedObject(settings);
            defaultFontProperty = settingsObject.FindProperty(DefaultFontPropertyName);
            return defaultFontProperty != null;
        }

        private static TMP_FontAsset FindAnyFontAsset()
        {
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (font != null)
                {
                    return font;
                }
            }

            return null;
        }
    }
}
