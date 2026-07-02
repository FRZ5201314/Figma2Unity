using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace FigmaUiImporter.Editor.Tests
{
    public sealed class FigmaFontMappingServiceTests
    {
        [Test]
        public void UpdateFromLayersAddsUniqueEntriesAndCountsUses()
        {
            FigmaFontMappingAsset mapping = ScriptableObject.CreateInstance<FigmaFontMappingAsset>();
            try
            {
                List<ImportedLayerInfo> layers = new List<ImportedLayerInfo>
                {
                    CreateTextLayer("Title", "Inter", "Regular", "Inter-Regular", 400, "Hello"),
                    CreateTextLayer("Subtitle", "Inter", "Regular", "Inter-Regular", 400, "World"),
                    CreateTextLayer("Action", "Inter", "Bold", "Inter-Bold", 700, "Start")
                };

                FontMappingUpdateResult result = FigmaFontMappingService.UpdateFromLayers(mapping, layers);

                Assert.AreEqual(3, result.TextStyleCount);
                Assert.AreEqual(2, result.AddedEntries);
                Assert.AreEqual(2, result.TotalEntries);
                Assert.AreEqual("Inter-Regular", mapping.Entries[0].FigmaFontPostScriptName);
                Assert.AreEqual(2, mapping.Entries[0].UsageCount);
                Assert.AreEqual("Inter-Bold", mapping.Entries[1].FigmaFontPostScriptName);
                Assert.AreEqual(700, mapping.Entries[1].MinWeight);
                Assert.AreEqual(700, mapping.Entries[1].MaxWeight);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mapping);
            }
        }

        [Test]
        public void UpdateFromLayersIncludesTextMetadataWhenLayerStaysImage()
        {
            FigmaFontMappingAsset mapping = ScriptableObject.CreateInstance<FigmaFontMappingAsset>();
            try
            {
                ImportedLayerInfo imageTextLayer = CreateTextLayer("Title", "HarmonyOS Sans", "Medium", string.Empty, 500, "标题");
                imageTextLayer.Kind = ImportedLayerKind.Image;

                FontMappingUpdateResult result = FigmaFontMappingService.UpdateFromLayers(
                    mapping,
                    new List<ImportedLayerInfo> { imageTextLayer });

                Assert.AreEqual(1, result.TextStyleCount);
                Assert.AreEqual(1, result.AddedEntries);
                Assert.AreEqual("HarmonyOS Sans", mapping.Entries[0].FigmaFontFamily);
                Assert.AreEqual("Medium", mapping.Entries[0].FigmaFontStyle);
                Assert.AreEqual(500, mapping.Entries[0].MinWeight);
                Assert.AreEqual(500, mapping.Entries[0].MaxWeight);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mapping);
            }
        }

        [Test]
        public void UpdateFromJsonTextScansNestedTextObjects()
        {
            FigmaFontMappingAsset mapping = ScriptableObject.CreateInstance<FigmaFontMappingAsset>();
            try
            {
                string json = "{"
                    + "\"manifest\":{\"layers\":["
                    + "{\"kind\":\"image\",\"text\":{\"characters\":\"A\",\"fontFamily\":\"Inter\",\"fontStyleName\":\"Regular\",\"fontWeight\":400}},"
                    + "{\"kind\":\"text\",\"text\":{\"characters\":\"B\",\"fontFamily\":\"Inter\",\"fontStyleName\":\"Regular\",\"fontWeight\":400}}"
                    + "]}}";

                FontMappingUpdateResult result = FigmaFontMappingService.UpdateFromJsonText(mapping, json);

                Assert.AreEqual(2, result.TextStyleCount);
                Assert.AreEqual(1, result.AddedEntries);
                Assert.AreEqual(1, result.TotalEntries);
                Assert.AreEqual(2, mapping.Entries[0].UsageCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mapping);
            }
        }

        [Test]
        public void ResolveEffectiveFontStyleDoesNotBoldConfiguredMediumOrBoldFont()
        {
            FigmaTextStyleInfo style = new FigmaTextStyleInfo();
            style.FontFamily = "Inter";
            style.FontStyleName = "Bold";
            style.FontWeight = 700;

            FontStyles result = FigmaFontMappingService.ResolveEffectiveFontStyle(
                style,
                FontStyles.Bold,
                true);

            Assert.AreEqual(FontStyles.Normal, result);
        }

        [Test]
        public void ResolveEffectiveFontStyleKeepsBoldFallbackWhenNoMappedFontMatched()
        {
            FigmaTextStyleInfo style = new FigmaTextStyleInfo();
            style.FontFamily = "Inter";
            style.FontStyleName = "Bold";
            style.FontWeight = 700;

            FontStyles result = FigmaFontMappingService.ResolveEffectiveFontStyle(
                style,
                FontStyles.Normal,
                false);

            Assert.AreEqual(FontStyles.Bold, result);
        }

        private static ImportedLayerInfo CreateTextLayer(
            string name,
            string family,
            string styleName,
            string postScriptName,
            int weight,
            string characters)
        {
            ImportedLayerInfo layer = new ImportedLayerInfo();
            layer.NodeId = name;
            layer.NodeName = name;
            layer.NodeType = "TEXT";
            layer.Kind = ImportedLayerKind.Text;
            layer.TextStyle = new FigmaTextStyleInfo();
            layer.TextStyle.Characters = characters;
            layer.TextStyle.FontFamily = family;
            layer.TextStyle.FontStyleName = styleName;
            layer.TextStyle.FontPostScriptName = postScriptName;
            layer.TextStyle.FontWeight = weight;
            layer.TextStyle.FontSize = 16f;
            return layer;
        }
    }
}
