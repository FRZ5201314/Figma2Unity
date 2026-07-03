# Changelog

## 0.7.13

- Added `FigmaLayerBinding` Inspector actions for switching Figma TEXT nodes between fallback PNG Image and editable TextMeshProUGUI.
- Added lightweight manifest lookup for converting imported PNG text layers back to TMP and removed the redundant scene `FigmaTextBinding`.
- Prepared Git URL installation metadata, release docs, and the Figma offline exporter zip workflow.

## 0.7.12

- Shortened the fixed importer window while keeping all columns at the same content height.
- Reduced the page tree and log panel heights so the left column remains fully visible.

## 0.7.11

- Made the font mapping column the same width as the left column and restored enough fixed height so lower controls are visible.
- Reworked font mapping entry rows for the narrower column with a stacked `FontAsset` field and full-width details.

## 0.7.10

- Reduced the fixed importer window size and narrowed the font mapping column.

## 0.7.9

- Added a compact font mapping entry drawer that keeps `FontAsset` prominent and supports row-summary expand/collapse.
- Added an import completion prompt when new font mapping entries are discovered and still need TMP font assignment.
- Fixed the importer window size to keep the font mapping list readable.

## 0.7.8

- Tightened the spacing between local package import and local manifest rebuild sections.
- Moved the REST import selected action into the Page / Frame section so selection and import stay together.

## 0.7.7

- Reorganized the importer window so the REST network workflow lives in the left column and local package/manifest workflows live in the middle column.
- Tightened the page tree height to keep the left network workflow visible as a single vertical path.

## 0.7.6

- Split the importer window workflow into REST network import, local Figma plugin package import, and local manifest rebuild sections.
- Moved shared output fields away from REST connection settings so local and network workflows are clearer.

## 0.7.5

- Added an imported-project list in the lower-left importer window area that scans existing `manifest.json` files and rebuilds scenes from the selected project button.
- Removed the old manual manifest picker from the main action bar while keeping plugin package import there.

## 0.7.4

- Restored the font mapping entries to Unity's default serialized List UI in both the importer window and `FigmaFontMapping.asset` Inspector.
- Shortened the importer window and moved the operation controls above the page/status panels in the main workflow column.

## 0.7.3

- Reworked the importer window into a wider three-column layout with settings, page/status panels, and font mapping management visible at once.
- Moved font mapping update/apply actions back into the importer window and added the mapping entries list there.
- Kept only page tree, log output, and font mapping entries as independently scrollable areas.

## 0.7.2

- Improved the `FigmaFontMapping.asset` Inspector with a clearer mapping list and centralized font actions.
- Moved applying font mappings to selection/open scenes from the importer window into the mapping asset Inspector.
- Refined the importer window layout and fixed the log panel rendering so log entries are visible.

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
