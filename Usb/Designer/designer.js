// FX Pro Dash Studio (served by the FXPro Unlocked plugin or `fxdash serve`). Edits the dash JSON format
// (docs/dash-format.md); checks, exact previews, SimHub import and the wheel go through the local API (GET /api).
// Agents: window.fxdash = { dash, load(d), check() }.
'use strict';

const W = 800, H = 480;
const $ = id => document.getElementById(id);
const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

// ---------- icons (stroke paths, 24 x 24) ----------
const ICONS = {
  wheel: 'M4 9c0-3 3-4 8-4s8 1 8 4v6c0 2-1 3-3 3l-2-2H9l-2 2c-2 0-3-1-3-3z M9 8h6v4H9z',
  undo: 'M9 14 4 9l5-5 M4 9h10a6 6 0 0 1 0 12h-3', redo: 'm15 14 5-5-5-5 M20 9H10a6 6 0 0 0 0 12h3',
  edit: 'M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z', eye: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z',
  eyeoff: 'M3 3l18 18 M10.6 10.6a2 2 0 0 0 2.8 2.8 M9.9 5.1A10 10 0 0 1 12 5c6 0 10 7 10 7a17 17 0 0 1-3.2 3.9 M6.6 6.6C3.9 8.4 2 12 2 12s4 7 10 7c1.7 0 3.3-.5 4.6-1.2',
  play: 'M6 4l14 8-14 8z', save: 'M5 3h11l3 3v15H5z M8 3v6h8 M8 21v-7h8v7', more: 'M5 12h.01 M12 12h.01 M19 12h.01',
  copy: 'M9 9h11v11H9z M5 15H4V4h11v1', download: 'M12 3v12 M7 10l5 5 5-5 M5 21h14', upload: 'M12 21V9 M7 14l5-5 5 5 M5 3h14',
  plus: 'M12 5v14 M5 12h14', minus: 'M5 12h14', import: 'M12 3v11 M8 10l4 4 4-4 M4 15v4h16v-4', keyboard: 'M3 6h18v12H3z M7 10h.01 M11 10h.01 M15 10h.01 M7 14h10',
  trash: 'M4 7h16 M9 7V4h6v3 M6 7l1 13h10l1-13', layers: 'm12 3 9 5-9 5-9-5z m-9 9 9 5 9-5 M3 16l9 5 9-5', grid: 'M4 4h7v7H4z M13 4h7v7h-7z M4 13h7v7H4z M13 13h7v7h-7z',
  search: 'M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z m9 3-4.5-4.5', fit: 'M4 9V4h5 M20 9V4h-5 M4 15v5h5 M20 15v5h-5', x: 'M6 6l12 12 M18 6 6 18',
  sparkle: 'M12 3v4 M12 17v4 M3 12h4 M17 12h4 M6 6l2.5 2.5 M15.5 15.5 18 18 M18 6l-2.5 2.5 M8.5 15.5 6 18',
  text: 'M5 6V4h14v2 M12 4v16 M9 20h6', value: 'M4 17 9 7l3 6 3-4 5 8', rect: 'M4 6h16v12H4z', box: 'M4 6h16v12H4z M7 9h10v6H7z',
  ellipse: 'M12 19c4.4 0 8-3.1 8-7s-3.6-7-8-7-8 3.1-8 7 3.6 7 8 7z', gradient: 'M4 6h16v12H4z M8 6v12 M12 6v12 M16 6v12', image: 'M4 5h16v14H4z M4 16l5-5 4 4 3-3 4 4 M15 9h.01',
  bar: 'M3 10h18v5H3z M3 10h11v5H3z', deltabar: 'M3 12h3 M7 9v6 M10 10v4 M14 10v4 M17 9v6 M20 12h1', popup: 'M4 5h16v11H9l-5 4z M8 9h8 M8 12h5',
  grip: 'M9 6h.01 M15 6h.01 M9 12h.01 M15 12h.01 M9 18h.01 M15 18h.01', warn: 'M12 3 2 20h20z M12 9v5 M12 17h.01', err: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z M9 9l6 6 M15 9l-6 6',
  ok: 'M20 6 9 17l-5-5', chev: 'm6 9 6 6 6-6', up: 'm6 15 6-6 6 6', down: 'm6 9 6 6 6-6', front: 'M8 8h12v12H8z M4 4h12v4 M4 4v12h4', back: 'M4 4h12v12H4z M8 20h12V8',
  alignL: 'M4 3v18 M8 7h10v4H8z M8 14h6v4H8z', alignCH: 'M12 3v18 M6 7h12v4H6z M8 14h8v4H8z', alignR: 'M20 3v18 M6 7h10v4H6z M10 14h6v4h-6z',
  alignT: 'M3 4h18 M7 8v10h4V8z M14 8v6h4V8z', alignCV: 'M3 12h18 M7 6v12h4V6z M14 8v8h4V8z', alignB: 'M3 20h18 M7 6v10h4V6z M14 10v6h4v-6z',
  tLeft: 'M4 6h16 M4 10h10 M4 14h16 M4 18h10', tCenter: 'M4 6h16 M7 10h10 M4 14h16 M7 18h10', tRight: 'M4 6h16 M10 10h10 M4 14h16 M10 18h10',
  data: 'M12 3c5 0 8 1.3 8 3s-3 3-8 3-8-1.3-8-3 3-3 8-3z M4 6v6c0 1.7 3 3 8 3s8-1.3 8-3V6 M4 12v6c0 1.7 3 3 8 3s8-1.3 8-3v-6', wand: 'M15 4V2 M15 16v-2 M8 9h2 M20 9h2 M17.8 11.8 19 13 M17.8 6.2 19 5 M12.2 6.2 11 5 M15 9 3 21',
  cond: 'M9 11l3 3L22 4 M21 12v7H3V5h11', code: 'm16 18 6-6-6-6 M8 6l-6 6 6 6', info: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z M12 16v-4 M12 8h.01',
};
const icon = (name, cls = 'i') => `<svg class="${cls}" viewBox="0 0 24 24"><path d="${ICONS[name] || ''}"/></svg>`;
const hydrateIcons = root => root.querySelectorAll('[data-icon]').forEach(el => { if (!el.querySelector('svg')) el.insertAdjacentHTML('afterbegin', icon(el.dataset.icon)); });

// ---------- api ----------
const api = async (path, opt = {}) => {
  const r = await fetch(path, opt);
  const type = r.headers.get('content-type') || '';
  if (type.startsWith('image/')) return r.blob();
  const j = await r.json();
  if (!r.ok || (j && j.error)) throw new Error(j && j.error || r.statusText);
  return j;
};
const post = (path, body) => api(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: typeof body === 'string' ? body : JSON.stringify(body) });

// ---------- toasts ----------
function toast(msg, level = 'ok', ms = 2600) {
  const t = document.createElement('div');
  t.className = 'toast ' + level;
  t.innerHTML = `<span class="lv">${icon(level === 'err' ? 'err' : level === 'warn' ? 'warn' : 'ok')}</span><span>${esc(msg)}</span>`;
  $('toasts').appendChild(t);
  setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 300); }, ms);
}

// ---------- element types ----------
const TYPES = {
  label: { name: 'Label', blurb: 'Fixed text', icon: 'text' },
  value: { name: 'Value', blurb: 'Live data as text', icon: 'value' },
  bar: { name: 'Bar', blurb: 'Fills with data', icon: 'bar' },
  rect: { name: 'Rectangle', blurb: 'Solid block', icon: 'rect' },
  box: { name: 'Frame', blurb: 'Rounded border', icon: 'box' },
  ellipse: { name: 'Circle', blurb: 'Ellipse or ring', icon: 'ellipse' },
  gradient: { name: 'Gradient', blurb: 'Colour blend', icon: 'gradient' },
  image: { name: 'Picture', blurb: 'From a file', icon: 'image' },
  deltabar: { name: 'Delta bar', blurb: 'Gain / loss', icon: 'deltabar' },
  popup: { name: 'Pop-up', blurb: 'On TC / ABS change', icon: 'popup' },
};
const DEFAULTS = {
  label: { Text: 'LABEL', W: 180, H: 30, Font: 14, Color: '#D3D3D3', Align: 'left' },
  value: { Bind: 'speed', Format: '0', W: 180, H: 44, Font: 98, Color: '#FFFFFF', Align: 'center', Empty: '-', Samples: ['388'] },
  rect: { W: 200, H: 100, Color: '#428AED' },
  box: { W: 200, H: 100, Color: '#FFFFFF', Border: 3, Radius: 10 },
  ellipse: { W: 160, H: 120, Color: '#FFFFFF', Border: 4, Fill: '#1B1F3C' },
  gradient: { W: 200, H: 100, Colors: ['#1B1F3C', '#428AED'], Angle: 90 },
  bar: { Bind: 'rpmPercent', Min: 0, Max: 100, W: 500, H: 30, Color: '#00FF40', Fill: '#202020', Orientation: 'horizontal' },
  deltabar: { Bind: 'delta', H: 50, Segments: 7, Pitch: 28, SegmentWidth: 20, Range: 1, PositiveColor: '#FF0000', NegativeColor: '#00FF00', SegmentColor: '#808080' },
  popup: { W: 240, H: 160, Radius: 10, Font: 101, ValueFont: 35, Duration: 2, Color: '#000000', Watch: [{ Bind: 'tcLevel', Label: 'TC', Color: '#28598B', Format: 'int' }] },
};
const FORMATS = [['0', '123'], ['0.0', '12.3'], ['0.00', '1.23'], ['int', 'Whole'], ['laptime', '1:23.456'], ['gear', 'R N 1'], ['delta', '+0.12'], ['text', 'Text']];
const PALETTE = ['#FFFFFF', '#D3D3D3', '#8A8F98', '#3A3F4A', '#1B1F3C', '#000000', '#FF1F2D', '#FF1A1A', '#FF5A00', '#FFB000', '#FFD000', '#FFFF00',
  '#16D65A', '#00FF40', '#00FFA3', '#00F0FF', '#428AED', '#0060FF', '#2F6BFF', '#7B2FFF', '#9D4EDD', '#FF00D0', '#FF2E97', '#28598B'];

// ---------- state ----------
let dash = null, sel = -1, hover = -1, dirty = false;
let metrics = null, fonts = [], bindings = [], hostInfo = null, library = [];
const undoStack = [], redoStack = [];
const images = {};
let zoom = 1, view = 'edit', clipboard = null;
let pad = { l: 10, t: 20 };

const isShape = t => ['rect', 'ellipse', 'box', 'gradient', 'image'].includes(t);
const visList = e => !e.Visible ? [] : Array.isArray(e.Visible) ? e.Visible : [e.Visible];
const isConditional = e => visList(e).length > 0;
const shownInPreview = e => !isConditional(e) || e.PreviewVisible !== false;
const nameOf = e => e.Name || e.Text || e.Bind || e.Type;
const cur = () => dash && dash.Elements[sel];

function parseColor(s, fallback = 'rgba(255,255,255,1)') {
  if (!s) return fallback;
  s = String(s).trim();
  if (s[0] !== '#') return s;
  let h = s.slice(1);
  if (h.length === 3) h = h.split('').map(c => c + c).join('');
  if (h.length === 6) return '#' + h;
  if (h.length === 8) { const a = parseInt(h.slice(0, 2), 16) / 255; return `rgba(${parseInt(h.slice(2, 4), 16)},${parseInt(h.slice(4, 6), 16)},${parseInt(h.slice(6, 8), 16)},${a.toFixed(3)})`; }
  return fallback;
}
const hex6 = s => { if (!s || s[0] !== '#') return '#FFFFFF'; let h = s.slice(1); if (h.length === 8) h = h.slice(2); if (h.length === 3) h = h.split('').map(c => c + c).join(''); return ('#' + h.slice(0, 6)).toUpperCase(); };
const alphaOf = s => (s && s[0] === '#' && s.length === 9) ? parseInt(s.slice(1, 3), 16) : 255;
const withAlpha = (hex, a) => a >= 255 ? hex6(hex) : '#' + a.toString(16).padStart(2, '0').toUpperCase() + hex6(hex).slice(1);

function fontHeight(f) { return metrics && metrics.heights[f] || 0; }
function charWidth(f, ch) { const c = ch.charCodeAt(0); if (!metrics || !metrics.widths[f] || c < 32 || c > 126) return -1; return metrics.widths[f][c - 32]; }
function textWidth(f, t) { let w = 0; for (const ch of String(t)) { const g = charWidth(f, ch); if (g < 0) return -1; w += g; } return w; }

