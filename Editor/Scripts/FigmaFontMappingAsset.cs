using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    [CreateAssetMenu(fileName = "FigmaFontMapping", menuName = "Figma2Unity/Font Mapping")]
    public sealed class FigmaFontMappingAsset : ScriptableObject
    {
        public TMP_FontAsset DefaultFont;
        public List<Entry> Entries = new List<Entry>();

        public TMP_FontAsset ResolveFont(FigmaTextStyleInfo textStyle, out FontStyles mappedStyle)
        {
            bool matchedEntry;
            return ResolveFont(textStyle, out mappedStyle, out matchedEntry);
        }

        public TMP_FontAsset ResolveFont(
            FigmaTextStyleInfo textStyle,
            out FontStyles mappedStyle,
            out bool matchedEntry)
        {
            mappedStyle = FontStyles.Normal;
            matchedEntry = false;
            if (textStyle == null)
            {
                return DefaultFont;
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                Entry entry = Entries[i];
                if (entry != null && entry.FontAsset != null && entry.MatchesPostScriptName(textStyle))
                {
                    mappedStyle = entry.FontStyle;
                    matchedEntry = true;
                    return entry.FontAsset;
                }
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                Entry entry = Entries[i];
                if (entry != null && entry.FontAsset != null && entry.MatchesFamilyAndStyle(textStyle))
                {
                    mappedStyle = entry.FontStyle;
                    matchedEntry = true;
                    return entry.FontAsset;
                }
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                Entry entry = Entries[i];
                if (entry != null && entry.FontAsset != null && entry.MatchesFamilyAndWeight(textStyle))
                {
                    mappedStyle = entry.FontStyle;
                    matchedEntry = true;
                    return entry.FontAsset;
                }
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                Entry entry = Entries[i];
                if (entry != null && entry.FontAsset != null && entry.MatchesFamily(textStyle))
                {
                    mappedStyle = entry.FontStyle;
                    matchedEntry = true;
                    return entry.FontAsset;
                }
            }

            return DefaultFont;
        }

        [Serializable]
        public sealed class Entry
        {
            public string FigmaFontFamily;
            public string FigmaFontStyle;
            public string FigmaFontPostScriptName;
            public int MinWeight;
            public int MaxWeight;
            public int UsageCount;
            public string SampleText;
            public string LastSeenAt;
            public TMP_FontAsset FontAsset;
            public FontStyles FontStyle = FontStyles.Normal;

            public bool MatchesPostScriptName(FigmaTextStyleInfo textStyle)
            {
                return !string.IsNullOrEmpty(Normalize(FigmaFontPostScriptName))
                    && string.Equals(
                        Normalize(FigmaFontPostScriptName),
                        Normalize(textStyle == null ? string.Empty : textStyle.FontPostScriptName),
                        StringComparison.OrdinalIgnoreCase);
            }

            public bool MatchesFamilyAndStyle(FigmaTextStyleInfo textStyle)
            {
                return MatchesFamily(textStyle)
                    && !string.IsNullOrEmpty(Normalize(FigmaFontStyle))
                    && string.Equals(
                        Normalize(FigmaFontStyle),
                        Normalize(textStyle == null ? string.Empty : textStyle.FontStyleName),
                        StringComparison.OrdinalIgnoreCase);
            }

            public bool MatchesFamilyAndWeight(FigmaTextStyleInfo textStyle)
            {
                if (!MatchesFamily(textStyle) || MinWeight <= 0 || MaxWeight <= 0)
                {
                    return false;
                }

                int min = Mathf.Min(MinWeight, MaxWeight);
                int max = Mathf.Max(MinWeight, MaxWeight);
                return textStyle.FontWeight >= min && textStyle.FontWeight <= max;
            }

            public bool MatchesFamily(FigmaTextStyleInfo textStyle)
            {
                return textStyle != null
                    && !string.IsNullOrEmpty(Normalize(FigmaFontFamily))
                    && string.Equals(Normalize(FigmaFontFamily), Normalize(textStyle.FontFamily), StringComparison.OrdinalIgnoreCase);
            }

            private static string Normalize(string value)
            {
                return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            }
        }
    }
}
