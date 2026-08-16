// Progressive-enhancement UI behaviour: scroll reveals and the header disclosures.
//
// Everything here is plain DOM work with no Blazor JS interop. That is the point — it lets
// Home and SiteHeader render as static SSR instead of opening a SignalR circuit on every
// page just to toggle a CSS class.

(() => {
    "use strict";

    // ---------------------------------------------------------------- scroll reveal

    // One observer for the whole document, rather than one per element. Elements are
    // unobserved once revealed, so this never accumulates work.
    const revealObserver = "IntersectionObserver" in window
        ? new IntersectionObserver(onIntersect, { threshold: 0.2 })
        : null;

    function onIntersect(entries) {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;

            // Wait one frame so the browser registers the initial state before transitioning.
            requestAnimationFrame(() => entry.target.classList.add("is-visible"));
            revealObserver.unobserve(entry.target);
        }
    }

    function registerReveals(root) {
        const candidates = root.querySelectorAll?.(".fade-up:not(.is-visible)");
        if (!candidates) return;

        for (const element of candidates) {
            // Without IntersectionObserver, show everything immediately rather than
            // leaving the page stuck at opacity 0.
            if (!revealObserver) {
                element.classList.add("is-visible");
                continue;
            }
            revealObserver.observe(element);
        }
    }

    // ---------------------------------------------------------------- disclosures

    // A button marked [data-toggle="<id>"] shows/hides the element with that id.
    // Used by the header's hamburger menu and its nested Tools submenu.
    function openButtons() {
        return document.querySelectorAll('[data-toggle][aria-expanded="true"]');
    }

    function panelFor(button) {
        return document.getElementById(button.getAttribute("data-toggle"));
    }

    function setDisclosure(button, open) {
        const panel = panelFor(button);
        if (!panel) return;

        button.setAttribute("aria-expanded", String(open));
        panel.hidden = !open;

        // Closing a menu also closes anything nested inside it.
        if (!open) {
            for (const nested of panel.querySelectorAll('[data-toggle][aria-expanded="true"]')) {
                setDisclosure(nested, false);
            }
        }
    }

    // `keep` is the button about to be opened, if any. Menus that *contain* it must stay
    // open, otherwise opening the nested Tools menu would hide the nav panel holding it.
    function closeAllDisclosures(keep) {
        for (const button of openButtons()) {
            if (button === keep) continue;
            if (keep && panelFor(button)?.contains(keep)) continue;
            setDisclosure(button, false);
        }
    }

    function isInsideOpenPanel(node) {
        for (const button of openButtons()) {
            if (panelFor(button)?.contains(node)) return true;
        }
        return false;
    }

    document.addEventListener("click", (event) => {
        const target = event.target;
        const button = target.closest?.("[data-toggle]");

        if (button) {
            const isOpen = button.getAttribute("aria-expanded") === "true";
            // Sibling menus shouldn't stay open behind this one; ancestors must.
            if (!isOpen) closeAllDisclosures(button);
            setDisclosure(button, !isOpen);
            return;
        }

        // Following a link or submitting closes the menu, the way navigating used to.
        if (target.closest?.("a[href], button")) {
            closeAllDisclosures(null);
            return;
        }

        // Clicks on the inert parts of an open menu leave it alone; anything else dismisses.
        if (isInsideOpenPanel(target)) return;
        closeAllDisclosures(null);
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") closeAllDisclosures(null);
    });

    // ---------------------------------------------------------------- wiring

    function scan() {
        registerReveals(document);
    }

    // Mark the document as script-capable. The .fade-up hidden state is gated on this in
    // CSS, so a browser without JS shows the content instead of hiding it forever.
    document.documentElement.classList.add("js");

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", scan, { once: true });
    } else {
        scan();
    }

    // Blazor's enhanced navigation patches the DOM in place rather than reloading, so
    // newly arrived content has to be picked up. A MutationObserver covers that as well as
    // anything an interactive component renders later.
    new MutationObserver((mutations) => {
        for (const mutation of mutations) {
            for (const node of mutation.addedNodes) {
                if (node.nodeType !== Node.ELEMENT_NODE) continue;
                if (node.classList.contains("fade-up")) registerReveals(node.parentNode ?? document);
                registerReveals(node);
            }
        }
    }).observe(document.documentElement, { childList: true, subtree: true });
})();
