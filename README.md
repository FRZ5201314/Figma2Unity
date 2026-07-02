# Figma UI Importer

这是一个 Unity Editor 插件 MVP，用于把 Figma 中选定的 Page / Frame 导入到 Unity Canvas 中。

当前版本实现：

- 中文编辑器窗口：`Tools/Figma UI Importer`
- 支持输入 Figma Personal Access Token
- 支持输入 Figma File Key 或 Figma 文件 URL
- 加载 Figma 文件并展示 Page / Frame
- 导入完整参考图，自动添加 `CanvasGroup`，默认透明度 `0.5`
- 导入切图资源并设置为 Sprite
- 按 Figma `absoluteBoundingBox` 还原 RectTransform 坐标
- 使用导出 PNG 实际尺寸和参考图局部匹配校准切图位置
- 生成 `manifest.json`
- 生成 `diagnostics.json` 导入诊断报告
- 支持从已有 `manifest.json` 离线重建场景
- 支持导入 Figma 本地插件导出的 `.figma2unity.json` 离线包
- 支持 Figma Text 按图片、TextMeshPro 或智能模式导入
- 支持项目级全局 TMP 字体映射表，并可从已导入 manifest 自动补全 Figma 字体条目
- 支持覆盖同名场景根节点
- 支持把导入根节点保存为 Prefab

## 安装

这是一个 Unity Package Manager 本地包。推荐放在 Unity 项目外部或项目内 `Packages` 目录外，然后在 Unity 中选择：

`Window / Package Manager / + / Add package from disk...`

选择本目录下的 `package.json`。

## 使用

### REST API 导入

1. 在 Unity 菜单打开 `Tools/Figma UI Importer`。窗口中“网络导入（Figma REST API）”会使用 Figma API，“本地导入（Figma 插件包）”和“本地重建（已导入项目）”只读取本地文件。
2. 填写 Figma Personal Access Token。
3. 填写 Figma File Key 或文件 URL。
4. 设置输出资源目录，默认是 `Assets/FigmaImports`。如果需要复用导入结果，可勾选 `保存为 Prefab` 并设置 Prefab 输出目录。
5. 点击 `加载 Figma 文件`。默认会优先读取本地缓存，避免频繁请求 Figma；需要获取最新设计稿时点击 `强制重新加载`。
6. 在 Page / Frame 树中选择要导入的目标。
7. 在“网络导入（Figma REST API）”中点击 `导入选中项`。

如果 Figma API 正在限流，且项目里已经有一次成功导入生成的 `manifest.json` 和 PNG 资源，可以在窗口左下角的“已导入项目”列表中点击对应项目按钮重建场景。该流程不会请求 Figma，只会使用本地 `reference.png` 和 `layers/*.png` 重新生成 Canvas 层级。

### Figma 插件离线包导入

如果 REST API 被限流，可以改用仓库内的 Figma 本地插件：

1. 在 Figma 中打开 `Plugins / Development / Import plugin from manifest...`。
2. 选择本仓库 `FigmaPlugin/manifest.json`。
3. 在 Figma 文件中选中要导入的 Frame / Component / Instance。
4. 运行 `Figma2Unity Offline Exporter`，导出 `.figma2unity.json`。
5. 回到 Unity，打开 `Tools/Figma UI Importer`。
6. 设置输出资源目录、目标 Canvas、参考图透明度、容器处理策略、是否保存 Prefab 等 Unity 侧选项。
7. 在“本地导入（Figma 插件包）”中点击 `导入 .figma2unity.json`，选择刚下载的 `.figma2unity.json`。
8. Unity 会先展示离线包信息，包括节点名、节点类型、根尺寸、图片倍率、图片资源数、切图图层数、包大小和输出目录；确认后才会写入资源并重建场景。

这个流程不需要 Figma Personal Access Token，也不会请求 Figma REST API。Unity 会把包内图片写入 `Assets/FigmaImports/{fileKey}/{selectedNodeId}/`，生成 `manifest.json`，再用现有离线重建流程创建 Canvas 层级。
如果离线包体积较大或图片资源较多，导入前会在同一个确认窗口里提示，避免误导入大包导致 Unity 短暂卡顿。

导入后会生成类似目录：

```text
Assets/FigmaImports/{fileKey}/{selectedNodeId}/
├── reference.png
├── manifest.json
├── diagnostics.json
└── layers/
    ├── 1_2.png
    └── 3_4.png
```

场景结构：

```text
Canvas
└── FigmaImport_[Name]_[NodeId]
    ├── Reference
    │   └── DesignReference
    └── Layers
        ├── Layer_[Name]_[NodeId]
        └── ...
```

## Prefab 输出

- 勾选 `保存为 Prefab` 后，REST API 导入、Manifest 重建、Figma 插件包导入都会在生成场景根节点后额外保存 Prefab。
- 默认 Prefab 输出目录是 `Assets/FigmaImports/Prefabs`。
- Prefab 文件名格式是 `FigmaImport_[Name]_[NodeId].prefab`。
- 勾选 `覆盖同名场景根节点 / Prefab` 时，同名场景根节点和同名 Prefab 会被覆盖；未勾选时会生成唯一名称，避免覆盖已有内容。

## 切图模式

- `ExportSettings 优先，并补齐自动切片`：默认模式，优先保留 Figma 中设置过 Export 的节点，同时补齐未被这些节点覆盖的可见图层。如果带 Export 的 Auto Layout 容器下包含组件实例，导入器会保留容器自身的可见底图，并展开组件实例，避免整个 Auto Layout 被压成一张图。
- `只导入已设置 Export 的节点`：只导入设计师标记过导出的节点。如果当前选中的根 Frame 本身也设置了 Export，它会作为最底部背景层导入。
- `自动切片可见图层`：遍历可见图层并自动导出图片。
- `仅导入整张参考图`：只生成半透明参考图，不生成 Layers。

