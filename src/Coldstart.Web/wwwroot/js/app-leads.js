// Progressive enhancement: drag a lead card onto a tab header to move its state.
(function () {
  var dragId = null;

  document.querySelectorAll('.app-lead[draggable="true"]').forEach(function (card) {
    card.addEventListener('dragstart', function (e) {
      dragId = card.getAttribute('data-lead-id');
      card.classList.add('dragging');
      e.dataTransfer.effectAllowed = 'move';
    });
    card.addEventListener('dragend', function () { card.classList.remove('dragging'); });
  });

  var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
  var token = tokenEl ? tokenEl.value : '';

  document.querySelectorAll('.app-tab').forEach(function (tab) {
    tab.addEventListener('dragover', function (e) { e.preventDefault(); tab.classList.add('drop-hover'); });
    tab.addEventListener('dragleave', function () { tab.classList.remove('drop-hover'); });
    tab.addEventListener('drop', function (e) {
      e.preventDefault();
      tab.classList.remove('drop-hover');
      if (!dragId) return;
      var target = tab.getAttribute('data-tab');
      var body = new URLSearchParams();
      body.set('id', dragId);
      body.set('tab', target);
      fetch('/app?handler=Move', {
        method: 'POST',
        headers: { 'RequestVerificationToken': token, 'Content-Type': 'application/x-www-form-urlencoded' },
        body: body.toString()
      }).then(function (r) { return r.json(); }).then(function (res) {
        if (res.ok) { window.location = '/app?tab=' + target; }
        else { alert(res.message || 'Could not move that lead.'); }
      }).catch(function () { alert('Could not move that lead.'); });
      dragId = null;
    });
  });
})();
