using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal static class FigmaPathUtility
    {
        private const string ExtraInvalidFileNameChars = ":/\\?*\"<>|";

        private static readonly Regex FigmaFileRegex = new Regex(
            @"figma\.com/(?:file|design|proto)/([A-Za-z0-9]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string ExtractFileKey(string fileKeyOrUrl)
        {
            if (string.IsNullOrWhiteSpace(fileKeyOrUrl))
            {
                return string.Empty;
            }

            string trimmed = fileKeyOrUrl.Trim();
            Match match = FigmaFileRegex.Match(trimmed);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            int queryIndex = trimmed.IndexOf("?", StringComparison.Ordinal);
            if (queryIndex >= 0)
            {
                trimmed = trimmed.Substring(0, queryIndex);
            }

            trimmed = trimmed.Trim('/');
            int slashIndex = trimmed.LastIndexOf("/", StringComparison.Ordinal);
            if (slashIndex >= 0)
            {
                trimmed = trimmed.Substring(slashIndex + 1);
            }

            return trimmed;
        }

        public static string NormalizeAssetFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return "Assets/FigmaImports";
            }

            string normalized = folder.Trim().Replace('\\', '/').TrimEnd('/');
            if (!string.Equals(normalized, "Assets", StringComparison.Ordinal)
                && !normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                normalized = "Assets/FigmaImports";
            }

            return normalized;
        }

        public static string BuildImportFolder(string outputFolder, string fileKey, string nodeId)
        {
            return CombineAssetPath(
                NormalizeAssetFolder(outputFolder),
                SafeFileName(fileKey),
                SafeFileName(nodeId));
        }

        public static string BuildPrefabAssetPath(string prefabFolder, string nodeName, string nodeId)
        {
            string safeName = SafeFileName(SafeObjectName(nodeName));
            string safeId = SafeFileName(nodeId);
            string fileName = "FigmaImport_" + safeName + "_" + safeId + ".prefab";
            return CombineAssetPath(NormalizeAssetFolder(prefabFolder), fileName);
        }

        public static string CombineAssetPath(params string[] parts)
        {
            string combined = string.Empty;
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i]))
                {
                    continue;
                }

                string part = parts[i].Replace('\\', '/').Trim('/');
                if (string.IsNullOrEmpty(combined))
                {
                    combined = part;
                }
                else
                {
                    combined += "/" + part;
                }
            }

            return combined;
        }

        public static string ToFullPath(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, normalized);
        }

        public static void EnsureAssetFolder(string assetFolder)
        {
            string fullPath = ToFullPath(assetFolder);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
        }

        public static string SafeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "empty";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0 || ExtraInvalidFileNameChars.IndexOf(chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        public static string SafeObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unnamed";
            }

            string safe = value.Trim();
            safe = safe.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
            return safe.Length > 48 ? safe.Substring(0, 48) : safe;
        }
    }
}
