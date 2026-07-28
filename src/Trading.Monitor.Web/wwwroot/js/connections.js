(() => {
    document.querySelectorAll("[data-source-retry]").forEach(button => {
        button.addEventListener("click", async () => {
            const panel = button.closest("[data-detail-panel]") || button.parentElement;
            const result = panel?.querySelector("[data-source-retry-result]");
            const status = panel?.querySelector("[data-source-status-output]");

            button.disabled = true;
            const originalText = button.textContent;
            button.textContent = "Reintentando...";

            if (result) {
                result.textContent = "Probando conexión ahora mismo.";
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

                if (status) {
                    status.textContent = payload.statusLabel || payload.status || "Revisada";
                    status.classList.remove("status-win", "status-muted", "status-loss");
                    status.classList.add(payload.cssClass || "status-muted");
                }

                if (result) {
                    result.textContent = `${payload.message} Revisado ${formatTime(payload.checkedAt)}.`;
                }
            } catch (error) {
                if (status) {
                    status.textContent = "Failed";
                    status.classList.remove("status-win", "status-muted");
                    status.classList.add("status-loss");
                }

                if (result) {
                    result.textContent = `No respondió: ${error.message || error}`;
                }
            } finally {
                button.disabled = false;
                button.textContent = originalText || "Reintentar conexión";
            }
        });
    });

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
