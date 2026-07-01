# Figma2Unity Offline Exporter

本目录是配套的 Figma 本地开发插件，用于在 Figma 内导出 `.figma2unity.json` 离线包。

## 使用方式

1. 在 Figma 中打开 `Plugins / Development / Import plugin from manifest...`。
2. 选择本目录下的 `manifest.json`。
3. 在设计稿里选中一个 Frame / Component / Instance。
4. 运行 `Figma2Unity Offline Exporter`。
5. 点击 `导出 .figma2unity.json`。
6. 回到 Unity，打开 `Tools/Figma UI Importer`，点击 `从 Figma 插件包导入`。

插件不会请求 Figma REST API，也不需要 Personal Access Token。它只读取当前打开文件中的当前选择，并使用 Figma Plugin API 的 `exportAsync` 导出 PNG。
