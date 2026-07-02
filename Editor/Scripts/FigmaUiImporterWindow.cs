using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

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

        private static readonly string[] SliceModeLabels =
        {
            "ExportSettings 优先，并补齐自动切片",
            "只导入已设置 Export 的节点",
            "自动切片可见图层",
            "仅导入整张参考图"
        };

        private static readonly string[] ContainerSliceStrategyLabels =
        {
            "自动：保留容器底图并展开组件实例",
            "严格 ExportSettings：标记容器优先",
            "强制展开容器：尽量拆到子级"
        };

        private static readonly string[] TextImportModeLabels =
        {
            "图片：文字也按 PNG 导入",
            "TextMeshPro：尽量转为可编辑文本",
            "智能：普通文本转 TMP，复杂文本用图片"
        };

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
        private readonly FigmaFileCacheService _fileCache = new FigmaFileCacheService();
        private int _loadedPageCount;
        private int _loadedFrameCount;
        private Vector2 _mainScroll;
        private Vector2 _treeScroll;
        private Vector2 _logScroll;
        private bool _isBusy;
        private string _status;
        private string _error;
        private float _progress = -1f;
        private CancellationTokenSource _cancellation;

        [MenuItem("Tools/Figma UI Importer")]
        public static void Open()
        {
            FigmaUiImporterWindow window = GetWindow<FigmaUiImporterWindow>();
            window.titleContent = new GUIContent("Figma UI 导入器");
            window.minSize = new Vector2(520f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
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
            _referenceAlpha = EditorPrefs.GetFloat(PrefReferenceAlpha, 0.5f);
            _createReference = EditorPrefs.GetBool(PrefCreateReference, true);
            _disableRaycastTarget = EditorPrefs.GetBool(PrefDisableRaycast, true);
            _overwriteExisting = EditorPrefs.GetBool(PrefOverwrite, false);
            _savePrefab = EditorPrefs.GetBool(PrefSavePrefab, false);
            _prefabFolder = EditorPrefs.GetString(PrefPrefabFolder, "Assets/FigmaImports/Prefabs");
            _ignoreSslCertificateErrors = EditorPrefs.GetBool(PrefIgnoreSslCertificateErrors, false);
            _preferCachedFile = EditorPrefs.GetBool(PrefPreferCachedFile, true);
            _status = "等待加载 Figma 文件。";
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
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);
            DrawSettings();
            EditorGUILayout.Space(8f);
            DrawActions();
            EditorGUILayout.Space(8f);
            DrawSelectionTree();
            EditorGUILayout.Space(8f);
            DrawStatus();
            EditorGUILayout.EndScrollView();
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("连接设置", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                _accessToken = EditorGUILayout.PasswordField("Figma Token", _accessToken);
                _fileInput = EditorGUILayout.TextField("File Key 或 URL", _fileInput);

                EditorGUILayout.BeginHorizontal();
                _outputFolder = EditorGUILayout.TextField("输出资源目录", _outputFolder);
                if (GUILayout.Button("选择", GUILayout.Width(56f)))
                {
                    PickOutputFolder();
                }
                EditorGUILayout.EndHorizontal();

                _targetCanvas = (Canvas)EditorGUILayout.ObjectField("目标 Canvas", _targetCanvas, typeof(Canvas), true);
                _sliceMode = (FigmaSliceMode)EditorGUILayout.Popup("切图模式", (int)_sliceMode, SliceModeLabels);
                using (new EditorGUI.DisabledScope(_sliceMode != FigmaSliceMode.ExportSettingsThenAutoSlice))
                {
                    _containerSliceStrategy = (FigmaContainerSliceStrategy)EditorGUILayout.Popup("容器处理策略", (int)_containerSliceStrategy, ContainerSliceStrategyLabels);
                }

                _textImportMode = (FigmaTextImportMode)EditorGUILayout.Popup("文本导入模式", (int)_textImportMode, TextImportModeLabels);
                _fontMapping = AssetDatabase.LoadAssetAtPath<FigmaFontMappingAsset>(FigmaFontMappingService.GlobalMappingAssetPath);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("全局字体映射", _fontMapping, typeof(FigmaFontMappingAsset), false);
                }
                _imageScale = Mathf.Clamp(EditorGUILayout.FloatField("图片倍率", _imageScale), 0.01f, 4f);
                _createReference = EditorGUILayout.Toggle("创建参考图", _createReference);
                using (new EditorGUI.DisabledScope(!_createReference))
                {
                    _referenceAlpha = EditorGUILayout.Slider("参考图透明度", _referenceAlpha, 0f, 1f);
                }

                _disableRaycastTarget = EditorGUILayout.Toggle("关闭图片射线检测", _disableRaycastTarget);
                _overwriteExisting = EditorGUILayout.Toggle("覆盖同名场景根节点 / Prefab", _overwriteExisting);
                _savePrefab = EditorGUILayout.Toggle("保存为 Prefab", _savePrefab);
                using (new EditorGUI.DisabledScope(!_savePrefab))
                {
                    EditorGUILayout.BeginHorizontal();
                    _prefabFolder = EditorGUILayout.TextField("Prefab 输出目录", _prefabFolder);
                    if (GUILayout.Button("选择", GUILayout.Width(56f)))
                    {
                        PickPrefabFolder();
                    }
                    EditorGUILayout.EndHorizontal();
                }

                _preferCachedFile = EditorGUILayout.Toggle("优先使用本地缓存", _preferCachedFile);
                _ignoreSslCertificateErrors = EditorGUILayout.Toggle("忽略 SSL 证书错误", _ignoreSslCertificateErrors);
                if (_ignoreSslCertificateErrors)
                {
                    EditorGUILayout.HelpBox("仅在 Unity 报 Unable to complete SSL connection 时使用。该选项会跳过 HTTPS 证书校验。", MessageType.Warning);
                }
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                if (GUILayout.Button("加载 Figma 文件", GUILayout.Height(30f)))
                {
                    _ = LoadFileAsync(false);
                }

                if (GUILayout.Button("强制重新加载", GUILayout.Width(110f), GUILayout.Height(30f)))
                {
                    _ = LoadFileAsync(true);
                }

                using (new EditorGUI.DisabledScope(_selectedNode == null))
                {
                    if (GUILayout.Button("导入选中项", GUILayout.Height(30f)))
                    {
                        _ = ImportSelectedAsync();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!_isBusy))
            {
                if (GUILayout.Button("取消", GUILayout.Width(72f), GUILayout.Height(30f)))
                {
                    if (_cancellation != null)
                    {
                        _cancellation.Cancel();
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                if (GUILayout.Button("从 Manifest 重建场景", GUILayout.Height(26f)))
                {
                    RebuildFromManifest();
                }

                if (GUILayout.Button("从 Figma 插件包导入", GUILayout.Height(26f)))
                {
                    ImportFromPluginPackage();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                if (GUILayout.Button("打开/创建字体映射", GUILayout.Height(26f)))
                {
                    OpenGlobalFontMapping();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                if (GUILayout.Button("应用映射到选中项", GUILayout.Height(26f)))
                {
                    ApplyFontMappingToSelection();
                }

                if (GUILayout.Button("应用映射到场景文字", GUILayout.Height(26f)))
                {
                    ApplyFontMappingToScene();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSelectionTree()
        {
            EditorGUILayout.LabelField("页面 / Frame", EditorStyles.boldLabel);

            if (_documentRoot != null)
            {
                EditorGUILayout.LabelField(
                    string.Format("已加载：{0} 个 Page，{1} 个 Frame / Component / Instance", _loadedPageCount, _loadedFrameCount),
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(260f));
            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll, GUILayout.ExpandHeight(true));

            if (_documentRoot == null)
            {
                EditorGUILayout.HelpBox("加载 Figma 文件后，这里会显示 Page 和 Frame。", MessageType.Info);
            }
            else
            {
                List<FigmaNodeInfo> pages = FigmaDocumentParser.GetPages(_documentRoot);
                List<FigmaNodeInfo> roots = pages.Count > 0 ? pages : GetFallbackTreeRoots(_documentRoot);

                if (pages.Count == 0 && roots.Count > 0)
                {
                    EditorGUILayout.HelpBox("没有读取到标准 CANVAS 类型 Page，已改为显示 document 的一级节点。", MessageType.Warning);
                }

                if (roots.Count == 0)
                {
                    EditorGUILayout.HelpBox("Figma 返回的 document 没有可显示的子节点。请确认 token 有权限访问该文件，且文件不是空文件。", MessageType.Warning);
                }

                for (int i = 0; i < roots.Count; i++)
                {
                    DrawNodeTreeRoot(roots[i], 0);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
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
                    _status = "已选择：" + node.DisplayName;
                    Repaint();
                }
            }

            GUIStyle labelStyle = _selectedNode == node ? EditorStyles.boldLabel : EditorStyles.label;
            EditorGUILayout.LabelField(node.DisplayName, labelStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("状态", EditorStyles.boldLabel);

            if (_progress >= 0f)
            {
                Rect rect = GUILayoutUtility.GetRect(1f, 20f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(rect, _progress, _status);
            }
            else
            {
                EditorGUILayout.HelpBox(_status, MessageType.None);
            }

            if (!string.IsNullOrEmpty(_error))
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
            }

            EditorGUILayout.LabelField("日志", EditorStyles.boldLabel);
            Rect logRect = GUILayoutUtility.GetRect(1f, 120f, GUILayout.ExpandWidth(true));
            GUI.Box(logRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(logRect.x + 6f, logRect.y + 4f, logRect.width - 12f, logRect.height - 8f));
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll);
            for (int i = 0; i < _logs.Count; i++)
            {
                EditorGUILayout.LabelField(_logs[i], EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private async Task LoadFileAsync(bool forceRemote)
        {
            string fileKey = FigmaPathUtility.ExtractFileKey(_fileInput);
            if (!ValidateConnectionInput(fileKey))
            {
                return;
            }

            BeginBusy("正在加载 Figma 文件...");
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
                        ApplyLoadedDocument(fileKey, FigmaDocumentParser.ParseFile(json), "从本地缓存加载完成。");
                        return;
                    }
                    catch (Exception cacheException)
                    {
                        AddLog("本地缓存解析失败，将改为远程加载：" + cacheException.Message);
                    }
                }

                if (!forceRemote && _preferCachedFile && !string.IsNullOrEmpty(cacheMessage))
                {
                    AddLog(cacheMessage);
                }

                FigmaApiClient client = new FigmaApiClient(_accessToken, _ignoreSslCertificateErrors, OnClientStatus);
                json = await client.LoadFileJsonAsync(fileKey, _cancellation.Token);
                _fileCache.Save(fileKey, json);
                ApplyLoadedDocument(fileKey, FigmaDocumentParser.ParseFile(json), forceRemote ? "强制重新加载完成。" : "远程加载完成并已写入本地缓存。");
            }
            catch (OperationCanceledException)
            {
                _status = "已取消加载。";
                AddLog("加载已取消。");
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = "加载失败。";
                AddLog("加载失败：" + exception.Message);
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

            _status = string.Format("{0} 读取到 {1} 个 Page，{2} 个 Frame。请选择 Page 或 Frame。", sourceMessage, _loadedPageCount, _loadedFrameCount);
            AddLog(string.Format("{0} {1} 个 Page，{2} 个 Frame，document 一级节点 {3} 个。", sourceMessage, _loadedPageCount, _loadedFrameCount, _documentRoot.Children.Count));
        }

        private async Task ImportSelectedAsync()
        {
            if (_selectedNode == null)
            {
                _error = "请先选择一个 Page 或 Frame。";
                return;
            }

            string fileKey = FigmaPathUtility.ExtractFileKey(_fileInput);
            if (!ValidateConnectionInput(fileKey))
            {
                return;
            }

            if (string.IsNullOrEmpty(_loadedFileKey) || !string.Equals(_loadedFileKey, fileKey, StringComparison.Ordinal))
            {
                _error = "当前选择来自上一次加载的文件。请先重新加载 Figma 文件，再导入。";
                return;
            }

            if (_selectedNode.IsPage)
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "确认导入 Page",
                    "Page 可能包含多个界面，导入范围可能很大。建议优先选择具体 Frame。\n\n确定继续导入这个 Page 吗？",
                    "继续导入",
                    "取消");
                if (!confirmed)
                {
                    return;
                }
            }

            if (!ConfirmSliceEstimate())
            {
                return;
            }

            BeginBusy("准备导入...");
            SavePrefs();

            try
            {
                FigmaImportSettings settings = BuildSettings(fileKey);
                FigmaApiClient client = new FigmaApiClient(_accessToken, _ignoreSslCertificateErrors, OnClientStatus);
                FigmaImportOrchestrator importer = new FigmaImportOrchestrator(client);
                Progress<FigmaImportProgress> progress = new Progress<FigmaImportProgress>(OnProgress);
                FigmaImportResult result = await importer.ImportAsync(settings, _selectedNode, progress, _cancellation.Token);

                _status = "导入完成：" + result.LayerCount + " 个切图图层。";
                _progress = 1f;
                AddLog("输出目录：" + result.OutputFolder);
                AddLog("Manifest：" + result.ManifestPath);
                AddFontMappingLog(result);
                if (!string.IsNullOrEmpty(result.DiagnosticReportPath))
                {
                    AddLog("诊断报告：" + result.DiagnosticReportPath);
                }

                if (!string.IsNullOrEmpty(result.PrefabPath))
                {
                    AddLog("Prefab：" + result.PrefabPath);
                }

                if (result.RootObject != null)
                {
                    AddLog("场景根节点：" + result.RootObject.name);
                }
            }
            catch (OperationCanceledException)
            {
                _status = "已取消导入。";
                AddLog("导入已取消。");
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = "导入失败。";
                AddLog("导入失败：" + exception.Message);
            }
            finally
            {
                EndBusy();
            }
        }

        private void RebuildFromManifest()
        {
            string pickedManifest = EditorUtility.OpenFilePanel("选择 manifest.json", Application.dataPath, "json");
            if (string.IsNullOrEmpty(pickedManifest))
            {
                return;
            }

            BeginBusy("准备从 manifest 重建场景...");
            SavePrefs();

            try
            {
                FigmaImportSettings settings = BuildSettings(FigmaPathUtility.ExtractFileKey(_fileInput));
                FigmaOfflineSceneRebuilder rebuilder = new FigmaOfflineSceneRebuilder();
                FigmaImportResult result = rebuilder.Rebuild(settings, pickedManifest, new ImmediateProgress(OnProgress));

                _status = "离线重建完成：" + result.LayerCount + " 个切图图层。";
                _progress = 1f;
                AddLog("离线重建输出目录：" + result.OutputFolder);
                AddLog("Manifest：" + result.ManifestPath);
                AddFontMappingLog(result);
                if (!string.IsNullOrEmpty(result.DiagnosticReportPath))
                {
                    AddLog("诊断报告：" + result.DiagnosticReportPath);
                }

                if (!string.IsNullOrEmpty(result.PrefabPath))
                {
                    AddLog("Prefab：" + result.PrefabPath);
                }

                if (result.RootObject != null)
                {
                    AddLog("场景根节点：" + result.RootObject.name);
                }
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = "离线重建失败。";
                AddLog("离线重建失败：" + exception.Message);
            }
            finally
            {
                EndBusy();
            }
        }

        private void ImportFromPluginPackage()
        {
            string pickedPackage = EditorUtility.OpenFilePanel("选择 Figma 插件导出包", string.Empty, "json");
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
                _status = "Figma 插件包无效。";
                AddLog("Figma 插件包无效：" + exception.Message);
                Repaint();
                return;
            }

            FigmaImportSettings previewSettings = BuildOfflineSettings();
            if (!ConfirmPluginPackageImport(packageInfo, previewSettings))
            {
                return;
            }

            BeginBusy("准备导入 Figma 插件包...");
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

                _status = "Figma 插件包导入完成：" + result.LayerCount + " 个切图图层。";
                _progress = 1f;
                AddLog("插件包：" + pickedPackage);
                AddLog("插件包节点：" + FormatPluginPackageNode(packageInfo));
                AddLog("插件包资源：" + packageInfo.AssetCount + " 个图片资源，" + packageInfo.LayerCount + " 个切图图层，大小 " + FormatFileSize(packageInfo.PackageSizeBytes));
                AddLog("导入输出目录：" + result.OutputFolder);
                AddLog("Manifest：" + result.ManifestPath);
                AddFontMappingLog(result);
                if (!string.IsNullOrEmpty(result.DiagnosticReportPath))
                {
                    AddLog("诊断报告：" + result.DiagnosticReportPath);
                }

                if (!string.IsNullOrEmpty(result.PrefabPath))
                {
                    AddLog("Prefab：" + result.PrefabPath);
                }

                if (result.RootObject != null)
                {
                    AddLog("场景根节点：" + result.RootObject.name);
                }
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = "Figma 插件包导入失败。";
                AddLog("Figma 插件包导入失败：" + exception.Message);
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
                _status = "已打开全局字体映射。";
                AddLog("全局字体映射：" + AssetDatabase.GetAssetPath(_fontMapping));
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = "打开全局字体映射失败。";
                AddLog("打开全局字体映射失败：" + exception.Message);
            }

            Repaint();
        }

        private void ApplyFontMappingToSelection()
        {
            try
            {
                _fontMapping = FigmaFontMappingService.GetOrCreateGlobalMapping();
                SavePrefs();
                int count = FigmaFontMappingService.ApplyGlobalMappingToSelection();
                _status = "已更新选中项文字字体：" + count + " 个。";
                AddLog("已应用全局字体映射到选中项：" + count + " 个 TMP 文字。");
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = "应用字体映射失败。";
                AddLog("应用字体映射失败：" + exception.Message);
            }

            Repaint();
        }

        private void ApplyFontMappingToScene()
        {
            try
            {
                _fontMapping = FigmaFontMappingService.GetOrCreateGlobalMapping();
                SavePrefs();
                int count = FigmaFontMappingService.ApplyGlobalMappingToOpenScenes();
                _status = "已更新场景文字字体：" + count + " 个。";
                AddLog("已应用全局字体映射到场景文字：" + count + " 个 TMP 文字。");
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = "应用字体映射失败。";
                AddLog("应用字体映射失败：" + exception.Message);
            }

            Repaint();
        }

        private void AddFontMappingLog(FigmaImportResult result)
        {
            if (result == null)
            {
                return;
            }

            AddLog("字体映射：" + result.FontMappingPath
                + "，文本样式 " + result.FontMappingTextStyleCount
                + " 个，新增 " + result.FontMappingAddedEntries
                + " 个，更新 " + result.FontMappingUpdatedEntries + " 个。");
        }

        private bool ConfirmPluginPackageImport(FigmaPluginPackageInfo packageInfo, FigmaImportSettings settings)
        {
            bool largePackage = IsLargePluginPackage(packageInfo);
            string outputFolder = FigmaPathUtility.BuildImportFolder(
                settings.OutputFolder,
                packageInfo.FileKey,
                packageInfo.SelectedNodeId);
            string message =
                "即将导入 Figma 插件离线包：\n\n"
                + "包文件：" + packageInfo.PackageFileName + "\n"
                + "包大小：" + FormatFileSize(packageInfo.PackageSizeBytes) + "\n"
                + "节点：" + FormatPluginPackageNode(packageInfo) + "\n"
                + "File Key：" + packageInfo.FileKey + "\n"
                + "根尺寸：" + FormatNumber(packageInfo.RootWidth) + " x " + FormatNumber(packageInfo.RootHeight) + "\n"
                + "图片倍率：" + FormatNumber(packageInfo.ImageScale) + "\n"
                + "容器策略：" + FormatPluginContainerStrategy(packageInfo.ContainerSliceStrategy) + "\n"
                + "包文本数据：" + FormatPluginTextMode(packageInfo.TextImportMode) + "\n"
                + "Unity 文本模式：" + FormatSettingsTextMode(settings.TextImportMode) + "\n"
                + "字体映射：" + (settings.FontMapping == null ? FigmaFontMappingService.GlobalMappingAssetPath : AssetDatabase.GetAssetPath(settings.FontMapping)) + "\n"
                + "图片资源：" + packageInfo.AssetCount + " 个"
                + (packageInfo.HasReferenceImage ? "（包含参考图）" : "（不包含参考图）") + "\n"
                + "切图图层：" + packageInfo.LayerCount + " 个\n"
                + "输出目录：" + outputFolder;

            if (settings.SavePrefab)
            {
                message += "\nPrefab 目录：" + settings.PrefabFolder;
            }

            if (largePackage)
            {
                message += "\n\n这个离线包较大，导入会写入较多 PNG，Unity 可能短暂卡顿。建议确认当前场景已保存。";
            }

            string title = largePackage ? "确认导入大型 Figma 插件包" : "确认导入 Figma 插件包";
            return EditorUtility.DisplayDialog(title, message, "导入", "取消");
        }

        private static bool IsLargePluginPackage(FigmaPluginPackageInfo packageInfo)
        {
            return packageInfo.AssetCount >= LargePluginPackageAssetThreshold
                || packageInfo.PackageSizeBytes >= LargePluginPackageSizeBytes;
        }

        private static string FormatPluginPackageNode(FigmaPluginPackageInfo packageInfo)
        {
            string nodeName = string.IsNullOrWhiteSpace(packageInfo.SelectedNodeName)
                ? "(未命名)"
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
                return "严格 ExportSettings";
            }

            if (string.Equals(value, "expand", StringComparison.OrdinalIgnoreCase))
            {
                return "强制展开容器";
            }

            if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(value))
            {
                return "自动";
            }

            return value;
        }

        private static string FormatPluginTextMode(string value)
        {
            if (string.Equals(value, "metadata", StringComparison.OrdinalIgnoreCase))
            {
                return "仅导出文本元数据";
            }

            if (string.Equals(value, "tmp", StringComparison.OrdinalIgnoreCase))
            {
                return "TextMeshPro";
            }

            if (string.Equals(value, "image", StringComparison.OrdinalIgnoreCase))
            {
                return "图片";
            }

            if (string.Equals(value, "smart", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(value))
            {
                return "智能";
            }

            return value;
        }

        private static string FormatSettingsTextMode(FigmaTextImportMode mode)
        {
            switch (mode)
            {
                case FigmaTextImportMode.Image:
                    return "图片";
                case FigmaTextImportMode.TextMeshPro:
                    return "TextMeshPro";
                default:
                    return "智能";
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
            AddLog("预计切图数量：" + sliceCount);

            if (sliceCount <= SliceWarningThreshold)
            {
                return true;
            }

            string message = string.Format(
                "预计将导出 {0} 个切图。\n\n这会请求 Figma 图片地址并下载大量 PNG，可能再次触发 429 限流。\n\n建议先改用“仅导入整张参考图”或“只导入已设置 Export 的节点”。\n\n是否继续导入？",
                sliceCount);
            return EditorUtility.DisplayDialog("切图数量较多", message, "继续导入", "取消");
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
                _error = "请填写 Figma Personal Access Token。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(fileKey))
            {
                _error = "请填写 Figma File Key 或文件 URL。";
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
            string picked = EditorUtility.OpenFolderPanel("选择输出资源目录", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            picked = picked.Replace('\\', '/');
            if (!picked.StartsWith(projectRoot + "/", StringComparison.Ordinal))
            {
                _error = "输出目录必须位于当前 Unity 项目内。";
                return;
            }

            string assetPath = picked.Substring(projectRoot.Length + 1);
            _outputFolder = FigmaPathUtility.NormalizeAssetFolder(assetPath);
        }

        private void PickPrefabFolder()
        {
            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            string picked = EditorUtility.OpenFolderPanel("选择 Prefab 输出目录", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            picked = picked.Replace('\\', '/');
            if (!picked.StartsWith(projectRoot + "/", StringComparison.Ordinal))
            {
                _error = "Prefab 输出目录必须位于当前 Unity 项目内。";
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
    }
}
