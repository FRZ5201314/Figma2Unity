using NUnit.Framework;

namespace FigmaUiImporter.Editor.Tests
{
    public sealed class FigmaPathUtilityTests
    {
        [TestCase("https://www.figma.com/file/AbC12345/My-File?node-id=1-2", "AbC12345")]
        [TestCase("https://www.figma.com/design/ZxY98765/Screen?m=auto", "ZxY98765")]
        [TestCase("PlainFileKey", "PlainFileKey")]
        [TestCase("PlainFileKey?node-id=1-2", "PlainFileKey")]
        public void ExtractFileKeyAcceptsUrlsAndRawKeys(string input, string expected)
        {
            Assert.AreEqual(expected, FigmaPathUtility.ExtractFileKey(input));
        }

        [Test]
        public void NormalizeAssetFolderKeepsOnlyAssetsPaths()
        {
            Assert.AreEqual("Assets/FigmaImports", FigmaPathUtility.NormalizeAssetFolder(string.Empty));
            Assert.AreEqual("Assets/FigmaImports", FigmaPathUtility.NormalizeAssetFolder("Packages/FigmaImports"));
            Assert.AreEqual("Assets/FigmaImports", FigmaPathUtility.NormalizeAssetFolder("AssetsButNotARealRoot"));
            Assert.AreEqual("Assets/UI/Figma", FigmaPathUtility.NormalizeAssetFolder("Assets/UI/Figma/"));
        }

        [Test]
        public void BuildImportFolderSanitizesFileKeyAndNodeId()
        {
            Assert.AreEqual(
                "Assets/FigmaImports/file_key/1_2",
                FigmaPathUtility.BuildImportFolder("Assets/FigmaImports", "file:key", "1:2"));
        }

        [Test]
        public void BuildPrefabAssetPathSanitizesNameAndNodeId()
        {
            Assert.AreEqual(
                "Assets/FigmaImports/Prefabs/FigmaImport_Login_View__1_2.prefab",
                FigmaPathUtility.BuildPrefabAssetPath("Assets/FigmaImports/Prefabs", "Login/View?", "1:2"));
        }
    }
}
