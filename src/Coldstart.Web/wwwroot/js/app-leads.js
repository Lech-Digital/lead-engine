// Progressive enhancement for the lead board:
//  - switch tabs client-side (no full navigation, so the page keeps its scroll position)
//  - drag a lead card onto a tab header to move its state, then relocate the card in place
//  - surface a move failure in a small modal instead of a browser alert
(function () {
  var TABS = ['new', 'accepted', 'discarded'];
  // Which grid a drop-target tab lands cards in.
  var DROP_BUCKET = { 'new': 'new', 'accepted': 'ready', 'discarded': 'shelved' };

  function tabEl(name) { return document.querySelector('.app-tab[data-tab="' + name + '"]'); }
  function panelEl(name) { return document.querySelector('.app-panel[data-panel="' + name + '"]'); }

  // --- Tabs: toggle panels client-side, keep the URL shareable, never scroll. ---
  function activateTab(name) {
    document.querySelectorAll('.app-tab').forEach(function (t) {
      t.classList.toggle('is-active', t.getAttribute('data-tab') === name);
    });
    document.querySelectorAll('.app-panel').forEach(function (p) {
      p.classList.toggle('is-active', p.getAttribute('data-panel') === name);
    });
    try { history.replaceState(null, '', '/app?tab=' + name); } catch (e) { /* non-fatal */ }
  }

  document.querySelectorAll('.app-tab').forEach(function (tab) {
    tab.addEventListener('click', function (e) {
      // Let modified clicks (open in new tab) and non-primary buttons navigate normally.
      if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button !== 0) return;
      e.preventDefault();
      activateTab(tab.getAttribute('data-tab'));
    });
  });

  // --- Counts + empty states, recomputed from the DOM after a move. ---
  function refreshGrid(grid) {
    if (!grid) return;
    grid.setAttribute('data-count', grid.querySelectorAll('.app-lead').length);
  }
  function refreshCounts() {
    TABS.forEach(function (name) {
      var count = panelEl(name) ? panelEl(name).querySelectorAll('.app-lead').length : 0;
      var badge = tabEl(name) ? tabEl(name).querySelector('.app-tab-count') : null;
      if (badge) badge.textContent = count;
    });
  }

  function moveCardTo(card, target) {
    var grid = document.querySelector('.app-leads[data-bucket="' + DROP_BUCKET[target] + '"]');
    if (!card || !grid) { window.location = '/app?tab=' + target; return; }  // fall back to a reload
    var from = card.closest('.app-leads');
    grid.insertBefore(card, grid.firstChild);
    refreshGrid(from);
    refreshGrid(grid);
    refreshCounts();
    activateTab(target);
  }

  // --- Move-failure modal. ---
  var modal = document.getElementById('move-error');
  var modalDetail = document.getElementById('move-error-detail');
  function showMoveError(detail) {
    var text = detail || 'Something went wrong on our end. Give it a moment and try again.';
    if (modal && typeof modal.showModal === 'function') {
      modalDetail.textContent = text;
      modal.showModal();
    } else {
      alert(text);
    }
  }

  // --- Drag to move. ---
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

  // Extracted from the drop handler — one shared move path.
  function requestMove(id, target) {
    var card = document.querySelector('.app-lead[data-lead-id="' + id + '"]');
    var body = new URLSearchParams();
    body.set('id', id);
    body.set('tab', target);
    fetch('/app?handler=Move', {
      method: 'POST',
      headers: { 'RequestVerificationToken': token, 'Content-Type': 'application/x-www-form-urlencoded' },
      body: body.toString()
    }).then(function (r) { return r.json(); }).then(function (res) {
      if (res.ok) { moveCardTo(card, target); }
      else { showMoveError(res.message); }
    }).catch(function () { showMoveError(); });
  }

  document.querySelectorAll('.app-tab').forEach(function (tab) {
    tab.addEventListener('dragover', function (e) { e.preventDefault(); tab.classList.add('drop-hover'); });
    tab.addEventListener('dragleave', function () { tab.classList.remove('drop-hover'); });
    tab.addEventListener('drop', function (e) {
      e.preventDefault();
      tab.classList.remove('drop-hover');
      if (!dragId) return;
      var id = dragId;
      dragId = null;
      requestMove(id, tab.getAttribute('data-tab'));
    });
  });

  // --- Card Approve/Skip buttons — same endpoint as drag-and-drop. ---
  document.querySelectorAll('.app-lead-actions button[data-move]').forEach(function (btn) {
    btn.addEventListener('click', function (ev) {
      ev.preventDefault();
      ev.stopPropagation();
      var card = btn.closest('.app-lead');
      if (card) requestMove(card.getAttribute('data-lead-id'), btn.getAttribute('data-move'));
    });
  });
})();
