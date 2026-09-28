// FX Pro Dash Designer (served by the FXPro RPM Sync plugin or `fxdash serve`). Edits the dash JSON format
// (docs/dash-format.md); layout checks, exact previews, SimHub import and the wheel go through the local API (GET /api).
'use strict';

const W = 800, H = 480;
const $ = id => document.getElementById(id);
const api = async (path, opt = {}) => {
  const r = await fetch(path, opt);
  const type = r.headers.get('content-type') || '';
  if (type.startsWith('image/')) return r.blob();
  const j = await r.json();
  if (!r.ok || (j && j.error)) throw new Error(j && j.error || r.statusText);
  return j;
};
const post = (path, body) => api(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: typeof body === 'string' ? body : JSON.stringify(body) });

let dash = null, sel = -1, dirty = false;
let metrics = null, fonts = [], bindings = [], hostInfo = null;
const undo = [], redo = [];
const images = {}; // name -> HTMLImageElement
let zoom = 1;

// ---------- helpers ----------

function toast(msg, ms = 2200) { const t = $('toast'); t.textContent = msg; t.classList.add('show'); clearTimeout(t._h); t._h = setTimeout(() => t.classList.remove('show'), ms); }

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
const hex6 = s => { if (!s || s[0] !== '#') return '#ffffff'; let h = s.slice(1); if (h.length === 8) h = h.slice(2); if (h.length === 3) h = h.split('').map(c => c + c).join(''); return '#' + h.slice(0, 6); };
const alphaOf = s => (s && s[0] === '#' && s.length === 9) ? s.slice(1, 3) : '';

function fontHeight(f) { return metrics && metrics.heights[f] || 0; }
function charWidth(f, ch) { const c = ch.charCodeAt(0); if (!metrics || c < 32 || c > 126) return -1; return metrics.widths[f][c - 32]; }
function textWidth(f, t) { let w = 0; for (const ch of t) { const g = charWidth(f, ch); if (g < 0) return -1; w += g; } return w; }

const isShape = t => ['rect', 'ellipse', 'box', 'gradient', 'image'].includes(t);
const isConditional = e => (e.Visible && (Array.isArray(e.Visible) ? e.Visible.length : 1));
const shownInPreview = e => !isConditional(e) || e.PreviewVisible !== false;
const visList = e => !e.Visible ? [] : Array.isArray(e.Visible) ? e.Visible : [e.Visible];
const nameOf = e => e.Name || e.Text || e.Bind || e.Type;

// ---------- history ----------

function snapshot() { return JSON.stringify(dash); }
function begin() { undo.push(snapshot()); if (undo.length > 200) undo.shift(); redo.length = 0; }
function changed(opt = {}) { dirty = true; if (!opt.noList) renderList(); draw(); if (!opt.noProps) renderProps(); scheduleCheck(); scheduleWheel(); saveDraft(); }
function doUndo() { if (!undo.length) return; redo.push(snapshot()); load(JSON.parse(undo.pop()), true); }
function doRedo() { if (!redo.length) return; undo.push(snapshot()); load(JSON.parse(redo.pop()), true); }

function saveDraft() { try { localStorage.setItem('fxdash-draft', snapshot()); } catch (e) { } }

// ---------- loading ----------

function load(d, keepHistory = false) {
  dash = d;
  dash.Elements = dash.Elements || [];
  dash.Images = dash.Images || {};
  if (!keepHistory) { undo.length = 0; redo.length = 0; dirty = false; }
  sel = Math.min(sel, dash.Elements.length - 1);
  for (const k of Object.keys(images)) delete images[k];
  for (const [k, v] of Object.entries(dash.Images)) { const img = new Image(); img.onload = draw; img.src = 'data:image/png;base64,' + v; images[k] = img; }
  renderList(); renderProps(); draw(); scheduleCheck(0); scheduleWheel();
}

function blankDash() {
  return { FormatVersion: 2, Id: 'my-dash', Name: 'My dash', Author: '', Description: '', Elements: [], Images: {} };
}

async function refreshLibrary(selectId) {
  const list = await api('/api/dashes');
  const s = $('library');
  s.innerHTML = '<option value="">Open from library…</option>' + list.map(d => `<option value="${esc(d.id)}">${esc(d.name)}${d.builtIn ? ' (built-in)' : ''}</option>`).join('');
  if (selectId) s.value = selectId;
}
const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

// ---------- drawing ----------

const stage = $('stage'), ctx = stage.getContext('2d');

function draw() {
  if (!dash) return;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.fillStyle = '#000'; ctx.fillRect(0, 0, W, H);
  dash.Elements.forEach((e, i) => { if (e.Type !== 'popup' && shownInPreview(e)) { ctx.save(); drawElement(e); ctx.restore(); } });
  const s = dash.Elements[sel];
  if (s) drawSelection(s);
}

function withOpacity(e) { ctx.globalAlpha = (e.Opacity ?? 100) / 100; }

function roundRect(x, y, w, h, r) {
  r = Math.max(0, Math.min(r || 0, w / 2, h / 2));
  ctx.beginPath(); ctx.moveTo(x + r, y); ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r); ctx.closePath();
}

