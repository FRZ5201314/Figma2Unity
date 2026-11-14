using FigmaUiImporter;
using UnityEditor;
using UnityEngine;
using static FigmaUiImporter.Editor.FigmaLocalization;

namespace FigmaUiImporter.Editor
{
    /// <summary>
    /// FigmaBackgroundBlur 的自定义 Inspector：
    /// 提供清晰的模糊模式切换、相机模式提示和手动刷新按钮。
    /// </summary>
    [CustomEditor(typeof(FigmaBackgroundBlur))]
    [CanEditMultipleObjects]
    public sealed class FigmaBackgroundBlurEditor : UnityEditor.Editor
    {
        private SerializedProperty _targetImage;
        private SerializedProperty _sourceMode;
        private SerializedProperty _sourceTexture;
        private SerializedProperty _sourceCamera;
        private SerializedProperty _sourceDesignSize;
        private SerializedProperty _radius;
        private SerializedProperty _downsample;
        private SerializedProperty _iterations;
        private SerializedProperty _useRuntimeBlur;
        private SerializedProperty _updateEveryFrame;
        private SerializedProperty _blurShader;
        private SerializedProperty _sourceRoot;
        private SerializedProperty _sourceRootDesignBounds;
        private SerializedProperty _sourceExportBounds;
        private SerializedProperty _trackedRect;
        private SerializedProperty _trackSourceUv;
        private SerializedProperty _debugShowCamera;
        private SerializedProperty _debugLogCamera;
        private SerializedProperty _debugSkipBlur;
        private bool _showTracking = true;
        private bool _showDebug = false;

        private void OnEnable()
        {
            _targetImage = serializedObject.FindProperty("targetImage");
            _sourceMode = serializedObject.FindProperty("sourceMode");
            _sourceTexture = serializedObject.FindProperty("sourceTexture");
            _sourceCamera = serializedObject.FindProperty("sourceCamera");
            _sourceDesignSize = serializedObject.FindProperty("sourceDesignSize");
            _radius = serializedObject.FindProperty("radius");
            _downsample = serializedObject.FindProperty("downsample");
            _iterations = serializedObject.FindProperty("iterations");
            _useRuntimeBlur = serializedObject.FindProperty("useRuntimeBlur");
            _updateEveryFrame = serializedObject.FindProperty("updateEveryFrame");
            _blurShader = serializedObject.FindProperty("blurShader");
            _sourceRoot = serializedObject.FindProperty("sourceRoot");
            _sourceRootDesignBounds = serializedObject.FindProperty("sourceRootDesignBounds");
            _sourceExportBounds = serializedObject.FindProperty("sourceExportBounds");
            _trackedRect = serializedObject.FindProperty("trackedRect");
            _trackSourceUv = serializedObject.FindProperty("trackSourceUv");
            _debugShowCamera = serializedObject.FindProperty("debugShowCamera");
            _debugLogCamera = serializedObject.FindProperty("debugLogCamera");
            _debugSkipBlur = serializedObject.FindProperty("debugSkipBlur");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(
                _targetImage,
                new GUIContent(L("目标 RawImage", "Target RawImage")));
            EditorGUILayout.PropertyField(
                _sourceMode,
                new GUIContent(L("模糊来源", "Source Mode")));

            FigmaBackgroundBlur.SourceMode sourceMode =
                (FigmaBackgroundBlur.SourceMode)_sourceMode.enumValueIndex;
            if (sourceMode == FigmaBackgroundBlur.SourceMode.StaticTexture)
            {
                EditorGUILayout.PropertyField(
                    _sourceTexture,
                    new GUIContent(L("背景纹理", "Source Texture")));
                EditorGUILayout.PropertyField(
                    _sourceDesignSize,
                    new GUIContent(L("源纹理设计尺寸", "Source Design Size")));
            }
            else
            {
                EditorGUILayout.PropertyField(
                    _sourceCamera,
                    new GUIContent(L("背景相机", "Source Camera")));
                if (_sourceCamera.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        L("Camera 模式需要指定 Source Camera；未指定时保留原纹理。",
                            "Camera mode needs a Source Camera; without one the original texture is kept."),
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        L("Camera 模式会把相机渲染到 RenderTexture 再模糊；请让相机只渲染 UI 背后的内容，并按需要关闭 Source Tracking。",
                            "Camera mode renders the camera to a RenderTexture before blurring. Make sure the camera renders only the content behind the UI; disable Source Tracking if needed."),
                        MessageType.Info);
                }
            }

