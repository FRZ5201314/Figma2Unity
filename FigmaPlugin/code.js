// schema 2：manifest 增加 exportBounds / 纹理尺寸，导入端不再靠参考图尺寸反推倍率。
var PACKAGE_SCHEMA_VERSION = 2;
var DEFAULT_SCALE = 1;
var MAX_SCALE = 4;
var currentLanguage = "en";
var STRINGS = {
  en: {
    selectOne: "Select a Frame / Component / Instance.",
    selectExport: "Select a Frame / Component / Instance to export.",
    noBounds: "The selected node has no usable size and cannot be exported.",
    exportingReference: "Exporting the full reference image...",
    exportingSlice: "Exporting slice {0}/{1}...",
    generatingPackage: "Generating offline package...",
    unsupportedPng: "This node does not support PNG export: {0}",
    unknown: "Unknown"
  },
  zh: {
    selectOne: "请选择一个 Frame / Component / Instance。",
    selectExport: "请选择一个要导出的 Frame / Component / Instance。",
    noBounds: "所选节点没有可用尺寸，无法导出。",
    exportingReference: "正在导出整张参考图...",
    exportingSlice: "正在导出切图 {0}/{1}...",
    generatingPackage: "正在生成离线包...",
    unsupportedPng: "节点不支持导出 PNG：{0}",
    unknown: "未知"
  }
};

function t(key) {
  var table = STRINGS[currentLanguage] || STRINGS.en;
  return table[key] || STRINGS.en[key] || key;
}

function tf(key, first, second) {
  return t(key).replace("{0}", String(first)).replace("{1}", String(second));
}

figma.showUI(__html__, {
  width: 380,
  height: 360,
  themeColors: true
});

postSelection();
figma.on("selectionchange", postSelection);

figma.ui.onmessage = function (message) {
  if (!message) {
    return;
  }

  if (message.type === "set-language") {
    currentLanguage = message.language === "zh" ? "zh" : "en";
    postSelection();
    return;
  }

  if (message.type === "refresh-selection") {
    postSelection();
    return;
  }

  if (message.type !== "export-package") {
    return;
  }

  exportSelectedNode(message)
    .then(function (result) {
      figma.ui.postMessage({ type: "done", package: result });
    })
    .catch(function (error) {
      figma.ui.postMessage({
        type: "error",
        message: error && error.message ? error.message : String(error)
      });
    });
};

function postSelection() {
  var selection = figma.currentPage.selection;
  if (!selection || selection.length !== 1) {
    figma.ui.postMessage({
      type: "selection",
      ok: false,
      message: t("selectOne")
    });
    return;
  }

  var node = selection[0];
  figma.ui.postMessage({
    type: "selection",
    ok: true,
    id: node.id,
    name: node.name,
    nodeType: node.type
  });
}

