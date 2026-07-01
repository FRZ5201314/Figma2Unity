# Changelog

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
