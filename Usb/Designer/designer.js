// FX Unleashed Dash Studio (served by the FX Unleashed plugin or `fxdash serve`). Edits the dash JSON format
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
  pages: 'M8 3h12v15H8z M5 6v15h11',
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
  dim: { name: 'Dim', blurb: 'Darkens the screen', icon: 'eyeoff' },
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
  popup: { W: 240, H: 160, Radius: 10, Font: 101, ValueFont: 35, Duration: 2, Color: '#000000', Watch: [{ Bind: 'tcLevel', Label: 'TC', Color: '#28598B', Format: 'int' }] },  dim: { X: 0, Y: 0, W: 0, H: 0, Opacity: 50, Visible: 'ncalc:[DataCorePlugin.GameRawData.CurrentPlayer.mHeadlights]' },
};
const FORMATS = [['0', '123'], ['0.0', '12.3'], ['0.00', '1.23'], ['int', 'Whole'], ['laptime', '1:23.456'], ['gear', 'R N 1'], ['delta', '+0.12'], ['text', 'Text']];
const PALETTE = ['#FFFFFF', '#D3D3D3', '#8A8F98', '#3A3F4A', '#1B1F3C', '#000000', '#FF1F2D', '#FF1A1A', '#FF5A00', '#FFB000', '#FFD000', '#FFFF00',
  '#16D65A', '#00FF40', '#00FFA3', '#00F0FF', '#428AED', '#0060FF', '#2F6BFF', '#7B2FFF', '#9D4EDD', '#FF00D0', '#FF2E97', '#28598B'];

// ---------- state ----------
let dash = null, sel = -1, hover = -1, dirty = false;
// selection: `sel` is the element the inspector edits (the last one picked), `picked` every selected element (sel among them)
let picked = new Set();
let metrics = null, fonts = [], bindings = [], hostInfo = null, library = [];
const undoStack = [], redoStack = [];
const images = {};
let zoom = 1, view = 'edit', clipboard = null;
let pad = { l: 10, t: 20 };
let overlay = -1, overlays = []; // the overlay shown in the previews (index in /api/overlays), the dash's overlays

const isShape = t => ['rect', 'ellipse', 'box', 'gradient', 'image'].includes(t);
const visList = e => !e.Visible ? [] : Array.isArray(e.Visible) ? e.Visible : [e.Visible];
const isConditional = e => visList(e).length > 0;
const setVis = (e, l) => { if (l.length) e.Visible = l.length === 1 ? l[0] : l; else { delete e.Visible; delete e.PreviewVisible; } };

