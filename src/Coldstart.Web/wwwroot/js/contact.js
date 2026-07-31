// Landing page behavior: scroll reveals + sample-leads form submit.
(function () {
  "use strict";

  // Reveal-on-scroll (no-op when reduced motion is preferred — CSS shows everything)
  var reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var revealed = document.querySelectorAll("[data-reveal]");
  if (reduced || !("IntersectionObserver" in window)) {
    revealed.forEach(function (el) { el.classList.add("is-visible"); });
  } else {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        if (e.isIntersecting) { e.target.classList.add("is-visible"); io.unobserve(e.target); }
      });
    }, { threshold: 0.15 });
    revealed.forEach(function (el) { io.observe(el); });
  }

  // Sample-leads form: post without navigation, report inline.
  var form = document.getElementById("sample-form");
  var status = document.getElementById("form-status");
  if (!form || !status) return;
  form.addEventListener("submit", function (ev) {
    ev.preventDefault();
    var btn = form.querySelector("button[type=submit]");
    btn.disabled = true;
    status.className = "lp-form-status";
    status.textContent = "Sending…";
    fetch(form.action, { method: "POST", body: new FormData(form) })
      .then(function (res) {
        if (res.ok && window.umami) { try { window.umami.track("sample-request-success"); } catch (e) {} }
        if (!res.ok) throw new Error("bad status " + res.status);
        status.className = "lp-form-status ok";
        status.textContent = "Done — your 3 sample leads are being prepared. Check your inbox in a day or two.";
        form.reset();
      })
      .catch(function () {
        status.className = "lp-form-status err";
        status.textContent = "That didn't go through. Check your name and email, or write to us directly.";
        btn.disabled = false;
      });
  });
})();
