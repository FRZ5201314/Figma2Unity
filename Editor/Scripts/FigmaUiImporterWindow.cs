using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FigmaUiImporter.Editor.FigmaLocalization;

namespace FigmaUiImporter.Editor
{
    public sealed class FigmaUiImporterWindow : EditorWindow
    {
        private const string PrefToken = "FigmaUiImporter.Token";
        private const string PrefFileInput = "FigmaUiImporter.FileInput";
        private const string PrefOutputFolder = "FigmaUiImporter.OutputFolder";
        private const string PrefSliceMode = "FigmaUiImporter.SliceMode";
        private const string PrefContainerSliceStrategy = "FigmaUiImporter.ContainerSliceStrategy";
        private const string PrefTextImportMode = "FigmaUiImporter.TextImportMode";
        private const string PrefFontMappingPath = "FigmaUiImporter.FontMappingPath";
        private const string PrefImageScale = "FigmaUiImporter.ImageScale";
        private const string PrefCompressionMode = "FigmaUiImporter.CompressionMode";
        private const string PrefReferenceAlpha = "FigmaUiImporter.ReferenceAlpha";
        private const string PrefCreateReference = "FigmaUiImporter.CreateReference";
        private const string PrefDisableRaycast = "FigmaUiImporter.DisableRaycast";
        private const string PrefOverwrite = "FigmaUiImporter.Overwrite";
        private const string PrefSavePrefab = "FigmaUiImporter.SavePrefab";
        private const string PrefPrefabFolder = "FigmaUiImporter.PrefabFolder";
        private const string PrefIgnoreSslCertificateErrors = "FigmaUiImporter.IgnoreSslCertificateErrors";
        private const string PrefPreferCachedFile = "FigmaUiImporter.PreferCachedFile";
        private const int SliceWarningThreshold = 80;
        private const int LargePluginPackageAssetThreshold = 120;
        private const long LargePluginPackageSizeBytes = 80L * 1024L * 1024L;
        private const float DefaultWindowWidth = 1180f;
        private const float DefaultWindowHeight = 640f;
        private const float MinWindowWidth = 1180f;
        private const float MinWindowHeight = 560f;
        private const float MaxWindowSize = 10000f;
        private const float LeftColumnWidth = 360f;
        private const float CenterColumnWidth = 410f;
        private const float FontColumnWidth = LeftColumnWidth;
        private const float ColumnSpacing = 4f;
        private const string PrefWindowSized = "FigmaUiImporter.WindowSized";
        private const float SectionSpacing = 8f;
        private const float PageTreeHeight = 160f;
        private const float ImportedManifestListHeight = 150f;
        private const float LogPanelMinHeight = 84f;
        private const float StatusMessageMaxHeight = 44f;
        private const float ErrorMessageMaxHeight = 56f;
        private const float StatusMessageContentWidth = CenterColumnWidth - 36f;

        private static string[] SliceModeLabels
        {
            get
            {
                return new[]
                {
                    L("ExportSettings 优先，并补齐自动切片", "ExportSettings first, then auto slices"),
                    L("只导入已设置 Export 的节点", "Export-marked nodes only"),
                    L("自动切片可见图层", "Auto-slice visible layers"),
                    L("仅导入整张参考图", "Reference image only")
                };
            }
        }

        private static string[] ContainerSliceStrategyLabels
        {
            get
            {
                return new[]
                {
                    L("自动：保留容器底图并展开组件实例", "Auto: keep background, expand instances"),
                    L("严格 ExportSettings：标记容器优先", "Strict: marked containers first"),
                    L("强制展开容器：尽量拆到子级", "Force expand containers")
                };
            }
        }

        private static string[] TextImportModeLabels
        {
            get
            {
                return new[]
                {
                    L("图片：文字也按 PNG 导入", "Image: import text as PNG"),
                    L("TextMeshPro：尽量转为可编辑文本", "TextMeshPro: editable when possible"),
                    L("智能：普通文本转 TMP，复杂文本用图片", "Smart: TMP for simple text, PNG for complex")
                };
            }
        }

        private static string[] CompressionModeLabels
        {
            get
            {
                return new[]
                {
                    L("不压缩（质量优先）", "Uncompressed (quality)"),
                    L("压缩（体积优先）", "Compressed (size)")
                };
            }
        }

        private string _accessToken;
        private string _fileInput;
        private string _loadedFileKey;
        private string _outputFolder;
        private Canvas _targetCanvas;
        private FigmaSliceMode _sliceMode;
        private FigmaContainerSliceStrategy _containerSliceStrategy;
        private FigmaTextImportMode _textImportMode;
        private FigmaFontMappingAsset _fontMapping;
        private float _imageScale;
        private TextureImporterCompressionMode _compressionMode;
        private bool _createReference;
        private float _referenceAlpha;
        private bool _disableRaycastTarget;
        private bool _overwriteExisting;
        private bool _savePrefab;
        private string _prefabFolder;
        private bool _ignoreSslCertificateErrors;
        private bool _preferCachedFile;

        private FigmaNodeInfo _documentRoot;
        private FigmaNodeInfo _selectedNode;
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();
        private readonly List<string> _logs = new List<string>();
        private readonly List<ImportedManifestEntry> _importedManifests = new List<ImportedManifestEntry>();
        private readonly FigmaFileCacheService _fileCache = new FigmaFileCacheService();
        private int _loadedPageCount;
        private int _loadedFrameCount;
        private Vector2 _treeScroll;
        private Vector2 _logScroll;
        private Vector2 _statusMessageScroll;
        private Vector2 _errorMessageScroll;
        private Vector2 _fontEntriesScroll;
        private Vector2 _importedManifestScroll;
        private bool _isBusy;
        private string _status;
        private string _error;
        private string _importedManifestMessage;
        private float _progress = -1f;
        private CancellationTokenSource _cancellation;
        private SerializedObject _fontMappingSerializedObject;
        private SerializedProperty _fontDefaultFontProperty;
        private SerializedProperty _fontEntriesProperty;
        private GUIStyle _importedManifestButtonStyle;

        [MenuItem("Tools/Figma2Unity")]
        public static void Open()
        {
            FigmaUiImporterWindow window = GetWindow<FigmaUiImporterWindow>();
            window.titleContent = new GUIContent("Figma2Unity");
            window.ConfigureWindowSize();
            window.Show();
        }

        private void OnEnable()
        {
            ConfigureWindowSize();
            _accessToken = EditorPrefs.GetString(PrefToken, string.Empty);
            _fileInput = EditorPrefs.GetString(PrefFileInput, string.Empty);
            _outputFolder = EditorPrefs.GetString(PrefOutputFolder, "Assets/FigmaImports");
            _sliceMode = (FigmaSliceMode)EditorPrefs.GetInt(PrefSliceMode, (int)FigmaSliceMode.ExportSettingsThenAutoSlice);
            _containerSliceStrategy = (FigmaContainerSliceStrategy)EditorPrefs.GetInt(PrefContainerSliceStrategy, (int)FigmaContainerSliceStrategy.Auto);
            _textImportMode = (FigmaTextImportMode)EditorPrefs.GetInt(PrefTextImportMode, (int)FigmaTextImportMode.Smart);
            _fontMapping = AssetDatabase.LoadAssetAtPath<FigmaFontMappingAsset>(EditorPrefs.GetString(PrefFontMappingPath, string.Empty));
            if (_fontMapping == null)
            {
                _fontMapping = AssetDatabase.LoadAssetAtPath<FigmaFontMappingAsset>(FigmaFontMappingService.GlobalMappingAssetPath);
            }
            _imageScale = EditorPrefs.GetFloat(PrefImageScale, 1f);
            _compressionMode = (TextureImporterCompressionMode)EditorPrefs.GetInt(
                PrefCompressionMode,
                (int)TextureImporterCompressionMode.None);
            _referenceAlpha = EditorPrefs.GetFloat(PrefReferenceAlpha, 0.5f);
            _createReference = EditorPrefs.GetBool(PrefCreateReference, true);
            _disableRaycastTarget = EditorPrefs.GetBool(PrefDisableRaycast, true);
            _overwriteExisting = EditorPrefs.GetBool(PrefOverwrite, false);
            _savePrefab = EditorPrefs.GetBool(PrefSavePrefab, false);
            _prefabFolder = EditorPrefs.GetString(PrefPrefabFolder, "Assets/FigmaImports/Prefabs");
            _ignoreSslCertificateErrors = EditorPrefs.GetBool(PrefIgnoreSslCertificateErrors, false);
            _preferCachedFile = EditorPrefs.GetBool(PrefPreferCachedFile, true);
            _status = L("等待加载 Figma 文件。", "Waiting to load a Figma file.");
            BindFontMappingAsset();
            RefreshImportedManifests(false);
        }

