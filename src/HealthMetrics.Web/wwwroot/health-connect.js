document.addEventListener("click", event => {
    const link = event.target.closest("[data-google-health-connect]");
    if (link === null
        || event.defaultPrevented
        || event.button !== 0
        || event.metaKey
        || event.ctrlKey
        || event.shiftKey
        || event.altKey) {
        return;
    }

    link.classList.add("is-pending");
    link.setAttribute("aria-busy", "true");
});
