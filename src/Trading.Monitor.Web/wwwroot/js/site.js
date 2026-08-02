(() => {
    const storageKey = "trading-monitor-theme";
    const root = document.documentElement;
    const toggle = document.getElementById("themeToggle");
    const stored = localStorage.getItem(storageKey);
    const prefersDark = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
    const theme = stored || (prefersDark ? "dark" : "light");

    root.dataset.theme = theme;

    if (toggle) {
        toggle.checked = theme === "dark";
        toggle.addEventListener("change", () => {
            const next = toggle.checked ? "dark" : "light";
            root.dataset.theme = next;
            localStorage.setItem(storageKey, next);
        });
    }
})();

(() => {
    document.querySelectorAll("[data-auto-submit]").forEach(form => {
        const delay = Math.max(150, Number(form.dataset.autoSubmitDelay || 360));
        const state = form.querySelector("[data-auto-submit-state]");
        let timer;

        const submit = () => {
            window.clearTimeout(timer);
            form.requestSubmit();
        };
        const submitSoon = () => {
            window.clearTimeout(timer);
            timer = window.setTimeout(submit, delay);
        };

        form.addEventListener("submit", () => {
            form.classList.add("is-applying");
            form.setAttribute("aria-busy", "true");
            if (state) {
                state.textContent = "Aplicando…";
            }
        });

        form.querySelectorAll("select, input[type='checkbox'], input[type='radio']").forEach(control => {
            control.addEventListener("change", submit);
        });
        form.querySelectorAll("input[type='number'], input[type='date']").forEach(control => {
            control.addEventListener("change", submit);
        });
        form.querySelectorAll("input[type='search'], input[type='text']").forEach(control => {
            control.addEventListener("input", submitSoon);
        });
    });
})();