        private void ConfigureWindowSize()
        {
            minSize = new Vector2(MinWindowWidth, MinWindowHeight);
            maxSize = new Vector2(MaxWindowSize, MaxWindowSize);

            // Apply the comfortable default once, or when the saved layout is below the usable minimum.
            bool needsDefault = !EditorPrefs.GetBool(PrefWindowSized, false)
                || position.width < MinWindowWidth
                || position.height < MinWindowHeight;
            if (needsDefault)
            {
                Rect currentPosition = position;
                currentPosition.width = DefaultWindowWidth;
                currentPosition.height = DefaultWindowHeight;
                position = currentPosition;
                EditorPrefs.SetBool(PrefWindowSized, true);
            }
        }

        private void OnDisable()
        {
            SavePrefs();
            CancellationTokenSource cancellation = _cancellation;
            _cancellation = null;
            if (cancellation != null)
            {
                try
                {
                    cancellation.Cancel();
                    cancellation.Dispose();
                }
                catch (Exception)
                {
                    // Unity may call OnDisable during domain reload after async state has already been torn down.
                }
            }
        }

        private void OnGUI()
        {
            BindFontMappingAsset();

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginVertical(GUILayout.Width(LeftColumnWidth), GUILayout.ExpandHeight(true));
            DrawNetworkColumn();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(ColumnSpacing);
            EditorGUILayout.BeginVertical(
                GUILayout.MinWidth(CenterColumnWidth),
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            DrawLocalColumn();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(ColumnSpacing);
            EditorGUILayout.BeginVertical(GUILayout.Width(FontColumnWidth), GUILayout.ExpandHeight(true));
            DrawFontMappingPanel();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private static void BeginSection(string title, params GUILayoutOption[] options)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, options);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void EndSection()
        {
            EditorGUILayout.EndVertical();
        }

        private static void AddSectionSpacing()
        {
            EditorGUILayout.Space(SectionSpacing);
        }

        private void BindFontMappingAsset()
        {
            FigmaFontMappingAsset mapping = AssetDatabase.LoadAssetAtPath<FigmaFontMappingAsset>(FigmaFontMappingService.GlobalMappingAssetPath);
            if (mapping == null)
            {
                mapping = FigmaFontMappingService.GetOrCreateGlobalMapping();
            }

            if (_fontMapping != mapping)
            {
                _fontMapping = mapping;
            }

            if (_fontMappingSerializedObject == null || _fontMappingSerializedObject.targetObject != _fontMapping)
            {
                _fontMappingSerializedObject = _fontMapping == null ? null : new SerializedObject(_fontMapping);
                _fontDefaultFontProperty = _fontMappingSerializedObject == null ? null : _fontMappingSerializedObject.FindProperty("DefaultFont");
                _fontEntriesProperty = _fontMappingSerializedObject == null ? null : _fontMappingSerializedObject.FindProperty("Entries");
            }
        }

        private void DrawNetworkColumn()
        {
            DrawRestConnectionSettings();
            AddSectionSpacing();
            DrawNetworkImportActions();
            AddSectionSpacing();
            DrawSelectionTree();
            AddSectionSpacing();
            DrawImportedManifestsPanel();
        }

        private void DrawLocalColumn()
        {
            DrawSharedSettings();
            AddSectionSpacing();
            DrawLocalImportActions();
            AddSectionSpacing();
            DrawStatus();
        }

        private void DrawRestConnectionSettings()
        {
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                BeginSection(L("REST 连接", "REST Connection"));
                _accessToken = EditorGUILayout.PasswordField("Figma Token", _accessToken);
                _fileInput = EditorGUILayout.TextField(L("File Key 或 URL", "File Key or URL"), _fileInput);
                _preferCachedFile = EditorGUILayout.Toggle(L("优先使用本地缓存", "Prefer local cache"), _preferCachedFile);
                _ignoreSslCertificateErrors = EditorGUILayout.Toggle(L("忽略 SSL 证书错误", "Ignore SSL certificate errors"), _ignoreSslCertificateErrors);
                if (_ignoreSslCertificateErrors)
                {
                    EditorGUILayout.HelpBox(L("仅在 Unity 报 Unable to complete SSL connection 时使用。该选项会跳过 HTTPS 证书校验。", "Use only when Unity reports \"Unable to complete SSL connection\". This skips HTTPS certificate validation."), MessageType.Warning);
                }
                EndSection();
            }
        }

        private void DrawSharedSettings()
        {
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                BeginSection(L("通用输出", "Output"));
                EditorGUILayout.BeginHorizontal();
                _outputFolder = EditorGUILayout.TextField(L("输出资源目录", "Output Folder"), _outputFolder);
                if (GUILayout.Button(L("选择", "Select"), GUILayout.Width(56f)))
                {
                    PickOutputFolder();
                }
                EditorGUILayout.EndHorizontal();

                _targetCanvas = (Canvas)EditorGUILayout.ObjectField(L("目标 Canvas", "Target Canvas"), _targetCanvas, typeof(Canvas), true);
                EndSection();

                AddSectionSpacing();
                BeginSection(L("通用导入设置", "Import Settings"));
                _sliceMode = (FigmaSliceMode)EditorGUILayout.Popup(L("切图模式", "Slice Mode"), (int)_sliceMode, SliceModeLabels);
                using (new EditorGUI.DisabledScope(_sliceMode != FigmaSliceMode.ExportSettingsThenAutoSlice))
                {
                    _containerSliceStrategy = (FigmaContainerSliceStrategy)EditorGUILayout.Popup(L("容器处理策略", "Container Strategy"), (int)_containerSliceStrategy, ContainerSliceStrategyLabels);
                }

                _textImportMode = (FigmaTextImportMode)EditorGUILayout.Popup(L("文本导入模式", "Text Mode"), (int)_textImportMode, TextImportModeLabels);
                _fontMapping = AssetDatabase.LoadAssetAtPath<FigmaFontMappingAsset>(FigmaFontMappingService.GlobalMappingAssetPath);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(L("全局字体映射", "Global Font Mapping"), _fontMapping, typeof(FigmaFontMappingAsset), false);
                }
                _imageScale = Mathf.Clamp(EditorGUILayout.FloatField(L("图片倍率", "Image Scale"), _imageScale), 0.01f, 4f);
                _compressionMode = (TextureImporterCompressionMode)EditorGUILayout.Popup(
                    L("纹理压缩", "Texture Compression"),
                    (int)_compressionMode,
                    CompressionModeLabels);
                _createReference = EditorGUILayout.Toggle(L("创建参考图", "Create Reference Image"), _createReference);
                using (new EditorGUI.DisabledScope(!_createReference))
                {
                    _referenceAlpha = EditorGUILayout.Slider(L("参考图透明度", "Reference Alpha"), _referenceAlpha, 0f, 1f);
                }

                _disableRaycastTarget = EditorGUILayout.Toggle(L("关闭图片射线检测", "Disable Raycast Target"), _disableRaycastTarget);
                EndSection();

                AddSectionSpacing();
                BeginSection(L("输出选项", "Output Options"));
                _overwriteExisting = EditorGUILayout.Toggle(L("覆盖同名场景根节点 / Prefab", "Overwrite scene root / Prefab"), _overwriteExisting);
                _savePrefab = EditorGUILayout.Toggle(L("保存为 Prefab", "Save as Prefab"), _savePrefab);
                using (new EditorGUI.DisabledScope(!_savePrefab))
                {
                    EditorGUILayout.BeginHorizontal();
                    _prefabFolder = EditorGUILayout.TextField(L("Prefab 输出目录", "Prefab Output Folder"), _prefabFolder);
                    if (GUILayout.Button(L("选择", "Select"), GUILayout.Width(56f)))
                    {
                        PickPrefabFolder();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EndSection();
            }
        }

