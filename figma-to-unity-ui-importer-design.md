# Figma2Unity 插件开发设计文档

## 1. 目标

开发一个 Unity 编辑器插件，用于从 Figma 设计稿中选择指定页面或界面 Frame，将其导入到 Unity 场景的 Canvas 中。

插件不依赖 Figma 节点命名规范，不尝试把设计稿语义化还原为按钮、文本、列表等 Unity 组件。MVP 目标是：

- 加载指定 Figma 文件。
- 展示可导入的 Page / Frame 列表。
- 用户选择目标界面后导入。
- 导入一张完整设计参考图，并放入 Canvas。
- 参考图挂载 CanvasGroup，alpha 设置为 0.5。
- 导入该界面的切图资源。
- 按 Figma 设计布局在 Unity Canvas 中实例化切图。
- unity编辑器插件界面使用中文

## 2. 复杂度评估

整体复杂度为中等。

MVP 不算很复杂，因为它可以把问题限定为“图片导入 + 坐标还原 + 编辑器工具”。真正复杂的部分不是 Unity 实例化，而是 Figma 节点导出策略、坐标系转换、图片资源管理、重复导入更新、以及不同设计稿结构的兼容。

建议分三档实现：

| 阶段 | 范围 | 复杂度 | 预估 |
| --- | --- | --- | --- |
| MVP | 输入 token/file key，选择页面或 Frame，导入参考图和基础切图 | 中低 | 5-8 个工作日 |
| 可用版 | 导入缓存、重导入覆盖、进度条、错误提示、导入配置、Prefab 输出 | 中 | 2-4 周 |
| 生产版 | 增量更新、九宫格、文本转 TMP、自动锚点、组件映射、多分辨率适配 | 中高 | 4-8 周 |

## 3. 不做的事情

MVP 明确不做以下能力：

- 不根据节点名称识别 Button、Toggle、ScrollView 等业务组件。
- 不完整转换复杂 Figma Text；单一样式文本可通过智能文本模式转换为 TextMeshPro。
- 不把 Auto Layout 完整转换为 Unity LayoutGroup。
- 不自动推断锚点、适配策略和交互逻辑。
- 不保证导入结果是可直接上线的最终 UI Prefab。

MVP 的导入结果是一个高保真的视觉还原层，适合用于 Unity 中快速搭建、对位和后续人工替换。

## 4. 用户工作流

1. 用户打开 Unity 菜单：`Tools/Figma2Unity`。
2. 在编辑器窗口中填写：
   - Figma Personal Access Token。
   - Figma File Key 或文件 URL。
   - 输出资源目录，例如 `Assets/FigmaImports`。
   - 目标 Canvas，未指定时自动创建。
3. 点击 `Load File`。
4. 插件调用 Figma API 读取文件结构。
5. 插件展示 Page 列表，每个 Page 下展示顶层 Frame。
6. 用户选择一个 Page 或 Frame。
7. 点击 `Import Selected`。
8. 插件导出：
   - 选中区域完整参考图。
   - 选中区域内的切图。
   - layout manifest。
9. 插件在当前场景 Canvas 下创建导入根节点。
10. 插件创建参考图节点，设置 CanvasGroup alpha 为 0.5。
11. 插件创建切图节点，并按 Figma 中的绝对位置还原 RectTransform。

## 5. 核心设计原则

### 5.1 不依赖节点名称

节点名称只用于 Unity GameObject 的可读显示，不参与逻辑判断。

节点的身份使用 Figma node id。

### 5.2 选择优先于约定

由于设计稿命名不可控，插件应该让用户在界面中手动选择目标 Page / Frame。后续可以增加缩略图预览，帮助用户选择正确界面。

### 5.3 先视觉还原，再语义增强

第一版只处理图片和布局。语义化组件转换作为后续功能。

### 5.4 可重复导入

每次导入保存一份 manifest，用于记录 Figma node id、图片路径、Unity 对象路径和坐标。后续可以基于 manifest 做覆盖或增量更新。

## 6. Figma API 方案

### 6.1 输入

支持两种输入：

