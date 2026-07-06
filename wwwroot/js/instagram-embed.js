window.speSite = window.speSite || {};

window.speSite.processInstagramEmbeds = function () {
    if (window.instgrm && window.instgrm.Embeds) {
        window.instgrm.Embeds.process();
    }
};
