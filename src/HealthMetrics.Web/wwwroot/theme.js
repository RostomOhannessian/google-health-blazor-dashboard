(function () {
    const storageKey = "healthmetrics-theme";
    const validThemes = new Set(["light", "dark"]);
    const mediaQuery = typeof window.matchMedia === "function"
        ? window.matchMedia("(prefers-color-scheme: dark)")
        : null;

    function systemTheme() {
        return mediaQuery?.matches ? "dark" : "light";
    }

    function savedTheme() {
        try {
            const value = window.localStorage.getItem(storageKey);
            return validThemes.has(value) ? value : null;
        } catch {
            return null;
        }
    }

    function apply(theme, persist) {
        const selected = validThemes.has(theme) ? theme : systemTheme();
        document.documentElement.setAttribute("data-bs-theme", selected);
        document.documentElement.style.colorScheme = selected;

        if (persist) {
            try {
                window.localStorage.setItem(storageKey, selected);
            } catch {
                // Private browsing or a blocked storage provider should not break the UI.
            }
        }

        window.dispatchEvent(new CustomEvent("healthmetrics:themechange", {
            detail: { theme: selected }
        }));
        return selected;
    }

    function currentTheme() {
        const theme = document.documentElement.getAttribute("data-bs-theme");
        return validThemes.has(theme) ? theme : systemTheme();
    }

    function syncToggleUi(theme) {
        const isDark = theme === "dark";
        const label = isDark ? "Switch to light theme" : "Switch to dark theme";
        const pressed = String(isDark);
        const icon = isDark ? "☀" : "☾";
        const text = isDark ? "Light" : "Dark";

        document.querySelectorAll("[data-theme-toggle]").forEach(button => {
            if (button.getAttribute("aria-label") !== label)
                button.setAttribute("aria-label", label);
            if (button.getAttribute("aria-pressed") !== pressed)
                button.setAttribute("aria-pressed", pressed);

            const iconElement = button.querySelector("[data-theme-icon]");
            if (iconElement && iconElement.textContent !== icon)
                iconElement.textContent = icon;

            const labelElement = button.querySelector("[data-theme-label]");
            if (labelElement && labelElement.textContent !== text)
                labelElement.textContent = text;
        });
    }

    apply(savedTheme() ?? systemTheme(), false);

    window.HealthTheme = {
        getTheme: currentTheme,
        setTheme: (theme, persist) => apply(theme, persist !== false),
        clearPreference: () => {
            try {
                window.localStorage.removeItem(storageKey);
            } catch {
                // Ignore unavailable storage.
            }
            return apply(systemTheme(), false);
        }
    };

    window.addEventListener("healthmetrics:themechange", event => {
        syncToggleUi(event.detail?.theme ?? currentTheme());
    });

    document.addEventListener("click", event => {
        const target = event.target;
        const button = target && typeof target.closest === "function"
            ? target.closest("[data-theme-toggle]")
            : null;
        if (!button) return;

        event.preventDefault();
        const nextTheme = currentTheme() === "dark" ? "light" : "dark";
        apply(nextTheme, true);
    });

    const syncWhenReady = () => syncToggleUi(currentTheme());
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", syncWhenReady, { once: true });
    } else {
        syncWhenReady();
    }

    if (typeof MutationObserver === "function") {
        const observer = new MutationObserver(syncWhenReady);
        observer.observe(document.documentElement, { childList: true, subtree: true });
    }

    if (mediaQuery) {
        const handleSystemThemeChange = () => {
            if (savedTheme() === null) {
                apply(systemTheme(), false);
            }
        };

        if (typeof mediaQuery.addEventListener === "function") {
            mediaQuery.addEventListener("change", handleSystemThemeChange);
        } else if (typeof mediaQuery.addListener === "function") {
            mediaQuery.addListener(handleSystemThemeChange);
        }
    }
})();