// ---------- history ----------
const snapshot = () => JSON.stringify(dash);
function begin() { undoStack.push(snapshot()); if (undoStack.length > 300) undoStack.shift(); redoStack.length = 0; updateUndo(); }
function changed(opt = {}) {
  setDirty(true);
  if (!opt.keepLayers) renderLayers();
  draw();
  if (opt.inspector) renderInspector();
  scheduleCheck(); scheduleWheel(); saveDraft(); updateHint();
}
function doUndo() { if (!undoStack.length) return; redoStack.push(snapshot()); restore(JSON.parse(undoStack.pop())); }
function doRedo() { if (!redoStack.length) return; undoStack.push(snapshot()); restore(JSON.parse(redoStack.pop())); }
function restore(d) { dash = d; loadImages(); sel = Math.min(sel, dash.Elements.length - 1); setDirty(true); renderAll(); scheduleCheck(); scheduleWheel(); updateUndo(); }
function updateUndo() { $('btnUndo').disabled = !undoStack.length; $('btnRedo').disabled = !redoStack.length; }
function setDirty(v) { dirty = v; $('dirty').classList.toggle('on', v); }
function saveDraft() { try { localStorage.setItem('fxdash-draft', snapshot()); } catch (e) { } }

// ---------- loading ----------
function load(d, opt = {}) {
  dash = d;
  dash.Elements = dash.Elements || [];
  dash.Images = dash.Images || {};
  undoStack.length = 0; redoStack.length = 0; updateUndo();
  setDirty(!!opt.dirty);
  sel = -1; hover = -1;
  loadImages();
  renderAll();
  scheduleCheck(0); scheduleWheel();
  markLibrary();
}
function loadImages() {
  for (const k of Object.keys(images)) delete images[k];
  for (const [k, v] of Object.entries(dash.Images || {})) { const img = new Image(); img.onload = draw; img.src = 'data:image/png;base64,' + v; images[k] = img; }
}
const blankDash = () => ({ FormatVersion: 2, Id: 'my-dash', Name: 'My dash', Author: '', Description: '', Elements: [], Images: {} });
function renderAll() { renderHeader(); renderLayers(); renderInspector(); draw(); updateHint(); }
function renderHeader() { $('dashName').value = dash.Name || ''; $('dashId').textContent = dash.Id ? dash.Id + '.json' : ''; }
function updateHint() { $('hint').style.opacity = dash && dash.Elements.length ? 0 : 1; }

// ---------- canvas drawing ----------
const canvas = $('canvas'), ctx = canvas.getContext('2d');

function draw() {
  if (!dash) return;
  const scale = zoom * (window.devicePixelRatio || 1);
  if (canvas.width !== Math.round(W * scale)) { canvas.width = Math.round(W * scale); canvas.height = Math.round(H * scale); }
  ctx.setTransform(scale, 0, 0, scale, 0, 0);
  ctx.fillStyle = '#000'; ctx.fillRect(0, 0, W, H);
  dash.Elements.forEach((e, i) => {
    if (e.Type === 'popup' ? i !== sel : !shownInPreview(e)) return;
    ctx.save(); drawElement(e); ctx.restore();
  });
  placeOverlay();
}

const withOpacity = e => { ctx.globalAlpha = (e.Opacity ?? 100) / 100; };
function roundRect(x, y, w, h, r) {
  r = Math.max(0, Math.min(r || 0, w / 2, h / 2));
  ctx.beginPath(); ctx.moveTo(x + r, y); ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r); ctx.closePath();
}
function segX(e) { const n = e.Segments || 7; return (e.SegmentX && e.SegmentX.length === n * 2) ? e.SegmentX : Array.from({ length: n * 2 }, (_, k) => (e.X || 0) + Math.round(k * (e.Pitch || 40))); }

function drawElement(e) {
  const { X: x = 0, Y: y = 0, W: w = 0, H: h = 0 } = e;
  switch (e.Type) {
    case 'rect': withOpacity(e); ctx.fillStyle = parseColor(e.Color); ctx.fillRect(x, y, w, h); break;
    case 'ellipse': {
      withOpacity(e); const b = e.Border || 0;
      if (b <= 0) { ctx.beginPath(); ctx.ellipse(x + w / 2, y + h / 2, w / 2, h / 2, 0, 0, Math.PI * 2); ctx.fillStyle = parseColor(e.Color); ctx.fill(); break; }
      if (e.Fill) { ctx.beginPath(); ctx.ellipse(x + w / 2, y + h / 2, Math.max(0, w / 2 - b), Math.max(0, h / 2 - b), 0, 0, Math.PI * 2); ctx.fillStyle = parseColor(e.Fill); ctx.fill(); }
      ctx.beginPath(); ctx.ellipse(x + w / 2, y + h / 2, Math.max(0, w / 2 - b / 2), Math.max(0, h / 2 - b / 2), 0, 0, Math.PI * 2);
      ctx.lineWidth = b; ctx.strokeStyle = parseColor(e.Color); ctx.stroke(); break;
    }
    case 'box': {
      withOpacity(e); const b = e.Border || 0;
      if (e.Fill) { roundRect(x, y, w, h, e.Radius); ctx.fillStyle = parseColor(e.Fill); ctx.fill(); }
      if (b > 0) { ctx.lineWidth = b; ctx.strokeStyle = parseColor(e.Color); roundRect(x + b / 2, y + b / 2, w - b, h - b, Math.max(0, (e.Radius || 0) - b / 2)); ctx.stroke(); }
      break;
    }
    case 'gradient': {
      withOpacity(e);
      const cols = (e.Colors && e.Colors.length ? e.Colors : [e.Color || '#fff', e.Color || '#fff']);
      const a = (e.Angle ?? 90) * Math.PI / 180, cx = x + w / 2, cy = y + h / 2, len = Math.abs(w * Math.cos(a)) / 2 + Math.abs(h * Math.sin(a)) / 2;
      const g = ctx.createLinearGradient(cx - Math.cos(a) * len, cy - Math.sin(a) * len, cx + Math.cos(a) * len, cy + Math.sin(a) * len);
      cols.forEach((c, i) => g.addColorStop(cols.length === 1 ? 0 : i / (cols.length - 1), parseColor(c)));
      roundRect(x, y, w, h, e.Radius); ctx.fillStyle = g; ctx.fill();
      if (e.Border > 0) { ctx.lineWidth = e.Border; ctx.strokeStyle = parseColor(e.Color || '#808080'); roundRect(x + e.Border / 2, y + e.Border / 2, w - e.Border, h - e.Border, e.Radius); ctx.stroke(); }
      break;
    }
    case 'image': {
      withOpacity(e); const img = images[e.Image];
      if (img && img.complete) ctx.drawImage(img, x, y, w, h);
      else { ctx.strokeStyle = '#666'; ctx.setLineDash([4, 3]); ctx.strokeRect(x + .5, y + .5, w - 1, h - 1); ctx.setLineDash([]); }
      break;
    }
    case 'label': drawText(e, e.Text || '', e.Color); break;
    case 'value': {
      const t = e.PreviewText ?? (e.Samples && e.Samples[0]) ?? e.Empty ?? '';
      if (e.Background) { ctx.fillStyle = parseColor(e.Background); ctx.fillRect(x, y, w, h); }
      drawText(e, t, e.Color); break;
    }
    case 'bar': {
      withOpacity(e); const vert = e.Orientation === 'vertical', f = .62;
      if (e.Fill) { ctx.fillStyle = parseColor(e.Fill); ctx.fillRect(x, y, w, h); }
      ctx.fillStyle = parseColor(e.Color || '#00ff00');
      if (!vert) ctx.fillRect(e.Reverse ? x + w * (1 - f) : x, y, w * f, h); else ctx.fillRect(x, e.Reverse ? y : y + h * (1 - f), w, h * f);
      break;
    }
    case 'deltabar': {
      const n = e.Segments || 7;
      segX(e).forEach((sx, k) => { ctx.fillStyle = parseColor(k === n - 1 || k === n - 2 ? e.PositiveColor || '#ff0000' : e.SegmentColor || '#808080'); ctx.fillRect(sx, y, e.SegmentWidth || 30, h); });
      break;
    }
    case 'popup': {
      roundRect(x, y, w, h, e.Radius); ctx.fillStyle = parseColor((e.Watch && e.Watch[0] && e.Watch[0].Color) || '#28598B'); ctx.fill();
      const lbl = { X: x, Y: y + 8, W: w, H: fontHeight(e.Font ?? 101), Font: e.Font ?? 101, Align: 'center' };
      drawText(lbl, (e.Watch && e.Watch[0] && e.Watch[0].Label) || 'TC', e.Color || '#000');
      const val = { X: x, Y: y + h - fontHeight(e.ValueFont ?? 35) - 10, W: w, H: fontHeight(e.ValueFont ?? 35), Font: e.ValueFont ?? 35, Align: 'center' };
      drawText(val, '6', e.Color || '#000');
      break;
    }
  }
}

function drawText(e, text, colour) {
  const f = e.Font ?? 14, fh = fontHeight(f);
  if (!fh) return;
  text = String(text).replace(/[^\x20-\x7e]/g, '');
  const cells = []; let width = 0;
  for (const ch of text) { const cw = Math.max(0, charWidth(f, ch)); if (width + cw > e.W) break; cells.push([ch, cw]); width += cw; }
  let x = e.Align === 'center' ? e.X + (e.W - width) / 2 : e.Align === 'right' ? e.X + e.W - width : e.X;
  const y = e.Y + (e.H - fh) / 2;
  ctx.save();
  ctx.beginPath(); ctx.rect(e.X, e.Y, e.W, e.H); ctx.clip();
  ctx.fillStyle = parseColor(colour || '#ffffff');
  ctx.font = `${fh >= 90 ? 'bold ' : ''}${Math.max(6, fh * .78)}px Bahnschrift, "Segoe UI", sans-serif`;
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  for (const [ch, cw] of cells) { ctx.fillText(ch, x + cw / 2, y + fh / 2); x += cw; }
  ctx.restore();
}

function bounds(e) {
  if (!e) return null;
  if (e.Type === 'deltabar') { const xs = segX(e), x0 = Math.min(...xs), x1 = Math.max(...xs) + (e.SegmentWidth || 30); return { x: x0, y: e.Y || 0, w: x1 - x0, h: e.H || 0 }; }
  return { x: e.X || 0, y: e.Y || 0, w: e.W || 0, h: e.H || 0 };
}

// ---------- overlay ----------
function place(el, r) { el.style.left = r.x * zoom + 'px'; el.style.top = r.y * zoom + 'px'; el.style.width = Math.max(1, r.w * zoom) + 'px'; el.style.height = Math.max(1, r.h * zoom) + 'px'; }
function placeOverlay() {
  const scr = $('screen');
  scr.style.width = W * zoom + 'px'; scr.style.height = H * zoom + 'px';
  place($('safe'), { x: pad.l, y: pad.t, w: W - pad.l, h: H - pad.t });
  const hb = $('hoverbox'), hv = hover !== sel && dash && dash.Elements[hover];
  hb.classList.toggle('on', !!hv && view === 'edit');
  if (hv) place(hb, bounds(hv));
  const sb = $('selbox'), s = cur();
  const was = sb.classList.contains('on');
  sb.classList.toggle('on', !!s && view === 'edit');
  if (!was && s) { sb.style.animation = 'none'; void sb.offsetWidth; sb.style.animation = ''; }
  sb.classList.toggle('nohandles', !!s && s.Type === 'deltabar');
  if (s) place(sb, bounds(s));
}
function flashAt(r) {
  const f = document.createElement('div'); f.className = 'flash'; place(f, r);
  $('overlay').appendChild(f); setTimeout(() => f.remove(), 1000);
}
let guideEls = [];
function showGuides(xs, ys) {
  guideEls.forEach(g => g.remove()); guideEls = [];
  for (const x of xs) { const g = document.createElement('div'); g.className = 'guide v'; g.style.left = x * zoom + 'px'; $('overlay').appendChild(g); guideEls.push(g); }
  for (const y of ys) { const g = document.createElement('div'); g.className = 'guide h'; g.style.top = y * zoom + 'px'; $('overlay').appendChild(g); guideEls.push(g); }
}

// ---------- zoom ----------
function setZoom(z, opt = {}) {
  zoom = clamp(Math.round(z * 100) / 100, .4, 3);
  $('zoomVal').textContent = Math.round(zoom * 100) + '%';
  draw();
  if (!opt.silent) localStorage.setItem('fxdash-zoom', String(zoom));
}
function fitZoom() {
  const st = $('stage');
  setZoom(Math.min((st.clientWidth - 110) / W, (st.clientHeight - 150) / H), { silent: true });
}

