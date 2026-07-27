// Reusable confirmation for destructive actions across the customer zone.
//  - Any submit button with a data-confirm attribute is intercepted: we show the
//    shared #confirm-dialog with its message, and only submit the button's form
//    once the user confirms.
//  - data-confirm      : the body text (required to opt in)
//    data-confirm-title : the heading (optional)
//    data-confirm-ok    : the confirm button label (optional)
//  - Without <dialog> support the buttons post natively — the action still works,
//    it just isn't gated by a confirm (progressive enhancement, never a blocker).
(function () {
  var dlg = document.getElementById('confirm-dialog');
  if (!dlg || typeof dlg.showModal !== 'function') return;

  var titleEl = document.getElementById('confirm-dialog-title');
  var bodyEl = document.getElementById('confirm-dialog-body');
  var okBtn = document.getElementById('confirm-dialog-ok');
  var cancelBtn = document.getElementById('confirm-dialog-cancel');
  var pending = null;

  document.querySelectorAll('[data-confirm]').forEach(function (el) {
    el.addEventListener('click', function (e) {
      e.preventDefault();
      pending = el;
      titleEl.textContent = el.getAttribute('data-confirm-title') || 'Are you sure?';
      bodyEl.textContent = el.getAttribute('data-confirm') || '';
      okBtn.textContent = el.getAttribute('data-confirm-ok') || 'Confirm';
      dlg.showModal();
    });
  });

  okBtn.addEventListener('click', function () {
    var el = pending;
    pending = null;
    dlg.close();
    if (!el) return;
    // el.form resolves the associated form even when the button uses form="…".
    var form = el.form || (el.closest && el.closest('form'));
    if (!form) return;
    // requestSubmit(submitter) preserves the button's formaction (asp-page-handler)
    // and name/value; fall back to submit() where it isn't available.
    if (form.requestSubmit) { form.requestSubmit(el.tagName === 'BUTTON' ? el : null); }
    else { form.submit(); }
  });

  cancelBtn.addEventListener('click', function () {
    pending = null;
    dlg.close();
  });
})();
