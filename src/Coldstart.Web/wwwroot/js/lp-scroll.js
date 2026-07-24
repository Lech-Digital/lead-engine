// Landing scroll engine: drives the beats (data-active) + scrub (--progress) per scene, and the
// single fixed robot's fade-in/out across the narrative. Native scrolling only — no hijacking.
// No-ops on mobile / reduced motion / missing APIs (CSS handles the static fallback).
(function () {
  "use strict";

  var mqReduce = window.matchMedia("(prefers-reduced-motion: reduce)");
  var mqNarrow = window.matchMedia("(max-width: 819.98px)");
  var supported = "IntersectionObserver" in window && "requestAnimationFrame" in window;

  var scenes = [].slice.call(document.querySelectorAll(".scene"));
  if (!scenes.length || !supported) return;

  var nightScene = document.querySelector(".scene--night");
  var robotStage = nightScene && nightScene.querySelector(".scene-stage");

  var beatObservers = [];
  var scrollBound = false;
  var raf = 0;

  function active() { return !mqReduce.matches && !mqNarrow.matches; }

  function setup() {
    teardown();
    if (!active()) {
      scenes.forEach(function (s) { s.style.removeProperty("--progress"); s.setAttribute("data-active", "0"); });
      if (robotStage) robotStage.style.removeProperty("--robot-op");   // static/visible on mobile
      return;
    }
    scenes.forEach(function (scene) {
      var beats = [].slice.call(scene.querySelectorAll(".beat"));
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
        var total = r.height - vh;
        var p = total > 0 ? Math.min(1, Math.max(0, -r.top / total)) : 0;
        scene.style.setProperty("--progress", p.toFixed(4));
      });
      // Single robot: visible from the top through the night, then fades out as the night's
      // bottom rises past the lower viewport (before the daylight "how it works" section).
      if (robotStage && nightScene) {
        var nb = nightScene.getBoundingClientRect().bottom;
        // Robot rides at full opacity through every beat (delivery included), then dissolves
        // into the dawn over the delivery beat's tail — reaching zero exactly as the night
        // ends, so it's gone before the daylight how-it-works section scrolls up.
        var op = Math.min(1, Math.max(0, nb / (vh * 0.55)));
        robotStage.style.setProperty("--robot-op", op.toFixed(3));
      }
    });
  }

  setup();

  var resizeT;
  window.addEventListener("resize", function () { clearTimeout(resizeT); resizeT = setTimeout(setup, 200); });
  if (mqReduce.addEventListener) mqReduce.addEventListener("change", setup);
})();
