using System;
using System.Collections.Generic;
using System.IO;
using FigmaUiImporter;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal static class FigmaFontMappingService
    {
        public const string GlobalMappingAssetPath = "Assets/FigmaImports/FigmaFontMapping.asset";

        public static FigmaFontMappingAsset GetOrCreateGlobalMapping()
        {
            FigmaFontMappingAsset mapping = AssetDatabase.LoadAssetAtPath<FigmaFontMappingAsset>(GlobalMappingAssetPath);
            if (mapping != null)
            {
                EnsureEntryList(mapping);
                return mapping;
            }

            FigmaPathUtility.EnsureAssetFolder(GetAssetDirectory(GlobalMappingAssetPath));
            mapping = ScriptableObject.CreateInstance<FigmaFontMappingAsset>();
            mapping.DefaultFont = FigmaTmpFontUtility.GetProjectDefaultFont();
            AssetDatabase.CreateAsset(mapping, GlobalMappingAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return mapping;
        }

        public static FigmaFontMappingAsset EnsureSettingsUseGlobalMapping(FigmaImportSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            FigmaFontMappingAsset mapping = GetOrCreateGlobalMapping();
            settings.FontMapping = mapping;
            return mapping;
        }

        public static FontMappingUpdateResult UpdateGlobalFromLayers(IList<ImportedLayerInfo> layers)
        {
            return UpdateFromLayers(GetOrCreateGlobalMapping(), layers);
        }

        public static FontMappingUpdateResult UpdateFromImportedManifests(FigmaFontMappingAsset mapping)
        {
            List<FigmaTextStyleInfo> styles = new List<FigmaTextStyleInfo>();
            List<string> manifestPaths = FindImportedManifestPaths();

            for (int i = 0; i < manifestPaths.Count; i++)
            {
                object root = MiniJson.Deserialize(File.ReadAllText(manifestPaths[i]));
                CollectTextStyles(root, styles);
            }

            FontMappingUpdateResult result = UpdateFromTextStyles(mapping, styles);
            result.SourceFileCount = manifestPaths.Count;
            return result;
        }

        internal static FontMappingUpdateResult UpdateFromJsonText(
            FigmaFontMappingAsset mapping,
            string json)
        {
            return UpdateFromJsonObject(mapping, MiniJson.Deserialize(json));
        }

        private static FontMappingUpdateResult UpdateFromJsonObject(
            FigmaFontMappingAsset mapping,
            object root)
        {
            List<FigmaTextStyleInfo> styles = new List<FigmaTextStyleInfo>();
            CollectTextStyles(root, styles);
            return UpdateFromTextStyles(mapping, styles);
        }

        public static FontMappingUpdateResult UpdateFromLayers(
            FigmaFontMappingAsset mapping,
            IList<ImportedLayerInfo> layers)
        {
            List<FigmaTextStyleInfo> styles = new List<FigmaTextStyleInfo>();
            if (layers != null)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    ImportedLayerInfo layer = layers[i];
                    if (layer != null && layer.HasTextStyle)
                    {
                        styles.Add(layer.TextStyle);
                    }
                }
            }

            return UpdateFromTextStyles(mapping, styles);
        }

        internal static FontMappingUpdateResult UpdateFromTextStyles(
            FigmaFontMappingAsset mapping,
            IEnumerable<FigmaTextStyleInfo> textStyles)
        {
            if (mapping == null)
            {
                throw new ArgumentNullException("mapping");
            }

            EnsureEntryList(mapping);

            FontMappingUpdateResult result = new FontMappingUpdateResult();
            result.MappingAssetPath = AssetDatabase.GetAssetPath(mapping);
            if (string.IsNullOrEmpty(result.MappingAssetPath))
            {
                result.MappingAssetPath = GlobalMappingAssetPath;
            }

            HashSet<string> touchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string now = DateTime.UtcNow.ToString("o");

            if (textStyles != null)
            {
                foreach (FigmaTextStyleInfo textStyle in textStyles)
                {
                    if (!HasFontIdentity(textStyle))
                    {
                        continue;
                    }

                    result.TextStyleCount++;
                    FigmaFontMappingAsset.Entry entry = FindEntry(mapping, textStyle);
                    bool isNew = entry == null;
                    if (isNew)
                    {
                        entry = CreateEntry(textStyle);
                        mapping.Entries.Add(entry);
                    }
                    else
                    {
                        MergeEntry(entry, textStyle);
                    }

                    entry.UsageCount = Mathf.Max(0, entry.UsageCount) + 1;
                    entry.LastSeenAt = now;
                    if (string.IsNullOrEmpty(entry.SampleText))
                    {
                        entry.SampleText = TrimSampleText(textStyle.Characters);
                    }

                    string key = BuildEntryKey(entry);
                    if (touchedKeys.Add(key))
                    {
                        if (isNew)
                        {
                            result.AddedEntries++;
                        }
                        else
                        {
                            result.UpdatedEntries++;
                        }
                    }
                }
            }

            result.TotalEntries = mapping.Entries.Count;
            if ((result.AddedEntries > 0 || result.UpdatedEntries > 0) && EditorUtility.IsPersistent(mapping))
            {
                EditorUtility.SetDirty(mapping);
                AssetDatabase.SaveAssets();
            }

            return result;
        }

        public static int ApplyGlobalMappingToSelection()
        {
            return ApplyMappingToSelection(GetOrCreateGlobalMapping());
        }

        public static int ApplyMappingToSelection(FigmaFontMappingAsset mapping)
        {
            if (mapping == null)
            {
                return 0;
            }

            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                return 0;
            }

            HashSet<int> seen = new HashSet<int>();
            List<FigmaLayerBinding> bindings = new List<FigmaLayerBinding>();
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                GameObject selectedObject = selectedObjects[i];
                if (selectedObject == null)
                {
                    continue;
                }

                FigmaLayerBinding[] childBindings = selectedObject.GetComponentsInChildren<FigmaLayerBinding>(true);
                for (int j = 0; j < childBindings.Length; j++)
                {
                    FigmaLayerBinding binding = childBindings[j];
                    if (binding != null && seen.Add(binding.GetInstanceID()))
                    {
                        bindings.Add(binding);
                    }
                }
            }

            return ApplyMappingToBindings(mapping, bindings);
        }

        public static int ApplyGlobalMappingToOpenScenes()
        {
            return ApplyMappingToOpenScenes(GetOrCreateGlobalMapping());
        }

        public static int ApplyMappingToOpenScenes(FigmaFontMappingAsset mapping)
        {
            return ApplyMappingToBindings(mapping, FindSceneLayerBindings());
        }

        public static FontStyles ResolveEffectiveFontStyle(
            FigmaTextStyleInfo textStyle,
            FontStyles mappedStyle,
            bool matchedMappedFont)
        {
            if (matchedMappedFont)
            {
                return IsMediumOrBold(textStyle)
                    ? (FontStyles)((int)mappedStyle & ~(int)FontStyles.Bold)
                    : mappedStyle;
            }

            FontStyles effectiveStyle = mappedStyle;
            if (textStyle != null && textStyle.FontWeight >= 600)
            {
                effectiveStyle |= FontStyles.Bold;
            }

            return effectiveStyle;
        }

        private static int ApplyMappingToBindings(
            FigmaFontMappingAsset mapping,
            IEnumerable<FigmaLayerBinding> bindings)
        {
            if (mapping == null || bindings == null)
            {
                return 0;
            }

            int count = 0;
            foreach (FigmaLayerBinding binding in bindings)
            {
                if (binding == null
                    || EditorUtility.IsPersistent(binding)
                    || !binding.gameObject.scene.IsValid()
                    || !FigmaLayerConversionService.IsTextNode(binding))
                {
                    continue;
                }

                TextMeshProUGUI text = binding.GetComponent<TextMeshProUGUI>();
                if (text == null)
                {
                    continue;
                }

                FigmaTextStyleInfo style;
                string reason;
                if (!FigmaLayerConversionService.TryLoadTextStyle(binding, out style, out reason))
                {
                    continue;
                }

                FontStyles mappedStyle = FontStyles.Normal;
                bool matchedMappedFont = false;
                TMP_FontAsset mappedFont = FigmaTmpFontUtility.ResolveFont(
                    text,
                    style,
                    mapping,
                    out mappedStyle,
                    out matchedMappedFont);
                if (mappedFont == null)
                {
                    continue;
                }

                Undo.RecordObject(text, "应用 Figma 字体映射");
                text.font = mappedFont;
                text.fontStyle = ResolveEffectiveFontStyle(style, mappedStyle, matchedMappedFont);
                EditorUtility.SetDirty(text);
                count++;
            }

            if (count > 0)
            {
                EditorSceneManager.MarkAllScenesDirty();
            }

            return count;
        }

        private static IEnumerable<FigmaLayerBinding> FindSceneLayerBindings()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<FigmaLayerBinding>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
            return Resources.FindObjectsOfTypeAll<FigmaLayerBinding>();
