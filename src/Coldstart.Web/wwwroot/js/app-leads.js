// Progressive enhancement for the briefing queue (/app): a Skip confirmation.
//  - Each .lead-skip-form posts natively (POST -> redirect -> reload) with no JS.
//  - With JS + <dialog> support, we intercept the submit, ask for confirmation,
//    and on confirm submit the form for real. The reload keeps every count
//    (e.g. "N in history") in sync — no client-side patching that can drift.
(function () {
  var dlg = document.getElementById('skip-confirm');
  if (!dlg || typeof dlg.showModal !== 'function') return; // no dialog support: native submit, no confirm

  var coEl = document.getElementById('skip-confirm-co');
  var okBtn = document.getElementById('skip-confirm-ok');
  var cancelBtn = document.getElementById('skip-confirm-cancel');
  var pending = null;

  document.querySelectorAll('.lead-skip-form').forEach(function (form) {
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      pending = form;
      if (coEl) { coEl.textContent = form.getAttribute('data-skip-co') || 'this lead'; }
      dlg.showModal();
    });
  });

  okBtn.addEventListener('click', function () {
    var form = pending;
    pending = null;
    dlg.close();
    if (form) { form.submit(); } // native submit -> server redirect -> reload (counts refresh)
  });

  cancelBtn.addEventListener('click', function () {
    pending = null;
    dlg.close();
  });
})();
