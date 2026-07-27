(() => {
    const containers = document.querySelectorAll("[data-master-detail]");
    const appTimeZone = resolveTimeZone(document.documentElement.dataset.appTimezone);

    containers.forEach(container => {
        const items = directItems(container);
        const panels = directPanels(container);

        if (items.length === 0 || panels.length === 0) {
            return;
        }

        setupInternalFilters(container, items);

        items.forEach(item => {
            item.addEventListener("click", () => activate(container, item.dataset.detailKey));
        });

        const active = items.find(item => item.classList.contains("active")) || items[0];
        activate(container, active.dataset.detailKey);
    });

    function activate(container, key) {
        directItems(container).forEach(item => {
            item.classList.toggle("active", item.dataset.detailKey === key);
        });

        directPanels(container).forEach(panel => {
            const isActive = panel.getAttribute("data-detail-panel") === key;
            panel.classList.toggle("active", isActive);

            if (isActive) {
                renderSignalReplay(panel);
            }
        });
    }

    function directItems(container) {
        const list = container.querySelector(":scope > .master-list");
        return list ? [...list.querySelectorAll(":scope > [data-detail-key]")] : [];
    }

    function directPanels(container) {
        return [...container.querySelectorAll(":scope > .detail-stack > [data-detail-panel]")];
    }

    function setupInternalFilters(container, items) {
        if (items.length < 8 || container.querySelector(":scope > .internal-filter-bar")) {
            return;
        }

        const list = container.querySelector(":scope > .master-list");
        if (!list) {
            return;
        }

        const statuses = unique(items.map(item => item.dataset.filterStatus).filter(Boolean));
        const types = unique(items.map(item => item.dataset.filterType).filter(Boolean));
        const filter = document.createElement("div");
        filter.className = "internal-filter-bar";
        filter.innerHTML = `
            <label>
                <span>Buscar</span>
                <input type="search" data-internal-search placeholder="Filtrar esta lista"/>
            </label>
            ${statuses.length > 1 ? `<label><span>Estado</span><select data-internal-status><option value="">Todos</option>${statuses.map(value => `<option value="${escapeAttribute(value)}">${escapeHtml(value)}</option>`).join("")}</select></label>` : ""}
            ${types.length > 1 ? `<label><span>Tipo</span><select data-internal-type><option value="">Todos</option>${types.map(value => `<option value="${escapeAttribute(value)}">${escapeHtml(value)}</option>`).join("")}</select></label>` : ""}
            <strong data-filter-count>${items.length}</strong>`;
        container.insertBefore(filter, list);

        const search = filter.querySelector("[data-internal-search]");
        const status = filter.querySelector("[data-internal-status]");
        const type = filter.querySelector("[data-internal-type]");
        const count = filter.querySelector("[data-filter-count]");
        const apply = () => {
            const query = (search?.value || "").trim().toLowerCase();
            const selectedStatus = status?.value || "";
            const selectedType = type?.value || "";
            let visible = 0;

            items.forEach(item => {
                const text = (item.dataset.filterText || item.textContent || "").toLowerCase();
                const matches = (!query || text.includes(query))
                    && (!selectedStatus || item.dataset.filterStatus === selectedStatus)
                    && (!selectedType || item.dataset.filterType === selectedType);

                item.hidden = !matches;
                if (matches) {
                    visible += 1;
                }
            });

            if (count) {
                count.textContent = String(visible);
            }

            const active = items.find(item => item.classList.contains("active"));
            if (!active || active.hidden) {
                const firstVisible = items.find(item => !item.hidden);
                if (firstVisible) {
                    activate(container, firstVisible.dataset.detailKey);
                }
            }
        };

        search?.addEventListener("input", apply);
        status?.addEventListener("change", apply);
        type?.addEventListener("change", apply);
    }

    async function renderSignalReplay(panel) {
        const host = panel.querySelector("[data-signal-chart]");
        if (!host || host.dataset.rendered === "true" || !window.LightweightCharts) {
            return;
        }

        host.dataset.rendered = "true";

        try {
            const url = `/api/grafico-vivo?symbol=${encodeURIComponent(panel.dataset.symbol)}&interval=${encodeURIComponent(panel.dataset.interval)}&capital=${encodeURIComponent(panel.dataset.capital || "1000")}&estado=todas&from=${encodeURIComponent(panel.dataset.from || "")}&to=${encodeURIComponent(panel.dataset.to || "")}`;
            const response = await fetch(url, { cache: "no-store" });
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const snapshot = await response.json();
            drawReplay(host, panel, snapshot);
        } catch {
            host.innerHTML = "<span>No pude cargar el replay de velas para esta senal.</span>";
        }
    }

    function drawReplay(host, panel, snapshot) {
        const lwc = window.LightweightCharts;
        const candles = (snapshot.candles || []).map(toCandle).filter(item => Number.isFinite(item.time));

        if (candles.length === 0) {
            host.innerHTML = "<span>Sin velas suficientes para esta ventana.</span>";
            return;
        }

        host.innerHTML = "";
        const chart = lwc.createChart(host, {
            width: host.clientWidth,
            height: host.clientWidth < 760 ? 320 : 380,
            autoSize: true,
            layout: {
                background: { type: lwc.ColorType?.Solid ?? "solid", color: "#0b0e11" },
                textColor: "#eaecef",
                fontFamily: "Inter, Segoe UI, sans-serif"
            },
            grid: {
                vertLines: { color: "#1e2329" },
                horzLines: { color: "#1e2329" }
            },
            rightPriceScale: {
                borderColor: "#1e2329",
                autoScale: true,
                scaleMargins: { top: 0.08, bottom: 0.18 }
            },
            timeScale: {
                borderColor: "#1e2329",
                timeVisible: true,
                secondsVisible: true,
                rightOffset: 5,
                barSpacing: 8,
                minBarSpacing: 2,
                tickMarkFormatter: formatChartTick
            },
            localization: {
                locale: navigator.language || "es-MX",
                priceFormatter: formatPrice,
                timeFormatter: formatChartDateTime
            }
        });

        const candleSeries = addSeries(lwc, chart, "candlestick", {
            upColor: "#0ecb81",
            downColor: "#f6465d",
            borderUpColor: "#0ecb81",
            borderDownColor: "#f6465d",
            wickUpColor: "#0ecb81",
            wickDownColor: "#f6465d",
            lastValueVisible: true
        });
        candleSeries.setData(candles);
        const hover = createHoverCard(host);

        const route = buildRoute(panel, candles);
        if (route.length >= 2) {
            const routeSeries = addSeries(lwc, chart, "line", {
                color: "#f0b90b",
                lineWidth: 3,
                priceLineVisible: false,
                lastValueVisible: false
            });
            routeSeries.setData(route);
        }

        addLevel(candleSeries, Number(panel.dataset.entryPrice), "#f0b90b", entryLabel(panel));
        addLevel(candleSeries, Number(panel.dataset.takeProfit1), "#0ecb81", profitLabel(panel));
        addLevel(candleSeries, Number(panel.dataset.takeProfit2), "#0ecb81", `${profitLabel(panel)} extra`);
        addLevel(candleSeries, Number(panel.dataset.stopLoss), "#f6465d", "Salir por perdida max");

        const markers = buildMarkers(panel, candles);
        if (lwc.createSeriesMarkers) {
            lwc.createSeriesMarkers(candleSeries, markers);
        } else if (candleSeries.setMarkers) {
            candleSeries.setMarkers(markers);
        }

        chart.subscribeCrosshairMove(param => showHoverInfo(host, hover, candleSeries, param));
        chart.timeScale().fitContent();
        new ResizeObserver(() => {
            chart.applyOptions({ width: host.clientWidth, height: host.clientWidth < 760 ? 320 : 380 });
        }).observe(host);
    }

    function addSeries(lwc, chart, kind, options) {
        if (kind === "candlestick") {
            return chart.addSeries && lwc.CandlestickSeries
                ? chart.addSeries(lwc.CandlestickSeries, options)
                : chart.addCandlestickSeries(options);
        }

        return chart.addSeries && lwc.LineSeries
            ? chart.addSeries(lwc.LineSeries, options)
            : chart.addLineSeries(options);
    }

    function buildRoute(panel, candles) {
        const entryTime = nearestTime(toUnix(panel.dataset.observedAt), candles);
        const exitTime = nearestTime(toUnix(panel.dataset.exitTime), candles);
        const entryPrice = Number(panel.dataset.entryPrice);
        const exitPrice = Number(panel.dataset.exitPrice);
        const points = [];

        if (Number.isFinite(entryTime) && Number.isFinite(entryPrice)) {
            points.push({ time: entryTime, value: entryPrice });
        }

        candles
            .filter(candle => candle.time >= entryTime && candle.time <= exitTime)
            .forEach(candle => points.push({ time: candle.time, value: candle.close }));

        if (Number.isFinite(exitTime)) {
            points.push({
                time: exitTime,
                value: Number.isFinite(exitPrice) && exitPrice > 0 ? exitPrice : candles.at(-1).close
            });
        }

        return [...new Map(points.map(point => [point.time, point])).values()]
            .filter(point => Number.isFinite(point.time) && Number.isFinite(point.value) && point.value > 0)
            .sort((a, b) => a.time - b.time);
    }

    function buildMarkers(panel, candles) {
        const isLong = panel.dataset.side === "Long";
        const entryTime = nearestTime(toUnix(panel.dataset.observedAt), candles);
        const exitTime = nearestTime(toUnix(panel.dataset.exitTime), candles);
        const resultColor = panel.dataset.status === "Perdida" ? "#f6465d" : panel.dataset.status === "Abierta" ? "#2ab5f6" : "#0ecb81";
        const markers = [];

        if (Number.isFinite(entryTime)) {
            markers.push({
                time: entryTime,
                position: isLong ? "belowBar" : "aboveBar",
                color: "#f0b90b",
                shape: isLong ? "arrowUp" : "arrowDown",
                text: isLong ? "Comprar" : "Vender"
            });
        }

        if (Number.isFinite(exitTime)) {
            markers.push({
                time: exitTime,
                position: isLong ? "aboveBar" : "belowBar",
                color: resultColor,
                shape: panel.dataset.status === "Abierta" ? "circle" : "square",
                text: isLong ? "Vender" : "Comprar"
            });
        }

        return markers;
    }

    function addLevel(series, price, color, title) {
        if (!Number.isFinite(price) || price <= 0) {
            return;
        }

        series.createPriceLine({
            price,
            color,
            lineWidth: 1,
            lineStyle: window.LightweightCharts.LineStyle?.Dashed ?? 2,
            axisLabelVisible: true,
            title
        });
    }

    function entryLabel(panel) {
        return panel.dataset.side === "Long" ? "Comprar entrada" : "Vender entrada";
    }

    function profitLabel(panel) {
        return panel.dataset.side === "Long" ? "Vender con ganancia" : "Comprar con ganancia";
    }

    function nearestTime(target, candles) {
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

        return Math.floor(new Date(value).getTime() / 1000);
    }

    function createHoverCard(host) {
        const hover = document.createElement("div");
        hover.className = "chart-hover-card report-chart-hover";
        hover.hidden = true;
        host.appendChild(hover);
        return hover;
    }

    function showHoverInfo(host, hover, candleSeries, param) {
        if (!hover || !param?.time || !param.point || param.point.x < 0 || param.point.y < 0) {
            if (hover) {
                hover.hidden = true;
            }
            return;
        }

        const candle = param.seriesData.get(candleSeries);
        if (!candle) {
            hover.hidden = true;
            return;
        }

        const change = Number(candle.close) - Number(candle.open);
        const changePercent = Number(candle.open) === 0 ? 0 : change / Number(candle.open) * 100;
        hover.innerHTML = `
            <strong>${escapeHtml(formatChartDateTime(param.time))}</strong>
            <dl>
                <div><dt>Abrio</dt><dd>${formatPrice(candle.open)}</dd></div>
                <div><dt>Alto</dt><dd>${formatPrice(candle.high)}</dd></div>
                <div><dt>Bajo</dt><dd>${formatPrice(candle.low)}</dd></div>
                <div><dt>Cerro</dt><dd class="${change >= 0 ? "gain" : "loss"}">${formatPrice(candle.close)} (${changePercent.toFixed(2)}%)</dd></div>
            </dl>`;
        hover.hidden = false;

        const x = Math.min(host.clientWidth - 230, Math.max(12, param.point.x + 16));
        const y = Math.min(host.clientHeight - 126, Math.max(12, param.point.y + 16));
        hover.style.transform = `translate(${x}px, ${y}px)`;
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

    function unique(values) {
        return [...new Set(values)].sort((a, b) => a.localeCompare(b));
    }

    function formatPrice(value) {
        const number = Number(value || 0);
        const decimals = Math.abs(number) >= 1000 ? 2 : Math.abs(number) >= 1 ? 4 : 8;
        return number.toLocaleString("en-US", { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
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