## 容器处理策略

该选项主要影响 `ExportSettings 优先，并补齐自动切片`：

- `自动`：默认策略。Auto Layout 容器下有组件实例时，会保留容器自身可见底图，并把组件实例单独导出。
- `严格 ExportSettings`：设计师标记了哪个节点就优先导出哪个节点，适合需要完全尊重 Figma ExportSettings 的设计稿。
- `强制展开容器`：带子切片的容器会尽量展开到子级，适合排查或避免大容器压成一张图。

## 文本导入模式

- `智能`：默认模式。单一样式、纯色、无复杂效果的 Figma Text 会导入为 `TextMeshProUGUI`，复杂文本保留 PNG 切图。
- `TextMeshPro`：尽量把 Figma Text 转成可编辑 TMP 文本，即使混合样式或视觉效果可能无法完全还原。
- `图片`：所有 Text 都按 PNG 切图导入，适合需要完全视觉一致的场景。

Text 图层会在 manifest 中保留 fallback PNG。切换回 `图片` 模式或遇到不支持的复杂文本时，Unity 会继续使用这张 PNG。

### 字体映射

Figma 字体不会自动变成 Unity 字体资源。导入器会统一使用项目级全局映射表：

```text
Assets/FigmaImports/FigmaFontMapping.asset
```

首次导入、从 Manifest 重建、从 Figma 插件包导入，都会自动创建或更新这一个映射表。导入窗口右侧会直接显示字体映射管理栏，可更新所有已导入页面、编辑映射条目、应用映射到选中项，或应用映射到当前打开场景。更新操作会自动扫描 `Assets/FigmaImports` 下所有导入过的页面 manifest 并补齐字体条目。

映射表支持按 Figma PostScript 名称、字体族 + 样式、字体族 + 字重范围匹配到 `TMP_FontAsset`。没有匹配时会使用映射表里的默认字体；如果默认字体也没设置，Unity 会使用 TextMeshPro 组件自己的默认字体。导入时创建的 TMP 文字会记录 Figma 字体信息，设置好映射后可以一键把当前场景里的文字节点更新为新的 TMP 字体。

## 导入诊断报告

每次 REST API 导入、Manifest 重建、Figma 插件包导入都会在输出目录生成 `diagnostics.json`。

- REST API 导入会记录节点动作：`导出`、`展开`、`合并`、`跳过`，并写明原因。
- Manifest 重建和 Figma 插件包导入会记录 manifest 中实际重建的切图层。
- 如果某个节点没有出现在 Unity 中，可以先查看 `diagnostics.json` 中对应 node id 的原因。

## 注意

- Token 使用 `EditorPrefs` 保存在本机，不会写入项目资源。
- 如果 Unity 报 `Unable to complete SSL connection`，可在插件窗口临时勾选 `忽略 SSL 证书错误` 后重试；该选项会跳过 HTTPS 证书校验，仅建议在受信任网络或代理环境中使用。
- 如果 Figma 返回 `429 Rate limit exceeded`，说明当前 token、seat、plan 或网络出口触发了 Figma REST API 限流。可以等待额度恢复，也可以改用 `FigmaPlugin` 本地插件导出 `.figma2unity.json` 离线包。
- 插件会把 Figma 文件结构缓存到 Unity 项目的 `Library/FigmaUiImporterCache`，该目录不会进入版本库。导入时会优先使用已加载的节点树，减少额外 API 请求。
- 导入前如果预估切图数量较多，插件会弹窗确认，避免自动切片一次性触发过多图片导出请求。
- 已导入项目列表的离线重建只能使用当前 Unity 项目 `Assets` 目录内的 manifest 和图片资源；如果移动了 `reference.png` 或 `layers` 目录，需要保持 manifest 中记录的相对路径仍然可用。
- `.figma2unity.json` 使用 base64 内嵌 PNG，文件会比原始图片总量更大。导入器会校验 manifest 引用、图片路径和 base64 数据，错误信息会指出缺失或损坏的资源。很大的 Frame 建议先用 Figma 插件的“仅导出整张参考图”或“只导出已设置 Export 的节点”。
- 为了让切图大小和位置更贴近参考图，建议保留 `创建参考图`。没有参考图时，插件仍会使用 PNG 实际尺寸，但无法进行像素位置校准。
- 插件优先按视觉还原导入；普通 Figma Text 可按文本导入模式转成 TMP，但不会自动生成 Button、Toggle、ScrollView 等语义组件。
- Page 可能很大，建议优先选择具体 Frame。
- Figma 图片 URL 有时效，插件会立即下载到 Unity 项目资源目录。

## 开发验证

仓库包含 Unity EditMode 测试，位于 `Tests/Editor`：

- `FigmaPathUtilityTests`：覆盖 file key 解析、Assets 路径归一化、导入目录和 Prefab 路径生成。
- `FigmaImportManifestServiceTests`：覆盖 manifest 保存与离线重建数据读取。
- `FigmaDocumentParserTests`：覆盖 Auto Layout / Instance 切图策略和导入诊断计划。
- `FigmaImportDiagnosticServiceTests`：覆盖 `diagnostics.json` 报告写入。
- `FigmaPluginPackageImporterTests`：覆盖 `.figma2unity.json` 信息读取、解包、图片写入、资源引用校验、base64 损坏提示和非法资源路径拦截。
- `FigmaFontMappingServiceTests`：覆盖全局字体映射条目去重、PNG 文本图层元数据收集和 manifest 文本元数据扫描。
