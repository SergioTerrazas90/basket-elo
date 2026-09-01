(function () {
    const selector = "a[data-culture-switch]";
    let switchPending = false;

    document.addEventListener("click", async function (event) {
        const link = event.target.closest(selector);
        if (!link || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
            return;
        }

        event.preventDefault();

        if (link.getAttribute("aria-current") === "true") {
            link.closest("details")?.removeAttribute("open");
            return;
        }

        if (switchPending) {
            return;
        }

        switchPending = true;
        const updateUrl = new URL(link.href, window.location.href);
        updateUrl.searchParams.set("updateOnly", "true");

        try {
            const response = await fetch(updateUrl, {
                method: "GET",
                credentials: "same-origin",
                cache: "no-store",
                headers: {
                    "Accept": "application/json"
                }
            });

            if (!response.ok) {
                throw new Error(`Culture update failed with status ${response.status}.`);
            }

            window.location.reload();
        } catch {
            window.location.assign(link.href);
        }
    });
})();
