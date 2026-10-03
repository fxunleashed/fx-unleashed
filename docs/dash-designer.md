# Dash designer

> **Where this fits:** this is step 4 of the [setup guide](https://fxunleashed.com/start/#4-pick-your-dashes-and-lights). It needs the
> plugin ([step 2](https://fxunleashed.com/start/#2-install-the-plugin)), and to see a dash on the wheel the custom firmware
> ([step 1](https://fxunleashed.com/start/#1-install-the-custom-firmware)). The screen's RAM patch
> ([step 3](https://fxunleashed.com/start/#3-turn-on-picture-memory), optional but highly recommended) lets dashes use full-colour
> pictures. When your dash is done you can [share it or submit it to the library](https://fxunleashed.com/docs/library-submit/).
> Most people only need the web designer and SimHub import sections below: the HTTP API, command line and agent parts are for tinkerers.

Design dashes for the FX Pro screen (Unleashed mode, [developer reference](usb-mode.md)), import SimHub dashes and fine-tune them,
and see changes on the wheel as you make them. Three ways in, all backed by the same code (`Usb/DashTools.cs`), so they
agree with each other and with the wheel:

| | For | Needs |
|---|---|---|
| **Web designer** | people | SimHub running (plugin) or `fxdash serve` |
| **HTTP API** | agents, scripts, the web designer | same |
| **`fxdash`** command line | agents, scripts | SimHub installed (its DLLs); SimHub needn't run |

The dash format: [dash-format.md](dash-format.md). Designing with an AI agent: the `/create-dash` skill in
`.claude/skills/` (and the "For agents" section below).

## Web designer (Dash Studio)

SimHub → FX Unleashed → Dashes → **Designer** (or **Edit in the designer** on a dash; or http://127.0.0.1:8899/ while SimHub runs; port in
settings, `DesignerPort`). Offline: `fxdash serve` (no wheel, SimHub formulas not evaluated). Same look as the plugin.

- **Top bar:** the dash's name (edit in place; amber dot = unsaved), undo/redo, **Edit / Exact / Demo lap** (Exact and
  Demo are the plugin's own rendering, exactly what the wheel draws), **On wheel** (the wheel follows every change),
  the checks pill (click for the list), **Save** and a menu (save as, download/open JSON, new, import, delete).
- **Left:** **Add** (element tiles: click, or drag onto the screen where you want it), **Layers** (top = in front;
  drag to reorder; eye = shown/hidden in previews for conditional elements; ⚠ = a check issue; hover highlights on the
  screen), **Dashes** (the library with rendered thumbnails, New, From SimHub).
- **Screen:** in a bezel, with the wheel's visible area dashed (the padding). Click to select, drag to move, 8 handles
  to resize; smart snapping (screen edges/centre, the visible area, other elements) with red guides; Alt = no snap,
  Shift = straight moves / keep proportions. Right-click menu, double-click to edit the text or data, zoom
  (+/-/0, Ctrl+wheel). A click only selects (no undo step).
- **Inspector:** position & size (drag the X/Y/W/H letters to scrub), align in the visible area; text with a **font
  picker** that measures the element's widest text in every screen font ("use the biggest that fits"); colour
  swatches (palette, hex, opacity); **data picker** (searchable, grouped, plain-English, or any SimHub property /
  formula); format chips; widest-text chips; colour from data; **Show when** conditions as chips; raw JSON.
- **Keys:** arrows (Shift = 10 px), Del, Ctrl+D, Ctrl+C/V, Ctrl+Z/Y, [ ], Esc, Ctrl+S, 1/2/3 views, ? for the list.
- Development: `FXDASH_DESIGNER_DIR=<repo>\Usb\Designer fxdash serve --port 8898` serves the page's files from disk
  (edit and reload, no rebuild).

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
fxdash fit-bands DASH.json [OUT.json]    # values whose text crosses a border line: nudged or a smaller font (no flashing)
fxdash verify DASH.json [--seconds N]    # demo lap on a simulated wheel: traffic, flashes, drawing errors (exit 1 if not ok)
fxdash builtin [ID] [OUT.json]           # e.g. fxdash builtin lmgt3-mustang mustang.json
fxdash simhub | simhub-screens NAME
fxdash import NAME|PATH OUT.json [--screen S] [--fit 790,460] [--colors N] [--no-images] [--png OUT.png]
fxdash serve [--port 8899]
```

## For agents

The loop that works: read the format (`fxdash schema`), write the dash JSON, `fxdash check` it, `fxdash render` it and
look at the PNG, fix, repeat; then save it into the dashes folder (or `PUT /api/dashes/{id}`), and with SimHub running
`POST /api/wheel/show` to see it on the wheel. The skill `.claude/skills/create-dash/SKILL.md` (`/create-dash`) spells this out with
the screen's constraints. In the browser, `window.fxdash` exposes the designer's dash (`fxdash.dash`, `fxdash.load(d)`).