async function exportSelectedNode(options) {
  var selection = figma.currentPage.selection;
  if (!selection || selection.length !== 1) {
    throw new Error(t("selectExport"));
  }

  var root = selection[0];
  var rootBounds = getRootBounds(root);
  if (!rootBounds) {
    throw new Error(t("noBounds"));
  }

  var scale = clampNumber(options.scale, DEFAULT_SCALE, 0.01, MAX_SCALE);
  var mode = options.mode || "markedThenAuto";
  var containerStrategy = options.containerStrategy || "auto";
  var now = new Date().toISOString();
  var fileKey = getFileKey();
  var assets = [];
  var layers = [];
  var referencePngSize = null;
  var referenceExportInfo = null;

  if (mode !== "noReference") {
    postProgress(t("exportingReference"), 0.1);
    var referenceBytes = await exportNodePng(root, scale);
    referencePngSize = readPngSize(referenceBytes);
    // 参考图不隐藏任何子节点，导出边界即整张参考图的实际画布。
    referenceExportInfo = resolveExportBounds(root, {}, referencePngSize, scale);
    assets.push({
      path: "reference.png",
      mimeType: "image/png",
      dataBase64: bytesToBase64(referenceBytes)
    });
  }

  var nodes = mode === "referenceOnly" || mode === "noReference"
    ? []
    : collectImportableNodes(root, mode, containerStrategy);
  var selectedMap = toNodeMap(nodes);

  for (var i = 0; i < nodes.length; i++) {
    var node = nodes[i];
    var bounds = getSliceBounds(node);
    if (!bounds) {
      continue;
    }

    postProgress(tf("exportingSlice", i + 1, nodes.length), 0.25 + 0.65 * (i + 1) / Math.max(1, nodes.length));
    var hiddenDescendantPaths = collectNearestSelectedDescendantPaths(node, selectedMap);
    var bytes = hiddenDescendantPaths.length > 0
      ? await exportNodePngWithHiddenDescendants(node, hiddenDescendantPaths, scale)
      : await exportNodePng(node, scale);
    var imagePath = "layers/" + safeFileName(node.id) + ".png";
    assets.push({
      path: imagePath,
      mimeType: "image/png",
      dataBase64: bytesToBase64(bytes)
    });

    // 切图可能因为子级溢出/阴影而比节点 bounds 大；记录实际导出画布，
    // 导入端直接按画布放置，避免再用参考图尺寸反推倍率。
    var pngSize = readPngSize(bytes);
    var hiddenIds = collectHiddenNodeIds(node, hiddenDescendantPaths);
    var exportInfo = resolveExportBounds(node, hiddenIds, pngSize, scale);
    layers.push(createLayerDto(rootBounds, node, bounds, imagePath, layers.length, exportInfo, pngSize));
  }

  postProgress(t("generatingPackage"), 0.95);
  var manifest = {
    fileKey: fileKey,
    imageScale: scale,
    selectedNodeId: root.id,
    selectedNodeName: root.name || "Selection",
    selectedNodeType: root.type || "FRAME",
    // 只有 Frame 类节点会显式提供 clipsContent；GROUP 默认不裁剪。
    clipsContent: root.clipsContent === undefined
      ? (root.type === "FRAME" || root.type === "COMPONENT" || root.type === "INSTANCE")
      : root.clipsContent === true,
    clipsContentVersion: 1,
    containerSliceStrategy: containerStrategy,
    textImportMode: "metadata",
    importedAt: now,
    rootBounds: rootBounds,
    referenceImage: mode === "noReference" ? "" : "reference.png",
    layers: layers
  };

  if (referenceExportInfo) {
    manifest.rootExportBounds = referenceExportInfo.bounds;
    manifest.rootExportBoundsExact = referenceExportInfo.exact;
  }

  if (referencePngSize) {
    manifest.referenceTextureWidth = referencePngSize.width;
    manifest.referenceTextureHeight = referencePngSize.height;
  }

  return {
    schemaVersion: PACKAGE_SCHEMA_VERSION,
    exportedAt: now,
    fileKey: fileKey,
    imageScale: scale,
    manifest: manifest,
    assets: assets
  };
}

async function exportNodePng(node, scale) {
  if (!node || typeof node.exportAsync !== "function") {
    throw new Error(t("unsupportedPng").replace("{0}", node && node.name ? node.name : t("unknown")));
  }

  return await node.exportAsync({
    format: "PNG",
    constraint: {
      type: "SCALE",
      value: scale
    }
  });
}

async function exportNodePngWithHiddenDescendants(node, hiddenDescendantPaths, scale) {
  var clone = node.clone();
  try {
    figma.currentPage.appendChild(clone);
    clone.x = node.x;
    clone.y = node.y;
    for (var i = 0; i < hiddenDescendantPaths.length; i++) {
      var target = findNodeByChildPath(clone, hiddenDescendantPaths[i]);
      if (target) {
        setNodeInvisibleForExport(target);
      }
    }

    return await exportNodePng(clone, scale);
  } finally {
    clone.remove();
  }
}