(() => {
    const canvas = document.getElementById("liveTradesChart");
    const list = document.getElementById("liveTradeCards");
    const updated = document.getElementById("liveUpdated");
    const board = document.querySelector("[data-live-capital]");
    const chartAnalysis = document.getElementById("chartAnalysis");

    if (!canvas || !list || !board) {
        return;
    }

    const context = canvas.getContext("2d");
    const capital = board.dataset.liveCapital || "1000";
    const estado = board.dataset.liveEstado || "abiertas";
    const symbolFilter = board.dataset.liveSymbol || "";
    const chartZoomInput = document.getElementById("chartZoom");
    const chartZoomLabel = document.getElementById("chartZoomLabel");
    let selectedSymbol = null;
    let selectedInterval = "1m";
    let chartZoom = Number(chartZoomInput?.value || 45);
    let lastChartSnapshot = null;
    let lastOperations = [];

    async function refreshLiveTrades() {
        try {
            const response = await fetch(`/api/operaciones-vivas?capital=${encodeURIComponent(capital)}&estado=${encodeURIComponent(estado)}&symbol=${encodeURIComponent(symbolFilter)}`, { cache: "no-store" });
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const data = await response.json();
            lastOperations = data.operations || [];
            selectedSymbol = symbolFilter || selectedSymbol || pickChartSymbol(lastOperations);
            renderList(lastOperations);
            await refreshChart(selectedSymbol);

            if (updated) {
                const date = new Date(data.serverTime);
                updated.textContent = `actualizado ${date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" })}`;
            }
        } catch {
            list.innerHTML = `<div class="empty-state"><strong>No pude refrescar.</strong><span>El servicio sigue corriendo; reintentare solo.</span></div>`;
        }
    }

    async function refreshChart(symbol) {
        const chartSymbol = symbol || pickChartSymbol(lastOperations);
        const response = await fetch(`/api/grafico-vivo?symbol=${encodeURIComponent(chartSymbol)}&interval=${encodeURIComponent(selectedInterval)}&capital=${encodeURIComponent(capital)}&estado=${encodeURIComponent(estado)}`, { cache: "no-store" });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        renderChartV2(await response.json());
    }

    function renderList(operations) {
        if (operations.length === 0) {
            list.innerHTML = `<div class="empty-state"><strong>Sin operaciones.</strong><span>Esperando una señal clara.</span></div>`;
            return;
        }

        list.innerHTML = operations.slice(0, 6).map(item => `
            <article class="live-card ${escapeHtml(item.signalClass)}">
                <div>
                    <span>${escapeHtml(item.action)}</span>
                    <strong>${escapeHtml(item.symbol)} ${escapeHtml(item.side)}</strong>
                    <small>${escapeHtml(item.status)} | score ${item.score}/100</small>
                </div>
                <dl>
                    <div><dt>Entrar</dt><dd>${formatTime(item.entryAt)}-${formatTime(item.entryUntil)}</dd></div>
                    <div><dt>Precio entrada</dt><dd>${formatPrice(item.entryLower)}-${formatPrice(item.entryUpper)}</dd></div>
                    <div><dt>Ganancia objetivo</dt><dd class="gain">${escapeHtml(item.potentialTp1)}</dd></div>
                    <div><dt>Pérdida max</dt><dd class="loss">${escapeHtml(item.potentialStop)}</dd></div>
                    <div><dt>Salir antes de</dt><dd>${formatTime(item.exitBy)}</dd></div>
                    <div><dt>Tiempo</dt><dd>${escapeHtml(item.timeText)}</dd></div>
                    <div><dt>Resultado final</dt><dd class="${item.realizedText.startsWith("-") ? "loss" : item.realizedText === "Abierta" ? "" : "gain"}">${escapeHtml(item.realizedText)}</dd></div>
                    <div><dt>Cambio</dt><dd>${escapeHtml(item.realizedPercent)}</dd></div>
                    <div><dt>Cantidad</dt><dd>${escapeHtml(item.quantityText)}</dd></div>
                    <div><dt>Inicio/final</dt><dd>${escapeHtml(item.entryExitText)}</dd></div>
                </dl>
                <p class="live-calc">${escapeHtml(item.realizedFormulaText)}</p>
                <div class="trade-links">
                    <button type="button" data-chart-symbol="${escapeHtml(item.symbol)}">Ver gráfico</button>
                    ${(item.links || []).slice(0, 5).map(link => link.url?.startsWith("/") ? `<a href="${escapeAttribute(link.url)}">${escapeHtml(link.label)}</a>` : `<a href="${escapeAttribute(link.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(link.label)}</a>`).join("")}
                </div>
            </article>`).join("");

        list.querySelectorAll("[data-chart-symbol]").forEach(button => {
            button.addEventListener("click", async () => {
                selectedSymbol = button.getAttribute("data-chart-symbol") || selectedSymbol;
                await refreshChart(selectedSymbol);
            });
    });

    document.querySelector("[data-chart-zoom-out]")?.addEventListener("click", async () => {
        setChartZoom(chartZoom - 10);
    });

    document.querySelector("[data-chart-zoom-in]")?.addEventListener("click", async () => {
        setChartZoom(chartZoom + 10);
    });

    chartZoomInput?.addEventListener("input", () => {
        setChartZoom(Number(chartZoomInput.value));
    });

    canvas.addEventListener("wheel", event => {
        event.preventDefault();
        setChartZoom(chartZoom + (event.deltaY < 0 ? 8 : -8));
    }, { passive: false });

    function setChartZoom(value) {
        chartZoom = Math.max(0, Math.min(100, Number(value) || 0));

        if (chartZoomInput) {
            chartZoomInput.value = String(chartZoom);
        }

        if (chartZoomLabel) {
            chartZoomLabel.textContent = chartZoom >= 75 ? "cerca" : chartZoom <= 25 ? "lejos" : "medio";
        }

        if (lastChartSnapshot) {
            renderChartV2(lastChartSnapshot);
        }
    }
    }

    document.querySelectorAll("[data-chart-symbol-button]").forEach(button => {
        if (symbolFilter && button.dataset.chartSymbolButton !== symbolFilter) {
            button.disabled = true;
        }

        if ((symbolFilter || "BTCUSDT") === button.dataset.chartSymbolButton) {
            button.classList.add("active");
        }

        button.addEventListener("click", async () => {
            selectedSymbol = button.dataset.chartSymbolButton || selectedSymbol;
            document.querySelectorAll("[data-chart-symbol-button]").forEach(item => item.classList.toggle("active", item === button));
            await refreshChart(selectedSymbol);
        });
    });

    document.querySelectorAll("[data-chart-interval]").forEach(button => {
        button.addEventListener("click", async () => {
            selectedInterval = button.dataset.chartInterval || selectedInterval;
            document.querySelectorAll("[data-chart-interval]").forEach(item => item.classList.toggle("active", item === button));
            await refreshChart(selectedSymbol);
        });
    });

    function renderChart(snapshot) {
        const width = canvas.clientWidth || canvas.width;
        const height = 430;
        const ratio = window.devicePixelRatio || 1;

        canvas.width = width * ratio;
        canvas.height = height * ratio;
        canvas.style.height = `${height}px`;
        context.setTransform(ratio, 0, 0, ratio, 0, 0);
        context.clearRect(0, 0, width, height);

        const styles = getComputedStyle(document.documentElement);
        const ink = styles.getPropertyValue("--ink").trim();
        const muted = styles.getPropertyValue("--muted").trim();
        const line = styles.getPropertyValue("--line").trim();
        const good = styles.getPropertyValue("--good").trim();
        const bad = styles.getPropertyValue("--bad").trim();
        const warn = styles.getPropertyValue("--warn").trim();
        const surface = styles.getPropertyValue("--surface-2").trim();
        const candles = snapshot.candles || [];
        const operations = snapshot.operations || [];
        const active = operations[0];

        context.fillStyle = surface;
        context.fillRect(0, 0, width, height);

        if (candles.length === 0) {
            context.fillStyle = muted;
            context.font = "600 15px Inter, sans-serif";
            context.fillText("Esperando velas de mercado", 24, 44);
            return;
        }

        const values = candles.flatMap(item => [item.high, item.low])
            .concat(operations.flatMap(item => [item.stopLoss, item.entryLower, item.entryUpper, item.takeProfit1, item.takeProfit2, item.exitPrice || item.lastPrice]));
        const min = Math.min(...values);
        const max = Math.max(...values);
        const padding = Math.max((max - min) * 0.08, max * 0.0005);
        const scaleMin = min - padding;
        const scaleMax = max + padding;
        const left = 56;
        const right = width - 76;
        const top = 42;
        const bottom = height - 42;
        const candleGap = 3;
        const candleWidth = Math.max(4, (right - left) / candles.length - candleGap);

        drawHeader(snapshot, active, ink, muted, good, bad);
        drawGrid(left, right, top, bottom, line, muted);
        drawCandles(candles, left, bottom, candleWidth, candleGap, good, bad);

        operations.slice(0, 4).forEach(item => {
            drawZone(item.entryLower, item.entryUpper, warn, "ENTRADA");
            drawLine(item.takeProfit1, good, "GANAR");
            drawLine(item.takeProfit2, good, "GANAR MAS");
            drawLine(item.stopLoss, bad, "CORTAR PERDIDA");
        });

        const last = candles[candles.length - 1];
        drawLine(last.close, ink, "PRECIO");

        function toY(value) {
            if (scaleMax <= scaleMin) {
                return bottom;
            }

            return bottom - ((value - scaleMin) / (scaleMax - scaleMin)) * (bottom - top);
        }

        function drawHeader(chart, item, inkColor, mutedColor, goodColor, badColor) {
            const last = candles[candles.length - 1];
            const prev = candles[candles.length - 2] || last;
            const change = last.close - prev.close;
            context.fillStyle = inkColor;
            context.font = "800 15px Inter, sans-serif";
            context.fillText(`${chart.symbol} · ${chart.interval}`, 18, 24);
            context.fillStyle = change >= 0 ? goodColor : badColor;
            context.fillText(`${formatPrice(last.close)} (${change >= 0 ? "+" : ""}${formatPrice(change)})`, 132, 24);
            context.fillStyle = mutedColor;
            context.font = "700 12px Inter, sans-serif";
            context.fillText(item ? `${item.action} · entrar ${formatTime(item.entryAt)}-${formatTime(item.entryUntil)} · salir antes de ${formatTime(item.exitBy)}` : "Sin operación viva seleccionada", 18, height - 14);
        }

        function drawGrid(leftX, rightX, topY, bottomY, lineColor, mutedColor) {
            context.strokeStyle = lineColor;
            context.lineWidth = 1;
            context.font = "600 11px Inter, sans-serif";
            context.fillStyle = mutedColor;

            for (let i = 0; i <= 4; i++) {
                const y = topY + ((bottomY - topY) / 4) * i;
                const value = scaleMax - ((scaleMax - scaleMin) / 4) * i;
                context.beginPath();
                context.moveTo(leftX, y);
                context.lineTo(rightX, y);
                context.stroke();
                context.fillText(formatPrice(value), rightX + 8, y + 4);
            }
        }

        function drawCandles(items, leftX, bottomY, bodyWidth, gap, goodColor, badColor) {
            items.forEach((candle, index) => {
                const x = leftX + index * (bodyWidth + gap);
                const center = x + bodyWidth / 2;
                const openY = toY(candle.open);
                const closeY = toY(candle.close);
                const highY = toY(candle.high);
                const lowY = toY(candle.low);
                const up = candle.close >= candle.open;
                const color = up ? goodColor : badColor;

                context.strokeStyle = color;
                context.fillStyle = color;
                context.lineWidth = 1.5;
                context.beginPath();
                context.moveTo(center, highY);
                context.lineTo(center, lowY);
                context.stroke();
                context.fillRect(x, Math.min(openY, closeY), bodyWidth, Math.max(2, Math.abs(openY - closeY)));
            });
        }

        function drawLine(value, color, label) {
            const y = toY(value);
            context.strokeStyle = color;
            context.fillStyle = color;
            context.lineWidth = 1.5;
            context.setLineDash([6, 5]);
            context.beginPath();
            context.moveTo(left, y);
            context.lineTo(right, y);
            context.stroke();
            context.setLineDash([]);
            context.font = "800 11px Inter, sans-serif";
            context.fillText(`${label} ${formatPrice(value)}`, left + 8, y - 5);
        }

        function drawZone(low, high, color, label) {
            const topY = toY(high);
            const bottomY = toY(low);
            context.fillStyle = colorWithAlpha(color, 0.12);
            context.fillRect(left, topY, right - left, Math.max(3, bottomY - topY));
            drawLine((low + high) / 2, color, label);
        }
    }

    function renderChartV2(snapshot) {
        lastChartSnapshot = snapshot;
        const width = Math.max(620, canvas.clientWidth || canvas.width);
        const height = 480;
        const ratio = window.devicePixelRatio || 1;
        const palette = {
            background: "#08111f",
            panel: "#0d1728",
            grid: "#1d2a3d",
            text: "#e5edf7",
            muted: "#91a3b8",
            green: "#22c55e",
            red: "#ef4444",
            amber: "#f59e0b",
            blue: "#38bdf8",
            entryFill: "rgba(245, 158, 11, 0.14)",
            greenFill: "rgba(34, 197, 94, 0.12)",
            redFill: "rgba(239, 68, 68, 0.10)"
        };

        canvas.width = width * ratio;
        canvas.height = height * ratio;
        canvas.style.height = `${height}px`;
        context.setTransform(ratio, 0, 0, ratio, 0, 0);
        context.clearRect(0, 0, width, height);
        context.fillStyle = palette.background;
        context.fillRect(0, 0, width, height);

        const rawCandles = snapshot.candles || [];
        const maxVisible = Math.min(rawCandles.length || 120, width < 760 ? 90 : 120);
        const minVisible = width < 760 ? 16 : 22;
        const visibleCount = Math.max(minVisible, Math.round(maxVisible - ((maxVisible - minVisible) * (chartZoom / 100))));
        const candles = rawCandles.slice(-visibleCount);
        const operations = snapshot.operations || [];
        const selectedOperation = operations.find(item => item.highlight) || operations.find(item => item.status === "Abierta") || operations[0] || null;
        const analysisTrade = buildAnalysisTrade(snapshot.analysis);
        const trade = selectedOperation || analysisTrade;

        updateChartAnalysis(snapshot, selectedOperation, analysisTrade);

        if (candles.length === 0) {
            drawEmpty("Esperando velas de mercado");
            return;
        }

        const chart = {
            left: 58,
            right: width - 86,
            top: 62,
            bottom: height - 88,
            volumeTop: height - 74,
            volumeBottom: height - 34
        };
        const priceValues = candles.flatMap(item => [item.high, item.low]);
        if (trade) {
            priceValues.push(trade.stopLoss, trade.entryLower, trade.entryUpper, trade.takeProfit1, trade.takeProfit2, trade.exitPrice || trade.lastPrice);
        }

        const min = Math.min(...priceValues);
        const max = Math.max(...priceValues);
        const padding = Math.max((max - min) * 0.12, max * 0.0008);
        const scaleMin = min - padding;
        const scaleMax = max + padding;
        const slot = (chart.right - chart.left) / candles.length;
        const bodyWidth = Math.max(5, Math.min(12, slot * 0.62));
        const maxVolume = Math.max(...candles.map(candle => candle.volume), 1);
        const priceLabelBoxes = [];

        drawPanel();
        drawHeader();
        drawGrid();
        drawTradeZones();
        drawCandles();
        drawVolume();
        drawTimeAxis();
        drawCurrentPrice();
        drawFooter();

        function toY(value) {
            return chart.bottom - ((value - scaleMin) / (scaleMax - scaleMin)) * (chart.bottom - chart.top);
        }

        function toX(index) {
            return chart.left + slot * index + slot / 2;
        }

        function drawPanel() {
            context.fillStyle = palette.panel;
            roundRect(12, 12, width - 24, height - 24, 8);
            context.fill();
        }

        function drawHeader() {
            const last = candles[candles.length - 1];
            const prev = candles[candles.length - 2] || last;
            const change = last.close - prev.close;
            const changePercent = prev.close === 0 ? 0 : change / prev.close * 100;

            context.fillStyle = palette.text;
            context.font = "800 16px Inter, sans-serif";
            const header = `${snapshot.symbol} | ${snapshot.interval} | ${snapshot.analysis?.horizon || "Mercado"}`;
            context.fillText(header, 26, 34);

            context.fillStyle = change >= 0 ? palette.green : palette.red;
            context.font = "800 15px Inter, sans-serif";
            const priceX = Math.min(width - 340, 26 + context.measureText(header).width + 18);
            context.fillText(`${formatPrice(last.close)} (${change >= 0 ? "+" : ""}${changePercent.toFixed(2)}%)`, priceX, 34);

            drawLegend(width - 430, 26, palette.amber, "Entrada");
            drawLegend(width - 330, 26, palette.green, "Ganancia");
            drawLegend(width - 220, 26, palette.red, "Pérdida max");
            drawLegend(width - 110, 26, palette.blue, "Precio");
        }

        function drawGrid() {
            context.strokeStyle = palette.grid;
            context.lineWidth = 1;
            context.font = "600 11px Inter, sans-serif";
            context.fillStyle = palette.muted;

            for (let i = 0; i <= 5; i++) {
                const y = chart.top + ((chart.bottom - chart.top) / 5) * i;
                const value = scaleMax - ((scaleMax - scaleMin) / 5) * i;
                context.beginPath();
                context.moveTo(chart.left, y);
                context.lineTo(chart.right, y);
                context.stroke();
                context.fillText(formatPrice(value), chart.right + 10, y + 4);
            }
        }

        function drawTradeZones() {
            if (!trade) {
                return;
            }

            const entryTop = toY(Math.max(trade.entryLower, trade.entryUpper));
            const entryBottom = toY(Math.min(trade.entryLower, trade.entryUpper));
            context.fillStyle = palette.entryFill;
            context.fillRect(chart.left, entryTop, chart.right - chart.left, Math.max(4, entryBottom - entryTop));

            if (trade.side === "Long") {
                drawRangeFill(trade.entryUpper, trade.takeProfit2, palette.greenFill);
                drawRangeFill(trade.stopLoss, trade.entryLower, palette.redFill);
            } else {
                drawRangeFill(trade.takeProfit2, trade.entryLower, palette.greenFill);
                drawRangeFill(trade.entryUpper, trade.stopLoss, palette.redFill);
            }

            drawPriceLine((trade.entryLower + trade.entryUpper) / 2, palette.amber, "ENTRAR");
            drawPriceLine(trade.takeProfit1, palette.green, "GANAR");
            drawPriceLine(trade.takeProfit2, palette.green, "GANAR MAS");
            drawPriceLine(trade.stopLoss, palette.red, "CORTAR PERDIDA");
        }

        function drawRangeFill(a, b, color) {
            const y1 = toY(Math.max(a, b));
            const y2 = toY(Math.min(a, b));
            context.fillStyle = color;
            context.fillRect(chart.left, y1, chart.right - chart.left, Math.max(3, y2 - y1));
        }

        function drawCandles() {
            candles.forEach((candle, index) => {
                const x = toX(index);
                const openY = toY(candle.open);
                const closeY = toY(candle.close);
                const highY = toY(candle.high);
                const lowY = toY(candle.low);
                const up = candle.close >= candle.open;
                const color = up ? palette.green : palette.red;

                context.strokeStyle = color;
                context.fillStyle = color;
                context.lineWidth = 1.4;
                context.beginPath();
                context.moveTo(x, highY);
                context.lineTo(x, lowY);
                context.stroke();

                const bodyTop = Math.min(openY, closeY);
                const bodyHeight = Math.max(3, Math.abs(openY - closeY));
                context.fillRect(x - bodyWidth / 2, bodyTop, bodyWidth, bodyHeight);
            });
        }

        function drawVolume() {
            candles.forEach((candle, index) => {
                const x = toX(index);
                const up = candle.close >= candle.open;
                const color = up ? "rgba(34, 197, 94, 0.36)" : "rgba(239, 68, 68, 0.34)";
                const heightRatio = candle.volume / maxVolume;
                const barHeight = Math.max(2, (chart.volumeBottom - chart.volumeTop) * heightRatio);

                context.fillStyle = color;
                context.fillRect(x - bodyWidth / 2, chart.volumeBottom - barHeight, bodyWidth, barHeight);
            });
        }

        function drawTimeAxis() {
            context.fillStyle = palette.muted;
            context.font = "600 10px Inter, sans-serif";
            const step = Math.max(10, Math.floor(candles.length / 4));

            for (let index = 0; index < candles.length; index += step) {
                context.fillText(formatTime(candles[index].closeTime), toX(index) - 14, chart.bottom + 18);
            }
        }

        function drawCurrentPrice() {
            const last = candles[candles.length - 1];
            drawPriceLine(last.close, palette.blue, "PRECIO", true);
        }

        function drawPriceLine(value, color, label, solid = false) {
            const y = toY(value);
            context.strokeStyle = color;
            context.lineWidth = solid ? 2 : 1.4;
            context.setLineDash(solid ? [] : [7, 6]);
            context.beginPath();
            context.moveTo(chart.left, y);
            context.lineTo(chart.right, y);
            context.stroke();
            context.setLineDash([]);

            const text = `${label} ${formatPrice(value)}`;
            context.font = "800 10px Inter, sans-serif";
            const labelWidth = context.measureText(text).width + 14;
            const labelY = resolvePriceLabelY(y);
            const labelX = chart.right - labelWidth - 8;

            if (Math.abs(labelY - y) > 2) {
                context.strokeStyle = color;
                context.lineWidth = 1;
                context.beginPath();
                context.moveTo(chart.right - 10, y);
                context.lineTo(chart.right - 10, labelY);
                context.stroke();
            }

            context.fillStyle = color;
            roundRect(labelX, labelY - 12, labelWidth, 22, 5);
            context.fill();
            context.fillStyle = "#06111f";
            context.fillText(text, labelX + 7, labelY + 4);
            priceLabelBoxes.push({ y: labelY, height: 22 });
        }

        function resolvePriceLabelY(targetY) {
            const minY = chart.top + 13;
            const maxY = chart.bottom - 13;
            const base = Math.min(maxY, Math.max(minY, targetY));
            const offsets = [0, 24, -24, 48, -48, 72, -72, 96, -96, 120, -120];

            for (const offset of offsets) {
                const candidate = Math.min(maxY, Math.max(minY, base + offset));
                const overlaps = priceLabelBoxes.some(box => Math.abs(box.y - candidate) < 24);
                if (!overlaps) {
                    return candidate;
                }
            }

            return base;
        }

        function drawFooter() {
            context.fillStyle = palette.muted;
            context.font = "700 12px Inter, sans-serif";
            const text = selectedOperation
                ? `${trade.action} | entrar ${formatTime(trade.entryAt)}-${formatTime(trade.entryUntil)} | salir antes de ${formatTime(trade.exitBy)} | ganar ${trade.potentialTp1}`
                : analysisTrade
                    ? `${snapshot.analysis.action} | entrar ${formatTime(snapshot.analysis.entryAt)}-${formatTime(snapshot.analysis.entryUntil)} | salir antes de ${formatTime(snapshot.analysis.exitBy)} | ganar ${formatMoney(snapshot.analysis.potentialTp1)}`
                    : `${snapshot.analysis?.action || "Esperar"} | ${snapshot.analysis?.readout || "Sin lectura todavia"}`;
            context.fillText(text, 26, height - 12);
        }

        function drawLegend(x, y, color, text) {
            context.fillStyle = color;
            context.fillRect(x, y - 9, 10, 10);
            context.fillStyle = palette.muted;
            context.font = "800 11px Inter, sans-serif";
            context.fillText(text, x + 16, y);
        }

        function drawEmpty(text) {
            context.fillStyle = palette.panel;
            roundRect(12, 12, width - 24, height - 24, 8);
            context.fill();
            context.fillStyle = palette.muted;
            context.font = "700 15px Inter, sans-serif";
            context.fillText(text, 28, 44);
        }

        function roundRect(x, y, rectWidth, rectHeight, radius) {
            context.beginPath();
            context.moveTo(x + radius, y);
            context.arcTo(x + rectWidth, y, x + rectWidth, y + rectHeight, radius);
            context.arcTo(x + rectWidth, y + rectHeight, x, y + rectHeight, radius);
            context.arcTo(x, y + rectHeight, x, y, radius);
            context.arcTo(x, y, x + rectWidth, y, radius);
            context.closePath();
        }
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

    function updateChartAnalysis(snapshot, operation, analysisTrade) {
        if (!chartAnalysis) {
            return;
        }

        const analysis = snapshot.analysis || {};
        const sideClass = operation
            ? operation.realizedText?.startsWith("-") ? "loss" : operation.realizedText === "Abierta" ? "flat" : "gain"
            : analysis.side === "Long" ? "gain" : analysis.side === "Short" ? "loss" : "flat";
        const sourceText = operation ? "señal guardada" : analysisTrade ? "lectura técnica" : "sin entrada";
        const actionText = operation ? operation.action : analysis.action || "Esperar";
        const readoutText = operation
            ? `${operation.status} | score ${operation.score}/100 | ${operation.realizedFormulaText}`
            : analysis.readout || "Sin datos suficientes.";
        const entryText = operation
            ? `${formatPrice(operation.entryLower)}-${formatPrice(operation.entryUpper)}`
            : analysisTrade ? `${formatPrice(analysis.entryLower)}-${formatPrice(analysis.entryUpper)}` : "Esperar";
        const entryAtText = operation ? operation.entryAt : analysis.entryAt;
        const entryUntilText = operation ? operation.entryUntil : analysis.entryUntil;
        const exitByText = operation ? operation.exitBy : analysis.exitBy;
        const holdingText = operation ? operation.timeText : analysis.holdingText || "-";
        const tp1Text = operation ? operation.potentialTp1 : analysisTrade ? formatMoney(analysis.potentialTp1) : "-";
        const tp1Price = operation ? formatPrice(operation.takeProfit1) : analysisTrade ? formatPrice(analysis.takeProfit1) : "-";
        const stopText = operation ? operation.potentialStop : analysisTrade ? formatMoney(analysis.potentialStop) : "-";
        const stopPrice = operation ? formatPrice(operation.stopLoss) : analysisTrade ? formatPrice(analysis.stopLoss) : "-";

        chartAnalysis.innerHTML = `
            <div class="analysis-headline">
                <div>
                    <span>${escapeHtml(snapshot.symbol)} ${escapeHtml(snapshot.interval)} | ${escapeHtml(analysis.horizon || "Mercado")} | ${escapeHtml(sourceText)}</span>
                    <strong class="${sideClass}">${escapeHtml(actionText)}</strong>
                </div>
                <small>${escapeHtml(readoutText)}</small>
            </div>
            <dl class="analysis-grid">
                <div><dt>Entrar</dt><dd>${entryText}</dd><small>${formatTime(entryAtText)}-${formatTime(entryUntilText)}</small></div>
                <div><dt>Salir antes de</dt><dd>${formatTime(exitByText)}</dd><small>${escapeHtml(holdingText)}</small></div>
                <div><dt>Ganar</dt><dd class="gain">${escapeHtml(tp1Text)}</dd><small>${tp1Price}</small></div>
                <div><dt>Perder max</dt><dd class="loss">${escapeHtml(stopText)}</dd><small>${stopPrice}</small></div>
            </dl>`;
    }

    function formatPrice(value) {
        const decimals = Math.abs(value) >= 1000 ? 2 : Math.abs(value) >= 1 ? 4 : 8;
        return Number(value).toLocaleString("en-US", { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
    }

    function formatMoney(value) {
        return Number(value || 0).toLocaleString("en-US", { style: "currency", currency: "USD", minimumFractionDigits: 2, maximumFractionDigits: 2 });
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

    function formatTime(value) {
        if (!value) {
            return "-";
        }

        return new Date(value).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    }

    function pickChartSymbol(operations) {
        return (operations.find(item => item.highlight) || operations.find(item => item.status === "Abierta") || operations[0] || { symbol: "BTCUSDT" }).symbol;
    }

    function colorWithAlpha(color, alpha) {
        if (color.startsWith("#")) {
            const value = color.slice(1);
            const bigint = parseInt(value.length === 3 ? value.split("").map(part => part + part).join("") : value, 16);
            const red = (bigint >> 16) & 255;
            const green = (bigint >> 8) & 255;
            const blue = bigint & 255;
            return `rgba(${red}, ${green}, ${blue}, ${alpha})`;
        }

        return color;
    }

    refreshLiveTrades();
    setChartZoom(chartZoom);
    window.setInterval(refreshLiveTrades, 5000);
    window.addEventListener("resize", refreshLiveTrades);
})();