function drawElement(e) {
  const { X: x = 0, Y: y = 0, W: w = 0, H: h = 0 } = e;
  switch (e.Type) {
    case 'rect': withOpacity(e); ctx.fillStyle = parseColor(e.Color); ctx.fillRect(x, y, w, h); break;
    case 'ellipse': {
      withOpacity(e);
      const b = e.Border || 0;
      if (b <= 0) { ctx.beginPath(); ctx.ellipse(x + w / 2, y + h / 2, w / 2, h / 2, 0, 0, Math.PI * 2); ctx.fillStyle = parseColor(e.Color); ctx.fill(); break; }
      if (e.Fill) { ctx.beginPath(); ctx.ellipse(x + w / 2, y + h / 2, Math.max(0, w / 2 - b), Math.max(0, h / 2 - b), 0, 0, Math.PI * 2); ctx.fillStyle = parseColor(e.Fill); ctx.fill(); }
      ctx.beginPath(); ctx.ellipse(x + w / 2, y + h / 2, Math.max(0, w / 2 - b / 2), Math.max(0, h / 2 - b / 2), 0, 0, Math.PI * 2);
      ctx.lineWidth = b; ctx.strokeStyle = parseColor(e.Color); ctx.stroke();
      break;
    }
    case 'box': {
      withOpacity(e);
      const b = e.Border || 0;
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
      withOpacity(e);
      const img = images[e.Image];
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
      withOpacity(e);
      const vert = e.Orientation === 'vertical', f = 0.6;
      if (e.Fill) { ctx.fillStyle = parseColor(e.Fill); ctx.fillRect(x, y, w, h); }
      ctx.fillStyle = parseColor(e.Color || '#00ff00');
      if (!vert) ctx.fillRect(e.Reverse ? x + w * (1 - f) : x, y, w * f, h); else ctx.fillRect(x, e.Reverse ? y : y + h * (1 - f), w, h * f);
      break;
    }
    case 'deltabar': {
      const n = e.Segments || 7, xs = (e.SegmentX && e.SegmentX.length === n * 2) ? e.SegmentX : Array.from({ length: n * 2 }, (_, k) => x + Math.round(k * (e.Pitch || 40)));
      xs.forEach((sx, k) => {
        ctx.fillStyle = parseColor(k === n - 1 || k === n - 2 ? e.PositiveColor || '#ff0000' : e.SegmentColor || '#808080');
        ctx.fillRect(sx, y, e.SegmentWidth || 30, h);
      });
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
  ctx.font = `${fh >= 90 ? 'bold ' : ''}${Math.max(6, fh * 0.78)}px "Segoe UI", sans-serif`;
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  for (const [ch, cw] of cells) { ctx.fillText(ch, x + cw / 2, y + fh / 2); x += cw; }
  ctx.restore();
}

function drawSelection(e) {
  const r = bounds(e);
  ctx.save();
  ctx.strokeStyle = '#3d8bfd'; ctx.lineWidth = 1; ctx.setLineDash([4, 3]);
  ctx.strokeRect(r.x + .5, r.y + .5, Math.max(1, r.w - 1), Math.max(1, r.h - 1));
  ctx.setLineDash([]); ctx.fillStyle = '#3d8bfd';
  for (const [hx, hy] of handles(r)) ctx.fillRect(hx - 4, hy - 4, 8, 8);
  if (e.Type === 'popup') { ctx.fillStyle = 'rgba(61,139,253,.12)'; ctx.fillRect(r.x, r.y, r.w, r.h); }
  ctx.restore();
}

function bounds(e) {
  if (e.Type === 'deltabar') {
    const n = e.Segments || 7, xs = (e.SegmentX && e.SegmentX.length === n * 2) ? e.SegmentX : Array.from({ length: n * 2 }, (_, k) => (e.X || 0) + Math.round(k * (e.Pitch || 40)));
    const x0 = Math.min(...xs), x1 = Math.max(...xs) + (e.SegmentWidth || 30);
    return { x: x0, y: e.Y || 0, w: x1 - x0, h: e.H || 0 };
  }
  return { x: e.X || 0, y: e.Y || 0, w: e.W || 0, h: e.H || 0 };
}
const handles = r => [[r.x, r.y], [r.x + r.w, r.y], [r.x, r.y + r.h], [r.x + r.w, r.y + r.h]];

// ---------- mouse ----------

let drag = null;
function pos(ev) { const b = stage.getBoundingClientRect(); return { x: (ev.clientX - b.left) / zoom, y: (ev.clientY - b.top) / zoom }; }

stage.addEventListener('mousedown', ev => {
  if (!dash) return;
  const p = pos(ev);
  const s = dash.Elements[sel];
  if (s) {
    const hs = handles(bounds(s));
    const hi = hs.findIndex(([hx, hy]) => Math.abs(hx - p.x) <= 6 && Math.abs(hy - p.y) <= 6);
    if (hi >= 0 && s.Type !== 'deltabar') { begin(); drag = { mode: 'resize', corner: hi, start: p, orig: { ...s } }; return; }
  }
  let hit = -1;
  for (let i = dash.Elements.length - 1; i >= 0; i--) {
    const e = dash.Elements[i];
    if (e.Type !== 'popup' && !shownInPreview(e) && i !== sel) continue;
    const r = bounds(e);
    if (p.x >= r.x && p.x <= r.x + r.w && p.y >= r.y && p.y <= r.y + r.h) { hit = i; break; }
  }
  select(hit);
  if (hit >= 0) { begin(); const e = dash.Elements[hit]; drag = { mode: 'move', start: p, orig: { X: e.X, Y: e.Y, SegmentX: e.SegmentX ? [...e.SegmentX] : null } }; }
});

window.addEventListener('mousemove', ev => {
  if (!drag || !dash) return;
  const p = pos(ev), e = dash.Elements[sel];
  const dx = Math.round(p.x - drag.start.x), dy = Math.round(p.y - drag.start.y);
  if (drag.mode === 'move') {
    e.X = drag.orig.X + dx; e.Y = drag.orig.Y + dy;
    if (drag.orig.SegmentX) e.SegmentX = drag.orig.SegmentX.map(v => v + dx);
  } else {
    const o = drag.orig; let x0 = o.X, y0 = o.Y, x1 = o.X + o.W, y1 = o.Y + o.H;
    if (drag.corner === 0 || drag.corner === 2) x0 += dx; else x1 += dx;
    if (drag.corner === 0 || drag.corner === 1) y0 += dy; else y1 += dy;
    e.X = Math.min(x0, x1); e.Y = Math.min(y0, y1); e.W = Math.max(1, Math.abs(x1 - x0)); e.H = Math.max(1, Math.abs(y1 - y0));
  }
  drag.moved = true;
  draw(); updateGeometryFields();
});
window.addEventListener('mouseup', () => {
  if (!drag) return;
  if (drag.moved) changed({ noProps: true }); else undo.pop();
  drag = null;
});

// ---------- keyboard ----------

window.addEventListener('keydown', ev => {
  const typing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement.tagName);
  if (ev.ctrlKey && ev.key.toLowerCase() === 's') { ev.preventDefault(); save(); return; }
  if (typing) return;
  if (ev.ctrlKey && ev.key.toLowerCase() === 'z') { ev.preventDefault(); doUndo(); return; }
  if (ev.ctrlKey && ev.key.toLowerCase() === 'y') { ev.preventDefault(); doRedo(); return; }
  const e = dash && dash.Elements[sel];
  if (!e) return;
  if (ev.ctrlKey && ev.key.toLowerCase() === 'd') { ev.preventDefault(); duplicate(); return; }
  if (ev.key === 'Delete') { remove(); return; }
  const step = ev.shiftKey ? 10 : 1;
  const moves = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
  if (moves[ev.key]) {
    ev.preventDefault(); begin();
    const [dx, dy] = moves[ev.key];
    e.X += dx; e.Y += dy; if (e.SegmentX) e.SegmentX = e.SegmentX.map(v => v + dx);
    changed({ noProps: true }); updateGeometryFields();
  }
});

// ---------- element list ----------

function select(i) { sel = i; renderList(); renderProps(); draw(); }

function renderList() {
  const ul = $('elements');
  ul.innerHTML = '';
  (dash ? dash.Elements : []).forEach((e, i) => {
    const li = document.createElement('li');
    if (i === sel) li.classList.add('sel');
    if (!shownInPreview(e)) li.classList.add('hidden');
    li.innerHTML = `<span class="t">${esc(e.Type)}</span><span class="n" title="${esc(nameOf(e))}">${esc(nameOf(e))}</span>` +
      (isConditional(e) ? `<span class="eye" title="Has conditions: ${esc(visList(e).join(' AND '))}">${e.PreviewVisible === false ? '◌' : '◉'}</span>` : '') +
      (issuesByName[e.Name] ? `<span class="flag" title="${esc(issuesByName[e.Name])}">⚠</span>` : '');
    li.onclick = () => select(i);
    ul.appendChild(li);
  });
  const li = ul.children[sel]; if (li) li.scrollIntoView({ block: 'nearest' });
}

function move(delta) {
  const i = sel, j = i + delta;
  if (i < 0 || j < 0 || j >= dash.Elements.length) return;
  begin();
  const a = dash.Elements; [a[i], a[j]] = [a[j], a[i]]; sel = j;
  changed();
}

function duplicate() {
  const e = dash.Elements[sel]; if (!e) return;
  begin();
  const c = JSON.parse(JSON.stringify(e));
  c.Name = (e.Name || e.Type) + ' copy'; c.X = (c.X || 0) + 10; c.Y = (c.Y || 0) + 10;
  if (c.SegmentX) c.SegmentX = c.SegmentX.map(v => v + 10);
  dash.Elements.splice(sel + 1, 0, c); sel++;
  changed();
}

function remove() {
  if (sel < 0) return;
  begin();
  dash.Elements.splice(sel, 1); sel = Math.min(sel, dash.Elements.length - 1);
  changed();
}

function uniqueName(base) { let n = 1; const names = new Set(dash.Elements.map(e => e.Name)); while (names.has(base + n)) n++; return base + n; }

function addElement(type) {
  const cx = 300, cy = 200;
  const d = {
    label: { Text: 'Label', X: cx, Y: cy, W: 200, H: 30, Font: 14, Color: '#D3D3D3', Align: 'left' },
    value: { Bind: 'speed', Format: '0', X: cx, Y: cy, W: 200, H: 40, Font: 98, Color: '#FFFFFF', Align: 'center', Empty: '-', Samples: ['388'] },
    rect: { X: cx, Y: cy, W: 200, H: 100, Color: '#428AED' },
    box: { X: cx, Y: cy, W: 200, H: 100, Color: '#FFFFFF', Border: 3, Radius: 10 },
    ellipse: { X: cx, Y: cy, W: 200, H: 120, Color: '#FFFFFF', Border: 4, Fill: '#1B1F3C' },
    gradient: { X: cx, Y: cy, W: 200, H: 100, Colors: ['#1B1F3C', '#428AED'], Angle: 90 },
    bar: { Bind: 'rpmPercent', Min: 0, Max: 100, X: 100, Y: cy, W: 600, H: 30, Color: '#00FF40', Fill: '#202020', Orientation: 'horizontal' },
    deltabar: { Bind: 'delta', X: 200, Y: cy, H: 60, Segments: 7, Pitch: 28, SegmentWidth: 20, Range: 1, PositiveColor: '#FF0000', NegativeColor: '#00FF00', SegmentColor: '#808080' },
    popup: { X: 280, Y: 160, W: 240, H: 160, Radius: 10, Font: 101, ValueFont: 35, Duration: 2, Color: '#000000', Watch: [{ Bind: 'tcLevel', Label: 'TC', Color: '#28598B', Format: 'int' }] },
  }[type];
  begin();
  const e = { Type: type, Name: uniqueName(type), ...d };
  dash.Elements.push(e); sel = dash.Elements.length - 1;
  changed();
}

function addImage(file) {
  const reader = new FileReader();
  reader.onload = () => {
    const b64 = String(reader.result).split(',')[1];
    const img = new Image();
    img.onload = () => {
      begin();
      const name = file.name.replace(/\.[^.]+$/, '') + '@' + img.naturalWidth + 'x' + img.naturalHeight;
      dash.Images[name] = b64; images[name] = img;
      const k = Math.min(1, 300 / img.naturalWidth, 200 / img.naturalHeight);
      dash.Elements.push({ Type: 'image', Name: uniqueName('image'), Image: name, X: 250, Y: 140, W: Math.round(img.naturalWidth * k), H: Math.round(img.naturalHeight * k), MaxColors: 6 });
      sel = dash.Elements.length - 1;
      changed();
    };
    img.src = reader.result;
  };
  reader.readAsDataURL(file);
}

// ---------- inspector ----------

const FORMATS = ['0', '0.0', '0.00', '0.000', 'int', 'laptime', 'time:mm\\:ss\\.fff', 'gear', 'delta', 'text'];
const TYPES_FIELDS = {
  rect: [['Color', 'color'], ['ColorBind', 'bind'], ['ColorStops', 'json'], ['Opacity', 'number']],
  ellipse: [['Color', 'color', 'Colour (rim when Border > 0)'], ['Fill', 'color?', 'Inside'], ['Border', 'number'], ['ColorBind', 'bind'], ['ColorStops', 'json'], ['Opacity', 'number']],
  box: [['Color', 'color', 'Border colour'], ['Fill', 'color?', 'Inside'], ['Border', 'number'], ['Radius', 'number'], ['ColorBind', 'bind'], ['ColorStops', 'json'], ['Opacity', 'number']],
  gradient: [['Colors', 'lines', 'Colours (one per line)'], ['Angle', 'number', 'Angle (90 = down)'], ['Radius', 'number'], ['Border', 'number'], ['Color', 'color', 'Border colour'], ['Opacity', 'number']],
  image: [['Image', 'image'], ['MaxColors', 'number', 'Max colours'], ['Opacity', 'number']],
  label: [['Text', 'text'], ['Font', 'font'], ['Color', 'color'], ['Align', 'align'], ['ColorBind', 'bind']],
  value: [['Bind', 'bind'], ['Format', 'format'], ['Scale', 'number'], ['Empty', 'text', 'When no data'], ['Samples', 'csv', 'Widest texts'], ['PreviewText', 'text', 'Preview text'],
    ['Font', 'font'], ['Color', 'color'], ['Align', 'align'], ['PositiveColor', 'color?', 'Colour if > 0'], ['NegativeColor', 'color?', 'Colour if < 0'], ['Background', 'color?', 'Background (auto if empty)'], ['ColorBind', 'bind']],
  bar: [['Bind', 'bind'], ['Min', 'number'], ['Max', 'number'], ['Orientation', 'orientation'], ['Reverse', 'bool'], ['Color', 'color', 'Fill colour'], ['Fill', 'color?', 'Empty colour'], ['ColorBind', 'bind'], ['ColorStops', 'json']],
  deltabar: [['Bind', 'bind'], ['Segments', 'number', 'Segments per side'], ['SegmentWidth', 'number'], ['Pitch', 'number'], ['Range', 'number'], ['PositiveColor', 'color', 'Slower (left)'], ['NegativeColor', 'color', 'Faster (right)'], ['SegmentColor', 'color', 'Empty'], ['SegmentX', 'json', 'Segment x list (optional)']],
  popup: [['Watch', 'json', 'Watch [{Bind, Label, Color, Format}]'], ['Duration', 'number'], ['Font', 'font', 'Label font'], ['ValueFont', 'font', 'Value font'], ['Color', 'color', 'Text colour'], ['Radius', 'number']],
};

function renderProps() {
  const p = $('props');
  if (!dash) { p.innerHTML = ''; return; }
  const e = dash.Elements[sel];
  if (!e) { renderDashProps(p); return; }
  let html = `<div class="section" style="padding:0 0 8px"><h3>${esc(e.Type)}</h3>`;
  html += row('Name', `<input type="text" data-k="Name" value="${esc(e.Name)}">`);
  if (e.Type !== 'deltabar') html += row('X Y W H', `<div class="grid4">${['X', 'Y', 'W', 'H'].map(k => `<input type="number" data-k="${k}" data-num="1" value="${e[k] ?? 0}" title="${k}">`).join('')}</div>`);
  else html += row('X Y H', `<div class="grid4">${['X', 'Y', 'H'].map(k => `<input type="number" data-k="${k}" data-num="1" value="${e[k] ?? 0}" title="${k}">`).join('')}</div>`);
  for (const [k, kind, label] of (TYPES_FIELDS[e.Type] || [])) html += field(e, k, kind, label || k);
  html += `</div><div class="section" style="padding:8px 0"><h3>Conditions</h3>` +
    row('Visible when', `<textarea data-k="Visible" data-kind="lines" rows="2" placeholder="one binding per line, all must be true">${esc(visList(e).join('\n'))}</textarea>`) +
    row('Preview', `<label class="small"><input type="checkbox" data-k="PreviewVisible" data-kind="preview" ${e.PreviewVisible === false ? '' : 'checked'}> show in previews when conditions can't be evaluated</label>`) +
    `<div class="small">Bindings: a key (${bindings.slice(0, 6).map(b => b.key).join(', ')}…), <code>prop:</code>SimHub property, <code>ncalc:</code>/<code>js:</code> SimHub formula.</div></div>`;
  p.innerHTML = html + bindingsDatalist();
  wireInputs(p, e);
}

function renderDashProps(p) {
  const n = dash.Elements.length;
  p.innerHTML = `<div class="section" style="padding:0 0 8px"><h3>Dash</h3>` +
    row('Id', `<input type="text" data-k="Id" value="${esc(dash.Id)}">`) +
    row('Name', `<input type="text" data-k="Name" value="${esc(dash.Name)}">`) +
    row('Author', `<input type="text" data-k="Author" value="${esc(dash.Author)}">`) +
    row('Description', `<textarea data-k="Description" rows="3">${esc(dash.Description)}</textarea>`) +
    (dash.Source ? `<div class="small">Source: ${esc(dash.Source)}</div>` : '') +
    `<div class="small" style="margin-top:8px">${n} elements, ${Object.keys(dash.Images || {}).length} images. Select an element on the canvas or in the list to edit it.</div></div>`;
  wireInputs(p, dash);
}

const row = (label, control) => `<div class="row"><label>${esc(label)}</label><div>${control}</div></div>`;

function field(e, k, kind, label) {
  const v = e[k];
  switch (kind) {
    case 'number': return row(label, `<input type="number" data-k="${k}" data-num="1" value="${v ?? ''}">`);
    case 'text': return row(label, `<input type="text" data-k="${k}" value="${esc(v ?? '')}">`);
    case 'bool': return row(label, `<input type="checkbox" data-k="${k}" data-kind="bool" ${v ? 'checked' : ''}>`);
    case 'color': case 'color?': {
      const opt = kind === 'color?';
      return row(label, `<div class="inline"><input type="color" data-k="${k}" data-kind="colorpick" value="${hex6(v)}" ${opt && !v ? 'style="opacity:.35"' : ''}>` +
        `<input type="text" data-k="${k}" data-kind="colortext" value="${esc(v ?? '')}" placeholder="${opt ? 'none' : '#RRGGBB'}"></div>`);
    }
    case 'bind': return row(label, `<input type="text" data-k="${k}" list="bindlist" value="${esc(v ?? '')}" placeholder="key, prop:, ncalc:, js:">`);
    case 'format': return row(label, `<input type="text" data-k="${k}" list="formatlist" value="${esc(v ?? '0')}">`);
    case 'align': return row(label, `<select data-k="${k}">${['left', 'center', 'right'].map(a => `<option ${a === (v || 'left') ? 'selected' : ''}>${a}</option>`).join('')}</select>`);
    case 'orientation': return row(label, `<select data-k="${k}">${['horizontal', 'vertical'].map(a => `<option ${a === (v || 'horizontal') ? 'selected' : ''}>${a}</option>`).join('')}</select>`);
    case 'lines': return row(label, `<textarea data-k="${k}" data-kind="lines" rows="3">${esc((v || []).join('\n'))}</textarea>`);
    case 'csv': return row(label, `<input type="text" data-k="${k}" data-kind="csv" value="${esc((v || []).join(', '))}" placeholder="comma separated">`);
    case 'json': return row(label, `<textarea data-k="${k}" data-kind="json" rows="3">${esc(v == null ? '' : JSON.stringify(v))}</textarea>`);
    case 'image': return row(label, `<select data-k="${k}">${Object.keys(dash.Images || {}).map(n => `<option ${n === v ? 'selected' : ''}>${esc(n)}</option>`).join('')}</select>`);
    case 'font': {
      const cur = v ?? 14;
      const opts = fonts.map(f => `<option value="${f.id}" ${f.id === cur ? 'selected' : ''}>${f.id} · ${f.height} px${f.fullAscii ? '' : ' · ' + f.chars.slice(0, 14)}</option>`).join('');
      return row(label, `<div class="inline"><select data-k="${k}" data-num="1" style="flex:1">${opts}</select><button data-fit="${k}" title="Pick the tallest font whose text fits the box">Fit</button></div>`);
    }
  }
  return '';
}

function bindingsDatalist() {
  return `<datalist id="bindlist">${bindings.map(b => `<option value="${esc(b.key)}">${esc(b.description)}</option>`).join('')}</datalist>` +
    `<datalist id="formatlist">${FORMATS.map(f => `<option value="${esc(f)}">`).join('')}</datalist>`;
}

function wireInputs(root, target) {
  root.querySelectorAll('[data-k]').forEach(inp => {
    const k = inp.dataset.k, kind = inp.dataset.kind;
    const ev = (inp.tagName === 'SELECT' || inp.type === 'checkbox' || inp.type === 'color') ? 'change' : 'change';
    inp.addEventListener(ev, () => {
      begin();
      let v;
      if (kind === 'bool') v = inp.checked;
      else if (kind === 'preview') { v = inp.checked ? undefined : false; }
      else if (kind === 'colorpick') { const a = alphaOf(target[k]); v = (a ? '#' + a + inp.value.slice(1) : inp.value).toUpperCase(); }
      else if (kind === 'colortext') v = inp.value.trim() || undefined;
      else if (kind === 'lines') { v = inp.value.split('\n').map(s => s.trim()).filter(Boolean); if (!v.length) v = undefined; }
      else if (kind === 'csv') { v = inp.value.split(',').map(s => s.trim()).filter(Boolean); if (!v.length) v = undefined; }
      else if (kind === 'json') { try { v = inp.value.trim() ? JSON.parse(inp.value) : undefined; } catch (e) { toast('Not valid JSON: ' + e.message); undo.pop(); return; } }
      else if (inp.dataset.num) v = inp.value === '' ? undefined : Number(inp.value);
      else v = inp.value;
      if (v === undefined) delete target[k]; else target[k] = v;
      // keep the panel (and the cursor) as it is; colour pickers refresh their text twin
      changed({ noProps: !(kind === 'colorpick' || kind === 'colortext' || kind === 'preview') });
    });
  });
  root.querySelectorAll('[data-fit]').forEach(b => b.addEventListener('click', async () => {
    const e = target, text = e.Type === 'label' ? (e.Text || '0') : (e.Samples && e.Samples[0]) || e.PreviewText || '888';
    const r = await api(`/api/fonts/suggest?w=${e.W}&h=${e.H}&text=${encodeURIComponent(text)}`);
    if (r.font < 0) { toast(`"${text}" doesn't fit a ${e.W} x ${e.H} box in any font`); return; }
    begin(); e[b.dataset.fit] = r.font; changed(); toast(`Font ${r.font}: ${r.height} px tall, "${text}" is ${r.width} px wide`);
  }));
}

function updateGeometryFields() {
  const e = dash.Elements[sel]; if (!e) return;
  document.querySelectorAll('#props [data-k]').forEach(inp => { if (['X', 'Y', 'W', 'H'].includes(inp.dataset.k)) inp.value = e[inp.dataset.k] ?? 0; });
}

// ---------- checks ----------

let checkTimer = null, issuesByName = {};
function scheduleCheck(ms = 500) { clearTimeout(checkTimer); checkTimer = setTimeout(runCheck, ms); }
async function runCheck() {
  if (!dash) return;
  try {
    const r = await post(`/api/check?left=${padL()}&top=${padT()}`, dash);
    const st = $('status');
    st.className = 'badge ' + (r.errors ? 'err' : r.warnings ? 'warn' : 'ok');
    st.textContent = r.errors ? `${r.errors} error${r.errors > 1 ? 's' : ''}, ${r.warnings} warnings` : r.warnings ? `${r.warnings} warnings` : 'No problems';
    const c = r.cost;
    $('cost').textContent = `Draws in ~${c.StaticSeconds.toFixed(1)} s (${c.StaticFills} fills), ${c.DynamicElements} live elements` +
      (c.RedrawnValues.length ? `, ${c.RedrawnValues.length} values on busy backgrounds` : '');
    issuesByName = {};
    const ul = $('issues'); ul.innerHTML = '';
    for (const i of r.issues) {
      if (i.Element) issuesByName[i.Element] = (issuesByName[i.Element] ? issuesByName[i.Element] + '; ' : '') + i.Message;
      const li = document.createElement('li'); li.className = i.Level;
      li.textContent = (i.Element ? i.Element + ': ' : '') + i.Message;
      li.onclick = () => { const idx = dash.Elements.findIndex(e => e.Name === i.Element); if (idx >= 0) select(idx); };
      ul.appendChild(li);
    }
    renderList();
  } catch (e) { $('status').className = 'badge err'; $('status').textContent = 'Check failed: ' + e.message; }
}
const padL = () => Number($('padL').value) || 0, padT = () => Number($('padT').value) || 0;

// ---------- exact preview, demo ----------

let exact = false, demo = false, demoT = 2, demoTimer = null;
async function refreshExact() {
  if (!exact) return;
  try {
    const blob = await post(`/api/render?mode=${demo ? 'demo' : 'preview'}&seconds=${demoT}`, dash);
    const img = $('preview'); const old = img.src; img.src = URL.createObjectURL(blob); if (old) URL.revokeObjectURL(old);
  } catch (e) { toast('Preview failed: ' + e.message); }
}
function setExact(on) {
  exact = on; $('btnExact').classList.toggle('on', on); $('preview').style.display = on ? 'block' : 'none';
  if (on) refreshExact(); else setDemo(false);
}
function setDemo(on) {
  demo = on; $('btnDemo').classList.toggle('on', on); clearInterval(demoTimer);
  if (on) { if (!exact) setExact(true); demoTimer = setInterval(() => { demoT = demoT >= 90 ? 2 : demoT + 0.5; refreshExact(); }, 500); }
  else if (exact) refreshExact();
}

// ---------- wheel ----------

let wheelOn = false, wheelTimer = null;
function scheduleWheel() { if (!wheelOn) return; clearTimeout(wheelTimer); wheelTimer = setTimeout(pushWheel, 700); }
async function pushWheel() {
  try { await post(`/api/wheel/show?left=${padL()}&top=${padT()}`, dash); } catch (e) { toast('Wheel: ' + e.message); setWheel(false); }
}
async function setWheel(on) {
  wheelOn = on; $('btnWheel').classList.toggle('on', on);
  if (on) pushWheel(); else { try { await post('/api/wheel/stop', {}); } catch (e) { } }
}
setInterval(() => { if (wheelOn) pushWheel(); }, 20000); // the plugin drops a preview after ~60 s without news

// ---------- save / open ----------

async function save() {
  if (!dash) return;
  const builtIn = (await api('/api/dashes')).some(d => d.id === dash.Id && d.builtIn);
  if (builtIn || !dash.Id) return saveAs();
  try {
    const r = await api('/api/dashes/' + encodeURIComponent(dash.Id), { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(dash) });
    dirty = false; toast('Saved: ' + r.saved);
    refreshLibrary(dash.Id);
  } catch (e) { toast('Save failed: ' + e.message, 4000); }
}
async function saveAs() {
  const id = prompt('Save as (Id, used as the file name):', (dash.Id || 'my-dash').replace(/-copy$/, '') + (dash.Id ? '-copy' : ''));
  if (!id) return;
  dash.Id = id.trim();
  const name = prompt('Name shown in the dash list:', dash.Name || id);
  if (name) dash.Name = name;
  renderProps(); await save();
}
function download() {
  const blob = new Blob([JSON.stringify(dash, null, 2)], { type: 'application/json' });
  const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = (dash.Id || 'dash') + '.json'; a.click();
}
function confirmDiscard() { return !dirty || confirm('Discard unsaved changes?'); }

// ---------- SimHub import ----------

let simhub = [], simhubSel = null;
async function openImport() {
  $('report').style.display = 'none';
  $('importDlg').showModal();
  if (!simhub.length) simhub = await api('/api/simhub');
  renderSimhub();
}
function renderSimhub() {
  const f = $('simhubFilter').value.toLowerCase();
  $('simhublist').innerHTML = simhub.filter(d => d.name.toLowerCase().includes(f))
    .map(d => `<div data-name="${esc(d.name)}" class="${d.name === simhubSel ? 'sel' : ''}">${esc(d.name)}</div>`).join('') || '<div class="small">No SimHub dashes found.</div>';
  $('simhublist').querySelectorAll('div[data-name]').forEach(el => el.onclick = async () => {
    simhubSel = el.dataset.name; renderSimhub();
    const screens = await api('/api/simhub/screens?name=' + encodeURIComponent(simhubSel));
    $('simhubScreen').innerHTML = '<option value="">(main screen)</option>' + screens.map((s, i) => `<option value="${i}">${esc(s)}</option>`).join('');
  });
}
async function runImport() {
  if (!simhubSel) { toast('Pick a dash'); return; }
  if (!confirmDiscard()) return;
  $('impGo').disabled = true; $('impGo').textContent = 'Importing…';
  try {
    const r = await post('/api/import', {
      name: simhubSel, screen: $('simhubScreen').value || null, images: $('impImages').checked, colors: Number($('impColors').value),
      maxSeconds: Number($('impSeconds').value), fitWidth: W - padL(), fitHeight: H - padT(), // leave room for the wheel's padding
    });
    load(r.dash); dirty = true; sel = -1; renderProps();
    const rep = r.report;
    $('report').style.display = 'block';
    $('report').textContent = `${rep.Dash} / ${rep.Screen}\n${rep.Converted} converted, ${rep.Approximated} approximated, ${rep.Skipped} skipped` +
      (Object.keys(rep.SkippedTypes).length ? ` (${Object.entries(rep.SkippedTypes).map(([k, v]) => k + ' x' + v).join(', ')})` : '') +
      `\nFormulas kept for SimHub: ${rep.NcalcFormulas} NCalc, ${rep.JsFormulas} JavaScript\n` +
      `Check: ${r.check.errors} errors, ${r.check.warnings} warnings; draws in ~${r.check.cost.StaticSeconds.toFixed(1)} s\n` +
      (rep.Notes.length ? '\n' + rep.Notes.map(n => '- ' + n).join('\n') : '') + '\n\nFine-tune it here, then Save.';
  } catch (e) { toast('Import failed: ' + e.message, 5000); }
  finally { $('impGo').disabled = false; $('impGo').textContent = 'Import'; }
}

// ---------- layout ----------

function fit() {
  const c = $('center');
  const avail = Math.min((c.clientWidth - 30) / W, (c.clientHeight - 80) / H);
  zoom = Math.max(0.5, Math.min(2, Math.floor(avail * 4) / 4 || 1));
  for (const el of [stage, $('preview')]) { el.style.width = W * zoom + 'px'; el.style.height = H * zoom + 'px'; }
}

// ---------- start ----------

async function start() {
  [metrics, fonts, bindings] = await Promise.all([api('/api/fonts/metrics'), api('/api/fonts'), api('/api/bindings')]);
  fonts = fonts.filter(f => f.height > 0).sort((a, b) => a.height - b.height || a.id - b.id);
  try { hostInfo = await api('/api/wheel'); } catch (e) { hostInfo = null; }
  const wheelOk = hostInfo && hostInfo.available !== false;
  $('btnWheel').disabled = !wheelOk;
  $('host').textContent = wheelOk ? 'Running in SimHub: "Show on wheel" puts this dash on your FX Pro' : 'Offline designer (no wheel; SimHub formulas aren\'t evaluated here)';
  await refreshLibrary();
  let draft = null;
  try { draft = JSON.parse(localStorage.getItem('fxdash-draft') || 'null'); } catch (e) { }
  const q = new URLSearchParams(location.search).get('dash');
  if (q) { load(await api('/api/dashes/' + encodeURIComponent(q))); $('library').value = q; }
  else if (draft && draft.Elements) { load(draft); dirty = true; toast('Restored your last unsaved draft'); }
  else load(blankDash());
  fit();
}

$('library').onchange = async e => { const id = e.target.value; if (!id || !confirmDiscard()) return; sel = -1; load(await api('/api/dashes/' + encodeURIComponent(id))); };
$('btnNew').onclick = () => { if (confirmDiscard()) { sel = -1; load(blankDash()); } };
$('btnImport').onclick = openImport;
$('btnSave').onclick = save; $('btnSaveAs').onclick = saveAs; $('btnDownload').onclick = download;
$('btnUpload').onclick = () => $('file').click();
$('file').onchange = e => { const f = e.target.files[0]; if (!f || !confirmDiscard()) return; f.text().then(t => { sel = -1; load(JSON.parse(t)); dirty = true; }).catch(err => toast('Not a dash: ' + err.message)); e.target.value = ''; };
$('btnUndo').onclick = doUndo; $('btnRedo').onclick = doRedo;
$('btnAdd').onclick = () => { const t = $('addType').value; if (t === 'image') $('imgfile').click(); else addElement(t); };
$('imgfile').onchange = e => { const f = e.target.files[0]; if (f) addImage(f); e.target.value = ''; };
$('btnUp').onclick = () => move(-1); $('btnDown').onclick = () => move(1); $('btnDup').onclick = duplicate; $('btnDel').onclick = remove;
$('btnExact').onclick = () => setExact(!exact); $('btnDemo').onclick = () => setDemo(!demo); $('btnWheel').onclick = () => setWheel(!wheelOn);
$('padL').onchange = $('padT').onchange = () => { scheduleCheck(0); scheduleWheel(); };
$('simhubFilter').oninput = renderSimhub; $('impGo').onclick = runImport; $('impCancel').onclick = () => $('importDlg').close();
window.addEventListener('resize', fit);
window.addEventListener('beforeunload', e => { if (wheelOn) navigator.sendBeacon('/api/wheel/stop'); });
setInterval(() => { if (exact && !demo) refreshExact(); }, 1500);
start().catch(e => { document.body.innerHTML = '<p style="padding:20px">The designer couldn\'t start: ' + esc(e.message) + '</p>'; });

// For agents driving the page: the current dash and a way to replace it.
window.fxdash = { get dash() { return dash; }, load: d => load(d), check: runCheck };