function createLayerDto(rootBounds, node, bounds, imagePath, siblingIndex, exportInfo, pngSize) {
  var text = createTextDto(node);
  var layoutBounds = text ? getTextLayoutBounds(node, bounds) : bounds;
  var anchoredPosition = toUnityAnchoredPosition(rootBounds, layoutBounds);
  var result = {
    nodeId: node.id,
    nodeName: node.name || "Layer",
    type: node.type || "UNKNOWN",
    kind: text ? "text" : "image",
    image: imagePath,
    text: text,
    bounds: layoutBounds,
    unity: {
      anchoredPosition: anchoredPosition,
      sizeDelta: {
        x: layoutBounds.width,
        y: layoutBounds.height
      }
    },
    siblingIndex: siblingIndex,
    unityObjectPath: ""
  };

  // layout bounds 用于 TMP 文本；exportBounds 是 PNG 实际画布，用于图片放置。
  if (exportInfo) {
    result.exportBounds = exportInfo.bounds;
    result.exportBoundsExact = exportInfo.exact;
  }

  if (pngSize) {
    result.textureWidth = pngSize.width;
    result.textureHeight = pngSize.height;
  }

  return result;
}

function createTextDto(node) {
  if (!node || node.type !== "TEXT" || typeof node.characters !== "string" || node.characters.length === 0) {
    return null;
  }

  var fontSize = readNumberValue(node.fontSize);
  var fontName = readFontName(node.fontName);
  var mixedStyles = hasMixedTextStyles(node);
  var colorResult = getSolidTextColor(node);
  var unsupportedVisuals = colorResult.unsupported
    || hasVisiblePaints(node.strokes)
    || hasEffects(node);

  return {
    characters: node.characters,
    fontFamily: fontName.family,
    fontStyleName: fontName.style,
    fontPostScriptName: "",
    fontWeight: inferFontWeight(fontName.style),
    fontSize: fontSize,
    lineHeightPx: readLineHeightPx(node.lineHeight, fontSize),
    letterSpacing: readLetterSpacing(node.letterSpacing, fontSize),
    color: colorResult.color,
    horizontalAlign: readStringValue(node.textAlignHorizontal),
    verticalAlign: readStringValue(node.textAlignVertical),
    autoResize: node.textAutoResize || "",
    hasMixedStyles: mixedStyles,
    hasUnsupportedVisuals: unsupportedVisuals
  };
}

function getTextLayoutBounds(node, fallbackBounds) {
  return normalizeBounds(node && (node.absoluteBoundingBox || node.absoluteRenderBounds)) || fallbackBounds;
}

function hasMixedTextStyles(node) {
  if (isMixedValue(node.fontName)
    || isMixedValue(node.fontSize)
    || isMixedValue(node.lineHeight)
    || isMixedValue(node.letterSpacing)
    || isMixedValue(node.textAlignHorizontal)
    || isMixedValue(node.textAlignVertical)
    || isMixedValue(node.fills)
    || isMixedValue(node.strokes)) {
    return true;
  }

  var overrides = node && Array.isArray(node.characterStyleOverrides)
    ? node.characterStyleOverrides
    : [];
  for (var i = 0; i < overrides.length; i++) {
    if (Number(overrides[i]) !== 0) {
      return true;
    }
  }

  var table = node && node.styleOverrideTable;
  return !!table && Object.keys(table).length > 0;
}

function readFontName(value) {
  if (isMixedValue(value) || !value || typeof value !== "object") {
    return { family: "", style: "" };
  }

  return {
    family: value.family || "",
    style: value.style || ""
  };
}

function readNumberValue(value) {
  if (isMixedValue(value)) {
    return 0;
  }

  var number = Number(value);
  return isFinite(number) ? number : 0;
}

function readStringValue(value) {
  if (isMixedValue(value) || value == null) {
    return "";
  }

  return String(value);
}