// ---------- mouse on the canvas ----------
const screenEl = $('screen');
function pos(ev) { const b = screenEl.getBoundingClientRect(); return { x: (ev.clientX - b.left) / zoom, y: (ev.clientY - b.top) / zoom }; }
function hitTest(p) {
  if (!dash) return -1;
  for (let i = dash.Elements.length - 1; i >= 0; i--) {
    const e = dash.Elements[i];
    if (e.Type === 'popup' ? i !== sel : (!shownInPreview(e) && i !== sel)) continue;
    const r = bounds(e);
    if (p.x >= r.x && p.x <= r.x + r.w && p.y >= r.y && p.y <= r.y + r.h) return i;
  }
  return -1;
}

let drag = null;
screenEl.addEventListener('mousedown', ev => {
  if (!dash || view !== 'edit' || ev.button !== 0) return;
  hideMenus();
  const p = pos(ev), h = ev.target.dataset.h;
  const s = cur();
  if (h && s) { begin(); drag = { mode: 'resize', h, start: p, cx: ev.clientX, cy: ev.clientY, orig: { ...s }, ratio: (s.W || 1) / (s.H || 1) }; $('selbox').classList.add('dragging'); ev.preventDefault(); return; }
  const hit = hitTest(p);
  select(hit);
  if (hit >= 0) { begin(); const e = dash.Elements[hit]; drag = { mode: 'move', start: p, cx: ev.clientX, cy: ev.clientY, orig: { X: e.X || 0, Y: e.Y || 0, SegmentX: e.SegmentX ? [...e.SegmentX] : null } }; $('selbox').classList.add('dragging'); }
  ev.preventDefault();
});
screenEl.addEventListener('mousemove', ev => {
  const p = pos(ev);
  $('coords').textContent = `${Math.round(p.x)} , ${Math.round(p.y)}`;
  if (drag || view !== 'edit') return;
  const h = hitTest(p);
  if (h !== hover) { hover = h; placeOverlay(); highlightLayer(h); }
  screenEl.style.cursor = h >= 0 ? 'move' : 'default';
});
screenEl.addEventListener('mouseleave', () => { $('coords').textContent = '— , —'; if (hover !== -1) { hover = -1; placeOverlay(); highlightLayer(-1); } });
screenEl.addEventListener('dblclick', ev => {
  const e = cur(); if (!e) return;
  const inp = document.querySelector(e.Type === 'label' ? '#inspector [data-k="Text"]' : e.Type === 'value' ? '#inspector .bindbtn' : '#inspector .insp-head input');
  if (inp) { inp.focus(); if (inp.select) inp.select(); if (inp.click && inp.classList.contains('bindbtn')) inp.click(); }
});
screenEl.addEventListener('contextmenu', ev => {
  ev.preventDefault(); if (!dash || view !== 'edit') return;
  const hit = hitTest(pos(ev)); if (hit >= 0) select(hit);
  openContext(ev.clientX, ev.clientY);
});

function snapLines(skip) {
  const xs = [0, W, W / 2, pad.l, pad.l + (W - pad.l) / 2], ys = [0, H, H / 2, pad.t, pad.t + (H - pad.t) / 2];
  dash.Elements.forEach((e, i) => {
    if (i === skip || e.Type === 'popup' || !shownInPreview(e)) return;
    const r = bounds(e);
    xs.push(r.x, r.x + r.w / 2, r.x + r.w); ys.push(r.y, r.y + r.h / 2, r.y + r.h);
  });
  return { xs, ys };
}
function snap1(values, lines, tol) {
  let best = null;
  for (const v of values) for (const l of lines) { const d = l - v; if (Math.abs(d) <= tol && (!best || Math.abs(d) < Math.abs(best.d))) best = { d, line: l }; }
  return best;
}

window.addEventListener('mousemove', ev => {
  if (!drag || !dash) return;
  const p = pos(ev), e = cur(); if (!e) return;
  // a click isn't a drag: nothing moves (or gets an undo step) until the mouse has gone 3 screen px
  if (!drag.moved && Math.hypot(ev.clientX - drag.cx, ev.clientY - drag.cy) < 3) return;
  let dx = Math.round(p.x - drag.start.x), dy = Math.round(p.y - drag.start.y);
  const tol = 6 / zoom, lines = ev.altKey ? { xs: [], ys: [] } : snapLines(sel);
  const gx = [], gy = [];
  if (drag.mode === 'move') {
    const r0 = bounds({ ...e, X: drag.orig.X, Y: drag.orig.Y, SegmentX: drag.orig.SegmentX });
    if (ev.shiftKey) { if (Math.abs(dx) > Math.abs(dy)) dy = 0; else dx = 0; }
    const sx = snap1([r0.x + dx, r0.x + dx + r0.w / 2, r0.x + dx + r0.w], lines.xs, tol);
    const sy = snap1([r0.y + dy, r0.y + dy + r0.h / 2, r0.y + dy + r0.h], lines.ys, tol);
    if (sx) { dx += Math.round(sx.d); gx.push(sx.line); }
    if (sy) { dy += Math.round(sy.d); gy.push(sy.line); }
    e.X = drag.orig.X + dx; e.Y = drag.orig.Y + dy;
    if (drag.orig.SegmentX) e.SegmentX = drag.orig.SegmentX.map(v => v + dx);
  } else {
    const o = drag.orig, hd = drag.h;
    let x0 = o.X, y0 = o.Y, x1 = o.X + o.W, y1 = o.Y + o.H;
    if (hd.includes('w')) { x0 += dx; const s = snap1([x0], lines.xs, tol); if (s) { x0 += Math.round(s.d); gx.push(s.line); } }
    if (hd.includes('e')) { x1 += dx; const s = snap1([x1], lines.xs, tol); if (s) { x1 += Math.round(s.d); gx.push(s.line); } }
    if (hd.includes('n')) { y0 += dy; const s = snap1([y0], lines.ys, tol); if (s) { y0 += Math.round(s.d); gy.push(s.line); } }
    if (hd.includes('s')) { y1 += dy; const s = snap1([y1], lines.ys, tol); if (s) { y1 += Math.round(s.d); gy.push(s.line); } }
    if (ev.shiftKey && hd.length === 2) { const w = Math.abs(x1 - x0); const hh = Math.round(w / drag.ratio); if (hd.includes('n')) y0 = y1 - hh; else y1 = y0 + hh; }
    e.X = Math.min(x0, x1); e.Y = Math.min(y0, y1); e.W = Math.max(1, Math.abs(x1 - x0)); e.H = Math.max(1, Math.abs(y1 - y0));
  }
  showGuides(gx, gy);
  const r = bounds(e);
  $('selsize').textContent = drag.mode === 'move' ? `${r.x}, ${r.y}` : `${r.w} × ${r.h}`;
  drag.moved = true;
  draw(); updateGeometry();
});
window.addEventListener('mouseup', () => {
  if (!drag) return;
  $('selbox').classList.remove('dragging'); showGuides([], []);
  if (drag.moved) changed({ keepLayers: true }); else undoStack.pop();
  drag = null; updateUndo();
});

// ---------- selection & editing ----------
function select(i, opt = {}) {
  if (i === sel && !opt.force) return;
  sel = i;
  renderLayers(); renderInspector(); draw();
  if (i >= 0 && opt.flash) flashAt(bounds(dash.Elements[i]));
}
function uniqueName(base) { let n = 1; const names = new Set(dash.Elements.map(e => e.Name)); while (names.has(base + n)) n++; return base + n; }

function addElement(type, at) {
  if (type === 'image') { pendingImageAt = at; $('imgfile').click(); return; }
  const d = JSON.parse(JSON.stringify(DEFAULTS[type]));
  const w = type === 'deltabar' ? (d.Segments * 2 - 1) * d.Pitch + d.SegmentWidth : d.W, h = d.H;
  const x = Math.round(clamp((at ? at.x : W / 2) - w / 2, 0, W - w)), y = Math.round(clamp((at ? at.y : H / 2) - h / 2, 0, H - h));
  begin();
  const e = { Type: type, Name: uniqueName(type), X: x, Y: y, ...d };
  dash.Elements.push(e);
  sel = dash.Elements.length - 1;
  changed({ inspector: true });
  flashAt(bounds(e));
  switchTab('layers', true);
}
let pendingImageAt = null;
function addImage(file) {
  const reader = new FileReader();
  reader.onload = () => {
    const b64 = String(reader.result).split(',')[1];
    const img = new Image();
    img.onload = () => {
      begin();
      const name = file.name.replace(/\.[^.]+$/, '') + '@' + img.naturalWidth + 'x' + img.naturalHeight;
      dash.Images[name] = b64; images[name] = img;
      const k = Math.min(1, 300 / img.naturalWidth, 200 / img.naturalHeight), w = Math.round(img.naturalWidth * k), h = Math.round(img.naturalHeight * k);
      const at = pendingImageAt || { x: W / 2, y: H / 2 };
      const e = { Type: 'image', Name: uniqueName('image'), Image: name, X: Math.round(clamp(at.x - w / 2, 0, W - w)), Y: Math.round(clamp(at.y - h / 2, 0, H - h)), W: w, H: h, MaxColors: 6 };
      dash.Elements.push(e); sel = dash.Elements.length - 1;
      changed({ inspector: true }); flashAt(bounds(e));
    };
    img.src = reader.result;
  };
  reader.readAsDataURL(file);
}
function duplicate() {
  const e = cur(); if (!e) return;
  begin();
  const c = JSON.parse(JSON.stringify(e));
  c.Name = uniqueName((e.Name || e.Type).replace(/\d+$/, '')); c.X = (c.X || 0) + 12; c.Y = (c.Y || 0) + 12;
  if (c.SegmentX) c.SegmentX = c.SegmentX.map(v => v + 12);
  dash.Elements.splice(sel + 1, 0, c); sel++;
  changed({ inspector: true }); flashAt(bounds(c));
}
function removeSel() {
  if (sel < 0) return;
  begin();
  const name = nameOf(cur());
  dash.Elements.splice(sel, 1); sel = Math.min(sel, dash.Elements.length - 1);
  changed({ inspector: true });
  toast(`Deleted ${name}`, 'ok', 1800);
}
function moveTo(from, to) {
  if (from === to || from < 0 || to < 0 || to >= dash.Elements.length) return;
  begin();
  const [e] = dash.Elements.splice(from, 1); dash.Elements.splice(to, 0, e);
  sel = to; changed({ inspector: true });
}
function copySel() { const e = cur(); if (!e) return; clipboard = JSON.stringify(e); toast('Copied ' + nameOf(e), 'ok', 1400); }
function paste() {
  if (!clipboard) return;
  begin();
  const c = JSON.parse(clipboard); c.Name = uniqueName((c.Name || c.Type).replace(/\d+$/, '')); c.X = (c.X || 0) + 16; c.Y = (c.Y || 0) + 16;
  if (c.SegmentX) c.SegmentX = c.SegmentX.map(v => v + 16);
  dash.Elements.push(c); sel = dash.Elements.length - 1;
  changed({ inspector: true }); flashAt(bounds(c));
}
function align(kind) {
  const e = cur(); if (!e) return;
  const r = bounds(e); begin();
  const L = pad.l, T = pad.t, R = W, B = H;
  let dx = 0, dy = 0;
  if (kind === 'l') dx = L - r.x; if (kind === 'ch') dx = Math.round(L + (R - L - r.w) / 2) - r.x; if (kind === 'r') dx = R - r.w - r.x;
  if (kind === 't') dy = T - r.y; if (kind === 'cv') dy = Math.round(T + (B - T - r.h) / 2) - r.y; if (kind === 'b') dy = B - r.h - r.y;
  e.X = (e.X || 0) + dx; e.Y = (e.Y || 0) + dy; if (e.SegmentX) e.SegmentX = e.SegmentX.map(v => v + dx);
  changed({ keepLayers: true }); updateGeometry();
}

