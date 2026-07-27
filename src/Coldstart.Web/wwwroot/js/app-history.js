// Live "search as you type" for the History page. The <form> still works on
// Enter / without JS (server-side ?q= filter); this just filters the already-
// rendered rows instantly so the list narrows on every keystroke.
(function () {
  var input = document.getElementById('history-search');
  var list = document.getElementById('history-list');
  if (!input || !list) return;

  var rows = [].slice.call(list.querySelectorAll('.cz-row'));
  var empty = document.getElementById('history-nomatch');

  function apply() {
    var q = input.value.trim().toLowerCase();
    var shown = 0;
    rows.forEach(function (row) {
      var co = row.querySelector('.cz-co');
      var match = !q || (co && co.textContent.toLowerCase().indexOf(q) !== -1);
      row.hidden = !match;
      if (match) shown++;
    });
    if (empty) empty.hidden = shown !== 0;
  }

  input.addEventListener('input', apply);
  // Don't reload on Enter — the live filter already reflects the query.
  input.form && input.form.addEventListener('submit', function (e) { e.preventDefault(); });
})();
