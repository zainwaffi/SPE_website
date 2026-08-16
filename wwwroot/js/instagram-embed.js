// Lazy Instagram embeds, as an ES module.
//
// Loaded on demand by EventsPage via a dynamic import, so the ~3 KB of script and the
// preconnects it implies are no longer paid for on every page of the site.
//
// Instagram's embed.js turns every element carrying the `instagram-media` class into an
// iframe as soon as Embeds.process() runs. On a page with many events that means dozens of
// simultaneous requests to instagram.com, nearly all of them for posts far below the fold.
//
// So blockquotes render WITHOUT that class (see `ig-lazy`), and we only add it — and load
// embed.js at all — once a post is close to entering the viewport.

const LAZY_CLASS = 'ig-lazy';
const SCRIPT_ID = 'instagram-embed-script';
const SCRIPT_SRC = 'https://www.instagram.com/embed.js';

let observer = null;
let scriptRequested = false;

function runProcess() {
    if (window.instgrm && window.instgrm.Embeds) {
        window.instgrm.Embeds.process();
    }
}

function loadEmbedScript() {
    if (scriptRequested || document.getElementById(SCRIPT_ID)) {
        return;
    }
    scriptRequested = true;

    const script = document.createElement('script');
    script.id = SCRIPT_ID;
    script.async = true;
    script.src = SCRIPT_SRC;
    // Posts activated before the script arrives are picked up by this first pass,
    // because process() sweeps every unprocessed .instagram-media on the page.
    script.onload = runProcess;
    document.body.appendChild(script);
}

function activate(el) {
    el.classList.remove(LAZY_CLASS);
    el.classList.add('instagram-media');

    if (scriptRequested || document.getElementById(SCRIPT_ID)) {
        runProcess();
    } else {
        loadEmbedScript();
    }
}

function getObserver() {
    if (observer || typeof IntersectionObserver === 'undefined') {
        return observer;
    }

    observer = new IntersectionObserver((entries) => {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            observer.unobserve(entry.target);
            activate(entry.target);
        }
    }, {
        // Start fetching a little before the post scrolls into view so it has usually
        // finished rendering by the time the user reaches it.
        rootMargin: '300px 0px'
    });

    return observer;
}

/** Called after each render that may have added new embeds. */
export function processInstagramEmbeds() {
    const lazy = document.querySelectorAll('blockquote.' + LAZY_CLASS);
    if (lazy.length === 0) {
        return;
    }

    const io = getObserver();
    if (!io) {
        // No IntersectionObserver (very old browser): fall back to loading everything at
        // once rather than showing nothing.
        lazy.forEach(activate);
        return;
    }

    lazy.forEach((el) => io.observe(el));
}

/** Releases the observer when the events page goes away, so it doesn't outlive the circuit. */
export function dispose() {
    observer?.disconnect();
    observer = null;
}