- Figma File Key，例如：`abc123...`
- Figma 文件 URL，从 URL 中解析 file key。

Token 使用 Personal Access Token，保存在 Unity EditorPrefs 中，不写入项目资源。

### 6.2 API 调用

MVP 需要三类接口：

1. 读取文件结构：
   - `GET /v1/files/{file_key}`
   - 用于获取 pages、frames、children、bounds、visible 等节点信息。

2. 读取指定节点：
   - `GET /v1/files/{file_key}/nodes?ids={node_ids}`
   - 用于按选择目标获取局部节点树。

3. 渲染节点图片：
   - `GET /v1/images/{file_key}?ids={node_ids}&format=png&scale=1`
   - 用于导出参考图和切图。

### 6.3 节点选择策略

选中目标可以是：

- Page，即 Figma 的 `CANVAS` 节点。
- Frame / Component / Instance，即一个具体 UI 界面。

推荐 MVP 优先支持 Frame / Component / Instance。Page 通常包含多个界面，直接导入整个 Page 可能边界巨大且对象过多。

### 6.4 切图导出策略

因为不能依赖节点命名，推荐提供两种模式：

#### 模式 A：Export Settings 优先

只导出 Figma 中已经设置 exportSettings 的节点。

优点：
- 最符合设计师“切图”的原始意图。
- 资源数量可控。
- 结果更接近真实项目资产。

缺点：
- 如果设计师没有设置导出项，导入内容会很少。

#### 模式 B：可见图层自动切片

遍历选中 Frame 的可见后代节点，筛选可渲染节点并逐个导出。

初版可导出的节点类型：

- RECTANGLE
- ELLIPSE
- POLYGON
- STAR
- VECTOR
- BOOLEAN_OPERATION
- LINE
- TEXT
- FRAME
- GROUP
- COMPONENT
- INSTANCE

建议默认跳过：

- 不可见节点。
- 尺寸为 0 的节点。
- 位于选中区域外的节点。
- 仅作为容器且自身无填充、描边、效果、图片、文本的 Frame/Group。

优点：
- 不要求设计师做任何设置。
- 能满足“把设计稿视觉导入 Unity”的目标。

缺点：
- 容易产生大量图片。
- 部分父子节点可能重复渲染。
- Text 会被导成图片，不可编辑。

#### MVP 推荐

默认采用“Export Settings 优先，如果没有 exportSettings，则自动切片”的策略。

同时在窗口里提供选项：

- `Export marked nodes only`
- `Auto slice visible layers`
- `Flatten selected frame as single image`

## 7. Unity 场景结构

导入后建议生成如下结构：

```text
Canvas
└── FigmaImport_[PageOrFrameName]_[NodeId]
    ├── Reference
    │   └── DesignReference
    └── Layers
        ├── Layer_[NodeName]_[NodeId]
        ├── Layer_[NodeName]_[NodeId]
        └── ...
```

### 7.1 根节点

根节点使用 RectTransform，尺寸等于选中 Figma Frame 的尺寸。

建议 pivot 设置为 `(0.5, 0.5)`。

### 7.2 参考图节点

组件：

- `RectTransform`
- `UnityEngine.UI.Image`
- `CanvasGroup`

设置：

- 图片为完整参考图。
- 尺寸等于选中区域尺寸。
- anchoredPosition 为 `(0, 0)`。
- CanvasGroup alpha 为 `0.5`。
- blocksRaycasts 为 `false`。
- interactable 为 `false`。

### 7.3 切图节点

每个切图节点包含：

- `RectTransform`
- `UnityEngine.UI.Image`

设置：

- sprite 为导入后的 PNG。
- preserveAspect 可按需启用。
- raycastTarget 默认关闭。
- sibling order 按 Figma 节点绘制顺序排列。

## 8. 坐标转换

Figma 坐标系：

- 原点在画布左上角。
- x 向右。
- y 向下。
- 节点通常提供 absoluteBoundingBox。

Unity UI RectTransform：

- 推荐根节点 pivot 为中心。
- anchoredPosition 以父级中心为参考。
- x 向右。
- y 向上。

