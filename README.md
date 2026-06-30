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
- 生成 `manifest.json`
- 支持覆盖同名场景根节点

## 安装

这是一个 Unity Package Manager 本地包。推荐放在 Unity 项目外部或项目内 `Packages` 目录外，然后在 Unity 中选择：

`Window / Package Manager / + / Add package from disk...`

选择本目录下的 `package.json`。

## 使用

1. 在 Unity 菜单打开 `Tools/Figma UI Importer`。
2. 填写 Figma Personal Access Token。
3. 填写 Figma File Key 或文件 URL。
4. 设置输出资源目录，默认是 `Assets/FigmaImports`。
5. 点击 `加载 Figma 文件`。默认会优先读取本地缓存，避免频繁请求 Figma；需要获取最新设计稿时点击 `强制重新加载`。
6. 在 Page / Frame 树中选择要导入的目标。
7. 点击 `导入选中项`。

导入后会生成类似目录：

```text
Assets/FigmaImports/{fileKey}/{selectedNodeId}/
├── reference.png
├── manifest.json
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

## 切图模式

- `ExportSettings 优先，并补齐自动切片`：默认模式，优先保留 Figma 中设置过 Export 的节点，同时补齐未被这些节点覆盖的可见图层。
- `只导入已设置 Export 的节点`：只导入设计师标记过导出的节点。
- `自动切片可见图层`：遍历可见图层并自动导出图片。
- `仅导入整张参考图`：只生成半透明参考图，不生成 Layers。

## 注意

- Token 使用 `EditorPrefs` 保存在本机，不会写入项目资源。
- 如果 Unity 报 `Unable to complete SSL connection`，可在插件窗口临时勾选 `忽略 SSL 证书错误` 后重试；该选项会跳过 HTTPS 证书校验，仅建议在受信任网络或代理环境中使用。
- 如果 Figma 返回 `429 Rate limit exceeded`，说明当前 token 或网络出口触发了 Figma 限流。插件会自动退避重试；仍失败时请等待几分钟后再导入，或降低重复加载/导入频率。
- 插件会把 Figma 文件结构缓存到 Unity 项目的 `Library/FigmaUiImporterCache`，该目录不会进入版本库。导入时会优先使用已加载的节点树，减少额外 API 请求。
- 导入前如果预估切图数量较多，插件会弹窗确认，避免自动切片一次性触发过多图片导出请求。
- 插件按视觉还原导入，不会把 Figma Text 转成 TMP，也不会自动生成 Button、Toggle、ScrollView 等语义组件。
- Page 可能很大，建议优先选择具体 Frame。
- Figma 图片 URL 有时效，插件会立即下载到 Unity 项目资源目录。
