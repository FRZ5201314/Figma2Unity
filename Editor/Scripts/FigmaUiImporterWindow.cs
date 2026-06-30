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
        private const string PrefImageScale = "FigmaUiImporter.ImageScale";
        private const string PrefReferenceAlpha = "FigmaUiImporter.ReferenceAlpha";
        private const string PrefCreateReference = "FigmaUiImporter.CreateReference";
        private const string PrefDisableRaycast = "FigmaUiImporter.DisableRaycast";
        private const string PrefOverwrite = "FigmaUiImporter.Overwrite";
        private const string PrefIgnoreSslCertificateErrors = "FigmaUiImporter.IgnoreSslCertificateErrors";

        private static readonly string[] SliceModeLabels =
        {
            "ExportSettings 优先，并补齐自动切片",
            "只导入已设置 Export 的节点",
            "自动切片可见图层",
            "仅导入整张参考图"
        };

        private string _accessToken;
        private string _fileInput;
        private string _loadedFileKey;
        private string _outputFolder;
        private Canvas _targetCanvas;
        private FigmaSliceMode _sliceMode;
        private float _imageScale;
        private bool _createReference;
        private float _referenceAlpha;
        private bool _disableRaycastTarget;
        private bool _overwriteExisting;
        private bool _ignoreSslCertificateErrors;

        private FigmaNodeInfo _documentRoot;
        private FigmaNodeInfo _selectedNode;
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();
        private readonly List<string> _logs = new List<string>();
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
            _imageScale = EditorPrefs.GetFloat(PrefImageScale, 1f);
            _referenceAlpha = EditorPrefs.GetFloat(PrefReferenceAlpha, 0.5f);
            _createReference = EditorPrefs.GetBool(PrefCreateReference, true);
            _disableRaycastTarget = EditorPrefs.GetBool(PrefDisableRaycast, true);
            _overwriteExisting = EditorPrefs.GetBool(PrefOverwrite, false);
            _ignoreSslCertificateErrors = EditorPrefs.GetBool(PrefIgnoreSslCertificateErrors, false);
            _status = "等待加载 Figma 文件。";
        }

        private void OnDisable()
        {
            SavePrefs();
            if (_cancellation != null)
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
                _cancellation = null;
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
                _imageScale = Mathf.Clamp(EditorGUILayout.FloatField("图片倍率", _imageScale), 0.01f, 4f);
                _createReference = EditorGUILayout.Toggle("创建参考图", _createReference);
                using (new EditorGUI.DisabledScope(!_createReference))
                {
                    _referenceAlpha = EditorGUILayout.Slider("参考图透明度", _referenceAlpha, 0f, 1f);
                }

                _disableRaycastTarget = EditorGUILayout.Toggle("关闭图片射线检测", _disableRaycastTarget);
                _overwriteExisting = EditorGUILayout.Toggle("覆盖同名场景根节点", _overwriteExisting);
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
                    _ = LoadFileAsync();
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

        private async Task LoadFileAsync()
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
                FigmaApiClient client = new FigmaApiClient(_accessToken, _ignoreSslCertificateErrors, OnClientStatus);
                _documentRoot = await client.LoadFileAsync(fileKey, _cancellation.Token);
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

                _status = string.Format("加载完成。读取到 {0} 个 Page，{1} 个 Frame。请选择 Page 或 Frame。", _loadedPageCount, _loadedFrameCount);
                AddLog(string.Format("加载完成：{0} 个 Page，{1} 个 Frame，document 一级节点 {2} 个。", _loadedPageCount, _loadedFrameCount, _documentRoot.Children.Count));
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

        private FigmaImportSettings BuildSettings(string fileKey)
        {
            FigmaImportSettings settings = new FigmaImportSettings();
            settings.FileKey = fileKey;
            settings.AccessToken = _accessToken;
            settings.OutputFolder = FigmaPathUtility.NormalizeAssetFolder(_outputFolder);
            settings.TargetCanvas = _targetCanvas;
            settings.SliceMode = _sliceMode;
            settings.ImageScale = _imageScale;
            settings.CreateReference = _createReference;
            settings.ReferenceAlpha = _referenceAlpha;
            settings.DisableRaycastTarget = _disableRaycastTarget;
            settings.OverwriteExisting = _overwriteExisting;
            settings.IgnoreSslCertificateErrors = _ignoreSslCertificateErrors;
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
            EditorPrefs.SetFloat(PrefImageScale, _imageScale);
            EditorPrefs.SetFloat(PrefReferenceAlpha, _referenceAlpha);
            EditorPrefs.SetBool(PrefCreateReference, _createReference);
            EditorPrefs.SetBool(PrefDisableRaycast, _disableRaycastTarget);
            EditorPrefs.SetBool(PrefOverwrite, _overwriteExisting);
            EditorPrefs.SetBool(PrefIgnoreSslCertificateErrors, _ignoreSslCertificateErrors);
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
    }
}
