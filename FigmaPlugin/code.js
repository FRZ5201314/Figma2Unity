var PACKAGE_SCHEMA_VERSION = 1;
var DEFAULT_SCALE = 1;
var MAX_SCALE = 4;

figma.showUI(__html__, {
  width: 380,
  height: 360,
  themeColors: true
});

postSelection();
figma.on("selectionchange", postSelection);

figma.ui.onmessage = function (message) {
  if (!message || message.type !== "export-package") {
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
      message: "请选择一个 Frame / Component / Instance。"
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
    throw new Error("请选择一个要导出的 Frame / Component / Instance。");
  }

  var root = selection[0];
  var rootBounds = getRootBounds(root);
  if (!rootBounds) {
    throw new Error("所选节点没有可用尺寸，无法导出。");
  }

  var scale = clampNumber(options.scale, DEFAULT_SCALE, 0.01, MAX_SCALE);
  var mode = options.mode || "markedThenAuto";
  var containerStrategy = options.containerStrategy || "auto";
  var now = new Date().toISOString();
  var fileKey = getFileKey();
  var assets = [];
  var layers = [];

  if (mode !== "noReference") {
    postProgress("正在导出整张参考图...", 0.1);
    var referenceBytes = await exportNodePng(root, scale);
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

    postProgress("正在导出切图 " + (i + 1) + "/" + nodes.length + "...", 0.25 + 0.65 * (i + 1) / Math.max(1, nodes.length));
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
    layers.push(createLayerDto(rootBounds, node, bounds, imagePath, layers.length));
  }

  postProgress("正在生成离线包...", 0.95);
  return {
    schemaVersion: PACKAGE_SCHEMA_VERSION,
    exportedAt: now,
    fileKey: fileKey,
    imageScale: scale,
    manifest: {
      fileKey: fileKey,
      imageScale: scale,
      selectedNodeId: root.id,
      selectedNodeName: root.name || "Selection",
      selectedNodeType: root.type || "FRAME",
      containerSliceStrategy: containerStrategy,
      importedAt: now,
      rootBounds: rootBounds,
      referenceImage: mode === "noReference" ? "" : "reference.png",
      layers: layers
    },
    assets: assets
  };
}

async function exportNodePng(node, scale) {
  if (!node || typeof node.exportAsync !== "function") {
    throw new Error("节点不支持导出 PNG：" + (node && node.name ? node.name : "Unknown"));
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

function createLayerDto(rootBounds, node, bounds, imagePath, siblingIndex) {
  var anchoredPosition = toUnityAnchoredPosition(rootBounds, bounds);
  return {
    nodeId: node.id,
    nodeName: node.name || "Layer",
    type: node.type || "UNKNOWN",
    image: imagePath,
    bounds: bounds,
    unity: {
      anchoredPosition: anchoredPosition,
      sizeDelta: {
        x: bounds.width,
        y: bounds.height
      }
    },
    siblingIndex: siblingIndex,
    unityObjectPath: ""
  };
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

  var canRenderSelf = node !== root && isRenderableNode(node);
  if (canRenderSelf && isAtomicContainerNode(node)) {
    results.push(node);
    return true;
  }

  var isContainer = isContainerNode(node);
  if (canRenderSelf && !isContainer && hasRenderableSelf(node)) {
    results.push(node);
    return true;
  }

  var hasCollectedChild = false;
  var children = getChildren(node);
  for (var i = 0; i < children.length; i++) {
    hasCollectedChild = collectAutoSliceNodes(root, children[i], results) || hasCollectedChild;
  }

  if (node === root) {
    return hasCollectedChild;
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
  }

  collectMergedNodesInDrawOrder(root, markedMap, autoMap, containerStrategy, merged);
  return merged;
}

function collectMergedNodesInDrawOrder(node, markedMap, autoMap, containerStrategy, results) {
  if (!node) {
    return;
  }

  var children = getChildren(node);
  for (var i = 0; i < children.length; i++) {
    var child = children[i];
    if (markedMap[child.id]) {
      if (shouldExpandMarkedContainer(child, autoMap, containerStrategy)) {
        if (hasVisualSelf(child)) {
          results.push(markedMap[child.id]);
        }

        collectMergedNodesInDrawOrder(child, markedMap, autoMap, containerStrategy, results);
        continue;
      }

      results.push(markedMap[child.id]);
      continue;
    }

    if (autoMap[child.id] && (isAtomicContainerNode(child) || !hasMappedDescendant(child, markedMap))) {
      results.push(autoMap[child.id]);
      continue;
    }

    collectMergedNodesInDrawOrder(child, markedMap, autoMap, containerStrategy, results);
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
  return normalizeBounds(node && (node.absoluteBoundingBox || node.absoluteRenderBounds));
}

function getSliceBounds(node) {
  return normalizeBounds(node && (node.absoluteRenderBounds || node.absoluteBoundingBox));
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
