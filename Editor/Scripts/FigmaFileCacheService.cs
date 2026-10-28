using System;
using System.IO;
using UnityEngine;
using static FigmaUiImporter.Editor.FigmaLocalization;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaFileCacheService
    {
        private const int DefaultMaxAgeHours = 24;

        public bool TryLoad(string fileKey, out string json, out string message)
        {
            json = string.Empty;
            message = string.Empty;

            string path = GetCachePath(fileKey);
            if (!File.Exists(path))
            {
                message = L("未找到本地缓存。", "No local cache found.");
                return false;
            }

            DateTime lastWriteTime = File.GetLastWriteTimeUtc(path);
            TimeSpan age = DateTime.UtcNow - lastWriteTime;
            if (age.TotalHours > DefaultMaxAgeHours)
            {
                message = F("本地缓存已超过 {0} 小时，建议强制重新加载。", "Local cache is older than {0} hours; use Force Reload if you need the latest design.", DefaultMaxAgeHours);
            }
            else
            {
                message = F("已使用本地缓存，缓存时间：{0:g} 前。", "Using local cache from {0:g} ago.", age);
            }

            json = File.ReadAllText(path);
            return !string.IsNullOrEmpty(json);
        }

        public void Save(string fileKey, string json)
        {
            if (string.IsNullOrEmpty(fileKey) || string.IsNullOrEmpty(json))
            {
                return;
            }

            string path = GetCachePath(fileKey);
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json);
        }

        private static string GetCachePath(string fileKey)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(projectRoot, "Library", "FigmaUiImporterCache");
            return Path.Combine(folder, FigmaPathUtility.SafeFileName(fileKey) + ".json");
        }
    }
}