function readLineHeightPx(value, fontSize) {
  if (isMixedValue(value) || !value || typeof value !== "object") {
    return 0;
  }

  var amount = Number(value.value);
  if (!isFinite(amount)) {
    return 0;
  }

  if (value.unit === "PIXELS") {
    return amount;
  }

  if (value.unit === "PERCENT") {
    return fontSize * amount / 100;
  }

  return 0;
}

function readLetterSpacing(value, fontSize) {
  if (isMixedValue(value) || !value || typeof value !== "object") {
    return 0;
  }

  var amount = Number(value.value);
  if (!isFinite(amount)) {
    return 0;
  }

  if (value.unit === "PIXELS") {
    return amount;
  }

  if (value.unit === "PERCENT") {
    return fontSize * amount / 100;
  }

  return 0;
}

function inferFontWeight(style) {
  var normalized = String(style || "").toLowerCase().replace(/[\s_-]+/g, "");
  if (normalized.indexOf("thin") >= 0 || normalized.indexOf("hairline") >= 0) {
    return 100;
  }

  if (normalized.indexOf("extralight") >= 0 || normalized.indexOf("ultralight") >= 0) {
    return 200;
  }

  if (normalized.indexOf("light") >= 0) {
    return 300;
  }

  if (normalized.indexOf("medium") >= 0) {
    return 500;
  }

  if (normalized.indexOf("semibold") >= 0 || normalized.indexOf("demibold") >= 0) {
    return 600;
  }

  if (normalized.indexOf("extrabold") >= 0 || normalized.indexOf("ultrabold") >= 0) {
    return 800;
  }

  if (normalized.indexOf("black") >= 0 || normalized.indexOf("heavy") >= 0) {
    return 900;
  }

  if (normalized.indexOf("bold") >= 0) {
    return 700;
  }

  return 400;
}

function isMixedValue(value) {
  return typeof figma !== "undefined" && value === figma.mixed;
}

function getSolidTextColor(node) {
  var result = {
    color: { r: 1, g: 1, b: 1, a: 1 },
    unsupported: false
  };

  var fills = node && Array.isArray(node.fills) ? node.fills : [];
  var visibleCount = 0;
  var foundColor = false;
  for (var i = 0; i < fills.length; i++) {
    var fill = fills[i];
    if (!fill || fill.visible === false) {
      continue;
    }

    visibleCount++;
    if (fill.type !== "SOLID" || !fill.color) {
      result.unsupported = true;
      continue;
    }

    if (!foundColor) {
      var fillOpacity = typeof fill.opacity === "number" ? fill.opacity : 1;
      var nodeOpacity = typeof node.opacity === "number" ? node.opacity : 1;
      result.color = {
        r: clampNumber(fill.color.r, 1, 0, 1),
        g: clampNumber(fill.color.g, 1, 0, 1),
        b: clampNumber(fill.color.b, 1, 0, 1),
        a: clampNumber(fillOpacity * nodeOpacity, 1, 0, 1)
      };
      foundColor = true;
    }
  }

  result.unsupported = result.unsupported || !foundColor || visibleCount > 1;
  return result;
}

function collectImportableNodes(root, mode, containerStrategy) {
  if (mode === "markedOnly") {
    var markedOnly = [];
    collectMarkedNodes(root, root, markedOnly);
    return markedOnly;
  }

  if (mode === "autoOnly") {
    var autoOnly = [];
    collectAutoSliceNodes(root, root, autoOnly);
    return autoOnly;
  }

  var markedNodes = [];
  var autoNodes = [];
  collectMarkedNodes(root, root, markedNodes);
  collectAutoSliceNodes(root, root, autoNodes);
  return mergeMarkedAndAutoNodes(root, markedNodes, autoNodes, containerStrategy);
}

function collectMarkedNodes(root, node, results) {
  if (!node || !isVisible(node)) {
    return;
  }

  if (hasExportSettings(node) && isNodeInsideRoot(root, node) && hasUsableBounds(node)) {
    results.push(node);
  }

  var children = getChildren(node);
  for (var i = 0; i < children.length; i++) {
    collectMarkedNodes(root, children[i], results);
  }
}