        private void DrawNetworkImportActions()
        {
            BeginSection(L("网络导入（Figma REST API）", "Network Import (Figma REST API)"));
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                if (GUILayout.Button(L("加载 Figma 文件", "Load Figma File"), GUILayout.Height(30f)))
                {
                    _ = LoadFileAsync(false);
                }

                if (GUILayout.Button(L("强制重新加载", "Force Reload"), GUILayout.Width(110f), GUILayout.Height(30f)))
                {
                    _ = LoadFileAsync(true);
                }
            }

            using (new EditorGUI.DisabledScope(!_isBusy))
            {
                if (GUILayout.Button(L("取消", "Cancel"), GUILayout.Width(72f), GUILayout.Height(30f)))
                {
                    if (_cancellation != null)
                    {
                        _cancellation.Cancel();
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
            EndSection();
        }

        private void DrawLocalImportActions()
        {
            BeginSection(L("本地导入（Figma 插件包）", "Local Import (Figma Plugin Package)"));
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                if (GUILayout.Button(L("导入 .figma2unity.json", "Import .figma2unity.json"), GUILayout.Height(30f)))
                {
                    ImportFromPluginPackage();
                }
            }
            EndSection();
        }

        private void DrawImportedManifestsPanel()
        {
            BeginSection(L("本地重建（已导入项目）", "Local Rebuild (Imported Projects)"), GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(L("点击项目从 manifest 重建场景", "Click a project to rebuild from its manifest"), EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                if (GUILayout.Button(L("刷新", "Refresh"), GUILayout.Width(52f)))
                {
                    RefreshImportedManifests(true);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_importedManifestMessage))
            {
                EditorGUILayout.HelpBox(_importedManifestMessage, MessageType.Warning);
            }

            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.MinHeight(ImportedManifestListHeight),
                GUILayout.ExpandHeight(true));
            if (_importedManifests.Count == 0)
            {
                EditorGUILayout.LabelField(L("暂无已导入项目。导入后会在这里显示。", "No imported projects yet. Imports will appear here."), EditorStyles.miniLabel);
            }
            else
            {
                _importedManifestScroll = EditorGUILayout.BeginScrollView(_importedManifestScroll, GUILayout.ExpandHeight(true));
                GUIStyle buttonStyle = GetImportedManifestButtonStyle();
                using (new EditorGUI.DisabledScope(_isBusy))
                {
                    for (int i = 0; i < _importedManifests.Count; i++)
                    {
                        ImportedManifestEntry entry = _importedManifests[i];
                        GUIContent content = new GUIContent(entry.ButtonText, entry.ManifestPath);
                        if (GUILayout.Button(content, buttonStyle, GUILayout.Height(42f)))
                        {
                            RebuildFromManifest(entry.ManifestPath);
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
            EndSection();
        }

        private GUIStyle GetImportedManifestButtonStyle()
        {
            if (_importedManifestButtonStyle == null)
            {
                _importedManifestButtonStyle = new GUIStyle(GUI.skin.button);
                _importedManifestButtonStyle.alignment = TextAnchor.MiddleLeft;
                _importedManifestButtonStyle.wordWrap = true;
                _importedManifestButtonStyle.fontSize = 11;
            }

            return _importedManifestButtonStyle;
        }

        private void RefreshImportedManifests(bool addLog)
        {
            _importedManifests.Clear();
            _importedManifestMessage = string.Empty;

            try
            {
                List<string> manifestPaths = FindImportedManifestPaths();
                int skipped = 0;
                for (int i = 0; i < manifestPaths.Count; i++)
                {
                    try
                    {
                        _importedManifests.Add(BuildImportedManifestEntry(manifestPaths[i]));
                    }
                    catch (Exception)
                    {
                        skipped++;
                    }
                }

                _importedManifests.Sort(CompareImportedManifestEntries);
                if (skipped > 0)
                {
                    _importedManifestMessage = F("已跳过 {0} 个无法读取的 manifest。", "{0} unreadable manifest(s) skipped.", skipped);
                }

                if (addLog)
                {
                    AddLog(F("已刷新导入项目列表：找到 {0} 个 manifest。", "Imported project list refreshed: {0} manifest(s) found.", _importedManifests.Count));
                }
            }
            catch (Exception exception)
            {
                _importedManifestMessage = F("扫描已导入项目失败：{0}", "Failed to scan imported projects: {0}", exception.Message);
                if (addLog)
                {
                    AddLog(_importedManifestMessage);
                }
            }

            Repaint();
        }

        private static int CompareImportedManifestEntries(ImportedManifestEntry left, ImportedManifestEntry right)
        {
            return right.SortTimeUtc.CompareTo(left.SortTimeUtc);
        }

        private List<string> FindImportedManifestPaths()
        {
            List<string> manifestPaths = new List<string>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> rootFolders = new List<string>();
            AddImportedManifestRoot(rootFolders, GetAssetDirectory(FigmaFontMappingService.GlobalMappingAssetPath));
            AddImportedManifestRoot(rootFolders, FigmaPathUtility.NormalizeAssetFolder(_outputFolder));

            for (int i = 0; i < rootFolders.Count; i++)
            {
                string rootFolder = rootFolders[i];
                string fullRoot = FigmaPathUtility.ToFullPath(rootFolder);
                if (!Directory.Exists(fullRoot))
                {
                    continue;
                }

                string[] files = Directory.GetFiles(fullRoot, "manifest.json", SearchOption.AllDirectories);
                for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
                {
                    string assetPath = ToAssetPath(files[fileIndex]);
                    if (seenPaths.Add(assetPath))
                    {
                        manifestPaths.Add(assetPath);
                    }
                }
            }

            return manifestPaths;
        }

        private static void AddImportedManifestRoot(List<string> rootFolders, string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                return;
            }

            string normalized = FigmaPathUtility.NormalizeAssetFolder(rootFolder);
            for (int i = 0; i < rootFolders.Count; i++)
            {
                if (string.Equals(rootFolders[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            rootFolders.Add(normalized);
        }

        private static ImportedManifestEntry BuildImportedManifestEntry(string manifestPath)
        {
            string fullPath = FigmaPathUtility.ToFullPath(manifestPath);
            ImportedManifestSummaryDto manifest = JsonUtility.FromJson<ImportedManifestSummaryDto>(File.ReadAllText(fullPath));
            if (manifest == null || string.IsNullOrEmpty(manifest.selectedNodeId))
            {
                throw new InvalidOperationException(F("manifest 内容无效：{0}", "Invalid manifest: {0}", manifestPath));
            }

            DateTime sortTime = File.GetLastWriteTimeUtc(fullPath);
            DateTime importedAt;
            if (!string.IsNullOrEmpty(manifest.importedAt)
                && DateTime.TryParse(
                    manifest.importedAt,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out importedAt))
            {
                sortTime = importedAt.ToUniversalTime();
            }

            string nodeName = string.IsNullOrWhiteSpace(manifest.selectedNodeName)
                ? L("(未命名项目)", "(Unnamed project)")
                : manifest.selectedNodeName.Trim();
            nodeName = ShortenText(nodeName, 26);
            string nodeType = string.IsNullOrWhiteSpace(manifest.selectedNodeType)
                ? "FRAME"
                : manifest.selectedNodeType.Trim();
            int layerCount = manifest.layers == null ? 0 : manifest.layers.Count;
            string importedTime = sortTime.ToLocalTime().ToString("MM-dd HH:mm");
            string detail = F("{0} | {1} 图层 | {2}", "{0} | {1} layers | {2}", importedTime, layerCount, ShortenAssetPath(manifestPath, 42));

            ImportedManifestEntry entry = new ImportedManifestEntry();
            entry.ManifestPath = manifestPath;
            entry.SortTimeUtc = sortTime;
            entry.ButtonText = nodeName + " [" + nodeType + "]\n" + detail;
            return entry;
        }

        private static string ToAssetPath(string fullPath)
        {
            string normalizedFullPath = Path.GetFullPath(fullPath).Replace('\\', '/');
            string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            if (normalizedFullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFullPath.Substring(projectRoot.Length + 1);
            }

            return normalizedFullPath;
        }

        private static string ShortenAssetPath(string assetPath, int maxLength)
        {
            if (string.IsNullOrEmpty(assetPath) || assetPath.Length <= maxLength)
            {
                return assetPath;
            }

            return "..." + assetPath.Substring(assetPath.Length - maxLength + 3);
        }

        private static string ShortenText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }

        private void DrawFontMappingPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(L("字体映射", "Font Mapping"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUIContent languageButton = new GUIContent(
                FigmaLocalization.IsEnglish ? "中文" : "English",
                L("切换界面语言", "Switch interface language"));
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                if (GUILayout.Button(languageButton, EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    FigmaLocalization.ToggleLanguage();
                    Repaint();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (_fontMapping == null || _fontMappingSerializedObject == null)
            {
                EditorGUILayout.HelpBox(L("未找到全局字体映射。", "Global font mapping not found."), MessageType.Warning);
                if (GUILayout.Button(L("创建字体映射", "Create Font Mapping"), GUILayout.Height(28f)))
                {
                    OpenGlobalFontMapping();
                    BindFontMappingAsset();
                }

                EndSection();
                return;
            }

            _fontMappingSerializedObject.Update();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(L("映射资产", "Mapping Asset"), _fontMapping, typeof(FigmaFontMappingAsset), false);
            }

            if (GUILayout.Button(L("定位", "Locate"), GUILayout.Width(52f)))
            {
                Selection.activeObject = _fontMapping;
                EditorGUIUtility.PingObject(_fontMapping);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(_fontDefaultFontProperty, new GUIContent(L("默认 TMP 字体", "Default TMP Font")));

            using (new EditorGUI.DisabledScope(_isBusy))
            {
                if (GUILayout.Button(L("更新已导入页面", "Update Imported Pages"), GUILayout.Height(28f)))
                {
                    UpdateFontMappingFromImportedManifests();
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(L("应用到选中项", "Apply to Selection"), GUILayout.Height(28f)))
                {
                    ApplyFontMappingToSelection();
                }

                if (GUILayout.Button(L("应用到打开场景", "Apply to Open Scenes"), GUILayout.Height(28f)))
                {
                    ApplyFontMappingToOpenScenes();
                }
                EditorGUILayout.EndHorizontal();
            }

            AddSectionSpacing();
            DrawFontMappingEntries();

            _fontMappingSerializedObject.ApplyModifiedProperties();
            EndSection();
        }

        private void DrawFontMappingEntries()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            _fontEntriesScroll = EditorGUILayout.BeginScrollView(_fontEntriesScroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.PropertyField(
                _fontEntriesProperty,
                new GUIContent(F("映射条目 ({0})", "Mapping Entries ({0})", _fontEntriesProperty.arraySize)),
                true);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectionTree()
        {
            BeginSection(L("页面 / Frame", "Page / Frame"));

            if (_documentRoot != null)
            {
                EditorGUILayout.LabelField(
                    string.Format(L("已加载：{0} 个 Page，{1} 个 Frame / Component / Instance", "Loaded: {0} pages, {1} frames / components / instances"), _loadedPageCount, _loadedFrameCount),
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(PageTreeHeight));
            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll, GUILayout.ExpandHeight(true));

            if (_documentRoot == null)
            {
                EditorGUILayout.HelpBox(L("加载 Figma 文件后，这里会显示 Page 和 Frame。", "Load a Figma file to see pages and frames."), MessageType.Info);
            }
            else
            {
                List<FigmaNodeInfo> pages = FigmaDocumentParser.GetPages(_documentRoot);
                List<FigmaNodeInfo> roots = pages.Count > 0 ? pages : GetFallbackTreeRoots(_documentRoot);

                if (pages.Count == 0 && roots.Count > 0)
                {
                    EditorGUILayout.HelpBox(L("没有读取到标准 CANVAS 类型 Page，已改为显示 document 的一级节点。", "No standard CANVAS page found; showing top-level document nodes instead."), MessageType.Warning);
                }

                if (roots.Count == 0)
                {
                    EditorGUILayout.HelpBox(L("Figma 返回的 document 没有可显示的子节点。请确认 token 有权限访问该文件，且文件不是空文件。", "The Figma document has no visible child nodes. Check token access and that the file is not empty."), MessageType.Warning);
                }

                for (int i = 0; i < roots.Count; i++)
                {
                    DrawNodeTreeRoot(roots[i], 0);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            AddSectionSpacing();
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                using (new EditorGUI.DisabledScope(_selectedNode == null))
                {
                    if (GUILayout.Button(L("导入选中项", "Import Selected"), GUILayout.Height(28f)))
                    {
                        _ = ImportSelectedAsync();
                    }
                }
            }
            EndSection();
        }

        private void DrawNodeTreeRoot(FigmaNodeInfo node, int indent)
        {
            bool selectable = node.IsPage || node.IsFrameLike || node.HasUsableBounds;
            DrawNodeRow(node, indent, selectable);

            if (!GetFoldout(node))
            {
                return;
            }

            if (node.IsPage)
            {
                DrawFrameDescendants(node, indent + 1);
                return;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                DrawNodeTreeRoot(node.Children[i], indent + 1);
            }
        }

        private void DrawFrameDescendants(FigmaNodeInfo node, int indent)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                FigmaNodeInfo child = node.Children[i];
                bool drawSelf = child.IsFrameLike;
                if (drawSelf)
                {
                    DrawNodeRow(child, indent, true);
                }

                if (!drawSelf || GetFoldout(child))
                {
                    DrawFrameDescendants(child, drawSelf ? indent + 1 : indent);
                }
            }
        }

        private void DrawNodeRow(FigmaNodeInfo node, int indent, bool selectable)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * 18f);

            bool hasFrames = HasFrameDescendant(node);
            if (hasFrames)
            {
                Rect foldoutRect = GUILayoutUtility.GetRect(14f, EditorGUIUtility.singleLineHeight, GUILayout.Width(14f));
                bool expanded = EditorGUI.Foldout(foldoutRect, GetFoldout(node), GUIContent.none);
                SetFoldout(node, expanded);
            }
            else
            {
                GUILayout.Space(14f);
            }

            using (new EditorGUI.DisabledScope(!selectable || _isBusy))
            {
                bool selected = _selectedNode == node;
                bool toggled = GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(18f));
                if (toggled && !selected)
                {
                    _selectedNode = node;
                    _status = F("已选择：{0}", "Selected: {0}", node.DisplayName);
                    Repaint();
                }
            }

            GUIStyle labelStyle = _selectedNode == node ? EditorStyles.boldLabel : EditorStyles.label;
            EditorGUILayout.LabelField(node.DisplayName, labelStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatus()
        {
            BeginSection(L("状态", "Status"), GUILayout.ExpandHeight(true));
            if (_progress >= 0f)
            {
                Rect rect = GUILayoutUtility.GetRect(1f, 20f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(rect, _progress, _status);
            }
            else if (!string.IsNullOrEmpty(_error))
            {
                EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
            }
            else
            {
                DrawBoundedHelpBox(ref _statusMessageScroll, _status, MessageType.None, StatusMessageMaxHeight);
            }

            if (!string.IsNullOrEmpty(_error))
            {
                DrawBoundedHelpBox(ref _errorMessageScroll, _error, MessageType.Error, ErrorMessageMaxHeight);
            }

            EditorGUILayout.LabelField(L("日志", "Log"), EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.MinHeight(LogPanelMinHeight),
                GUILayout.ExpandHeight(true));
            if (_logs.Count == 0)
            {
                EditorGUILayout.LabelField(L("暂无日志。执行加载、导入或离线重建后会显示记录。", "No logs yet. Load, import, or rebuild to see activity."), EditorStyles.miniLabel);
            }
            else
            {
                _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
                for (int i = 0; i < _logs.Count; i++)
                {
                    EditorGUILayout.LabelField(_logs[i], EditorStyles.wordWrappedLabel);
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
            EndSection();
        }

        private static void DrawBoundedHelpBox(
            ref Vector2 scroll,
            string message,
            MessageType messageType,
            float maxHeight)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            GUIContent content = new GUIContent(message);
            float contentHeight = EditorStyles.helpBox.CalcHeight(content, StatusMessageContentWidth) + 6f;
            if (contentHeight <= maxHeight)
            {
                EditorGUILayout.HelpBox(message, messageType);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(maxHeight));
            EditorGUILayout.HelpBox(message, messageType);
            EditorGUILayout.EndScrollView();
        }

        private async Task LoadFileAsync(bool forceRemote)
        {
            string fileKey = FigmaPathUtility.ExtractFileKey(_fileInput);
            if (!ValidateConnectionInput(fileKey))
            {
                return;
            }

            BeginBusy(L("正在加载 Figma 文件...", "Loading Figma file..."));
            SavePrefs();

            try
            {
                string json;
                string cacheMessage = string.Empty;
                if (!forceRemote && _preferCachedFile && _fileCache.TryLoad(fileKey, out json, out cacheMessage))
                {
                    try
                    {
                        AddLog(cacheMessage);
                        ApplyLoadedDocument(fileKey, FigmaDocumentParser.ParseFile(json), L("从本地缓存加载完成。", "Loaded from local cache."));
                        return;
                    }
                    catch (Exception cacheException)
                    {
                        AddLog(F("本地缓存解析失败，将改为远程加载：{0}", "Failed to parse the local cache; loading remotely: {0}", cacheException.Message));
                    }
                }

                if (!forceRemote && _preferCachedFile && !string.IsNullOrEmpty(cacheMessage))
                {
                    AddLog(cacheMessage);
                }

                FigmaApiClient client = new FigmaApiClient(_accessToken, _ignoreSslCertificateErrors, OnClientStatus);
                json = await client.LoadFileJsonAsync(fileKey, _cancellation.Token);
                _fileCache.Save(fileKey, json);
                ApplyLoadedDocument(fileKey, FigmaDocumentParser.ParseFile(json), forceRemote ? L("强制重新加载完成。", "Force reload complete.") : L("远程加载完成并已写入本地缓存。", "Remote load complete and cached locally."));
            }
            catch (OperationCanceledException)
            {
                _status = L("已取消加载。", "Load canceled.");
                AddLog(L("加载已取消。", "Load canceled."));
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = L("加载失败。", "Load failed.");
                AddLog(F("加载失败：{0}", "Load failed: {0}", exception.Message));
            }
            finally
            {
                EndBusy();
            }
        }

        private void ApplyLoadedDocument(string fileKey, FigmaNodeInfo documentRoot, string sourceMessage)
        {
            _documentRoot = documentRoot;
            _loadedFileKey = fileKey;
            _selectedNode = null;
            _foldouts.Clear();

            List<FigmaNodeInfo> pages = FigmaDocumentParser.GetPages(_documentRoot);
            List<FigmaNodeInfo> frames = new List<FigmaNodeInfo>();
            FigmaDocumentParser.CollectSelectableFrames(_documentRoot, frames);
            _loadedPageCount = pages.Count;
            _loadedFrameCount = frames.Count;

            for (int i = 0; i < pages.Count; i++)
            {
                SetFoldout(pages[i], true);
            }

            if (pages.Count == 0)
            {
                List<FigmaNodeInfo> fallbackRoots = GetFallbackTreeRoots(_documentRoot);
                for (int i = 0; i < fallbackRoots.Count; i++)
                {
                    SetFoldout(fallbackRoots[i], true);
                }
            }

            _status = F("{0} 读取到 {1} 个 Page，{2} 个 Frame。请选择 Page 或 Frame。", "{0} Loaded {1} pages and {2} frames. Select a Page or Frame.", sourceMessage, _loadedPageCount, _loadedFrameCount);
            AddLog(F("{0} {1} 个 Page，{2} 个 Frame，document 一级节点 {3} 个。", "{0} {1} pages, {2} frames, {3} top-level document nodes.", sourceMessage, _loadedPageCount, _loadedFrameCount, _documentRoot.Children.Count));
        }

        private async Task ImportSelectedAsync()
        {
            if (_selectedNode == null)
            {
                _error = L("请先选择一个 Page 或 Frame。", "Select a Page or Frame first.");
                return;
            }

            string fileKey = FigmaPathUtility.ExtractFileKey(_fileInput);
            if (!ValidateConnectionInput(fileKey))
            {
                return;
            }

            if (string.IsNullOrEmpty(_loadedFileKey) || !string.Equals(_loadedFileKey, fileKey, StringComparison.Ordinal))
            {
                _error = L("当前选择来自上一次加载的文件。请先重新加载 Figma 文件，再导入。", "The current selection belongs to a previously loaded file. Reload the Figma file before importing.");
                return;
            }

            if (_selectedNode.IsPage)
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    L("确认导入 Page", "Import Page?"),
                    L("Page 可能包含多个界面，导入范围可能很大。建议优先选择具体 Frame。\n\n确定继续导入这个 Page 吗？", "A Page may contain multiple screens and produce a very large import. Prefer selecting a specific Frame.\n\nContinue importing this Page?"),
                    L("继续导入", "Continue"),
                    L("取消", "Cancel"));
                if (!confirmed)
                {
                    return;
                }
            }

            if (!ConfirmSliceEstimate())
            {
                return;
            }

            BeginBusy(L("准备导入...", "Preparing import..."));
            SavePrefs();

            try
            {
                FigmaImportSettings settings = BuildSettings(fileKey);
                FigmaApiClient client = new FigmaApiClient(_accessToken, _ignoreSslCertificateErrors, OnClientStatus);
                FigmaImportOrchestrator importer = new FigmaImportOrchestrator(client);
                Progress<FigmaImportProgress> progress = new Progress<FigmaImportProgress>(OnProgress);
                FigmaImportResult result = await importer.ImportAsync(settings, _selectedNode, progress, _cancellation.Token);

                _status = F("导入完成：{0} 个切图图层。", "Import complete: {0} layers.", result.LayerCount);
                _progress = 1f;
                AddLog(F("输出目录：{0}", "Output folder: {0}", result.OutputFolder));
                AddLog(F("Manifest：{0}", "Manifest: {0}", result.ManifestPath));
                AddFontMappingLog(result);
                if (!string.IsNullOrEmpty(result.DiagnosticReportPath))
                {
                    AddLog(F("诊断报告：{0}", "Diagnostics: {0}", result.DiagnosticReportPath));
                }

                if (!string.IsNullOrEmpty(result.PrefabPath))
                {
                    AddLog(F("Prefab：{0}", "Prefab: {0}", result.PrefabPath));
                }

                if (result.RootObject != null)
                {
                    AddLog(F("场景根节点：{0}", "Scene root: {0}", result.RootObject.name));
                }

                RefreshImportedManifests(false);
            }
            catch (OperationCanceledException)
            {
                _status = L("已取消导入。", "Import canceled.");
                AddLog(L("导入已取消。", "Import canceled."));
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = L("导入失败。", "Import failed.");
                AddLog(F("导入失败：{0}", "Import failed: {0}", exception.Message));
            }
            finally
            {
                EndBusy();
            }
        }

        private void RebuildFromManifest(string manifestPath)
        {
            if (string.IsNullOrEmpty(manifestPath))
            {
                return;
            }

            BeginBusy(L("准备从 manifest 重建场景...", "Preparing to rebuild from manifest..."));
            SavePrefs();

            try
            {
                FigmaImportSettings settings = BuildSettings(FigmaPathUtility.ExtractFileKey(_fileInput));
                FigmaOfflineSceneRebuilder rebuilder = new FigmaOfflineSceneRebuilder();
                FigmaImportResult result = rebuilder.Rebuild(settings, manifestPath, new ImmediateProgress(OnProgress));

                _status = F("离线重建完成：{0} 个切图图层。", "Offline rebuild complete: {0} layers.", result.LayerCount);
                _progress = 1f;
                AddLog(F("离线重建输出目录：{0}", "Offline rebuild output folder: {0}", result.OutputFolder));
                AddLog(F("Manifest：{0}", "Manifest: {0}", result.ManifestPath));
                AddFontMappingLog(result);
                if (!string.IsNullOrEmpty(result.DiagnosticReportPath))
                {
                    AddLog(F("诊断报告：{0}", "Diagnostics: {0}", result.DiagnosticReportPath));
                }

                if (!string.IsNullOrEmpty(result.PrefabPath))
                {
                    AddLog(F("Prefab：{0}", "Prefab: {0}", result.PrefabPath));
                }

                if (result.RootObject != null)
                {
                    AddLog(F("场景根节点：{0}", "Scene root: {0}", result.RootObject.name));
                }

                RefreshImportedManifests(false);
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = L("离线重建失败。", "Offline rebuild failed.");
                AddLog(F("离线重建失败：{0}", "Offline rebuild failed: {0}", exception.Message));
            }
            finally
            {
                EndBusy();
            }
        }

        private void ImportFromPluginPackage()
        {
            string pickedPackage = EditorUtility.OpenFilePanel(L("选择 Figma 插件导出包", "Select Figma Plugin Package"), string.Empty, "json");
            if (string.IsNullOrEmpty(pickedPackage))
            {
                return;
            }

            FigmaPluginPackageImporter packageImporter = new FigmaPluginPackageImporter();
            FigmaPluginPackageInfo packageInfo;
            try
            {
                packageInfo = packageImporter.ReadPackageInfo(pickedPackage);
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = L("Figma 插件包无效。", "Invalid Figma plugin package.");
                AddLog(F("Figma 插件包无效：{0}", "Invalid Figma plugin package: {0}", exception.Message));
                Repaint();
                return;
            }

            FigmaImportSettings previewSettings = BuildOfflineSettings();
            if (!ConfirmPluginPackageImport(packageInfo, previewSettings))
            {
                return;
            }

            BeginBusy(L("准备导入 Figma 插件包...", "Preparing to import Figma plugin package..."));
            SavePrefs();

            try
            {
                FigmaImportSettings settings = previewSettings;
                ImmediateProgress progress = new ImmediateProgress(OnProgress);
                string manifestPath = packageImporter.ImportPackage(pickedPackage, settings, progress);

                FigmaOfflineSceneRebuilder rebuilder = new FigmaOfflineSceneRebuilder();
                FigmaImportResult result = rebuilder.Rebuild(settings, manifestPath, progress);

                if (!string.IsNullOrEmpty(settings.FileKey))
                {
                    _fileInput = settings.FileKey;
                }

                _status = F("Figma 插件包导入完成：{0} 个切图图层。", "Figma plugin package import complete: {0} layers.", result.LayerCount);
                _progress = 1f;
                AddLog(F("插件包：{0}", "Package: {0}", pickedPackage));
                AddLog(F("插件包节点：{0}", "Package node: {0}", FormatPluginPackageNode(packageInfo)));
                AddLog(F("插件包资源：{0} 个图片资源，{1} 个切图图层，大小 {2}", "Package assets: {0} images, {1} layers, size {2}", packageInfo.AssetCount, packageInfo.LayerCount, FormatFileSize(packageInfo.PackageSizeBytes)));
                AddLog(F("导入输出目录：{0}", "Import output folder: {0}", result.OutputFolder));
                AddLog(F("Manifest：{0}", "Manifest: {0}", result.ManifestPath));
                AddFontMappingLog(result);
                if (!string.IsNullOrEmpty(result.DiagnosticReportPath))
                {
                    AddLog(F("诊断报告：{0}", "Diagnostics: {0}", result.DiagnosticReportPath));
                }

                if (!string.IsNullOrEmpty(result.PrefabPath))
                {
                    AddLog(F("Prefab：{0}", "Prefab: {0}", result.PrefabPath));
                }

                if (result.RootObject != null)
                {
                    AddLog(F("场景根节点：{0}", "Scene root: {0}", result.RootObject.name));
                }

                RefreshImportedManifests(false);
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = L("Figma 插件包导入失败。", "Figma plugin package import failed.");
                AddLog(F("Figma 插件包导入失败：{0}", "Figma plugin package import failed: {0}", exception.Message));
            }
            finally
            {
                EndBusy();
            }
        }

        private void OpenGlobalFontMapping()
        {
            try
            {
                _fontMapping = FigmaFontMappingService.GetOrCreateGlobalMapping();
                SavePrefs();
                Selection.activeObject = _fontMapping;
                EditorGUIUtility.PingObject(_fontMapping);
                _status = L("已打开全局字体映射。", "Opened global font mapping.");
                AddLog(F("全局字体映射：{0}", "Global font mapping: {0}", AssetDatabase.GetAssetPath(_fontMapping)));
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = L("打开全局字体映射失败。", "Failed to open global font mapping.");
                AddLog(F("打开全局字体映射失败：{0}", "Failed to open global font mapping: {0}", exception.Message));
            }

            Repaint();
        }

        private void UpdateFontMappingFromImportedManifests()
        {
            try
            {
                if (_fontMappingSerializedObject != null)
                {
                    _fontMappingSerializedObject.ApplyModifiedProperties();
                }

                Undo.RecordObject(_fontMapping, L("更新 Figma 字体映射", "Update Figma font mapping"));
                FontMappingUpdateResult result = FigmaFontMappingService.UpdateFromImportedManifests(
                    _fontMapping,
                    new[] { FigmaPathUtility.NormalizeAssetFolder(_outputFolder) });
                if (_fontMappingSerializedObject != null)
                {
                    _fontMappingSerializedObject.Update();
                }

                _status = L("字体映射更新完成。", "Font mapping updated.");
                AddLog(F("字体映射更新：扫描 manifest {0} 个，文本样式 {1} 个，新增 {2} 个，更新 {3} 个，总计 {4} 个。", "Font mapping update: {0} manifests, {1} text styles, {2} added, {3} updated, {4} total.", result.SourceFileCount, result.TextStyleCount, result.AddedEntries, result.UpdatedEntries, result.TotalEntries));
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = L("字体映射更新失败。", "Font mapping update failed.");
                AddLog(F("字体映射更新失败：{0}", "Font mapping update failed: {0}", exception.Message));
            }

            Repaint();
        }

        private void ApplyFontMappingToSelection()
        {
            try
            {
                if (_fontMappingSerializedObject != null)
                {
                    _fontMappingSerializedObject.ApplyModifiedProperties();
                }

                int count = FigmaFontMappingService.ApplyMappingToSelection(_fontMapping);
                _status = F("已更新选中项文字字体：{0} 个。", "Updated fonts on {0} selected text objects.", count);
                AddLog(F("已应用字体映射到选中项：{0} 个 TMP 文字。", "Applied font mapping to {0} TMP text objects in the selection.", count));
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = L("应用字体映射失败。", "Failed to apply font mapping.");
                AddLog(F("应用字体映射失败：{0}", "Failed to apply font mapping: {0}", exception.Message));
            }

            Repaint();
        }

        private void ApplyFontMappingToOpenScenes()
        {
            try
            {
                if (_fontMappingSerializedObject != null)
                {
                    _fontMappingSerializedObject.ApplyModifiedProperties();
                }

                int count = FigmaFontMappingService.ApplyMappingToOpenScenes(_fontMapping);
                _status = F("已更新打开场景文字字体：{0} 个。", "Updated fonts on {0} text objects in open scenes.", count);
                AddLog(F("已应用字体映射到打开场景：{0} 个 TMP 文字。", "Applied font mapping to {0} TMP text objects in open scenes.", count));
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = L("应用字体映射失败。", "Failed to apply font mapping.");
                AddLog(F("应用字体映射失败：{0}", "Failed to apply font mapping: {0}", exception.Message));
            }

            Repaint();
        }

        private void AddFontMappingLog(FigmaImportResult result)
        {
            if (result == null)
            {
                return;
            }

            AddLog(F("字体映射：{0}，文本样式 {1} 个，新增 {2} 个，更新 {3} 个。", "Font mapping: {0}, {1} text styles, {2} added, {3} updated.", result.FontMappingPath, result.FontMappingTextStyleCount, result.FontMappingAddedEntries, result.FontMappingUpdatedEntries));
            NotifyNewUnmappedFontEntries(result);
        }

        private void NotifyNewUnmappedFontEntries(FigmaImportResult result)
        {
            if (result.FontMappingAddedEntries <= 0)
            {
                return;
            }

            int unmappedCount = CountUnmappedFontEntries();
            if (unmappedCount <= 0)
            {
                return;
            }

            string message = F("本次新增 {0} 个字体映射条目，当前共有 {1} 个条目未配置 TMP 字体。\n\n请在右侧“字体映射”的映射条目中为“未配置 TMP 字体”的项目指定 FontAsset。", "{0} new font mapping entries were added; {1} entries still have no TMP font.\n\nAssign a FontAsset to the \"Unmapped\" entries in the Font Mapping panel.", result.FontMappingAddedEntries, unmappedCount);
            AddLog(F("需要配置字体映射：新增 {0} 个，当前未配置 {1} 个。", "Font mapping action needed: {0} new entries, {1} still unmapped.", result.FontMappingAddedEntries, unmappedCount));

            if (EditorUtility.DisplayDialog(L("发现新的 Figma 字体", "New Figma Fonts Found"), message, L("定位字体映射", "Open Font Mapping"), L("稍后", "Later")))
            {
                SelectGlobalFontMapping();
            }
        }

        private int CountUnmappedFontEntries()
        {
            BindFontMappingAsset();
            if (_fontMapping == null || _fontMapping.Entries == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _fontMapping.Entries.Count; i++)
            {
                FigmaFontMappingAsset.Entry entry = _fontMapping.Entries[i];
                if (entry != null && entry.FontAsset == null)
                {
                    count++;
                }
            }

            return count;
        }

        private void SelectGlobalFontMapping()
        {
            BindFontMappingAsset();
            if (_fontMapping == null)
            {
                return;
            }

            Selection.activeObject = _fontMapping;
            EditorGUIUtility.PingObject(_fontMapping);
        }

        private bool ConfirmPluginPackageImport(FigmaPluginPackageInfo packageInfo, FigmaImportSettings settings)
        {
            bool largePackage = IsLargePluginPackage(packageInfo);
            string outputFolder = FigmaPathUtility.BuildImportFolder(
                settings.OutputFolder,
                packageInfo.FileKey,
                packageInfo.SelectedNodeId);
            string referenceLabel = packageInfo.HasReferenceImage
                ? L("（包含参考图）", " (includes reference image)")
                : L("（不包含参考图）", " (no reference image)");
            string message = F(
                "即将导入 Figma 插件离线包：\n\n包文件：{0}\n包大小：{1}\n节点：{2}\nFile Key：{3}\n根尺寸：{4} x {5}\n图片倍率：{6}\n容器策略：{7}\n包文本数据：{8}\nUnity 文本模式：{9}\n字体映射：{10}\n图片资源：{11}{12}\n切图图层：{13} 个\n输出目录：{14}",
                "Import Figma plugin package:\n\nPackage: {0}\nSize: {1}\nNode: {2}\nFile Key: {3}\nRoot size: {4} x {5}\nImage scale: {6}\nContainer strategy: {7}\nPackage text data: {8}\nUnity text mode: {9}\nFont mapping: {10}\nImage assets: {11}{12}\nLayers: {13}\nOutput folder: {14}",
                packageInfo.PackageFileName,
                FormatFileSize(packageInfo.PackageSizeBytes),
                FormatPluginPackageNode(packageInfo),
                packageInfo.FileKey,
                FormatNumber(packageInfo.RootWidth),
                FormatNumber(packageInfo.RootHeight),
                FormatNumber(packageInfo.ImageScale),
                FormatPluginContainerStrategy(packageInfo.ContainerSliceStrategy),
                FormatPluginTextMode(packageInfo.TextImportMode),
                FormatSettingsTextMode(settings.TextImportMode),
                settings.FontMapping == null ? FigmaFontMappingService.GlobalMappingAssetPath : AssetDatabase.GetAssetPath(settings.FontMapping),
                packageInfo.AssetCount,
                referenceLabel,
                packageInfo.LayerCount,
                outputFolder);

            if (settings.SavePrefab)
            {
                message += "\n" + F("Prefab 目录：{0}", "Prefab folder: {0}", settings.PrefabFolder);
            }

            if (largePackage)
            {
                message += "\n\n" + L("这个离线包较大，导入会写入较多 PNG，Unity 可能短暂卡顿。建议确认当前场景已保存。", "This package is large; importing writes many PNGs and Unity may freeze briefly. Make sure the current scene is saved.");
            }

            string title = largePackage ? L("确认导入大型 Figma 插件包", "Import Large Figma Plugin Package?") : L("确认导入 Figma 插件包", "Import Figma Plugin Package?");
            return EditorUtility.DisplayDialog(title, message, L("导入", "Import"), L("取消", "Cancel"));
        }

        private static bool IsLargePluginPackage(FigmaPluginPackageInfo packageInfo)
        {
            return packageInfo.AssetCount >= LargePluginPackageAssetThreshold
                || packageInfo.PackageSizeBytes >= LargePluginPackageSizeBytes;
        }

        private static string FormatPluginPackageNode(FigmaPluginPackageInfo packageInfo)
        {
            string nodeName = string.IsNullOrWhiteSpace(packageInfo.SelectedNodeName)
                ? L("(未命名)", "(Unnamed)")
                : packageInfo.SelectedNodeName;
            string nodeType = string.IsNullOrWhiteSpace(packageInfo.SelectedNodeType)
                ? "UNKNOWN"
                : packageInfo.SelectedNodeType;
            return nodeName + " [" + nodeType + "] " + packageInfo.SelectedNodeId;
        }

        private static string FormatFileSize(long bytes)
        {
            const double OneKb = 1024d;
            const double OneMb = OneKb * 1024d;
            if (bytes >= OneMb)
            {
                return (bytes / OneMb).ToString("0.##") + " MB";
            }

            if (bytes >= OneKb)
            {
                return (bytes / OneKb).ToString("0.##") + " KB";
            }

            return bytes + " B";
        }

        private static string FormatPluginContainerStrategy(string value)
        {
            if (string.Equals(value, "strict", StringComparison.OrdinalIgnoreCase))
            {
                return L("严格 ExportSettings", "Strict ExportSettings");
            }

            if (string.Equals(value, "expand", StringComparison.OrdinalIgnoreCase))
            {
                return L("强制展开容器", "Force expand containers");
            }

            if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(value))
            {
                return L("自动", "Auto");
            }

            return value;
        }

        private static string FormatPluginTextMode(string value)
        {
            if (string.Equals(value, "metadata", StringComparison.OrdinalIgnoreCase))
            {
                return L("仅导出文本元数据", "Metadata only");
            }

            if (string.Equals(value, "tmp", StringComparison.OrdinalIgnoreCase))
            {
                return "TextMeshPro";
            }

            if (string.Equals(value, "image", StringComparison.OrdinalIgnoreCase))
            {
                return L("图片", "Image");
            }

            if (string.Equals(value, "smart", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(value))
            {
                return L("智能", "Smart");
            }

            return value;
        }

        private static string FormatSettingsTextMode(FigmaTextImportMode mode)
        {
            switch (mode)
            {
                case FigmaTextImportMode.Image:
                    return L("图片", "Image");
                case FigmaTextImportMode.TextMeshPro:
                    return "TextMeshPro";
                default:
                    return L("智能", "Smart");
            }
        }

        private static string FormatNumber(float value)
        {
            return value.ToString("0.##");
        }

        private bool ConfirmSliceEstimate()
        {
            if (_selectedNode == null || _sliceMode == FigmaSliceMode.FlattenSelectedFrameAsSingleImage)
            {
                return true;
            }

            FigmaDocumentParser.EnsureBoundsFromChildren(_selectedNode);
            List<FigmaNodeInfo> importableNodes = FigmaDocumentParser.CollectImportableNodes(_selectedNode, _sliceMode, _containerSliceStrategy);
            int sliceCount = importableNodes.Count;
            AddLog(F("预计切图数量：{0}", "Estimated slices: {0}", sliceCount));

            int overlapCount = FigmaDocumentParser.CountNodesWithSelectedAncestor(importableNodes);
            if (overlapCount > 0)
            {
                AddLog(F("REST 导入提示：{0} 个切图的祖先容器也会导出，半透明/阴影区域可能出现重复叠加；需要严格避免时请改用 Figma 插件离线包。", "REST import warning: the ancestor containers of {0} slices are also exported; semi-transparent or shadowed areas may overdraw. Use the offline Figma plugin package when this matters.", overlapCount));
            }

            if (sliceCount <= SliceWarningThreshold)
            {
                return true;
            }

            string message = F(
                "预计将导出 {0} 个切图。\n\n这会请求 Figma 图片地址并下载大量 PNG，可能再次触发 429 限流。\n\n建议先改用“仅导入整张参考图”或“只导入已设置 Export 的节点”。\n\n是否继续导入？",
                "This import will export {0} slices.\n\nIt requests Figma image URLs and downloads many PNGs, which may trigger 429 rate limits.\n\nConsider using \"Reference image only\" or \"Export-marked nodes only\" first.\n\nContinue?",
                sliceCount);
            return EditorUtility.DisplayDialog(L("切图数量较多", "Many Slices"), message, L("继续导入", "Continue"), L("取消", "Cancel"));
        }

        private FigmaImportSettings BuildSettings(string fileKey)
        {
            FigmaImportSettings settings = new FigmaImportSettings();
            settings.FileKey = fileKey;
            settings.AccessToken = _accessToken;
            settings.OutputFolder = FigmaPathUtility.NormalizeAssetFolder(_outputFolder);
            settings.TargetCanvas = _targetCanvas;
            settings.SliceMode = _sliceMode;
            settings.ContainerSliceStrategy = _containerSliceStrategy;
            settings.TextImportMode = _textImportMode;
            settings.FontMapping = FigmaFontMappingService.GetOrCreateGlobalMapping();
            _fontMapping = settings.FontMapping;
            settings.ImageScale = _imageScale;
            settings.CompressionMode = _compressionMode;
            settings.CreateReference = _createReference;
            settings.ReferenceAlpha = _referenceAlpha;
            settings.DisableRaycastTarget = _disableRaycastTarget;
            settings.OverwriteExisting = _overwriteExisting;
            settings.SavePrefab = _savePrefab;
            settings.PrefabFolder = NormalizePrefabFolder(_prefabFolder);
            settings.IgnoreSslCertificateErrors = _ignoreSslCertificateErrors;
            settings.UseLoadedNodeTree = true;
            return settings;
        }

        private FigmaImportSettings BuildOfflineSettings()
        {
            FigmaImportSettings settings = BuildSettings(FigmaPathUtility.ExtractFileKey(_fileInput));
            settings.AccessToken = string.Empty;
            return settings;
        }

        private bool ValidateConnectionInput(string fileKey)
        {
            _error = string.Empty;

            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                _error = L("请填写 Figma Personal Access Token。", "Enter a Figma Personal Access Token.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(fileKey))
            {
                _error = L("请填写 Figma File Key 或文件 URL。", "Enter a Figma File Key or file URL.");
                return false;
            }

            _fileInput = fileKey;
            _outputFolder = FigmaPathUtility.NormalizeAssetFolder(_outputFolder);
            return true;
        }

        private void BeginBusy(string status)
        {
            _isBusy = true;
            _error = string.Empty;
            _status = status;
            _progress = 0f;
            _cancellation = new CancellationTokenSource();
            Repaint();
        }

        private void EndBusy()
        {
            _isBusy = false;
            if (_cancellation != null)
            {
                _cancellation.Dispose();
                _cancellation = null;
            }

            _progress = -1f;

            Repaint();
        }

        private void OnProgress(FigmaImportProgress progress)
        {
            _status = progress.Message;
            _progress = progress.Value;
            Repaint();
        }

        private void OnClientStatus(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            _status = message;
            AddLog(message);
            Repaint();
        }

        private void AddLog(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            _logs.Add(line);
            if (_logs.Count > 80)
            {
                _logs.RemoveAt(0);
            }

            _logScroll.y = float.MaxValue;
            Repaint();
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(PrefToken, _accessToken ?? string.Empty);
            EditorPrefs.SetString(PrefFileInput, _fileInput ?? string.Empty);
            EditorPrefs.SetString(PrefOutputFolder, _outputFolder ?? "Assets/FigmaImports");
            EditorPrefs.SetInt(PrefSliceMode, (int)_sliceMode);
            EditorPrefs.SetInt(PrefContainerSliceStrategy, (int)_containerSliceStrategy);
            EditorPrefs.SetInt(PrefTextImportMode, (int)_textImportMode);
            EditorPrefs.SetString(PrefFontMappingPath, _fontMapping == null ? string.Empty : AssetDatabase.GetAssetPath(_fontMapping));
            EditorPrefs.SetFloat(PrefImageScale, _imageScale);
            EditorPrefs.SetInt(PrefCompressionMode, (int)_compressionMode);
            EditorPrefs.SetFloat(PrefReferenceAlpha, _referenceAlpha);
            EditorPrefs.SetBool(PrefCreateReference, _createReference);
            EditorPrefs.SetBool(PrefDisableRaycast, _disableRaycastTarget);
            EditorPrefs.SetBool(PrefOverwrite, _overwriteExisting);
            EditorPrefs.SetBool(PrefSavePrefab, _savePrefab);
            EditorPrefs.SetString(PrefPrefabFolder, string.IsNullOrWhiteSpace(_prefabFolder) ? "Assets/FigmaImports/Prefabs" : _prefabFolder);
            EditorPrefs.SetBool(PrefIgnoreSslCertificateErrors, _ignoreSslCertificateErrors);
            EditorPrefs.SetBool(PrefPreferCachedFile, _preferCachedFile);
        }

        private void PickOutputFolder()
        {
            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            string picked = EditorUtility.OpenFolderPanel(L("选择输出资源目录", "Select Output Folder"), Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            picked = picked.Replace('\\', '/');
            if (!picked.StartsWith(projectRoot + "/", StringComparison.Ordinal))
            {
                _error = L("输出目录必须位于当前 Unity 项目内。", "The output folder must be inside the current Unity project.");
                return;
            }

            string assetPath = picked.Substring(projectRoot.Length + 1);
            _outputFolder = FigmaPathUtility.NormalizeAssetFolder(assetPath);
            RefreshImportedManifests(false);
        }

        private void PickPrefabFolder()
        {
            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            string picked = EditorUtility.OpenFolderPanel(L("选择 Prefab 输出目录", "Select Prefab Output Folder"), Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            picked = picked.Replace('\\', '/');
            if (!picked.StartsWith(projectRoot + "/", StringComparison.Ordinal))
            {
                _error = L("Prefab 输出目录必须位于当前 Unity 项目内。", "The Prefab folder must be inside the current Unity project.");
                return;
            }

            string assetPath = picked.Substring(projectRoot.Length + 1);
            _prefabFolder = FigmaPathUtility.NormalizeAssetFolder(assetPath);
        }

        private static string NormalizePrefabFolder(string prefabFolder)
        {
            if (string.IsNullOrWhiteSpace(prefabFolder))
            {
                return "Assets/FigmaImports/Prefabs";
            }

            return FigmaPathUtility.NormalizeAssetFolder(prefabFolder);
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

        private bool GetFoldout(FigmaNodeInfo node)
        {
            if (node == null || string.IsNullOrEmpty(node.Id))
            {
                return false;
            }

            bool value;
            return _foldouts.TryGetValue(node.Id, out value) && value;
        }

        private void SetFoldout(FigmaNodeInfo node, bool value)
        {
            if (node != null && !string.IsNullOrEmpty(node.Id))
            {
                _foldouts[node.Id] = value;
            }
        }

        private static bool HasFrameDescendant(FigmaNodeInfo node)
        {
            if (node == null)
            {
                return false;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                FigmaNodeInfo child = node.Children[i];
                if (child.IsFrameLike || HasFrameDescendant(child))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<FigmaNodeInfo> GetFallbackTreeRoots(FigmaNodeInfo documentRoot)
        {
            List<FigmaNodeInfo> roots = new List<FigmaNodeInfo>();
            if (documentRoot == null)
            {
                return roots;
            }

            for (int i = 0; i < documentRoot.Children.Count; i++)
            {
                roots.Add(documentRoot.Children[i]);
            }

            if (roots.Count == 0 && !string.IsNullOrEmpty(documentRoot.Id))
            {
                roots.Add(documentRoot);
            }

            return roots;
        }

        private sealed class ImmediateProgress : IProgress<FigmaImportProgress>
        {
            private readonly Action<FigmaImportProgress> _handler;

            public ImmediateProgress(Action<FigmaImportProgress> handler)
            {
                _handler = handler;
            }

            public void Report(FigmaImportProgress value)
            {
                if (_handler != null)
                {
                    _handler(value);
                }
            }
        }

        private sealed class ImportedManifestEntry
        {
            public string ManifestPath;
            public string ButtonText;
            public DateTime SortTimeUtc;
        }

        [Serializable]
        private sealed class ImportedManifestSummaryDto
        {
            public string selectedNodeId;
            public string selectedNodeName;
            public string selectedNodeType;
            public string importedAt;
            public List<ImportedManifestLayerSummaryDto> layers;
        }

        [Serializable]
        private sealed class ImportedManifestLayerSummaryDto
        {
            public string nodeId;
        }
    }
}
