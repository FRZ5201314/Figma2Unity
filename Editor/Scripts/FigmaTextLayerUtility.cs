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
            text.lineSpacing = style.LineHeightPx > style.FontSize + 0.01f
                ? style.LineHeightPx - style.FontSize
                : 0f;
            text.characterSpacing = style.LetterSpacing;
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