// ---------- keyboard ----------
window.addEventListener('keydown', ev => {
  const typing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement.tagName);
  const k = ev.key.toLowerCase();
  if (ev.ctrlKey && k === 's') { ev.preventDefault(); save(); return; }
  if (ev.key === 'Escape') { if (closeTop()) return; if (!typing && sel >= 0) select(-1); return; }
  if (typing) return;
  if (ev.ctrlKey && k === 'z') { ev.preventDefault(); ev.shiftKey ? doRedo() : doUndo(); return; }
  if (ev.ctrlKey && k === 'y') { ev.preventDefault(); doRedo(); return; }
  if (ev.ctrlKey && k === 'c') { copySel(); return; }
  if (ev.ctrlKey && k === 'v') { paste(); return; }
  if (ev.ctrlKey && k === 'd') { ev.preventDefault(); duplicate(); return; }
  if (ev.key === '?') { openModal('keysModal'); return; }
  if (ev.key === '+' || ev.key === '=') { setZoom(zoom + .1); return; }
  if (ev.key === '-') { setZoom(zoom - .1); return; }
  if (ev.key === '0') { fitZoom(); return; }
  if (ev.key === '1') { setView('edit'); return; } if (ev.key === '2') { setView('exact'); return; } if (ev.key === '3') { setView('demo'); return; }
  const e = cur(); if (!e) return;
  if (ev.key === 'Delete' || ev.key === 'Backspace') { removeSel(); return; }
  if (ev.key === ']') { moveTo(sel, sel + 1); return; } if (ev.key === '[') { moveTo(sel, sel - 1); return; }
  const step = ev.shiftKey ? 10 : 1;
  const mv = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[ev.key];
  if (mv) {
    ev.preventDefault(); begin();
    e.X = (e.X || 0) + mv[0]; e.Y = (e.Y || 0) + mv[1]; if (e.SegmentX) e.SegmentX = e.SegmentX.map(v => v + mv[0]);
    changed({ keepLayers: true }); updateGeometry();
  }
});
const KEYS = [['Move', '← ↑ → ↓'], ['Move 10 px', 'Shift + arrows'], ['Duplicate', 'Ctrl D'], ['Copy / paste', 'Ctrl C / V'], ['Delete', 'Del'], ['Undo / redo', 'Ctrl Z / Y'],
  ['Forward / back', '] / ['], ['Deselect / close', 'Esc'], ['Save', 'Ctrl S'], ['Zoom', '+ / - / 0'], ['Edit / Exact / Demo', '1 / 2 / 3'], ['These shortcuts', '?']];

// ---------- context menu ----------
function openContext(x, y) {
  const m = $('ctxMenu'), e = cur();
  m.innerHTML = e ? `<button data-c="dup">${icon('copy')}Duplicate<kbd>Ctrl D</kbd></button><button data-c="copy">${icon('copy')}Copy<kbd>Ctrl C</kbd></button>` +
    (clipboard ? `<button data-c="paste">${icon('plus')}Paste<kbd>Ctrl V</kbd></button>` : '') +
    `<hr><button data-c="front">${icon('front')}Bring to front</button><button data-c="back">${icon('back')}Send to back</button><hr>` +
    `<button data-c="del" class="danger">${icon('trash')}Delete<kbd>Del</kbd></button>`
    : `<button data-c="paste" ${clipboard ? '' : 'disabled'}>${icon('plus')}Paste<kbd>Ctrl V</kbd></button>`;
  m.style.left = Math.min(x, innerWidth - 230) + 'px'; m.style.top = Math.min(y, innerHeight - 260) + 'px';
  m.style.transformOrigin = 'top left';
  m.classList.add('open');
  m.querySelectorAll('button').forEach(b => b.onclick = () => {
    hideMenus();
    ({ dup: duplicate, copy: copySel, paste, del: removeSel, front: () => moveTo(sel, dash.Elements.length - 1), back: () => moveTo(sel, 0) })[b.dataset.c]();
  });
}
function hideMenus() { $('ctxMenu').classList.remove('open'); $('moreMenu').classList.remove('open'); closePop(); }
document.addEventListener('mousedown', ev => {
  if (!ev.target.closest('.menu') && !ev.target.closest('#btnMore')) { $('ctxMenu').classList.remove('open'); $('moreMenu').classList.remove('open'); }
  if (!ev.target.closest('.pop') && !ev.target.closest('[data-pop]')) closePop();
});

// ---------- palette & drag in ----------
function renderPalette() {
  const p = $('palette');
  p.innerHTML = Object.entries(TYPES).map(([t, d]) => `<div class="tool" draggable="true" data-type="${t}" title="${esc(d.blurb)}"><div class="ic">${icon(d.icon)}</div><b>${d.name}</b><span>${d.blurb}</span></div>`).join('');
  p.querySelectorAll('.tool').forEach(el => {
    el.onclick = () => addElement(el.dataset.type);
    el.ondragstart = ev => { ev.dataTransfer.setData('text/fxtype', el.dataset.type); ev.dataTransfer.effectAllowed = 'copy'; };
  });
}
let ghost = null;
screenEl.addEventListener('dragover', ev => {
  if (!ev.dataTransfer.types.includes('text/fxtype')) return;
  ev.preventDefault();
  const p = pos(ev);
  if (!ghost) { ghost = document.createElement('div'); ghost.className = 'dropghost'; $('overlay').appendChild(ghost); }
  place(ghost, { x: p.x - 60, y: p.y - 25, w: 120, h: 50 });
});
screenEl.addEventListener('dragleave', () => { if (ghost) { ghost.remove(); ghost = null; } });
screenEl.addEventListener('drop', ev => {
  const t = ev.dataTransfer.getData('text/fxtype'); if (!t) return;
  ev.preventDefault(); if (ghost) { ghost.remove(); ghost = null; }
  addElement(t, pos(ev));
});

// ---------- tabs ----------
function switchTab(name, quiet) {
  document.querySelectorAll('#tabs button').forEach(b => b.classList.toggle('on', b.dataset.tab === name));
  document.querySelectorAll('.pane').forEach(p => p.classList.toggle('on', p.dataset.pane === name));
  if (name === 'library' && !quiet) renderLibrary();
}

// ---------- layers ----------
let issuesByName = {};
function renderLayers() {
  const ul = $('layers'); if (!dash) return;
  $('layerCount').textContent = dash.Elements.length;
  if (!dash.Elements.length) { ul.innerHTML = `<div class="empty">${icon('layers')}Nothing yet. Add elements from the Add tab.</div>`; return; }
  // top of the list = drawn last (in front), like most design tools
  const order = dash.Elements.map((e, i) => i).reverse();
  ul.innerHTML = order.map(i => {
    const e = dash.Elements[i], iss = issuesByName[e.Name];
    const cond = isConditional(e);
    return `<li class="layer ${i === sel ? 'sel' : ''} ${shownInPreview(e) ? '' : 'off'}" draggable="true" data-i="${i}">` +
      `<span class="grip">${icon('grip')}</span><span class="ti">${icon((TYPES[e.Type] || {}).icon || 'rect')}</span>` +
      `<span class="nm"><div>${esc(nameOf(e))}</div><small>${esc((TYPES[e.Type] || { name: e.Type }).name)}${cond ? ' · conditional' : ''}</small></span>` +
      (iss ? `<span class="flag ${iss.level === 'error' ? 'err' : ''}" title="${esc(iss.text)}">${icon('warn')}</span>` : '') +
      (cond ? `<button class="eye" data-eye="${i}" title="${e.PreviewVisible === false ? 'Hidden in previews: click to show' : 'Shown in previews: click to hide'}">${icon(e.PreviewVisible === false ? 'eyeoff' : 'eye')}</button>` : '') +
      `</li>`;
  }).join('');
  ul.querySelectorAll('.layer').forEach(li => {
    const i = Number(li.dataset.i);
    li.onclick = ev => { if (ev.target.closest('.eye')) return; select(i, { flash: true }); };
    li.onmouseenter = () => { hover = i; placeOverlay(); };
    li.onmouseleave = () => { hover = -1; placeOverlay(); };
    li.ondragstart = ev => { ev.dataTransfer.setData('text/fxlayer', String(i)); li.classList.add('dragging'); };
    li.ondragend = () => li.classList.remove('dragging');
    li.ondragover = ev => {
      if (!ev.dataTransfer.types.includes('text/fxlayer')) return;
      ev.preventDefault();
      const r = li.getBoundingClientRect(), before = ev.clientY < r.top + r.height / 2;
      ul.querySelectorAll('.layer').forEach(x => x.classList.remove('drop-before', 'drop-after'));
      li.classList.add(before ? 'drop-before' : 'drop-after');
    };
    li.ondragleave = () => li.classList.remove('drop-before', 'drop-after');
    li.ondrop = ev => {
      ev.preventDefault();
      const from = Number(ev.dataTransfer.getData('text/fxlayer'));
      const before = li.classList.contains('drop-before');
      li.classList.remove('drop-before', 'drop-after');
      // the list is reversed: "before" (above) in the list = later in drawing order
      let to = before ? i + 1 : i;
      if (from < to) to--;
      moveTo(from, clamp(to, 0, dash.Elements.length - 1));
    };
  });
  ul.querySelectorAll('[data-eye]').forEach(b => b.onclick = () => {
    const e = dash.Elements[Number(b.dataset.eye)]; begin();
    if (e.PreviewVisible === false) delete e.PreviewVisible; else e.PreviewVisible = false;
    changed({ inspector: sel === Number(b.dataset.eye) });
  });
  const s = ul.querySelector('.layer.sel'); if (s) s.scrollIntoView({ block: 'nearest' });
}
function highlightLayer(i) { document.querySelectorAll('.layer').forEach(li => li.style.background = Number(li.dataset.i) === i && i !== sel ? 'var(--raised)' : ''); }

// ---------- library ----------
const thumbCache = {};
async function refreshLibrary() { try { library = await api('/api/dashes'); } catch (e) { library = []; } renderLibrary(); }
function renderLibrary() {
  const f = $('libSearch').value.toLowerCase(), box = $('library');
  const list = library.filter(d => !f || (d.name || '').toLowerCase().includes(f) || (d.id || '').toLowerCase().includes(f));
  if (!list.length) { box.innerHTML = `<div class="empty">${icon('grid')}No dashes found.</div>`; return; }
  box.innerHTML = list.map(d => `<div class="dashcard ${dash && d.id === dash.Id ? 'cur' : ''}" data-id="${esc(d.id)}"><div class="th"><img alt=""></div>` +
    `<div class="meta"><b>${esc(d.name)}</b><small>${d.builtIn ? '<span class="tag">BUILT IN</span>' : ''}${dash && d.id === dash.Id ? '<span class="tag red">OPEN</span>' : ''}${esc(d.author || d.id)}</small></div></div>`).join('');
  const io = new IntersectionObserver(entries => entries.forEach(en => { if (en.isIntersecting) { io.unobserve(en.target); thumb(en.target); } }), { root: box.closest('.pane') });
  box.querySelectorAll('.dashcard').forEach(c => { c.onclick = () => openDash(c.dataset.id); io.observe(c); });
}
function markLibrary() { document.querySelectorAll('.dashcard').forEach(c => c.classList.toggle('cur', dash && c.dataset.id === dash.Id)); }
async function thumb(card) {
  const id = card.dataset.id, img = card.querySelector('img'), th = card.querySelector('.th');
  try {
    if (!thumbCache[id]) {
      const d = await api('/api/dashes/' + encodeURIComponent(id));
      thumbCache[id] = URL.createObjectURL(await post('/api/render?mode=demo&seconds=30', d));
    }
    img.onload = () => { img.classList.add('ok'); th.classList.add('loaded'); };
    img.src = thumbCache[id];
  } catch (e) { th.classList.add('loaded'); }
}
async function openDash(id) {
  if (!(await confirmDiscard())) return;
  try { load(await api('/api/dashes/' + encodeURIComponent(id))); toast('Opened ' + (dash.Name || id)); history.replaceState(null, '', '?dash=' + encodeURIComponent(id)); }
  catch (e) { toast('Couldn\'t open: ' + e.message, 'err'); }
}

// ---------- inspector ----------
const openCards = JSON.parse(localStorage.getItem('fxdash-cards') || '{}');
function card(id, title, iconName, body, closedByDefault) {
  const closed = openCards[id] === undefined ? closedByDefault : !openCards[id];
  return `<section class="card ${closed ? 'closed' : ''}" data-card="${id}"><header>${icon(iconName)}${esc(title)}${icon('chev', 'i chev')}</header><div class="body">${body}</div></section>`;
}
const f = (label, ctrl, top) => `<div class="f ${top ? 'top' : ''}"><label>${esc(label)}</label><div>${ctrl}</div></div>`;
const numIn = (k, v, lbl) => `<div class="num"><span data-scrub="${k}" title="Drag to change">${lbl}</span><input type="number" data-k="${k}" data-num="1" value="${v ?? 0}"></div>`;
const colorIn = (k, v, optional) => `<button class="swatch" data-pop="color" data-k="${k}" ${optional ? 'data-opt="1"' : ''}><i style="--c:${v ? parseColor(v) : 'transparent'}"></i><code>${v ? esc(v) : 'none'}</code>${optional && v ? `<span class="x" data-clear="${k}" title="Remove">${icon('x')}</span>` : ''}</button>`;
const rangeIn = (k, v, min, max, unit = '') => `<div class="range"><input type="range" data-k="${k}" data-num="1" data-live="1" min="${min}" max="${max}" value="${v}"><output>${v}${unit}</output></div>`;
const switchIn = (k, v, label) => `<label class="switch"><input type="checkbox" data-k="${k}" data-kind="bool" ${v ? 'checked' : ''}><i></i><span>${esc(label)}</span></label>`;
const textIn = (k, v, ph = '') => `<input class="in" data-k="${k}" value="${esc(v ?? '')}" placeholder="${esc(ph)}" spellcheck="false">`;

