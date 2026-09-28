# Dash designer

Design dashes for the FX Pro screen (USB mode, [usb-mode.md](usb-mode.md)), import SimHub dashes and fine-tune them,
and see changes on the wheel as you make them. Three ways in, all backed by the same code (`Usb/DashTools.cs`), so they
agree with each other and with the wheel:

| | For | Needs |
|---|---|---|
| **Web designer** | people | SimHub running (plugin) or `fxdash serve` |
| **HTTP API** | agents, scripts, the web designer | same |
| **`fxdash`** command line | agents, scripts | SimHub installed (its DLLs); SimHub needn't run |

The dash format: [dash-format.md](dash-format.md). Designing with an AI agent: the `fxpro-dash-design` skill in
`.claude/skills/` (and the "For agents" section below).

## Web designer

SimHub → FXPro RPM Sync → USB mode → **Open the dash designer** (or http://127.0.0.1:8899/ while SimHub runs; port in
settings, `DesignerPort`). Offline: `fxdash serve` (no wheel, SimHub formulas not evaluated).

- **Top bar:** open a dash from the library (built-in and saved), New, **Import SimHub dash…**, Save (to
  `PluginsData\Common\FXProRpmSync\Dashes\<Id>.json`; the plugin's dash list picks it up), Save as, Download / Open file
  (JSON), undo/redo, the wheel's padding (for checks and imports), **Exact preview** (the server's rendering, exactly
  what the wheel draws, over the canvas), **Demo lap** (the exact preview animated), **Show on wheel**.
- **Canvas:** click to select, drag to move, corner handles to resize, arrow keys nudge (Shift = 10 px), Del, Ctrl+D,
  Ctrl+Z/Y, Ctrl+S. Text is drawn at the screen fonts' real widths (letter shapes are stand-ins), so what fits here fits
  on the wheel.
- **Elements:** drawing order (▲▼ to reorder), add (label, value, rect, box, ellipse, gradient, image from a file, bar,
  delta bar, pop-up), duplicate, delete. ◉/◌ marks elements with conditions (shown/hidden in previews); ⚠ has issues.
- **Properties:** everything in the format, with a font list (height, characters) and **Fit** (the tallest font whose
  text fits the box), a bindings list, formats, colour pickers (alpha kept), conditions, "show in previews".
- **Checks:** live under the canvas (errors, warnings, draw time); click one to select its element.
- **Show on wheel** (plugin only, USB mode on): the wheel shows the dash being edited, with live data while a game runs
  and the simulated lap otherwise; it follows every change. It stops when you switch it off, close the page, or after a
  minute without the page.

## SimHub import

**Import SimHub dash…** (or `fxdash import`, `POST /api/import`) converts any installed SimHub dash
(`SimHub\DashTemplates`), or a `.djson` path:

- Scaled to fit the screen minus the wheel's padding; the main in-game screen (a screen named Main/Race/Dash, else the
  in-game screen with the most on it), or the one you pick; background/foreground layer screens included; overlay
  screens not.
- Layers and widgets flattened (widget screens switched by a formula become groups shown while the formula gives their
  index), in SimHub's drawing order.
- Shapes, rounded borders, gradients, images (from the dash's `.ressources` zip or SimHub's `ImageLibrary`; stored in
  the dash at their drawn size, reduced to a few colours; if drawing would take longer than the budget, colours are cut,
  then the largest images dropped), text (the tallest screen font that fits; SimHub's fonts can't be used), linear
  gauges as bars, text borders as boxes, colour gradients as `ColorStops`.
- Every binding is kept as a SimHub formula (`ncalc:`/`js:`) and evaluated live by SimHub's own engine, so values,
  visibility and colours behave as in SimHub; the dash's `JavascriptExtensions` folder is used for its JS helpers.
  SimHub's built-in text items (gear, speed, lap times, fuel...) become data keys.
- Pop-ups and warnings (a condition shared by a filled shape and its text, or big/flashing overlays) start hidden in
  previews; live, SimHub's conditions decide.
- Not converted (listed in the report): dial/circular gauges, charts, maps, leaderboards, web pages, buttons, shift
  light images, shade/progress items.
- Old dash files (Newtonsoft `$id`/`$values`) are read too. All 85 dashes in a stock SimHub install import (2026-09-27).

Expect to fine-tune: labels too wide for their box in the screen's fonts, overlapping texts, the main screen choice.
The report lists them and the checks point at each.

## HTTP API

`http://127.0.0.1:8899` (plugin) or `http://127.0.0.1:<port>` (`fxdash serve`); local only. JSON in and out; CORS open.
`GET /api` lists everything.

| Method | Path | |
|---|---|---|
| GET | `/api/schema` | the format (element types, fields, formats, limits) |
| GET | `/api/bindings` | data keys |
| GET | `/api/fonts?sample=TEXT` | fonts: id, height, characters, digit width, sample width |
| GET | `/api/fonts/metrics` | every font's height and ASCII advance widths (`widths[font][char - 32]`) |
| GET | `/api/fonts/suggest?w=W&h=H&text=TEXT[&height=PX]` | the best font for a box |
| GET | `/api/dashes` | the library |
| GET / PUT / DELETE | `/api/dashes/{id}` | read / save (body = dash) / delete a saved dash |
| POST | `/api/check?left=L&top=T` | body = dash → `{ok, errors, warnings, issues[], cost}` |
| POST | `/api/render?mode=preview\|demo\|live&seconds=N&left=L&top=T` | body = dash → PNG |
| GET | `/api/simhub`, `/api/simhub/screens?name=` | installed SimHub dashes, their screens |
| POST | `/api/import` | `{name or path, screen?, images?, colors?, maxSeconds?, fitWidth?, fitHeight?}` → `{dash, report, check}` |
| GET | `/api/wheel` | wheel status (plugin) |
| POST | `/api/wheel/show?left=L&top=T` | body = dash → shown on the wheel for ~60 s (repeat to keep it) |
| POST | `/api/wheel/stop` | back to the selected dash |

## fxdash

`tools/fxdash` (`dotnet build -c Release`, then `bin\Release\net48\fxdash.exe`). JSON on stdout; exit 1 when `check`
finds errors, 2 on failure. `--simhub DIR` if SimHub isn't in `C:\Program Files (x86)\SimHub`.

```
fxdash schema | bindings | fonts [--sample TEXT] | suggest-font W H TEXT
fxdash check DASH.json [--pad 10,20]
fxdash render DASH.json OUT.png [--mode preview|demo] [--seconds N] [--pad L,T]
fxdash builtin [ID] [OUT.json]           # e.g. fxdash builtin lmgt3-mustang mustang.json
fxdash simhub | simhub-screens NAME
fxdash import NAME|PATH OUT.json [--screen S] [--fit 790,460] [--colors N] [--no-images] [--png OUT.png]
fxdash serve [--port 8899]
```

## For agents

The loop that works: read the format (`fxdash schema`), write the dash JSON, `fxdash check` it, `fxdash render` it and
look at the PNG, fix, repeat; then save it into the dashes folder (or `PUT /api/dashes/{id}`), and with SimHub running
`POST /api/wheel/show` to see it on the wheel. The skill `.claude/skills/fxpro-dash-design/SKILL.md` spells this out with
the screen's constraints. In the browser, `window.fxdash` exposes the designer's dash (`fxdash.dash`, `fxdash.load(d)`).
