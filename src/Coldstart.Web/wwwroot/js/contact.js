(function () {
  var overlay = document.getElementById('composer');
  if (!overlay) return;
  var TO = 'adzius.lech@gmail.com';
  var form = document.getElementById('c-form');
  var nameI = document.getElementById('c-name');
  var nicheI = document.getElementById('c-niche');
  var sellI = document.getElementById('c-sell');
  var pvSubj = document.getElementById('c-subj');
  var pvName = document.getElementById('c-pv-name');
  var pvName2 = document.getElementById('c-pv-name2');
  var pvSell = document.getElementById('c-pv-sell');
  var pvNiche = document.getElementById('c-pv-niche');
  var openLink = document.getElementById('c-open');
  var copyBtn = document.getElementById('c-copy');
  var submitBtn = document.getElementById('c-submit');
  var statusEl = document.getElementById('c-status');
  var hint = document.getElementById('c-hint');
  var lastTrigger = null;

  function name() { return nameI.value.trim(); }
  function niche() { return nicheI.value.trim(); }
  function sell() { return sellI.value.trim(); }

  function mail() {
    var subject = 'Coldstart — sample leads' + (niche() ? ' for ' + niche() : '');
    var body = 'Hi Andrzej,\n\nI’m ' + (name() || '___') + ' — I sell ' + (sell() || '___') + ' to ' + (niche() || '___') +
      ', and I’d like to see the kind of leads Coldstart would find for us.\n\n' +
      'Could you send a few sample leads for my niche?\n\nThanks!\n' + (name() || '___');
    return { subject: subject, body: body };
  }

  function render() {
    var m = mail();
    pvSubj.textContent = m.subject;
    pvName.textContent = name() || 'your name';
    pvName.classList.toggle('empty', !name());
    pvName2.textContent = name() || 'your name';
    pvName2.classList.toggle('empty', !name());
    pvSell.textContent = sell() || 'what you sell';
    pvSell.classList.toggle('empty', !sell());
    pvNiche.textContent = niche() || 'your niche';
    pvNiche.classList.toggle('empty', !niche());
    openLink.href = 'mailto:' + TO + '?subject=' + encodeURIComponent(m.subject) + '&body=' + encodeURIComponent(m.body);
  }

  function open(trigger) {
    lastTrigger = trigger || null;
    overlay.classList.add('show');
    overlay.setAttribute('aria-hidden', 'false');
    document.body.classList.add('modal-open');
    render();
    setTimeout(function () { nameI.focus(); }, 0);
  }
  function close() {
    overlay.classList.remove('show');
    overlay.setAttribute('aria-hidden', 'true');
    document.body.classList.remove('modal-open');
    if (lastTrigger && lastTrigger.focus) lastTrigger.focus();
  }

  document.querySelectorAll('[data-compose]').forEach(function (a) {
    a.addEventListener('click', function (e) { e.preventDefault(); open(a); });
  });
  nameI.addEventListener('input', render);
  nicheI.addEventListener('input', render);
  sellI.addEventListener('input', render);

  // Progressive enhancement: submit via fetch and show a success state.
  // Without JS the form still posts to /Contact and works server-side.
  if (form && window.fetch) {
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      statusEl.textContent = '';
      statusEl.classList.remove('error');
      submitBtn.disabled = true;
      var originalLabel = submitBtn.textContent;
      submitBtn.textContent = 'Sending…';
      fetch(form.getAttribute('action') || '/Contact', { method: 'POST', body: new FormData(form) })
        .then(function (res) {
          if (res.ok) {
            statusEl.textContent = 'Thanks — I’ll be in touch shortly.';
            form.reset();
            render();
          } else {
            statusEl.textContent = 'Something went wrong — please use the email link below instead.';
            statusEl.classList.add('error');
          }
        })
        .catch(function () {
          statusEl.textContent = 'Something went wrong — please use the email link below instead.';
          statusEl.classList.add('error');
        })
        .finally(function () {
          submitBtn.disabled = false;
          submitBtn.textContent = originalLabel;
        });
    });
  }

  copyBtn.addEventListener('click', function () {
    var m = mail();
    var text = 'To: ' + TO + '\nSubject: ' + m.subject + '\n\n' + m.body;
    if (!navigator.clipboard) return;
    navigator.clipboard.writeText(text).then(function () {
      hint.textContent = 'Copied — paste it into any email app.';
      hint.classList.add('copied');
      setTimeout(function () {
        hint.textContent = 'Fill in your name and email — or open your email app to send the draft yourself.';
        hint.classList.remove('copied');
      }, 2200);
    });
  });

  document.getElementById('c-x').addEventListener('click', close);
  overlay.addEventListener('click', function (e) { if (e.target === overlay) close(); });
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && overlay.classList.contains('show')) close();
  });
  // focus trap
  overlay.addEventListener('keydown', function (e) {
    if (e.key !== 'Tab') return;
    var f = Array.prototype.filter.call(
      overlay.querySelectorAll('button, input, a[href]'),
      function (el) { return !el.disabled && el.tabIndex !== -1 && el.offsetParent !== null; }
    );
    if (!f.length) return;
    var first = f[0], last = f[f.length - 1];
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  });
})();