function collectAutoSliceNodes(root, node, results) {
  if (!node || !isVisible(node) || !hasUsableBounds(node) || !isNodeInsideRoot(root, node)) {
    return false;
  }

  var isRoot = node === root;
  var hasOwnRootVisual = isRoot && isRenderableNode(node) && hasVisualSelf(node);
  if (hasOwnRootVisual) {
    results.push(node);
  }

  var canRenderSelf = !isRoot && isRenderableNode(node);
  if (canRenderSelf && isAtomicContainerNode(node)) {
    results.push(node);
    return true;
  }

  var isContainer = isContainerNode(node);
  if (canRenderSelf && !isContainer && hasRenderableSelf(node)) {
    results.push(node);
    return true;
  }

  var hasOwnContainerVisual = canRenderSelf && isContainer && hasVisualSelf(node);
  if (hasOwnContainerVisual) {
    results.push(node);
  }

  var hasCollectedChild = false;
  var children = getChildren(node);
  for (var i = 0; i < children.length; i++) {
    hasCollectedChild = collectAutoSliceNodes(root, children[i], results) || hasCollectedChild;
  }

  if (isRoot) {
    return hasOwnRootVisual || hasCollectedChild;
  }

  if (hasOwnContainerVisual) {
    return true;
  }

  if (canRenderSelf && !hasCollectedChild && (hasExportSettings(node) || children.length === 0 || hasRenderableSelf(node))) {
    results.push(node);
    return true;
  }

  return hasCollectedChild;
}

function mergeMarkedAndAutoNodes(root, markedNodes, autoNodes, containerStrategy) {
  var markedMap = toNodeMap(markedNodes);
  var autoMap = toNodeMap(autoNodes);
  var merged = [];
  if (root && root.id && markedMap[root.id]) {
    merged.push(markedMap[root.id]);
  } else if (root && root.id && autoMap[root.id]) {
    merged.push(autoMap[root.id]);
  }

  collectMergedNodesInDrawOrder(root, markedMap, autoMap, containerStrategy, merged, true);
  return merged;
}

// includeAuto 为 false 时只继续收集显式标记 ExportSettings 的节点：
// 已折叠的容器/实例不再补齐普通子层，避免父子切图重复叠图。
function collectMergedNodesInDrawOrder(node, markedMap, autoMap, containerStrategy, results, includeAuto) {
  if (!node) {
    return;
  }

  var children = getChildren(node);
  for (var i = 0; i < children.length; i++) {
    var child = children[i];
    if (markedMap[child.id]) {
      // 显式标记的节点始终导出，即使它位于已折叠的容器内部。
      results.push(markedMap[child.id]);
      var expand = includeAuto && shouldExpandMarkedContainer(child, autoMap, containerStrategy);
      collectMergedNodesInDrawOrder(child, markedMap, autoMap, containerStrategy, results, expand);
      continue;
    }

    if (includeAuto && autoMap[child.id] && (isAtomicContainerNode(child) || !hasMappedDescendant(child, markedMap))) {
      var hasMarkedChild = hasMappedDescendant(child, markedMap);
      if (!isAtomicContainerNode(child) && hasVisualSelf(child) && hasMappedDescendant(child, autoMap)) {
        results.push(autoMap[child.id]);
        collectMergedNodesInDrawOrder(child, markedMap, autoMap, containerStrategy, results, includeAuto);
        continue;
      }

      results.push(autoMap[child.id]);
      // 整体折叠的实例/容器仍要保留内部显式标记的节点。
      if (hasMarkedChild) {
        collectMergedNodesInDrawOrder(child, markedMap, autoMap, containerStrategy, results, false);
      }
      continue;
    }

    collectMergedNodesInDrawOrder(child, markedMap, autoMap, containerStrategy, results, includeAuto);
  }
}

