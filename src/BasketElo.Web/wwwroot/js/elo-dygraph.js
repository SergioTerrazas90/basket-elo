(function () {
    "use strict";

    const states = new WeakMap();

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function formatDate(value) {
        return new Intl.DateTimeFormat(undefined, {
            month: "short",
            day: "numeric",
            year: "numeric"
        }).format(new Date(value));
    }

    function formatElo(value) {
        return Number(value).toLocaleString(undefined, {
            maximumFractionDigits: 0
        });
    }

    function formatMovement(value) {
        const number = Number(value);
        if (number > 0) {
            return `+${formatElo(number)}`;
        }

        return formatElo(number);
    }

    function waitForNextPaint() {
        return new Promise(resolve => {
            let completed = false;
            const finish = () => {
                if (completed) {
                    return;
                }

                completed = true;
                window.clearTimeout(fallbackTimer);
                resolve();
            };
            const fallbackTimer = window.setTimeout(finish, 500);
            window.requestAnimationFrame(() => window.requestAnimationFrame(finish));
        });
    }

    function ensureTooltip(host) {
        const tooltip = document.createElement("div");
        tooltip.className = "elo-dygraph-tooltip";
        tooltip.hidden = true;
        host.appendChild(tooltip);
        return tooltip;
    }

    function toRows(payload) {
        const rowMap = new Map();
        const metadata = [];

        payload.series.forEach((series, seriesIndex) => {
            series.points.forEach(point => {
                const time = Number(point.x);
                let row = rowMap.get(time);
                if (!row) {
                    row = {
                        time,
                        values: Array(payload.series.length).fill(null),
                        metadata: Array(payload.series.length).fill(null)
                    };
                    rowMap.set(time, row);
                }

                row.values[seriesIndex] = Number(point.y);
                row.metadata[seriesIndex] = {
                    x: time,
                    y: Number(point.y),
                    delta: point.delta == null ? null : Number(point.delta),
                    rank: point.rank == null ? null : Number(point.rank),
                    gameId: point.gameId == null ? null : String(point.gameId),
                    name: series.name,
                    color: series.color
                };
            });
        });

        const sortedRows = Array.from(rowMap.values()).sort((left, right) => left.time - right.time);
        sortedRows.forEach(row => metadata.push(row.metadata));

        return {
            rows: sortedRows.map(row => [new Date(row.time), ...row.values]),
            metadata,
            min: sortedRows[0]?.time ?? Date.now(),
            max: sortedRows.at(-1)?.time ?? Date.now()
        };
    }

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function selectedRange(state, minDate, maxDate) {
        const span = state.max - state.min;
        if (span <= 0) {
            return { start: 0, end: 1 };
        }

        return {
            start: clamp((minDate - state.min) / span, 0, 1),
            end: clamp((maxDate - state.min) / span, 0, 1)
        };
    }

    function notifyViewChange(state, minDate, maxDate) {
        const range = selectedRange(state, minDate, maxDate);
        if (state.lastNotifiedRange &&
            Math.abs(state.lastNotifiedRange.start - range.start) < 0.000001 &&
            Math.abs(state.lastNotifiedRange.end - range.end) < 0.000001) {
            return;
        }

        state.lastNotifiedRange = range;
        try {
            Promise.resolve(state.dotNet.invokeMethodAsync(
                "HandleDygraphDateRangeChange",
                minDate,
                maxDate))
                .catch(error => {
                    state.lastNotifiedRange = null;
                    console.error("Dygraphs viewport refinement failed.", error);
                });
        } catch (error) {
            state.lastNotifiedRange = null;
            console.error("Dygraphs viewport refinement failed.", error);
        }
    }

    function renderTooltip(state, event, points, row) {
        if (!state.tooltip) {
            return;
        }

        if (!points || points.length === 0 || row == null) {
            state.tooltip.hidden = true;
            return;
        }

        const validPoints = points.filter(point => point.yval != null && !Number.isNaN(point.yval));
        if (validPoints.length === 0) {
            state.tooltip.hidden = true;
            return;
        }

        const visiblePoints = state.sharedTooltip ? validPoints : [validPoints[0]];
        const rows = visiblePoints.map(point => {
            const seriesIndex = state.labels.indexOf(point.name) - 1;
            const meta = state.metadata[row]?.[seriesIndex];
            if (!meta) {
                return "";
            }

            const detail = [
                `<span>${formatDate(meta.x)}</span>`,
                `<span>${formatElo(meta.y)} ELO</span>`
            ];
            if (meta.delta != null) {
                detail.push(`<span>Change ${formatMovement(meta.delta)}</span>`);
            }
            if (meta.rank != null) {
                detail.push(`<span>Rank #${meta.rank}</span>`);
            }

            return `<div class="elo-dygraph-tooltip-series" style="--active-team-color: ${escapeHtml(meta.color)}"><span class="elo-chart-active-swatch"></span><strong>${escapeHtml(meta.name)}</strong></div><div class="elo-dygraph-tooltip-details">${detail.join("")}</div>`;
        }).filter(Boolean);

        if (rows.length === 0) {
            state.tooltip.hidden = true;
            return;
        }

        const first = visiblePoints[0];
        const firstSeriesIndex = state.labels.indexOf(first.name) - 1;
        const firstMeta = state.metadata[row]?.[firstSeriesIndex];
        state.tooltip.style.setProperty("--active-team-color", firstMeta?.color ?? "var(--color-accent)");
        state.tooltip.innerHTML = rows.join("");
        state.tooltip.hidden = false;

        const rect = state.host.getBoundingClientRect();
        const width = state.tooltip.offsetWidth || 210;
        const height = state.tooltip.offsetHeight || 80;
        const offsetX = event?.offsetX ?? first.canvasx ?? 0;
        const offsetY = event?.offsetY ?? first.canvasy ?? 0;
        state.tooltip.style.left = `${clamp(offsetX + 14, 8, Math.max(8, rect.width - width - 8))}px`;
        state.tooltip.style.top = `${clamp(offsetY - height - 12, 8, Math.max(8, rect.height - height - 8))}px`;
    }

    function notifyPointHover(state, points, row) {
        if (state.suppressHoverNotification || !state.dotNet) {
            return;
        }

        const validPoints = (points ?? []).filter(point => point.yval != null && !Number.isNaN(point.yval));
        const first = validPoints[0];
        const seriesIndex = first ? state.labels.indexOf(first.name) - 1 : -1;
        const metadata = row == null || seriesIndex < 0
            ? null
            : state.metadata[row]?.[seriesIndex];
        const gameId = metadata?.gameId ?? null;
        if (state.lastHoveredGameId === gameId) {
            return;
        }

        state.lastHoveredGameId = gameId;
        Promise.resolve(state.dotNet.invokeMethodAsync("HandleDygraphPointHover", gameId))
            .catch(error => console.debug("Dygraphs hover synchronization failed.", error));
    }

    function clearPointHover(state) {
        if (state.suppressHoverNotification || !state.dotNet || state.lastHoveredGameId == null) {
            return;
        }

        state.lastHoveredGameId = null;
        Promise.resolve(state.dotNet.invokeMethodAsync("HandleDygraphPointHover", null))
            .catch(error => console.debug("Dygraphs hover synchronization failed.", error));
    }

    function setHighlight(host, gameId) {
        const state = states.get(host);
        if (!state) {
            return;
        }

        const normalizedGameId = gameId == null || gameId === ""
            ? null
            : String(gameId).toLowerCase();
        if (!state.graph || normalizedGameId == null) {
            state.lastHoveredGameId = null;
            if (state.graph) {
                state.suppressHoverNotification = true;
                try {
                    state.graph.clearSelection();
                } finally {
                    state.suppressHoverNotification = false;
                }
            }
            return;
        }

        let match = null;
        for (let row = 0; row < state.metadata.length && match == null; row += 1) {
            const rowMetadata = state.metadata[row] ?? [];
            for (let seriesIndex = 0; seriesIndex < rowMetadata.length; seriesIndex += 1) {
                const metadata = rowMetadata[seriesIndex];
                if (metadata?.gameId?.toLowerCase() === normalizedGameId) {
                    match = {
                        row,
                        seriesName: state.labels[seriesIndex + 1]
                    };
                    break;
                }
            }
        }

        state.lastHoveredGameId = normalizedGameId;
        if (!match) {
            state.suppressHoverNotification = true;
            try {
                state.graph.clearSelection();
            } finally {
                state.suppressHoverNotification = false;
            }
            return;
        }

        state.suppressHoverNotification = true;
        try {
            state.graph.setSelection(match.row, match.seriesName, true);
        } finally {
            state.suppressHoverNotification = false;
        }
    }

    function disposeState(state) {
        state.wheelBindingCleanup?.();
        state.wheelBindingCleanup = null;
        state.selectionBindingCleanup?.();
        state.selectionBindingCleanup = null;
        if (state.wheelNotifyTimer != null) {
            window.clearTimeout(state.wheelNotifyTimer);
            state.wheelNotifyTimer = null;
        }
        state.rangeBindingCleanup?.();
        state.rangeBindingCleanup = null;
        state.resizeObserver?.disconnect();
        state.graph?.destroy();
        state.graph = null;
        state.host.replaceChildren();
        state.selectionOverlay = null;
    }

    function setDateWindow(state, requestedFrom, requestedTo, notify = false, markNotified = true) {
        if (!state.graph || !Number.isFinite(requestedFrom) || !Number.isFinite(requestedTo) || requestedTo <= requestedFrom) {
            return;
        }

        state.suppressCallbacks = true;
        try {
            state.graph.updateOptions({
                dateWindow: [requestedFrom, requestedTo],
                isZoomedIgnoreProgrammaticZoom: true
            });
            const [minDate, maxDate] = state.graph.xAxisRange();
            if (markNotified) {
                state.lastNotifiedRange = selectedRange(state, minDate, maxDate);
            }
            if (notify) {
                state.lastNotifiedRange = null;
                notifyViewChange(state, minDate, maxDate);
            }
        } finally {
            state.suppressCallbacks = false;
        }
    }

    function bindWheelZoom(state) {
        state.wheelBindingCleanup?.();

        const onWheel = event => {
            if (!state.graph || event.ctrlKey || event.metaKey || !(event.target instanceof HTMLCanvasElement)) {
                return;
            }

            if (event.target.classList.contains("dygraph-rangesel-fgcanvas") ||
                event.target.classList.contains("dygraph-rangesel-bgcanvas")) {
                return;
            }

            const area = state.graph.getArea();
            const hostRect = state.host.getBoundingClientRect();
            const x = event.clientX - hostRect.left;
            const y = event.clientY - hostRect.top;
            if (x < area.x || x > area.x + area.w || y < area.y || y > area.y + area.h) {
                return;
            }

            const [currentStart, currentEnd] = state.graph.xAxisRange();
            const [extremeStart, extremeEnd] = state.graph.xAxisExtremes();
            const fullSpan = extremeEnd - extremeStart;
            const currentSpan = currentEnd - currentStart;
            if (fullSpan <= 0 || currentSpan <= 0 || event.deltaY === 0) {
                return;
            }

            const deltaUnit = event.deltaMode === WheelEvent.DOM_DELTA_LINE
                ? 16
                : event.deltaMode === WheelEvent.DOM_DELTA_PAGE
                    ? Math.max(state.host.clientHeight, 1)
                    : 1;
            const deltaPixels = clamp(event.deltaY * deltaUnit, -240, 240);
            const zoomFactor = Math.exp(deltaPixels * 0.0015);
            const minimumSpan = Math.min(fullSpan, Math.max(60 * 60 * 1000, fullSpan / 100000));
            const requestedSpan = clamp(currentSpan * zoomFactor, minimumSpan, fullSpan);
            if (Math.abs(requestedSpan - currentSpan) < 0.5) {
                return;
            }

            const anchorFraction = clamp((x - area.x) / area.w, 0, 1);
            const anchorDate = currentStart + currentSpan * anchorFraction;
            let requestedStart = anchorDate - requestedSpan * anchorFraction;
            let requestedEnd = requestedStart + requestedSpan;
            if (requestedStart < extremeStart) {
                requestedEnd += extremeStart - requestedStart;
                requestedStart = extremeStart;
            }
            if (requestedEnd > extremeEnd) {
                requestedStart -= requestedEnd - extremeEnd;
                requestedEnd = extremeEnd;
            }
            requestedStart = Math.max(extremeStart, requestedStart);

            event.preventDefault();
            setDateWindow(state, requestedStart, requestedEnd, false, false);

            if (state.wheelNotifyTimer != null) {
                window.clearTimeout(state.wheelNotifyTimer);
            }
            state.wheelNotifyTimer = window.setTimeout(() => {
                state.wheelNotifyTimer = null;
                if (!state.graph) {
                    return;
                }

                const [minDate, maxDate] = state.graph.xAxisRange();
                state.lastNotifiedRange = null;
                notifyViewChange(state, minDate, maxDate);
            }, 180);
        };

        state.host.addEventListener("wheel", onWheel, { passive: false });
        state.wheelBindingCleanup = () => state.host.removeEventListener("wheel", onWheel);
    }

    function ensureSelectionOverlay(state) {
        if (state.selectionOverlay?.isConnected) {
            return state.selectionOverlay;
        }

        const overlay = document.createElement("div");
        overlay.className = "elo-dygraph-selection-window";
        overlay.hidden = true;
        overlay.style.position = "absolute";
        overlay.style.zIndex = "6";
        overlay.style.pointerEvents = "none";
        overlay.style.border = "1px solid rgba(36, 92, 122, 0.78)";
        overlay.style.background = "rgba(36, 92, 122, 0.16)";
        overlay.style.boxShadow = "inset 0 0 0 1px rgba(255, 255, 255, 0.28)";
        state.host.appendChild(overlay);
        state.selectionOverlay = overlay;
        return overlay;
    }

    function plotPointerPosition(state, event, area) {
        const hostRect = state.host.getBoundingClientRect();
        return {
            x: clamp(event.clientX - hostRect.left, area.x, area.x + area.w),
            y: clamp(event.clientY - hostRect.top, area.y, area.y + area.h)
        };
    }

    function bindPlotSelectionInteraction(state) {
        state.selectionBindingCleanup?.();

        const overlay = ensureSelectionOverlay(state);
        const removeDragListeners = () => {
            document.removeEventListener("pointermove", onDragMove, true);
            document.removeEventListener("pointerup", onDragEnd, true);
            document.removeEventListener("pointercancel", onDragCancel, true);
            document.removeEventListener("mousemove", onDragMove, true);
            document.removeEventListener("mouseup", onDragEnd, true);
        };

        const clearSelection = () => {
            state.selectionDrag = null;
            removeDragListeners();
            overlay.hidden = true;
            state.host.classList.remove("elo-dygraph-selecting");
            state.host.style.cursor = "";
            state.host.style.userSelect = "";
        };

        const updateSelection = event => {
            const drag = state.selectionDrag;
            if (!drag || !state.graph) {
                return;
            }

            if (drag.pointerId != null && event.pointerId == null) {
                return;
            }
            if (drag.pointerId != null && drag.pointerId !== event.pointerId) {
                return;
            }

            event.preventDefault?.();
            event.stopImmediatePropagation?.();
            const pointer = plotPointerPosition(state, event, drag.area);
            const left = Math.min(drag.startX, pointer.x);
            const right = Math.max(drag.startX, pointer.x);
            overlay.style.left = `${left}px`;
            overlay.style.top = `${drag.area.y}px`;
            overlay.style.width = `${right - left}px`;
            overlay.style.height = `${drag.area.h}px`;
            overlay.hidden = right - left < 1;
        };

        const onDragMove = event => updateSelection(event);

        const onDragEnd = event => {
            const drag = state.selectionDrag;
            if (!drag) {
                return;
            }

            if (drag.pointerId != null && event.pointerId == null) {
                return;
            }
            if (drag.pointerId != null && drag.pointerId !== event.pointerId) {
                return;
            }

            updateSelection(event);
            const pointer = plotPointerPosition(state, event, drag.area);
            const left = Math.min(drag.startX, pointer.x);
            const right = Math.max(drag.startX, pointer.x);
            const width = right - left;
            clearSelection();
            if (width < 8) {
                return;
            }

            const [currentStart, currentEnd] = state.graph.xAxisRange();
            const currentSpan = currentEnd - currentStart;
            if (currentSpan <= 0 || drag.area.w <= 0) {
                return;
            }

            const requestedStart = currentStart + ((left - drag.area.x) / drag.area.w) * currentSpan;
            const requestedEnd = currentStart + ((right - drag.area.x) / drag.area.w) * currentSpan;
            setDateWindow(state, requestedStart, requestedEnd, false, false);
            const [minDate, maxDate] = state.graph.xAxisRange();
            state.lastNotifiedRange = null;
            notifyViewChange(state, minDate, maxDate);
        };

        const onDragCancel = event => {
            const drag = state.selectionDrag;
            if (!drag) {
                return;
            }

            if (drag.pointerId != null && event.pointerId != null && drag.pointerId !== event.pointerId) {
                return;
            }

            clearSelection();
        };

        const startSelection = event => {
            if (!state.graph || !event.ctrlKey || event.button !== 0) {
                return;
            }

            if (state.selectionDrag) {
                event.preventDefault();
                event.stopImmediatePropagation();
                return;
            }

            const area = state.graph.getArea();
            const pointer = plotPointerPosition(state, event, area);
            const hostRect = state.host.getBoundingClientRect();
            const rawX = event.clientX - hostRect.left;
            const rawY = event.clientY - hostRect.top;
            if (rawX < area.x || rawX > area.x + area.w || rawY < area.y || rawY > area.y + area.h) {
                return;
            }

            event.preventDefault();
            event.stopImmediatePropagation();
            clearSelection();
            state.host.classList.add("elo-dygraph-selecting");
            state.host.style.cursor = "crosshair";
            state.host.style.userSelect = "none";
            state.selectionDrag = {
                pointerId: event.pointerId ?? null,
                area,
                startX: pointer.x
            };
            updateSelection(event);

            document.addEventListener("pointermove", onDragMove, { capture: true, passive: false });
            document.addEventListener("pointerup", onDragEnd, true);
            document.addEventListener("pointercancel", onDragCancel, true);
            document.addEventListener("mousemove", onDragMove, { capture: true, passive: false });
            document.addEventListener("mouseup", onDragEnd, true);
        };

        state.host.addEventListener("pointerdown", startSelection, true);
        state.host.addEventListener("mousedown", startSelection, true);
        state.selectionBindingCleanup = () => {
            state.host.removeEventListener("pointerdown", startSelection, true);
            state.host.removeEventListener("mousedown", startSelection, true);
            clearSelection();
            state.host.style.cursor = "";
            state.host.style.userSelect = "";
        };
    }

    function bindRangeSelectorInteraction(state) {
        const handles = state.host.querySelectorAll("img.dygraph-rangesel-zoomhandle");
        const foregroundCanvas = state.host.querySelector("canvas.dygraph-rangesel-fgcanvas");
        if (handles.length < 2 || !foregroundCanvas) {
            return;
        }

        state.rangeBindingCleanup?.();

        handles.forEach(handle => {
            handle.draggable = false;
            handle.style.touchAction = "none";
        });
        foregroundCanvas.style.touchAction = "none";

        const selectorGeometry = () => {
            const rect = foregroundCanvas.getBoundingClientRect();
            const positions = Array.from(handles)
                .map(handle => {
                    const handleRect = handle.getBoundingClientRect();
                    return handleRect.left + handleRect.width / 2;
                })
                .sort((left, right) => left - right);

            return {
                rect,
                leftPosition: positions[0],
                rightPosition: positions.at(-1)
            };
        };

        const toDateWindow = (leftPosition, rightPosition, rect) => {
            const extremes = state.graph.xAxisExtremes();
            const span = extremes[1] - extremes[0];
            return [
                extremes[0] + ((leftPosition - rect.left) / rect.width) * span,
                extremes[0] + ((rightPosition - rect.left) / rect.width) * span
            ];
        };

        const stopSelectorEvent = event => {
            event.preventDefault?.();
            event.stopImmediatePropagation?.();
        };

        const removeDragListeners = () => {
            document.removeEventListener("pointermove", onDragMove, true);
            document.removeEventListener("pointerup", onDragEnd, true);
            document.removeEventListener("pointercancel", onDragEnd, true);
            document.removeEventListener("mousemove", onDragMove, true);
            document.removeEventListener("mouseup", onDragEnd, true);
        };

        const onDragMove = event => {
            const drag = state.rangeDrag;
            if (!drag) {
                return;
            }

            if (drag.pointerId != null && event.pointerId == null) {
                return;
            }
            if (drag.pointerId != null && drag.pointerId !== event.pointerId) {
                return;
            }

            stopSelectorEvent(event);

            const pointer = clamp(event.clientX, drag.rect.left, drag.rect.right);
            const minimumGap = 12;
            let leftPosition = drag.leftPosition;
            let rightPosition = drag.rightPosition;
            if (drag.mode === "left") {
                leftPosition = Math.min(pointer, rightPosition - minimumGap);
                leftPosition = Math.max(leftPosition, drag.rect.left);
            } else if (drag.mode === "right") {
                rightPosition = Math.max(pointer, leftPosition + minimumGap);
                rightPosition = Math.min(rightPosition, drag.rect.right);
            } else {
                const width = rightPosition - leftPosition;
                const requestedLeft = leftPosition + (pointer - drag.startPointer);
                leftPosition = clamp(requestedLeft, drag.rect.left, drag.rect.right - width);
                rightPosition = leftPosition + width;
            }

            const [from, to] = toDateWindow(leftPosition, rightPosition, drag.rect);
            setDateWindow(state, from, to, false, false);
        };

        const onDragEnd = event => {
            const drag = state.rangeDrag;
            if (!drag) {
                return;
            }

            if (drag.pointerId != null && event.pointerId == null) {
                return;
            }
            if (drag.pointerId != null && drag.pointerId !== event.pointerId) {
                return;
            }

            stopSelectorEvent(event);
            state.rangeDrag = null;
            removeDragListeners();
            const [minDate, maxDate] = state.graph.xAxisRange();
            state.lastNotifiedRange = null;
            notifyViewChange(state, minDate, maxDate);
        };

        const startDrag = event => {
            const geometry = selectorGeometry();
            const withinSelector = event.clientX >= geometry.rect.left - 16 &&
                event.clientX <= geometry.rect.right + 16 &&
                event.clientY >= geometry.rect.top - 10 &&
                event.clientY <= geometry.rect.bottom + 10;
            if (!withinSelector) {
                return;
            }

            stopSelectorEvent(event);
            if (state.rangeDrag) {
                return;
            }

            const leftDistance = Math.abs(event.clientX - geometry.leftPosition);
            const rightDistance = Math.abs(event.clientX - geometry.rightPosition);
            let mode;
            if (leftDistance <= 18 && leftDistance <= rightDistance) {
                mode = "left";
            } else if (rightDistance <= 18) {
                mode = "right";
            } else if (event.clientX > geometry.leftPosition && event.clientX < geometry.rightPosition) {
                mode = "pan";
            } else {
                mode = event.clientX <= geometry.leftPosition ? "left" : "right";
            }

            state.rangeDrag = {
                mode,
                pointerId: event.pointerId ?? null,
                rect: geometry.rect,
                leftPosition: geometry.leftPosition,
                rightPosition: geometry.rightPosition,
                startPointer: event.clientX
            };

            document.addEventListener("pointermove", onDragMove, { capture: true, passive: false });
            document.addEventListener("pointerup", onDragEnd, true);
            document.addEventListener("pointercancel", onDragEnd, true);
            document.addEventListener("mousemove", onDragMove, { capture: true, passive: false });
            document.addEventListener("mouseup", onDragEnd, true);
        };

        state.host.addEventListener("pointerdown", startDrag, true);
        state.host.addEventListener("mousedown", startDrag, true);

        state.rangeBindingCleanup = () => {
            state.host.removeEventListener("pointerdown", startDrag, true);
            state.host.removeEventListener("mousedown", startDrag, true);
            removeDragListeners();
        };
    }

    async function render(host, payload, dotNet) {
        if (!window.Dygraph) {
            throw new Error("Dygraphs did not load.");
        }

        const state = states.get(host);
        if (!state) {
            throw new Error("Dygraphs host was not initialized.");
        }

        state.dotNet = dotNet;
        state.sharedTooltip = payload.sharedTooltip !== false;
        state.labels = ["Date", ...payload.series.map(series => series.name)];
        state.payload = payload;

        const normalized = toRows(payload);
        state.metadata = normalized.metadata;
        state.min = normalized.min;
        state.max = normalized.max;
        state.suppressCallbacks = true;
        state.lastNotifiedRange = null;

        if (payload.series.length === 0 || normalized.rows.length === 0) {
            state.graph?.destroy();
            state.graph = null;
            state.selectionBindingCleanup?.();
            state.selectionBindingCleanup = null;
            state.selectionOverlay = null;
            state.rangeBindingCleanup?.();
            state.rangeBindingCleanup = null;
            state.host.replaceChildren();
            const empty = document.createElement("div");
            empty.className = "elo-dygraph-empty";
            empty.textContent = "No chart data available";
            host.appendChild(empty);
            state.suppressCallbacks = false;
            await waitForNextPaint();
            return;
        }

        const rangeSpan = state.max - state.min;
        const startFraction = clamp(Number(payload.viewStart ?? 0), 0, 1);
        const endFraction = clamp(Number(payload.viewEnd ?? 1), 0, 1);
        const requestedFrom = Number(payload.viewFromUtc);
        const requestedTo = Number(payload.viewToUtc);
        const hasAbsoluteDateWindow = Number.isFinite(requestedFrom) &&
            Number.isFinite(requestedTo) &&
            requestedTo > requestedFrom;
        const dateWindow = hasAbsoluteDateWindow
            ? [requestedFrom, requestedTo]
            : rangeSpan > 0 && (startFraction > 0 || endFraction < 1)
                ? [state.min + rangeSpan * startFraction, state.min + rangeSpan * endFraction]
                : undefined;

        const isFullRangeReset = !hasAbsoluteDateWindow && startFraction <= 0 && endFraction >= 1;
        const previousDateWindow = state.graph && !isFullRangeReset
            ? (() => {
                try {
                    return state.graph.xAxisRange();
                } catch {
                    return null;
                }
            })()
            : null;

        if (state.graph) {
            const updateDateWindow = dateWindow ?? previousDateWindow ?? [state.min, state.max];
            state.graph.updateOptions({
                file: normalized.rows,
                labels: state.labels,
                colors: payload.series.map(series => series.color),
                dateWindow: updateDateWindow,
                xRangePad: dateWindow || previousDateWindow ? 0 : 5
            });
            state.graph.resize();
            bindRangeSelectorInteraction(state);
            bindPlotSelectionInteraction(state);
            state.tooltip ??= ensureTooltip(host);
            const [updatedMinDate, updatedMaxDate] = state.graph.xAxisRange();
            state.lastNotifiedRange = selectedRange(state, updatedMinDate, updatedMaxDate);
            state.suppressCallbacks = false;
            await waitForNextPaint();
            return;
        }

        state.selectionBindingCleanup?.();
        state.selectionBindingCleanup = null;
        state.selectionOverlay = null;
        state.host.replaceChildren();
        state.tooltip = null;

        state.graph = new Dygraph(host, normalized.rows, {
            labels: state.labels,
            colors: payload.series.map(series => series.color),
            strokeWidth: 2,
            drawPoints: true,
            pointSize: 3,
            highlightCircleSize: 6,
            connectSeparatedPoints: true,
            highlightSeriesOpts: { strokeWidth: 2.75 },
            legend: "never",
            animatedZooms: false,
            panEdgeFraction: 0,
            rightGap: 10,
            xRangePad: hasAbsoluteDateWindow ? 0 : 5,
            dateWindow,
            showRangeSelector: payload.enableRangeNavigation === true,
            rangeSelectorHeight: 52,
            axes: {
                x: { axisLabelWidth: 70 },
                y: { axisLabelWidth: 42 }
            },
            highlightCallback: (event, x, points, row) => {
                renderTooltip(state, event, points, row);
                notifyPointHover(state, points, row);
            },
            unhighlightCallback: () => {
                if (state.tooltip) state.tooltip.hidden = true;
                clearPointHover(state);
            },
            drawCallback: graph => {
                if (state.suppressCallbacks || !state.dotNet) {
                    return;
                }

                const [minDate, maxDate] = graph.xAxisRange();
                notifyViewChange(state, minDate, maxDate);
            },
            zoomCallback: (minDate, maxDate) => {
                if (state.suppressCallbacks || !state.dotNet) {
                    return;
                }

                notifyViewChange(state, minDate, maxDate);
            },
            clickCallback: (event, x, points, row) => {
                if (!state.dotNet || row == null) {
                    return;
                }

                let seriesName = points?.find(point => point.yval != null)?.name;
                try {
                    const coords = state.graph.eventToDomCoords(event);
                    seriesName = state.graph.findClosestPoint(coords[0], coords[1])?.seriesName ?? seriesName;
                } catch {
                    // The highlighted point is a sufficient fallback when browser event coordinates are unavailable.
                }

                if (seriesName) {
                    const seriesIndex = state.labels.indexOf(seriesName) - 1;
                    const point = state.metadata[row]?.[seriesIndex];
                    if (point) {
                        state.dotNet.invokeMethodAsync("HandleDygraphPointClick", seriesName, point.x);
                    }
                }
            }
        });

        if (hasAbsoluteDateWindow) {
            setDateWindow(state, requestedFrom, requestedTo);
        }
        bindRangeSelectorInteraction(state);
        bindPlotSelectionInteraction(state);

        // Dygraphs rebuilds the host during construction, so attach the custom
        // tooltip after the graph has created its canvases.
        state.tooltip = ensureTooltip(host);
        const [initialMinDate, initialMaxDate] = state.graph.xAxisRange();
        state.lastNotifiedRange = selectedRange(state, initialMinDate, initialMaxDate);

        state.graph.ready(() => {
            state.suppressCallbacks = false;
        });

        // Dygraphs can finish synchronously when the data is already in memory,
        // so the ready callback is not a reliable only path for enabling user
        // interactions. Initialization callbacks are already complete here.
        state.suppressCallbacks = false;
        await waitForNextPaint();
    }

    window.basketEloDygraph = {
        initialize: (host) => {
            if (states.has(host)) {
                return;
            }

            const state = {
                host,
                graph: null,
                tooltip: null,
                dotNet: null,
                labels: [],
                metadata: [],
                min: 0,
                max: 0,
                suppressCallbacks: true,
                lastNotifiedRange: null,
                lastHoveredGameId: null,
                suppressHoverNotification: false,
                sharedTooltip: true,
                resizeObserver: null,
                rangeDrag: null,
                rangeBindingCleanup: null,
                wheelNotifyTimer: null,
                wheelBindingCleanup: null,
                selectionOverlay: null,
                selectionDrag: null,
                selectionBindingCleanup: null
            };
            states.set(host, state);
            bindWheelZoom(state);
            state.resizeObserver = new ResizeObserver(() => state.graph?.resize());
            state.resizeObserver.observe(host);
        },
        render,
        setHighlight,
        setDateWindow: (host, requestedFrom, requestedTo) => {
            const state = states.get(host);
            if (state) {
                setDateWindow(state, Number(requestedFrom), Number(requestedTo));
            }
        },
        dispose: (host) => {
            const state = states.get(host);
            if (!state) {
                return;
            }

            disposeState(state);
            states.delete(host);
        }
    };
})();
