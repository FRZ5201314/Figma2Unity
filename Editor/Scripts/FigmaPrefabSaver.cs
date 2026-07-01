using System;
using UnityEditor;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaPrefabSaver
    {
        public string SavePrefab(FigmaImportSettings settings, FigmaNodeInfo selectedRoot, GameObject rootObject)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            if (!settings.SavePrefab)
            {
                return string.Empty;
            }

            if (selectedRoot == null)
            {
                throw new ArgumentNullException("selectedRoot");
            }

            if (rootObject == null)
            {
                throw new ArgumentNullException("rootObject");
            }

            string prefabFolder = string.IsNullOrWhiteSpace(settings.PrefabFolder)
                ? "Assets/FigmaImports/Prefabs"
                : FigmaPathUtility.NormalizeAssetFolder(settings.PrefabFolder);
            FigmaPathUtility.EnsureAssetFolder(prefabFolder);

            string prefabPath = FigmaPathUtility.BuildPrefabAssetPath(
                prefabFolder,
                selectedRoot.Name,
                selectedRoot.Id);
            if (!settings.OverwriteExisting)
            {
                prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(rootObject, prefabPath, InteractionMode.UserAction);
            if (prefab == null)
            {
                throw new InvalidOperationException("保存 Prefab 失败：" + prefabPath);
            }

            AssetDatabase.Refresh();
            return prefabPath;
        }
    }
}
