//#region ../preview-geometry/src/markers.ts
/** The CSS class for an empty Canvas slot placeholder. */
const CANVAS_EMPTY_SLOT_PLACEHOLDER_CLASS = "canvas--slot-empty-placeholder";
/** The CSS class for an empty Canvas region placeholder. */
const CANVAS_EMPTY_REGION_PLACEHOLDER_CLASS = "canvas--region-empty-placeholder";
const COMMENT_MARKER_PREFIXES = {
	component: {
		start: "canvas-start-",
		end: "canvas-end-"
	},
	slot: {
		start: "canvas-slot-start-",
		end: "canvas-slot-end-"
	},
	region: {
		start: "canvas-region-start-",
		end: "canvas-region-end-"
	}
};
/** Formats the text content of a Canvas comment marker. */
function formatCanvasCommentMarker({ type, position, id }) {
	return `${COMMENT_MARKER_PREFIXES[type][position]}${id}`;
}
/** Parses the text content of a Canvas comment marker. */
function parseCanvasCommentMarker(value) {
	const normalized = value.trim();
	for (const type of [
		"component",
		"slot",
		"region"
	]) for (const position of ["start", "end"]) {
		const prefix = COMMENT_MARKER_PREFIXES[type][position];
		if (!normalized.startsWith(prefix)) continue;
		const id = normalized.slice(prefix.length).trim();
		return id ? {
			type,
			position,
			id
		} : null;
	}
	return null;
}
/** Returns the attributes for a layout-neutral Canvas template marker. */
function getCanvasTemplateMarkerAttributes({ type, position, id }) {
	const attributes = {
		"data-canvas-marker": position,
		"data-canvas-type": type
	};
	if (type === "region") {
		attributes["data-canvas-region-id"] = id;
		return attributes;
	}
	if (type === "slot") {
		const slotIdentity = parseCanvasSlotIdentity(id);
		if (slotIdentity) attributes["data-canvas-slot-name"] = slotIdentity.slotName;
	}
	attributes["data-canvas-uuid"] = id;
	return attributes;
}
/** Discovers and pairs Canvas comment and template markers below a DOM root. */
function discoverCanvasBoundaries(root, options = {}) {
	const { commentMarkers = true, templateMarkers = true } = options;
	const walker = getOwnerDocument(root).createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_COMMENT);
	const openMarkers = /* @__PURE__ */ new Map();
	const boundaries = [];
	let order = 0;
	let node = walker.nextNode();
	while (node) {
		const marker = (commentMarkers ? parseCommentMarker(node, order) : null) ?? (templateMarkers ? parseTemplateMarker(node, order) : null);
		if (marker) {
			const key = markerKey(marker);
			if (marker.position === "start") {
				const starts = openMarkers.get(key) ?? [];
				starts.push(marker);
				openMarkers.set(key, starts);
			} else {
				const start = openMarkers.get(key)?.pop();
				if (start) boundaries.push({
					type: start.type,
					id: start.id,
					start: start.node,
					end: marker.node,
					markerFormat: start.markerFormat,
					...start.componentUuid ? { componentUuid: start.componentUuid } : {},
					...start.slotName ? { slotName: start.slotName } : {},
					order: start.order
				});
			}
		}
		order += 1;
		node = walker.nextNode();
	}
	return boundaries.sort((first, second) => first.order - second.order).map(({ order: _order, ...boundary }) => boundary);
}
function getOwnerDocument(root) {
	if (root.nodeType === Node.DOCUMENT_NODE) return root;
	if (!root.ownerDocument) throw new Error("Canvas geometry root must belong to a document.");
	return root.ownerDocument;
}
function markerKey(marker) {
	return `${marker.markerFormat}:${marker.type}:${marker.id}`;
}
function parseCommentMarker(node, order) {
	if (node.nodeType !== Node.COMMENT_NODE) return null;
	const marker = parseCanvasCommentMarker(node.nodeValue ?? "");
	if (!marker) return null;
	const slotIdentity = marker.type === "slot" ? parseCanvasSlotIdentity(marker.id) : null;
	return {
		...marker,
		node,
		markerFormat: "comment",
		order,
		...marker.type === "component" ? { componentUuid: marker.id } : {},
		...slotIdentity?.componentUuid ? { componentUuid: slotIdentity.componentUuid } : {},
		...slotIdentity?.slotName ? { slotName: slotIdentity.slotName } : {}
	};
}
function parseTemplateMarker(node, order) {
	if (node.nodeType !== Node.ELEMENT_NODE) return null;
	const element = node;
	if (element.localName !== "template") return null;
	const position = element.getAttribute("data-canvas-marker");
	const type = element.getAttribute("data-canvas-type");
	if (position !== "start" && position !== "end" || !isCanvasBoundaryType(type)) return null;
	const id = element.getAttribute("data-canvas-uuid") ?? (type === "region" ? element.getAttribute("data-canvas-region-id") : null);
	if (!id) return null;
	const slotIdentity = type === "slot" ? parseCanvasSlotIdentity(id) : null;
	const slotName = element.getAttribute("data-canvas-slot-name") ?? slotIdentity?.slotName;
	return {
		type,
		id,
		position,
		node,
		markerFormat: "template",
		order,
		...type === "component" ? { componentUuid: id } : slotIdentity?.componentUuid ? { componentUuid: slotIdentity.componentUuid } : {},
		...slotName ? { slotName } : {}
	};
}
function isCanvasBoundaryType(value) {
	return value === "component" || value === "slot" || value === "region";
}
function parseCanvasSlotIdentity(id) {
	const separator = id.indexOf("/");
	if (separator <= 0 || separator === id.length - 1) return null;
	return {
		componentUuid: id.slice(0, separator),
		slotName: id.slice(separator + 1)
	};
}
//#endregion
export { getCanvasTemplateMarkerAttributes as a, formatCanvasCommentMarker as i, CANVAS_EMPTY_SLOT_PLACEHOLDER_CLASS as n, discoverCanvasBoundaries as r, CANVAS_EMPTY_REGION_PLACEHOLDER_CLASS as t };