function shouldExpandMarkedContainer(node, autoMap, containerStrategy) {
  if (!node || !hasMappedDescendant(node, autoMap)) {
    return false;
  }

  if (containerStrategy === "strict") {
    return false;
  }

  if (containerStrategy === "expand") {
    return isContainerNode(node);
  }

  return isAutoLayoutNode(node) && hasAtomicDescendant(node);
}

function hasMappedDescendant(node, map) {
  var children = getChildren(node);
  for (var i = 0; i < children.length; i++) {
    var child = children[i];
    if (map[child.id] || hasMappedDescendant(child, map)) {
      return true;
    }
  }

  return false;
}

function collectNearestSelectedDescendantPaths(node, selectedMap) {
  var results = [];
  appendNearestSelectedDescendantPaths(node, selectedMap, [], results);
  return results;
}

function appendNearestSelectedDescendantPaths(node, selectedMap, path, results) {
  var children = getChildren(node);
  for (var i = 0; i < children.length; i++) {
    var child = children[i];
    var childPath = path.concat([i]);
    if (child && child.id && selectedMap[child.id]) {
      results.push(childPath);
      continue;
    }

    appendNearestSelectedDescendantPaths(child, selectedMap, childPath, results);
  }
}

function findNodeByChildPath(node, path) {
  var current = node;
  for (var i = 0; i < path.length; i++) {
    var children = getChildren(current);
    current = children[path[i]];
    if (!current) {
      return null;
    }
  }

  return current;
}

function setNodeInvisibleForExport(node) {
  if (!node) {
    return;
  }

  if (typeof node.opacity === "number") {
    node.opacity = 0;
    return;
  }

  if (typeof node.visible === "boolean") {
    node.visible = false;
  }
}

function hasAtomicDescendant(node) {
  var children = getChildren(node);
  for (var i = 0; i < children.length; i++) {
    var child = children[i];
    if (isAtomicContainerNode(child) || hasAtomicDescendant(child)) {
      return true;
    }
  }

  return false;
}

function hasVisualSelf(node) {
  return hasVisiblePaints(node && node.fills)
    || hasVisiblePaints(node && node.strokes)
    || hasEffects(node)
    || hasTextCharacters(node);
}

function toNodeMap(nodes) {
  var map = {};
  for (var i = 0; i < nodes.length; i++) {
    if (nodes[i] && nodes[i].id && !map[nodes[i].id]) {
      map[nodes[i].id] = nodes[i];
    }
  }

  return map;
}

function isRenderableNode(node) {
  return contains([
    "RECTANGLE",
    "ELLIPSE",
    "POLYGON",
    "STAR",
    "VECTOR",
    "BOOLEAN_OPERATION",
    "LINE",
    "TEXT",
    "FRAME",
    "GROUP",
    "COMPONENT",
    "INSTANCE"
  ], node.type);
}

function isContainerNode(node) {
  return contains([
    "DOCUMENT",
    "CANVAS",
    "FRAME",
    "GROUP",
    "COMPONENT",
    "INSTANCE"
  ], node.type);
}

function isAtomicContainerNode(node) {
  return node && node.type === "INSTANCE";
}

function isAutoLayoutNode(node) {
  return node && (node.layoutMode === "HORIZONTAL" || node.layoutMode === "VERTICAL");
}

function hasRenderableSelf(node) {
  return hasExportSettings(node)
    || hasVisiblePaints(node.fills)
    || hasVisiblePaints(node.strokes)
    || hasEffects(node)
    || hasTextCharacters(node)
    || getChildren(node).length === 0;
}

function hasExportSettings(node) {
  return Array.isArray(node.exportSettings) && node.exportSettings.length > 0;
}

function hasVisiblePaints(paints) {
  if (!Array.isArray(paints)) {
    return false;
  }

  for (var i = 0; i < paints.length; i++) {
    if (paints[i] && paints[i].visible !== false) {
      return true;
    }
  }

  return false;
}

