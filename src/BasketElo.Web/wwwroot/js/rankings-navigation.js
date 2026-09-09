(function () {
    const poolLinkSelector = ".rating-pool-tabs a";
    const loadingTableSelector = ".rankings-table-section.is-pool-loading";
    let fallbackTimer;

    function clearPoolLoading() {
        if (fallbackTimer) {
            window.clearTimeout(fallbackTimer);
            fallbackTimer = undefined;
        }

        document.querySelectorAll(loadingTableSelector).forEach(function (table) {
            table.classList.remove("is-pool-loading");
            table.removeAttribute("aria-busy");
        });
    }

    document.addEventListener("click", function (event) {
        const link = event.target.closest(poolLinkSelector);
        if (!link ||
            event.button !== 0 ||
            event.metaKey ||
            event.ctrlKey ||
            event.shiftKey ||
            event.altKey ||
            link.getAttribute("aria-current") === "page") {
            return;
        }

        const table = document.querySelector(".rankings-table-section");
        if (!table) {
            return;
        }

        table.classList.add("is-pool-loading");
        table.setAttribute("aria-busy", "true");
        fallbackTimer = window.setTimeout(clearPoolLoading, 10000);
    });

    window.addEventListener("pageshow", clearPoolLoading);
    window.Blazor?.addEventListener("enhancedload", clearPoolLoading);
})();