设：

- 选中根节点 bounds：`rootX, rootY, rootW, rootH`
- 子节点 bounds：`nodeX, nodeY, nodeW, nodeH`

转换公式：

```text
localLeft = nodeX - rootX
localTop = nodeY - rootY

unityX = localLeft + nodeW / 2 - rootW / 2
unityY = rootH / 2 - localTop - nodeH / 2
```

RectTransform：

```text
sizeDelta = (nodeW, nodeH)
anchoredPosition = (unityX, unityY)
anchorMin = anchorMax = (0.5, 0.5)
pivot = (0.5, 0.5)
```

参考图：

```text
sizeDelta = (rootW, rootH)
anchoredPosition = (0, 0)
```

## 9. 资源落地方案

默认资源目录：

```text
Assets/FigmaImports/{fileKey}/{nodeId}/
```

目录结构：

```text
Assets/FigmaImports/
└── {fileKey}/
    └── {selectedNodeId}/
        ├── reference.png
        ├── manifest.json
        └── layers/
            ├── {nodeId}.png
            ├── {nodeId}.png
            └── ...
```

### 9.1 图片导入设置

下载 PNG 后通过 AssetDatabase 导入，并设置 TextureImporter：

- textureType = Sprite
- spriteImportMode = Single
- alphaIsTransparency = true
- mipmapEnabled = false
- sRGBTexture = true
- compression 可配置，MVP 默认 None 或 Normal Quality。

### 9.2 文件名安全

Unity 资源文件名使用 node id 转义，不直接使用 Figma 节点名。

节点名只存入 manifest 或用于 GameObject 展示名。

## 10. Manifest 设计

`manifest.json` 用于支持重复导入、调试和后续增量更新。

示例：

```json
{
  "fileKey": "abc123",
  "selectedNodeId": "1:2",
  "selectedNodeName": "Home",
  "importedAt": "2026-06-30T00:00:00Z",
  "rootBounds": {
    "x": 0,
    "y": 0,
    "width": 1080,
    "height": 1920
  },
  "referenceImage": "reference.png",
  "layers": [
    {
      "nodeId": "3:4",
      "nodeName": "Logo",
      "type": "VECTOR",
      "image": "layers/3_4.png",
      "bounds": {
        "x": 100,
        "y": 80,
        "width": 200,
        "height": 80
      },
      "unity": {
        "anchoredPosition": {
          "x": -340,
          "y": 800
        },
        "sizeDelta": {
          "x": 200,
          "y": 80
        }
      }
    }
  ]
}
```

## 11. 插件模块划分

### 11.1 Editor Window

类名建议：

```text
FigmaUiImporterWindow
```

职责：

- 展示 token/file key 输入。
- 展示导入配置。
- 加载 Figma 文件。
- 展示 Page / Frame 树。
- 触发导入。
- 展示进度、错误和日志。

### 11.2 Figma API Client

类名建议：

```text
FigmaApiClient
```

职责：

- 构造 HTTP 请求。
- 添加 token header。
- 调用 files、nodes、images 接口。
- 处理限流、失败、重试和 JSON 解析。

### 11.3 Figma Document Parser

类名建议：

```text
FigmaDocumentParser
```

职责：

- 从 Figma document 中提取 Page / Frame。
- 遍历节点树。
- 筛选可导入节点。
- 保留绘制顺序。

### 11.4 Image Exporter

类名建议：

```text
FigmaImageExporter
```

职责：

- 按 node ids 请求图片 URL。
- 下载 PNG。
- 写入 Unity Assets。
- 批量导出时分批请求，避免 URL 太长。

### 11.5 Asset Importer

类名建议：

```text
UnitySpriteAssetImporter
```

职责：

- 调用 AssetDatabase.Refresh / ImportAsset。
- 配置 TextureImporter。
- 加载 Sprite。

### 11.6 Scene Builder

类名建议：

```text
FigmaSceneBuilder
```

职责：

- 创建 Canvas 或使用现有 Canvas。
- 创建导入根节点。
- 创建 Reference 节点。
- 创建 Layers 节点。
- 根据坐标转换创建 Image 对象。
- 设置 CanvasGroup。