(function () {
  var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  document.body.classList.add('js');

  /* ---- scroll-reveal, staggered within each group ---- */
  if ('IntersectionObserver' in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
      });
    }, { threshold: 0.14, rootMargin: '0px 0px -40px 0px' });
    document.querySelectorAll('[data-reveal]').forEach(function (el) {
      var sibs = Array.prototype.filter.call(el.parentNode.children, function (c) { return c.hasAttribute('data-reveal'); });
      el.style.transitionDelay = (Math.min(sibs.indexOf(el), 6) * 70) + 'ms';
      io.observe(el);
    });
  } else {
    document.querySelectorAll('[data-reveal]').forEach(function (el) { el.classList.add('in'); });
  }

  /* ---- sticky status bar + ticking signal counter ---- */
  var sb = document.getElementById('statusbar');
  var onScroll = function () { sb.classList.toggle('show', window.scrollY > 360); };
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();
  var sbCount = document.getElementById('sbCount');
  if (!reduce) {
    var n = 2481;
    setInterval(function () { n += Math.floor(Math.random() * 3) + 1; sbCount.textContent = n.toLocaleString('en-US'); }, 2600);
  }

  /* ---- live lead feed in the CRT monitor ---- */
  var leads = [
    { co: 'GitLab',      color: '#E07A3E', channel: 'Slack', subj: 'Optimizing HR Systems for GitLab',     line: 'Hiring a Staff HRIS Analyst — investing in how they scale hiring.' },
    { co: 'Stripe',      color: '#5E9E94', channel: 'Mail',  subj: 'Optimizing Talent Acquisition at Stripe', line: 'Hiring a Talent & Development Facilitator — growing the team.' },
    { co: 'Patronus AI', color: '#6E6AB0', channel: 'Slack', subj: 'Scaling Headcount with AI Efficiency',   line: 'Just raised $50M amid insatiable demand — scaling headcount fast.' }
  ];
  var screen = document.getElementById('demoScreen');
  if (!screen) return;
  var idx = 0, qualified = 0, typer = null;

  function buildMsg(L) {
    var el = document.createElement('div');
    el.className = 'msg';
    el.innerHTML =
      '<div class="msg-head">' +
        '<span class="avatar" style="background:' + L.color + '">' + L.co.charAt(0) + '</span>' +
        '<span class="who">Coldstart</span>' +
        '<span class="chip">' + L.channel + '</span>' +
        '<span class="time">now</span>' +
      '</div>' +
      '<div class="fresh">fresh · just now</div>' +
      '<div class="verdict">● NEW QUALIFIED LEAD · ' + L.co + '</div>' +
      '<div class="subj">Subj: ' + L.subj + '</div>' +
      '<div class="type"></div>' +
      '<div class="acts"><span class="mini ok">Approve ✓</span><span class="mini no">Skip</span></div>';
    return el;
  }

  function typeInto(node, text) {
    if (typer) clearInterval(typer);
    var i = 0;
    var caret = document.createElement('span'); caret.className = 'caret';
    node.textContent = '';
    node.appendChild(caret);
    typer = setInterval(function () {
      if (i < text.length) { caret.insertAdjacentText('beforebegin', text.charAt(i)); i++; }
      else { clearInterval(typer); typer = null; setTimeout(function () { if (caret.parentNode) caret.remove(); }, 1400); }
    }, 24);
  }

  function pushLead() {
    var L = leads[idx % leads.length];
    var el = buildMsg(L);
    screen.appendChild(el);
    requestAnimationFrame(function () { requestAnimationFrame(function () { el.classList.add('in'); }); });
    typeInto(el.querySelector('.type'), L.line);
    qualified = (qualified % 3) + 1;
    while (screen.children.length > 2) screen.removeChild(screen.firstElementChild);
    idx++;
  }

  if (reduce) {
    screen.innerHTML = '';
    leads.forEach(function (L) {
      var el = buildMsg(L); el.classList.add('in');
      screen.appendChild(el);
      el.querySelector('.type').textContent = L.line;
    });
  } else {
    screen.innerHTML = '';
    pushLead();
    setInterval(pushLead, 4200);
  }
})();