function renderInspector() {
  const p = $('inspector');
  if (!dash) { p.innerHTML = ''; return; }
  const e = cur();
  if (!e) { renderDashInspector(p); return; }
  const T = TYPES[e.Type] || { name: e.Type, icon: 'rect' };
  let html = `<div class="insp-head"><div class="ti">${icon(T.icon)}</div><div style="flex:1;min-width:0"><input data-k="Name" value="${esc(e.Name || '')}" placeholder="${esc(T.name)}" spellcheck="false"><small>${esc(T.name)}</small></div></div>`;
  html += `<div class="insp-actions"><button class="btn" data-a="dup" title="Duplicate (Ctrl D)">${icon('copy')}Duplicate</button><button class="btn" data-a="front" title="Bring to front">${icon('up')}</button><button class="btn" data-a="back" title="Send to back">${icon('down')}</button><button class="btn" data-a="del" title="Delete (Del)">${icon('trash')}</button></div>`;

  // layout
  const geo = e.Type === 'deltabar' ? ['X', 'Y', 'H'] : ['X', 'Y', 'W', 'H'];
  let lay = `<div class="num4" style="grid-template-columns:repeat(${geo.length},1fr)">${geo.map(k => numIn(k, e[k], k)).join('')}</div>`;
  lay += `<div class="row2"><div class="icongroup" style="flex:1">${[['l', 'alignL', 'Left'], ['ch', 'alignCH', 'Centre'], ['r', 'alignR', 'Right']].map(([a, ic, t]) => `<button data-align="${a}" title="Align ${t} (inside the wheel padding)">${icon(ic)}</button>`).join('')}</div>` +
    `<div class="icongroup" style="flex:1">${[['t', 'alignT', 'Top'], ['cv', 'alignCV', 'Middle'], ['b', 'alignB', 'Bottom']].map(([a, ic, t]) => `<button data-align="${a}" title="Align ${t} (inside the wheel padding)">${icon(ic)}</button>`).join('')}</div></div>`;
  html += card('layout', 'Position & size', 'fit', lay);

  // text
  if (e.Type === 'label' || e.Type === 'value') {
    let t = '';
    if (e.Type === 'label') t += f('Text', textIn('Text', e.Text, 'Label text'));
    t += f('Font', fontBtn(e));
    t += f('Align', `<div class="icongroup">${[['left', 'tLeft'], ['center', 'tCenter'], ['right', 'tRight']].map(([a, ic]) => `<button data-set="Align" data-v="${a}" class="${(e.Align || 'left') === a ? 'on' : ''}" title="${a}">${icon(ic)}</button>`).join('')}</div>`);
    t += f('Colour', colorIn('Color', e.Color));
    if (e.Type === 'value') {
      t += f('If above 0', colorIn('PositiveColor', e.PositiveColor, true));
      t += f('If below 0', colorIn('NegativeColor', e.NegativeColor, true));
      t += f('Background', colorIn('Background', e.Background, true));
      t += `<div class="note">Leave the background empty: the dash works out what's under the value. Set it only to force a colour.</div>`;
    }
    html += card('text', 'Text', 'text', t);
  }

  // data
  if (['value', 'bar', 'deltabar'].includes(e.Type)) {
    let d = f('Shows', bindBtn('Bind', e.Bind));
    if (e.Type === 'value') {
      d += f('Format', `<div class="chips">${FORMATS.map(([v, l]) => `<button class="chipb ${(e.Format || '0') === v ? 'on' : ''}" data-set="Format" data-v="${esc(v)}" title="${esc(v)}">${esc(l)}</button>`).join('')}</div>`, true);
      d += f('Custom', textIn('Format', e.Format, '0.0, time:mm\\:ss...'));
      d += f('Multiply by', `<input class="in" type="number" step="any" data-k="Scale" data-num="1" value="${e.Scale ?? 1}">`);
      d += f('No data', textIn('Empty', e.Empty, '-'));
      d += f('Widest', tagsIn('Samples', e.Samples), true);
      d += `<div class="note">"Widest" are the longest texts this value can show: the checks make sure they fit the box and the font picker uses them.</div>`;
      d += f('Preview', textIn('PreviewText', e.PreviewText, '(first widest text)'));
    }
    if (e.Type === 'bar') {
      d += f('From / to', `<div class="num4" style="grid-template-columns:1fr 1fr">${numIn('Min', e.Min, '↓')}${numIn('Max', e.Max, '↑')}</div>`);
      d += f('Direction', `<div class="icongroup">${[['horizontal', 'Across'], ['vertical', 'Up']].map(([v, l]) => `<button data-set="Orientation" data-v="${v}" class="${(e.Orientation || 'horizontal') === v ? 'on' : ''}">${l}</button>`).join('')}</div>`);
      d += f('', switchIn('Reverse', e.Reverse, 'Fill from the other end'));
    }
    if (e.Type === 'deltabar') {
      d += f('Full at', `<input class="in" type="number" step="any" data-k="Range" data-num="1" value="${e.Range ?? 1}">`);
      d += `<div class="note">The value (in seconds) that fills one side.</div>`;
    }
    html += card('data', 'Data', 'data', d);
  }

  // style
  let st = '';
  switch (e.Type) {
    case 'rect': st += f('Colour', colorIn('Color', e.Color)); break;
    case 'ellipse': st += f('Rim', colorIn('Color', e.Color)) + f('Inside', colorIn('Fill', e.Fill, true)) + f('Rim width', rangeIn('Border', e.Border || 0, 0, 40, ' px')); break;
    case 'box': st += f('Border', colorIn('Color', e.Color)) + f('Inside', colorIn('Fill', e.Fill, true)) + f('Width', rangeIn('Border', e.Border || 0, 0, 30, ' px')) + f('Corners', rangeIn('Radius', e.Radius || 0, 0, 80, ' px')); break;
    case 'gradient': st += f('Colours', gradList(e), true) + f('Angle', rangeIn('Angle', e.Angle ?? 90, 0, 360, '°')) + f('Corners', rangeIn('Radius', e.Radius || 0, 0, 80, ' px')); break;
    case 'image': st += f('Picture', `<select class="in" data-k="Image">${Object.keys(dash.Images || {}).map(n => `<option ${n === e.Image ? 'selected' : ''}>${esc(n)}</option>`).join('')}</select>`) +
      f('Colours', rangeIn('MaxColors', e.MaxColors ?? 8, 2, 32)) + `<div class="note">The wheel draws pictures with rectangles: fewer colours draw faster.</div>`; break;
    case 'bar': st += f('Fill', colorIn('Color', e.Color)) + f('Empty', colorIn('Fill', e.Fill, true)); break;
    case 'deltabar': st += f('Slower', colorIn('PositiveColor', e.PositiveColor)) + f('Faster', colorIn('NegativeColor', e.NegativeColor)) + f('Off', colorIn('SegmentColor', e.SegmentColor)) +
      f('Segments', rangeIn('Segments', e.Segments || 7, 2, 15)) + f('Seg. width', rangeIn('SegmentWidth', e.SegmentWidth || 30, 2, 80, ' px')) + f('Spacing', rangeIn('Pitch', e.Pitch || 40, 4, 100, ' px')); break;
    case 'popup': st += f('Text', colorIn('Color', e.Color)) + f('Label font', fontBtn(e, 'Font')) + f('Value font', fontBtn(e, 'ValueFont')) + f('Corners', rangeIn('Radius', e.Radius || 0, 0, 60, ' px')) +
      f('Shows for', rangeIn('Duration', e.Duration ?? 2, 1, 10, ' s')) + f('Watches', `<textarea class="in" rows="4" data-k="Watch" data-kind="json">${esc(JSON.stringify(e.Watch || [], null, 1))}</textarea>`, true) +
      `<div class="note">Each watch: {"Bind": "tcLevel", "Label": "TC", "Color": "#28598B", "Format": "int"}. The pop-up shows when one of them changes.</div>`; break;
  }
  if (isShape(e.Type) || e.Type === 'bar') st += f('Opacity', rangeIn('Opacity', e.Opacity ?? 100, 0, 100, '%'));
  if (st) html += card('style', 'Style', 'sparkle', st);

  // colour from data
  if (['rect', 'ellipse', 'box', 'label', 'value', 'bar'].includes(e.Type)) {
    const cb = f('Colour from', bindBtn('ColorBind', e.ColorBind, true)) +
      f('Stops', `<textarea class="in" rows="3" data-k="ColorStops" data-kind="json" placeholder='[{"Value":0,"Color":"#00FF00"},{"Value":100,"Color":"#FF0000"}]'>${esc(e.ColorStops ? JSON.stringify(e.ColorStops) : '')}</textarea>`, true) +
      `<div class="note">A binding giving a colour ("#RRGGBB"), or a number mapped through the stops.</div>`;
    html += card('colorbind', 'Colour from data', 'wand', cb, !e.ColorBind);
  }

  // conditions
  const conds = visList(e);
  let c = `<div class="chips" id="condChips">${conds.map((b, i) => `<span class="chipb on" title="${esc(bindDesc(b))}">${esc(b)}<span class="x" data-rmcond="${i}">${icon('x')}</span></span>`).join('')}<button class="chipb" data-pop="bind" data-k="__cond">${icon('plus')}Add</button></div>`;
  c += `<div class="note">${conds.length ? 'Shown only while all of these are true (a number other than 0, or some text).' : 'Always shown. Add a condition to show it only sometimes (a warning, a pit screen).'}</div>`;
  if (conds.length) c += switchIn('__preview', e.PreviewVisible !== false, 'Show it in the designer and previews');
  html += card('cond', 'Show when', 'cond', c, !conds.length);

  // advanced
  html += card('json', 'Advanced (JSON)', 'code', `<textarea class="in" rows="10" id="rawJson" spellcheck="false">${esc(JSON.stringify(e, null, 2))}</textarea><button class="btn" id="applyJson">${icon('ok')}Apply</button>`, true);
  p.innerHTML = html;
  wireInspector(p, e);
}

function renderDashInspector(p) {
  const n = dash.Elements.length, live = dash.Elements.filter(e => ['value', 'bar', 'deltabar', 'popup'].includes(e.Type) || isConditional(e) || e.ColorBind).length;
  let html = `<div class="insp-head"><div class="ti">${icon('grid')}</div><div style="flex:1"><input data-k="Name" value="${esc(dash.Name || '')}" spellcheck="false"><small>This dash</small></div></div>`;
  html += `<div style="padding:0 12px 10px"><div class="stat"><div><b>${n}</b><small>Elements</small></div><div><b>${live}</b><small>Live</small></div><div><b id="statTime">–</b><small>Draw time</small></div></div></div>`;
  html += card('dashinfo', 'About', 'info',
    f('File name', `<input class="in" data-k="Id" value="${esc(dash.Id || '')}" spellcheck="false">`) +
    f('Author', textIn('Author', dash.Author)) +
    f('About', `<textarea class="in" rows="3" data-k="Description" style="font-family:var(--body)">${esc(dash.Description || '')}</textarea>`, true) +
    (dash.Source ? `<div class="note">From ${esc(dash.Source)}</div>` : ''));
  html += card('padding', 'Wheel padding', 'fit',
    f('From left', `<div class="range"><input type="range" id="padL" min="0" max="40" value="${pad.l}"><output>${pad.l} px</output></div>`) +
    f('From top', `<div class="range"><input type="range" id="padT" min="0" max="40" value="${pad.t}"><output>${pad.t} px</output></div>`) +
    `<div class="note">The wheel's bezel hides the screen's edges; the dashed frame shows what's left. Match the plugin's setting (Dashes tab).</div>`);
  html += `<div class="empty" style="padding:18px">${icon('sparkle')}Select an element on the screen or in Layers to change it.</div>`;
  p.innerHTML = html;
  wireInspector(p, dash);
  const upd = () => { pad = { l: Number($('padL').value), t: Number($('padT').value) }; $('padL').nextElementSibling.textContent = pad.l + ' px'; $('padT').nextElementSibling.textContent = pad.t + ' px'; localStorage.setItem('fxdash-pad', JSON.stringify(pad)); draw(); scheduleCheck(); scheduleWheel(); };
  $('padL').oninput = upd; $('padT').oninput = upd;
  if (lastCheck) $('statTime').textContent = lastCheck.cost.StaticSeconds.toFixed(1) + ' s';
}

