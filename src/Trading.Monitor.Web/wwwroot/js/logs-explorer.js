(() => {
    const explorer = document.querySelector("[data-log-explorer]");
    if (!explorer) {
        return;
    }

    const fileList = document.getElementById("logFileList");
    const eventList = document.getElementById("logEventList");
    const detailPanel = document.getElementById("logDetailPanel");
    const bucketChart = document.getElementById("logBucketChart");
    const filterForm = explorer.querySelector("[data-log-filter]");
    const searchInput = explorer.querySelector("[data-log-search]");
    const levelSelect = explorer.querySelector("[data-log-level]");
    const eventSelect = explorer.querySelector("[data-log-event]");
    const linesInput = explorer.querySelector("[data-log-lines-input]");
    const scopeInput = explorer.querySelector("[data-log-scope-input]");
    const applyButton = explorer.querySelector("[data-log-apply]");
    const activeFileLabel = document.getElementById("logActiveFile");
    const fileCountLabel = document.getElementById("logFileCount");
    const eventCountLabel = document.getElementById("logEventCount");
    const metricFile = document.getElementById("logMetricFile");
    const metricRoot = document.getElementById("logMetricRoot");
    const metricEvents = document.getElementById("logMetricEvents");
    const metricWarnings = document.getElementById("logMetricWarnings");
    const metricSignals = document.getElementById("logMetricSignals");

    let state = {
        logFile: explorer.dataset.logFile || "",
        lines: Number(explorer.dataset.logLines || 250),
        scope: explorer.dataset.logScope || "todo",
        level: levelSelect?.value || "",
        eventType: eventSelect?.value || "",
        search: searchInput?.value || "",
        selectedIndex: 0
    };
    let latestEntries = [];
    let requestId = 0;

    wire();
    loadLogs();

    function wire() {
        fileList?.querySelectorAll("[data-log-file]").forEach(button => {
            button.addEventListener("click", () => {
                state.logFile = button.dataset.logFile || "";
                state.selectedIndex = 0;
                loadLogs();
            });
        });

        filterForm?.addEventListener("submit", event => {
            event.preventDefault();
            readFilterState();
            state.selectedIndex = 0;
            loadLogs();
        });

        searchInput?.addEventListener("input", debounce(() => {
            readFilterState();
            state.selectedIndex = 0;
            loadLogs();
        }, 280));

        [levelSelect, eventSelect, linesInput].forEach(control => {
            control?.addEventListener("change", () => {
                readFilterState();
                state.selectedIndex = 0;
                loadLogs();
            });
        });

        eventList?.querySelectorAll("[data-log-entry-index]").forEach(button => {
            button.addEventListener("click", () => selectEntry(Number(button.dataset.logEntryIndex || 0)));
        });
    }

    async function loadLogs() {
        const currentRequest = ++requestId;
        setBusy(true);

        try {
            const url = new URL("/api/logs", window.location.origin);
            url.searchParams.set("logFile", state.logFile);
            url.searchParams.set("lines", String(state.lines));
            url.searchParams.set("nivel", state.level);
            url.searchParams.set("evento", state.eventType);
            url.searchParams.set("buscar", state.search);
            url.searchParams.set("ambito", state.scope);

            const response = await fetch(url, { cache: "no-store" });
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const data = await response.json();
            if (currentRequest !== requestId) {
                return;
            }

            latestEntries = data.entries || [];
            state.logFile = data.file?.relativePath || state.logFile;
            updateUrl();
            renderFiles(data.files || []);
            renderOptions(levelSelect, data.availableLevels || [], state.level);
            renderOptions(eventSelect, data.availableEvents || [], state.eventType);
            renderBuckets(data.buckets || []);
            renderEvents(latestEntries);
            renderMetrics(data);
            selectEntry(Math.min(state.selectedIndex, Math.max(0, latestEntries.length - 1)));
        } catch {
            eventList.innerHTML = `<div class="empty-state"><strong>No pude leer los logs.</strong><span>El servicio sigue vivo; intenta otro archivo o filtro.</span></div>`;
            detailPanel.innerHTML = `<div class="empty-state"><strong>Sin detalle.</strong><span>No llegó información nueva para mostrar.</span></div>`;
        } finally {
            setBusy(false);
        }
    }

    function readFilterState() {
        state.search = searchInput?.value || "";
        state.level = levelSelect?.value || "";
        state.eventType = eventSelect?.value || "";
        state.lines = Math.max(50, Math.min(1000, Number(linesInput?.value || 250)));
        state.scope = scopeInput?.value || state.scope || "todo";
    }

    function renderFiles(files) {
        if (!fileList) {
            return;
        }

        if (fileCountLabel) {
            fileCountLabel.textContent = String(files.length);
        }

        if (files.length === 0) {
            fileList.innerHTML = `<div class="empty-state"><strong>Sin archivos.</strong><span>Cuando web o worker escriban logs aparecerán aquí.</span></div>`;
            return;
        }

        fileList.innerHTML = files.map(file => `
            <button type="button" class="log-file-link ${file.relativePath === state.logFile ? "active" : ""}" data-log-file="${escapeAttribute(file.relativePath)}">
                <strong>${escapeHtml(file.displayName)}</strong>
                <span>${formatSize(file.sizeBytes)}</span>
                <span>${formatDate(file.lastWriteTime)}</span>
            </button>`).join("");

        fileList.querySelectorAll("[data-log-file]").forEach(button => {
            button.addEventListener("click", () => {
                state.logFile = button.dataset.logFile || "";
                state.selectedIndex = 0;
                loadLogs();
            });
        });
    }

    function renderBuckets(buckets) {
        if (!bucketChart) {
            return;
        }

        if (buckets.length === 0) {
            bucketChart.innerHTML = `<div class="empty-state"><strong>Sin actividad visible.</strong><span>El filtro actual no tiene eventos.</span></div>`;
            return;
        }

        bucketChart.innerHTML = buckets.map(bucket => `
            <article>
                <span>${escapeHtml(bucket.hour)}:00</span>
                <div><i style="width:${Number(bucket.width || 0).toFixed(2)}%"></i></div>
                <strong>${bucket.count}</strong>
            </article>`).join("");
    }

    function renderEvents(entries) {
        if (!eventList) {
            return;
        }

        if (eventCountLabel) {
            eventCountLabel.textContent = String(entries.length);
        }

        if (entries.length === 0) {
            eventList.innerHTML = `<div class="empty-state"><strong>No hay eventos para ese filtro.</strong><span>Prueba con otro nivel, evento o búsqueda.</span></div>`;
            return;
        }

        eventList.innerHTML = entries.map((entry, index) => `
            <button type="button" class="master-item ${index === state.selectedIndex ? "active" : ""}" data-log-entry-index="${index}">
                <span class="master-kicker">${escapeHtml(entry.time)} | ${escapeHtml(entry.service)}</span>
                <strong>${escapeHtml(entry.eventType)}</strong>
                <span class="status ${levelClass(entry.level)}">${escapeHtml(entry.levelLabel)}</span>
                <small>${escapeHtml(entry.message)}</small>
            </button>`).join("");

        eventList.querySelectorAll("[data-log-entry-index]").forEach(button => {
            button.addEventListener("click", () => selectEntry(Number(button.dataset.logEntryIndex || 0)));
        });
    }

    function selectEntry(index) {
        state.selectedIndex = index;
        eventList?.querySelectorAll("[data-log-entry-index]").forEach(button => {
            button.classList.toggle("active", Number(button.dataset.logEntryIndex || 0) === index);
        });

        const entry = latestEntries[index];
        if (!entry) {
            detailPanel.innerHTML = `<div class="empty-state"><strong>Selecciona un evento.</strong><span>Aquí aparecerá el detalle interpretado del log.</span></div>`;
            return;
        }

        detailPanel.innerHTML = `
            <div class="section-title">
                <h2>${escapeHtml(entry.eventType)}</h2>
                <span>${escapeHtml(entry.time)} | ${escapeHtml(entry.service)}</span>
            </div>
            <div class="detail-grid">
                <article><span>Nivel</span><strong class="${eventClass(entry.eventType)}">${escapeHtml(entry.levelLabel)}</strong><small>${escapeHtml(entry.level)}</small></article>
                <article><span>Servicio</span><strong>${escapeHtml(entry.service)}</strong><small>${escapeHtml(state.logFile || "-")}</small></article>
                <article><span>Hora</span><strong>${escapeHtml(entry.time)}</strong><small>bucket ${escapeHtml(entry.hour)}:00</small></article>
                <article><span>Tipo</span><strong>${escapeHtml(entry.eventType)}</strong><small>interpretado</small></article>
            </div>
            <p class="detail-copy">${escapeHtml(entry.message)}</p>
            <details class="compact-details">
                <summary>Ver línea cruda</summary>
                <pre class="log-output compact-log-output">${escapeHtml(entry.rawLine)}</pre>
            </details>`;
    }

    function renderMetrics(data) {
        if (activeFileLabel) {
            activeFileLabel.textContent = data.file?.displayName || "sin archivo";
        }

        if (metricFile) {
            metricFile.textContent = data.file?.displayName || "-";
        }

        if (metricRoot) {
            metricRoot.textContent = data.rootPath || "";
        }

        if (metricEvents) {
            metricEvents.textContent = String(data.filteredCount || 0);
        }

        if (metricWarnings) {
            metricWarnings.textContent = `${data.errorCount || 0} / ${data.warningCount || 0}`;
            metricWarnings.className = (data.errorCount || 0) > 0 ? "loss" : "gain";
        }

        if (metricSignals) {
            metricSignals.textContent = `${data.signalCount || 0} / ${data.scanCount || 0}`;
        }
    }

    function renderOptions(select, values, selected) {
        if (!select) {
            return;
        }

        const current = selected || select.value || "";
        select.innerHTML = `<option value="">Todos</option>${values.map(value => `<option value="${escapeAttribute(value)}">${escapeHtml(value)}</option>`).join("")}`;
        select.value = values.includes(current) ? current : "";
        if (select === levelSelect) {
            state.level = select.value;
        }

        if (select === eventSelect) {
            state.eventType = select.value;
        }
    }

    function updateUrl() {
        if (!window.history?.replaceState) {
            return;
        }

        const url = new URL(window.location.href);
        url.searchParams.set("LogFile", state.logFile);
        url.searchParams.set("Lines", String(state.lines));
        url.searchParams.set("Ambito", state.scope);
        setOptionalParam(url, "Nivel", state.level);
        setOptionalParam(url, "Evento", state.eventType);
        setOptionalParam(url, "Buscar", state.search);
        window.history.replaceState(null, "", url);
    }

    function setOptionalParam(url, key, value) {
        if (value) {
            url.searchParams.set(key, value);
        } else {
            url.searchParams.delete(key);
        }
    }

    function setBusy(isBusy) {
        if (applyButton) {
            applyButton.disabled = isBusy;
            applyButton.textContent = isBusy ? "Leyendo..." : "Filtrar";
        }
    }

    function levelClass(level) {
        return level === "ERR" || level === "FTL" ? "status-loss" : level === "WRN" ? "status-muted" : level === "INF" ? "status-win" : "status-open";
    }

    function eventClass(eventType) {
        return eventType === "Incidente" ? "loss" : eventType === "Señal" ? "gain" : eventType === "Barrido" ? "flat" : "muted";
    }

    function formatSize(bytes) {
        const value = Number(bytes || 0);
        if (value >= 1024 * 1024) {
            return `${(value / 1024 / 1024).toFixed(2)} MB`;
        }

        if (value >= 1024) {
            return `${(value / 1024).toFixed(1)} KB`;
        }

        return `${value} B`;
    }

    function formatDate(value) {
        if (!value) {
            return "-";
        }

        return new Date(value).toLocaleString([], { month: "short", day: "2-digit", hour: "2-digit", minute: "2-digit" });
    }

    function debounce(callback, delay) {
        let handle;
        return (...args) => {
            window.clearTimeout(handle);
            handle = window.setTimeout(() => callback(...args), delay);
        };
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