// ---------- pages ----------
// A dash has up to four sets of pages, each flipped on its own on the wheel (Next page flips them all, Next page 1-4 one
// set). Set 0 = Pages ("page:N" in an element's conditions), sets 1-3 = PageSets ("page2:N".."page4:N"). An element
// without a page condition of a set shows on every page of that set. The designer shows one page of each set (`shown`).
const MAX_SETS = 4;
let shown = [0, 0, 0, 0];
let addTo = null; // new elements go on: null = every page, else the set whose shown page they go on
const condOf = (s, p) => (s === 0 ? 'page' : 'page' + (s + 1)) + ':' + p;
const parsePage = c => { const m = /^\s*page([2-4])?\s*:\s*(\d+)\s*$/i.exec(String(c)); return m ? [m[1] ? Number(m[1]) - 1 : 0, Number(m[2])] : null; };
const pageConds = e => visList(e).map(parsePage).filter(Boolean);
const pageIn = (e, s) => { for (const [k, p] of pageConds(e)) if (k === s) return p; return null; };
const pageOf = e => pageIn(e, 0);
const setNames = s => !dash ? null : s === 0 ? dash.Pages : ((dash.PageSets || [])[s - 1] || {}).Pages;
const countOf = s => !dash ? 1 : Math.max(1, (setNames(s) || []).length, ...dash.Elements.map(e => (pageIn(e, s) ?? -1) + 1));
const pageCount = () => countOf(0);
const setExists = s => s === 0 || !!(dash && (dash.PageSets || [])[s - 1]) || countOf(s) > 1;
const setsUsed = () => { let n = 1; for (let s = 1; s < MAX_SETS; s++) if (setExists(s)) n = s + 1; return n; };
const setTitle = s => s === 0 ? 'Pages' : (((dash.PageSets || [])[s - 1] || {}).Name || `Pages ${s + 1}`);
const pageName = (i, s = 0) => (setNames(s) || [])[i] || `Page ${i + 1}`;
const shownIn = s => clamp(shown[s] || 0, 0, countOf(s) - 1);
const flipCount = () => Math.max(...Array.from({ length: MAX_SETS }, (_, s) => countOf(s)));
const flippingSets = () => Array.from({ length: setsUsed() }, (_, s) => s).filter(s => countOf(s) > 1);
const hasSets = () => flippingSets().some(s => s > 0);
const onPage = e => pageConds(e).every(([s, p]) => p === shownIn(s));
const otherConds = e => visList(e).filter(c => !parsePage(c));
const pagesParam = () => Array.from({ length: MAX_SETS }, (_, s) => shownIn(s)).join(',');
const pageLabel = e => pageConds(e).map(([s, p]) => (hasSets() ? setTitle(s) + ': ' : '') + pageName(p, s)).join(' · ');
// with an overlay picked: what shows while it's up (its conditions, and its parents'; blinking and staged items too;
// what takes turns with it hides)
function overlayShows(e) {
  const o = overlays[overlay]; if (!o) return false;
  const forced = new Set(); for (let x = o; x; x = overlays[x.parent]) x.conditions.forEach(c => forced.add(c));
  // the negations that go false: of each forced condition, and of every part of them joined in order (as written)
  const nc = o.conditions.filter(c => c.startsWith('ncalc:')).map(c => c.slice(6)).slice(0, 8), joined = new Set();
  for (const c of forced) if (c.startsWith('ncalc:')) joined.add(c.slice(6));
  for (let mask = 1; mask < (1 << nc.length); mask++) { const part = nc.filter((c, i) => (mask >> i) & 1); if (part.length > 1) joined.add(part.map(c => '(' + c + ')').join(' and ')); }
  for (const c of otherConds(e)) {
    if (forced.has(c)) continue;
    if (/blink\(/i.test(c) || /^ncalc:\s*!\s*changed\(/i.test(c)) continue;
    return false;
  }
  for (const c of visList(e).map(String))
    if (c.startsWith('ncalc:!(') && joined.has(c.slice(8, -1))) return false;
  return true;
}
const shownInPreview = e => onPage(e) && (overlay >= 0 ? overlayShows(e) : (otherConds(e).length === 0 || e.PreviewVisible !== false));
const nameOf = e => e.Name || e.Text || e.Bind || e.Type;
const cur = () => dash && dash.Elements[sel];
const isPicked = i => picked.has(i);
const pickedList = () => !dash ? [] : [...picked].filter(i => i >= 0 && i < dash.Elements.length).sort((a, b) => a - b);
const multi = () => picked.size > 1;
// set the selection without drawing (callers redraw): `primary` is the one the inspector edits
function setPicked(list, primary) {
  picked = new Set(list.filter(i => i >= 0 && dash && i < dash.Elements.length));
  sel = primary !== undefined && primary >= 0 && picked.has(primary) ? primary : picked.size ? [...picked][picked.size - 1] : -1;
  if (primary !== undefined && primary >= 0 && !picked.has(primary) && dash && primary < dash.Elements.length) { picked.add(primary); sel = primary; }
}
const selOnly = i => setPicked(i >= 0 ? [i] : []);

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

// ---------- fonts ----------
// The screen has 128 fonts, each made for one of Simagic's own dashes (their names in the screen image: "992a" is a font of
// the Comet 992 dash). The designer shows them by family (that dash, or Standard / Arial), size, width and what they
// can draw, instead of bare numbers; the id stays in the dash file.
const FONT_NAMES = ('S56 S150 S28 S48 S32 S24 s36 s30 S40 54me S16 S128 S16 s36 S20 S44 unit_30 unit_24 f175a f175b f175c f175d f175e f175f f175g 911_b 911_c 911_d 911_e 911_a1 '
  + 'f122_a f122_b f122_c f122_d ess_a ess_b ess_c ess_d gt_a arial_a arial_b arial_c ir18_a 720s_a 720s_b 720s_c w12_a w12_b w12_c w12_d w12_e w12_f w12_g bmw1 bmw2 bmw3 bmw4 bmw5 '
  + 'flags f123 f123a f123b f123c m4a m4b m4c m4c 296a 296b 992a 992b 992c 992d ir04a ir04b ir04c 488a 488b caddy1 caddy2 jsp320a jsp320b jsp320c jsp320d jsp320e onea onee one1 one3 '
  + 'one4 one6 one6 isf23a isf23b isf23c isf23d 963a 963b 963c 963d 963e 963f 963g arx06a arx06b arx06c arx06c arx06e fmx2 fxm3 fxm1 f3a f3a f3b f3c f3d m4d 499a lmp2a lmp2b c8ra c8rb '
  + 'c8rc c8rd c8re c8rf c8rg c8rh').split(' ');
// font name -> family (the stock dash it belongs to, by the name SimPro shows; the raw prefix where that isn't clear)
const FONT_FAMILIES = [[/^s\d/i, 'Standard'], [/^arial/, 'Arial'], [/^unit/, 'Units'], [/^flags/, 'Flags'], [/^720s/, '720Super'], [/^w12/, 'W12_3'], [/^992/, 'Comet 992'],
  [/^911/, 'Comet GT3'], [/^caddy/, 'IMD-Caddy'], [/^jsp320/, 'LMP320'], [/^isf23/, 'ISF23'], [/^963/, 'LM 963'], [/^arx06/, 'LM06'], [/^499/, 'LMD-Hybrid'], [/^lmp2/, 'LMP207'],
  [/^c8r/, 'GM-8Racing'], [/^488/, 'V8 F154 EVO'], [/^296/, 'Progettof171'], [/^bmw/, 'Motor Sportg82'], [/^f175/, 'Formula75'], [/^f122/, 'Formula22'], [/^f123/, 'Formula23'],
  [/^ir18/, 'Racing18'], [/^one/, 'Simagic ONE'], [/^(fmx|fxm)/, 'FMX'], [/^ir04/, 'IR04'], [/^m4/, 'M4'], [/^f3/, 'F3'], [/^ess/, 'ESS'], [/^gt_/, 'GT'], [/^54me/, '54ME']];
const STYLE_NAMES = { xwide: 'Extra wide', wide: 'Wide', regular: 'Regular', narrow: 'Narrow', mono: 'Fixed width' };
const CHAR_NAMES = { text: 'Text', numbers: 'Numbers', gear: 'Gear digits', caps: 'Capitals', symbols: 'Symbols' };
let fontInfo = [];
function buildFontInfo() {
  const seen = new Map();
  fontInfo = metrics.heights.map((h, id) => {
    const w = metrics.widths[id], adv = c => w[c.charCodeAt(0) - 32] ?? -1, has = c => adv(c) >= 0;
    const n = w.filter(x => x >= 0).length;
    const chars = n === 0 || !h ? 'none' : n >= 95 ? 'text' : has('A') && has('Z') && !has('a') && !has('0') ? 'caps'
      : has('0') && has('9') && has('N') && has('R') ? 'gear' : has('0') && has('9') ? 'numbers' : 'symbols';
    // width: of the digits (or capitals) against the height; every character as wide as the next = fixed width
    const ref = [...'0123456789'].filter(has).length >= 5 ? '0123456789' : 'AHMNOR';
    const ws = [...ref].filter(has).map(adv), rw = ws.length ? ws.reduce((a, b) => a + b, 0) / ws.length / h : 0.5;
    const mono = chars === 'text' && adv('i') === adv('W') && adv('i') === adv('0');
    const style = mono ? 'mono' : rw >= 0.9 ? 'xwide' : rw >= 0.64 ? 'wide' : rw >= 0.49 ? 'regular' : 'narrow';
    const name = FONT_NAMES[id] || 'font' + id, fam = (FONT_FAMILIES.find(([re]) => re.test(name)) || [, name])[1];
    const key = h + ':' + w.join(','), dupOf = seen.has(key) ? seen.get(key) : -1;
    if (dupOf < 0) seen.set(key, id);
    return { id, h, chars, style, family: fam, name, dupOf, usable: chars !== 'none' };
  });
}
// "40 px Narrow" (+ what it draws when that isn't all text)
function fontLabel(id) {
  const i = fontInfo[id]; if (!i) return 'Font ' + id;
  return `${i.h} px ${STYLE_NAMES[i.style]}${i.chars !== 'text' ? ' · ' + (CHAR_NAMES[i.chars] || '') : ''}`;
}
const fontSub = id => { const i = fontInfo[id]; return i ? `${i.family}${i.family === 'Standard' || i.family === 'Arial' ? '' : ' dash'} · #${id}` : '#' + id; };
const canDraw = (id, text) => textWidth(id, text) >= 0;
// the face the designer draws a font with (the screen's own letters aren't in the plugin): the closest width of Bahnschrift
function faceFor(f) {
  const st = (fontInfo[f] || {}).style;
  return st === 'mono' ? '"Cascadia Mono", Consolas, monospace' : st === 'narrow' ? '"Bahnschrift Condensed", "Bahnschrift SemiCondensed", Bahnschrift, "Arial Narrow", sans-serif'
    : st === 'regular' ? '"Bahnschrift SemiCondensed", Bahnschrift, "Segoe UI", sans-serif' : 'Bahnschrift, "Segoe UI", sans-serif';
}
// text in font f, each character in its real cell (the screen's advance widths): at x (left), y (top of the font's
// height), `scale` times the real size; cells past maxW are left out (the screen wraps them onto a clipped line)
function glyphCells(f, text, maxW = Infinity) {
  const cells = []; let width = 0;
  for (const ch of String(text).replace(/[^\x20-\x7e]/g, '')) { const cw = Math.max(0, charWidth(f, ch)); if (width + cw > maxW) break; cells.push([ch, cw]); width += cw; }
  return { cells, width };
}
function drawGlyphs(g, f, cells, x, y, scale = 1) {
  const fh = fontHeight(f); if (!fh) return;
  // the screen's letters stand on a baseline near the top of the line: the Windows font matched to this screen font
  // (FontLooks: face, size, width, baseline), each letter centred in its cell, as the plugin's exact preview draws them
  const lk = metrics && metrics.looks && metrics.looks[f];
  if (lk) {
    g.font = `${lk.weight} ${lk.stretch !== 'normal' ? lk.stretch + ' ' : ''}${Math.max(2, lk.size * scale)}px ${lk.css}, sans-serif`;
    if ('fontStretch' in g) g.fontStretch = lk.stretch === 'semi-condensed' ? 'semi-condensed' : lk.stretch === 'condensed' ? 'condensed' : 'normal';
    g.textAlign = 'center'; g.textBaseline = 'alphabetic';
    for (const [ch, cw] of cells) {
      g.save(); g.translate(x + (cw / 2 + lk.dx) * scale, y + lk.baseline * scale); g.scale(lk.sx, 1); g.fillText(ch, 0, 0); g.restore();
      x += cw * scale;
    }
    if ('fontStretch' in g) g.fontStretch = 'normal';
    return;
  }
  g.font = `${fh >= 90 ? 'bold ' : ''}${Math.max(4, fh * .78 * scale)}px ${faceFor(f)}`;
  g.textAlign = 'center'; g.textBaseline = 'middle';
  for (const [ch, cw] of cells) {
    const cx = x + cw * scale / 2, cy = y + fh * scale / 2;
    // letters and digits fill their cell about as the screen's do: squeezed or widened a little
    const mw = /[A-Za-z0-9]/.test(ch) ? g.measureText(ch).width : 0;
    const sx = mw > 0 ? clamp(cw * scale * .84 / mw, .7, 1.45) : 1;
    if (sx !== 1) { g.save(); g.translate(cx, cy); g.scale(sx, 1); g.fillText(ch, 0, 0); g.restore(); } else g.fillText(ch, cx, cy);
    x += cw * scale;
  }
}
// a small picture of `text` in font f in a w x h box (dashed), scaled to fit `cw` x `ch` css px: what fits, and what doesn't (red)
function paintFontSample(cv, f, text, boxW, boxH, align = 'left') {
  const dpr = window.devicePixelRatio || 1, cw = cv.clientWidth || Number(cv.getAttribute('width')) || 150, chh = cv.clientHeight || 36;
  cv.width = Math.round(cw * dpr); cv.height = Math.round(chh * dpr);
  const g = cv.getContext('2d'); g.setTransform(dpr, 0, 0, dpr, 0, 0); g.clearRect(0, 0, cw, chh);
  const fh = fontHeight(f); if (!fh) return;
  const all = glyphCells(f, text), tw = all.width;
  const bw = boxW || tw, bh = boxH || fh;
  const scale = Math.min(1, (chh - 4) / Math.max(fh, bh), (cw - 4) / Math.max(bw, tw, 1));
  const bx = 2, by = (chh - bh * scale) / 2;
  if (boxW) { g.setLineDash([3, 2]); g.strokeStyle = 'rgba(255,255,255,.28)'; g.strokeRect(bx + .5, by + .5, bw * scale - 1, bh * scale - 1); g.setLineDash([]); }
  const fit = glyphCells(f, text, bw);
  const x0 = align === 'center' ? bx + (bw - fit.width) / 2 * scale : align === 'right' ? bx + (bw - fit.width) * scale : bx;
  const ty = by + (bh - fh) / 2 * scale;
  g.fillStyle = fh > bh ? '#ff5560' : '#eef0f3';
  drawGlyphs(g, f, fit.cells, x0, ty, scale);
  if (fit.cells.length < all.cells.length) { g.fillStyle = '#ff5560'; drawGlyphs(g, f, all.cells.slice(fit.cells.length), x0 + fit.width * scale, ty, scale); }
}
// the next bigger (dir 1) or smaller (-1) font for an element: same family first, then the same width, that can draw its text
function stepFont(e, k, dir) {
  const curF = e[k] ?? 14, ci = fontInfo[curF] || {}, h0 = fontHeight(curF), text = sampleText(e) || '0';
  const ok = fontInfo.filter(i => i.usable && i.dupOf < 0 && i.id !== curF && canDraw(i.id, text) && (dir > 0 ? i.h > h0 : i.h < h0));
  const near = l => l.sort((a, b) => dir > 0 ? a.h - b.h : b.h - a.h)[0];
  return (near(ok.filter(i => i.family === ci.family)) || near(ok.filter(i => i.style === ci.style && i.chars === ci.chars)) || near(ok) || {}).id;
}

// ---------- history ----------
const snapshot = () => JSON.stringify(dash);
function begin() { undoStack.push(snapshot()); if (undoStack.length > 300) undoStack.shift(); redoStack.length = 0; updateUndo(); }
function changed(opt = {}) {
  setDirty(true);
  if (!opt.keepLayers) renderLayers();
  renderPages(); renderPagesPane();
  draw();
  if (opt.inspector) renderInspector();
  scheduleCheck(); scheduleWheel(); saveDraft(); updateHint();
}
function doUndo() { if (!undoStack.length) return; redoStack.push(snapshot()); restore(JSON.parse(undoStack.pop())); }
function doRedo() { if (!redoStack.length) return; undoStack.push(snapshot()); restore(JSON.parse(redoStack.pop())); }
function restore(d) { dash = d; loadImages(); setPicked(pickedList().filter(i => i < dash.Elements.length), sel < dash.Elements.length ? sel : -1); setDirty(true); renderAll(); scheduleCheck(); scheduleWheel(); updateUndo(); }
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
  setPicked([]); hover = -1; overlay = -1; shown = [0, 0, 0, 0]; addTo = null;
  loadImages();
  renderAll();
  scheduleCheck(0); scheduleWheel();
  markLibrary();
}
function loadImages() {
  for (const k of Object.keys(images)) delete images[k];
  for (const [k, v] of Object.entries(dash.Images || {})) { const img = new Image(); img.onload = draw; img.src = 'data:' + imageMime(v) + ';base64,' + v; images[k] = img; }
}
const blankDash = () => ({ FormatVersion: 2, Id: 'my-dash', Name: 'My dash', Author: '', Description: '', Elements: [], Images: {} });
function renderAll() {
  if (addTo !== null && !setExists(addTo)) addTo = null;
  renderHeader(); renderPages(); renderPagesPane(); renderLayers(); renderInspector(); draw(); updateHint();
}

// ---------- pages: names, order, adding and removing (the elements' page conditions follow) ----------
// the names of set s, filled up to its page count (creating the set's PageSets entry, and those before it)
function ensureNames(s) {
  const n = countOf(s);
  if (s === 0) { if (!dash.Pages || dash.Pages.length < n) dash.Pages = Array.from({ length: n }, (_, i) => pageName(i, 0)); return dash.Pages; }
  dash.PageSets = dash.PageSets || [];
  while (dash.PageSets.length < s) dash.PageSets.push({ Name: `Pages ${dash.PageSets.length + 2}`, Pages: [] });
  const ps = dash.PageSets[s - 1];
  if (!ps.Pages || ps.Pages.length < n) ps.Pages = Array.from({ length: n }, (_, i) => pageName(i, s));
  return ps.Pages;
}
// set s's page conditions: map(old page) -> new page, or null (the condition goes: the element shows on every page)
function remapPages(s, map) {
  for (const e of dash.Elements) {
    let hit = false; const out = [];
    for (const c of visList(e)) {
      const pc = parsePage(c);
      if (!pc || pc[0] !== s) { out.push(c); continue; }
      hit = true; const to = map(pc[1]);
      if (to !== null && to !== undefined) out.push(condOf(s, to));
    }
    if (hit) setVis(e, out);
  }
}
// set `from`'s conditions become set `to`'s (after a set before it went)
function renumberSet(from, to) {
  for (const e of dash.Elements) {
    const l = visList(e); if (!l.some(c => { const p = parsePage(c); return p && p[0] === from; })) continue;
    setVis(e, l.map(c => { const p = parsePage(c); return p && p[0] === from ? condOf(to, p[1]) : c; }));
  }
}
function addPage(s = 0) {
  begin();
  const names = ensureNames(s);
  if (names.length === 0) names.push('Page 1');
  names.push('Page ' + (names.length + 1));
  shown[s] = names.length - 1;
  changed({ inspector: true });
  if (names.length === 2) toast('Two pages: what you had shows on every page. Put elements on a page from the Pages tab, Page in the inspector, or right-click', 'ok', 4800);
  switchTab('pages', true);
}
async function addSet() {
  const s = setsUsed(); if (s >= MAX_SETS) return;
  const r = await ask({ title: 'Add a set of pages', icon: 'layers', ok: 'Add the set',
    text: 'A set of pages flips on its own: one part of the dash (fuel, lap times, tyres) with its own Next page button on the wheel. Next page flips every set at once.',
    fields: [{ label: 'What it shows', value: s === 1 ? 'Lap times' : 'Pages ' + (s + 1) }] });
  if (!r) return;
  begin();
  for (let k = 1; k < s; k++) ensureNames(k);
  dash.PageSets = dash.PageSets || [];
  dash.PageSets.push({ Name: r[0] || 'Pages ' + (s + 1), Pages: ['Page 1', 'Page 2'] });
  shown[s] = 0;
  changed({ inspector: true });
  toast(`"${setTitle(s)}" added: put elements on its pages, the rest of the dash stays as it is`, 'ok', 4200);
}
function removeSetNow(s) { // set s (1-3) goes; its elements show on every page of it (the caller removed them if wanted)
  remapPages(s, () => null);
  dash.PageSets.splice(s - 1, 1);
  for (let k = s + 1; k < MAX_SETS; k++) renumberSet(k, k - 1);
  shown.splice(s, 1); shown.push(0);
  if (addTo === s) addTo = null; else if (addTo > s) addTo--;
  if (!dash.PageSets.length) delete dash.PageSets;
}
async function removeSet(s) {
  const on = dash.Elements.filter(e => pageIn(e, s) !== null).length;
  let keep = true;
  if (on) {
    const r = await ask({ title: `Remove "${setTitle(s)}"?`, icon: 'trash', danger: true, ok: 'Remove the set',
      text: `${on} element${on === 1 ? ' is' : 's are'} on its pages. Remove ${on === 1 ? 'it' : 'them'} too, or keep ${on === 1 ? 'it' : 'them'} showing all the time.`,
      fields: [{ k: 'keep', label: 'Keep them, showing all the time', type: 'check', value: false }] });
    if (!r) return; keep = !!r[0];
  }
  begin();
  if (!keep) dash.Elements = dash.Elements.filter(e => pageIn(e, s) === null);
  removeSetNow(s);
  setPicked(pickedList().filter(i => i < dash.Elements.length), sel < dash.Elements.length ? sel : -1);
  changed({ inspector: true });
}
function renameSet(s, name) { if (s === 0) return; begin(); ensureNames(s); dash.PageSets[s - 1].Name = name.trim() || `Pages ${s + 1}`; changed({}); }
function renamePage(s, i, name) { begin(); ensureNames(s)[i] = name.trim() || ('Page ' + (i + 1)); changed({}); }
function movePage(s, i, dir) {
  const names = ensureNames(s), j = i + dir; if (j < 0 || j >= names.length) return;
  begin();
  [names[i], names[j]] = [names[j], names[i]];
  remapPages(s, k => k === i ? j : k === j ? i : k);
  if (shown[s] === i) shown[s] = j; else if (shown[s] === j) shown[s] = i;
  changed({ inspector: true });
}
async function deletePage(s, i) {
  ensureNames(s);
  const on = dash.Elements.filter(e => pageIn(e, s) === i).length;
  let keep = false;
  if (on) {
    const r = await ask({ title: `Remove "${pageName(i, s)}"?`, icon: 'trash', danger: true, ok: 'Remove the page',
      text: `${on} element${on === 1 ? ' is' : 's are'} on this page. Remove ${on === 1 ? 'it' : 'them'} with the page, or keep ${on === 1 ? 'it' : 'them'} on every page instead.`,
      fields: [{ k: 'keep', label: 'Keep them, on every page', type: 'check' }] });
    if (!r) return; keep = !!r[0];
  }
  begin();
  if (!keep) dash.Elements = dash.Elements.filter(e => pageIn(e, s) !== i);
  const names = ensureNames(s);
  names.splice(i, 1);
  remapPages(s, k => k === i ? null : k > i ? k - 1 : k);
  // one page left is no pages: its elements show all the time (a set of one page goes)
  if (names.length < 2) { if (s === 0) { delete dash.Pages; remapPages(0, () => null); } else removeSetNow(s); }
  shown[s] = clamp(shown[s] > i ? shown[s] - 1 : shown[s], 0, Math.max(0, countOf(s) - 1));
  setPicked(pickedList().filter(x => x < dash.Elements.length), sel < dash.Elements.length ? sel : -1);
  changed({ inspector: true });
}
// element e on page p of set s (null = every page of that set)
function putOnPage(e, s, p) {
  const l = visList(e).filter(c => { const pc = parsePage(c); return !pc || pc[0] !== s; });
  if (p !== null && p !== undefined) l.unshift(condOf(s, p));
  setVis(e, l);
}
// a new element goes on the page shown in the set picked under "New elements go on"
function placeNew(e) { if (addTo !== null && countOf(addTo) > 1) putOnPage(e, addTo, shownIn(addTo)); }

// the Pages tab
function renderPagesPane() {
  const box = $('pagesPane'); if (!box || !dash) return;
  const used = setsUsed(), one = used === 1 && pageCount() < 2;
  $('pageTabCount').textContent = one ? '' : String(Array.from({ length: used }, (_, s) => countOf(s) > 1 ? countOf(s) : 0).reduce((a, b) => a + b, 0));
  if (one) {
    box.innerHTML = `<div class="pg-intro">${icon('layers')}<b>One page</b><span>Pages let the driver flip part of the dash (a panel, a strip, the whole screen) with a wheel button, like a SimHub widget's screens.</span>
      <button class="btn primary" data-pg="add" data-s="0">${icon('plus')}Add a page</button></div>`;
    wirePagesPane(box); return;
  }
  const target = `<div class="f"><label>New elements</label><select class="in" id="addTo"><option value="">Go on every page</option>${flippingSets().map(s => `<option value="${s}" ${addTo === s ? 'selected' : ''}>Go on the page shown${hasSets() ? ' in ' + esc(setTitle(s)) : ''}</option>`).join('')}</select></div>`;
  const sets = Array.from({ length: used }, (_, s) => {
    const n = countOf(s), names = Array.from({ length: n }, (_, i) => pageName(i, s));
    const rows = n < 2 ? `<div class="note">One page: add one to make this part flip.</div>` : names.map((nm, i) => `<div class="pg-row ${i === shownIn(s) ? 'on' : ''}">
      <button class="pg-num" data-pg="go" data-s="${s}" data-i="${i}" title="Show this page">${i + 1}</button>
      <input class="in" data-pg="name" data-s="${s}" data-i="${i}" value="${esc(nm)}" spellcheck="false" title="The page's name (the settings page and SimHub's UsbDashPage property show it)">
      <button class="pg-n" data-pg="pick" data-s="${s}" data-i="${i}" title="Elements on this page only: click to select them">${dash.Elements.filter(e => pageIn(e, s) === i).length}</button>
      <button class="btn icon ghost" data-pg="up" data-s="${s}" data-i="${i}" title="Move up" ${i === 0 ? 'disabled' : ''}>${icon('up')}</button>
      <button class="btn icon ghost" data-pg="down" data-s="${s}" data-i="${i}" title="Move down" ${i === n - 1 ? 'disabled' : ''}>${icon('down')}</button>
      <button class="btn icon ghost" data-pg="del" data-s="${s}" data-i="${i}" title="Remove this page">${icon('trash')}</button></div>`).join('');
    return `<section class="pset">
      <header>${s === 0 ? `<b>${used > 1 ? 'Pages' : 'Pages'}</b>` : `<input class="in" data-pg="setname" data-s="${s}" value="${esc(setTitle(s))}" spellcheck="false" title="What this set of pages shows">`}
        <span class="pset-btn" title="The SimHub action / wheel button that flips only this set">Next page ${s + 1}</span>
        ${s > 0 ? `<button class="btn icon ghost" data-pg="delset" data-s="${s}" title="Remove this set of pages">${icon('trash')}</button>` : ''}</header>
      <div class="pages-ed">${rows}</div>
      <button class="btn small" data-pg="add" data-s="${s}">${icon('plus')}Add a page</button></section>`;
  }).join('');
  box.innerHTML = target + sets +
    (used < MAX_SETS ? `<button class="btn" data-pg="addset" style="width:100%;justify-content:center">${icon('plus')}Add a set of pages</button>` : '') +
    `<div class="note" style="margin-top:10px">Put elements on a page with <b>Page</b> in the inspector or a right-click (several selected at once too); elements on no page show on every page. Next page flips every set; a set flips alone with its own button. Keep a page to one area: a flip redraws only what changes.</div>`;
  wirePagesPane(box);
}
function wirePagesPane(root) {
  hydrateIcons(root);
  root.querySelectorAll('[data-pg]').forEach(b => {
    const s = Number(b.dataset.s || 0), i = Number(b.dataset.i), a = b.dataset.pg;
    if (a === 'name' || a === 'setname') {
      b.onchange = () => a === 'name' ? renamePage(s, i, b.value) : renameSet(s, b.value);
      b.onkeydown = ev => { if (ev.key === 'Enter') b.blur(); };
      return;
    }
    b.onclick = () => ({
      add: () => addPage(s), addset: addSet, delset: () => removeSet(s), go: () => setShown(s, i), up: () => movePage(s, i, -1), down: () => movePage(s, i, 1), del: () => deletePage(s, i),
      pick: () => { setShown(s, i); const l = dash.Elements.map((e, x) => pageIn(e, s) === i ? x : -1).filter(x => x >= 0); if (l.length) { setPicked(l); renderLayers(); renderInspector(); draw(); } },
    })[a]();
  });
  const at = root.querySelector('#addTo');
  if (at) at.onchange = () => { addTo = at.value === '' ? null : Number(at.value); updateAddTarget(); };
  updateAddTarget();
}
// the Add tab says where new elements go
function updateAddTarget() {
  const el = $('addTarget'); if (!el || !dash) return;
  const on = addTo !== null && countOf(addTo) > 1;
  el.hidden = flipCount() < 2;
  el.innerHTML = on ? `${icon('layers')}<span>New elements go on <b>${esc(pageName(shownIn(addTo), addTo))}</b>${hasSets() ? ' (' + esc(setTitle(addTo)) + ')' : ''}</span><button data-act2="pages">Change</button>`
    : `${icon('layers')}<span>New elements go on <b>every page</b></span><button data-act2="pages">Change</button>`;
  const b = el.querySelector('button'); if (b) b.onclick = () => switchTab('pages');
}

// ---------- overlays: the dash's pop-ups, warnings and screens that show on a condition ----------
let overlaysTimer = null;
function scheduleOverlays() { clearTimeout(overlaysTimer); overlaysTimer = setTimeout(loadOverlays, 500); }
async function loadOverlays() {
  if (!dash) return;
  try { overlays = await post('/api/overlays', dash); } catch (e) { overlays = []; }
  if (overlay >= overlays.length) overlay = -1;
  renderOverlayPick();
}
function overlayLabel(o) { return o ? `${o.name}` : ''; }
function renderOverlayPick() {
  const b = $('btnOverlay'); if (!b) return;
  b.hidden = !overlays.length;
  b.classList.toggle('on', overlay >= 0);
  $('overlayText').textContent = overlay >= 0 ? overlayLabel(overlays[overlay]) : `Overlays (${overlays.filter(o => o.major).length || overlays.length})`;
  b.title = overlay >= 0 ? 'Showing this overlay in the previews and on the wheel: click to pick another, or none' : 'Preview one of the dash\'s overlays (pop-ups, warnings, pit and flag screens): they only show on their condition';
}
function openOverlayPick() {
  const m = $('overlayMenu'); if (!m) return;
  const depth = o => { let d = 0; for (let x = o; x && x.parent >= 0 && d < 4; x = overlays[x.parent]) d++; return d; };
  m.innerHTML = `<div class="ov-head">Show an overlay<small>Previews and the wheel show it as if its condition held</small></div>`
    + `<button data-ov="-1" class="${overlay < 0 ? 'on' : ''}">${icon('eye')}<span>None: the dash as it drives</span></button><hr>`
    + (() => {
      // its condition, shortened, under the name ("when [Flag_Blue]")
      const when = o => { const c = o.conditions.filter(x => !overlays[o.parent] || !overlays[o.parent].conditions.includes(x)).map(x => x.replace(/^(ncalc|js):/, '').replace(/\s+/g, ' ')).join(' & '); return c.length > 70 ? c.slice(0, 67) + '…' : c; };
      const row = o => `<button data-ov="${o.index}" class="${overlay === o.index ? 'on' : ''}" style="padding-left:${12 + depth(o) * 16}px" title="${esc(o.conditions.join('  &  '))}">${icon(depth(o) ? 'cond' : o.major ? 'popup' : 'eye')}<span>${esc(o.name)}<small>${esc(when(o))}</small></span><kbd>${o.elements}</kbd></button>`;
      // (small items inside an overlay show with it: not listed on their own)
      const major = overlays.filter(o => o.major), minor = overlays.filter(o => !o.major && o.parent < 0);
      return (major.length ? `<div class="ov-sec">Overlays</div>` + major.map(row).join('') : '')
        + (minor.length ? `<div class="ov-sec">Small conditional items</div>` + minor.map(row).join('') : '');
    })();
  m.classList.add('open');
  m.querySelectorAll('[data-ov]').forEach(x => x.onclick = () => { m.classList.remove('open'); setOverlay(Number(x.dataset.ov)); });
}
function setOverlay(i) {
  overlay = i;
  renderOverlayPick(); renderLayers(); draw(); refreshExact(); scheduleWheel();
  if (i >= 0 && overlays[i] && overlays[i].first >= 0) flashAt(bounds(dash.Elements[overlays[i].first]));
}

function renderPages() {
  const seg = $('pageSeg'); if (!seg) return;
  const sets = dash ? flippingSets() : [];
  seg.hidden = !sets.length;
  if (!sets.length) { seg.innerHTML = ''; return; }
  // one set: its pages by name; more: a small group per set, pages by number (names in the tooltips)
  seg.innerHTML = sets.map(s => {
    const n = countOf(s), names = sets.length === 1;
    return `<div class="pgset">${names ? '' : `<small title="${esc(setTitle(s))}">${esc(setTitle(s))}</small>`}${Array.from({ length: n }, (_, i) =>
      `<button data-s="${s}" data-page="${i}" class="${i === shownIn(s) ? 'on' : ''}" title="Show ${esc(pageName(i, s))}${names ? ' (the driver flips pages with a wheel button)' : ' (' + esc(setTitle(s)) + ' flips on its own: Next page ' + (s + 1) + ')'}">${names ? esc(pageName(i, s)) : i + 1}</button>`).join('')}</div>`;
  }).join('');
  seg.querySelectorAll('[data-page]').forEach(b => b.onclick = () => setShown(Number(b.dataset.s), Number(b.dataset.page)));
}
function setShown(s, i) {
  shown[s] = clamp(i, 0, countOf(s) - 1);
  renderPages(); renderPagesPane(); renderLayers(); draw(); refreshExact(); scheduleWheel(); updateAddTarget();
}
// flip k as the wheel's Next page does from the first pages: page k of every set, each wrapping round its own pages
function showFlip(k) { for (let s = 0; s < MAX_SETS; s++) shown[s] = k % countOf(s); renderPages(); renderPagesPane(); renderLayers(); }
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
  let dim = 0;
  dash.Elements.forEach((e, i) => {
    if (e.Type === 'dim') { if (shownInPreview(e) && (overlay >= 0 || !isConditional(e))) dim = Math.max(dim, Math.min(95, e.Opacity ?? 50)); return; }
    if (e.Type === 'popup' ? i !== sel : !shownInPreview(e)) return;
    ctx.save(); drawElement(e); ctx.restore();
  });
  // a dim shown: the screen darker by its opacity (the wheel lowers its backlight)
  if (dim > 0) { ctx.fillStyle = `rgba(0,0,0,${dim / 100})`; ctx.fillRect(0, 0, W, H); }
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
  const { cells, width } = glyphCells(f, text, e.W);
  const x = e.Align === 'center' ? e.X + (e.W - width) / 2 : e.Align === 'right' ? e.X + e.W - width : e.X;
  ctx.save();
  ctx.beginPath(); ctx.rect(e.X, e.Y, e.W, e.H); ctx.clip();
  ctx.fillStyle = parseColor(colour || '#ffffff');
  drawGlyphs(ctx, f, cells, x, e.Y + (e.H - fh) / 2);
  ctx.restore();
}

