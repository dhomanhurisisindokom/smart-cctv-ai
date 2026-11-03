// Smart CCTV Mini — popups and live AI streams. Plain JS, no framework.
(function () {
  'use strict';
  var modal = document.getElementById('modal');
  var box = document.getElementById('modal-box');
  var dirty = false;

  function antiForgery() {
    var input = document.querySelector('#af input[name=__RequestVerificationToken]');
    return input ? input.value : '';
  }

  function stopStreams(root) {
    // An MJPEG <img> keeps its connection open until its src is cleared.
    Array.prototype.forEach.call(root.querySelectorAll('img[data-stream]'), function (img) { img.removeAttribute('src'); });
  }

  function open(html, width) {
    box.style.maxWidth = (width || 560) + 'px';
    box.innerHTML = html;
    modal.classList.add('open');
    var focus = box.querySelector('[autofocus]');
    if (focus) focus.focus();
  }

  function close() {
    stopStreams(box);
    modal.classList.remove('open');
    box.innerHTML = '';
    if (dirty) location.reload();
  }

  modal.addEventListener('click', function (e) {
    if (e.target === modal || e.target.closest('[data-close]')) close();
  });
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && modal.classList.contains('open')) close();
  });

  // ---- Case detail popup (server-rendered partial; every action re-renders it) ----
  function loadDetail(id) {
    fetch(appRoot() + 'Violations/Detail/' + encodeURIComponent(id), { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
      .then(function (r) { if (r.status === 401) { location.reload(); throw 0; } return r.text(); })
      .then(function (html) { open(html, 560); })
      .catch(function (e) { if (e !== 0) alert('Detail tidak bisa dimuat.'); });
  }

  box.addEventListener('submit', function (e) {
    var form = e.target;
    if (!form.hasAttribute('data-ajax')) return;
    e.preventDefault();
    if (form.hasAttribute('data-confirm') && !confirm(form.getAttribute('data-confirm'))) return;
    var data = new FormData(form);
    data.append('__RequestVerificationToken', antiForgery());
    fetch(form.action, { method: 'POST', body: data, credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
      .then(function (r) { if (!r.ok) throw r.status; return r.text(); })
      .then(function (html) { dirty = true; box.innerHTML = html; })
      .catch(function () { alert('Perubahan gagal disimpan.'); });
  });

  // Assignment saves as soon as a person is picked.
  box.addEventListener('change', function (e) {
    if (e.target.hasAttribute('data-autosubmit')) e.target.form.requestSubmit ? e.target.form.requestSubmit() : e.target.form.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
  });

  // ---- AI streams: the browser reads FastAPI directly with a signed token ----
  function appRoot() { return document.body.getAttribute('data-root') || '/'; }

  function aiBase() {
    var ai = window.MINI_AI || {};
    if (ai.url) return ai.url.replace(/\/$/, '');
    return location.protocol + '//' + location.hostname + ':' + (ai.port || '8090');
  }

  function streamUrl(camera, mode) {
    return aiBase() + '/stream/' + camera + '/' + mode + '?t=' + encodeURIComponent((window.MINI_AI || {}).token || '');
  }

  function attach(img) {
    img.onerror = function () {
      // AI restarting or camera offline: retry quietly.
      setTimeout(function () { if (img.hasAttribute('src') || img.dataset.want) { img.src = streamUrl(img.dataset.camera, img.dataset.mode) + '&r=' + Date.now(); } }, 3000);
    };
    img.dataset.want = '1';
    img.src = streamUrl(img.dataset.camera, img.dataset.mode);
  }

  function openCamera(card) {
    var id = card.getAttribute('data-camera');
    var mode = card.getAttribute('data-mode');
    var esc = function (s) { var d = document.createElement('div'); d.textContent = s || ''; return d.innerHTML; };
    open(
      '<div class="modal-head"><div><h3>' + esc(card.getAttribute('data-name')) + '</h3><div class="sub">' + esc(card.getAttribute('data-location')) + '</div></div>' +
      '<div style="display:flex;gap:6.4px;margin-left:auto;margin-right:12px">' +
      '<button type="button" class="tab active" data-view="' + mode + '">Tampilan AI</button><button type="button" class="tab" data-view="raw">Live</button></div>' +
      '<button type="button" class="modal-close" data-close aria-label="Tutup">&times;</button></div>' +
      '<div class="view"><span>Menghubungkan ke layanan AI…</span><span class="tag">AI</span>' +
      '<img alt="" data-stream data-camera="' + esc(id) + '" data-mode="' + esc(mode) + '"></div>' +
      '<div class="hint" id="view-note" style="font-size:12.2px;padding:8.8px 14.4px;margin:0">Frame terakhir yang dianalisa AI, dengan kotak dan label dari model. Diperbarui setiap kali AI selesai menganalisa.</div>',
      960);
    attach(box.querySelector('img[data-stream]'));
  }

  box.addEventListener('click', function (e) {
    var tab = e.target.closest('[data-view]');
    if (!tab) return;
    var img = box.querySelector('img[data-stream]');
    Array.prototype.forEach.call(box.querySelectorAll('[data-view]'), function (t) { t.classList.toggle('active', t === tab); });
    img.dataset.mode = tab.getAttribute('data-view');
    box.querySelector('.tag').textContent = img.dataset.mode === 'raw' ? 'LIVE' : 'AI';
    document.getElementById('view-note').textContent = img.dataset.mode === 'raw'
      ? 'Video kamera tanpa analisa AI (±5 fps).'
      : 'Frame terakhir yang dianalisa AI, dengan kotak dan label dari model. Diperbarui setiap kali AI selesai menganalisa.';
    img.src = streamUrl(img.dataset.camera, img.dataset.mode);
  });

  document.addEventListener('click', function (e) {
    var detail = e.target.closest('[data-detail]');
    if (detail) { e.preventDefault(); loadDetail(detail.getAttribute('data-detail')); return; }
    var card = e.target.closest('[data-open-camera]');
    if (card) { e.preventDefault(); openCamera(card); return; }
    var tpl = e.target.closest('[data-template]');
    if (tpl) { e.preventDefault(); openTemplate(tpl); }
  });

  // ---- Edit forms (camera, user): a <template> filled from the button's data-* ----
  function openTemplate(button) {
    var template = document.getElementById(button.getAttribute('data-template'));
    open(template.innerHTML, parseInt(template.getAttribute('data-width') || '420', 10));
    var values = JSON.parse(button.getAttribute('data-values') || '{}');
    Object.keys(values).forEach(function (name) {
      Array.prototype.forEach.call(box.querySelectorAll('[name="' + name + '"]'), function (field) {
        if (field.type === 'checkbox') field.checked = !!values[name];
        else field.value = values[name] == null ? '' : values[name];
      });
      Array.prototype.forEach.call(box.querySelectorAll('[data-text="' + name + '"]'), function (el) { el.textContent = values[name] || ''; });
    });
    Array.prototype.forEach.call(box.querySelectorAll('[data-show-if]'), function (el) {
      el.style.display = values[el.getAttribute('data-show-if')] ? '' : 'none';
    });
  }

  // ---- Camera cards: live AI stream + status line ----
  var cards = document.querySelectorAll('[data-camera-card]');
  Array.prototype.forEach.call(cards, function (card) {
    var img = card.querySelector('img[data-stream]');
    if (img) attach(img);
  });

  var badges = document.querySelectorAll('[data-ai-status]');

  function pollStatus() {
    if (document.hidden) return;
    fetch(aiBase() + '/api/status?t=' + encodeURIComponent((window.MINI_AI || {}).token || ''), { cache: 'no-store' })
      .then(function (r) { return r.json(); })
      .then(function (s) {
        Array.prototype.forEach.call(cards, function (card) {
          var stat = card.querySelector('.stat');
          var msg = card.querySelector('.msg');
          var cam = (s.cameras || []).filter(function (c) { return String(c.id) === card.getAttribute('data-camera'); })[0];
          var text;
          if (s.models === 'loading') text = 'Model AI sedang dimuat…';
          else if (s.models === 'error') text = 'Model AI gagal dimuat';
          else if (!cam || !cam.ai_enabled) text = 'AI nonaktif';
          else if (!cam.connected) text = 'Kamera belum tersambung';
          else if (!cam.ready) text = 'Menunggu hasil AI…';
          else text = cam.fps.toFixed(1) + ' sampel/dtk · ' + cam.ms + ' ms';
          if (stat) stat.textContent = text;
          if (msg) msg.style.display = cam && cam.ready ? 'none' : '';
          if (msg && !(cam && cam.ready)) msg.textContent = text;
        });
        // Pengaturan: one badge per camera.
        Array.prototype.forEach.call(badges, function (badge) {
          var cam = (s.cameras || []).filter(function (c) { return String(c.id) === badge.getAttribute('data-ai-status'); })[0];
          if (!cam || !cam.ai_enabled) return;
          var ok = cam.connected;
          badge.className = 'badge ' + (ok ? 'b-success' : 'b-warning');
          badge.textContent = s.models === 'loading' ? 'Memuat model' : ok ? 'Live' : 'Terputus';
        });
      })
      .catch(function () {
        Array.prototype.forEach.call(cards, function (card) {
          var stat = card.querySelector('.stat');
          if (stat) stat.textContent = 'Layanan AI tidak terjangkau';
        });
        Array.prototype.forEach.call(badges, function (badge) {
          if (badge.textContent !== '–') { badge.className = 'badge b-muted'; badge.textContent = 'AI offline'; }
        });
      });
  }
  if (cards.length || badges.length) { pollStatus(); setInterval(pollStatus, 2000); }

  window.addEventListener('pagehide', function () { stopStreams(document); });
})();
