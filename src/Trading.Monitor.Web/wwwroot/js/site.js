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
