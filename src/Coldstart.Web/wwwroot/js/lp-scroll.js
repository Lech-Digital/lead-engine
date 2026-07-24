// Landing scroll-scene engine: pins each .scene's stage (via CSS sticky) and drives the pinned
// robot by writing data-active (current beat) + --progress (0..1 scrub) on each .scene.
// Native scrolling only — no hijacking. No-ops on mobile / reduced motion / missing APIs.
(function () {
  "use strict";

  var mqReduce = window.matchMedia("(prefers-reduced-motion: reduce)");
  var mqNarrow = window.matchMedia("(max-width: 819.98px)");
  var supported = "IntersectionObserver" in window && "requestAnimationFrame" in window;

  var scenes = [].slice.call(document.querySelectorAll(".scene"));
  if (!scenes.length || !supported) return;

  var beatObservers = [];
  var scrollBound = false;
  var raf = 0;

  function active() { return !mqReduce.matches && !mqNarrow.matches; }

  function setup() {
    teardown();
    if (!active()) {
      scenes.forEach(function (s) { s.style.removeProperty("--progress"); s.setAttribute("data-active", "0"); });
      return;
    }
    scenes.forEach(function (scene) {
      var beats = [].slice.call(scene.querySelectorAll(".beat"));
      // Activate whichever beat sits nearest the viewport centre.
      var io = new IntersectionObserver(function (entries) {
        entries.forEach(function (e) {
          if (e.isIntersecting) scene.setAttribute("data-active", e.target.getAttribute("data-beat"));
        });
      }, { rootMargin: "-45% 0px -45% 0px", threshold: 0 });
      beats.forEach(function (b) { io.observe(b); });
      beatObservers.push(io);
    });
    if (!scrollBound) { window.addEventListener("scroll", onScroll, { passive: true }); scrollBound = true; }
    onScroll();
  }

  function teardown() {
    beatObservers.forEach(function (io) { io.disconnect(); });
    beatObservers = [];
  }

  function onScroll() {
    if (raf) return;
    raf = window.requestAnimationFrame(function () {
      raf = 0;
      if (!active()) return;
      var vh = window.innerHeight;
      scenes.forEach(function (scene) {
        var track = scene.querySelector(".scene-track") || scene;
        var r = track.getBoundingClientRect();
        var total = r.height - vh;                              // scrollable range while pinned
        var p = total > 0 ? Math.min(1, Math.max(0, -r.top / total)) : 0;
        scene.style.setProperty("--progress", p.toFixed(4));
      });
    });
  }

  setup();

  var resizeT;
  window.addEventListener("resize", function () { clearTimeout(resizeT); resizeT = setTimeout(setup, 200); });
  if (mqReduce.addEventListener) mqReduce.addEventListener("change", setup);
})();
