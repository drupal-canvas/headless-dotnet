import { d as HEADLESS_GEOMETRY_MESSAGE, g as HEADLESS_REFRESH_ACK_MESSAGE, m as HEADLESS_HEIGHT_PROBE_MESSAGE, p as HEADLESS_HEIGHT_MESSAGE, t as EXPIRY_SLACK_MS, v as HEADLESS_RENEW_REQUEST_MESSAGE, y as HEADLESS_STATUS_MESSAGE } from "../draft-data-CEe-bbd-.js";
import { r as discoverCanvasBoundaries } from "../markers-CTZZDG85.js";
//#region ../preview-geometry/src/measure.ts
/** Returns the smallest rectangle containing every non-empty input rectangle. */
function unionCanvasRects(rects) {
	let top = Number.POSITIVE_INFINITY;
	let right = Number.NEGATIVE_INFINITY;
	let bottom = Number.NEGATIVE_INFINITY;
	let left = Number.POSITIVE_INFINITY;
	let found = false;
	for (let index = 0; index < rects.length; index += 1) {
		const rect = rects[index];
		if (!rect) continue;
		const width = rect.right - rect.left;
		const height = rect.bottom - rect.top;
		if (![
			rect.top,
			rect.right,
			rect.bottom,
			rect.left
		].every(Number.isFinite) || width === 0 && height === 0) continue;
		found = true;
		top = Math.min(top, rect.top);
		right = Math.max(right, rect.right);
		bottom = Math.max(bottom, rect.bottom);
		left = Math.min(left, rect.left);
	}
	if (!found) return null;
	return {
		top,
		right,
		bottom,
		left,
		width: right - left,
		height: bottom - top
	};
}
/** Measures one marker pair in viewport CSS pixels. */
function measureCanvasBoundary(boundary) {
	if (!boundary.start.isConnected || !boundary.end.isConnected || boundary.start.ownerDocument !== boundary.end.ownerDocument) return null;
	const rect = measureRange(boundary);
	if (!rect) return null;
	const slotContainer = boundary.type === "slot" && boundary.start.parentElement === boundary.end.parentElement ? boundary.start.parentElement : null;
	return {
		type: boundary.type,
		id: boundary.id,
		rect,
		markerFormat: boundary.markerFormat,
		...boundary.componentUuid ? { componentUuid: boundary.componentUuid } : {},
		...boundary.slotName ? { slotName: boundary.slotName } : {},
		...slotContainer ? { stackDirection: getCanvasStackDirection(slotContainer) } : {}
	};
}
/** Discovers and measures every complete Canvas boundary below a DOM root. */
function measureCanvasGeometry(root, options = {}) {
	return discoverCanvasBoundaries(root, options).flatMap((boundary) => {
		const geometry = measureCanvasBoundary(boundary);
		return geometry ? [geometry] : [];
	});
}
/** Detects the primary flex or grid stacking direction of a slot container. */
function getCanvasStackDirection(container) {
	const view = container.ownerDocument.defaultView;
	if (!view) return "vertical";
	let element = container;
	let style = view.getComputedStyle(element);
	if (style.display === "contents" && element.parentElement) {
		element = element.parentElement;
		style = view.getComputedStyle(element);
	}
	if (style.display.includes("flex")) return style.flexDirection === "row" || style.flexDirection === "row-reverse" ? "horizontal-flex" : "vertical-flex";
	if (style.display.includes("grid")) {
		const columns = gridTracks(style.gridTemplateColumns);
		const rows = gridTracks(style.gridTemplateRows);
		if (columns.length > 1) return "horizontal-grid";
		if (columns.length === 1 || rows.length > 1) return "vertical-grid";
		if (style.gridAutoFlow.includes("column")) return "vertical-grid";
		if (style.gridAutoFlow.includes("row")) return "horizontal-grid";
	}
	return "vertical";
}
function measureRange(boundary) {
	const document = boundary.start.ownerDocument;
	if (!document) return null;
	const elementRect = measureSiblingElements(boundary);
	if (elementRect) return elementRect;
	try {
		const boundaryRange = document.createRange();
		boundaryRange.setStartAfter(boundary.start);
		boundaryRange.setEndBefore(boundary.end);
		const textRange = createTextRange(boundaryRange, boundary.start, boundary.end);
		if (textRange && typeof textRange.getClientRects === "function") {
			const rect = unionCanvasRects(textRange.getClientRects());
			if (rect) return rect;
		}
	} catch {}
	return null;
}
/** Creates a range containing text between markers without boundary whitespace. */
function createTextRange(boundaryRange, boundaryStart, boundaryEnd) {
	const document = boundaryRange.startContainer.ownerDocument;
	if (!document) return null;
	const walker = document.createTreeWalker(boundaryRange.commonAncestorContainer, NodeFilter.SHOW_ALL);
	walker.currentNode = boundaryStart;
	let firstNode = null;
	let firstOffset = 0;
	let lastNode = null;
	let lastOffset = 0;
	let node = walker.nextNode();
	while (node && node !== boundaryEnd) {
		if (node.nodeType === Node.TEXT_NODE) {
			const value = node.nodeValue ?? "";
			const firstContentOffset = value.search(/[^\t\n\f\r ]/u);
			if (firstContentOffset !== -1) {
				firstNode ??= node;
				if (firstNode === node) firstOffset = firstContentOffset;
				lastNode = node;
				lastOffset = value.search(/[\t\n\f\r ]*$/u);
			}
		}
		node = walker.nextNode();
	}
	if (node !== boundaryEnd || !firstNode || !lastNode) return null;
	const textRange = document.createRange();
	textRange.setStart(firstNode, firstOffset);
	textRange.setEnd(lastNode, lastOffset);
	return textRange;
}
function measureSiblingElements(boundary) {
	if (boundary.start.parentNode !== boundary.end.parentNode) return null;
	const rects = [];
	let node = boundary.start.nextSibling;
	while (node && node !== boundary.end) {
		if (node.nodeType === Node.ELEMENT_NODE) collectElementRects(node, rects);
		node = node.nextSibling;
	}
	return unionCanvasRects(rects);
}
function collectElementRects(element, rects) {
	const clientRects = Array.from(element.getClientRects());
	if (clientRects.length > 0) {
		rects.push(...clientRects);
		return;
	}
	const boundingRect = element.getBoundingClientRect();
	if (boundingRect.width !== 0 || boundingRect.height !== 0) {
		rects.push(boundingRect);
		return;
	}
	Array.from(element.children).forEach((child) => {
		collectElementRects(child, rects);
	});
}
function gridTracks(value) {
	if (!value || value === "none") return [];
	return value.split(/\s+/).filter((track) => track !== "0px" && track !== "auto");
}
//#endregion
//#region ../preview-geometry/src/observer.ts
/**
* Observes one preview document and emits batched geometry snapshots when its
* layout can have changed.
*/
function createCanvasGeometryObserver(options) {
	const { root, onChange, ...measurementOptions } = options;
	const document = getOwnerDocument(root);
	const view = document.defaultView;
	if (!view) throw new Error("Canvas geometry observation requires a browser window.");
	let disconnected = false;
	let scheduledFrame = null;
	let previousSnapshot = null;
	let forceNextSnapshot = false;
	const measure = () => measureCanvasGeometry(root, measurementOptions);
	const emit = () => {
		scheduledFrame = null;
		if (disconnected) return;
		const forceSnapshot = forceNextSnapshot;
		forceNextSnapshot = false;
		const geometry = measure();
		const snapshot = JSON.stringify(geometry);
		if (forceSnapshot || snapshot !== previousSnapshot) {
			previousSnapshot = snapshot;
			onChange(geometry);
		}
	};
	const scheduleRefresh = (forceSnapshot = false) => {
		if (disconnected) return;
		forceNextSnapshot ||= forceSnapshot;
		if (scheduledFrame !== null) return;
		scheduledFrame = view.requestAnimationFrame(emit);
	};
	const refresh = () => scheduleRefresh();
	const resizeObserver = typeof view.ResizeObserver === "function" ? new view.ResizeObserver(refresh) : null;
	const refreshResizeTargets = () => {
		resizeObserver?.disconnect();
		const boundaries = discoverCanvasBoundaries(root, measurementOptions);
		collectResizeTargets(root, boundaries).forEach((element) => {
			resizeObserver?.observe(element);
		});
	};
	refreshResizeTargets();
	const mutationObserver = new view.MutationObserver((mutations) => {
		if (mutations.some((mutation) => mutation.type === "childList")) refreshResizeTargets();
		scheduleRefresh(true);
	});
	mutationObserver.observe(root, {
		attributes: true,
		characterData: true,
		childList: true,
		subtree: true
	});
	document.addEventListener("scroll", refresh, true);
	document.addEventListener("animationend", refresh, true);
	document.addEventListener("transitionend", refresh, true);
	view.addEventListener("resize", refresh);
	document.fonts?.addEventListener("loadingdone", refresh);
	emit();
	return {
		measure,
		refresh,
		disconnect: () => {
			if (disconnected) return;
			disconnected = true;
			mutationObserver.disconnect();
			resizeObserver?.disconnect();
			document.removeEventListener("scroll", refresh, true);
			document.removeEventListener("animationend", refresh, true);
			document.removeEventListener("transitionend", refresh, true);
			view.removeEventListener("resize", refresh);
			document.fonts?.removeEventListener("loadingdone", refresh);
			if (scheduledFrame !== null) {
				view.cancelAnimationFrame(scheduledFrame);
				scheduledFrame = null;
			}
		}
	};
}
function getOwnerDocument(root) {
	if (root.nodeType === Node.DOCUMENT_NODE) return root;
	if (!root.ownerDocument) throw new Error("Canvas geometry root must belong to a document.");
	return root.ownerDocument;
}
function collectResizeTargets(root, boundaries) {
	const targets = /* @__PURE__ */ new Set();
	if (root.nodeType === Node.ELEMENT_NODE) addObservableResizeTarget(targets, root);
	else if (root.nodeType === Node.DOCUMENT_NODE) {
		const rootDocument = root;
		targets.add(rootDocument.documentElement);
		if (rootDocument.body) targets.add(rootDocument.body);
	}
	boundaries.forEach((boundary) => {
		const parent = boundary.start.parentElement;
		if (parent && parent === boundary.end.parentElement) {
			addObservableResizeTarget(targets, parent);
			let node = boundary.start.nextSibling;
			while (node && node !== boundary.end) {
				if (node.nodeType === Node.ELEMENT_NODE) addObservableResizeTarget(targets, node);
				node = node.nextSibling;
			}
		}
	});
	return Array.from(targets);
}
/** Adds the first observable resize target at or above a rendered element. */
function addObservableResizeTarget(targets, element) {
	let target = element;
	while (target && !isElementObservable(target)) target = target.parentElement;
	if (target) targets.add(target);
}
function isElementObservable(element) {
	const view = element.ownerDocument.defaultView;
	if (!view || view.getComputedStyle(element).display === "contents") return false;
	const offsetWidth = "offsetWidth" in element && typeof element.offsetWidth === "number" ? element.offsetWidth : 0;
	const offsetHeight = "offsetHeight" in element && typeof element.offsetHeight === "number" ? element.offsetHeight : 0;
	return Boolean(offsetWidth || offsetHeight || element.getClientRects().length);
}
//#endregion
//#region src/client/draft-session.ts
/**
* @file
* The app's side of the draft session lifecycle, as a framework-free state
* machine. The consumer (a React component, a Svelte store, plain DOM code)
* owns presentation and data refreshing; this module owns timing, host
* messaging, and renewal.
*
* Renewal is a division of labor: the app knows *when* the token dies
* (tokenExpiresAt is right in the session cookie) but cannot mint a new
* assertion — only the editor's Drupal session can, and the app's requests
* never carry it. So, embedded, the app asks its host over postMessage
* before expiry; the host answers with a fresh assertion, the app redeems
* it at the renew endpoint (new token, same cookies), and the consumer's
* refreshData() re-renders with draft data — no document reload, no
* navigation loss. The editor never sees the seam.
*
* Two lanes, cleanly divided: *renewal* is proactive (before expiry, in
* place, invisible); *recovery* is reactive (after expiry, the host resets
* the iframe src — coarse but dependable). The app triggers recovery simply
* by reporting status "expired"; it never asks for renewal after expiry.
* The same origin-checked channel carries host refresh requests after Canvas
* persists new auto-save data; consumers refresh through their framework or
* reload the current document without replaying an activation assertion.
*
* A session epoch is immutable: a successful renewal produces a new
* tokenExpiresAt (via the refreshed server data), and the consumer destroys
* this machine and creates a fresh one for the new epoch. That replaces
* prop-driven state resets with a plain lifecycle, which is what keeps
* non-React consumers trivial.
*
* Messages are origin-checked in both directions against the exact editor
* origin carried by the redeemed assertion's signed renewal URL.
*
* The design record behind this protocol, in the Drupal Canvas repository:
* docs/adr/0015-headless-draft-preview-session-renewal-re-anchored-in-drupal-session.md.
*/
/**
* How long before token expiry the app asks its host for a fresh assertion.
* Comfortably more than one round trip (host mints, app redeems), small
* next to the 15-minute token life. Clamped to half the token's remaining
* life at scheduling time: with a site-configured TTL at or below the
* margin, a fixed 60 s lead would fire immediately on every activation —
* renew, refresh, renew again, a token-minting loop. The clamp turns that
* into renewal at half-life, which is merely frequent.
*/
const RENEW_MARGIN_MS = 6e4;
/**
* How long a requested renewal may go unanswered before it counts as
* failed; the recovery lane takes over at expiry.
*/
const RENEW_TIMEOUT_MS = 1e4;
const DEFAULT_RENEW_ENDPOINT = "/api/draft/renew";
/**
* Creates the app side of the renewal protocol for one session epoch.
*/
function createDraftSession(options) {
	const { tokenExpiresAt, initialExpired, embedded, editorOrigin, renewEndpoint = DEFAULT_RENEW_ENDPOINT, refreshData, onEvent, hostWindow = typeof window === "undefined" ? void 0 : window.parent, listenerTarget = typeof window === "undefined" ? void 0 : window, fetchImpl = typeof fetch === "undefined" ? void 0 : fetch } = options;
	let path = options.path;
	let expired = initialExpired;
	let renewState = "idle";
	let destroyed = false;
	let hostSessionId = null;
	let passive = false;
	const timers = /* @__PURE__ */ new Set();
	const emit = (event) => {
		if (!destroyed) onEvent?.(event);
	};
	const schedule = (callback, delay) => {
		const timer = setTimeout(() => {
			timers.delete(timer);
			if (!destroyed) callback();
		}, Math.max(delay, 0));
		timers.add(timer);
	};
	const postToHost = (message) => {
		if (editorOrigin) hostWindow?.postMessage(hostSessionId ? {
			...message,
			hostSessionId
		} : message, editorOrigin);
	};
	const reportStatus = () => {
		if (!embedded) return;
		postToHost({
			type: HEADLESS_STATUS_MESSAGE,
			status: expired ? "expired" : "active",
			path,
			tokenExpiresAt
		});
	};
	const expireIfDue = () => {
		if (expired || tokenExpiresAt === null || Date.now() < tokenExpiresAt - 5e3) return false;
		expired = true;
		emit({ type: "expired" });
		reportStatus();
		return true;
	};
	if (tokenExpiresAt !== null && !expired) schedule(() => {
		expireIfDue();
	}, tokenExpiresAt - EXPIRY_SLACK_MS - Date.now());
	if (embedded && !expired && tokenExpiresAt !== null) {
		const remaining = tokenExpiresAt - Date.now();
		schedule(() => {
			if (passive || expired || renewState !== "idle") return;
			if (expireIfDue()) return;
			renewState = "requested";
			emit({ type: "renew-requested" });
			postToHost({
				type: HEADLESS_RENEW_REQUEST_MESSAGE,
				path
			});
			schedule(() => {
				if (renewState === "requested") {
					renewState = "failed";
					emit({ type: "renew-failed" });
				}
			}, RENEW_TIMEOUT_MS);
		}, remaining - Math.min(RENEW_MARGIN_MS, remaining / 2));
	}
	const onMessage = embedded ? (event) => {
		if (event.source !== hostWindow || event.origin !== editorOrigin || !event.data || typeof event.data.type !== "string") return;
		if (event.data.type === "canvas-headless:status-request") {
			if (typeof event.data.hostSessionId === "string" && event.data.hostSessionId !== "") {
				hostSessionId = event.data.hostSessionId;
				passive = event.data.passive === true;
				reportStatus();
			}
			return;
		}
		if (hostSessionId === null || event.data.hostSessionId !== hostSessionId) return;
		if (event.data.type === "canvas-headless:refresh") {
			if (typeof event.data.refreshId === "number") postToHost({
				type: HEADLESS_REFRESH_ACK_MESSAGE,
				refreshId: event.data.refreshId
			});
			emit({ type: "refresh-requested" });
			refreshData?.();
			return;
		}
		if (event.data.type !== "canvas-headless:assertion" || typeof event.data.assertion !== "string") return;
		fetchImpl?.(renewEndpoint, {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify({ assertion: event.data.assertion })
		}).then(async (response) => {
			if (destroyed) return;
			if (response.ok) {
				const body = await response.json().catch(() => null);
				if (destroyed) return;
				emit({
					type: "renewed",
					tokenExpiresAt: typeof body?.tokenExpiresAt === "number" ? body.tokenExpiresAt : null
				});
				refreshData?.();
			} else {
				renewState = "failed";
				emit({ type: "renew-failed" });
			}
		}, () => {
			if (!destroyed) {
				renewState = "failed";
				emit({ type: "renew-failed" });
			}
		});
	} : null;
	if (onMessage) listenerTarget?.addEventListener("message", onMessage);
	reportStatus();
	return {
		getState: () => ({
			expired,
			renewState
		}),
		setPath: (nextPath) => {
			path = nextPath;
			reportStatus();
		},
		destroy: () => {
			destroyed = true;
			for (const timer of timers) clearTimeout(timer);
			timers.clear();
			if (onMessage) listenerTarget?.removeEventListener("message", onMessage);
		}
	};
}
//#endregion
//#region src/client/geometry-bridge.ts
/**
* App-side bridge between shared Canvas geometry measurement and its editor
* host. Geometry remains in iframe viewport CSS pixels; the host owns all
* coordinate conversion.
*/
/** Starts measurement only after an origin- and source-checked host request. */
function createCanvasGeometryBridge(options) {
	const { editorOrigin, root = document, hostWindow = window.parent, listenerTarget = window } = options;
	let observer = null;
	let hostSessionId = null;
	let destroyed = false;
	const postGeometry = (geometry) => {
		if (!destroyed) hostWindow.postMessage({
			type: HEADLESS_GEOMETRY_MESSAGE,
			geometry,
			hostSessionId
		}, editorOrigin);
	};
	const onMessage = (event) => {
		if (destroyed || event.source !== hostWindow || event.origin !== editorOrigin || !event.data) return;
		if (event.data.type !== "canvas-headless:geometry-request" || typeof event.data.hostSessionId !== "string" || event.data.hostSessionId === "") return;
		hostSessionId = event.data.hostSessionId;
		if (observer) postGeometry(observer.measure());
		else observer = createCanvasGeometryObserver({
			root,
			onChange: postGeometry
		});
	};
	listenerTarget.addEventListener("message", onMessage);
	return { destroy: () => {
		destroyed = true;
		observer?.disconnect();
		listenerTarget.removeEventListener("message", onMessage);
	} };
}
//#endregion
//#region ../height-reader/src/stable-height.ts
/** Matches the inline pin Canvas applies once a viewport-relative height settles. */
const STABLE_HEIGHT_ATTRIBUTE = "data-canvas-preview-max-height";
const DEFAULT_PROBE_MULTIPLIERS = [3, 8];
function snapshotStyles(elements) {
	return elements.map((element) => ({
		element,
		height: element.style.getPropertyValue("height"),
		heightPriority: element.style.getPropertyPriority("height"),
		minHeight: element.style.getPropertyValue("min-height"),
		minHeightPriority: element.style.getPropertyPriority("min-height"),
		maxHeight: element.style.getPropertyValue("max-height"),
		maxHeightPriority: element.style.getPropertyPriority("max-height"),
		stableHeight: element.getAttribute(STABLE_HEIGHT_ATTRIBUTE)
	}));
}
function resetRootHeights(elements) {
	for (const element of elements) {
		element.style.setProperty("height", "auto", "important");
		element.style.setProperty("min-height", "0px", "important");
	}
}
function restore(snapshots) {
	for (const { element, height, heightPriority, minHeight, minHeightPriority, maxHeight, maxHeightPriority, stableHeight } of snapshots) {
		if (height) element.style.setProperty("height", height, heightPriority);
		else element.style.removeProperty("height");
		if (minHeight) element.style.setProperty("min-height", minHeight, minHeightPriority);
		else element.style.removeProperty("min-height");
		if (maxHeight) element.style.setProperty("max-height", maxHeight, maxHeightPriority);
		else element.style.removeProperty("max-height");
		if (stableHeight === null) element.removeAttribute(STABLE_HEIGHT_ATTRIBUTE);
		else element.setAttribute(STABLE_HEIGHT_ATTRIBUTE, stableHeight);
	}
}
function getElementSignature(element) {
	return [
		element.tagName,
		element.id,
		element.getAttribute("data-div") ?? "",
		element.getAttribute("data-testid") ?? "",
		getClassNameString(element),
		element.getAttribute("style") ?? ""
	].join("|");
}
function collectElementsUnderRoots(roots) {
	const elements = [];
	const seen = /* @__PURE__ */ new Set();
	for (const root of roots) {
		if (root.nodeType !== Node.ELEMENT_NODE || seen.has(root)) continue;
		seen.add(root);
		elements.push(root);
		root.querySelectorAll("*").forEach((element) => {
			if (!seen.has(element)) {
				seen.add(element);
				elements.push(element);
			}
		});
	}
	return elements;
}
function isHeightExplicitlyConstrained(element) {
	const baseline = element.clientHeight;
	const originalStyle = element.getAttribute("style");
	element.style.setProperty("height", "auto", "important");
	element.offsetHeight;
	const withAutoHeight = element.clientHeight;
	if (originalStyle === null) element.removeAttribute("style");
	else element.setAttribute("style", originalStyle);
	element.offsetHeight;
	return Math.abs(withAutoHeight - baseline) > 2;
}
function usesViewportHeightProperty(element, _effectiveViewportHeight) {
	return isHeightExplicitlyConstrained(element);
}
function applyStableHeight(element, entry) {
	const pixelHeight = `${entry.maxHeight}px`;
	element.style.setProperty("min-height", pixelHeight, "important");
	const naturalHeight = element.clientHeight;
	if (entry.shouldCapMaxHeight) {
		element.style.setProperty("height", pixelHeight, "important");
		element.style.setProperty("max-height", pixelHeight, "important");
	} else {
		element.style.removeProperty("height");
		if (naturalHeight > entry.maxHeight + 2) element.style.removeProperty("max-height");
		else element.style.setProperty("max-height", pixelHeight, "important");
	}
	element.setAttribute(STABLE_HEIGHT_ATTRIBUTE, `${entry.maxHeight}`);
}
var StableHeightReader = class {
	#elementCache = /* @__PURE__ */ new WeakMap();
	#signatureCache = /* @__PURE__ */ new Map();
	#pinSnapshots = /* @__PURE__ */ new Map();
	clear() {
		restore([...this.#pinSnapshots.values()]);
		this.#elementCache = /* @__PURE__ */ new WeakMap();
		this.#signatureCache.clear();
		this.#pinSnapshots.clear();
	}
	#getCachedEntry(element) {
		return this.#elementCache.get(element) ?? this.#signatureCache.get(getElementSignature(element));
	}
	async stabilize(options) {
		const { roots, effectiveViewportHeight } = options;
		if (roots.length === 0) return {
			pinnedElements: /* @__PURE__ */ new Set(),
			didProbe: false
		};
		const pinnedElements = /* @__PURE__ */ new Set();
		const candidates = [];
		for (const element of collectElementsUnderRoots(roots)) {
			const signature = getElementSignature(element);
			const cached = this.#getCachedEntry(element);
			if (cached) {
				applyStableHeight(element, cached);
				pinnedElements.add(element);
				continue;
			}
			if (isVhMeasurementCandidate(element, effectiveViewportHeight)) candidates.push({
				element,
				signature
			});
		}
		if (candidates.length === 0) return {
			pinnedElements,
			didProbe: false
		};
		const baseViewportHeight = options.baseViewportHeight ?? effectiveViewportHeight;
		const canPinNewElements = options.shouldPinNewElements?.() ?? true;
		if (options.probeController && baseViewportHeight > 0) {
			try {
				const confirmed = await this.#confirmByProbe(candidates, baseViewportHeight, options.probeController, options.probeMultipliers ?? DEFAULT_PROBE_MULTIPLIERS);
				if (canPinNewElements) for (const [candidate, maxHeight] of confirmed) this.#pinElement(candidate, maxHeight, pinnedElements);
			} finally {
				await options.probeController.restoreViewportHeight();
			}
			return {
				pinnedElements,
				didProbe: true
			};
		}
		return {
			pinnedElements,
			didProbe: false
		};
	}
	async measureDocumentHeight(document, options = {}) {
		const { body, documentElement } = document;
		const effectiveViewportHeight = document.defaultView?.innerHeight ?? documentElement.clientHeight;
		const rootElements = [documentElement, body].filter((element) => element instanceof HTMLElement);
		await this.stabilize({
			...options,
			roots: [documentElement],
			effectiveViewportHeight
		});
		const snapshots = snapshotStyles(rootElements);
		try {
			resetRootHeights(rootElements);
			return documentElement.offsetHeight;
		} finally {
			restore(snapshots);
		}
	}
	async #confirmByProbe(candidates, baseViewportHeight, probeController, probeMultipliers) {
		const inferredHeights = /* @__PURE__ */ new WeakMap();
		for (const multiplier of probeMultipliers) {
			await probeController.setViewportHeight(baseViewportHeight * multiplier);
			for (const { element } of candidates) {
				if (element.clientHeight <= 10) continue;
				const heights = inferredHeights.get(element) ?? [];
				heights.push(Math.floor(element.clientHeight / multiplier));
				inferredHeights.set(element, heights);
			}
		}
		const confirmed = /* @__PURE__ */ new Map();
		for (const candidate of candidates) {
			const heights = inferredHeights.get(candidate.element) ?? [];
			if (heights.length === probeMultipliers.length && heights.every((height) => height === heights[0]) && heights[0] > 0) confirmed.set(candidate, heights[0]);
		}
		return confirmed;
	}
	#pinElement(candidate, maxHeight, pinnedElements) {
		const { element, signature } = candidate;
		const entry = {
			maxHeight,
			shouldCapMaxHeight: usesViewportHeightProperty(element)
		};
		if (!this.#pinSnapshots.has(element)) {
			const [snapshot] = snapshotStyles([element]);
			this.#pinSnapshots.set(element, snapshot);
		}
		applyStableHeight(element, entry);
		this.#elementCache.set(element, entry);
		this.#signatureCache.set(signature, entry);
		pinnedElements.add(element);
	}
};
//#endregion
//#region ../height-reader/src/vh-detection.ts
/**
* @file
* Heuristics for finding elements whose height is driven by viewport-relative
* CSS (vh units, Tailwind's h-screen/min-h-screen/min-h-*, or an inline vh
* style). These resolve against the current viewport height — inside a preview
* iframe, the height the host last set. Measuring them, resizing the iframe to
* fit, and re-measuring feeds back: the element grows because the iframe grew.
* Detecting them lets callers neutralize them before measuring.
*/
/**
* HTML elements expose className as a string; SVG elements use SVGAnimatedString.
*/
function getClassNameString(element) {
	const cn = element.className;
	if (typeof cn === "string") return cn;
	if (cn && typeof cn === "object" && "baseVal" in cn && typeof cn.baseVal === "string") return cn.baseVal;
	return element.getAttribute("class") ?? "";
}
/**
* Matches Tailwind height utilities that resolve against the viewport
* (h-screen/dvh/svh/lvh, and their min-/max- variants, including arbitrary
* values like h-[100vh]). Deliberately excludes plain min-h-<number>
* utilities (e.g. min-h-20, min-h-96) — those are fixed sizes, not
* viewport-relative, and matching them as a bare "min-h-" substring was
* flagging unrelated elements for neutralization during measurement.
*/
const VIEWPORT_HEIGHT_CLASS_TOKEN = /^(?:min-|max-)?h-(?:screen|dvh|svh|lvh|\[[^\]]*\d(?:d|s|l)?vh[^\]]*\])$/;
function looksLikeVhClassOrInline(element) {
	const cls = getClassNameString(element);
	const styleAttr = element.getAttribute("style");
	return cls.split(/\s+/).some((token) => VIEWPORT_HEIGHT_CLASS_TOKEN.test(token)) || styleAttr != null && /\d(?:d|s|l)?vh\b/.test(styleAttr);
}
function approximatelyEquals(a, b) {
	return Math.abs(a - b) <= 2;
}
/**
* Catches CSS-file vh rules (e.g. a stylesheet with no Tailwind-style class
* markers) by comparing computed height/min-height against the viewport
* height and half the viewport height (for 50vh-style rules). Heights above
* the viewport are also candidates: a probe rejects fixed or content-driven
* boxes while allowing stylesheet rules such as `height: 150vh` to be found.
*/
function cssMatchesViewportHeuristic(element, effectiveViewportHeight) {
	const win = element.ownerDocument.defaultView;
	if (!win) return false;
	const computedStyle = win.getComputedStyle(element);
	const minHeight = parseFloat(computedStyle.minHeight);
	const height = parseFloat(computedStyle.height);
	if (Number.isFinite(minHeight) && minHeight > effectiveViewportHeight + 2 || Number.isFinite(height) && height > effectiveViewportHeight + 2) return true;
	const targets = [effectiveViewportHeight, effectiveViewportHeight / 2];
	for (const target of targets) {
		if (Number.isFinite(minHeight) && approximatelyEquals(minHeight, target)) return true;
		if (Number.isFinite(height) && approximatelyEquals(height, target)) return true;
	}
	return false;
}
/**
* Whether element's height is likely driven by viewport-relative CSS.
* html/body are excluded: callers that need to neutralize those two
* elements specifically already do so unconditionally.
*/
function isVhMeasurementCandidate(element, effectiveViewportHeight) {
	if (["HTML", "BODY"].includes(element.tagName)) return false;
	if (looksLikeVhClassOrInline(element)) return true;
	if (element.hasAttribute("data-canvas-preview-max-height")) return true;
	return cssMatchesViewportHeuristic(element, effectiveViewportHeight);
}
//#endregion
//#region src/client/height-report.ts
/**
* @file
* The app's side of content-height reporting. It tells the editing host how
* tall the embedded app's rendered content currently is, so the host can size
* the preview iframe to fit.
* The app reports final heights and uses a short request/acknowledgement
* exchange when the shared reader needs temporary viewport heights.
*
* Both a ResizeObserver and a MutationObserver are used: viewport-relative
* CSS (h-full, min-h-screen, vh units) can pin an element's rendered box to
* whatever height the host last applied, so content growing past it changes
* scrollHeight without firing ResizeObserver. MutationObserver on the
* subtree catches that case.
*/
const PROBE_TIMEOUT_MS = 2e3;
/** Creates the app side of document-height reporting. */
function createHeightReporter(options) {
	const { editorOrigin, embedded, hostWindow = typeof window === "undefined" ? void 0 : window.parent } = options;
	const target = typeof document === "undefined" ? void 0 : document.documentElement;
	if (!embedded || !target || typeof ResizeObserver === "undefined") return { destroy: () => {} };
	const resolvedTarget = target;
	const resolvedDocument = resolvedTarget.ownerDocument;
	const resolvedWindow = resolvedDocument.defaultView;
	const stableHeightReader = new StableHeightReader();
	let baseViewportHeight = resolvedWindow?.innerHeight ?? resolvedTarget.clientHeight;
	let lastObservedRootHeight = resolvedTarget.offsetHeight;
	let hostSessionId = null;
	let passive = false;
	let destroyed = false;
	let measuring = false;
	let measureAgain = false;
	let probeActive = false;
	let probeReleaseFrame = null;
	let nextProbeId = 0;
	const pendingProbes = /* @__PURE__ */ new Map();
	const mutationOptions = {
		childList: true,
		subtree: true,
		attributes: true,
		characterData: true
	};
	const mutationObserver = new MutationObserver(scheduleHeight);
	const resizeObserver = new ResizeObserver(() => {
		if (probeActive) return;
		const rootHeight = resolvedTarget.offsetHeight;
		if (rootHeight === lastObservedRootHeight) return;
		lastObservedRootHeight = rootHeight;
		scheduleHeight();
	});
	function releaseProbeAfterLayout() {
		if (!resolvedWindow || typeof resolvedWindow.requestAnimationFrame !== "function") {
			probeActive = false;
			return;
		}
		if (probeReleaseFrame !== null) resolvedWindow.cancelAnimationFrame(probeReleaseFrame);
		probeReleaseFrame = resolvedWindow.requestAnimationFrame(() => {
			probeReleaseFrame = null;
			probeActive = false;
		});
	}
	function requestProbeHeight(height) {
		return new Promise((resolve, reject) => {
			if (!editorOrigin || !hostWindow || !hostSessionId || passive || destroyed) {
				reject(/* @__PURE__ */ new Error("The height probe host is unavailable."));
				return;
			}
			const probeSessionId = hostSessionId;
			const id = `height-probe-${++nextProbeId}`;
			const timeout = resolvedWindow?.setTimeout(() => {
				pendingProbes.delete(id);
				if (height === null) releaseProbeAfterLayout();
				reject(/* @__PURE__ */ new Error("The height probe host did not respond."));
			}, PROBE_TIMEOUT_MS);
			if (timeout === void 0) {
				reject(/* @__PURE__ */ new Error("The height probe window is unavailable."));
				return;
			}
			if (height !== null) {
				if (probeReleaseFrame !== null) {
					resolvedWindow?.cancelAnimationFrame(probeReleaseFrame);
					probeReleaseFrame = null;
				}
				probeActive = true;
			}
			pendingProbes.set(id, {
				resolve,
				reject,
				restoresViewport: height === null,
				timeout
			});
			hostWindow.postMessage({
				type: HEADLESS_HEIGHT_PROBE_MESSAGE,
				hostSessionId: probeSessionId,
				id,
				height
			}, editorOrigin);
		});
	}
	function handleHostMessage(event) {
		if (event.origin !== editorOrigin || event.source !== hostWindow) return;
		if (event.data?.type === "canvas-headless:status-request" && typeof event.data.hostSessionId === "string") {
			hostSessionId = event.data.hostSessionId;
			passive = event.data.passive === true;
			stableHeightReader.clear();
			if (!passive) scheduleHeight();
			return;
		}
		if (hostSessionId === null || event.data?.hostSessionId !== hostSessionId) return;
		if (event.data.type === "canvas-headless:viewport-height") {
			const { height } = event.data;
			if (typeof height === "number" && Number.isFinite(height) && height > 0 && height !== baseViewportHeight) {
				baseViewportHeight = height;
				stableHeightReader.clear();
				scheduleHeight();
			}
			return;
		}
		if (event.data?.type !== "canvas-headless:height-probe-ready" || typeof event.data.id !== "string") return;
		const pending = pendingProbes.get(event.data.id);
		if (!pending) return;
		pendingProbes.delete(event.data.id);
		resolvedWindow?.clearTimeout(pending.timeout);
		if (pending.restoresViewport) releaseProbeAfterLayout();
		pending.resolve();
	}
	async function measureAndPostHeight() {
		if (!editorOrigin || !hostSessionId || passive || destroyed) return;
		const measurementSessionId = hostSessionId;
		if (measuring) {
			measureAgain = true;
			return;
		}
		measuring = true;
		try {
			do {
				measureAgain = false;
				mutationObserver.disconnect();
				try {
					const height = await stableHeightReader.measureDocumentHeight(resolvedDocument, {
						baseViewportHeight,
						probeController: {
							setViewportHeight: requestProbeHeight,
							restoreViewportHeight: () => requestProbeHeight(null)
						}
					});
					lastObservedRootHeight = resolvedTarget.offsetHeight;
					if (!destroyed && !passive && measurementSessionId === hostSessionId) hostWindow?.postMessage({
						type: HEADLESS_HEIGHT_MESSAGE,
						hostSessionId: measurementSessionId,
						height
					}, editorOrigin);
				} catch {
					const height = await stableHeightReader.measureDocumentHeight(resolvedDocument);
					lastObservedRootHeight = resolvedTarget.offsetHeight;
					if (!destroyed && !passive && measurementSessionId === hostSessionId) hostWindow?.postMessage({
						type: HEADLESS_HEIGHT_MESSAGE,
						hostSessionId: measurementSessionId,
						height
					}, editorOrigin);
				} finally {
					if (!destroyed) mutationObserver.observe(resolvedTarget, mutationOptions);
				}
			} while (measureAgain && !destroyed);
		} finally {
			measuring = false;
		}
	}
	function scheduleHeight() {
		measureAndPostHeight();
	}
	resolvedWindow?.addEventListener("message", handleHostMessage);
	resizeObserver.observe(target);
	mutationObserver.observe(target, mutationOptions);
	scheduleHeight();
	return { destroy: () => {
		if (destroyed) return;
		if (probeActive && editorOrigin && hostSessionId) hostWindow?.postMessage({
			type: HEADLESS_HEIGHT_PROBE_MESSAGE,
			hostSessionId,
			id: `height-probe-${++nextProbeId}`,
			height: null
		}, editorOrigin);
		destroyed = true;
		probeActive = false;
		if (probeReleaseFrame !== null) {
			resolvedWindow?.cancelAnimationFrame(probeReleaseFrame);
			probeReleaseFrame = null;
		}
		resizeObserver.disconnect();
		mutationObserver.disconnect();
		resolvedWindow?.removeEventListener("message", handleHostMessage);
		for (const pending of pendingProbes.values()) {
			resolvedWindow?.clearTimeout(pending.timeout);
			pending.reject(/* @__PURE__ */ new Error("The height reporter was destroyed."));
		}
		pendingProbes.clear();
		stableHeightReader.clear();
	} };
}
//#endregion
//#region src/client/draft-session-element.ts
/**
* @file
* `<canvas-draft-session>`: the draft session lifecycle as a framework-free
* custom element, for consumers without a component runtime of their own
* (Astro, Nuxt without a session store, plain server-rendered pages). It
* wraps the state machine in ./draft-session the way the React
* `<DraftSession>` in @drupal-canvas/headless-react does, with the DOM as
* the presentation contract instead of a render prop:
*
* - The element owns the machine lifecycle: one machine per session epoch,
*   re-created in place when a renewal delivers a new tokenExpiresAt (the
*   'renewed' event carries it, so no server re-render is needed).
* - A host refresh request reloads the current document so server-rendered
*   adapters fetch the latest Canvas auto-save data. Before reloading, the
*   element emits a cancelable refresh event so framework adapters can use
*   their own data-refresh or navigation primitive instead.
* - Session state is reflected as host attributes (`expired`, `embedded`,
*   `renew-state`) and announced via a bubbling
*   `canvas-draft-session:change` CustomEvent, for consumers that want to
*   drive their own presentation.
* - Children opt into the protocol's intended visibility rules by marking
*   themselves: `data-draft-session-view="active"` renders only while the
*   session is live *and* the page is standalone (embedded, the host owns
*   the session chrome); `data-draft-session-view="expired"` renders once
*   the session has expired, embedded or not — expiry going invisible
*   inside an iframe is the failure mode the renewal protocol exists for,
*   so the expired view is the last-resort fallback for a host that does
*   not speak it. A `data-draft-session-renew-link` element gets its href
*   pointed at the renew URL with the current path, and is hidden when
*   embedded (the link is a top-level navigation through Drupal, which
*   makes no sense inside the frame) or when there is no renew URL.
*
* The element reads its configuration attributes once, when connected: it
* serves server-rendered multi-page documents, where a new navigation is a
* new document and a fresh element. The one exception is `path`, which is
* observed: a client-routed app (Nuxt, a Next.js page transition) keeps
* the element alive across navigations, and the host must hear about the
* current path — status reports and the renew link both carry it. Without
* the attribute the path is read from window.location at connect time.
*
* Alongside the session machine, the element also runs a content-height
* reporter (./height-report) for the same `editor-origin`: an independent
* exchange that lets the host size the preview iframe to fit.
*/
const DRAFT_SESSION_ELEMENT_TAG = "canvas-draft-session";
/**
* The event name state changes are announced under (bubbling, composed).
*/
const DRAFT_SESSION_CHANGE_EVENT = "canvas-draft-session:change";
/**
* Cancelable event emitted when the host reports new Canvas auto-save data.
* Preventing its default behavior tells the element that an adapter owns the
* refresh; otherwise the current document reloads.
*/
const DRAFT_SESSION_REFRESH_EVENT = "canvas-draft-session:refresh-requested";
const BaseElement = typeof HTMLElement === "undefined" ? class {} : HTMLElement;
var DraftSessionElement = class extends BaseElement {
	static observedAttributes = ["path"];
	#machine = null;
	#geometryBridge = null;
	#heightReporter = null;
	#connected = false;
	#tokenExpiresAt = null;
	#expired = false;
	#embedded = false;
	#renewUrl = null;
	#path = "/";
	connectedCallback() {
		const expiresAttribute = this.getAttribute("token-expires-at");
		const parsedExpiry = expiresAttribute === null ? NaN : +expiresAttribute;
		this.#tokenExpiresAt = Number.isFinite(parsedExpiry) ? parsedExpiry : null;
		this.#expired = this.hasAttribute("initial-expired") && this.getAttribute("initial-expired") !== "false";
		this.#renewUrl = this.getAttribute("renew-url");
		this.#embedded = window.self !== window.top;
		this.#path = this.getAttribute("path") ?? window.location.pathname;
		this.#connected = true;
		this.#startEpoch();
		const editorOrigin = this.getAttribute("editor-origin");
		this.#heightReporter = createHeightReporter({
			editorOrigin,
			embedded: this.#embedded
		});
		if (this.#embedded && editorOrigin) this.#geometryBridge = createCanvasGeometryBridge({ editorOrigin });
		this.#render();
	}
	disconnectedCallback() {
		this.#connected = false;
		this.#machine?.destroy();
		this.#machine = null;
		this.#heightReporter?.destroy();
		this.#heightReporter = null;
		this.#geometryBridge?.destroy();
		this.#geometryBridge = null;
	}
	attributeChangedCallback(name, _oldValue, newValue) {
		if (!this.#connected || name !== "path") return;
		const path = newValue ?? window.location.pathname;
		if (path === this.#path) return;
		this.#path = path;
		this.#machine?.setPath(path);
		this.#render();
	}
	/**
	* Creates the machine for the current epoch from the element's state.
	*/
	#startEpoch() {
		this.#machine?.destroy();
		this.#machine = createDraftSession({
			tokenExpiresAt: this.#tokenExpiresAt,
			initialExpired: this.#expired,
			embedded: this.#embedded,
			path: this.#path,
			editorOrigin: this.getAttribute("editor-origin"),
			renewEndpoint: this.getAttribute("renew-endpoint") ?? void 0,
			onEvent: (event) => {
				if (event.type === "refresh-requested") {
					const refreshEvent = new Event(DRAFT_SESSION_REFRESH_EVENT, {
						bubbles: true,
						cancelable: true,
						composed: true
					});
					if (this.dispatchEvent(refreshEvent)) window.location.reload();
					return;
				}
				if (event.type === "renewed") {
					if (event.tokenExpiresAt === null) {
						window.location.reload();
						return;
					}
					this.#tokenExpiresAt = event.tokenExpiresAt;
					this.#expired = false;
					this.#startEpoch();
					this.#render();
					return;
				}
				this.#expired = this.#machine?.getState().expired ?? this.#expired;
				this.#render();
			}
		});
	}
	/**
	* Reflects the current state onto the host and the marked children, and
	* announces it.
	*/
	#render() {
		const renewState = this.#machine?.getState().renewState ?? "idle";
		this.toggleAttribute("expired", this.#expired);
		this.toggleAttribute("embedded", this.#embedded);
		this.setAttribute("renew-state", renewState);
		for (const view of this.querySelectorAll("[data-draft-session-view]")) {
			const name = view.getAttribute("data-draft-session-view");
			view.hidden = !(name === "expired" ? this.#expired : name === "active" ? !this.#expired && !this.#embedded : false);
		}
		for (const link of this.querySelectorAll("[data-draft-session-renew-link]")) {
			if (this.#embedded || !this.#renewUrl) {
				link.hidden = true;
				continue;
			}
			link.hidden = false;
			link.href = `${this.#renewUrl}?path=${encodeURIComponent(this.#path)}`;
		}
		this.dispatchEvent(new CustomEvent(DRAFT_SESSION_CHANGE_EVENT, {
			bubbles: true,
			composed: true,
			detail: {
				embedded: this.#embedded,
				expired: this.#expired,
				renewState,
				path: this.#path,
				renewUrl: this.#renewUrl,
				tokenExpiresAt: this.#tokenExpiresAt
			}
		}));
	}
};
/**
* Registers the element under its canonical tag name. Safe to call more
* than once (bundled twice, imported by two islands): an existing
* registration wins.
*/
function defineDraftSessionElement() {
	if (!customElements.get("canvas-draft-session")) customElements.define(DRAFT_SESSION_ELEMENT_TAG, DraftSessionElement);
}
//#endregion
//#region src/client/refresh-queue.ts
/**
* Serializes framework data refreshes and coalesces requests received while
* one is running. The queued pass is important for Canvas: a newer auto-save
* can arrive while a framework is still rendering the previous one.
*/
function createAsyncRefreshQueue(refresh, onError) {
	let pending = false;
	let active = null;
	const drain = async () => {
		try {
			do {
				pending = false;
				try {
					await refresh();
				} catch (error) {
					onError(error);
				}
			} while (pending);
		} finally {
			active = null;
		}
	};
	return { request: () => {
		pending = true;
		active ??= drain();
		return active;
	} };
}
//#endregion
export { DRAFT_SESSION_CHANGE_EVENT, DRAFT_SESSION_ELEMENT_TAG, DRAFT_SESSION_REFRESH_EVENT, DraftSessionElement, RENEW_MARGIN_MS, RENEW_TIMEOUT_MS, createAsyncRefreshQueue, createCanvasGeometryBridge, createCanvasGeometryObserver, createDraftSession, createHeightReporter, defineDraftSessionElement };
