// Progressive-enhancement UI behaviour: scroll reveals and the header disclosures.
//
// Everything here is plain DOM work with no Blazor JS interop. That is the point — it lets
// Home and SiteHeader render as static SSR instead of opening a SignalR circuit on every
// page just to toggle a CSS class.

(() => {
    "use strict";

    // ---------------------------------------------------------------- helpers

    // Matches inside `root` plus `root` itself, because a MutationObserver hands us the added
    // node directly and querySelectorAll never returns its own root.
    function collect(root, selector) {
        if (root.nodeType !== Node.ELEMENT_NODE && root.nodeType !== Node.DOCUMENT_NODE) return [];
        const found = [...root.querySelectorAll(selector)];
        if (root.matches?.(selector)) found.unshift(root);
        return found;
    }

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
        for (const element of collect(root, ".fade-up:not(.is-visible)")) {
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

    // ---------------------------------------------------------------- instagram embeds

    // Instagram's embed.js turns every element carrying the `instagram-media` class into an
    // iframe as soon as Embeds.process() runs. On a page with many events that means dozens of
    // simultaneous requests to instagram.com, nearly all for posts far below the fold.
    //
    // So the blockquotes render WITHOUT that class (see `ig-lazy` in EventsPage), and we add
    // it — and load embed.js at all — only once a post is close to entering the viewport.
    //
    // This lives here, in the same plain-DOM script as everything else, rather than in a
    // module imported over Blazor JS interop. That import used to wait for the SignalR circuit
    // to connect and for the page's first interactive render before a single post could start
    // loading. Now the first post begins as soon as the markup is parsed, and posts still
    // appear if the circuit is slow or never connects at all.

    const IG_LAZY_SELECTOR = "blockquote.ig-lazy";
    const IG_SCRIPT_ID = "instagram-embed-script";
    const IG_SCRIPT_SRC = "https://www.instagram.com/embed.js";

    let igScriptRequested = false;

    // Start fetching a little before the post scrolls into view, so it has usually finished
    // rendering by the time the reader reaches it.
    const embedObserver = "IntersectionObserver" in window
        ? new IntersectionObserver(onEmbedIntersect, { rootMargin: "300px 0px" })
        : null;

    function onEmbedIntersect(entries) {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            embedObserver.unobserve(entry.target);
            activateEmbed(entry.target);
        }
    }

    function runEmbedProcess() {
        window.instgrm?.Embeds?.process();
    }

    function loadEmbedScript() {
        if (igScriptRequested || document.getElementById(IG_SCRIPT_ID)) return;
        igScriptRequested = true;

        const script = document.createElement("script");
        script.id = IG_SCRIPT_ID;
        script.async = true;
        script.src = IG_SCRIPT_SRC;
        // Posts activated before the script arrives are picked up by this first pass, because
        // process() sweeps every unprocessed .instagram-media on the page.
        script.onload = runEmbedProcess;
        document.body.appendChild(script);
    }

    function activateEmbed(element) {
        element.classList.remove("ig-lazy");
        element.classList.add("instagram-media");

        if (igScriptRequested || document.getElementById(IG_SCRIPT_ID)) runEmbedProcess();
        else loadEmbedScript();
    }

    function registerEmbeds(root) {
        for (const element of collect(root, IG_LAZY_SELECTOR)) {
            // Without IntersectionObserver, load everything at once rather than show nothing.
            if (!embedObserver) {
                activateEmbed(element);
                continue;
            }
            embedObserver.observe(element);
        }
    }

    // Filtering the events list removes cards. Dropping the observer's reference to them keeps
    // it from pinning detached nodes in memory for the life of the page.
    function releaseEmbeds(root) {
        if (!embedObserver) return;
        for (const element of collect(root, IG_LAZY_SELECTOR)) embedObserver.unobserve(element);
    }

    // ---------------------------------------------------------------- copy to clipboard

    // A button marked [data-copy="<input id>"] copies that input's value. Plain DOM again, not
    // Blazor interop — and pure convenience: the value always sits in a readonly input the user
    // can select and copy by hand, so nothing is lost where the clipboard API is unavailable.
    //
    // The confirmation is written straight onto the button rather than raised back into Blazor,
    // so what it says matches whether the write actually succeeded. Blazor owns this element and
    // will overwrite the label on its next render of the page, which is fine: the label is
    // transient either way.
    document.addEventListener("click", async (event) => {
        const button = event.target.closest?.("[data-copy]");
        if (!button) return;

        const source = document.getElementById(button.getAttribute("data-copy"));
        if (!source) return;

        try {
            await navigator.clipboard.writeText(source.value);
        } catch {
            // No permission, or an insecure origin. Select the text so the next Ctrl+C works.
            source.focus();
            source.select();
            return;
        }

        // Stashed on first use so repeated clicks restore the real label, not "Copied".
        button.dataset.copyLabel ??= button.textContent.trim();
        button.textContent = "Copied";
        setTimeout(() => { button.textContent = button.dataset.copyLabel; }, 1500);
    });

    // ---------------------------------------------------------------- wiring

    function scan(root) {
        registerReveals(root);
        registerEmbeds(root);
    }

    // Mark the document as script-capable. The .fade-up hidden state is gated on this in
    // CSS, so a browser without JS shows the content instead of hiding it forever.
    document.documentElement.classList.add("js");

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", () => scan(document), { once: true });
    } else {
        scan(document);
    }

    // Blazor's enhanced navigation patches the DOM in place rather than reloading, so
    // newly arrived content has to be picked up. A MutationObserver covers that as well as
    // anything an interactive component renders later.
    new MutationObserver((mutations) => {
        for (const mutation of mutations) {
            for (const node of mutation.addedNodes) scan(node);
            for (const node of mutation.removedNodes) releaseEmbeds(node);
        }
    }).observe(document.documentElement, { childList: true, subtree: true });
})();