### 11.7 Manifest Service

类名建议：

```text
FigmaImportManifestService
```

职责：

- 生成 manifest。
- 保存 manifest。
- 后续支持读取旧 manifest 并重导入。

## 12. 关键数据结构

### 12.1 FigmaImportSettings

```csharp
public class FigmaImportSettings
{
    public string FileKey;
    public string AccessToken;
    public string OutputFolder;
    public Canvas TargetCanvas;
    public FigmaSliceMode SliceMode;
    public float ImageScale = 1f;
    public bool CreateReference = true;
    public float ReferenceAlpha = 0.5f;
    public bool DisableRaycastTarget = true;
}
```

### 12.2 FigmaNodeInfo

```csharp
public class FigmaNodeInfo
{
    public string Id;
    public string Name;
    public string Type;
    public bool Visible;
    public Rect AbsoluteBounds;
    public List<FigmaNodeInfo> Children;
    public bool HasExportSettings;
}
```

### 12.3 ImportedLayerInfo

```csharp
public class ImportedLayerInfo
{
    public string NodeId;
    public string NodeName;
    public string NodeType;
    public string AssetPath;
    public Rect FigmaBounds;
    public Vector2 AnchoredPosition;
    public Vector2 SizeDelta;
    public int SiblingIndex;
}
```

## 13. 开发步骤

### 阶段 1：插件骨架

- 创建 Editor 文件夹。
- 创建 `FigmaUiImporterWindow`。
- 添加 Unity 菜单入口。
- 实现 token、file key、output folder、target canvas 的基础 UI。
- 使用 EditorPrefs 保存 token 和上次输入。

验收标准：

- Unity 菜单能打开插件窗口。
- 输入项能保存和恢复。

### 阶段 2：Figma 文件加载

- 实现 `FigmaApiClient`。
- 支持 `GET /v1/files/{file_key}`。
- 解析返回 JSON。
- 提取 Page 和顶层 Frame。
- 在窗口中展示可选列表。

验收标准：

- 输入合法 token 和 file key 后能看到 Figma 页面/Frame 树。
- API 错误能在窗口中显示明确提示。

### 阶段 3：参考图导入

- 用户选择一个 Frame。
- 调用 images API 渲染该 Frame。
- 下载 PNG 到 `Assets/FigmaImports/.../reference.png`。
- 设置为 Sprite。
- 在 Canvas 下创建参考图 Image。
- 添加 CanvasGroup，alpha = 0.5。

验收标准：

- 场景中出现一张与 Figma Frame 一致的半透明参考图。

### 阶段 4：切图导入

- 遍历选中节点子树。
- 根据切图策略筛选节点。
- 批量调用 images API。
- 下载所有 PNG。
- 配置 Sprite 导入设置。

验收标准：

- 资源目录下生成每个节点对应的 PNG/Sprite。
- 能处理无 exportSettings 的普通设计稿。

### 阶段 5：布局实例化

- 实现 Figma 到 Unity UI 的坐标转换。
- 在 `Layers` 根节点下创建 Image。
- 设置 RectTransform。
- 按 Figma 绘制顺序设置 sibling order。

验收标准：

- 切图基本覆盖参考图。
- 位置、大小、层级顺序与设计稿一致。

### 阶段 6：Manifest 与重导入基础

- 保存 manifest。
- 导入前检测同目录旧 manifest。
- 提供覆盖导入选项。
- 覆盖导入时删除旧导入根节点或创建新版本。

验收标准：

- 每次导入都有可追踪记录。
- 不会无意覆盖用户手工修改，除非用户明确选择覆盖。

### 阶段 7：体验优化

- 增加进度条。
- 增加导入日志。
- 增加取消导入。
- 增加图片 scale 配置。
- 增加导入模式选择。
- 增加异常恢复和失败重试。

验收标准：

- 大型设计稿导入时用户能看到当前进度。
- 单个图片下载失败不会导致整个编辑器无响应。

## 14. 技术风险与处理

