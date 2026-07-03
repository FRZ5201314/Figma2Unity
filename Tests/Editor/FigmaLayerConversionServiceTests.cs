using System.IO;
using FigmaUiImporter;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FigmaUiImporter.Editor.Tests
{
    public sealed class FigmaLayerConversionServiceTests
    {
        private const string TestFolder = "Assets/FigmaUiImporterLayerConversionTests";

        [SetUp]
        public void SetUp()
        {
            Cleanup();
        }

        [TearDown]
        public void TearDown()
        {
            Cleanup();
        }

        [Test]
        public void TryFindManifestPathWalksUpFromLayerPng()
        {
            string manifestPath = FigmaPathUtility.CombineAssetPath(TestFolder, "manifest.json");
            WriteTextAsset(manifestPath, "{\"layers\":[]}");

            string foundPath;
            bool found = FigmaLayerConversionService.TryFindManifestPath(
                FigmaPathUtility.CombineAssetPath(TestFolder, "layers", "3_4.png"),
                out foundPath);

            Assert.IsTrue(found);
            Assert.AreEqual(manifestPath, foundPath);
        }

        [Test]
        public void TryLoadTextStyleUsesManifestNodeId()
        {
            string manifestPath = FigmaPathUtility.CombineAssetPath(TestFolder, "manifest.json");
            WriteTextAsset(
                manifestPath,
                "{"
                + "\"layers\":["
                + "{\"nodeId\":\"5:6\",\"type\":\"TEXT\","
                + "\"text\":{\"characters\":\"Hello\",\"fontFamily\":\"Inter\",\"fontStyleName\":\"Regular\","
                + "\"fontPostScriptName\":\"Inter-Regular\",\"fontWeight\":400,\"fontSize\":18,"
                + "\"color\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1}}}"
                + "]}");

            GameObject gameObject = new GameObject("Title");
            try
            {
                FigmaLayerBinding binding = gameObject.AddComponent<FigmaLayerBinding>();
                binding.SetLayerInfo(
                    "5:6",
                    "Title",
                    "TEXT",
                    FigmaPathUtility.CombineAssetPath(TestFolder, "layers", "5_6.png"),
                    0);

                FigmaTextStyleInfo style;
                string reason;
                bool found = FigmaLayerConversionService.TryLoadTextStyle(binding, out style, out reason);

                Assert.IsTrue(found, reason);
                Assert.AreEqual("Hello", style.Characters);
                Assert.AreEqual("Inter-Regular", style.FontPostScriptName);
                Assert.AreEqual(18f, style.FontSize);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ConvertToImageUsesImportRootCoordinatesWithNestedAnchors()
        {
            string layerPath = FigmaPathUtility.CombineAssetPath(TestFolder, "layers", "5_6.png");
            WritePngAsset(layerPath, 60, 30);
            WriteTextAsset(
                FigmaPathUtility.CombineAssetPath(TestFolder, "manifest.json"),
                "{"
                + "\"fileKey\":\"test\",\"imageScale\":1,"
                + "\"selectedNodeId\":\"1:2\",\"selectedNodeName\":\"Root\",\"selectedNodeType\":\"FRAME\","
                + "\"rootBounds\":{\"x\":0,\"y\":0,\"width\":200,\"height\":100},"
                + "\"layers\":["
                + "{\"nodeId\":\"5:6\",\"nodeName\":\"Title\",\"type\":\"TEXT\",\"kind\":\"text\","
                + "\"image\":\"layers/5_6.png\","
                + "\"bounds\":{\"x\":20,\"y\":10,\"width\":40,\"height\":20},"
                + "\"text\":{\"characters\":\"Hello\",\"fontFamily\":\"Inter\",\"fontStyleName\":\"Regular\","
                + "\"fontPostScriptName\":\"Inter-Regular\",\"fontWeight\":400,\"fontSize\":18,"
                + "\"color\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1}}}"
                + "]}");

            GameObject rootObject = CreateRectObject("FigmaImport_Root_1_2", null);
            GameObject layersObject = CreateRectObject("Layers", rootObject.transform);
            GameObject parentObject = CreateRectObject("ResponsiveParent", layersObject.transform);
            GameObject layerObject = CreateRectObject("Title", parentObject.transform);

            try
            {
                RectTransform rootRect = rootObject.GetComponent<RectTransform>();
                rootRect.pivot = new Vector2(0.5f, 0.5f);
                rootRect.position = new Vector3(120f, -35f, 0f);
                rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 200f);
                rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 100f);

                RectTransform layersRect = layersObject.GetComponent<RectTransform>();
                layersRect.anchorMin = Vector2.zero;
                layersRect.anchorMax = Vector2.one;
                layersRect.pivot = new Vector2(0.5f, 0.5f);
                layersRect.anchoredPosition = Vector2.zero;
                layersRect.sizeDelta = Vector2.zero;

                RectTransform parentRect = parentObject.GetComponent<RectTransform>();
                parentRect.anchorMin = new Vector2(0.15f, 0.2f);
                parentRect.anchorMax = new Vector2(0.85f, 0.9f);
                parentRect.pivot = new Vector2(0.25f, 0.75f);
                parentRect.anchoredPosition = new Vector2(13f, -17f);
                parentRect.sizeDelta = new Vector2(-12f, 8f);

                RectTransform layerRect = layerObject.GetComponent<RectTransform>();
                layerRect.anchorMin = new Vector2(0.1f, 0.2f);
                layerRect.anchorMax = new Vector2(0.9f, 0.8f);
                layerRect.pivot = new Vector2(0.2f, 0.8f);
                layerRect.anchoredPosition = new Vector2(7f, -11f);
                layerRect.sizeDelta = new Vector2(5f, 6f);

                FigmaLayerBinding binding = layerObject.AddComponent<FigmaLayerBinding>();
                binding.SetLayerInfo("5:6", "Title", "TEXT", layerPath, 0);
                layerObject.AddComponent<TextMeshProUGUI>();

                Vector2 rootCenteredPosition = new Vector2(-60f, 30f);
                Vector3 expectedCenter = layersRect.TransformPoint(new Vector3(rootCenteredPosition.x, rootCenteredPosition.y, 0f));

                string message;
                bool converted = FigmaLayerConversionService.ConvertToImage(binding, out message);

                Assert.IsTrue(converted, message);
                Assert.IsNotNull(layerObject.GetComponent<Image>());
                Assert.IsNull(layerObject.GetComponent<TextMeshProUGUI>());
                Assert.That(GetWorldCenter(layerRect).x, Is.EqualTo(expectedCenter.x).Within(0.05f));
                Assert.That(GetWorldCenter(layerRect).y, Is.EqualTo(expectedCenter.y).Within(0.05f));
                Assert.That(layerRect.rect.width, Is.EqualTo(60f).Within(0.05f));
                Assert.That(layerRect.rect.height, Is.EqualTo(30f).Within(0.05f));
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        private static void WriteTextAsset(string assetPath, string content)
        {
            string fullPath = FigmaPathUtility.ToFullPath(assetPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);
        }

        private static void WritePngAsset(string assetPath, int width, int height)
        {
            string fullPath = FigmaPathUtility.ToFullPath(assetPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static GameObject CreateRectObject(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 100f);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 100f);
            return gameObject;
        }

        private static Vector3 GetWorldCenter(RectTransform rectTransform)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        private static void Cleanup()
        {
            AssetDatabase.DeleteAsset(TestFolder);
            string fullPath = FigmaPathUtility.ToFullPath(TestFolder);
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }

            AssetDatabase.Refresh();
        }
    }
}