function bounds(e) {
  if (!e) return null;
  if (e.Type === 'deltabar') { const xs = segX(e), x0 = Math.min(...xs), x1 = Math.max(...xs) + (e.SegmentWidth || 30); return { x: x0, y: e.Y || 0, w: x1 - x0, h: e.H || 0 }; }
  if (e.Type === 'dim') return { x: 0, y: 0, w: W, h: H }; // the whole screen
  return { x: e.X || 0, y: e.Y || 0, w: e.W || 0, h: e.H || 0 };
}

// ---------- overlay ----------
function place(el, r) { el.style.left = r.x * zoom + 'px'; el.style.top = r.y * zoom + 'px'; el.style.width = Math.max(1, r.w * zoom) + 'px'; el.style.height = Math.max(1, r.h * zoom) + 'px'; }
const unionOf = rs => { rs = rs.filter(Boolean); if (!rs.length) return null; const x0 = Math.min(...rs.map(r => r.x)), y0 = Math.min(...rs.map(r => r.y)); return { x: x0, y: y0, w: Math.max(...rs.map(r => r.x + r.w)) - x0, h: Math.max(...rs.map(r => r.y + r.h)) - y0 }; };
const pickedBounds = () => unionOf(pickedList().map(i => bounds(dash.Elements[i])));
let pickEls = [];
function placeOverlay() {
  const scr = $('screen');
  scr.style.width = W * zoom + 'px'; scr.style.height = H * zoom + 'px';
  place($('safe'), { x: pad.l, y: pad.t, w: W - pad.l, h: H - pad.t });
  const hb = $('hoverbox'), hv = !isPicked(hover) && dash && dash.Elements[hover];
  hb.classList.toggle('on', !!hv && view === 'edit');
  if (hv) place(hb, bounds(hv));
  // several selected: a thin frame round each, and the selection box (no handles) round them all
  const list = multi() && view === 'edit' ? pickedList() : [];
  while (pickEls.length < list.length) { const d = document.createElement('div'); d.className = 'pickbox'; $('overlay').appendChild(d); pickEls.push(d); }
  pickEls.forEach((d, k) => { d.classList.toggle('on', k < list.length); if (k < list.length) place(d, bounds(dash.Elements[list[k]])); });
  const sb = $('selbox'), s = cur();
  const was = sb.classList.contains('on');
  sb.classList.toggle('on', !!s && view === 'edit');
  if (!was && s) { sb.style.animation = 'none'; void sb.offsetWidth; sb.style.animation = ''; }
  sb.classList.toggle('nohandles', !!s && (s.Type === 'deltabar' || multi()));
  sb.classList.toggle('group', multi());
  if (s) place(sb, multi() ? pickedBounds() : bounds(s));
}
function flashAt(r) {
  if (!r) return;
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
const pickable = (e, i) => e.Type === 'popup' ? i === sel : (shownInPreview(e) || isPicked(i));
// every element under a point, front first (what a click there can select: the front one, then the ones behind)
function hitsAt(p) {
  if (!dash) return [];
  const out = [];
  for (let i = dash.Elements.length - 1; i >= 0; i--) {
    const e = dash.Elements[i];
    if (!pickable(e, i)) continue;
    const r = bounds(e);
    if (p.x >= r.x && p.x <= r.x + r.w && p.y >= r.y && p.y <= r.y + r.h) out.push(i);
  }
  // a dim covers the whole screen: behind everything else here
  return [...out.filter(i => dash.Elements[i].Type !== 'dim'), ...out.filter(i => dash.Elements[i].Type === 'dim')];
}
function hitTest(p) { const h = hitsAt(p); return h.length ? h[0] : -1; }

let drag = null;
screenEl.addEventListener('mousedown', ev => {
  if (!dash || view !== 'edit' || ev.button !== 0) return;
  hideMenus();
  const p = pos(ev), h = ev.target.dataset.h, s = cur();
  if (h && s && !multi()) { begin(); drag = { mode: 'resize', h, start: p, cx: ev.clientX, cy: ev.clientY, orig: { ...s }, ratio: (s.W || 1) / (s.H || 1) }; $('selbox').classList.add('dragging'); ev.preventDefault(); return; }
  const hits = hitsAt(p), add = ev.shiftKey || ev.ctrlKey || ev.metaKey;
  // Alt+click: straight to the element behind the selected one
  const hit = ev.altKey && hits.length > 1 && hits.includes(sel) ? hits[(hits.indexOf(sel) + 1) % hits.length] : hits.length ? hits[0] : -1;
  if (hit < 0) {
    // empty screen: drag a box round elements to select the ones inside it (Shift / Ctrl adds them to the selection)
    drag = { mode: 'marquee', start: p, cx: ev.clientX, cy: ev.clientY, base: add ? pickedList() : [] };
    ev.preventDefault(); return;
  }
  if (add) { select(hit, { toggle: true }); ev.preventDefault(); return; }
  // a click on what's already selected keeps the selection (to drag it all); the mouse coming up without a drag then
  // selects the element behind it (click again to go deeper), or just that one out of several
  const already = isPicked(hit);
  if (!already) select(hit);
  begin();
  const orig = {}; for (const i of pickedList()) { const e = dash.Elements[i]; orig[i] = { X: e.X || 0, Y: e.Y || 0, SegmentX: e.SegmentX ? [...e.SegmentX] : null }; }
  drag = { mode: 'move', start: p, cx: ev.clientX, cy: ev.clientY, orig, r0: pickedBounds(), already, hit, hits };
  $('selbox').classList.add('dragging');
  ev.preventDefault();
});
screenEl.addEventListener('mousemove', ev => {
  const p = pos(ev);
  $('coords').textContent = `${Math.round(p.x)} , ${Math.round(p.y)}`;
  if (drag || view !== 'edit') return;
  const h = hitTest(p);
  if (h !== hover) { hover = h; placeOverlay(); highlightLayer(h); }
  screenEl.style.cursor = h >= 0 ? 'move' : 'default';
  screenEl.title = h >= 0 && hitsAt(p).length > 1 ? 'Click again (or Alt+click) to select what\'s behind; right-click lists everything here' : '';
});
screenEl.addEventListener('mouseleave', () => { $('coords').textContent = '— , —'; if (hover !== -1) { hover = -1; placeOverlay(); highlightLayer(-1); } });
screenEl.addEventListener('dblclick', ev => {
  const e = cur(); if (!e || multi()) return;
  const inp = document.querySelector(e.Type === 'label' ? '#inspector [data-k="Text"]' : e.Type === 'value' ? '#inspector .bindbtn' : '#inspector .insp-head input');
  if (inp) { inp.focus(); if (inp.select) inp.select(); if (inp.click && inp.classList.contains('bindbtn')) inp.click(); }
});
screenEl.addEventListener('contextmenu', ev => {
  ev.preventDefault(); if (!dash || view !== 'edit') return;
  const p = pos(ev), hits = hitsAt(p);
  if (hits.length && !isPicked(hits[0]) && !hits.some(isPicked)) select(hits[0]);
  openContext(ev.clientX, ev.clientY, hits);
});

function snapLines() {
  const xs = [0, W, W / 2, pad.l, pad.l + (W - pad.l) / 2], ys = [0, H, H / 2, pad.t, pad.t + (H - pad.t) / 2];
  dash.Elements.forEach((e, i) => {
    if (isPicked(i) || e.Type === 'popup' || e.Type === 'dim' || !shownInPreview(e)) return;
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
// a box drawn round elements takes those wholly inside it (not the big backgrounds it only touches)
const contains = (a, b) => b.x >= a.x && b.y >= a.y && b.x + b.w <= a.x + a.w && b.y + b.h <= a.y + a.h;

window.addEventListener('mousemove', ev => {
  if (!drag || !dash) return;
  const p = pos(ev);
  // a click isn't a drag: nothing moves (or gets an undo step) until the mouse has gone 3 screen px
  if (!drag.moved && Math.hypot(ev.clientX - drag.cx, ev.clientY - drag.cy) < 3) return;
  drag.moved = true;
  if (drag.mode === 'marquee') {
    const r = { x: Math.min(p.x, drag.start.x), y: Math.min(p.y, drag.start.y), w: Math.abs(p.x - drag.start.x), h: Math.abs(p.y - drag.start.y) };
    if (!drag.box) { drag.box = document.createElement('div'); drag.box.className = 'marquee'; $('overlay').appendChild(drag.box); }
    place(drag.box, r);
    const inside = dash.Elements.map((e, i) => i).filter(i => { const e = dash.Elements[i]; return e.Type !== 'dim' && e.Type !== 'popup' && shownInPreview(e) && contains(r, bounds(e)); });
    setPicked([...new Set([...drag.base, ...inside])]);
    placeOverlay(); renderLayers();
    return;
  }
  let dx = Math.round(p.x - drag.start.x), dy = Math.round(p.y - drag.start.y);
  const tol = 6 / zoom, lines = ev.altKey ? { xs: [], ys: [] } : snapLines();
  const gx = [], gy = [];
  if (drag.mode === 'move') {
    const r0 = drag.r0;
    if (ev.shiftKey) { if (Math.abs(dx) > Math.abs(dy)) dy = 0; else dx = 0; }
    const sx = snap1([r0.x + dx, r0.x + dx + r0.w / 2, r0.x + dx + r0.w], lines.xs, tol);
    const sy = snap1([r0.y + dy, r0.y + dy + r0.h / 2, r0.y + dy + r0.h], lines.ys, tol);
    if (sx) { dx += Math.round(sx.d); gx.push(sx.line); }
    if (sy) { dy += Math.round(sy.d); gy.push(sy.line); }
    for (const [i, o] of Object.entries(drag.orig)) {
      const e = dash.Elements[i]; if (!e) continue;
      e.X = o.X + dx; e.Y = o.Y + dy;
      if (o.SegmentX) e.SegmentX = o.SegmentX.map(v => v + dx);
    }
  } else {
    const e = cur(); if (!e) return;
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
  const r = multi() ? pickedBounds() : bounds(cur());
  $('selsize').textContent = drag.mode === 'move' ? `${r.x}, ${r.y}` : `${r.w} × ${r.h}`;
  draw(); updateGeometry();
});
window.addEventListener('mouseup', () => {
  if (!drag) return;
  const d = drag; drag = null;
  $('selbox').classList.remove('dragging'); showGuides([], []);
  if (d.mode === 'marquee') {
    if (d.box) d.box.remove();
    if (!d.moved) { if (!d.base.length) select(-1); return; }
    renderLayers(); renderInspector(); draw();
    return;
  }
  if (d.moved) { changed({ keepLayers: true, inspector: multi() }); updateUndo(); return; }
  undoStack.pop(); updateUndo();
  if (d.mode !== 'move' || !d.already) return;
  // a click on what was already selected: one of several -> just that one; the only one -> the one behind it
  if (multi()) select(d.hit, { force: true });
  else if (d.hits.length > 1) {
    const next = d.hits[(d.hits.indexOf(sel) + 1) % d.hits.length];
    select(next, { flash: true });
    toast(`${nameOf(dash.Elements[next])}${d.hits.indexOf(next) === 0 ? ' (the front one again)' : ': behind'}`, 'ok', 1200);
  }
});

// ---------- selection & editing ----------
// select(i): just that one (-1 = nothing); {toggle}: add it to the selection or take it out; {range}: from the last
// picked to it, in the Layers order
function select(i, opt = {}) {
  if (opt.toggle && i >= 0) {
    if (isPicked(i)) { picked.delete(i); if (sel === i) sel = picked.size ? [...picked][picked.size - 1] : -1; }
    else { picked.add(i); sel = i; }
  } else if (opt.range && i >= 0 && sel >= 0) {
    const a = Math.min(sel, i), b = Math.max(sel, i), vis = new Set(layerOrder());
    setPicked([...pickedList(), ...Array.from({ length: b - a + 1 }, (_, k) => a + k).filter(k => vis.has(k))], i);
  } else {
    if (i === sel && picked.size === (i >= 0 ? 1 : 0) && !opt.force) return;
    selOnly(i);
  }
  renderLayers(); renderInspector(); draw();
  if (i >= 0 && opt.flash) flashAt(bounds(dash.Elements[i]));
}
function selectAll() {
  const l = dash.Elements.map((e, i) => i).filter(i => { const e = dash.Elements[i]; return e.Type !== 'popup' && shownInPreview(e); });
  setPicked(l, l.includes(sel) ? sel : undefined); renderLayers(); renderInspector(); draw();
}
function uniqueName(base) { let n = 1; const names = new Set(dash.Elements.map(e => e.Name)); while (names.has(base + n)) n++; return base + n; }

function addElement(type, at) {
  if (type === 'image') { pendingImageAt = at; $('imgfile').click(); return; }
  const d = JSON.parse(JSON.stringify(DEFAULTS[type]));
  const w = type === 'deltabar' ? (d.Segments * 2 - 1) * d.Pitch + d.SegmentWidth : d.W, h = d.H;
  const x = Math.round(clamp((at ? at.x : W / 2) - w / 2, 0, W - w)), y = Math.round(clamp((at ? at.y : H / 2) - h / 2, 0, H - h));
  begin();
  const e = { Type: type, Name: uniqueName(type), X: x, Y: y, ...d };
  placeNew(e);
  dash.Elements.push(e);
  selOnly(dash.Elements.length - 1);
  changed({ inspector: true });
  flashAt(bounds(e));
  switchTab('layers', true);
}
let pendingImageAt = null;
// the mime type a stored picture needs, from its first bytes (base64): PNG, JPEG or a GIF someone put in by hand
function imageMime(b64) { return b64.startsWith('/9j/') ? 'image/jpeg' : b64.startsWith('R0lGOD') ? 'image/gif' : 'image/png'; }
// A picture file as the dash keeps it: its first frame (a GIF's, an animated one's too: the screen draws stills), no bigger
// than the screen, as a PNG, or as a JPEG when that's much smaller (photos) and nothing in it is see-through.
async function normalisePicture(file) {
  let src;
  try { src = await createImageBitmap(file); }
  catch (_) { src = await new Promise((ok, no) => { const i = new Image(); i.onload = () => ok(i); i.onerror = () => no(new Error('not a picture the browser can read')); i.src = URL.createObjectURL(file); }); }
  const sw = src.width || src.naturalWidth, sh = src.height || src.naturalHeight;
  const k = Math.min(1, W / sw, H / sh), w = Math.max(1, Math.round(sw * k)), h = Math.max(1, Math.round(sh * k));
  const c = document.createElement('canvas'); c.width = w; c.height = h;
  const g = c.getContext('2d'); g.imageSmoothingQuality = 'high'; g.drawImage(src, 0, 0, w, h);
  const px = g.getImageData(0, 0, w, h).data; let seeThrough = false;
  for (let i = 3; i < px.length; i += 4) if (px[i] < 250) { seeThrough = true; break; }
  const b64 = u => u.split(',')[1];
  let out = b64(c.toDataURL('image/png'));
  if (!seeThrough) { const j = b64(c.toDataURL('image/jpeg', 0.92)); if (j.length * 2 < out.length) out = j; }
  return { b64: out, w, h, scaled: k < 1, gif: file.type === 'image/gif' };
}
async function addImage(file) {
  let pic;
  try { pic = await normalisePicture(file); }
  catch (err) { toast("Couldn't use that picture: " + err.message, 'err'); return; }
  const img = new Image();
  img.onload = () => {
    begin();
    const name = file.name.replace(/\.[^.]+$/, '') + '@' + pic.w + 'x' + pic.h;
    dash.Images[name] = pic.b64; images[name] = img;
    const k = Math.min(1, 300 / pic.w, 200 / pic.h), w = Math.round(pic.w * k), h = Math.round(pic.h * k);
    const at = pendingImageAt || { x: W / 2, y: H / 2 };
    const e = { Type: 'image', Name: uniqueName('image'), Image: name, X: Math.round(clamp(at.x - w / 2, 0, W - w)), Y: Math.round(clamp(at.y - h / 2, 0, H - h)), W: w, H: h, MaxColors: 6, Block: 1 };
    placeNew(e);
    dash.Elements.push(e); selOnly(dash.Elements.length - 1);
    changed({ inspector: true }); flashAt(bounds(e));
    if (pic.gif) toast('A GIF gives its first frame: the screen draws stills', 'ok', 3500);
  };
  img.src = 'data:' + imageMime(pic.b64) + ';base64,' + pic.b64;
}
// copies of elements, each just above its original (or all on top, for a paste), offset by `off` px, selected
function insertCopies(list, off, atEnd) {
  const out = [];
  for (const src of list) {
    const c = JSON.parse(JSON.stringify(src));
    c.Name = uniqueName((src.Name || src.Type).replace(/\d+$/, '')); c.X = (c.X || 0) + off; c.Y = (c.Y || 0) + off;
    if (c.SegmentX) c.SegmentX = c.SegmentX.map(v => v + off);
    out.push(c);
  }
  if (atEnd) { const at = dash.Elements.length; dash.Elements.push(...out); return out.map((_, k) => at + k); }
  // duplicates: right after the last picked one, in their order
  const after = Math.max(...pickedList()) + 1;
  dash.Elements.splice(after, 0, ...out);
  return out.map((_, k) => after + k);
}
function duplicate() {
  const l = pickedList(); if (!l.length) return;
  begin();
  const idx = insertCopies(l.map(i => dash.Elements[i]), 12, false);
  setPicked(idx, idx[idx.length - 1]);
  changed({ inspector: true }); flashAt(pickedBounds());
}
function removeSel() {
  const l = pickedList(); if (!l.length) return;
  begin();
  const name = l.length === 1 ? nameOf(dash.Elements[l[0]]) : l.length + ' elements';
  const drop = new Set(l), first = l[0];
  dash.Elements = dash.Elements.filter((e, i) => !drop.has(i));
  selOnly(Math.min(first, dash.Elements.length - 1));
  changed({ inspector: true });
  toast(`Deleted ${name}`, 'ok', 1800);
}
function moveTo(from, to) {
  if (from === to || from < 0 || to < 0 || to >= dash.Elements.length) return;
  begin();
  const [e] = dash.Elements.splice(from, 1); dash.Elements.splice(to, 0, e);
  selOnly(to); changed({ inspector: true });
}
// the selection to the front (end of the list) or back, keeping its own order
function stackPicked(front) {
  const l = pickedList(); if (!l.length) return;
  if (l.length === 1) { moveTo(l[0], front ? dash.Elements.length - 1 : 0); return; }
  begin();
  const set = new Set(l), mine = l.map(i => dash.Elements[i]), rest = dash.Elements.filter((e, i) => !set.has(i)), primary = dash.Elements[sel];
  dash.Elements = front ? [...rest, ...mine] : [...mine, ...rest];
  setPicked(mine.map(e => dash.Elements.indexOf(e)), dash.Elements.indexOf(primary));
  changed({ inspector: true });
}
function copySel() {
  const l = pickedList(); if (!l.length) return;
  clipboard = JSON.stringify(l.map(i => dash.Elements[i]));
  toast(l.length === 1 ? 'Copied ' + nameOf(dash.Elements[l[0]]) : `Copied ${l.length} elements`, 'ok', 1400);
}
function paste() {
  if (!clipboard) return;
  let list = JSON.parse(clipboard); if (!Array.isArray(list)) list = [list];
  begin();
  const idx = insertCopies(list, 16, true);
  idx.forEach(i => placeNew(dash.Elements[i]));
  setPicked(idx, idx[idx.length - 1]);
  changed({ inspector: true }); flashAt(pickedBounds());
}
function shiftEl(e, dx, dy) { e.X = (e.X || 0) + dx; e.Y = (e.Y || 0) + dy; if (e.SegmentX) e.SegmentX = e.SegmentX.map(v => v + dx); }
// one element: to the wheel's visible area; several: to each other (the box round them all)
function align(kind) {
  const l = pickedList(); if (!l.length) return;
  begin();
  const box = l.length > 1 ? pickedBounds() : { x: pad.l, y: pad.t, w: W - pad.l, h: H - pad.t };
  for (const i of l) {
    const e = dash.Elements[i], r = bounds(e);
    let dx = 0, dy = 0;
    if (kind === 'l') dx = box.x - r.x; if (kind === 'ch') dx = Math.round(box.x + (box.w - r.w) / 2) - r.x; if (kind === 'r') dx = box.x + box.w - r.w - r.x;
    if (kind === 't') dy = box.y - r.y; if (kind === 'cv') dy = Math.round(box.y + (box.h - r.h) / 2) - r.y; if (kind === 'b') dy = box.y + box.h - r.h - r.y;
    shiftEl(e, dx, dy);
  }
  changed({ keepLayers: true }); updateGeometry(); placeOverlay();
}
// three or more: the same gap between each, across (h) or down (v)
function distribute(axis) {
  const l = pickedList(); if (l.length < 3) return;
  begin();
  const k = axis === 'h' ? ['x', 'w'] : ['y', 'h'];
  const items = l.map(i => ({ e: dash.Elements[i], r: bounds(dash.Elements[i]) })).sort((a, b) => a.r[k[0]] - b.r[k[0]]);
  const start = items[0].r[k[0]], end = Math.max(...items.map(t => t.r[k[0]] + t.r[k[1]]));
  const gap = (end - start - items.reduce((a, t) => a + t.r[k[1]], 0)) / (items.length - 1);
  let at = start;
  for (const t of items) { const d = Math.round(at) - t.r[k[0]]; shiftEl(t.e, axis === 'h' ? d : 0, axis === 'h' ? 0 : d); at += t.r[k[1]] + gap; }
  changed({ keepLayers: true }); placeOverlay();
}

// ---------- keyboard ----------
window.addEventListener('keydown', ev => {
  const typing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement.tagName);
  const k = ev.key.toLowerCase();
  if (ev.ctrlKey && k === 's') { ev.preventDefault(); save(); return; }
  if (ev.ctrlKey && k === 'f') { ev.preventDefault(); switchTab('layers', true); const s = $('layerSearch'); s.focus(); s.select(); return; }
  if (ev.key === 'Escape') { if (closeTop()) return; if (typing && document.activeElement.id === 'layerSearch') { document.activeElement.blur(); return; } if (!typing && sel >= 0) select(-1); return; }
  if (typing) return;
  if (ev.ctrlKey && k === 'z') { ev.preventDefault(); ev.shiftKey ? doRedo() : doUndo(); return; }
  if (ev.ctrlKey && k === 'y') { ev.preventDefault(); doRedo(); return; }
  if (ev.ctrlKey && k === 'a') { ev.preventDefault(); selectAll(); return; }
  if (ev.ctrlKey && k === 'c') { copySel(); return; }
  if (ev.ctrlKey && k === 'v') { paste(); return; }
  if (ev.ctrlKey && k === 'd') { ev.preventDefault(); duplicate(); return; }
  if (ev.key === '?') { openModal('keysModal'); return; }
  if (ev.key === '+' || ev.key === '=') { setZoom(zoom + .1); return; }
  if (ev.key === '-') { setZoom(zoom - .1); return; }
  if (ev.key === '0') { fitZoom(); return; }
  if (ev.key === '1') { setView('edit'); return; } if (ev.key === '2') { setView('exact'); return; } if (ev.key === '3') { setView('demo'); return; }
  if (!picked.size) return;
  if (ev.key === 'Delete' || ev.key === 'Backspace') { removeSel(); return; }
  if (!multi()) { if (ev.key === ']') { moveTo(sel, sel + 1); return; } if (ev.key === '[') { moveTo(sel, sel - 1); return; } }
  else { if (ev.key === ']') { stackPicked(true); return; } if (ev.key === '[') { stackPicked(false); return; } }
  const step = ev.shiftKey ? 10 : 1;
  const mv = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[ev.key];
  if (mv) {
    ev.preventDefault(); begin();
    for (const i of pickedList()) shiftEl(dash.Elements[i], mv[0], mv[1]);
    changed({ keepLayers: true, inspector: multi() }); updateGeometry();
  }
});
const KEYS = [['Move', '← ↑ → ↓'], ['Move 10 px', 'Shift + arrows'], ['Select several', 'Shift / Ctrl + click, or drag a box'], ['Select all on screen', 'Ctrl A'],
  ['Select what\'s behind', 'Click again, or Alt + click'], ['Search layers', 'Ctrl F'], ['Duplicate', 'Ctrl D'], ['Copy / paste', 'Ctrl C / V'], ['Delete', 'Del'], ['Undo / redo', 'Ctrl Z / Y'],
  ['Forward / back', '] / ['], ['Deselect / close', 'Esc'], ['Save', 'Ctrl S'], ['Zoom', '+ / - / 0'], ['Edit / Exact / Demo', '1 / 2 / 3'], ['These shortcuts', '?']];

// ---------- context menu ----------
// the page items: for each set that flips, "every page" and each page; ticks show where the selection is
function pageMenu(l) {
  const sets = flippingSets(); if (!sets.length) return '';
  return sets.map(s => {
    const at = new Set(l.map(i => pageIn(dash.Elements[i], s)));
    const on = v => at.size === 1 && at.has(v) ? 'on' : '';
    return (hasSets() ? `<div class="ov-sec">${esc(setTitle(s))}</div>` : '')
      + `<button data-c="pg:${s}:all" class="${on(null)}">${icon('layers')}On every page</button>`
      + Array.from({ length: countOf(s) }, (_, i) => `<button data-c="pg:${s}:${i}" class="${on(i)}">${icon('layers')}Only on ${esc(pageName(i, s))}</button>`).join('');
  }).join('') + '<hr>';
}
function openContext(x, y, hits = []) {
  const m = $('ctxMenu'), l = pickedList(), n = l.length;
  const here = hits.length > 1 ? `<div class="ov-sec">Here, front to back</div>` + hits.slice(0, 8).map(i => `<button data-c="sel:${i}" class="${isPicked(i) ? 'on' : ''}">${icon((TYPES[dash.Elements[i].Type] || {}).icon || 'rect')}<span class="ctx-nm">${esc(nameOf(dash.Elements[i]))}</span></button>`).join('') + '<hr>' : '';
  m.innerHTML = here + (n ? (n > 1 ? `<div class="ov-sec">${n} elements selected</div>` : '') +
    `<button data-c="dup">${icon('copy')}Duplicate<kbd>Ctrl D</kbd></button><button data-c="copy">${icon('copy')}Copy<kbd>Ctrl C</kbd></button>` +
    (clipboard ? `<button data-c="paste">${icon('plus')}Paste<kbd>Ctrl V</kbd></button>` : '') +
    `<hr><button data-c="front">${icon('front')}Bring to front</button><button data-c="back">${icon('back')}Send to back</button><hr>` +
    pageMenu(l) +
    `<button data-c="del" class="danger">${icon('trash')}Delete${n > 1 ? ' ' + n : ''}<kbd>Del</kbd></button>`
    : `<button data-c="paste" ${clipboard ? '' : 'disabled'}>${icon('plus')}Paste<kbd>Ctrl V</kbd></button><button data-c="all">${icon('grid')}Select all<kbd>Ctrl A</kbd></button>`);
  m.style.left = Math.min(x, innerWidth - 240) + 'px'; m.style.top = Math.max(8, Math.min(y, innerHeight - m.scrollHeight - 12)) + 'px';
  m.style.transformOrigin = 'top left';
  m.classList.add('open');
  m.querySelectorAll('button').forEach(b => b.onclick = () => {
    hideMenus();
    const c = b.dataset.c;
    if (c.startsWith('sel:')) { select(Number(c.slice(4)), { flash: true, force: true }); return; }
    if (c.startsWith('pg:')) {
      const [, s, v] = c.split(':'), set = Number(s), p = v === 'all' ? null : Number(v);
      begin(); for (const i of pickedList()) putOnPage(dash.Elements[i], set, p);
      if (p !== null) shown[set] = p;
      changed({ inspector: true }); refreshExact(); return;
    }
    ({ dup: duplicate, copy: copySel, paste, del: removeSel, all: selectAll, front: () => stackPicked(true), back: () => stackPicked(false) })[c]();
  });
}
function hideMenus() { $('ctxMenu').classList.remove('open'); $('moreMenu').classList.remove('open'); const om = $('overlayMenu'); if (om) om.classList.remove('open'); closePop(); }
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
let layerQuery = '', layerShownOnly = false;
// what a search looks through: name, type, text, data, conditions, the pages it's on
const layerText = e => [nameOf(e), e.Name, (TYPES[e.Type] || {}).name, e.Type, e.Text, e.Bind, e.ColorBind, e.Image, ...visList(e), pageLabel(e)].filter(Boolean).join('\n').toLowerCase();
function layerMatches(e) {
  if (layerShownOnly && !shownInPreview(e)) return false;
  if (!layerQuery) return true;
  const t = layerText(e);
  return layerQuery.toLowerCase().split(/\s+/).filter(Boolean).every(w => t.includes(w));
}
// the elements the Layers list shows, top of the list (drawn last, in front) first
const layerOrder = () => !dash ? [] : dash.Elements.map((e, i) => i).reverse().filter(i => layerMatches(dash.Elements[i]));
const mark = (text, q) => {
  const t = esc(text); if (!q) return t;
  const words = q.split(/\s+/).filter(Boolean).map(w => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
  return words.length ? t.replace(new RegExp('(' + words.map(esc).join('|') + ')', 'gi'), '<mark>$1</mark>') : t;
};
function renderLayers() {
  const ul = $('layers'); if (!dash) return;
  $('layerCount').textContent = dash.Elements.length;
  const order = layerOrder(), filtered = !!layerQuery || layerShownOnly;
  const info = $('layerInfo');
  if (info) {
    const n = picked.size;
    info.innerHTML = (filtered ? `<span>${order.length} of ${dash.Elements.length}</span>` : `<span>Top = in front · drag to reorder</span>`) +
      (filtered && order.length ? `<button class="linkbtn" data-li="all">Select ${order.length === 1 ? 'it' : 'all ' + order.length}</button>` : '') +
      (n > 1 ? `<button class="linkbtn" data-li="none">${n} selected · clear</button>` : '');
    info.querySelectorAll('[data-li]').forEach(b => b.onclick = () => { if (b.dataset.li === 'all') setPicked(order, order[0]); else setPicked([]); renderLayers(); renderInspector(); draw(); });
  }
  if (!dash.Elements.length) { ul.innerHTML = `<div class="empty">${icon('layers')}Nothing yet. Add elements from the Add tab.</div>`; return; }
  if (!order.length) { ul.innerHTML = `<div class="empty">${icon('search')}Nothing matches${layerQuery ? ` "${esc(layerQuery)}"` : ''}.${layerShownOnly ? '<br>Only what the screen shows now is listed.' : ''}</div>`; return; }
  ul.innerHTML = order.map(i => {
    const e = dash.Elements[i], iss = issuesByName[e.Name];
    const pl = pageLabel(e);
    return `<li class="layer ${isPicked(i) ? 'sel' : ''} ${i === sel && multi() ? 'primary' : ''} ${shownInPreview(e) ? '' : 'off'}" draggable="true" data-i="${i}">` +
      `<span class="grip">${icon('grip')}</span><span class="ti">${icon((TYPES[e.Type] || {}).icon || 'rect')}</span>` +
      `<span class="nm"><div>${mark(nameOf(e), layerQuery)}</div><small>${esc((TYPES[e.Type] || { name: e.Type }).name)}${pl ? ' · ' + esc(pl) : ''}${otherConds(e).length ? ' · conditional' : ''}</small></span>` +
      (iss ? `<span class="flag ${iss.level === 'error' ? 'err' : ''}" title="${esc(iss.text)}">${icon('warn')}</span>` : '') +
      (otherConds(e).length ? `<button class="eye" data-eye="${i}" title="${e.PreviewVisible === false ? 'Hidden in previews: click to show' : 'Shown in previews: click to hide'}">${icon(e.PreviewVisible === false ? 'eyeoff' : 'eye')}</button>` : '') +
      `</li>`;
  }).join('');
  ul.querySelectorAll('.layer').forEach(li => {
    const i = Number(li.dataset.i);
    li.onclick = ev => {
      if (ev.target.closest('.eye')) return;
      if (ev.shiftKey) select(i, { range: true }); else if (ev.ctrlKey || ev.metaKey) select(i, { toggle: true }); else select(i, { flash: true, force: multi() });
    };
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
  const s = ul.querySelector('.layer.primary') || ul.querySelector('.layer.sel'); if (s) s.scrollIntoView({ block: 'nearest' });
}
function highlightLayer(i) { document.querySelectorAll('.layer').forEach(li => li.style.background = Number(li.dataset.i) === i && !isPicked(i) ? 'var(--raised)' : ''); }

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
  if (multi()) { renderMultiInspector(p); return; }
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
    t += f('Font', fontBtn(e), true);
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
      f('Colours', rangeIn('MaxColors', e.MaxColors ?? 8, 2, 32)) + f('Pixel size', rangeIn('Block', e.Block ?? 1, 1, 12, ' px')) +
      `<div class="note">A screen with picture memory draws the picture as it is, in every colour: turn on "My screen has picture memory" on the plugin's Wheel tab (its Test button checks it), and the dash must use it. Without, the wheel draws pictures with rectangles, in at most these colours and squares of this size: fewer colours and bigger pixels draw faster. A GIF gives its first frame.</div>`; break;
    case 'bar': st += f('Fill', colorIn('Color', e.Color)) + f('Empty', colorIn('Fill', e.Fill, true)); break;
    case 'deltabar': st += f('Slower', colorIn('PositiveColor', e.PositiveColor)) + f('Faster', colorIn('NegativeColor', e.NegativeColor)) + f('Off', colorIn('SegmentColor', e.SegmentColor)) +
      f('Segments', rangeIn('Segments', e.Segments || 7, 2, 15)) + f('Seg. width', rangeIn('SegmentWidth', e.SegmentWidth || 30, 2, 80, ' px')) + f('Spacing', rangeIn('Pitch', e.Pitch || 40, 4, 100, ' px')); break;
    case 'dim': st += f('Darker by', rangeIn('Opacity', e.Opacity ?? 50, 0, 95, '%')) + `<div class="note">While its conditions hold (Show when), the whole screen is this much darker: the wheel lowers its backlight, so nothing is redrawn. Not drawn on the canvas.</div>`; break;
    case 'popup': st += f('Text', colorIn('Color', e.Color)) + f('Label font', fontBtn(e, 'Font'), true) + f('Value font', fontBtn(e, 'ValueFont'), true) + f('Corners', rangeIn('Radius', e.Radius || 0, 0, 60, ' px')) +
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
  let c = '';
  c += pageFields([e]);
  c += `<div class="chips" id="condChips">${conds.map((b, i) => `<span class="chipb on" title="${esc(bindDesc(b))}">${esc(b)}<span class="x" data-rmcond="${i}">${icon('x')}</span></span>`).join('')}<button class="chipb" data-pop="bind" data-k="__cond">${icon('plus')}Add</button></div>`;
  c += `<div class="note">${conds.length ? 'Shown only while all of these are true (a number other than 0, or some text).' : 'Always shown. Add a condition to show it only sometimes (a warning, a pit screen).'}</div>`;
  if (otherConds(e).length) c += switchIn('__preview', e.PreviewVisible !== false, 'Show it in the designer and previews');
  html += card('cond', 'Show when', 'cond', c, !otherConds(e).length && !flippingSets().length);

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
  html += card('pages', 'Pages', 'layers', (flipCount() < 2 ? `<div class="note">One page.</div>` : flippingSets().map(s => f(setTitle(s), `<span class="note" style="color:var(--text2)">${countOf(s)} pages: ${esc(Array.from({ length: countOf(s) }, (_, i) => pageName(i, s)).join(', '))}</span>`)).join(''))
    + `<button class="btn" id="goPages">${icon('layers')}${flipCount() < 2 ? 'Add pages' : 'Edit pages'}</button>`, flipCount() < 2);
  html += card('padding', 'Wheel padding', 'fit',
    f('From left', `<div class="range"><input type="range" id="padL" min="0" max="40" value="${pad.l}"><output>${pad.l} px</output></div>`) +
    f('From top', `<div class="range"><input type="range" id="padT" min="0" max="40" value="${pad.t}"><output>${pad.t} px</output></div>`) +
    `<div class="note">The wheel's bezel hides the screen's edges; the dashed frame shows what's left. Match the plugin's setting (Dashes tab).</div>`);
  html += `<div class="empty" style="padding:18px">${icon('sparkle')}Select an element on the screen or in Layers to change it.</div>`;
  p.innerHTML = html;
  wireInspector(p, dash);
  $('goPages').onclick = () => switchTab('pages');
  const upd = () => { pad = { l: Number($('padL').value), t: Number($('padT').value) }; $('padL').nextElementSibling.textContent = pad.l + ' px'; $('padT').nextElementSibling.textContent = pad.t + ' px'; localStorage.setItem('fxdash-pad', JSON.stringify(pad)); draw(); scheduleCheck(); scheduleWheel(); };
  $('padL').oninput = upd; $('padT').oninput = upd;
  if (lastCheck) $('statTime').textContent = lastCheck.cost.StaticSeconds.toFixed(1) + ' s';
}

// Page: one picker per set of pages that flips (for one element or several: "(mixed)" when they differ)
function pageFields(list) {
  return flippingSets().map(s => {
    const at = new Set(list.map(e => pageIn(e, s))), v = at.size === 1 ? [...at][0] : undefined;
    return f(hasSets() ? setTitle(s) : 'Page', `<select class="in" data-pageset="${s}">${v === undefined ? '<option value="mixed" selected>(mixed)</option>' : ''}<option value="" ${v === null ? 'selected' : ''}>Every page</option>${Array.from({ length: countOf(s) }, (_, i) => `<option value="${i}" ${v === i ? 'selected' : ''}>${esc(pageName(i, s))}</option>`).join('')}</select>`);
  }).join('');
}
function wirePageFields(root, list) {
  root.querySelectorAll('[data-pageset]').forEach(sl => sl.onchange = () => {
    if (sl.value === 'mixed') return;
    const s = Number(sl.dataset.pageset), p = sl.value === '' ? null : Number(sl.value);
    begin(); for (const e of list) putOnPage(e, s, p);
    if (p !== null) shown[s] = p;
    changed({ inspector: true }); refreshExact(); scheduleWheel();
  });
}
// several elements selected: what they share
function renderMultiInspector(p) {
  const l = pickedList(), els = l.map(i => dash.Elements[i]), r = pickedBounds();
  const types = [...new Set(els.map(e => (TYPES[e.Type] || { name: e.Type }).name))];
  let html = `<div class="insp-head"><div class="ti">${icon('layers')}</div><div style="flex:1;min-width:0"><b style="font:600 15px var(--display)">${l.length} elements</b><small>${esc(types.join(', '))}</small></div></div>`;
  html += `<div class="insp-actions"><button class="btn" data-a="dup" title="Duplicate (Ctrl D)">${icon('copy')}Duplicate</button><button class="btn" data-a="front" title="Bring to front (])">${icon('up')}</button><button class="btn" data-a="back" title="Send to back ([)">${icon('down')}</button><button class="btn" data-a="del" title="Delete (Del)">${icon('trash')}</button></div>`;
  let lay = `<div class="num4" style="grid-template-columns:repeat(2,1fr)">${numIn('gX', r.x, 'X')}${numIn('gY', r.y, 'Y')}</div>`;
  lay += `<div class="note">The box round them all: ${r.w} × ${r.h}. Drag them on the screen or use the arrow keys.</div>`;
  lay += `<div class="row2"><div class="icongroup" style="flex:1">${[['l', 'alignL', 'left edges'], ['ch', 'alignCH', 'centres across'], ['r', 'alignR', 'right edges']].map(([a, ic, t]) => `<button data-align="${a}" title="Line up their ${t}">${icon(ic)}</button>`).join('')}</div>` +
    `<div class="icongroup" style="flex:1">${[['t', 'alignT', 'tops'], ['cv', 'alignCV', 'middles'], ['b', 'alignB', 'bottoms']].map(([a, ic, t]) => `<button data-align="${a}" title="Line up their ${t}">${icon(ic)}</button>`).join('')}</div></div>`;
  if (l.length > 2) lay += `<div class="row2"><button class="btn" data-dist="h" style="flex:1">Space evenly across</button><button class="btn" data-dist="v" style="flex:1">Space evenly down</button></div>`;
  html += card('mlayout', 'Position & alignment', 'fit', lay);
  const texts = els.filter(e => e.Type === 'label' || e.Type === 'value');
  if (texts.length) {
    const fontsAt = new Set(texts.map(e => e.Font ?? 14)), cols = new Set(texts.map(e => e.Color));
    let t = f('Font', `<button class="fontbtn" id="mFont"><span class="fmeta"><b>${fontsAt.size === 1 ? esc(fontLabel([...fontsAt][0])) : 'Mixed fonts'}</b><small>Set one font for all ${texts.length}</small></span></button>`);
    t += f('Colour', `<button class="swatch" id="mColor"><i style="--c:${cols.size === 1 ? parseColor([...cols][0]) : 'transparent'}"></i><code>${cols.size === 1 ? esc([...cols][0]) : 'mixed'}</code></button>`);
    html += card('mtext', `Text (${texts.length})`, 'text', t);
  }
  const pf = pageFields(els);
  if (pf) html += card('mpages', 'Pages', 'layers', pf);
  html += card('mlist', 'Selected', 'layers', `<div class="mlist">${l.map(i => `<button data-only="${i}" class="${i === sel ? 'on' : ''}">${icon((TYPES[dash.Elements[i].Type] || {}).icon || 'rect')}<span>${esc(nameOf(dash.Elements[i]))}</span></button>`).join('')}</div><div class="note">Click one to edit it alone. Shift / Ctrl + click (screen or Layers) adds or takes out.</div>`);
  p.innerHTML = html;
  hydrateIcons(p);
  p.querySelectorAll('.card > header').forEach(h => h.onclick = () => { const c = h.parentElement; c.classList.toggle('closed'); openCards[c.dataset.card] = !c.classList.contains('closed'); localStorage.setItem('fxdash-cards', JSON.stringify(openCards)); });
  p.querySelectorAll('[data-a]').forEach(b => b.onclick = () => ({ dup: duplicate, del: removeSel, front: () => stackPicked(true), back: () => stackPicked(false) })[b.dataset.a]());
  p.querySelectorAll('[data-align]').forEach(b => b.onclick = () => align(b.dataset.align));
  p.querySelectorAll('[data-dist]').forEach(b => b.onclick = () => distribute(b.dataset.dist));
  p.querySelectorAll('[data-only]').forEach(b => b.onclick = () => select(Number(b.dataset.only), { flash: true, force: true }));
  ['gX', 'gY'].forEach(k => {
    const inp = p.querySelector(`[data-k="${k}"]`); if (!inp) return;
    inp.onchange = () => { const b = pickedBounds(), v = Number(inp.value) || 0; begin(); for (const i of pickedList()) shiftEl(dash.Elements[i], k === 'gX' ? v - b.x : 0, k === 'gY' ? v - b.y : 0); changed({ keepLayers: true, inspector: true }); };
  });
  p.querySelectorAll('[data-scrub]').forEach(sp => sp.removeAttribute('data-scrub'));
  // one font / one colour for every text: the pop-ups edit a stand-in whose __multi elements get the same value
  const widest = texts.map(sampleText).reduce((a, b) => String(b).length > String(a).length ? b : a, '');
  const mf = p.querySelector('#mFont');
  if (mf) mf.onclick = ev => { ev.stopPropagation(); openPop(mf, 'font', 'Font', { Type: 'label', Text: widest, W: Math.min(...texts.map(e => e.W || 0)), H: Math.min(...texts.map(e => e.H || 0)), Font: texts[0].Font ?? 14, __multi: texts }); };
  const mc = p.querySelector('#mColor');
  if (mc) mc.onclick = ev => { ev.stopPropagation(); openPop(mc, 'color', 'Color', { Color: texts[0].Color, __multi: texts }); };
  wirePageFields(p, els);
}

function fontBtn(e, k = 'Font') {
  const fid = e[k] ?? 14, fit = fitInfo(e, fid, k);
  return `<div class="fontctl"><button class="btn icon ghost" data-fstep="-1" data-k="${k}" title="Smaller (same family first)">${icon('minus')}</button>`
    + `<button class="fontbtn" data-pop="font" data-k="${k}" title="Pick a font: the screen's fonts, measured with this text"><canvas class="fsample" data-k="${k}"></canvas>`
    + `<span class="fmeta"><b>${esc(fontLabel(fid))}</b><small>${esc(fontSub(fid))}</small></span><span class="fit ${fit.cls}">${fit.text}</span></button>`
    + `<button class="btn icon ghost" data-fstep="1" data-k="${k}" title="Bigger (same family first)">${icon('plus')}</button></div>`;
}
function paintFontButtons(root, target) {
  root.querySelectorAll('canvas.fsample').forEach(cv => { const k = cv.dataset.k, t = String(sampleText(target) || 'Ag12').slice(0, 8) || 'Ag12'; paintFontSample(cv, target[k] ?? 14, t, 0, 0); });
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
  root.querySelectorAll('[data-fstep]').forEach(b => b.onclick = () => {
    const k = b.dataset.k, f = stepFont(target, k, Number(b.dataset.fstep));
    if (f === undefined) { toast(Number(b.dataset.fstep) > 0 ? 'No bigger font draws this text' : 'No smaller font draws this text', 'warn', 1800); return; }
    begin(); target[k] = f; changed({ inspector: true, keepLayers: true });
  });
  paintFontButtons(root, target);
  root.querySelectorAll('[data-a]').forEach(b => b.onclick = () => ({ dup: duplicate, del: removeSel, front: () => stackPicked(true), back: () => stackPicked(false) })[b.dataset.a]());
  root.querySelectorAll('[data-clear]').forEach(x => x.onclick = ev => { ev.stopPropagation(); begin(); delete target[x.dataset.clear]; changed({ inspector: true, keepLayers: true }); });
  root.querySelectorAll('[data-pop]').forEach(b => b.onclick = ev => { ev.stopPropagation(); openPop(b, b.dataset.pop, b.dataset.k, target); });
  root.querySelectorAll('[data-rmcond]').forEach(x => x.onclick = ev => { ev.stopPropagation(); begin(); const l = visList(target); l.splice(Number(x.dataset.rmcond), 1); if (l.length) target.Visible = l; else { delete target.Visible; delete target.PreviewVisible; } changed({ inspector: true }); });
  if (target !== dash) wirePageFields(root, [target]);
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
  const fb = document.querySelectorAll('#inspector .fontbtn[data-k] .fit');
  fb.forEach(el => { const k = el.closest('.fontbtn').dataset.k, i = fitInfo(e, e[k] ?? 14, k); el.className = 'fit ' + i.cls; el.textContent = i.text; });
}

// ---------- popovers: colour, binding, font ----------
let popState = null;
function closePop() { if (popState && popState.restore) popState.restore(); $('pop').classList.remove('open'); popState = null; }
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
  if (target.__multi) for (const t of target.__multi) { if (v === undefined) delete t[k]; else t[k] = v; }
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
let fontFits = true, fontFamily = '';
try { fontFits = localStorage.getItem('fxdash-font-fits') !== '0'; } catch (e) { }
function renderFontPop(pop, k, target) {
  const targets = target.__multi || [target];
  const curF = target[k] ?? 14, boxW = target.W || 0, boxH = target.H || 0, align = target.Align || 'left';
  let text = String(sampleText(target) || 'Ag 123'), kb = -1, rows = [];
  // the screen shows each font as it would look while the mouse is over it; the font the element had comes back after
  const orig = targets.map(t => t[k]);
  const tryFont = id => { targets.forEach(t => t[k] = id); draw(); };
  const undoTry = () => { targets.forEach((t, j) => { if (orig[j] === undefined) delete t[k]; else t[k] = orig[j]; }); draw(); };
  popState.restore = undoTry;
  const pick = id => { undoTry(); popState.restore = null; begin(); targets.forEach(t => t[k] = id); closePop(); changed({ inspector: true, keepLayers: true }); };
  const used = [...new Set(dash.Elements.filter(e => !targets.includes(e)).flatMap(e => [e.Font, e.ValueFont]).filter(x => x !== undefined && x !== null))]
    .map(id => fontInfo[id] && fontInfo[id].dupOf >= 0 ? fontInfo[id].dupOf : id).filter((id, i, a) => a.indexOf(id) === i && fontInfo[id]).sort((a, b) => fontHeight(b) - fontHeight(a));
  pop.innerHTML = `<div class="fontpick">
    <div class="fp-top"><label class="search"><span>${icon('text')}</span><input id="fpText" value="${esc(text)}" spellcheck="false" title="The text each font is measured with (the element's longest text)"></label><span class="fp-box" title="The element's box">${boxW} × ${boxH}</span></div>
    <div class="chips fp-fams" id="fpFams"></div>
    <div id="fpUsed"></div><div id="fpBest"></div>
    <div class="fp-list" id="fpList"></div>
    <div class="fp-foot"><label class="switch"><input type="checkbox" id="fpFits" ${fontFits ? 'checked' : ''}><i></i><span>Only what fits</span></label><span>Heights and widths are the wheel's own; letter shapes are close, not exact.</span></div></div>`;
  const fits = i => i.h <= boxH && textWidth(i.id, text) <= boxW;
  const list = () => {
    const drawable = fontInfo.filter(i => i.usable && i.dupOf < 0 && canDraw(i.id, text));
    // families with something that can draw the text (and fits, when only those are shown)
    const pool = drawable.filter(i => !fontFits || fits(i) || i.id === curF);
    const fams = [...new Set(pool.map(i => i.family))].sort((a, b) => (a === 'Standard' ? -1 : b === 'Standard' ? 1 : a.localeCompare(b)));
    if (fontFamily && !fams.includes(fontFamily)) fontFamily = '';
    $('fpFams').innerHTML = `<button class="chipb ${!fontFamily ? 'on' : ''}" data-fam="">All <small>${pool.length}</small></button>` + fams.map(fm => `<button class="chipb ${fontFamily === fm ? 'on' : ''}" data-fam="${esc(fm)}" title="${esc(fm === 'Standard' || fm === 'Arial' || fm === 'Units' || fm === 'Flags' ? fm + ' fonts' : 'Fonts of Simagic\'s ' + fm + ' dash')}">${esc(fm)} <small>${pool.filter(i => i.family === fm).length}</small></button>`).join('');
    $('fpFams').querySelectorAll('[data-fam]').forEach(b => b.onclick = () => { fontFamily = b.dataset.fam; kb = -1; list(); });
    $('fpUsed').innerHTML = used.length ? `<div class="fp-sec">In this dash</div><div class="chips">${used.slice(0, 8).map(id => `<button class="chipb ${id === curF ? 'on' : ''}" data-f="${id}" title="${esc(fontSub(id))}${canDraw(id, text) ? '' : ': can\'t draw this text'}" ${canDraw(id, text) ? '' : 'disabled'}>${esc(fontLabel(id))}</button>`).join('')}</div>` : '';
    const best = drawable.filter(fits).sort((a, b) => b.h - a.h || (a.family === 'Standard' ? -1 : 1))[0];
    $('fpBest').innerHTML = best ? `<button class="btn primary fp-bestbtn" data-f="${best.id}">${icon('wand')}Biggest that fits: ${esc(fontLabel(best.id))} <small>${esc(fontSub(best.id))}</small></button>`
      : `<div class="note" style="color:#ff7580;margin:2px 2px 8px">Nothing fits: make the box bigger or the text shorter.</div>`;
    rows = pool.filter(i => !fontFamily || i.family === fontFamily).sort((a, b) => b.h - a.h || a.id - b.id);
    const curRow = fontInfo[curF] && fontInfo[curF].dupOf >= 0 ? fontInfo[curF].dupOf : curF;
    $('fpList').innerHTML = rows.map((i, n) => {
      const w = textWidth(i.id, text), tall = i.h > boxH, wide = w > boxW, why = tall ? `${i.h} px tall, box ${boxH}` : wide ? `${w} px wide, box ${boxW}` : `${w} of ${boxW} px wide`;
      return `<button class="fp-row ${i.id === curRow ? 'cur' : ''} ${n === kb ? 'kb' : ''}" data-f="${i.id}" data-n="${n}" title="${esc(why)}"><canvas width="150" height="38"></canvas>`
        + `<span class="fp-meta"><b>${i.h} px <em>${STYLE_NAMES[i.style]}</em></b><small>${esc(fontSub(i.id))}${i.chars !== 'text' ? ' · ' + CHAR_NAMES[i.chars] : ''}</small></span>`
        + `<span class="fit ${tall || wide ? 'bad' : 'ok'}">${tall ? 'too tall' : wide ? 'too wide' : 'fits'}</span></button>`;
    }).join('') || `<div class="empty" style="padding:14px">No font ${fontFits ? 'fits this box with' : 'can draw'} "${esc(text)}".${fontFits ? ' Untick "Only what fits" to see them all.' : ''}</div>`;
    $('fpList').querySelectorAll('.fp-row').forEach(b => {
      paintFontSample(b.querySelector('canvas'), Number(b.dataset.f), text, boxW, boxH, align);
      b.onmouseenter = () => tryFont(Number(b.dataset.f));
    });
    $('fpList').onmouseleave = undoTry;
    pop.querySelectorAll('[data-f]').forEach(b => b.onclick = () => pick(Number(b.dataset.f)));
    positionPop(popState.anchor);
  };
  list();
  setTimeout(() => { const c = pop.querySelector('.fp-row.cur'), box = $('fpList'); if (c && box) box.scrollTop = c.offsetTop - box.offsetTop - box.clientHeight / 2 + c.offsetHeight / 2; }, 0);
  $('fpText').oninput = () => { text = $('fpText').value || ' '; kb = -1; list(); };
  $('fpText').onkeydown = ev => {
    if (ev.key !== 'ArrowDown' && ev.key !== 'ArrowUp' && ev.key !== 'Enter') return;
    ev.preventDefault();
    if (ev.key === 'Enter') { if (rows[kb]) pick(rows[kb].id); return; }
    kb = clamp(kb + (ev.key === 'ArrowDown' ? 1 : -1), 0, rows.length - 1);
    $('fpList').querySelectorAll('.fp-row').forEach(b => b.classList.toggle('kb', Number(b.dataset.n) === kb));
    const r = $('fpList').querySelector('.fp-row.kb'); if (r) { r.scrollIntoView({ block: 'nearest' }); tryFont(rows[kb].id); }
  };
  $('fpFits').onchange = () => { fontFits = $('fpFits').checked; try { localStorage.setItem('fxdash-font-fits', fontFits ? '1' : '0'); } catch (e) { } kb = -1; list(); };
}

// ---------- checks ----------
let checkTimer = null, lastCheck = null;
// The screen's RAM drive (FXProDashes docs/screen-images.md): on a wheel with the RAM-drive screen image, the dash is
// kept on the screen as pictures (tiles): drawn at once, in full colour. How much of the drive it takes decides how
// many dashes stay loaded together (instant switching).
// ---------- the screen's RAM drive: what this dash keeps there ----------
async function showPictures() {
  const box = $('ramList'); if (!box || !dash) return;
  box.innerHTML = `<div class="empty" style="padding:18px">Measuring…</div>`;
  let res;
  try { res = await post('/api/pictures', dash); } catch (e) { box.innerHTML = `<div class="empty" style="padding:18px">${icon('warn')}Couldn't measure: ${esc(e.message)}</div>`; return; }
  const pics = res.pictures, total = res.total, budget = res.budget, picBytes = res.shapes;
  const kb = b => (b / 1024).toFixed(b < 10240 ? 1 : 0) + ' KB';
  const rows = pics.sort((a, b) => b.variantBytes - a.variantBytes).map(p => `<div class="tr-row" data-i="${elementIndex(p.element)}" title="Select this element"><span>${esc(elementLabel(p.element))}</span><span class="n">${kb(p.variantBytes)}</span><span class="n">${p.variants > 1 ? p.variants + ' looks' : ''}</span><div class="bar"><i style="width:${(p.variantBytes / Math.max(1, pics[0] ? pics[0].variantBytes : 1) * 100).toFixed(0)}%"></i></div></div>`).join('');
  box.innerHTML = `<div class="tr">
    <div class="tr-cards">
      <div class="tr-card ${total > budget ? 'err' : 'ok'}"><small>On the RAM drive</small><b>${kb(total)}</b><em>of ${kb(budget)}: about ${Math.max(1, Math.floor(budget / Math.max(1, total)))} dash${Math.floor(budget / Math.max(1, total)) === 1 ? '' : 'es'} this size fit together</em></div>
      <div class="tr-card ok"><small>The dash itself</small><b>${kb(res.own)}</b><em>its look, drawn at once</em></div>
      <div class="tr-card ok"><small>Shapes that come and go</small><b>${kb(picBytes)}</b><em>${pics.length} picture${pics.length === 1 ? '' : 's'}: each overlay drawn with one command</em></div>
    </div>
    <h4>Pictures of shapes that come and go</h4>
    <div class="tr-row head"><span>Element</span><span class="n">Takes</span><span class="n">Looks</span></div>${rows || '<div class="empty" style="padding:12px">None: nothing in this dash comes and goes with a fixed look.</div>'}
    <div class="note">On a wheel with the screen's RAM patch, the dash and each overlay's ovals, frames and pictures are kept on the screen as JPEG pictures, so they show at once. A shape that's always under another one's picture lives in that picture; a shape coloured by data has one look per colour stop. Without the patch everything is drawn with rectangles instead.</div>
  </div>`;
  box.querySelectorAll('[data-i]').forEach(el => { const i = Number(el.dataset.i); if (i >= 0) el.onclick = () => { select(i, { flash: true, force: true }); switchTab('layers', true); }; });
}

function showRam(c) {
  const rp = $('ramPill'); if (!rp) return;
  const kb = Math.max(1, Math.round((c.RamBytes || 0) / 1024)), budget = Math.round((c.RamBudget || 344064) / 1024);
  const fit = Math.max(1, Math.floor((c.RamBudget || 344064) / Math.max(1, c.RamBytes || 1)));
  rp.classList.remove('busy', 'ok', 'warn', 'err');
  rp.classList.add(c.RamBytes > c.RamBudget ? 'err' : c.RamBytes > 96 * 1024 ? 'warn' : 'ok');
  $('ramText').textContent = `Screen RAM ${kb} KB`;
  rp.title = c.RamBytes > c.RamBudget
    ? `Takes ${kb} KB: more than the screen's RAM drive (${budget} KB). On a wheel with it, this dash is drawn with rectangles instead. Fewer or smaller pictures and gradients fit.`
    : `On a wheel with the screen's RAM drive this dash is kept as ${c.RamFiles} picture${c.RamFiles === 1 ? '' : 's'} (${kb} KB of ${budget} KB): `
      + `drawn at once, in full colour. About ${fit} dash${fit === 1 ? '' : 'es'} this size stay loaded together; the first show of one loads for a few seconds.`;
}

// ---------- wheel traffic: what `fxdash verify` and `fxdash fit-bands` tell, live in the designer ----------
// A demo lap on a simulated screen (about two seconds of work for a minute of lap), run a moment after the last edit.
const TRAFFIC_BUDGET = 25000, TRAFFIC_TARGET = 12000; // the screen takes 25 KB/s; aim under 12 KB/s (the lights share the time)
let verifyTimer = null, verifyBusy = false, lastVerify = null, lastBands = null, trafficMode = 'rect';
try { trafficMode = localStorage.getItem('fxdash-traffic-mode') === 'ram' ? 'ram' : 'rect'; } catch (e) { }
const fmtRate = b => (b < 1000 ? Math.round(b) + ' B/s' : (b / 1000).toFixed(b < 10000 ? 1 : 0) + ' KB/s');
const usesImages = () => !!dash && (Object.keys(dash.Images || {}).length > 0 || dash.Elements.some(e => e.Type === 'image'));
const elementIndex = label => { const m = /^#(\d+)\s/.exec(String(label)); return m ? Number(m[1]) : -1; };
const elementLabel = label => String(label).replace(/^#\d+\s+/, '');

function markTrafficPending() {
  const pill = $('trafficPill'); if (pill) pill.classList.add('busy');
  const tr = $('traffic'); if (tr && tr.firstElementChild) tr.firstElementChild.classList.add('stale');
}
function scheduleVerify(ms = 1500) {
  clearTimeout(verifyTimer);
  markTrafficPending();
  verifyTimer = setTimeout(runVerify, ms);
}
async function runVerify() {
  if (!dash) return;
  if (verifyBusy) { scheduleVerify(600); return; }
  if (!dash.Elements.length) { lastVerify = null; lastBands = null; showTraffic(); return; }
  verifyBusy = true;
  const snap = snapshot();
  try {
    const tiles = trafficMode === 'ram' && usesImages() ? 1 : 0;
    const [v, b] = await Promise.all([
      post(`/api/verify?left=${pad.l}&top=${pad.t}&seconds=60&tiles=${tiles}`, dash),
      post('/api/fit-bands', dash),
    ]);
    lastVerify = v; lastBands = b;
  } catch (e) {
    verifyBusy = false; lastVerify = null; lastBands = null;
    const p = $('trafficPill'); p.className = 'pill err'; $('trafficText').textContent = 'Traffic: not measured'; p.title = 'The measurement failed: ' + e.message;
    return;
  }
  verifyBusy = false;
  if (snapshot() !== snap) { scheduleVerify(300); return; } // edited meanwhile: measure the new version
  showTraffic();
}

function showTraffic() {
  const pill = $('trafficPill'), box = $('traffic');
  if (!lastVerify) {
    pill.className = 'pill'; $('trafficText').textContent = 'Wheel traffic';
    pill.title = 'Add elements to measure what this dash sends to the screen';
    box.innerHTML = `<div class="tr"><div class="empty" style="padding:18px">${icon('ok')}Nothing to measure yet: add elements and what the dash sends to the wheel over a demo lap shows here.</div></div>`;
    return;
  }
  const v = lastVerify, bands = lastBands && lastBands.changes ? lastBands.changes : [];
  const chk = lastCheck ? { e: lastCheck.errors, w: lastCheck.warnings } : null;
  const bad = v.WorstSecondBytes > TRAFFIC_BUDGET || v.FlashingUpdates > 0 || !!v.RedrawMismatch;
  const soft = !bad && (v.AvgBytesPerSecond > TRAFFIC_TARGET || bands.length > 0);
  pill.className = 'pill ' + (bad ? 'err' : soft ? 'warn' : 'ok');
  $('trafficText').textContent = 'Wheel traffic ' + fmtRate(v.AvgBytesPerSecond);
  pill.title = `Over a ${v.Seconds} s demo lap this dash sends ${fmtRate(v.AvgBytesPerSecond)} on average and ${fmtRate(v.WorstSecondBytes)} in its busiest second (the screen takes ${fmtRate(TRAFFIC_BUDGET)}). Click for details.`;

  const gate = (ok, text, action) => `<div class="tr-gate ${ok ? 'ok' : 'bad'}"><span class="lv">${icon(ok ? 'ok' : 'warn')}</span><span>${esc(text)}</span>${action || ''}</div>`;
  const checksOk = chk ? !chk.e && !chk.w : true;
  const gates = [
    gate(checksOk, checksOk ? 'Layout checks: no errors, no warnings' : `Layout checks: ${chk.e} problem${chk.e === 1 ? '' : 's'}, ${chk.w} warning${chk.w === 1 ? '' : 's'}`,
      checksOk ? '' : '<button class="btn small" data-act="checks">Show</button>'),
    gate(bands.length === 0, bands.length === 0 ? 'Text sits between the border lines: nothing flashes on a line'
      : `${bands.length} value${bands.length === 1 ? ' crosses' : 's cross'} a border line and would flash`, bands.length === 0 ? '' : '<button class="btn small primary" data-act="bands">Fix them</button>'),
    gate(v.Ok, v.Ok ? 'Traffic under budget, no flashing, drawing matches a full redraw' : (v.Problems[0] || 'Traffic or flashing needs a look')),
  ];
  const passed = gates.filter(g => g.includes('tr-gate ok')).length;
  const verdictCls = passed === 3 ? 'ok' : bad ? 'err' : 'warn';
  const verdict = passed === 3 ? 'Ready for the wheel and the library' : `${3 - passed} of 3 gates need attention`;
  const sub = passed === 3 ? 'Passes every gate the /create-dash command checks.' : 'The same three gates the /create-dash command checks.';

  const worstTop = Object.keys(v.WorstSecondBy || {})[0];
  const cards = [
    ['Average', fmtRate(v.AvgBytesPerSecond), v.AvgBytesPerSecond > TRAFFIC_BUDGET ? 'err' : v.AvgBytesPerSecond > TRAFFIC_TARGET ? 'warn' : 'ok', `aim under ${fmtRate(TRAFFIC_TARGET)}; the screen takes ${fmtRate(TRAFFIC_BUDGET)}`],
    ['Busiest second', fmtRate(v.WorstSecondBytes), v.WorstSecondBytes > TRAFFIC_BUDGET ? 'err' : 'ok', `at ${v.WorstSecondAt} s` + (worstTop ? `: ${elementLabel(worstTop)}` : '')],
    ['Flashing updates', `${v.FlashingUpdates} of ${v.Updates}`, v.FlashingUpdates ? 'err' : 'ok', v.PopupUpdates ? `${v.PopupUpdates} pop-up changes (drawn on purpose)` : 'must be 0'],
    ['Draws in', lastCheck ? lastCheck.cost.StaticSeconds.toFixed(1) + ' s' : '…', lastCheck && lastCheck.cost.StaticSeconds > 8 ? 'warn' : 'ok', 'the first full draw on the wheel'],
    ...(v.PageFlips ? [['Page flip', fmtRate(v.WorstPageFlipBytes).replace('/s', ''), v.WorstPageFlipBytes > TRAFFIC_BUDGET / 2 ? 'warn' : 'ok', `the biggest of ${v.PageFlips} flips during the lap`]] : []),
  ].map(c => `<div class="tr-card ${c[2]}"><small>${c[0]}</small><b>${esc(c[1])}</b><em>${esc(c[3])}</em></div>`).join('');

  const tl = v.Timeline || [];
  const maxB = Math.max(TRAFFIC_BUDGET * 1.15, ...tl.map(t => t.Bytes)), SW = 600, SH = 80, bw = SW / Math.max(1, tl.length);
  const y = b => (SH - b / maxB * SH).toFixed(1);
  const bars = tl.map((t, i) => {
    const h = Math.max(1, t.Bytes / maxB * SH);
    const top = Object.entries(t.By || {}).slice(0, 3).map(([k, b]) => `${elementLabel(k)} ${fmtRate(b)}`).join(', ');
    return `<rect class="${t.Bytes > TRAFFIC_BUDGET ? 'err' : t.Bytes > TRAFFIC_TARGET ? 'warn' : ''}" x="${(i * bw + 0.5).toFixed(1)}" y="${(SH - h).toFixed(1)}" width="${Math.max(1, bw - 1).toFixed(1)}" height="${h.toFixed(1)}"><title>${esc(`${t.Second} s: ${fmtRate(t.Bytes)}${top ? ' · ' + top : ''}`)}</title></rect>`;
  }).join('');
  const spark = `<svg viewBox="0 0 ${SW} ${SH + 6}" preserveAspectRatio="none" role="img" aria-label="Bytes sent to the screen, second by second">${bars}`
    + `<line x1="0" x2="${SW}" y1="${y(TRAFFIC_BUDGET)}" y2="${y(TRAFFIC_BUDGET)}" stroke="#ff3b46" stroke-width="1" stroke-dasharray="4 3"/>`
    + `<line x1="0" x2="${SW}" y1="${y(TRAFFIC_TARGET)}" y2="${y(TRAFFIC_TARGET)}" stroke="#ffb000" stroke-width="1" stroke-dasharray="2 4"/></svg>`;

  const maxRow = Math.max(1, ...(v.Traffic || []).map(t => t.BytesPerSecond));
  const rows = (v.Traffic || []).map(t => `<div class="tr-row" data-i="${elementIndex(t.Element)}" title="Select this element"><span>${esc(elementLabel(t.Element))}</span><span class="n">${fmtRate(t.BytesPerSecond)}</span><span class="n">${t.DrawsPerSecond}/s</span><div class="bar"><i style="width:${(t.BytesPerSecond / maxRow * 100).toFixed(0)}%"></i></div></div>`).join('');
  const problems = [
    ...v.Problems.map(p => ({ text: p, i: -1 })),
    ...bands.map(c => ({ text: c, i: -1 })),
    ...(v.Flashes || []).map(f => ({ text: `At ${f.Time} s, ${f.Pixels} pixels flashed${f.Elements && f.Elements.length ? ' around ' + f.Elements.map(elementLabel).join(', ') : ''}`, i: f.Elements && f.Elements.length ? elementIndex(f.Elements[0]) : -1 })),
  ];
  const probs = problems.length ? problems.map(p => `<div class="tr-prob" data-i="${p.i}"><span class="lv">${icon('warn')}</span><span>${esc(p.text)}</span></div>`).join('')
    : `<div class="tr-prob"><span class="lv" style="color:var(--green)">${icon('ok')}</span><span>Nothing to fix.</span></div>`;

  const seg = usesImages() ? `<div class="tr-seg" title="Measure as the wheel will draw it"><button data-mode="rect" class="${trafficMode === 'rect' ? 'on' : ''}">Rectangles</button><button data-mode="ram" class="${trafficMode === 'ram' ? 'on' : ''}">With the RAM patch</button></div>` : '';
  box.innerHTML = `<div class="tr">
    <div class="tr-top"><div class="tr-verdict ${verdictCls}"><span class="lv">${icon(verdictCls === 'ok' ? 'ok' : 'warn')}</span><div><b>${verdict}</b><br><span class="sub">${esc(sub)}</span></div></div>${seg}<button class="btn small" data-act="again">Measure again</button></div>
    <div class="tr-gates">${gates.join('')}</div>
    <div class="tr-cards">${cards}</div>
    <h4>Bytes to the screen, second by second</h4><div class="tr-spark">${spark}</div>
    <div class="tr-cols"><div><h4>What sends the most</h4><div class="tr-row head"><span>Element</span><span class="n">Sends</span><span class="n">Redraws</span></div>${rows}</div><div><h4>What to look at</h4>${probs}</div></div>
    ${overlaysSection()}
    <div class="note">Measured on a simulated screen over a ${v.Seconds} s demo lap, the dash updating 10 times a second as on the wheel: the same numbers as <code>fxdash verify</code> and <code>fxdash fit-bands</code>. Click a row to select the element.</div>
  </div>`;
  wireOverlaysSection(box);
  box.querySelectorAll('[data-i]').forEach(el => {
    const i = Number(el.dataset.i); if (!(i >= 0 && i < dash.Elements.length)) { el.style.cursor = 'default'; return; }
    el.onclick = () => { select(i, { flash: true, force: true }); switchTab('layers', true); };
  });
  box.querySelectorAll('[data-mode]').forEach(b => b.onclick = () => {
    trafficMode = b.dataset.mode; try { localStorage.setItem('fxdash-traffic-mode', trafficMode); } catch (e) { }
    scheduleVerify(50);
  });
  box.querySelector('[data-act="again"]').onclick = () => scheduleVerify(50);
  const fix = box.querySelector('[data-act="bands"]'); if (fix) fix.onclick = applyBands;
  const show = box.querySelector('[data-act="checks"]'); if (show) show.onclick = () => setDrawerTab('checks');
}

// every overlay brought up in turn on every page (fxdash verify --overlays): what it sends, whether anything flashes
let lastOverlayCheck = null, overlayCheckBusy = false, overlayCheckFor = null;
function overlaysSection() {
  if (!overlays.length) return '';
  const r = lastOverlayCheck && overlayCheckFor === snapshot() ? lastOverlayCheck : null;
  let body;
  if (overlayCheckBusy) body = `<div class="empty" style="padding:12px">Bringing up each of the ${overlays.length} overlays on ${pageCount() > 1 ? 'every page' : 'the dash'}, over a running lap… (up to a minute for a big dash)</div>`;
  else if (!r) body = `<div class="note">The demo lap doesn't reach most overlays (pit screens, flags, warnings). Check them all: each comes up in turn over the running lap, as on the wheel.</div>`;
  else {
    const flashing = r.Overlays.filter(o => o.FlashingUpdates > 0), slow = r.Overlays.filter(o => Math.max(o.ShowBytes, o.HideBytes) > TRAFFIC_BUDGET);
    const head = flashing.length || r.RedrawMismatch ? `<div class="tr-gate bad"><span class="lv">${icon('warn')}</span><span>${flashing.length ? `${flashing.length} overlay${flashing.length === 1 ? '' : 's'}: something under ${flashing.length === 1 ? 'it' : 'them'} flashes while ${flashing.length === 1 ? 'it shows' : 'they show'}` : 'A drawing error with an overlay up'}</span><button class="btn small primary" data-act="taketurns" title="Values and bars half under an overlay hide while it shows (they'd redraw the overlay at every change)">Make them take turns</button></div>`
      : `<div class="tr-gate ok"><span class="lv">${icon('ok')}</span><span>Every overlay comes and goes cleanly${slow.length ? `; ${slow.length} take${slow.length === 1 ? 's' : ''} over a second (big ones, about as long as drawing the dash)` : ''}</span></div>`;
    const byFirst = idx => overlays.find(o => o.first === idx);
    const rows = r.Overlays.slice().sort((a, b) => (b.FlashingUpdates - a.FlashingUpdates) || (Math.max(b.ShowBytes, b.HideBytes) - Math.max(a.ShowBytes, a.HideBytes))).slice(0, 40).map(o => {
      const ov = byFirst(elementIndex(o.Overlay)); const name = ov ? ov.name : elementLabel(o.Overlay);
      const most = Math.max(o.ShowBytes, o.HideBytes), cls = o.FlashingUpdates ? 'err' : most > TRAFFIC_BUDGET ? 'warn' : '';
      return `<div class="tr-row ov ${cls}" data-ov="${ov ? ov.index : -1}" data-pg="${o.Page}" title="Show this overlay${flipCount() > 1 ? ' on this page' : ''}"><span>${esc(name)}${flipCount() > 1 ? ` <em>${esc(hasSets() ? 'flip ' + (o.Page + 1) : pageName(o.Page, 0))}</em>` : ''}</span><span class="n">${fmtRate(o.ShowBytes).replace('/s', '')}</span><span class="n">${fmtRate(o.HideBytes).replace('/s', '')}</span><span class="n">${o.FlashingUpdates ? o.FlashingUpdates + ' flash' : ''}</span></div>`;
    }).join('');
    body = head + `<div class="tr-row head ov"><span>Overlay</span><span class="n">Shows</span><span class="n">Goes</span><span class="n"></span></div>` + rows;
  }
  return `<h4>Overlays <button class="btn small" data-act="checkoverlays" ${overlayCheckBusy ? 'disabled' : ''}>${r ? 'Check again' : 'Check every overlay'}</button></h4>${body}`;
}
function wireOverlaysSection(box) {
  const c = box.querySelector('[data-act="checkoverlays"]'); if (c) c.onclick = checkOverlays;
  const t = box.querySelector('[data-act="taketurns"]'); if (t) t.onclick = takeTurns;
  box.querySelectorAll('.tr-row.ov[data-ov]').forEach(el => { const i = Number(el.dataset.ov); if (i < 0) return; el.onclick = () => { if (flipCount() > 1) showFlip(Number(el.dataset.pg)); setOverlay(i); }; });
}
async function checkOverlays() {
  if (overlayCheckBusy || !dash) return;
  overlayCheckBusy = true; showTraffic();
  const snap = snapshot();
  try {
    const tiles = trafficMode === 'ram' ? 1 : 0;
    lastOverlayCheck = await post(`/api/verify?left=${pad.l}&top=${pad.t}&seconds=20&tiles=${tiles}&overlays=1`, dash);
    overlayCheckFor = snap;
  } catch (e) { toast('Overlay check failed: ' + e.message, 'err'); }
  overlayCheckBusy = false; showTraffic();
}
async function takeTurns() {
  try {
    const r = await post('/api/take-turns', dash);
    if (!r.elements) { toast('Nothing to change: no value sits half under an overlay', 'ok'); return; }
    begin(); dash.Elements = r.elements; changed({ inspector: true });
    toast(`${r.changes.length} value${r.changes.length === 1 ? '' : 's'} now hide while the overlay over ${r.changes.length === 1 ? 'it' : 'them'} shows`, 'ok', 4200);
    checkOverlays();
  } catch (e) { toast('Failed: ' + e.message, 'err'); }
}

function applyBands() {
  if (!lastBands || !lastBands.elements) return;
  const n = lastBands.changes.length;
  begin(); dash.Elements = lastBands.elements; changed({ inspector: true });
  toast(`Fixed ${n} value${n === 1 ? '' : 's'}: the text now sits between the border lines`, 'ok');
}

// the bottom drawer: Checks and Wheel traffic
function setDrawerTab(t, open = true) {
  document.querySelectorAll('#drawerTabs button').forEach(b => b.classList.toggle('on', b.dataset.dtab === t));
  $('issues').hidden = t !== 'checks'; $('traffic').hidden = t !== 'traffic'; $('ramList').hidden = t !== 'ram';
  if (t === 'ram') showPictures();
  const meter = $('costMeter').parentElement; meter.style.display = $('costText').style.display = t === 'checks' ? '' : 'none';
  if (open) $('drawer').classList.add('open');
}
function toggleDrawer(t) {
  const on = document.querySelector('#drawerTabs .on');
  if ($('drawer').classList.contains('open') && on && on.dataset.dtab === t) $('drawer').classList.remove('open'); else setDrawerTab(t, true);
}

function scheduleCheck(ms = 450) {
  clearTimeout(checkTimer); $('checksPill').classList.add('busy'); checkTimer = setTimeout(runCheck, ms);
  markTrafficPending(); // the numbers on show are for the version before this edit
}
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
    showRam(c);
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
    scheduleVerify();
    scheduleOverlays();
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
    const blob = await post(`/api/render?mode=${view === 'demo' ? 'demo' : 'preview'}&seconds=${demoT}&left=0&top=0&pages=${pagesParam()}&overlay=${overlay}`, dash);
    const img = $('exact'), old = img.src; img.src = URL.createObjectURL(blob); if (old) setTimeout(() => URL.revokeObjectURL(old), 1000);
  } catch (e) { toast('Preview failed: ' + e.message, 'err'); setView('edit'); }
  finally { exactBusy = false; }
}

// ---------- wheel ----------
let wheelOn = false, wheelTimer = null;
function scheduleWheel() { if (!wheelOn) return; clearTimeout(wheelTimer); wheelTimer = setTimeout(pushWheel, 600); }
async function pushWheel() { try { await post(`/api/wheel/show?left=${pad.l}&top=${pad.t}&pages=${pagesParam()}&overlay=${overlay}`, dash); } catch (e) { toast('Wheel: ' + e.message, 'err'); setWheel(false); } }
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
    $('promptBody').innerHTML = fields.map((fl, i) => fl.type === 'check'
      ? `<label class="switch" style="margin:4px 0"><input type="checkbox" id="pf${i}" ${fl.value ? 'checked' : ''}><i></i><span class="note" style="color:var(--text2)">${esc(fl.label)}</span></label>`
      : f(fl.label, `<input class="in" id="pf${i}" value="${esc(fl.value || '')}" spellcheck="false">`)).join('');
    $('promptBody').style.display = fields.length ? '' : 'none';
    const okb = $('promptOk'); okb.textContent = ok; okb.className = 'btn primary'; if (danger) okb.style.background = 'linear-gradient(180deg,#ff2b38,#a00)';
    openModal('promptModal');
    setTimeout(() => { const i = $('pf0'); if (i && i.type !== 'checkbox') { i.focus(); i.select(); } else okb.focus(); }, 60);
    const done = v => { closeModal('promptModal'); okb.onclick = null; resolve(v); };
    okb.onclick = () => done(fields.map((fl, i) => fl.type === 'check' ? $('pf' + i).checked : $('pf' + i).value.trim()));
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
    saveas: () => saveAs(false), download, open: () => $('file').click(), keys: () => openModal('keysModal'), delete: deleteDash, addpage: () => addPage(0),
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
  $('checksPill').onclick = () => toggleDrawer('checks');
  $('ramPill').onclick = () => toggleDrawer('ram');
  $('btnOverlay').onclick = ev => { ev.stopPropagation(); const m = $('overlayMenu'); if (m.classList.contains('open')) m.classList.remove('open'); else openOverlayPick(); };
  $('trafficPill').onclick = () => toggleDrawer('traffic');
  document.querySelectorAll('#drawerTabs button').forEach(b => b.onclick = () => setDrawerTab(b.dataset.dtab, true));
  $('drawerClose').onclick = () => $('drawer').classList.remove('open');
  $('zoomIn').onclick = () => setZoom(zoom + .1); $('zoomOut').onclick = () => setZoom(zoom - .1); $('zoomFit').onclick = fitZoom;
  $('stage').addEventListener('wheel', ev => { if (!ev.ctrlKey) return; ev.preventDefault(); setZoom(zoom * (ev.deltaY < 0 ? 1.1 : 1 / 1.1)); }, { passive: false });
  $('stageScroll').addEventListener('mousedown', ev => { if (ev.target === $('stageScroll') || ev.target === $('bezel')) select(-1); });
  $('dashName').onchange = () => { begin(); dash.Name = $('dashName').value; changed({ keepLayers: true, inspector: sel < 0 }); };
  $('libSearch').oninput = renderLibrary;
  $('layerSearch').oninput = () => { layerQuery = $('layerSearch').value.trim(); $('layerSearchClear').hidden = !layerQuery; renderLayers(); };
  $('layerSearch').onkeydown = ev => { if (ev.key === 'Enter') { const o = layerOrder(); if (o.length) { setPicked(o, o[0]); renderLayers(); renderInspector(); draw(); } } };
  $('layerSearchClear').onclick = () => { $('layerSearch').value = ''; layerQuery = ''; $('layerSearchClear').hidden = true; renderLayers(); $('layerSearch').focus(); };
  $('layerShownOnly').onchange = () => { layerShownOnly = $('layerShownOnly').checked; renderLayers(); };
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
  buildFontInfo();
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
