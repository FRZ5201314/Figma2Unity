# Changelog

## 0.7.1

- Moved manual imported-page font discovery from the importer window to the `FigmaFontMapping.asset` Inspector.
- Removed the arbitrary JSON font mapping button from the importer window.
- Stopped applying extra TMP Bold style when a mapped Medium/Bold TMP font asset is already assigned.

## 0.7.0

- Added one project-wide global font mapping asset at `Assets/FigmaImports/FigmaFontMapping.asset`.
- Updated REST imports, manifest rebuilds, and plugin package imports to auto-discover Figma fonts and append missing mapping entries.
- Added local JSON font scanning, scene text binding metadata, and window actions for applying font mappings to selected or open-scene TMP text.
- Added EditMode coverage for font mapping discovery and de-duplication.

## 0.6.0

- Added Unity-side Figma font mapping assets for resolving Figma text family/style/weight to TMP font assets.
- Changed Figma plugin package exports to always include TEXT metadata when available, leaving TMP-vs-PNG decisions to the Unity importer.
- Preserved text metadata from manifests even when the exported layer kind is image, enabling Unity import settings to choose the final representation.

## 0.5.1

- Fixed Figma plugin text metadata export by reading TextNode fields from the Plugin API instead of REST-style `style` fields.

## 0.5.0

- Added Text import modes with Smart TextMeshPro conversion for simple single-style Figma text and PNG fallback for complex text.
- Preserved text style metadata in REST manifests and Figma plugin package manifests, including content, font family, size, color, alignment, line height, and fallback images.
- Added TextMeshPro package dependency and diagnostics output for layer kind and text import mode.

## 0.4.2

- Fixed marked root frames being skipped as exported background layers in Figma plugin packages and REST import plans.

## 0.4.1

- Fixed Figma plugin exports for expanded container background slices by rendering a temporary copy with separately exported child slices hidden, preserving the full parent background without transparent holes.

## 0.4.0

- Added configurable container slicing strategies for mixed ExportSettings and auto-slice imports.
- Added `diagnostics.json` import reports for API imports, manifest rebuilds, and Figma plugin package imports.
- Added Figma plugin UI support for container slicing strategy selection.

## 0.3.2

- Fixed default slicing so marked Auto Layout containers with component instance descendants can keep their own visual background while still exporting those instances as separate layers.

## 0.3.1

- Added a confirmation preview before importing `.figma2unity.json` packages, including selected node, dimensions, image scale, package size, asset count, layer count, and output folder.
- Added large offline package warnings before writing PNG assets.
- Improved offline package validation messages for missing manifest references, unsafe paths, and corrupt base64 image data.

## 0.3.0

- Added optional Prefab export for REST imports, manifest rebuilds, and Figma plugin package imports.
- Added a bundled Figma offline exporter workflow for `.figma2unity.json` packages.
- Added EditMode tests for path handling, manifest rebuild data, and plugin package import validation.

## 0.2.0

- Added manifest-based offline scene rebuild.
- Improved visual slice placement using exported PNG size and reference image matching.
- Added local Figma file caching and import retry/error handling improvements.
