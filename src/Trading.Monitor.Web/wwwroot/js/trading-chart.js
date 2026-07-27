(() => {
    const chartHost = document.getElementById("advancedTradesChart");
    const list = document.getElementById("liveTradeCards");
    const updated = document.getElementById("liveUpdated");
    const board = document.querySelector("[data-live-capital]");
    const chartAnalysis = document.getElementById("chartAnalysis");
    const hoverCard = document.getElementById("chartHoverInfo");
    const terminalStrip = document.getElementById("terminalStrip");
    const chartZoomInput = document.getElementById("chartZoom");
    const chartZoomLabel = document.getElementById("chartZoomLabel");

    if (!chartHost || !list || !board || !window.LightweightCharts) {
        return;
    }

    const lwc = window.LightweightCharts;
    const colors = {
        background: "#0b0e11",
        panel: "#101418",
        grid: "#1e2329",
        text: "#eaecef",
        muted: "#848e9c",
        yellow: "#f0b90b",
        green: "#0ecb81",
        red: "#f6465d",
        blue: "#2ab5f6",
        violet: "#a78bfa"
    };
    const routePalette = [
        "#f0b90b",
        "#2ab5f6",
        "#a78bfa",
        "#f97316",
        "#22c55e",
        "#ec4899",
        "#14b8a6",
        "#eab308",
        "#60a5fa",
        "#f43f5e"
    ];
    const capital = board.dataset.liveCapital || "1000";
    const estado = board.dataset.liveEstado || "abiertas";
    const symbolFilter = board.dataset.liveSymbol || "";
    const tipoSenal = board.dataset.liveTipoSenal || "";
    const symbolStorageKey = "trading-monitor-chart-symbol";
    const intervalStorageKey = "trading-monitor-chart-interval";
    const lineStyleDashed = lwc.LineStyle?.Dashed ?? 2;
    const crosshairModeNormal = lwc.CrosshairMode?.Normal ?? 0;
    const appTimeZone = resolveTimeZone(document.documentElement.dataset.appTimezone);

    let chart;
    let candleSeries;
    let volumeSeries;
    let markerApi;
    let selectedSymbol = symbolFilter || localStorage.getItem(symbolStorageKey) || "BTCUSDT";
    let selectedInterval = localStorage.getItem(intervalStorageKey) || "1m";
    let chartZoom = Number(chartZoomInput?.value || 45);
    let lastOperations = [];
    let lastSnapshot = null;
    let lastDataKey = "";
    let lastCandleData = [];
    let selectedOperationId = null;
    let priceLines = [];
    let operationSeriesById = new Map();
    let followLive = true;
    let applyingRange = false;
    let hasRenderedSnapshot = false;
    let userViewLocked = false;
    let userSelectedSymbol = Boolean(symbolFilter || localStorage.getItem(symbolStorageKey));
    let savedLogicalRange = null;
    let resetViewOnNextRender = true;
    let chartRequestId = 0;

    initializeChart();
    wireControls();
    refreshLiveTrades();
    window.setInterval(refreshLiveTrades, 5000);
    window.addEventListener("resize", debounce(() => {
        chart.applyOptions({ width: chartHost.clientWidth, height: chartHeight() });
        renderSnapshot(lastSnapshot, { keepRange: true });
    }, 160));

    function initializeChart() {
        chart = lwc.createChart(chartHost, {
            width: chartHost.clientWidth,
            height: chartHeight(),
            autoSize: true,
            layout: {
                background: { type: lwc.ColorType?.Solid ?? "solid", color: colors.background },
                textColor: colors.text,
                fontFamily: "Inter, Segoe UI, sans-serif"
            },
            grid: {
                vertLines: { color: colors.grid },
                horzLines: { color: colors.grid }
            },
            rightPriceScale: {
                borderColor: colors.grid,
                autoScale: true,
                entireTextOnly: true,
                scaleMargins: { top: 0.08, bottom: 0.22 }
            },
            timeScale: {
                borderColor: colors.grid,
                timeVisible: true,
                secondsVisible: true,
                rightOffset: 8,
                barSpacing: 8,
                minBarSpacing: 2,
                fixLeftEdge: false,
                fixRightEdge: false,
                tickMarkFormatter: formatChartTick
            },
            crosshair: {
                mode: crosshairModeNormal,
                vertLine: {
                    color: "rgba(132, 142, 156, 0.72)",
                    width: 1,
                    style: lineStyleDashed,
                    labelBackgroundColor: colors.panel
                },
                horzLine: {
                    color: "rgba(132, 142, 156, 0.72)",
                    width: 1,
                    style: lineStyleDashed,
                    labelBackgroundColor: colors.panel
                }
            },
            handleScroll: {
                mouseWheel: true,
                pressedMouseMove: true,
                horzTouchDrag: true,
                vertTouchDrag: true
            },
            handleScale: {
                axisPressedMouseMove: { time: true, price: true },
                mouseWheel: true,
                pinch: true
            },
            localization: {
                locale: navigator.language || "es-MX",
                priceFormatter: formatPrice,
                timeFormatter: formatChartDateTime
            }
        });

        candleSeries = addSeries("candlestick", {
            upColor: colors.green,
            downColor: colors.red,
            borderUpColor: colors.green,
            borderDownColor: colors.red,
            wickUpColor: colors.green,
            wickDownColor: colors.red,
            priceLineColor: colors.yellow,
            priceLineWidth: 1,
            lastValueVisible: true
        });

        volumeSeries = addSeries("histogram", {
            priceFormat: { type: "volume" },
            priceScaleId: "",
            lastValueVisible: false,
            priceLineVisible: false
        });
        chart.priceScale("").applyOptions({
            scaleMargins: { top: 0.78, bottom: 0 }
        });

        chart.subscribeCrosshairMove(showHoverInfo);
        chart.timeScale().subscribeVisibleLogicalRangeChange(range => {
            if (applyingRange || !range || !lastSnapshot?.candles?.length) {
                return;
            }

            savedLogicalRange = cloneLogicalRange(range);
            if (hasRenderedSnapshot) {
                userViewLocked = true;
                followLive = false;
            }
        });
    }

    function addSeries(kind, options) {
        if (kind === "candlestick") {
            return chart.addSeries && lwc.CandlestickSeries
                ? chart.addSeries(lwc.CandlestickSeries, options)
                : chart.addCandlestickSeries(options);
        }

        if (kind === "histogram") {
            return chart.addSeries && lwc.HistogramSeries
                ? chart.addSeries(lwc.HistogramSeries, options)
                : chart.addHistogramSeries(options);
        }

        return chart.addSeries && lwc.LineSeries
            ? chart.addSeries(lwc.LineSeries, options)
            : chart.addLineSeries(options);
    }

    function wireControls() {
        document.querySelectorAll("[data-chart-symbol-button]").forEach(button => {
            if (symbolFilter && button.dataset.chartSymbolButton !== symbolFilter) {
                button.disabled = true;
            }

            button.classList.toggle("active", selectedSymbol === button.dataset.chartSymbolButton);
            button.addEventListener("click", async () => {
                selectedSymbol = button.dataset.chartSymbolButton || selectedSymbol;
                userSelectedSymbol = true;
                selectedOperationId = null;
                persistChartChoice();
                lockCurrentView();
                document.querySelectorAll("[data-chart-symbol-button]").forEach(item => item.classList.toggle("active", item === button));
                await refreshChart();
            });
        });

        document.querySelectorAll("[data-chart-interval]").forEach(button => {
            button.classList.toggle("active", selectedInterval === button.dataset.chartInterval);
            button.addEventListener("click", async () => {
                selectedInterval = button.dataset.chartInterval || selectedInterval;
                persistChartChoice();
                lockCurrentView();
                document.querySelectorAll("[data-chart-interval]").forEach(item => item.classList.toggle("active", item === button));
                await refreshChart();
            });
        });

        document.querySelector("[data-chart-live]")?.addEventListener("click", () => {
            resetChartView();
            applyZoomFromSlider();
            scrollChartToRealTime();
        });

        document.querySelector("[data-chart-zoom-out]")?.addEventListener("click", () => setChartZoom(chartZoom - 10));
        document.querySelector("[data-chart-zoom-in]")?.addEventListener("click", () => setChartZoom(chartZoom + 10));
        chartZoomInput?.addEventListener("input", () => setChartZoom(Number(chartZoomInput.value)));
        setChartZoom(chartZoom, false);
    }

    async function refreshLiveTrades() {
        try {
            const url = `/api/operaciones-vivas?capital=${encodeURIComponent(capital)}&estado=${encodeURIComponent(estado)}&symbol=${encodeURIComponent(symbolFilter)}&tipoSenal=${encodeURIComponent(tipoSenal)}`;
            const response = await fetch(url, { cache: "no-store" });
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const data = await response.json();
            lastOperations = data.operations || [];
            selectedSymbol = symbolFilter || selectedSymbol || pickChartSymbol(lastOperations);
            if (!symbolFilter && !userSelectedSymbol && lastOperations.length > 0 && !lastOperations.some(item => item.symbol === selectedSymbol)) {
                selectedSymbol = pickChartSymbol(lastOperations);
            }
            document.querySelectorAll("[data-chart-symbol-button]").forEach(item => {
                item.classList.toggle("active", item.dataset.chartSymbolButton === selectedSymbol);
            });
            await refreshChart();

            if (updated) {
                const date = new Date(data.serverTime);
                updated.textContent = `actualizado ${date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" })}`;
            }
        } catch {
            list.innerHTML = `<div class="empty-state"><strong>No pude refrescar.</strong><span>El servicio sigue corriendo; reintentare solo.</span></div>`;
        }
    }

    async function refreshChart() {
        const requestId = ++chartRequestId;
        const requestSymbol = selectedSymbol;
        const requestInterval = selectedInterval;
        const url = `/api/grafico-vivo?symbol=${encodeURIComponent(requestSymbol)}&interval=${encodeURIComponent(requestInterval)}&capital=${encodeURIComponent(capital)}&estado=${encodeURIComponent(estado)}&tipoSenal=${encodeURIComponent(tipoSenal)}`;
        const response = await fetch(url, { cache: "no-store" });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const snapshot = await response.json();
        if (requestId !== chartRequestId || snapshot.symbol !== selectedSymbol || snapshot.interval !== selectedInterval) {
            return;
        }

        renderSnapshot(snapshot, { keepRange: userViewLocked && !resetViewOnNextRender });
        if (Array.isArray(snapshot.operations)) {
            lastOperations = lastOperations
                .filter(operation => operation.symbol !== snapshot.symbol)
                .concat(snapshot.operations);
            renderList(lastOperations);
        }
    }

    function renderSnapshot(snapshot, options = {}) {
        if (!snapshot || !snapshot.candles) {
            return;
        }

        lastSnapshot = snapshot;
        const candleData = snapshot.candles.map(toCandleData).filter(hasValidTime);
        const volumeData = snapshot.candles.map(toVolumeData).filter(hasValidTime);
        const operations = (snapshot.operations || []).filter(item => item.symbol === snapshot.symbol);
        const selectedOperation = pickSelectedOperation(operations);
        const analysisTrade = buildAnalysisTrade(snapshot.analysis);
        const primaryTrade = selectedOperation || analysisTrade;
        const previousRange = cloneLogicalRange(savedLogicalRange || chart.timeScale().getVisibleLogicalRange());

        withRangeLock(() => {
            updateMarketSeries(snapshot, candleData, volumeData);
            clearPriceLines();
            drawOperationTrails(operations, candleData);
            drawMarkers(operations, candleData);

            if (primaryTrade) {
                drawPrimaryTradeLevels(primaryTrade);
            }
        });

        updateChartAnalysis(snapshot, selectedOperation, analysisTrade);
        updateTerminalStrip(snapshot, candleData, operations);

        if (options.keepRange && previousRange) {
            restoreLogicalRange(previousRange);
        } else if (resetViewOnNextRender || !hasRenderedSnapshot) {
            applyZoomFromSlider(candleData.length);
            scrollChartToRealTime();
            resetViewOnNextRender = false;
        } else if (followLive) {
            scrollChartToRealTime();
        }

        hasRenderedSnapshot = true;
    }

    function renderList(operations) {
        const visibleOperations = operations.filter(item => item.symbol === selectedSymbol);

        if (visibleOperations.length === 0) {
            list.innerHTML = `<div class="empty-state"><strong>Sin operaciones.</strong><span>Esperando una senal clara.</span></div>`;
            return;
        }

        if (selectedOperationId && !visibleOperations.some(item => item.id === selectedOperationId)) {
            selectedOperationId = null;
        }

        list.innerHTML = visibleOperations.slice(0, 18).map((item, index) => {
            const isSelected = item.id === selectedOperationId;
            const routeColor = operationColor(item, index);
            const resultClass = item.realizedText.startsWith("-") ? "loss" : item.realizedText === "Abierta" ? "" : "gain";

            return `
            <article class="live-card ${escapeHtml(item.signalClass)} ${isSelected ? "selected-live-card" : ""}" data-operation-id="${escapeAttribute(item.id)}" style="--signal-color: ${escapeAttribute(routeColor)}">
                <div class="live-card-summary">
                    <i aria-hidden="true"></i>
                    <div>
                        <span>${escapeHtml(item.action)}</span>
                        <strong>${escapeHtml(item.symbol)}</strong>
                        <small>${escapeHtml(item.signalTypeLabel || item.side)} | ${escapeHtml(item.horizon || "Mercado")} | ${escapeHtml(item.status)} | score ${item.score}/100</small>
                    </div>
                    <em>${isSelected ? "siguiendo" : "ver"}</em>
                </div>
                <div class="live-card-body">
                    <dl>
                        <div><dt>Tipo</dt><dd>${escapeHtml(item.signalTypeLabel || item.side)}</dd></div>
                        <div><dt>${escapeHtml(entryActionLabel(item))}</dt><dd>${formatTime(item.entryAt)}-${formatTime(item.entryUntil)}</dd></div>
                        <div><dt>Precio para ${escapeHtml(entryVerb(item))}</dt><dd>${formatPrice(item.entryLower)}-${formatPrice(item.entryUpper)}</dd></div>
                        <div><dt>${escapeHtml(exitActionLabel(item))}</dt><dd>${formatTime(item.exitBy)}</dd></div>
                        <div><dt>Ganar</dt><dd class="gain">${escapeHtml(item.potentialTp1)}</dd></div>
                        <div><dt>Perder max</dt><dd class="loss">${escapeHtml(item.potentialStop)}</dd></div>
                        <div><dt>Tiempo viva</dt><dd>${escapeHtml(item.timeText)}</dd></div>
                        <div><dt>Resultado final</dt><dd class="${resultClass}">${escapeHtml(item.realizedText)}</dd></div>
                        <div><dt>Entrada/final</dt><dd>${escapeHtml(item.entryExitText)}</dd></div>
                    </dl>
                    <p class="live-calc">${escapeHtml(item.signalTypeDescription || "")}</p>
                    <p class="live-calc">${escapeHtml(item.realizedFormulaText)}</p>
                    <div class="conversion-box">
                        <strong>${escapeHtml(item.finalConversionText || item.realizedText)}</strong>
                        <span>${escapeHtml(item.entryConversionText || "")}</span>
                        <span>${escapeHtml(item.exitConversionText || "")}</span>
                        <span>${escapeHtml(item.costText || "")}</span>
                        <span>${escapeHtml(item.breakEvenText || "")}</span>
                        <small>${escapeHtml(item.conversionHeadline || "")}</small>
                    </div>
                    <div class="trade-links">
                        <button type="button" data-chart-symbol="${escapeHtml(item.symbol)}" data-card-operation="${escapeAttribute(item.id)}">Ver en grafico</button>
                        ${(item.links || []).slice(0, 4).map(link => `<a href="${escapeAttribute(link.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(link.label)}</a>`).join("")}
                    </div>
                </div>
            </article>`;
        }).join("");

        list.querySelectorAll("[data-chart-symbol]").forEach(button => {
            button.addEventListener("click", async event => {
                event.stopPropagation();
                selectedSymbol = button.getAttribute("data-chart-symbol") || selectedSymbol;
                userSelectedSymbol = true;
                selectedOperationId = button.getAttribute("data-card-operation");
                persistChartChoice();
                lockCurrentView();
                document.querySelectorAll("[data-chart-symbol-button]").forEach(item => {
                    item.classList.toggle("active", item.dataset.chartSymbolButton === selectedSymbol);
                });
                await refreshChart();
                renderList(lastOperations);
            });
        });

        list.querySelectorAll("[data-operation-id]").forEach(card => {
            card.addEventListener("click", async event => {
                if (event.target.closest("a")) {
                    return;
                }

                selectedOperationId = card.getAttribute("data-operation-id");
                const operation = lastOperations.find(item => item.id === selectedOperationId);
                if (operation) {
                    selectedSymbol = operation.symbol;
                    userSelectedSymbol = true;
                    persistChartChoice();
                    lockCurrentView();
                    await refreshChart();
                    renderList(lastOperations);
                }
            });
        });
    }

    function drawOperationTrails(operations, candleData) {
        const activeIds = new Set();
        operations.slice(0, 10).forEach((operation, index) => {
            const path = buildOperationPath(operation, candleData);
            if (path.length < 2) {
                return;
            }

            activeIds.add(operation.id);
            const isSelected = operation.id === selectedOperationId || (!selectedOperationId && index === 0);
            const color = operationColor(operation, index);
            let series = operationSeriesById.get(operation.id);
            const options = {
                color,
                lineWidth: isSelected ? 4 : operation.status === "Abierta" ? 3 : 2,
                lineStyle: operation.status === "Abierta" ? 0 : lineStyleDashed,
                priceLineVisible: false,
                lastValueVisible: false,
                crosshairMarkerVisible: isSelected
            };

            if (!series) {
                series = addSeries("line", options);
                operationSeriesById.set(operation.id, series);
            } else {
                series.applyOptions(options);
            }

            series.setData(path);
        });

        operationSeriesById.forEach((series, id) => {
            if (!activeIds.has(id)) {
                chart.removeSeries(series);
                operationSeriesById.delete(id);
            }
        });
    }

    function drawMarkers(operations, candleData) {
        const markers = [];
        operations.slice(0, 10).forEach((operation, index) => {
            const isSelected = operation.id === selectedOperationId;
            const entryTime = nearestTime(toUnixTime(operation.entryAt || operation.observedAt), candleData);
            const exitTime = operation.exitTime
                ? nearestTime(toUnixTime(operation.exitTime), candleData)
                : nearestTime(candleData.at(-1)?.time, candleData);
            const routeColor = operationColor(operation, index);
            const resultIsLoss = operation.realizedText?.startsWith("-");
            const resultColor = operation.status === "Abierta" ? routeColor : resultIsLoss ? colors.red : colors.green;

            if (entryTime) {
                markers.push({
                    time: entryTime,
                    position: operation.side === "Long" ? "belowBar" : "aboveBar",
                    color: routeColor,
                    shape: operation.side === "Long" ? "arrowUp" : "arrowDown",
                    ...(isSelected ? { text: entryMarkerText(operation) } : {})
                });
            }

            if (exitTime) {
                markers.push({
                    time: exitTime,
                    position: operation.side === "Long" ? "aboveBar" : "belowBar",
                    color: resultColor,
                    shape: operation.status === "Abierta" ? "circle" : "square",
                    ...(isSelected ? { text: exitMarkerText(operation) } : {})
                });
            }
        });

        markers.sort((a, b) => a.time - b.time);
        if (lwc.createSeriesMarkers) {
            if (!markerApi) {
                markerApi = lwc.createSeriesMarkers(candleSeries, markers);
            } else {
                markerApi.setMarkers(markers);
            }
        } else if (candleSeries.setMarkers) {
            candleSeries.setMarkers(markers);
        }
    }

    function drawPrimaryTradeLevels(trade) {
        const entry = (Number(trade.entryLower) + Number(trade.entryUpper)) / 2;
        addPriceLine(entry, colors.yellow, entryLineTitle(trade));
        addPriceLine(Number(trade.takeProfit1), colors.green, profitLineTitle(trade));
        addPriceLine(Number(trade.takeProfit2), colors.green, `${profitLineTitle(trade)} extra`);
        addPriceLine(Number(trade.stopLoss), colors.red, lossLineTitle(trade));
    }

    function addPriceLine(price, color, title) {
        if (!Number.isFinite(price) || price <= 0) {
            return;
        }

        priceLines.push(candleSeries.createPriceLine({
            price,
            color,
            lineWidth: 1,
            lineStyle: lineStyleDashed,
            axisLabelVisible: true,
            title
        }));
    }

    function clearPriceLines() {
        priceLines.forEach(line => candleSeries.removePriceLine(line));
        priceLines = [];
    }

    function buildOperationPath(operation, candleData) {
        if (candleData.length === 0) {
            return [];
        }

        const entryTime = nearestTime(toUnixTime(operation.entryAt || operation.observedAt), candleData) || candleData[0].time;
        const exitLimit = operation.exitTime ? toUnixTime(operation.exitTime) : Number.MAX_SAFE_INTEGER;
        const points = [{ time: entryTime, value: Number(operation.entryPrice) }];

        candleData
            .filter(candle => candle.time >= entryTime && candle.time <= exitLimit)
            .forEach(candle => points.push({ time: candle.time, value: Number(candle.close) }));

        if (operation.exitPrice && operation.exitTime) {
            points.push({ time: nearestTime(toUnixTime(operation.exitTime), candleData) || points.at(-1).time, value: Number(operation.exitPrice) });
        } else {
            const last = candleData.at(-1);
            points.push({ time: last.time, value: Number(operation.lastPrice || last.close) });
        }

        return dedupeTimes(points)
            .filter(point => Number.isFinite(point.time) && Number.isFinite(point.value) && point.value > 0)
            .sort((a, b) => a.time - b.time);
    }

    function showHoverInfo(param) {
        if (!hoverCard || !param?.time || !param.point || param.point.x < 0 || param.point.y < 0) {
            if (hoverCard) {
                hoverCard.hidden = true;
            }
            return;
        }

        const candle = param.seriesData.get(candleSeries);
        if (!candle) {
            hoverCard.hidden = true;
            return;
        }

        const change = Number(candle.close) - Number(candle.open);
        const changePercent = Number(candle.open) === 0 ? 0 : change / Number(candle.open) * 100;
        hoverCard.innerHTML = `
            <strong>${formatDateTime(param.time)}</strong>
            <dl>
                <div><dt>Abrio</dt><dd>${formatPrice(candle.open)}</dd></div>
                <div><dt>Alto</dt><dd>${formatPrice(candle.high)}</dd></div>
                <div><dt>Bajo</dt><dd>${formatPrice(candle.low)}</dd></div>
                <div><dt>Cerro</dt><dd class="${change >= 0 ? "gain" : "loss"}">${formatPrice(candle.close)} (${changePercent.toFixed(2)}%)</dd></div>
            </dl>`;
        hoverCard.hidden = false;

        const x = Math.min(chartHost.clientWidth - 230, Math.max(12, param.point.x + 16));
        const y = Math.min(chartHost.clientHeight - 126, Math.max(12, param.point.y + 16));
        hoverCard.style.transform = `translate(${x}px, ${y}px)`;
    }

    function updateChartAnalysis(snapshot, operation, analysisTrade) {
        if (!chartAnalysis) {
            return;
        }

        const analysis = snapshot.analysis || {};
        const sideClass = operation
            ? operation.realizedText?.startsWith("-") ? "loss" : operation.realizedText === "Abierta" ? "flat" : "gain"
            : analysis.side === "Long" ? "gain" : analysis.side === "Short" ? "loss" : "flat";
        const sourceText = operation ? "senal guardada" : analysisTrade ? "lectura tecnica" : "sin entrada";
        const actionText = operation ? operation.action : analysis.action || "Esperar";
        const readoutText = operation
            ? `${operation.signalTypeLabel || operation.side} | ${operation.status} | score ${operation.score}/100 | ${operation.realizedFormulaText}`
            : analysis.readout || "Sin datos suficientes.";
        const entryText = operation
            ? `${formatPrice(operation.entryLower)}-${formatPrice(operation.entryUpper)}`
            : analysisTrade ? `${formatPrice(analysis.entryLower)}-${formatPrice(analysis.entryUpper)}` : "Esperar";
        const entryAtText = operation ? operation.entryAt : analysis.entryAt;
        const entryUntilText = operation ? operation.entryUntil : analysis.entryUntil;
        const exitByText = operation ? operation.exitBy : analysis.exitBy;
        const holdingText = operation ? operation.timeText : analysis.holdingText || "-";
        const gainText = operation ? operation.potentialTp1 : analysisTrade ? formatMoney(analysis.potentialTp1) : "-";
        const gainPrice = operation ? formatPrice(operation.takeProfit1) : analysisTrade ? formatPrice(analysis.takeProfit1) : "-";
        const lossText = operation ? operation.potentialStop : analysisTrade ? formatMoney(analysis.potentialStop) : "-";
        const lossPrice = operation ? formatPrice(operation.stopLoss) : analysisTrade ? formatPrice(analysis.stopLoss) : "-";
        const conversionHtml = operation
            ? `<div class="analysis-conversion">
                    <strong>${escapeHtml(operation.finalConversionText || operation.realizedText)}</strong>
                    <span>${escapeHtml(operation.entryConversionText || "")}</span>
                    <span>${escapeHtml(operation.exitConversionText || "")}</span>
                    <span>${escapeHtml(operation.costText || "")}</span>
                    <span>${escapeHtml(operation.breakEvenText || "")}</span>
                    <small>${escapeHtml(operation.conversionHeadline || "")}</small>
               </div>`
            : "";

        chartAnalysis.innerHTML = `
            <div class="analysis-headline">
                <div>
                    <span>${escapeHtml(snapshot.symbol)} ${escapeHtml(snapshot.interval)} | ${escapeHtml(operation?.horizon || analysis.horizon || "Mercado")} | ${escapeHtml(sourceText)}</span>
                    <strong class="${sideClass}">${escapeHtml(actionText)}</strong>
                </div>
                <small>${escapeHtml(readoutText)}</small>
            </div>
            <dl class="analysis-grid">
                <div><dt>${escapeHtml(operation ? entryActionLabel(operation) : analysisEntryLabel(analysis))}</dt><dd>${entryText}</dd><small>${formatTime(entryAtText)}-${formatTime(entryUntilText)}</small></div>
                <div><dt>${escapeHtml(operation ? exitActionLabel(operation) : analysisExitLabel(analysis))}</dt><dd>${formatTime(exitByText)}</dd><small>${escapeHtml(holdingText)}</small></div>
                <div><dt>Ganar</dt><dd class="gain">${escapeHtml(gainText)}</dd><small>${gainPrice}</small></div>
                <div><dt>Perder max</dt><dd class="loss">${escapeHtml(lossText)}</dd><small>${lossPrice}</small></div>
            </dl>
            ${conversionHtml}`;
    }

    function updateTerminalStrip(snapshot, candleData, operations) {
        if (!terminalStrip || candleData.length === 0) {
            return;
        }

        const last = candleData.at(-1);
        const previous = candleData.at(-2) || last;
        const change = last.close - previous.close;
        const changePercent = previous.close === 0 ? 0 : change / previous.close * 100;
        terminalStrip.innerHTML = `
            <strong>${escapeHtml(snapshot.symbol)} ${escapeHtml(snapshot.interval)}</strong>
            <span class="${change >= 0 ? "gain" : "loss"}">${formatPrice(last.close)} ${change >= 0 ? "+" : ""}${changePercent.toFixed(2)}%</span>
            <span>${operations.length} rutas</span>
            <span>${formatDateTime(last.time)}</span>`;
    }

    function updateMarketSeries(snapshot, candleData, volumeData) {
        const dataKey = `${snapshot.symbol}:${snapshot.interval}`;
        const sameWindow = dataKey === lastDataKey
            && lastCandleData.length === candleData.length
            && lastCandleData[0]?.time === candleData[0]?.time;

        if (sameWindow && candleData.length > 0) {
            candleSeries.update(candleData.at(-1));
            volumeSeries.update(volumeData.at(-1));
        } else {
            candleSeries.setData(candleData);
            volumeSeries.setData(volumeData);
        }

        lastDataKey = dataKey;
        lastCandleData = candleData;
    }

    function applyZoomFromSlider(dataLength = lastSnapshot?.candles?.length || 120) {
        if (!chart || dataLength <= 0) {
            return;
        }

        const maxBars = Math.min(dataLength, chartHost.clientWidth < 760 ? 90 : 120);
        const minBars = chartHost.clientWidth < 760 ? 18 : 26;
        const bars = Math.max(minBars, Math.round(maxBars - ((maxBars - minBars) * (chartZoom / 100))));
        restoreLogicalRange({ from: Math.max(0, dataLength - bars), to: dataLength + 6 });
    }

    function setChartZoom(value, updateRange = true) {
        chartZoom = Math.max(0, Math.min(100, Number(value) || 0));
        if (chartZoomInput) {
            chartZoomInput.value = String(chartZoom);
        }

        if (chartZoomLabel) {
            chartZoomLabel.textContent = chartZoom >= 75 ? "cerca" : chartZoom <= 25 ? "lejos" : "medio";
        }

        if (updateRange) {
            userViewLocked = true;
            followLive = false;
            applyZoomFromSlider();
        }
    }

    function resetChartView() {
        followLive = true;
        userViewLocked = false;
        savedLogicalRange = null;
        resetViewOnNextRender = true;
    }

    function lockCurrentView() {
        savedLogicalRange = cloneLogicalRange(chart.timeScale().getVisibleLogicalRange());
        userViewLocked = Boolean(savedLogicalRange);
        followLive = false;
        resetViewOnNextRender = !savedLogicalRange;
    }

    function persistChartChoice() {
        if (!symbolFilter) {
            localStorage.setItem(symbolStorageKey, selectedSymbol);
        }

        localStorage.setItem(intervalStorageKey, selectedInterval);
    }

    function cloneLogicalRange(range) {
        if (!range || !Number.isFinite(range.from) || !Number.isFinite(range.to)) {
            return null;
        }

        return { from: Number(range.from), to: Number(range.to) };
    }

    function restoreLogicalRange(range) {
        const nextRange = cloneLogicalRange(range);
        if (!nextRange || nextRange.to <= nextRange.from) {
            return;
        }

        withRangeLock(() => {
            chart.timeScale().setVisibleLogicalRange(nextRange);
            savedLogicalRange = cloneLogicalRange(chart.timeScale().getVisibleLogicalRange()) || nextRange;
        });
    }

    function scrollChartToRealTime() {
        withRangeLock(() => {
            chart.timeScale().scrollToRealTime();
            savedLogicalRange = cloneLogicalRange(chart.timeScale().getVisibleLogicalRange()) || savedLogicalRange;
        });
    }

    function withRangeLock(callback) {
        applyingRange = true;
        try {
            callback();
        } finally {
            applyingRange = false;
        }
    }

    function pickSelectedOperation(operations) {
        return selectedOperationId
            ? operations.find(item => item.id === selectedOperationId) || null
            : null;
    }

    function pickChartSymbol(operations) {
        return (operations.find(item => item.highlight) || operations.find(item => item.status === "Abierta") || operations[0] || { symbol: "BTCUSDT" }).symbol;
    }

    function buildAnalysisTrade(analysis) {
        if (!analysis || (analysis.side !== "Long" && analysis.side !== "Short") || Number(analysis.entryLower) <= 0 || Number(analysis.entryUpper) <= 0) {
            return null;
        }

        return {
            side: analysis.side,
            action: analysis.action,
            entryLower: Number(analysis.entryLower),
            entryUpper: Number(analysis.entryUpper),
            stopLoss: Number(analysis.stopLoss),
            takeProfit1: Number(analysis.takeProfit1),
            takeProfit2: Number(analysis.takeProfit2),
            lastPrice: Number(analysis.lastPrice),
            exitPrice: null,
            entryAt: analysis.entryAt,
            entryUntil: analysis.entryUntil,
            exitBy: analysis.exitBy,
            potentialTp1: formatMoney(analysis.potentialTp1)
        };
    }

    function operationColor(operation, index = 0) {
        const value = String(operation?.id || `${operation?.symbol || "signal"}-${index}`);
        let hash = 0;
        for (let position = 0; position < value.length; position += 1) {
            hash = ((hash << 5) - hash) + value.charCodeAt(position);
            hash |= 0;
        }

        return routePalette[Math.abs(hash + index) % routePalette.length];
    }

    function isBuyLowSellHigh(item) {
        return item?.side === "Long" || item?.signalType === "compra-bajo-vende-alto";
    }

    function entryVerb(item) {
        return isBuyLowSellHigh(item) ? "comprar" : "vender";
    }

    function entryActionLabel(item) {
        return isBuyLowSellHigh(item) ? "Comprar bajo" : "Vender alto";
    }

    function exitActionLabel(item) {
        return isBuyLowSellHigh(item) ? "Vender alto antes de" : "Comprar bajo antes de";
    }

    function entryMarkerText(item) {
        return isBuyLowSellHigh(item) ? "Comprar" : "Vender";
    }

    function exitMarkerText(item) {
        return isBuyLowSellHigh(item) ? "Vender" : "Comprar";
    }

    function entryLineTitle(trade) {
        return isBuyLowSellHigh(trade) ? "Comprar entrada" : "Vender entrada";
    }

    function profitLineTitle(trade) {
        return isBuyLowSellHigh(trade) ? "Vender con ganancia" : "Comprar con ganancia";
    }

    function lossLineTitle() {
        return "Salir por perdida max";
    }

    function analysisEntryLabel(analysis) {
        return analysis?.side === "Long" ? "Comprar bajo" : analysis?.side === "Short" ? "Vender alto" : "Esperar entrada";
    }

    function analysisExitLabel(analysis) {
        return analysis?.side === "Long" ? "Vender alto antes de" : analysis?.side === "Short" ? "Comprar bajo antes de" : "Sin salida sugerida";
    }

    function toCandleData(item) {
        return {
            time: toUnixTime(item.closeTime),
            open: Number(item.open),
            high: Number(item.high),
            low: Number(item.low),
            close: Number(item.close)
        };
    }

    function toVolumeData(item) {
        const open = Number(item.open);
        const close = Number(item.close);
        return {
            time: toUnixTime(item.closeTime),
            value: Number(item.volume),
            color: close >= open ? "rgba(14, 203, 129, 0.36)" : "rgba(246, 70, 93, 0.34)"
        };
    }

    function hasValidTime(item) {
        return Number.isFinite(item.time) && item.time > 0;
    }

    function toUnixTime(value) {
        if (!value) {
            return Number.NaN;
        }

        if (typeof value === "number") {
            return Math.floor(value);
        }

        return Math.floor(new Date(value).getTime() / 1000);
    }

    function nearestTime(target, candleData) {
        if (!Number.isFinite(target) || candleData.length === 0) {
            return candleData.at(-1)?.time;
        }

        let best = candleData[0].time;
        let bestDistance = Math.abs(best - target);
        for (const candle of candleData) {
            const distance = Math.abs(candle.time - target);
            if (distance < bestDistance) {
                best = candle.time;
                bestDistance = distance;
            }
        }

        return best;
    }

    function dedupeTimes(points) {
        const map = new Map();
        points.forEach(point => map.set(point.time, point));
        return [...map.values()];
    }

    function chartHeight() {
        return chartHost.clientWidth < 760 ? 430 : 620;
    }

    function formatPrice(value) {
        const number = Number(value || 0);
        const decimals = Math.abs(number) >= 1000 ? 2 : Math.abs(number) >= 1 ? 4 : 8;
        return number.toLocaleString("en-US", { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
    }

    function formatMoney(value) {
        return Number(value || 0).toLocaleString("en-US", { style: "currency", currency: "USD", minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatTime(value) {
        if (!value) {
            return "-";
        }

        return new Date(value).toLocaleTimeString([], withTimeZone({ hour: "2-digit", minute: "2-digit" }));
    }

    function formatDateTime(value) {
        return chartTimeToDate(value).toLocaleString([], withTimeZone({ month: "short", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit" }));
    }

    function formatChartDateTime(value) {
        return chartTimeToDate(value).toLocaleString([], withTimeZone({ month: "short", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit" }));
    }

    function formatChartTick(value) {
        return chartTimeToDate(value).toLocaleString([], withTimeZone({ month: "short", day: "2-digit", hour: "2-digit", minute: "2-digit" }));
    }

    function chartTimeToDate(value) {
        if (typeof value === "number") {
            return new Date(value * 1000);
        }

        if (value && typeof value === "object" && "year" in value) {
            return new Date(Date.UTC(value.year, value.month - 1, value.day));
        }

        return new Date(value);
    }

    function withTimeZone(options) {
        return appTimeZone ? { ...options, timeZone: appTimeZone } : options;
    }

    function resolveTimeZone(value) {
        if (!value) {
            return null;
        }

        try {
            new Intl.DateTimeFormat(undefined, { timeZone: value }).format(new Date());
            return value;
        } catch {
            return null;
        }
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll("\"", "&quot;")
            .replaceAll("'", "&#039;");
    }

    function escapeAttribute(value) {
        return escapeHtml(value).replaceAll("`", "&#096;");
    }

    function debounce(callback, delay) {
        let handle;
        return (...args) => {
            window.clearTimeout(handle);
            handle = window.setTimeout(() => callback(...args), delay);
        };
    }
})();