function fontBtn(e, k = 'Font') {
  const fid = e[k] ?? 14, fh = fontHeight(fid);
  const fit = fitInfo(e, fid, k);
  return `<button class="fontbtn" data-pop="font" data-k="${k}"><span class="big">${fid}</span><span>${fh} px tall</span><span class="fit ${fit.cls}">${fit.text}</span></button>`;
}
function sampleText(e) { return e.Type === 'label' ? (e.Text || '') : (e.Samples && e.Samples.length ? e.Samples.reduce((a, b) => (String(b).length > String(a).length ? b : a)) : e.PreviewText || '888'); }
function fitInfo(e, fid, k = 'Font') {
  if (e.Type === 'popup') return { cls: 'na', text: '' };
  const fh = fontHeight(fid), w = textWidth(fid, sampleText(e));
  if (!fh) return { cls: 'bad', text: 'missing' };
  if (w < 0) return { cls: 'bad', text: 'no glyphs' };
  if (fh > e.H) return { cls: 'bad', text: 'too tall' };
  if (w > e.W) return { cls: 'bad', text: 'too wide' };
  return { cls: 'ok', text: 'fits' };
}
function bindDesc(b) { const x = bindings.find(y => y.key === b); if (x) return x.description; if (!b) return ''; if (b.startsWith('prop:')) return 'SimHub property'; if (b.startsWith('ncalc:')) return 'SimHub NCalc formula'; if (b.startsWith('js:')) return 'SimHub JavaScript formula'; return 'Not a known key'; }
function bindBtn(k, v, optional) {
  return `<button class="bindbtn" data-pop="bind" data-k="${k}"><span class="ic">${icon('data')}</span><span style="min-width:0;flex:1"><b>${v ? esc(v) : (optional ? 'Nothing (fixed colour)' : 'Pick data')}</b><small>${v ? esc(bindDesc(v)) : 'Speed, gear, lap times, fuel…'}</small></span>${optional && v ? `<span class="x" data-clear="${k}" title="Remove" style="color:var(--text3)">${icon('x')}</span>` : ''}</button>`;
}
function tagsIn(k, v) {
  return `<div class="chips">${(v || []).map((t, i) => `<span class="chipb on">${esc(t)}<span class="x" data-rmtag="${k}:${i}">${icon('x')}</span></span>`).join('')}<input class="in" data-addtag="${k}" placeholder="add…" style="width:90px;height:26px"></div>`;
}
function gradList(e) {
  const cols = e.Colors && e.Colors.length ? e.Colors : ['#1B1F3C', '#428AED'];
  return `<div style="display:grid;gap:6px">${cols.map((c, i) => `<div class="row2">${colorIn('Colors.' + i, c)}${cols.length > 2 ? `<button class="btn icon ghost" data-rmgrad="${i}" title="Remove">${icon('x')}</button>` : ''}</div>`).join('')}<button class="btn" data-addgrad="1">${icon('plus')}Colour</button></div>`;
}

function wireInspector(root, target) {
  hydrateIcons(root);
  root.querySelectorAll('.card > header').forEach(h => h.onclick = () => {
    const c = h.parentElement; c.classList.toggle('closed'); openCards[c.dataset.card] = !c.classList.contains('closed'); localStorage.setItem('fxdash-cards', JSON.stringify(openCards));
  });
  const set = (k, v) => {
    if (k.startsWith('Colors.')) { const i = Number(k.split('.')[1]); target.Colors = target.Colors && target.Colors.length ? target.Colors : ['#1B1F3C', '#428AED']; target.Colors[i] = v; return; }
    if (v === undefined || v === '') delete target[k]; else target[k] = v;
  };
  root.querySelectorAll('[data-k]').forEach(inp => {
    if (inp.dataset.pop) return;
    const k = inp.dataset.k, kind = inp.dataset.kind;
    const read = () => {
      if (kind === 'bool') return inp.checked;
      if (kind === 'json') { const t = inp.value.trim(); if (!t) return undefined; return JSON.parse(t); }
      if (inp.dataset.num) return inp.value === '' ? undefined : Number(inp.value);
      return inp.value;
    };
    if (inp.dataset.live) {
      inp.addEventListener('pointerdown', () => begin());
      inp.addEventListener('input', () => { set(k, read()); inp.nextElementSibling.textContent = inp.value + (inp.nextElementSibling.textContent.replace(/^[-\d.]+/, '')); draw(); });
      inp.addEventListener('change', () => { changed({ keepLayers: true }); });
      return;
    }
    inp.addEventListener('change', () => {
      let v; try { v = read(); } catch (err) { toast('Not valid JSON: ' + err.message, 'err'); return; }
      begin(); set(k, v);
      if (target === dash) { if (k === 'Name') renderHeader(); if (k === 'Id') renderHeader(); }
      changed({ inspector: kind === 'json' || k === 'Text' || k === 'Samples', keepLayers: k !== 'Name' && k !== 'Text' });
    });
  });
  root.querySelectorAll('[data-scrub]').forEach(sp => {
    sp.onpointerdown = ev => {
      const k = sp.dataset.scrub, inp = sp.nextElementSibling, x0 = ev.clientX, v0 = Number(inp.value) || 0;
      begin(); sp.setPointerCapture(ev.pointerId);
      sp.onpointermove = m => { const v = Math.round(v0 + (m.clientX - x0) / (m.shiftKey ? 1 : 3)); inp.value = v; target[k] = v; if (target.SegmentX && k === 'X') target.SegmentX = target.SegmentX.map(x => x + (v - (target.__lx ?? v0))); target.__lx = v; draw(); };
      sp.onpointerup = () => { sp.onpointermove = null; delete target.__lx; changed({ keepLayers: true }); };
    };
  });
  root.querySelectorAll('[data-set]').forEach(b => b.onclick = () => { begin(); set(b.dataset.set, b.dataset.v); changed({ inspector: true, keepLayers: true }); });
  root.querySelectorAll('[data-align]').forEach(b => b.onclick = () => align(b.dataset.align));
  root.querySelectorAll('[data-a]').forEach(b => b.onclick = () => ({ dup: duplicate, del: removeSel, front: () => moveTo(sel, dash.Elements.length - 1), back: () => moveTo(sel, 0) })[b.dataset.a]());
  root.querySelectorAll('[data-clear]').forEach(x => x.onclick = ev => { ev.stopPropagation(); begin(); delete target[x.dataset.clear]; changed({ inspector: true, keepLayers: true }); });
  root.querySelectorAll('[data-pop]').forEach(b => b.onclick = ev => { ev.stopPropagation(); openPop(b, b.dataset.pop, b.dataset.k, target); });
  root.querySelectorAll('[data-rmcond]').forEach(x => x.onclick = ev => { ev.stopPropagation(); begin(); const l = visList(target); l.splice(Number(x.dataset.rmcond), 1); if (l.length) target.Visible = l; else { delete target.Visible; delete target.PreviewVisible; } changed({ inspector: true }); });
  const pv = root.querySelector('[data-k="__preview"]');
  if (pv) pv.onchange = () => { begin(); if (pv.checked) delete target.PreviewVisible; else target.PreviewVisible = false; changed({}); };
  root.querySelectorAll('[data-rmtag]').forEach(x => x.onclick = () => { const [k, i] = x.dataset.rmtag.split(':'); begin(); target[k].splice(Number(i), 1); if (!target[k].length) delete target[k]; changed({ inspector: true, keepLayers: true }); });
  root.querySelectorAll('[data-addtag]').forEach(inp => inp.onkeydown = ev => {
    if (ev.key !== 'Enter' || !inp.value.trim()) return;
    const k = inp.dataset.addtag; begin(); target[k] = [...(target[k] || []), inp.value.trim()]; changed({ inspector: true, keepLayers: true });
    setTimeout(() => { const n = document.querySelector(`[data-addtag="${k}"]`); if (n) n.focus(); }, 0);
  });
  root.querySelectorAll('[data-rmgrad]').forEach(b => b.onclick = () => { begin(); target.Colors.splice(Number(b.dataset.rmgrad), 1); changed({ inspector: true, keepLayers: true }); });
  root.querySelectorAll('[data-addgrad]').forEach(b => b.onclick = () => { begin(); target.Colors = [...(target.Colors && target.Colors.length ? target.Colors : ['#1B1F3C', '#428AED']), '#FFFFFF']; changed({ inspector: true, keepLayers: true }); });
  const aj = root.querySelector('#applyJson');
  if (aj) aj.onclick = () => {
    let v; try { v = JSON.parse($('rawJson').value); } catch (err) { toast('Not valid JSON: ' + err.message, 'err'); return; }
    begin(); dash.Elements[sel] = v; changed({ inspector: true }); toast('Applied');
  };
}
function updateGeometry() {
  const e = cur(); if (!e) return;
  document.querySelectorAll('#inspector .num input').forEach(inp => { if (['X', 'Y', 'W', 'H'].includes(inp.dataset.k)) inp.value = e[inp.dataset.k] ?? 0; });
  const fb = document.querySelectorAll('#inspector .fontbtn .fit');
  fb.forEach(el => { const k = el.closest('.fontbtn').dataset.k, i = fitInfo(e, e[k] ?? 14, k); el.className = 'fit ' + i.cls; el.textContent = i.text; });
}

