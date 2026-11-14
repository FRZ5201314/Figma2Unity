using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace FigmaUiImporter
{
    /// <summary>
    /// 对 Figma background blur 节点背后的背景做模糊。
    /// 默认使用静态背景纹理（导入时生成的根背景/backdrop），也支持相机 RenderTexture。
    /// 模糊通过 ComputeShader 完成，不依赖 Built-in/URP 的特定渲染管线。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class FigmaBackgroundBlur : MonoBehaviour
    {
        public enum SourceMode
        {
            StaticTexture,
            Camera
        }

        [Header("Target")]
        [Tooltip("显示模糊结果的 RawImage；为空时自动查找自身或子级。")]
        public RawImage targetImage;

        [Header("Source")]
        public SourceMode sourceMode = SourceMode.StaticTexture;
        [Tooltip("静态纹理模式下的背景源，通常是导入的根背景 Texture。")]
        public Texture sourceTexture;
        [Tooltip("相机模式下的背景相机；需自行保证相机渲染的是卡片背后的内容。")]
        public Camera sourceCamera;
        [Tooltip("静态纹理模式下，source 覆盖的设计区域尺寸，用于把 Figma 模糊半径换算成像素。")]
        public Vector2 sourceDesignSize = new Vector2(1920f, 1080f);

        [Header("Source Tracking")]
        [Tooltip("模糊源坐标空间的根 RectTransform（通常是导入的 Layers 根）。")]
        public RectTransform sourceRoot;
        [Tooltip("sourceRoot 对应的 Figma 根节点设计边界。")]
        public Rect sourceRootDesignBounds;
        [Tooltip("sourceTexture 覆盖的 Figma 设计边界（根背景层的 exportBounds）。")]
        public Rect sourceExportBounds;
        [Tooltip("需要跟随的逻辑卡片/弹窗 RectTransform；移动时 UV 会重新计算。")]
        public RectTransform trackedRect;
        public bool trackSourceUv = true;

        [Header("Debug")]
        [Tooltip("直接显示相机 RenderTexture，不做模糊，用于确认相机是否真的在更新。")]
        public bool debugShowCamera = false;
        [Tooltip("每隔约 1 秒打印相机/结果 RT 的平均颜色。")]
        public bool debugLogCamera = false;
        [Tooltip("跳过 ComputeShader 模糊，直接显示源纹理，用于确认 Blit 是否正常。")]
        public bool debugSkipBlur = false;

        [Header("Blur")]
        [Min(0f)]
        public float radius = 40f;
        [Range(1, 4)]
        public int downsample = 2;
        [Range(1, 4)]
        public int iterations = 3;
        [Tooltip("关闭时只使用导入时烘焙的模糊贴图；开启时用运行时 ComputeShader 覆盖它。")]
        public bool useRuntimeBlur = true;
        [Tooltip("背景会变化时开启；静态背景只需在 OnEnable 时模糊一次。")]
        public bool updateEveryFrame = false;
        public ComputeShader blurShader;

        private RenderTexture _sourceRT;
        private RenderTexture _cameraSourceRT;
        private RenderTexture _pingRT;
        private RenderTexture _pongRT;
        private RenderTexture _displayRT;
        private Texture _originalTexture;
        private Camera _captureCamera;
        private Camera _captureSourceCamera;
        private int _cameraWarmupFrames;
        private bool _warnedNoCompute;
        private bool _warnedNoCamera;
        private bool _dirty = true;

        private void Awake()
        {
            if (targetImage == null)
            {
                targetImage = GetComponent<RawImage>();
                if (targetImage == null)
                {
                    targetImage = GetComponentInChildren<RawImage>(true);
                }
            }

            if (targetImage != null)
            {
                _originalTexture = targetImage.texture;
            }

            if (sourceCamera == null)
            {
                // 动态模糊默认使用场景的 Main Camera，避免逐节点手动赋值。
                sourceCamera = Camera.main;
            }

            if (blurShader == null)
            {
                blurShader = Resources.Load<ComputeShader>("FigmaBackgroundBlur");
            }
        }

        private void OnEnable()
        {
            _dirty = true;
            UpdateTrackedUv();
            if (useRuntimeBlur)
            {
                RenderBlur();
            }
        }

        private void Update()
        {
            // UV 跟随是轻量操作，即使使用烘焙贴图也要更新。
            UpdateTrackedUv();

            if (!useRuntimeBlur)
            {
                return;
            }

            // 相机模式首次创建捕获相机后，多刷几帧避免把首帧黑图烘进去。
            if (sourceMode == SourceMode.Camera && _cameraWarmupFrames > 0)
            {
                _dirty = true;
                _cameraWarmupFrames--;
            }

            if (updateEveryFrame || _dirty)
            {
                _dirty = false;
                RenderBlur();
            }
        }

        private void OnDisable()
        {
            RestoreTexture();
            ReleaseTextures();
        }

        private void OnDestroy()
        {
            ReleaseTextures();
        }

        /// <summary>手动触发一次运行时模糊，适合背景变化时调用。</summary>
        public void Refresh()
        {
            _dirty = true;
            UpdateTrackedUv();
            // 编辑模式只刷新 UV；运行时模糊需要 Play Mode 的 RenderTexture/ComputeShader。
            if (Application.isPlaying && isActiveAndEnabled && useRuntimeBlur)
            {
                RenderBlur();
            }
        }

        /// <summary>
        /// 根据 trackedRect 当前在 sourceRoot 中的位置，重新计算 RawImage 的 uvRect。
        /// 用于弹窗移动/锚点变化时让模糊内容跟随背景，而不是跟着卡片一起移动。
        /// </summary>
        public void UpdateTrackedUv()
        {
            if (!trackSourceUv || targetImage == null || trackedRect == null)
            {
                return;
            }

            if (sourceMode == SourceMode.Camera)
            {
                UpdateCameraUv();
                return;
            }

            if (sourceRoot == null
                || sourceExportBounds.width <= 0.01f
                || sourceExportBounds.height <= 0.01f)
            {
                return;
            }

            // 只取 trackedRect 自身的 Rect，不能把子节点（卡片阴影/内容）算进来，
            // 否则 uvRect 会比遮罩大，模糊内容出现缩放/偏移。
            Vector3[] corners = new Vector3[4];
            trackedRect.GetWorldCorners(corners);
            Vector3 localMin = sourceRoot.InverseTransformPoint(corners[0]);
            Vector3 localMax = sourceRoot.InverseTransformPoint(corners[2]);
            float localMinX = Mathf.Min(localMin.x, localMax.x);
            float localMaxX = Mathf.Max(localMin.x, localMax.x);
            float localMinY = Mathf.Min(localMin.y, localMax.y);
            float localMaxY = Mathf.Max(localMin.y, localMax.y);

            Rect rootRect = sourceRoot.rect;
            float designX = sourceRootDesignBounds.x + (localMinX - rootRect.xMin);
            float designY = sourceRootDesignBounds.y + (rootRect.yMax - localMaxY);
            float designWidth = localMaxX - localMinX;
            float designHeight = localMaxY - localMinY;
            if (designWidth <= 0.01f || designHeight <= 0.01f)
            {
                return;
            }

            float u = (designX - sourceExportBounds.x) / sourceExportBounds.width;
            float v = 1f - ((designY - sourceExportBounds.y) + designHeight) / sourceExportBounds.height;
            float w = designWidth / sourceExportBounds.width;
            float h = designHeight / sourceExportBounds.height;

            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
            w = Mathf.Clamp(w, 0f, 1f - u);
            h = Mathf.Clamp(h, 0f, 1f - v);
            targetImage.uvRect = new Rect(u, v, w, h);
        }

        private void UpdateCameraUv()
        {
            if (sourceCamera == null || targetImage == null || trackedRect == null)
            {
                return;
            }

            Canvas canvas = targetImage.canvas;
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            Vector3[] corners = new Vector3[4];
            trackedRect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);

            float width = sourceCamera.pixelWidth > 0 ? sourceCamera.pixelWidth : Screen.width;
            float height = sourceCamera.pixelHeight > 0 ? sourceCamera.pixelHeight : Screen.height;
            if (width <= 0.01f || height <= 0.01f)
            {
                return;
            }

            float xMin = Mathf.Min(min.x, max.x);
            float yMin = Mathf.Min(min.y, max.y);
            float xMax = Mathf.Max(min.x, max.x);
            float yMax = Mathf.Max(min.y, max.y);
            targetImage.uvRect = new Rect(
                Mathf.Clamp01(xMin / width),
                Mathf.Clamp01(yMin / height),
                Mathf.Clamp01((xMax - xMin) / width),
                Mathf.Clamp01((yMax - yMin) / height));
        }

        private void RenderBlur()
        {
            if (!useRuntimeBlur)
            {
                return;
            }

            if (blurShader == null || !SystemInfo.supportsComputeShaders)
            {
                if (!_warnedNoCompute)
                {
                    _warnedNoCompute = true;
                    Debug.LogWarning(
                        "FigmaBackgroundBlur: ComputeShader 不可用，保留导入时烘焙的模糊贴图。",
                        this);
                }

                return;
            }

            if (sourceMode == SourceMode.Camera && sourceCamera == null)
            {
                if (!_warnedNoCamera)
                {
                    _warnedNoCamera = true;
                    Debug.LogWarning(
                        "FigmaBackgroundBlur: Source Mode 为 Camera，但没有指定 Source Camera，保留原纹理。",
                        this);
                }

                return;
            }

            Texture source = sourceMode == SourceMode.Camera
                ? EnsureCameraSource()
                : sourceTexture != null ? sourceTexture : _originalTexture;

            if (source == null || targetImage == null)
            {
                return;
            }

            if (sourceMode == SourceMode.Camera && debugShowCamera)
            {
                // 调试：直接显示相机 RT，确认相机内容是否随移动变化。
                int debugWidth = Mathf.Max(1, source.width / Mathf.Max(1, downsample));
                int debugHeight = Mathf.Max(1, source.height / Mathf.Max(1, downsample));
                EnsureTextures(debugWidth, debugHeight);
                Graphics.Blit(source, _displayRT);
                targetImage.texture = _displayRT;
                targetImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                return;
            }

            int sourceWidth = Mathf.Max(1, source.width);
            int sourceHeight = Mathf.Max(1, source.height);
            int width = Mathf.Max(1, sourceWidth / Mathf.Max(1, downsample));
            int height = Mathf.Max(1, sourceHeight / Mathf.Max(1, downsample));

            EnsureTextures(width, height);
            if (_sourceRT == null || _pingRT == null || _pongRT == null)
            {
                return;
            }

            Graphics.Blit(source, _sourceRT);
            if (debugLogCamera && Time.frameCount % 60 == 0)
            {
                LogTextureAverage("SourceRT", _sourceRT);
            }

            if (debugSkipBlur)
            {
                Graphics.Blit(_sourceRT, _displayRT);
                targetImage.texture = _displayRT;
                return;
            }

            float radiusPixels = GetRadiusPixels(sourceWidth, width);
            int horizontal = blurShader.FindKernel("BlurHorizontal");
            int vertical = blurShader.FindKernel("BlurVertical");
            blurShader.SetInt("_Width", width);
            blurShader.SetInt("_Height", height);
            blurShader.SetFloat("_Radius", radiusPixels);

            RenderTexture input = _sourceRT;
            RenderTexture output = _pingRT;
            RenderTexture swap;

            for (int i = 0; i < Mathf.Max(1, iterations); i++)
            {
                blurShader.SetTexture(horizontal, "_MainTex", input);
                blurShader.SetTexture(horizontal, "_Result", output);
                blurShader.Dispatch(horizontal, (width + 7) / 8, (height + 7) / 8, 1);

                swap = input;
                input = output;
                output = swap;

                blurShader.SetTexture(vertical, "_MainTex", input);
                blurShader.SetTexture(vertical, "_Result", output);
                blurShader.Dispatch(vertical, (width + 7) / 8, (height + 7) / 8, 1);

                swap = input;
                input = output;
                output = swap;
            }

            Graphics.Blit(input, _displayRT);
            targetImage.texture = _displayRT;

            if (debugLogCamera && Time.frameCount % 60 == 0)
            {
                LogTextureAverage("CameraSource", _cameraSourceRT);
                LogTextureAverage("BlurResult", _displayRT);
            }
        }

        public void LogCameraDebug()
        {
            if (_cameraSourceRT == null)
            {
                Debug.Log("FigmaBackgroundBlur debug: 没有相机 RT。请在 Play Mode、Camera 模式且指定 Source Camera 后调用。", this);
                return;
            }

            LogTextureAverage("CameraSource", _cameraSourceRT);
            if (targetImage != null)
            {
                RenderTexture targetRT = targetImage.texture as RenderTexture;
                if (targetRT != null)
                {
                    LogTextureAverage("TargetRawImage", targetRT);
                }
            }
        }

        private static void LogTextureAverage(string label, RenderTexture texture)
        {
            if (texture == null)
            {
                Debug.Log("FigmaBackgroundBlur debug " + label + ": null");
                return;
            }

            RenderTexture previous = RenderTexture.active;
            Texture2D readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = texture;
                readback.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
                readback.Apply();
                Color32[] pixels = readback.GetPixels32();
                int step = Mathf.Max(1, pixels.Length / 4096);
                long r = 0;
                long g = 0;
                long b = 0;
                long a = 0;
                int count = 0;
                for (int i = 0; i < pixels.Length; i += step)
                {
                    r += pixels[i].r;
                    g += pixels[i].g;
                    b += pixels[i].b;
                    a += pixels[i].a;
                    count++;
                }

                Debug.Log("FigmaBackgroundBlur debug " + label + ": "
                    + texture.width + "x" + texture.height
                    + " avg=(" + (r / Mathf.Max(1, count))
                    + "," + (g / Mathf.Max(1, count))
                    + "," + (b / Mathf.Max(1, count))
                    + "," + (a / Mathf.Max(1, count)) + ")");
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(readback);
            }
        }

        private Texture EnsureCameraSource()
        {
            if (sourceCamera == null)
            {
                return null;
            }

            int width = Mathf.Max(1, sourceCamera.pixelWidth > 0 ? sourceCamera.pixelWidth : Screen.width);
            int height = Mathf.Max(1, sourceCamera.pixelHeight > 0 ? sourceCamera.pixelHeight : Screen.height);

            if (_cameraSourceRT == null
                || _cameraSourceRT.width != width
                || _cameraSourceRT.height != height
                || _captureSourceCamera != sourceCamera)
            {
                DestroyCaptureCamera();
                ReleaseTexture(ref _cameraSourceRT);
                _cameraSourceRT = CreateTexture("FigmaBlurCameraSource", width, height);
                CreateCaptureCamera();
                _cameraWarmupFrames = 2;
            }

            SyncCaptureCamera();
            return _cameraSourceRT;
        }

        private void CreateCaptureCamera()
        {
            GameObject cameraObject = new GameObject("FigmaBlurCaptureCamera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.transform.SetParent(transform, false);

            Camera capture = cameraObject.AddComponent<Camera>();
            capture.targetTexture = _cameraSourceRT;
            capture.enabled = true;
            capture.allowHDR = false;
            capture.allowMSAA = false;
            EnsureUniversalCameraData(capture);

            _captureCamera = capture;
            _captureSourceCamera = sourceCamera;
            SyncCaptureCamera();
        }

        private void SyncCaptureCamera()
        {
            if (_captureCamera == null || sourceCamera == null)
            {
                return;
            }

            Transform source = sourceCamera.transform;
            _captureCamera.transform.SetPositionAndRotation(source.position, source.rotation);
            _captureCamera.clearFlags = sourceCamera.clearFlags;
            _captureCamera.backgroundColor = sourceCamera.backgroundColor;
            _captureCamera.cullingMask = sourceCamera.cullingMask;
            _captureCamera.orthographic = sourceCamera.orthographic;
            _captureCamera.orthographicSize = sourceCamera.orthographicSize;
            _captureCamera.fieldOfView = sourceCamera.fieldOfView;
            _captureCamera.nearClipPlane = sourceCamera.nearClipPlane;
            _captureCamera.farClipPlane = sourceCamera.farClipPlane;
            _captureCamera.aspect = sourceCamera.aspect;
            _captureCamera.rect = sourceCamera.rect;
            _captureCamera.depth = sourceCamera.depth;
            _captureCamera.useOcclusionCulling = sourceCamera.useOcclusionCulling;
            _captureCamera.allowHDR = false;
            _captureCamera.allowMSAA = false;
        }

        private static void EnsureUniversalCameraData(Camera camera)
        {
            // 运行时创建的相机在 URP 下需要 UniversalAdditionalCameraData；
            // 通过反射添加，避免 Runtime 程序集硬依赖 URP。
            Type cameraDataType = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                cameraDataType = assemblies[i].GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
                if (cameraDataType != null)
                {
                    break;
                }
            }

            if (cameraDataType == null)
            {
                return;
            }

            if (camera.GetComponent(cameraDataType) == null)
            {
                camera.gameObject.AddComponent(cameraDataType);
            }

            try
            {
                PropertyInfo renderTypeProperty = cameraDataType.GetProperty("renderType");
                if (renderTypeProperty != null && renderTypeProperty.CanWrite)
                {
                    renderTypeProperty.SetValue(
                        camera.GetComponent(cameraDataType),
                        Enum.Parse(renderTypeProperty.PropertyType, "Base"));
                }

                PropertyInfo postProperty = cameraDataType.GetProperty("renderPostProcessing");
                if (postProperty != null && postProperty.CanWrite)
                {
                    postProperty.SetValue(camera.GetComponent(cameraDataType), false);
                }
            }
            catch
            {
                // 不同 URP 版本属性可能不同，保持默认即可。
            }
        }

        private void DestroyCaptureCamera()
        {
            if (_captureCamera == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_captureCamera.gameObject);
            }
            else
            {
                DestroyImmediate(_captureCamera.gameObject);
            }

            _captureCamera = null;
            _captureSourceCamera = null;
        }

        private float GetRadiusPixels(int sourceWidth, int blurredWidth)
        {
            if (sourceMode == SourceMode.Camera)
            {
                // 相机模式下没有设计单位映射，直接把 radius 当作相机像素半径。
                return Mathf.Max(1f, radius / Mathf.Max(1, downsample));
            }

            float designWidth = sourceDesignSize.x > 0.01f ? sourceDesignSize.x : sourceWidth;
            float pixelsPerUnit = sourceWidth / designWidth;
            float radiusInSourcePixels = radius * pixelsPerUnit;
            float downscaled = radiusInSourcePixels / Mathf.Max(1, downsample);
            return Mathf.Max(1f, downscaled);
        }

        private void EnsureTextures(int width, int height)
        {
            if (_sourceRT == null || _sourceRT.width != width || _sourceRT.height != height)
            {
                // 注意：不能在这里释放 _cameraSourceRT，相机模式刚渲染出的结果还要作为 source。
                ReleaseTexture(ref _sourceRT);
                ReleaseTexture(ref _pingRT);
                ReleaseTexture(ref _pongRT);
                ReleaseTexture(ref _displayRT);
                _sourceRT = CreateTexture("FigmaBlurSource", width, height);
                _pingRT = CreateTexture("FigmaBlurPing", width, height);
                _pongRT = CreateTexture("FigmaBlurPong", width, height);
                _displayRT = CreateDisplayTexture("FigmaBlurDisplay", width, height);
            }

            if (_displayRT == null)
            {
                _displayRT = CreateDisplayTexture("FigmaBlurDisplay", width, height);
            }
        }

        private static RenderTexture CreateTexture(string name, int width, int height)
        {
            // Linear 色彩空间下，ComputeShader 的 UAV 写入 sRGB RT 可能失效/变黑；
            // 计算链使用 Linear RT，最后再 Blit 到 sRGB 显示 RT。
            RenderTexture texture = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = name,
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = true
            };
            texture.Create();
            return texture;
        }

        private static RenderTexture CreateDisplayTexture(string name, int width, int height)
        {
            RenderTexture texture = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = name,
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = false
            };
            texture.Create();
            return texture;
        }

        private void RestoreTexture()
        {
            if (targetImage != null && _originalTexture != null)
            {
                targetImage.texture = _originalTexture;
            }
        }

        private void ReleaseTextures()
        {
            DestroyCaptureCamera();
            ReleaseTexture(ref _sourceRT);
            ReleaseTexture(ref _cameraSourceRT);
            ReleaseTexture(ref _pingRT);
            ReleaseTexture(ref _pongRT);
            ReleaseTexture(ref _displayRT);
        }

        private static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }

            texture = null;
        }
    }
}
