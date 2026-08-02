(() => {
    const groups = [...document.querySelectorAll("[data-connection-group]")];

    document.querySelectorAll("[data-source-retry]").forEach(button => {
        button.addEventListener("click", () => retryOne(button));
    });

    groups.forEach(group => {
        group.querySelectorAll("[data-connection-filter]").forEach(button => {
            button.addEventListener("click", () => {
                group.dataset.connectionFilter = button.dataset.connectionFilter || "all";
                group.querySelectorAll("[data-connection-filter]").forEach(item => {
                    const active = item === button;
                    item.classList.toggle("active", active);
                    item.setAttribute("aria-pressed", String(active));
                });
                applyFilter(group);
            });
        });

        group.querySelector("[data-retry-group]")?.addEventListener("click", () => retryGroup(group));
        applyFilter(group);
    });

    async function retryGroup(group) {
        const button = group.querySelector("[data-retry-group]");
        const result = group.querySelector("[data-retry-group-result]");
        const retryButtons = [...group.querySelectorAll("[data-source-retry]")];
        if (!button || retryButtons.length === 0) {
            return;
        }

        button.disabled = true;
        button.classList.add("is-busy");
        const originalText = button.innerHTML;
        let completed = 0;
        let healthy = 0;

        if (result) {
            result.textContent = `Iniciando ${retryButtons.length} comprobaciones…`;
        }

        try {
            await runPool(retryButtons, 4, async retryButton => {
                const outcome = await retryOne(retryButton, true);
                completed += 1;
                if (outcome === "working") {
                    healthy += 1;
                }
                if (result) {
                    result.textContent = `Comprobando ${completed} de ${retryButtons.length}…`;
                }
            });

            const issues = completed - healthy;
            if (result) {
                result.textContent = `Comprobación terminada: ${healthy} funcionando y ${issues} con problemas.`;
            }
        } finally {
            button.disabled = false;
            button.classList.remove("is-busy");
            button.innerHTML = originalText;
            applyFilter(group);
        }
    }

    async function retryOne(button, compact = false) {
        const panel = button.closest("[data-detail-panel]") || button.parentElement;
        const result = panel?.querySelector("[data-source-retry-result]");
        const status = panel?.querySelector("[data-source-status-output]");
        const originalText = button.textContent;

        button.disabled = true;
        button.classList.add("is-busy");
        button.textContent = compact ? "Comprobando…" : "Reintentando…";

        if (result && !compact) {
            result.textContent = "Probando la conexión ahora mismo…";
        }

        try {
            const response = await fetch("/api/conexiones/reintentar", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    sourceName: button.dataset.sourceName || "",
                    kind: button.dataset.sourceKind || "",
                    url: button.dataset.sourceUrl || "",
                    scope: button.dataset.sourceScope || ""
                })
            });

            const payload = await response.json();
            if (!response.ok) {
                throw new Error(payload.message || `HTTP ${response.status}`);
            }

            const connectionState = String(payload.statusLabel || payload.status).toLowerCase() === "healthy" ? "working" : "issues";
            updateStatus(panel, status, payload.statusLabel || payload.status || "Revisada", payload.cssClass || "status-muted", connectionState);

            if (result) {
                result.textContent = `${payload.message} Revisado ${formatTime(payload.checkedAt)}.`;
            }

            return connectionState;
        } catch (error) {
            updateStatus(panel, status, "Failed", "status-loss", "issues");
            if (result) {
                result.textContent = `No respondió: ${error.message || error}`;
            }
            return "issues";
        } finally {
            button.disabled = false;
            button.classList.remove("is-busy");
            button.textContent = originalText || "Reintentar conexión";
        }
    }

    function updateStatus(panel, detailStatus, label, cssClass, connectionState) {
        if (detailStatus) {
            setStatusBadge(detailStatus, label, cssClass);
        }

        const workbench = panel?.closest("[data-connection-source-workbench]");
        const key = panel?.dataset.detailPanel;
        const item = directItems(workbench).find(candidate => candidate.dataset.detailKey === key);
        if (item) {
            item.dataset.connectionState = connectionState;
            item.dataset.filterStatus = label;
            const listStatus = item.querySelector("[data-source-list-status]");
            if (listStatus) {
                setStatusBadge(listStatus, label, cssClass);
            }
        }

        const group = workbench?.closest("[data-connection-group]");
        if (group) {
            applyFilter(group);
        }
    }

    function setStatusBadge(element, label, cssClass) {
        element.textContent = label;
        element.classList.remove("status-win", "status-muted", "status-loss", "status-open");
        element.classList.add(cssClass);
    }

    function applyFilter(group) {
        const workbench = group.querySelector("[data-connection-source-workbench]");
        if (!workbench) {
            return;
        }

        const selected = group.dataset.connectionFilter || "all";
        const items = directItems(workbench);
        let visible = 0;

        items.forEach(item => {
            const matches = selected === "all" || item.dataset.connectionState === selected;
            item.hidden = !matches;
            if (matches) {
                visible += 1;
            }
        });

        const count = group.querySelector("[data-connection-visible-count]");
        if (count) {
            count.textContent = String(visible);
        }

        const empty = group.querySelector("[data-connection-empty]");
        if (empty) {
            empty.hidden = visible > 0;
        }
        workbench.classList.toggle("is-empty", visible === 0);

        const active = items.find(item => item.classList.contains("active") && !item.hidden);
        if (!active) {
            activate(workbench, items.find(item => !item.hidden)?.dataset.detailKey);
        }
    }

    function activate(workbench, key) {
        directItems(workbench).forEach(item => {
            const active = Boolean(key) && item.dataset.detailKey === key;
            item.classList.toggle("active", active);
            item.setAttribute("aria-expanded", String(active));
        });

        directPanels(workbench).forEach(panel => {
            panel.classList.toggle("active", Boolean(key) && panel.dataset.detailPanel === key);
        });
    }

    function directItems(workbench) {
        const list = workbench?.querySelector(":scope > .master-list");
        return list ? [...list.querySelectorAll(":scope > [data-connection-item]")] : [];
    }

    function directPanels(workbench) {
        return workbench ? [...workbench.querySelectorAll(":scope > .detail-stack > [data-detail-panel]")] : [];
    }

    async function runPool(items, concurrency, work) {
        let nextIndex = 0;
        const workers = Array.from({ length: Math.min(concurrency, items.length) }, async () => {
            while (nextIndex < items.length) {
                const current = items[nextIndex];
                nextIndex += 1;
                await work(current);
            }
        });
        await Promise.all(workers);
    }

    function formatTime(value) {
        if (!value) {
            return "";
        }

        try {
            return new Date(value).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
        } catch {
            return "";
        }
    }
})();