// ---------- popovers: colour, binding, font ----------
let popState = null;
function closePop() { $('pop').classList.remove('open'); popState = null; }
function positionPop(anchor) {
  const pop = $('pop'), r = anchor.getBoundingClientRect();
  pop.style.left = Math.max(8, Math.min(r.right - pop.offsetWidth, innerWidth - pop.offsetWidth - 8)) + 'px';
  const below = r.bottom + 6, h = pop.offsetHeight;
  pop.style.top = (below + h > innerHeight - 8 ? Math.max(8, r.top - h - 6) : below) + 'px';
}
function openPop(anchor, kind, k, target) {
  const pop = $('pop');
  if (popState && popState.anchor === anchor) { closePop(); return; }
  popState = { anchor, kind, k, target };
  if (kind === 'color') renderColorPop(pop, k, target);
  if (kind === 'bind') renderBindPop(pop, k, target);
  if (kind === 'font') renderFontPop(pop, k, target);
  pop.classList.remove('open'); void pop.offsetWidth;
  positionPop(anchor); pop.classList.add('open');
  const s = pop.querySelector('input'); if (s) setTimeout(() => s.focus(), 30);
}
function getK(target, k) { if (k.startsWith('Colors.')) return (target.Colors || [])[Number(k.split('.')[1])]; return target[k]; }
function setK(target, k, v) {
  if (k.startsWith('Colors.')) { target.Colors = target.Colors && target.Colors.length ? target.Colors : ['#1B1F3C', '#428AED']; target.Colors[Number(k.split('.')[1])] = v; return; }
  if (v === undefined) delete target[k]; else target[k] = v;
}
function renderColorPop(pop, k, target) {
  const v = getK(target, k), a = alphaOf(v);
  pop.innerHTML = `<div class="colors">${PALETTE.map(c => `<button style="background:${c}" data-c="${c}" class="${v && hex6(v) === c ? 'on' : ''}" title="${c}"></button>`).join('')}</div>` +
    `<div class="row2" style="margin-bottom:8px"><input type="color" id="cpick" value="${hex6(v)}" style="width:40px;height:32px;border:0;background:none;padding:0;cursor:pointer"><input class="in" id="chex" value="${esc(v || '')}" placeholder="#RRGGBB" style="font-family:var(--mono)"></div>` +
    `<div class="range"><span class="note" style="font-size:11.5px;color:var(--text2);min-width:52px">Opacity</span><input type="range" id="calpha" min="0" max="255" value="${a}"><output id="calphaOut">${Math.round(a / 2.55)}%</output></div>`;
  let begun = false;
  const apply = (val, live) => {
    if (!begun) { begin(); begun = true; }
    setK(target, k, val); draw();
    if (!live) { changed({ inspector: true, keepLayers: true }); closePop(); }
    else { const sw = popState && popState.anchor; if (sw) { sw.querySelector('i').style.setProperty('--c', parseColor(val)); sw.querySelector('code').textContent = val; } }
  };
  pop.querySelectorAll('[data-c]').forEach(b => b.onclick = () => apply(withAlpha(b.dataset.c, Number($('calpha').value)), false));
  $('cpick').oninput = () => { const val = withAlpha($('cpick').value, Number($('calpha').value)); $('chex').value = val; apply(val, true); };
  $('cpick').onchange = () => changed({ keepLayers: true });
  $('chex').onchange = () => { const t = $('chex').value.trim(); if (!/^#([0-9a-f]{6}|[0-9a-f]{8})$/i.test(t)) { toast('Use #RRGGBB or #AARRGGBB', 'warn'); return; } apply(t.toUpperCase(), false); };
  $('calpha').oninput = () => { $('calphaOut').textContent = Math.round($('calpha').value / 2.55) + '%'; const val = withAlpha(getK(target, k) || '#FFFFFF', Number($('calpha').value)); $('chex').value = val; apply(val, true); };
  $('calpha').onchange = () => changed({ keepLayers: true });
}
const BIND_GROUPS = [
  ['Driving', /^(speed|gear|gearText|rpm|maxRpm|rpmPercent|throttle|brake|clutch|absActive|tcActive|pitLimiter|gameRunning)$/],
  ['Fuel & energy', /(fuel|energy|Energy|soc)/i],
  ['Timing', /(lap|Lap|delta|position|predicted|session)/i],
  ['Car settings', /(abs|tc|map|bias|Map|pas)/i],
  ['Temperatures', /(Temp|temp)/],
];
function renderBindPop(pop, k, target) {
  const cond = k === '__cond';
  const curV = cond ? null : target[k];
  pop.innerHTML = `<div class="bindlist"><label class="search" style="margin-bottom:6px"><span>${icon('search')}</span><input id="bindSearch" placeholder="Search data (speed, fuel, lap…)"></label><div id="bindItems"></div>` +
    `<div class="grp">SimHub property or formula</div><div class="row2" style="padding:0 4px 4px"><input class="in" id="bindCustom" placeholder="prop:DataCorePlugin… or ncalc:[…]" value="${esc(curV && !bindings.some(b => b.key === curV) ? curV : '')}" style="font-family:var(--mono);font-size:12px"><button class="btn" id="bindUse">Use</button></div></div>`;
  let kb = 0;
  const pick = v => {
    begin();
    if (cond) { target.Visible = [...visList(target), v]; }
    else setK(target, k, v);
    closePop(); changed({ inspector: true, keepLayers: k !== 'Bind' });
  };
  const list = () => {
    const q = $('bindSearch').value.toLowerCase();
    const items = bindings.filter(b => !q || b.key.toLowerCase().includes(q) || (b.description || '').toLowerCase().includes(q));
    const groups = {};
    for (const b of items) { const g = (BIND_GROUPS.find(([, re]) => re.test(b.key)) || ['Other'])[0]; (groups[g] = groups[g] || []).push(b); }
    const order = [...BIND_GROUPS.map(g => g[0]), 'Other'].filter(g => groups[g]);
    let n = 0;
    $('bindItems').innerHTML = order.map(g => `<div class="grp">${g}</div>` + groups[g].map(b => `<button data-b="${esc(b.key)}" class="${n++ === kb ? 'kb' : ''}"><b>${esc(b.key)}</b>${b.key === curV ? ' <span class="tag red">NOW</span>' : ''}<small>${esc(b.description)}</small></button>`).join('')).join('') || '<div class="empty">No match: use a SimHub property below.</div>';
    $('bindItems').querySelectorAll('[data-b]').forEach(b => b.onclick = () => pick(b.dataset.b));
  };
  list();
  $('bindSearch').oninput = () => { kb = 0; list(); };
  $('bindSearch').onkeydown = ev => {
    const all = [...$('bindItems').querySelectorAll('[data-b]')];
    if (ev.key === 'ArrowDown') { kb = Math.min(all.length - 1, kb + 1); list(); ev.preventDefault(); }
    if (ev.key === 'ArrowUp') { kb = Math.max(0, kb - 1); list(); ev.preventDefault(); }
    if (ev.key === 'Enter' && all[kb]) pick(all[kb].dataset.b);
    const k2 = $('bindItems').querySelector('.kb'); if (k2) k2.scrollIntoView({ block: 'nearest' });
  };
  $('bindUse').onclick = () => { const v = $('bindCustom').value.trim(); if (v) pick(v); };
  $('bindCustom').onkeydown = ev => { if (ev.key === 'Enter') $('bindUse').click(); };
}
function renderFontPop(pop, k, target) {
  const curF = target[k] ?? 14, text = sampleText(target);
  const rows = fonts.map(fnt => {
    const w = textWidth(fnt.id, text), tall = fnt.height > (target.H || 0), wide = w < 0 || w > (target.W || 0);
    const pct = w < 0 ? 100 : clamp(Math.round(w / Math.max(1, target.W) * 100), 2, 100);
    return { fnt, w, bad: tall || wide, pct, why: w < 0 ? 'no glyphs for this text' : tall ? `${fnt.height} px, box ${target.H}` : wide ? `${w} px wide, box ${target.W}` : `${w} px wide` };
  });
  const best = rows.filter(r => !r.bad).sort((a, b) => b.fnt.height - a.fnt.height)[0];
  pop.innerHTML = `<div class="fontlist"><div class="note" style="font-size:11.5px;color:var(--text3);margin:0 2px 8px">The screen's fonts, measured with "${esc(text)}" in a ${target.W} × ${target.H} box</div>` +
    (best ? `<button class="btn primary" data-f="${best.fnt.id}" style="width:100%;justify-content:center;margin-bottom:8px">${icon('wand')}Use the biggest that fits: font ${best.fnt.id} (${best.fnt.height} px)</button>` : `<div class="note" style="color:#ff7580;margin:0 2px 8px">Nothing fits: make the box bigger or the text shorter.</div>`) +
    '<div class="fontrows" style="max-height:340px;overflow-y:auto;margin:0 -4px;padding:0 4px">' + rows.map(r => `<button data-f="${r.fnt.id}" class="${r.fnt.id === curF ? 'cur' : ''}" title="${esc(r.why)}"><span class="big" style="font:700 13px var(--display);min-width:26px">${r.fnt.id}</span><span style="min-width:52px;color:var(--text2);font-size:12px">${r.fnt.height} px</span><span class="bar"><i class="${r.bad ? 'bad' : ''}" style="width:${r.pct}%"></i></span><span class="fit ${r.bad ? 'bad' : 'ok'}" style="font-size:10.5px;padding:1px 6px;border-radius:4px">${r.bad ? 'no' : 'fits'}</span></button>`).join('') + '</div></div>';
  pop.querySelectorAll('[data-f]').forEach(b => b.onclick = () => { begin(); target[k] = Number(b.dataset.f); closePop(); changed({ inspector: true, keepLayers: true }); });
  setTimeout(() => { const c = pop.querySelector('.fontrows button.cur'), box = pop.querySelector('.fontrows'); if (c && box) box.scrollTop = c.offsetTop - box.offsetTop - box.clientHeight / 2 + c.offsetHeight / 2; }, 0);
}

// ---------- checks ----------
let checkTimer = null, lastCheck = null;
function scheduleCheck(ms = 450) { clearTimeout(checkTimer); $('checksPill').classList.add('busy'); checkTimer = setTimeout(runCheck, ms); }
async function runCheck() {
  if (!dash) return;
  try {
    const r = await post(`/api/check?left=${pad.l}&top=${pad.t}`, dash);
    lastCheck = r;
    const pill = $('checksPill');
    pill.classList.remove('busy', 'ok', 'warn', 'err');
    pill.classList.add(r.errors ? 'err' : r.warnings ? 'warn' : 'ok');
    $('checksText').textContent = r.errors ? `${r.errors} problem${r.errors > 1 ? 's' : ''}` : r.warnings ? `${r.warnings} warning${r.warnings > 1 ? 's' : ''}` : 'Ready for the wheel';
    const c = r.cost;
    $('costText').textContent = `Draws in ~${c.StaticSeconds.toFixed(1)} s · ${c.DynamicElements} live element${c.DynamicElements === 1 ? '' : 's'}` + (c.RedrawnValues.length ? ` · ${c.RedrawnValues.length} on busy backgrounds` : '');
    $('costMeter').style.width = clamp(c.StaticSeconds / 15 * 100, 3, 100) + '%';
    if ($('statTime')) $('statTime').textContent = c.StaticSeconds.toFixed(1) + ' s';
    issuesByName = {};
    for (const i of r.issues) if (i.Element) { const o = issuesByName[i.Element]; issuesByName[i.Element] = { level: o && o.level === 'error' ? 'error' : i.Level, text: (o ? o.text + '\n' : '') + i.Message }; }
    $('issues').innerHTML = r.issues.length ? r.issues.map((i, n) => `<div class="issue ${i.Level}" data-n="${n}"><span class="lv">${icon(i.Level === 'error' ? 'err' : 'warn')}</span><span>${i.Element ? `<b>${esc(i.Element)}</b>` : ''}${esc(i.Message)}</span></div>`).join('')
      : `<div class="empty" style="padding:18px">${icon('ok')}Nothing to fix: text fits, nothing's off the screen, fonts exist.</div>`;
    $('issues').querySelectorAll('.issue').forEach(el => {
      const i = r.issues[Number(el.dataset.n)];
      const idx = () => dash.Elements.findIndex(e => e.Name === i.Element || nameOf(e) === i.Element);
      el.onclick = () => { const x = idx(); if (x >= 0) { select(x, { flash: true, force: true }); switchTab('layers', true); } };
      el.onmouseenter = () => { hover = idx(); placeOverlay(); };
      el.onmouseleave = () => { hover = -1; placeOverlay(); };
    });
    renderLayers();
  } catch (e) { $('checksPill').className = 'pill err'; $('checksText').textContent = 'Check failed'; }
}

// ---------- views: edit / exact / demo ----------
let demoT = 2, demoTimer = null, exactBusy = false;
function setView(v) {
  view = v;
  const seg = $('viewSeg'), b = seg.querySelector(`[data-view="${v}"]`), th = seg.querySelector('.thumb');
  seg.querySelectorAll('button').forEach(x => x.classList.toggle('on', x === b));
  th.style.left = b.offsetLeft + 'px'; th.style.width = b.offsetWidth + 'px';
  $('exact').classList.toggle('on', v !== 'edit');
  $('viewBadge').classList.toggle('on', v !== 'edit');
  $('viewBadgeText').textContent = v === 'demo' ? 'Demo lap, drawn exactly as the wheel does' : 'Exact picture, drawn exactly as the wheel does';
  clearInterval(demoTimer);
  if (v === 'demo') demoTimer = setInterval(() => { demoT = demoT >= 90 ? 2 : demoT + .5; refreshExact(); }, 450);
  if (v !== 'edit') refreshExact();
  placeOverlay();
}
async function refreshExact() {
  if (view === 'edit' || exactBusy) return;
  exactBusy = true;
  try {
    const blob = await post(`/api/render?mode=${view === 'demo' ? 'demo' : 'preview'}&seconds=${demoT}&left=0&top=0`, dash);
    const img = $('exact'), old = img.src; img.src = URL.createObjectURL(blob); if (old) setTimeout(() => URL.revokeObjectURL(old), 1000);
  } catch (e) { toast('Preview failed: ' + e.message, 'err'); setView('edit'); }
  finally { exactBusy = false; }
}

// ---------- wheel ----------
let wheelOn = false, wheelTimer = null;
function scheduleWheel() { if (!wheelOn) return; clearTimeout(wheelTimer); wheelTimer = setTimeout(pushWheel, 600); }
async function pushWheel() { try { await post(`/api/wheel/show?left=${pad.l}&top=${pad.t}`, dash); } catch (e) { toast('Wheel: ' + e.message, 'err'); setWheel(false); } }
async function setWheel(on) {
  wheelOn = on; $('btnWheel').classList.toggle('on', on); $('bezel').classList.toggle('wheel', on);
  if (on) { await pushWheel(); toast('On your wheel: it follows every change'); } else { try { await post('/api/wheel/stop', {}); } catch (e) { } }
}
setInterval(() => { if (wheelOn) pushWheel(); }, 20000);

// ---------- dialogs ----------
function openModal(id) { $(id).classList.add('open'); }
function closeModal(id) { $(id).classList.remove('open'); }
function closeTop() {
  if (popState) { closePop(); return true; }
  const open = [...document.querySelectorAll('.modal-bg.open')].pop();
  if (open) { closeModal(open.id); return true; }
  if ($('drawer').classList.contains('open')) { $('drawer').classList.remove('open'); return true; }
  if ($('moreMenu').classList.contains('open') || $('ctxMenu').classList.contains('open')) { hideMenus(); return true; }
  return false;
}
document.querySelectorAll('.modal-bg').forEach(m => {
  m.addEventListener('mousedown', ev => { if (ev.target === m) closeModal(m.id); });
  m.querySelectorAll('[data-close]').forEach(b => b.onclick = () => closeModal(m.id));
});
function ask({ title, text, icon: ic = 'save', fields = [], ok = 'OK', danger = false }) {
  return new Promise(resolve => {
    $('promptTitle').textContent = title; $('promptText').textContent = text || '';
    $('promptIcon').innerHTML = icon(ic);
    $('promptBody').innerHTML = fields.map((fl, i) => f(fl.label, `<input class="in" id="pf${i}" value="${esc(fl.value || '')}" spellcheck="false">`)).join('');
    $('promptBody').style.display = fields.length ? '' : 'none';
    const okb = $('promptOk'); okb.textContent = ok; okb.className = 'btn primary'; if (danger) okb.style.background = 'linear-gradient(180deg,#ff2b38,#a00)';
    openModal('promptModal');
    setTimeout(() => { const i = $('pf0'); if (i) { i.focus(); i.select(); } else okb.focus(); }, 60);
    const done = v => { closeModal('promptModal'); okb.onclick = null; resolve(v); };
    okb.onclick = () => done(fields.map((_, i) => $('pf' + i).value.trim()));
    $('promptBody').onkeydown = ev => { if (ev.key === 'Enter') okb.click(); };
    $('promptModal').querySelector('[data-close]').onclick = () => done(null);
  });
}
async function confirmDiscard() {
  if (!dirty) return true;
  return !!(await ask({ title: 'Discard your changes?', text: 'This dash has changes that aren\'t saved.', icon: 'warn', ok: 'Discard', danger: true }));
}

// ---------- save / open ----------
async function save() {
  if (!dash) return;
  const builtIn = library.some(d => d.id === dash.Id && d.builtIn);
  if (builtIn || !dash.Id) return saveAs(builtIn);
  try {
    const btn = $('btnSave'); btn.disabled = true;
    const r = await api('/api/dashes/' + encodeURIComponent(dash.Id), { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(dash) });
    setDirty(false); toast('Saved: the plugin shows it in its dash list'); delete thumbCache[dash.Id];
    refreshLibrary();
    btn.disabled = false; return r;
  } catch (e) { $('btnSave').disabled = false; toast('Save failed: ' + e.message, 'err', 4500); }
}
async function saveAs(builtIn) {
  const base = (dash.Id || 'my-dash').replace(/-copy$/, '');
  const r = await ask({ title: builtIn ? 'Save a copy' : 'Save as a new dash', text: builtIn ? 'Built-in dashes can\'t be changed: save your version as a new dash.' : 'A new file in the plugin\'s dashes folder.', icon: 'copy',
    fields: [{ label: 'Name', value: (dash.Name || 'My dash') + (builtIn ? ' (mine)' : '') }, { label: 'File name', value: base + '-copy' }], ok: 'Save' });
  if (!r) return;
  const [name, id] = r;
  if (!id) { toast('Give it a file name', 'warn'); return; }
  dash.Id = id.replace(/[^\w.-]+/g, '-'); if (name) dash.Name = name;
  renderHeader(); renderInspector(); await save();
}
function download() {
  const blob = new Blob([JSON.stringify(dash, null, 2)], { type: 'application/json' });
  const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = (dash.Id || 'dash') + '.json'; a.click();
}
async function deleteDash() {
  const d = library.find(x => x.id === dash.Id);
  if (!d || d.builtIn) { toast(d ? 'Built-in dashes can\'t be deleted' : 'This dash isn\'t saved in the library', 'warn'); return; }
  if (!(await ask({ title: `Delete "${dash.Name}"?`, text: 'Its file is removed from the plugin\'s dashes folder. This can\'t be undone.', icon: 'trash', ok: 'Delete', danger: true }))) return;
  try { await api('/api/dashes/' + encodeURIComponent(dash.Id), { method: 'DELETE' }); toast('Deleted'); load(blankDash()); refreshLibrary(); }
  catch (e) { toast('Delete failed: ' + e.message, 'err'); }
}

// ---------- SimHub import ----------
let simhub = [], simSel = null;
async function openImport() {
  $('impResult').style.display = 'none';
  openModal('importModal');
  if (!simhub.length) { $('simGrid').innerHTML = '<div class="progress" style="grid-column:1/-1"><i></i></div>'; try { simhub = await api('/api/simhub'); } catch (e) { simhub = []; } }
  renderSimhub(); setTimeout(() => $('simFilter').focus(), 80);
}
function renderSimhub() {
  const q = $('simFilter').value.toLowerCase();
  const list = simhub.filter(d => d.name.toLowerCase().includes(q));
  $('simGrid').innerHTML = list.map(d => `<button data-n="${esc(d.name)}" class="${d.name === simSel ? 'on' : ''}">${esc(d.name)}</button>`).join('') || `<div class="empty" style="grid-column:1/-1">No SimHub dashes found.</div>`;
  $('simGrid').querySelectorAll('[data-n]').forEach(b => b.onclick = async () => {
    simSel = b.dataset.n; $('simGrid').querySelectorAll('button').forEach(x => x.classList.toggle('on', x === b));
    try { const screens = await api('/api/simhub/screens?name=' + encodeURIComponent(simSel)); $('simScreen').innerHTML = '<option value="">The main in-game screen</option>' + screens.map((s, i) => `<option value="${i}">${esc(s)}</option>`).join(''); } catch (e) { }
  });
}
async function runImport() {
  if (!simSel) { toast('Pick a dash first', 'warn'); return; }
  if (!(await confirmDiscard())) return;
  const go = $('impGo'); go.disabled = true; $('impProgress').style.display = ''; $('impResult').style.display = 'none';
  try {
    const r = await post('/api/import', { name: simSel, screen: $('simScreen').value || null, images: $('impImages').checked, colors: Number($('impColors').value), maxSeconds: Number($('impSeconds').value), fitWidth: W - pad.l, fitHeight: H - pad.t });
    load(r.dash, { dirty: true });
    const rep = r.report, ok = r.check.errors === 0;
    $('impResult').innerHTML = `<div class="report"><div><b>${rep.Converted}</b><small>converted</small></div><div><b>${rep.Approximated}</b><small>approximated</small></div><div><b>${rep.Skipped}</b><small>skipped</small></div><div><b style="color:${ok ? 'var(--green)' : 'var(--red)'}">${r.check.errors}</b><small>problems</small></div></div>` +
      `<div class="notes">${esc(rep.Dash)} / ${esc(rep.Screen)} · draws in ~${r.check.cost.StaticSeconds.toFixed(1)} s · ${rep.NcalcFormulas + rep.JsFormulas} SimHub formulas kept` +
      (Object.keys(rep.SkippedTypes).length ? `<br>Not converted: ${esc(Object.entries(rep.SkippedTypes).map(([k, v]) => k + ' ×' + v).join(', '))}` : '') +
      (rep.Notes.length ? '<br>' + rep.Notes.map(n => '· ' + esc(n)).join('<br>') : '') + `</div><div class="note">Fine-tune it on the screen, then Save.</div>`;
    $('impResult').style.display = 'grid';
    toast('Imported ' + rep.Dash);
  } catch (e) { toast('Import failed: ' + e.message, 'err', 5000); }
  finally { go.disabled = false; $('impProgress').style.display = 'none'; }
}

// ---------- wiring ----------
function doAct(a) {
  hideMenus();
  ({
    saveas: () => saveAs(false), download, open: () => $('file').click(), keys: () => openModal('keysModal'), delete: deleteDash,
    new: async () => { if (await confirmDiscard()) { load(blankDash()); history.replaceState(null, '', location.pathname); } }, import: openImport,
  })[a]();
}
function wire() {
  hydrateIcons(document);
  renderPalette();
  $('keysList').innerHTML = KEYS.map(([a, b]) => `<div><span>${a}</span><kbd>${b}</kbd></div>`).join('');
  document.querySelectorAll('#tabs button').forEach(b => b.onclick = () => switchTab(b.dataset.tab));
  document.querySelectorAll('[data-act]').forEach(b => b.onclick = () => doAct(b.dataset.act));
  $('btnMore').onclick = ev => { ev.stopPropagation(); $('moreMenu').classList.toggle('open'); };
  $('btnSave').onclick = save;
  $('btnUndo').onclick = doUndo; $('btnRedo').onclick = doRedo;
  $('viewSeg').querySelectorAll('button').forEach(b => b.onclick = () => setView(b.dataset.view));
  $('btnWheel').onclick = () => setWheel(!wheelOn);
  $('checksPill').onclick = () => $('drawer').classList.toggle('open');
  $('drawerClose').onclick = () => $('drawer').classList.remove('open');
  $('zoomIn').onclick = () => setZoom(zoom + .1); $('zoomOut').onclick = () => setZoom(zoom - .1); $('zoomFit').onclick = fitZoom;
  $('stage').addEventListener('wheel', ev => { if (!ev.ctrlKey) return; ev.preventDefault(); setZoom(zoom * (ev.deltaY < 0 ? 1.1 : 1 / 1.1)); }, { passive: false });
  $('stageScroll').addEventListener('mousedown', ev => { if (ev.target === $('stageScroll') || ev.target === $('bezel')) select(-1); });
  $('dashName').onchange = () => { begin(); dash.Name = $('dashName').value; changed({ keepLayers: true, inspector: sel < 0 }); };
  $('libSearch').oninput = renderLibrary;
  $('file').onchange = async ev => { const fl = ev.target.files[0]; ev.target.value = ''; if (!fl || !(await confirmDiscard())) return; try { load(JSON.parse(await fl.text()), { dirty: true }); toast('Opened ' + fl.name); } catch (err) { toast('Not a dash file: ' + err.message, 'err'); } };
  $('imgfile').onchange = ev => { const fl = ev.target.files[0]; ev.target.value = ''; if (fl) addImage(fl); };
  $('simFilter').oninput = renderSimhub; $('impGo').onclick = runImport;
  $('impColors').oninput = () => { $('impColorsOut').textContent = $('impColors').value; $('impColorsVal').textContent = $('impColors').value; };
  $('impSeconds').oninput = () => { $('impSecondsOut').textContent = $('impSeconds').value + ' s'; };
  window.addEventListener('resize', () => { if (!localStorage.getItem('fxdash-zoom')) fitZoom(); setView(view); });
  window.addEventListener('beforeunload', ev => { if (wheelOn) navigator.sendBeacon('/api/wheel/stop'); if (dirty) { ev.preventDefault(); ev.returnValue = ''; } });
  setInterval(() => { if (view === 'exact') refreshExact(); }, 1500);
}

async function start() {
  wire();
  try { pad = { ...pad, ...JSON.parse(localStorage.getItem('fxdash-pad') || '{}') }; } catch (e) { }
  [metrics, fonts, bindings] = await Promise.all([api('/api/fonts/metrics'), api('/api/fonts'), api('/api/bindings')]);
  fonts = fonts.filter(x => x.height > 0).sort((a, b) => a.height - b.height || a.id - b.id);
  try { hostInfo = await api('/api/wheel'); } catch (e) { hostInfo = null; }
  const wheelOk = !!hostInfo && hostInfo.available !== false;
  $('btnWheel').disabled = !wheelOk;
  $('btnWheel').title = wheelOk ? 'Show this dash on your FX Pro while you edit' : hostInfo ? 'The wheel isn\'t available: ' + (hostInfo.why || '') : 'Offline designer: no wheel here (run it from SimHub)';
  await refreshLibrary();
  let draft = null;
  try { draft = JSON.parse(localStorage.getItem('fxdash-draft') || 'null'); } catch (e) { }
  const q = new URLSearchParams(location.search).get('dash');
  if (q) { try { load(await api('/api/dashes/' + encodeURIComponent(q))); } catch (e) { load(blankDash()); toast('No dash ' + q, 'warn'); } }
  else if (draft && draft.Elements) { load(draft, { dirty: true }); toast('Restored your last unsaved work'); }
  else load(blankDash());
  const z = Number(localStorage.getItem('fxdash-zoom'));
  if (z) setZoom(z, { silent: true }); else fitZoom();
  setView('edit');
  if (dash.Elements.length) switchTab('layers', true);
}
start().catch(e => { document.body.innerHTML = `<div style="padding:40px;font:14px Segoe UI;color:#eee;background:#0a0b0d;height:100%">The designer couldn't start: ${esc(e.message)}</div>`; });

// For agents driving the page: the current dash and a way to replace it.
window.fxdash = { get dash() { return dash; }, get dirty() { return dirty; }, get undo() { return undoStack.length; }, load: d => load(d, { dirty: true }), check: runCheck, select: i => select(i, { flash: true }) };
