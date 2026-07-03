# Figma2Unity Offline Exporter

本目录是配套的 Figma 本地开发插件，用于在 Figma 内导出 `.figma2unity.json` 离线包。

## 使用方式

### 发布包安装

1. 下载 `Figma2Unity-Offline-Exporter-v0.7.13.zip`。
2. 解压到本地目录。
3. 在 Figma 中打开 `Plugins / Development / Import plugin from manifest...`。
4. 选择解压目录中的 `manifest.json`。

### 仓库源码安装

1. 在 Figma 中打开 `Plugins / Development / Import plugin from manifest...`。
2. 选择本目录下的 `manifest.json`。
3. 在设计稿里选中一个 Frame / Component / Instance。
4. 运行 `Figma2Unity Offline Exporter`。
5. 选择导出模式、容器处理策略和图片倍率。
6. 点击 `导出 .figma2unity.json`。
7. 回到 Unity，打开 `Tools/Figma2Unity`，点击 `从 Figma 插件包导入`。

插件不会请求 Figma REST API，也不需要 Personal Access Token。它只读取当前打开文件中的当前选择，并使用 Figma Plugin API 的 `exportAsync` 导出 PNG。

插件会为 TEXT 节点写入 text 元数据，同时仍导出 PNG 作为 Unity 侧回退资源。最终使用 `TextMeshProUGUI` 还是 PNG Image，由 Unity 导入窗口中的文本导入模式决定。

容器处理策略主要影响 `ExportSettings 优先，并补齐可见图层`：

- `自动`：保留 Auto Layout 容器自身可见底图，并展开组件实例。
- `严格 ExportSettings`：优先按设计稿里的 ExportSettings 导出。
- `强制展开容器`：尽量把带子切片的容器拆到子级。

如果当前选中的根 Frame 本身设置了 Export，插件会把它作为最底部背景层导出；同时单独导出的子层会从这张背景层里临时隐藏，避免重复叠图。
