// Progressive enhancement for the briefing queue (/app):
//  - the Skip form works with plain HTML (POST -> redirect -> reload);
//  - here we intercept the submit, POST it in the background, and animate the
//    row away on success — falling back to a normal submit on any failure.
(function () {
  if (!window.fetch) return; // no fetch: let every .lead-skip-form post natively

  var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  function removeRow(form) {
    var row = form.closest('.lead-row-wrap');
    if (!row) { return; }
    if (reduceMotion) {
      row.remove();
      return;
    }
    row.style.transition = 'opacity .18s ease, transform .18s ease';
    row.style.transform = 'translateX(12px)';
    row.style.opacity = '0';
    row.addEventListener('transitionend', function () { row.remove(); }, { once: true });
  }

  document.querySelectorAll('.lead-skip-form').forEach(function (form) {
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      fetch(form.action, { method: 'POST', body: new FormData(form) })
        .then(function (response) {
          if (response.ok) { removeRow(form); }
          else { form.submit(); }
        })
        .catch(function () { form.submit(); });
    });
  });
})();