### 14.1 Figma 节点重叠或重复导出

风险：

父节点和子节点都被导出时，视觉会重复叠加。

处理：

- MVP 提供三种切图模式。
- 自动切片时优先导出叶子节点。
- 对有视觉属性的容器节点可以单独导出，但跳过其子节点，避免重复。

### 14.2 Page 尺寸过大

风险：

Page 可能包含多个界面，absolute bounds 非常大。

处理：

- 默认推荐选择 Frame。
- Page 导入需要提示用户确认。
- 后续支持 Page 内选择具体 Frame。

### 14.3 Figma 图片 URL 有时效

风险：

images API 返回的图片 URL 不是长期资源。

处理：

- 请求后立即下载到 Unity 项目。
- 不在 manifest 中依赖远程图片 URL。

### 14.4 坐标和缩放误差

风险：

Figma 像素尺寸与 Unity Canvas Scaler 设置不同，可能导致显示尺寸不符合预期。

处理：

- 导入根节点使用 Figma 原始像素尺寸。
- Canvas Scaler 由项目自己控制。
- 插件只保证导入根节点内部的相对布局准确。

### 14.5 网络和认证失败

风险：

Token 无效、网络失败、Figma API 限流。

处理：

- 所有请求有明确错误提示。
- 对 429 和 5xx 做有限重试。
- Token 存 EditorPrefs，不提交到版本库。

## 15. 后续增强方向

### 15.1 文本转 TextMeshPro

已支持读取 Figma Text 节点的 characters、style、fills，将单一样式文本转换为 TMP_Text，并为复杂文本保留 PNG fallback。Unity 侧可通过字体映射表把 Figma 字体匹配到 TMP_FontAsset。

后续难点：

- 行高、字间距、段落样式。
- 多样式文本片段。

### 15.2 九宫格 Sprite

根据 Figma slice 或自定义配置生成 sliced sprite。

难点：

- Figma 本身不一定有 Unity 需要的 border 信息。
- 可能需要用户手动配置。

### 15.3 组件映射

让用户建立 Figma node id / component key 到 Unity Prefab 的映射。

用途：

- Figma Button Instance 转 Unity Button Prefab。
- Icon 组件复用项目内 Sprite。

### 15.4 增量更新

基于 manifest 对比 node id 和 bounds，只更新变化的资源。

难点：

- 用户可能已经手工修改 Unity 对象。
- 需要明确保护用户改动的策略。

### 15.5 Prefab 输出

导入后自动保存为 Prefab，而不是只存在于当前 Scene。

推荐作为可用版功能加入。

## 16. 建议的 MVP 验收清单

- 可以打开 Unity 编辑器窗口。
- 可以输入并保存 Figma token。
- 可以从 Figma URL 解析 file key。
- 可以加载 Figma 文件并展示 Page / Frame。
- 可以选择一个 Frame 导入。
- 可以生成完整参考图。
- 参考图在 Canvas 下实例化。
- 参考图带 CanvasGroup，alpha = 0.5。
- 可以导出切图。
- 切图资源导入为 Sprite。
- 切图按 Figma 布局实例化到 Canvas。
- 导入结果与参考图基本重合。
- 导入过程有进度和错误提示。
- 生成 manifest.json。

## 17. 推荐实现顺序

最推荐先实现垂直闭环：

1. EditorWindow 输入 token/file key。
2. 加载文件并选择 Frame。
3. 只导入完整参考图。
4. 再导入一个测试节点切图。
5. 实现坐标转换。
6. 扩展到批量节点导入。
7. 加 manifest 和重导入。

这样可以最快验证 Figma API、Unity 图片导入和坐标转换三件最核心的事。

## 18. 结论

这个插件可以做，而且 MVP 不需要特别复杂。关键是把第一版目标控制在“视觉导入”而不是“完整 UI 自动工程化还原”。

第一版建议交付：

- Figma 文件加载。
- Frame 选择。
- 参考图导入。
- 自动切片导入。
- Canvas 实例化。
- Manifest 记录。

后续再逐步增加文本、组件、Prefab、增量更新和适配能力。