#endif
        }

        private static FigmaFontMappingAsset.Entry CreateEntry(FigmaTextStyleInfo textStyle)
        {
            FigmaFontMappingAsset.Entry entry = new FigmaFontMappingAsset.Entry();
            entry.FigmaFontFamily = SafeString(textStyle.FontFamily);
            entry.FigmaFontStyle = SafeString(textStyle.FontStyleName);
            entry.FigmaFontPostScriptName = SafeString(textStyle.FontPostScriptName);
            if (textStyle.FontWeight > 0)
            {
                entry.MinWeight = textStyle.FontWeight;
                entry.MaxWeight = textStyle.FontWeight;
            }

            entry.FontStyle = InferFontStyle(textStyle);
            entry.SampleText = TrimSampleText(textStyle.Characters);
            return entry;
        }

        private static void CollectTextStyles(object value, List<FigmaTextStyleInfo> styles)
        {
            Dictionary<string, object> dictionary = value as Dictionary<string, object>;
            if (dictionary != null)
            {
                FigmaTextStyleInfo textStyle = TryCreateTextStyle(dictionary);
                if (textStyle != null)
                {
                    styles.Add(textStyle);
                }

                foreach (KeyValuePair<string, object> pair in dictionary)
                {
                    CollectTextStyles(pair.Value, styles);
                }

                return;
            }

            List<object> list = value as List<object>;
            if (list == null)
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                CollectTextStyles(list[i], styles);
            }
        }

        private static FigmaTextStyleInfo TryCreateTextStyle(Dictionary<string, object> dictionary)
        {
            string fontFamily = ReadString(dictionary, "fontFamily");
            string postScriptName = ReadString(dictionary, "fontPostScriptName");
            if (string.IsNullOrEmpty(Normalize(fontFamily)) && string.IsNullOrEmpty(Normalize(postScriptName)))
            {
                return null;
            }

            FigmaTextStyleInfo style = new FigmaTextStyleInfo();
            style.Characters = ReadString(dictionary, "characters");
            style.FontFamily = fontFamily;
            style.FontStyleName = ReadString(dictionary, "fontStyleName");
            style.FontPostScriptName = postScriptName;
            style.FontWeight = ReadInt(dictionary, "fontWeight");
            style.FontSize = ReadFloat(dictionary, "fontSize");
            return style;
        }

        private static string ReadString(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary == null || !dictionary.TryGetValue(key, out value) || value == null)
            {
                return string.Empty;
            }

            return value as string ?? value.ToString();
        }

        private static int ReadInt(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary == null || !dictionary.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }

            if (value is int)
            {
                return (int)value;
            }

            if (value is long)
            {
                return (int)(long)value;
            }

            if (value is double)
            {
                return Mathf.RoundToInt((float)(double)value);
            }

            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
        }

        private static float ReadFloat(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary == null || !dictionary.TryGetValue(key, out value) || value == null)
            {
                return 0f;
            }

            if (value is float)
            {
                return (float)value;
            }

            if (value is double)
            {
                return (float)(double)value;
            }

            if (value is long)
            {
                return (long)value;
            }

            float parsed;
            return float.TryParse(value.ToString(), out parsed) ? parsed : 0f;
        }

        private static void MergeEntry(FigmaFontMappingAsset.Entry entry, FigmaTextStyleInfo textStyle)
        {
            if (entry == null || textStyle == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(entry.FigmaFontFamily))
            {
                entry.FigmaFontFamily = SafeString(textStyle.FontFamily);
            }

            if (string.IsNullOrEmpty(entry.FigmaFontStyle))
            {
                entry.FigmaFontStyle = SafeString(textStyle.FontStyleName);
            }

            if (string.IsNullOrEmpty(entry.FigmaFontPostScriptName))
            {
                entry.FigmaFontPostScriptName = SafeString(textStyle.FontPostScriptName);
            }

            if (textStyle.FontWeight > 0 && HasSamePostScriptName(entry, textStyle))
            {
                if (entry.MinWeight <= 0 || entry.MaxWeight <= 0)
                {
                    entry.MinWeight = textStyle.FontWeight;
                    entry.MaxWeight = textStyle.FontWeight;
                }
                else
                {
                    entry.MinWeight = Mathf.Min(entry.MinWeight, textStyle.FontWeight);
                    entry.MaxWeight = Mathf.Max(entry.MaxWeight, textStyle.FontWeight);
                }
            }
        }

        private static FigmaFontMappingAsset.Entry FindEntry(
            FigmaFontMappingAsset mapping,
            FigmaTextStyleInfo textStyle)
        {
            if (mapping == null || textStyle == null || mapping.Entries == null)
            {
                return null;
            }

            for (int i = 0; i < mapping.Entries.Count; i++)
            {
                FigmaFontMappingAsset.Entry entry = mapping.Entries[i];
                if (entry != null && HasSamePostScriptName(entry, textStyle))
                {
                    return entry;
                }
            }

            for (int i = 0; i < mapping.Entries.Count; i++)
            {
                FigmaFontMappingAsset.Entry entry = mapping.Entries[i];
                if (entry != null && HasSameFamilyStyleAndWeight(entry, textStyle))
                {
                    return entry;
                }
            }

            return null;
        }

        private static bool HasSamePostScriptName(
            FigmaFontMappingAsset.Entry entry,
            FigmaTextStyleInfo textStyle)
        {
            return !string.IsNullOrEmpty(Normalize(entry.FigmaFontPostScriptName))
                && string.Equals(
                    Normalize(entry.FigmaFontPostScriptName),
                    Normalize(textStyle.FontPostScriptName),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSameFamilyStyleAndWeight(
            FigmaFontMappingAsset.Entry entry,
            FigmaTextStyleInfo textStyle)
        {
            if (!string.Equals(Normalize(entry.FigmaFontFamily), Normalize(textStyle.FontFamily), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Normalize(entry.FigmaFontStyle), Normalize(textStyle.FontStyleName), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (textStyle.FontWeight <= 0)
            {
                return true;
            }

            if (entry.MinWeight <= 0 || entry.MaxWeight <= 0)
            {
                return false;
            }

            int min = Mathf.Min(entry.MinWeight, entry.MaxWeight);
            int max = Mathf.Max(entry.MinWeight, entry.MaxWeight);
            return textStyle.FontWeight >= min && textStyle.FontWeight <= max;
        }

        private static bool HasFontIdentity(FigmaTextStyleInfo textStyle)
        {
            return textStyle != null
                && (!string.IsNullOrEmpty(Normalize(textStyle.FontPostScriptName))
                    || !string.IsNullOrEmpty(Normalize(textStyle.FontFamily)));
        }

        private static FontStyles InferFontStyle(FigmaTextStyleInfo textStyle)
        {
            FontStyles style = FontStyles.Normal;
            if (textStyle == null)
            {
                return style;
            }

            string styleName = textStyle.FontStyleName ?? string.Empty;
            if (styleName.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0
                || styleName.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                style |= FontStyles.Italic;
            }

            return style;
        }

        private static bool IsMediumOrBold(FigmaTextStyleInfo textStyle)
        {
            if (textStyle == null)
            {
                return false;
            }

            if (textStyle.FontWeight >= 500)
            {
                return true;
            }

            string styleName = textStyle.FontStyleName ?? string.Empty;
            return styleName.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0
                || styleName.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0
                || styleName.IndexOf("SemiBold", StringComparison.OrdinalIgnoreCase) >= 0
                || styleName.IndexOf("DemiBold", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void EnsureEntryList(FigmaFontMappingAsset mapping)
        {
            if (mapping.Entries == null)
            {
                mapping.Entries = new List<FigmaFontMappingAsset.Entry>();
            }
        }

        private static string BuildEntryKey(FigmaFontMappingAsset.Entry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            string postScriptName = Normalize(entry.FigmaFontPostScriptName);
            if (!string.IsNullOrEmpty(postScriptName))
            {
                return "ps:" + postScriptName;
            }

            return "family:"
                + Normalize(entry.FigmaFontFamily)
                + "|style:"
                + Normalize(entry.FigmaFontStyle)
                + "|weight:"
                + entry.MinWeight
                + "-"
                + entry.MaxWeight;
        }

        private static string TrimSampleText(string value)
        {
            string normalized = SafeString(value).Replace("\r", " ").Replace("\n", " ");
            return normalized.Length <= 64 ? normalized : normalized.Substring(0, 64);
        }

        private static string SafeString(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private static string Normalize(string value)
        {
            return SafeString(value).ToLowerInvariant();
        }

        private static string GetAssetDirectory(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            int slashIndex = normalized.LastIndexOf("/", StringComparison.Ordinal);
            if (slashIndex <= 0)
            {
                return "Assets";
            }

            return normalized.Substring(0, slashIndex);
        }

        private static List<string> FindImportedManifestPaths()
        {
            List<string> manifestPaths = new List<string>();
            string importsRoot = FigmaPathUtility.ToFullPath(GetAssetDirectory(GlobalMappingAssetPath));
            if (!Directory.Exists(importsRoot))
            {
                return manifestPaths;
            }

            string[] files = Directory.GetFiles(importsRoot, "manifest.json", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                manifestPaths.Add(files[i]);
            }

            return manifestPaths;
        }
    }

    internal sealed class FontMappingUpdateResult
    {
        public string MappingAssetPath;
        public int TextStyleCount;
        public int AddedEntries;
        public int UpdatedEntries;
        public int TotalEntries;
        public int SourceFileCount;
    }
}