            EditorGUILayout.Space();
            int modeIndex = !_useRuntimeBlur.boolValue
                ? 0
                : _updateEveryFrame.boolValue ? 2 : 1;
            int nextModeIndex = EditorGUILayout.Popup(
                L("模糊模式", "Blur Mode"),
                modeIndex,
                new[]
                {
                    L("烘焙贴图（运行时零开销）", "Baked texture (zero runtime cost)"),
                    L("运行时模糊一次（ComputeShader）", "Runtime blur once (ComputeShader)"),
                    L("运行时每帧更新（动态背景）", "Runtime blur every frame (dynamic background)")
                });
            if (nextModeIndex != modeIndex)
            {
                _useRuntimeBlur.boolValue = nextModeIndex != 0;
                _updateEveryFrame.boolValue = nextModeIndex == 2;
            }

            if (nextModeIndex != 0 && !Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    L("运行时模糊在 Play Mode 生效；编辑模式显示导入时烘焙的贴图。",
                        "Runtime blur takes effect in Play Mode; Edit Mode shows the baked texture."),
                    MessageType.Info);
            }

            EditorGUILayout.PropertyField(
                _radius,
                new GUIContent(L("模糊半径", "Radius")));
            EditorGUILayout.PropertyField(
                _downsample,
                new GUIContent(L("降采样", "Downsample")));
            EditorGUILayout.PropertyField(
                _iterations,
                new GUIContent(L("迭代次数", "Iterations")));
            EditorGUILayout.PropertyField(
                _blurShader,
                new GUIContent(L("ComputeShader", "Compute Shader")));

            EditorGUILayout.Space();
            _showTracking = EditorGUILayout.Foldout(
                _showTracking,
                L("源坐标跟随", "Source Tracking"),
                true);
            if (_showTracking)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    _trackSourceUv,
                    new GUIContent(L("跟随移动", "Track Source UV")));
                EditorGUILayout.PropertyField(
                    _sourceRoot,
                    new GUIContent(L("源坐标根节点", "Source Root")));
                EditorGUILayout.PropertyField(
                    _sourceRootDesignBounds,
                    new GUIContent(L("源根设计边界", "Source Root Design Bounds")));
                EditorGUILayout.PropertyField(
                    _sourceExportBounds,
                    new GUIContent(L("背景导出边界", "Source Export Bounds")));
                EditorGUILayout.PropertyField(
                    _trackedRect,
                    new GUIContent(L("跟随的弹窗 Rect", "Tracked Rect")));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            _showDebug = EditorGUILayout.Foldout(
                _showDebug,
                L("调试", "Debug"),
                true);
            if (_showDebug)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    _debugShowCamera,
                    new GUIContent(L("直接显示相机画面", "Show Camera Directly")));
                EditorGUILayout.PropertyField(
                    _debugLogCamera,
                    new GUIContent(L("每秒打印相机 RT 颜色", "Log Camera RT Every Second")));
                EditorGUILayout.PropertyField(
                    _debugSkipBlur,
                    new GUIContent(L("跳过模糊，直接显示源纹理", "Skip Blur, Show Source")));
                if (GUILayout.Button(L("打印当前相机 RT 颜色", "Log Camera RT Now")))
                {
                    serializedObject.ApplyModifiedProperties();
                    foreach (Object target in targets)
                    {
                        FigmaBackgroundBlur blur = target as FigmaBackgroundBlur;
                        if (blur != null)
                        {
                            blur.LogCameraDebug();
                        }
                    }
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            serializedObject.ApplyModifiedProperties();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("刷新模糊 / UV", "Refresh Blur / UV")))
                {
                    foreach (Object target in targets)
                    {
                        FigmaBackgroundBlur blur = target as FigmaBackgroundBlur;
                        if (blur == null)
                        {
                            continue;
                        }

                        Undo.RecordObject(blur, L("刷新 Figma 模糊", "Refresh Figma Blur"));
                        blur.Refresh();
                        EditorUtility.SetDirty(blur);
                    }

                    SceneView.RepaintAll();
                }

                if (GUILayout.Button(L("使用烘焙贴图", "Use Baked")))
                {
                    _useRuntimeBlur.boolValue = false;
                    _updateEveryFrame.boolValue = false;
                    serializedObject.ApplyModifiedProperties();
                }

                if (GUILayout.Button(L("使用运行时模糊", "Use Runtime")))
                {
                    _useRuntimeBlur.boolValue = true;
                    _updateEveryFrame.boolValue = false;
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }
    }
}
