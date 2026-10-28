// QuotaDesk docs: sidebar filter, mobile menu, "Ask about this page" menu, "Copy for AI" and the
// "On this page" highlight. No dependencies; the page works without it (links stay plain links).
(function () {
  'use strict';
  var body = document.body;

  // ---------- Mobile menu
  var toggle = document.getElementById('nav-toggle');
  var sidebar = document.getElementById('sidebar');
  var header = document.querySelector('.topbar');

  function setHeaderHeight() {
    if (header) document.documentElement.style.setProperty('--header-h', header.offsetHeight + 'px');
  }
  function setNav(open) {
    if (!toggle || !sidebar) return;
    setHeaderHeight();
    sidebar.classList.toggle('open', open);
    body.classList.toggle('nav-open', open);
    toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
  }
  function isMobile() { return window.matchMedia('(max-width: 860px)').matches; }
  if (toggle) {
    toggle.addEventListener('click', function () { setNav(!sidebar.classList.contains('open')); });
  }
  window.addEventListener('resize', function () { setHeaderHeight(); if (!isMobile()) setNav(false); });
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && sidebar && sidebar.classList.contains('open')) { setNav(false); toggle.focus(); }
  });
  setHeaderHeight();

  // Keep the current page visible in a long sidebar.
  var current = sidebar && sidebar.querySelector('[aria-current="page"]');
  if (current && current.scrollIntoView && !isMobile()) {
    var r = current.getBoundingClientRect();
    if (r.bottom > window.innerHeight) current.scrollIntoView({ block: 'center' });
  }

  // ---------- Sidebar filter
  var search = document.getElementById('doc-search');
  function normalize(s) { return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase(); }
  if (search && sidebar) {
    var links = Array.prototype.slice.call(sidebar.querySelectorAll('li a'));
    links.forEach(function (a) { a.setAttribute('data-label', a.textContent); });
    var empty = sidebar.querySelector('.nav-empty');
    search.addEventListener('input', function () {
      var q = normalize(search.value.trim());
      var shown = 0;
      links.forEach(function (a) {
        var label = a.getAttribute('data-label');
        var hit = !q || normalize(label).indexOf(q) !== -1;
        a.parentNode.hidden = !hit;
        a.textContent = label;
        if (hit && q) {
          var start = normalize(label).indexOf(q);
          a.textContent = '';
          a.appendChild(document.createTextNode(label.slice(0, start)));
          var mark = document.createElement('mark');
          mark.textContent = label.slice(start, start + q.length);
          a.appendChild(mark);
          a.appendChild(document.createTextNode(label.slice(start + q.length)));
        }
        if (hit) shown++;
      });
      Array.prototype.forEach.call(sidebar.querySelectorAll('.nav-group'), function (g) {
        g.hidden = !g.querySelector('li:not([hidden])');
      });
      if (empty) empty.hidden = shown !== 0;
      if (isMobile()) setNav(q.length > 0 || sidebar.classList.contains('open'));
    });
    search.addEventListener('keydown', function (e) {
      if (e.key === 'Enter') {
        var first = sidebar.querySelector('li:not([hidden]) a');
        if (first) { e.preventDefault(); window.location.href = first.getAttribute('href'); }
      } else if (e.key === 'Escape') {
        search.value = '';
        search.dispatchEvent(new Event('input'));
      }
    });
    document.addEventListener('keydown', function (e) {
      var t = e.target;
      var typing = t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable);
      if (!typing && (e.key === '/' || ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k'))) {
        e.preventDefault();
        search.focus();
      }
    });
  }

  // ---------- "Ask about this page" menu
  var askBtn = document.getElementById('ask-btn');
  var askMenu = document.getElementById('ask-menu');
  if (askBtn && askMenu) {
    var items = Array.prototype.slice.call(askMenu.querySelectorAll('[role="menuitem"]'));
    var openMenu = function (focusIndex) {
      askMenu.hidden = false;
      askBtn.setAttribute('aria-expanded', 'true');
      if (focusIndex !== undefined && items[focusIndex]) items[focusIndex].focus();
    };
    var closeMenu = function (refocus) {
      if (askMenu.hidden) return;
      askMenu.hidden = true;
      askBtn.setAttribute('aria-expanded', 'false');
      if (refocus) askBtn.focus();
    };
    askBtn.addEventListener('click', function () {
      if (askMenu.hidden) openMenu(0); else closeMenu(false);
    });
    askBtn.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown') { e.preventDefault(); openMenu(0); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); openMenu(items.length - 1); }
    });
    askMenu.addEventListener('keydown', function (e) {
      var i = items.indexOf(document.activeElement);
      if (e.key === 'ArrowDown') { e.preventDefault(); items[(i + 1) % items.length].focus(); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); items[(i - 1 + items.length) % items.length].focus(); }
      else if (e.key === 'Home') { e.preventDefault(); items[0].focus(); }
      else if (e.key === 'End') { e.preventDefault(); items[items.length - 1].focus(); }
      else if (e.key === 'Escape') { e.preventDefault(); closeMenu(true); }
      else if (e.key === 'Tab') { closeMenu(false); }
    });
    items.forEach(function (a) { a.addEventListener('click', function () { closeMenu(false); }); });
    document.addEventListener('click', function (e) {
      if (!askMenu.hidden && !askMenu.contains(e.target) && !askBtn.contains(e.target)) closeMenu(false);
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && !askMenu.hidden) closeMenu(true);
    });
  }

  // ---------- "Copy for AI"
  var copyBtn = document.getElementById('copy-btn');
  var status = document.getElementById('copy-status');
  function legacyCopy(text) {
    var area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.top = '0';
    area.style.left = '0';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    var ok = false;
    try { ok = document.execCommand('copy'); } catch (err) { ok = false; }
    document.body.removeChild(area);
    return ok;
  }
  function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text).then(function () { return true; }, function () { return legacyCopy(text); });
    }
    return Promise.resolve(legacyCopy(text));
  }
  if (copyBtn) {
    var labelEl = copyBtn.querySelector('span');
    var iconEl = copyBtn.querySelector('svg');
    var copyIcon = iconEl ? iconEl.innerHTML : '';
    var checkIcon = '<path d="M20 6 9 17l-5-5"/>';
    var timer = null;
    var show = function (text, ok) {
      labelEl.textContent = text;
      if (status) status.textContent = text;
      copyBtn.classList.toggle('done', ok);
      if (iconEl && ok) iconEl.innerHTML = checkIcon;
      clearTimeout(timer);
      timer = setTimeout(function () {
        labelEl.textContent = copyBtn.getAttribute('data-label');
        copyBtn.classList.remove('done');
        if (iconEl) iconEl.innerHTML = copyIcon;
        if (status) status.textContent = '';
      }, 2000);
    };
    copyBtn.addEventListener('click', function () {
      var header = body.getAttribute('data-title') + ' (' + body.getAttribute('data-url') + ')\n\n';
      fetch(copyBtn.getAttribute('data-md'), { cache: 'no-cache' })
        .then(function (r) { if (!r.ok) throw new Error(r.status); return r.text(); })
        .then(function (md) { return copyText(header + md); })
        .then(function (ok) { show(copyBtn.getAttribute(ok ? 'data-done' : 'data-fail'), ok); })
        .catch(function () { show(copyBtn.getAttribute('data-fail'), false); });
    });
  }

  // ---------- "On this page": highlight the section being read
  var tocLinks = Array.prototype.slice.call(document.querySelectorAll('.toc a'));
  if (tocLinks.length && 'IntersectionObserver' in window) {
    var byId = {};
    tocLinks.forEach(function (a) { byId[decodeURIComponent(a.hash.slice(1))] = a; });
    var headings = tocLinks.map(function (a) { return document.getElementById(decodeURIComponent(a.hash.slice(1))); }).filter(Boolean);
    var visible = {};
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) { visible[en.target.id] = en.isIntersecting; });
      var active = null;
      for (var i = 0; i < headings.length; i++) { if (visible[headings[i].id]) { active = headings[i].id; break; } }
      if (!active) {
        for (var j = headings.length - 1; j >= 0; j--) { if (headings[j].getBoundingClientRect().top < 120) { active = headings[j].id; break; } }
      }
      tocLinks.forEach(function (a) { a.classList.remove('active'); });
      if (active && byId[active]) byId[active].classList.add('active');
    }, { rootMargin: '-70px 0px -60% 0px' });
    headings.forEach(function (h) { observer.observe(h); });
  }
})();