function hasEffects(node) {
  return Array.isArray(node.effects) && node.effects.length > 0;
}

function hasTextCharacters(node) {
  return typeof node.characters === "string" && node.characters.length > 0;
}

function isVisible(node) {
  return !node || node.visible !== false;
}

function getChildren(node) {
  return node && Array.isArray(node.children) ? node.children : [];
}

function isNodeInsideRoot(root, node) {
  var rootBounds = getRootBounds(root);
  var nodeBounds = getRootBounds(node);
  if (!rootBounds || !nodeBounds) {
    return false;
  }

  return nodeBounds.x + nodeBounds.width >= rootBounds.x
    && nodeBounds.x <= rootBounds.x + rootBounds.width
    && nodeBounds.y + nodeBounds.height >= rootBounds.y
    && nodeBounds.y <= rootBounds.y + rootBounds.height;
}

function hasUsableBounds(node) {
  return !!getRootBounds(node);
}

function getRootBounds(node) {
  if (!node) {
    return null;
  }

  // 零高度/零宽度节点（如 LINE、分割线）的 absoluteBoundingBox 不可用，
  // 需回退到包含描边和效果的 absoluteRenderBounds，否则会被误判为无尺寸而跳过。
  return normalizeBounds(node.absoluteBoundingBox) || normalizeBounds(node.absoluteRenderBounds);
}

function getSliceBounds(node) {
  if (!node) {
    return null;
  }

  // 切图尺寸优先取渲染边界，保证与 exportAsync 输出的 PNG 尺寸一致。
  return normalizeBounds(node.absoluteRenderBounds) || normalizeBounds(node.absoluteBoundingBox);
}

// 读取 PNG IHDR 中的像素尺寸。exportAsync 的实际画布可能包含 effects/子级溢出，
// 不能仅用 node bounds 代替。
function readPngSize(bytes) {
  if (!bytes || bytes.length < 24) {
    return null;
  }

  if (bytes[0] !== 0x89 || bytes[1] !== 0x50 || bytes[2] !== 0x4e || bytes[3] !== 0x47) {
    return null;
  }

  var width = ((bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19]) >>> 0;
  var height = ((bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]) >>> 0;
  return width > 0 && height > 0 ? { width: width, height: height } : null;
}

function unionBounds(a, b) {
  if (!a) {
    return b;
  }

  if (!b) {
    return a;
  }

  var left = Math.min(a.x, b.x);
  var top = Math.min(a.y, b.y);
  var right = Math.max(a.x + a.width, b.x + b.width);
  var bottom = Math.max(a.y + a.height, b.y + b.height);
  return { x: left, y: top, width: right - left, height: bottom - top };
}

function shouldIncludeChildrenInExportBounds(node) {
  if (!node) {
    return false;
  }

  // SECTION 不裁剪；Frame/Component 只有 clipsContent=false 时才把子级溢出算进画布。
  // GROUP/INSTANCE 的 render bounds 已经覆盖其内容，递归反而可能把被裁剪的子级算大。
  if (node.type === "SECTION") {
    return true;
  }

  if (node.type === "FRAME" || node.type === "COMPONENT") {
    return node.clipsContent === false;
  }

  return false;
}

function computeExportBounds(node, hiddenIds) {
  if (!node
    || node.visible === false
    || node.opacity === 0
    || (hiddenIds && hiddenIds[node.id])) {
    return null;
  }

  var bounds = getSliceBounds(node);
  if (!shouldIncludeChildrenInExportBounds(node)) {
    return bounds;
  }

  var children = getChildren(node);
  for (var i = 0; i < children.length; i++) {
    var child = children[i];
    if (!child
      || child.visible === false
      || child.opacity === 0
      || (hiddenIds && hiddenIds[child.id])) {
      continue;
    }

    var childBounds = computeExportBounds(child, hiddenIds);
    if (childBounds) {
      bounds = unionBounds(bounds, childBounds);
    }
  }

  return bounds;
}

