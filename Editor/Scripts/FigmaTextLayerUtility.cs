using System;
using FigmaUiImporter;
using TMPro;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal static class FigmaTextLayerUtility
    {
        public static void ApplyTextStyle(
            TextMeshProUGUI text,
            FigmaTextStyleInfo style,
            FigmaFontMappingAsset fontMapping,
            bool raycastTarget)
        {
            if (text == null || style == null)
            {
                return;
            }

            FontStyles mappedStyle;
            bool matchedMappedFont;
            TMP_FontAsset resolvedFont = FigmaTmpFontUtility.ResolveFont(
                text,
                style,
                fontMapping,
                out mappedStyle,
                out matchedMappedFont);
            if (resolvedFont != null)
            {
                text.font = resolvedFont;
            }

            text.text = style.Characters ?? string.Empty;
            text.richText = false;
            text.fontSize = style.FontSize;
            text.color = style.Color;
            text.alignment = ResolveTextAlignment(style.HorizontalAlign, style.VerticalAlign);
            text.enableWordWrapping = !string.Equals(style.AutoResize, "WIDTH_AND_HEIGHT", StringComparison.OrdinalIgnoreCase);
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = raycastTarget;
            text.margin = Vector4.zero;

            text.fontStyle = FigmaFontMappingService.ResolveEffectiveFontStyle(style, mappedStyle, matchedMappedFont);
            text.lineSpacing = ResolveTmpLineSpacing(style);
            text.characterSpacing = ResolveTmpCharacterSpacing(style);
        }

        /// <summary>
        /// TMP 的 lineSpacing / characterSpacing 使用字体单位（约 1/100 em），
        /// Figma 返回的是像素值，需要按字号换算后再写入。
        /// </summary>
        internal static float PixelsToTmpFontUnits(float pixels, float fontSize)
        {
            return fontSize > 0.01f ? pixels / fontSize * 100f : 0f;
        }

        internal static float ResolveTmpLineSpacing(FigmaTextStyleInfo style)
        {
            if (style == null || style.FontSize <= 0.01f || style.LineHeightPx <= style.FontSize + 0.01f)
            {
                return 0f;
            }

            return PixelsToTmpFontUnits(style.LineHeightPx - style.FontSize, style.FontSize);
        }

        internal static float ResolveTmpCharacterSpacing(FigmaTextStyleInfo style)
        {
            return style == null ? 0f : PixelsToTmpFontUnits(style.LetterSpacing, style.FontSize);
        }

        public static TextAlignmentOptions ResolveTextAlignment(string horizontal, string vertical)
        {
            bool isCenterX = string.Equals(horizontal, "CENTER", StringComparison.OrdinalIgnoreCase);
            bool isRight = string.Equals(horizontal, "RIGHT", StringComparison.OrdinalIgnoreCase);
            bool isCenterY = string.Equals(vertical, "CENTER", StringComparison.OrdinalIgnoreCase);
            bool isBottom = string.Equals(vertical, "BOTTOM", StringComparison.OrdinalIgnoreCase);

            if (isBottom)
            {
                if (isRight)
                {
                    return TextAlignmentOptions.BottomRight;
                }

                return isCenterX ? TextAlignmentOptions.Bottom : TextAlignmentOptions.BottomLeft;
            }

            if (isCenterY)
            {
                if (isRight)
                {
                    return TextAlignmentOptions.Right;
                }

                return isCenterX ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            }

            if (isRight)
            {
                return TextAlignmentOptions.TopRight;
            }

            return isCenterX ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;
        }

        public static string ResolveHorizontalAlign(TextAlignmentOptions alignment)
        {
            int horizontal = (int)alignment & 0xF;
            if (horizontal == 0x4)
            {
                return "RIGHT";
            }

            if (horizontal == 0x2)
            {
                return "CENTER";
            }

            return "LEFT";
        }

        public static string ResolveVerticalAlign(TextAlignmentOptions alignment)
        {
            int vertical = (int)alignment & 0xF00;
            if (vertical == 0x400)
            {
                return "BOTTOM";
            }

            if (vertical == 0x200)
            {
                return "CENTER";
            }

            return "TOP";
        }
    }
}
