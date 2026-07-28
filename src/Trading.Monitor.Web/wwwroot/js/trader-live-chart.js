(() => {
    const board = document.querySelector("[data-live-trader-board]");
    const chartHost = document.getElementById("traderLiveChart");
    const strip = document.getElementById("traderLiveStrip");
    const detail = document.getElementById("traderLiveDetail");
    const hover = document.getElementById("traderLiveHover");

    if (!board || !chartHost || !window.LightweightCharts) {
        return;
    }

    const lwc = window.LightweightCharts;
    const colors = {
        background: "#0b0e11",
        grid: "#1e2329",
        text: "#eaecef",
        muted: "#848e9c",
        yellow: "#f0b90b",
        green: "#0ecb81",
        red: "#f6465d",
        blue: "#2ab5f6",
        route: "#22d3ee"
    };
    const appTimeZone = resolveTimeZone(document.documentElement.dataset.appTimezone);
    const market = board.dataset.market || "crypto";
    const isForex = market === "forex";
    let selected = document.querySelector("[data-trader-operation]");
    let chart;
    let marketSeries;
    let routeSeries;
    let markerApi;
    let priceLines = [];
    let candles = [];
    let requestId = 0;

    initializeChart();
    wireList();
    renderSelected();
    window.setInterval(renderSelected, 7000);
    window.addEventListener("resize", () => chart.applyOptions({ width: chartHost.clientWidth, height: chartHeight() }));

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
                scaleMargins: { top: 0.08, bottom: 0.16 }
            },
            timeScale: {
                borderColor: colors.grid,
                timeVisible: true,
                secondsVisible: true,
                rightOffset: 8,
                barSpacing: 8,
                minBarSpacing: 2,
                tickMarkFormatter: formatChartTick
            },
            localization: {
                locale: navigator.language || "es-MX",
                priceFormatter: formatPrice,
                timeFormatter: formatChartDateTime
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
            }
        });

        marketSeries = isForex
            ? addSeries("line", { color: colors.blue, lineWidth: 3, crosshairMarkerVisible: true, lastValueVisible: true })
            : addSeries("candlestick", {
                upColor: colors.green,
                downColor: colors.red,
                borderUpColor: colors.green,
                borderDownColor: colors.red,
                wickUpColor: colors.green,
                wickDownColor: colors.red,
                lastValueVisible: true
            });
        routeSeries = addSeries("line", {
            color: colors.route,
            lineWidth: 3,
            priceLineVisible: false,
            lastValueVisible: false
        });
        chart.subscribeCrosshairMove(showHover);
    }

    function addSeries(kind, options) {
        if (kind === "candlestick") {
            return chart.addSeries && lwc.CandlestickSeries
                ? chart.addSeries(lwc.CandlestickSeries, options)
                : chart.addCandlestickSeries(options);
        }

        return chart.addSeries && lwc.LineSeries
            ? chart.addSeries(lwc.LineSeries, options)
            : chart.addLineSeries(options);
    }

    function wireList() {
        document.querySelectorAll("[data-trader-operation]").forEach(button => {
            button.addEventListener("click", () => {
                selected = button;
                document.querySelectorAll("[data-trader-operation]").forEach(item => {
                    const isActive = item === selected;
                    item.classList.toggle("selected-live-card", isActive);
                    const badge = item.querySelector(".live-card-summary em");
                    if (badge) {
                        badge.textContent = isActive ? "siguiendo" : "ver";
                    }
                });
                renderSelected(true);
            });
        });
    }

    async function renderSelected(resetRange = false) {
        if (!selected) {
            return;
        }

        const currentRequest = ++requestId;
        const symbol = selected.dataset.symbol;
        const interval = selected.dataset.interval || "1m";
        const from = selected.dataset.from || "";
        const url = `/api/grafico-vivo?symbol=${encodeURIComponent(symbol)}&interval=${encodeURIComponent(interval)}&mercado=${encodeURIComponent(market)}&from=${encodeURIComponent(from)}&estado=todas`;

        try {
            const response = await fetch(url, { cache: "no-store" });
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const snapshot = await response.json();
            if (currentRequest !== requestId) {
                return;
            }

            candles = (snapshot.candles || []).map(toCandle).filter(item => Number.isFinite(item.time));
            if (candles.length === 0) {
                showEmpty(symbol);
                return;
            }

            marketSeries.setData(isForex ? candles.map(candle => ({ time: candle.time, value: candle.close })) : candles);
            routeSeries.setData(buildRoute());
            drawLevels();
            drawMarkers();
            updateStrip(snapshot);
            updateDetail(snapshot);
            if (resetRange) {
                chart.timeScale().fitContent();
            } else {
                chart.timeScale().scrollToRealTime();
            }
        } catch {
            showEmpty(symbol);
        }
    }

    function buildRoute() {
        const entryTime = nearestTime(toUnix(selected.dataset.entryTime));
        const entryPrice = Number(selected.dataset.entryPrice);
        const points = [];

        if (Number.isFinite(entryTime) && Number.isFinite(entryPrice)) {
            points.push({ time: entryTime, value: entryPrice });
        }

        candles
            .filter(candle => candle.time >= entryTime)
            .forEach(candle => points.push({ time: candle.time, value: candle.close }));

        return [...new Map(points.map(point => [point.time, point])).values()]
            .filter(point => Number.isFinite(point.time) && Number.isFinite(point.value) && point.value > 0)
            .sort((a, b) => a.time - b.time);
    }

    function drawLevels() {
        priceLines.forEach(line => marketSeries.removePriceLine(line));
        priceLines = [];
        const entryPrice = Number(selected.dataset.entryPrice);
        const last = candles.at(-1);
        if (Number.isFinite(entryPrice) && entryPrice > 0) {
            priceLines.push(marketSeries.createPriceLine({
                price: entryPrice,
                color: colors.yellow,
                lineWidth: 1,
                lineStyle: lwc.LineStyle?.Dashed ?? 2,
                axisLabelVisible: true,
                title: selected.dataset.side === "Long" ? "Comprar" : "Vender"
            }));
        }

        if (last) {
            priceLines.push(marketSeries.createPriceLine({
                price: last.close,
                color: colors.blue,
                lineWidth: 1,
                lineStyle: lwc.LineStyle?.Dashed ?? 2,
                axisLabelVisible: true,
                title: "Mercado actual"
            }));
        }
    }

    function drawMarkers() {
        const isLong = selected.dataset.side === "Long";
        const entryTime = nearestTime(toUnix(selected.dataset.entryTime));
        const last = candles.at(-1);
        const markers = [];

        if (Number.isFinite(entryTime)) {
            markers.push({
                time: entryTime,
                position: isLong ? "belowBar" : "aboveBar",
                color: colors.yellow,
                shape: isLong ? "arrowUp" : "arrowDown",
                text: isLong ? "Comprar" : "Vender"
            });
        }

        if (last) {
            markers.push({
                time: last.time,
                position: isLong ? "aboveBar" : "belowBar",
                color: colors.blue,
                shape: "circle",
                text: "Actual"
            });
        }

        if (lwc.createSeriesMarkers) {
            if (!markerApi) {
                markerApi = lwc.createSeriesMarkers(marketSeries, markers);
            } else {
                markerApi.setMarkers(markers);
            }
        } else if (marketSeries.setMarkers) {
            marketSeries.setMarkers(markers);
        }
    }

    function updateStrip(snapshot) {
        const last = candles.at(-1);
        const previous = candles.at(-2) || last;
        const change = last.close - previous.close;
        const changePercent = previous.close === 0 ? 0 : change / previous.close * 100;
        strip.innerHTML = `
            <strong>${escapeHtml(snapshot.symbol)} ${escapeHtml(snapshot.interval)}</strong>
            <span class="${change >= 0 ? "gain" : "loss"}">${formatPrice(last.close)} ${change >= 0 ? "+" : ""}${changePercent.toFixed(2)}%</span>
            <span>${escapeHtml(selected.dataset.trader || "")}</span>
            <span>${formatChartDateTime(last.time)}</span>`;
    }

    function updateDetail(snapshot) {
        const last = candles.at(-1);
        const entry = Number(selected.dataset.entryPrice);
        const isLong = selected.dataset.side === "Long";
        const gross = isLong ? last.close - entry : entry - last.close;
        const grossPercent = entry <= 0 ? 0 : gross / entry * 100;
        detail.innerHTML = `
            <strong>${escapeHtml(selected.dataset.trader || "")} | ${escapeHtml(selected.dataset.sideLabel || "")}</strong>
            <span>Entrada ${formatPrice(entry)}. Mercado actual ${formatPrice(last.close)}. Movimiento bruto ${grossPercent >= 0 ? "+" : ""}${grossPercent.toFixed(2)}%.</span>
            ${selected.dataset.sourceUrl ? `<a href="${escapeAttribute(selected.dataset.sourceUrl)}" target="_blank" rel="noopener">Abrir operación original</a>` : ""}`;
    }

    function showEmpty(symbol) {
        strip.innerHTML = `<strong>${escapeHtml(symbol || "Sin datos")}</strong><span>No pude cargar mercado.</span>`;
    }

    function showHover(param) {
        if (!hover || !param?.time || !param.point || param.point.x < 0 || param.point.y < 0) {
            if (hover) {
                hover.hidden = true;
            }
            return;
        }

        const seriesValue = param.seriesData.get(marketSeries);
        const candle = isForex ? candles.find(item => item.time === toUnix(param.time)) : seriesValue;
        if (!candle) {
            hover.hidden = true;
            return;
        }

        const change = Number(candle.close) - Number(candle.open);
        const changePercent = Number(candle.open) === 0 ? 0 : change / Number(candle.open) * 100;
        hover.innerHTML = `
            <strong>${formatChartDateTime(param.time)}</strong>
            <dl>
                <div><dt>Abrió</dt><dd>${formatPrice(candle.open)}</dd></div>
                <div><dt>Alto</dt><dd>${formatPrice(candle.high)}</dd></div>
                <div><dt>Bajo</dt><dd>${formatPrice(candle.low)}</dd></div>
                <div><dt>Cerró</dt><dd class="${change >= 0 ? "gain" : "loss"}">${formatPrice(candle.close)} (${changePercent.toFixed(2)}%)</dd></div>
            </dl>`;
        hover.hidden = false;
        hover.style.transform = `translate(${Math.min(chartHost.clientWidth - 230, Math.max(12, param.point.x + 16))}px, ${Math.min(chartHost.clientHeight - 126, Math.max(12, param.point.y + 16))}px)`;
    }

    function nearestTime(target) {
        if (!Number.isFinite(target) || candles.length === 0) {
            return candles.at(-1)?.time;
        }

        let best = candles[0].time;
        let distance = Math.abs(best - target);
        for (const candle of candles) {
            const nextDistance = Math.abs(candle.time - target);
            if (nextDistance < distance) {
                best = candle.time;
                distance = nextDistance;
            }
        }

        return best;
    }

    function toCandle(item) {
        return {
            time: toUnix(item.closeTime),
            open: Number(item.open),
            high: Number(item.high),
            low: Number(item.low),
            close: Number(item.close)
        };
    }

    function toUnix(value) {
        if (!value) {
            return Number.NaN;
        }

        if (typeof value === "number") {
            return Math.floor(value);
        }

        return Math.floor(new Date(value).getTime() / 1000);
    }

    function chartHeight() {
        return chartHost.clientWidth < 760 ? 420 : 620;
    }

    function formatPrice(value) {
        const number = Number(value || 0);
        const decimals = Math.abs(number) >= 1000 ? 2 : Math.abs(number) >= 1 ? 4 : 8;
        return number.toLocaleString("en-US", { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
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
})();