function collectHiddenNodeIds(node, hiddenDescendantPaths) {
  var ids = {};
  if (!node || !hiddenDescendantPaths) {
    return ids;
  }

  for (var i = 0; i < hiddenDescendantPaths.length; i++) {
    var target = findNodeByChildPath(node, hiddenDescendantPaths[i]);
    if (target && target.id) {
      ids[target.id] = true;
    }
  }

  return ids;
}

function resolveExportBounds(node, hiddenIds, pngSize, scale) {
  var computed = computeExportBounds(node, hiddenIds);
  if (!computed) {
    return null;
  }

  if (!pngSize || !(scale > 0)) {
    return { bounds: computed, exact: false };
  }

  var tolerance = 2 * Math.max(1, scale);
  var expectedWidth = computed.width * scale;
  var expectedHeight = computed.height * scale;
  if (Math.abs(pngSize.width - expectedWidth) <= tolerance
    && Math.abs(pngSize.height - expectedHeight) <= tolerance) {
    return { bounds: computed, exact: true };
  }

  // 计算范围与 PNG 不一致时，围绕已计算的可见内容范围对称补齐/收缩。
  // 阴影偏移不对称时会有几像素误差，用 exportBoundsExact=false 标记。
  var targetWidth = pngSize.width / scale;
  var targetHeight = pngSize.height / scale;
  var extraWidth = targetWidth - computed.width;
  var extraHeight = targetHeight - computed.height;
  return {
    bounds: {
      x: computed.x - extraWidth * 0.5,
      y: computed.y - extraHeight * 0.5,
      width: targetWidth,
      height: targetHeight
    },
    exact: false
  };
}

function normalizeBounds(bounds) {
  if (!bounds || bounds.width <= 0.01 || bounds.height <= 0.01) {
    return null;
  }

  return {
    x: bounds.x,
    y: bounds.y,
    width: Math.max(1, bounds.width),
    height: Math.max(1, bounds.height)
  };
}

function toUnityAnchoredPosition(rootBounds, nodeBounds) {
  var localLeft = nodeBounds.x - rootBounds.x;
  var localTop = nodeBounds.y - rootBounds.y;
  return {
    x: localLeft + nodeBounds.width * 0.5 - rootBounds.width * 0.5,
    y: rootBounds.height * 0.5 - localTop - nodeBounds.height * 0.5
  };
}

function getFileKey() {
  if (typeof figma.fileKey === "string" && figma.fileKey.length > 0) {
    return figma.fileKey;
  }

  return "figma-local-file";
}

function safeFileName(value) {
  return String(value || "empty").replace(/[\\/:*?"<>|]/g, "_");
}

function clampNumber(value, fallback, min, max) {
  var parsed = Number(value);
  if (!isFinite(parsed)) {
    parsed = fallback;
  }

  return Math.max(min, Math.min(max, parsed));
}

function contains(values, value) {
  for (var i = 0; i < values.length; i++) {
    if (values[i] === value) {
      return true;
    }
  }

  return false;
}

function postProgress(message, value) {
  figma.ui.postMessage({
    type: "progress",
    message: message,
    value: value
  });
}

function bytesToBase64(bytes) {
  var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  var output = "";
  var i;
  for (i = 0; i + 2 < bytes.length; i += 3) {
    var n = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
    output += alphabet[(n >> 18) & 63];
    output += alphabet[(n >> 12) & 63];
    output += alphabet[(n >> 6) & 63];
    output += alphabet[n & 63];
  }

  if (i < bytes.length) {
    var remaining = bytes.length - i;
    var value = bytes[i] << 16;
    if (remaining === 2) {
      value |= bytes[i + 1] << 8;
    }

    output += alphabet[(value >> 18) & 63];
    output += alphabet[(value >> 12) & 63];
    output += remaining === 2 ? alphabet[(value >> 6) & 63] : "=";
    output += "=";
  }

  return output;
}
