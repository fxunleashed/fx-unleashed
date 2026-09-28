---
name: fxpro-dash-design
description: Design, import or fix dashes for the Simagic FX Pro screen (FXPro RPM Sync USB mode). Use when asked to make, change, check or import a dash for the FX Pro, or to turn a SimHub dash into one. Covers the dash JSON format, the screen's fonts and limits, and the check -> render -> look loop with fxdash or the designer's HTTP API.
---

# Designing FX Pro dashes

A dash is one JSON file (format: `docs/dash-format.md`) drawn on the wheel's 800x480 screen with the screen's own
commands. You can't see the wheel, but you can render exactly what it draws. Work in this loop:

1. **Learn the format once:** `fxdash schema`, `fxdash bindings`, `fxdash fonts` (or `GET /api/schema`, `/api/bindings`,
   `/api/fonts` on http://127.0.0.1:8899 while SimHub runs). Start from an example when there is one:
   `fxdash builtin lmgt3-mustang base.json`, or import a SimHub dash: `fxdash simhub` then
   `fxdash import "NAME" out.json --fit 790,460 --png out.png`.
2. **Write the JSON.** Keep it within 790 x 460 (the wheel pads 10 px left, 20 px top by default).
3. **Check:** `fxdash check dash.json --pad 10,20`. Fix every `error`; read the `warning`s.
4. **Render and look:** `fxdash render dash.json out.png` (preview texts) and
   `fxdash render dash.json out.png --mode demo --seconds 30` (a simulated lap). Open the PNG and judge it like a
   designer: alignment, spacing, contrast, what a driver reads at a glance.
5. Repeat 2-4. Then save into `<SimHub>\PluginsData\Common\FXProRpmSync\Dashes\<Id>.json` (or `PUT /api/dashes/<Id>`),
   and if SimHub runs, `POST /api/wheel/show?left=10&top=20` with the dash as body to put it on the wheel.

`fxdash` is `tools/fxdash` in the FXPro RPM Sync repo (`E:\Development\SimagicRpmSync`): `dotnet build -c Release`, then
`tools\fxdash\bin\Release\net48\fxdash.exe`. Output is JSON on stdout; `check` exits 1 when there are errors.

## Screen rules that matter

- **Text must fit its box in the screen's font**, or the screen drops it (it wraps onto a line that isn't drawn). The
  fonts are Simagic's, by id; many are wide. Use `fxdash suggest-font W H "TEXT"` for each text box, and give every
  `value` its widest `Samples` (e.g. `["8:88.888"]`, `["388"]`, `["+8.88"]`) so `check` verifies them.
- Good fonts: 963 family for numbers and labels (101 = 32 px, 98 = 40, 100 = 50, 96 = 44, 99 = 64), S 20 px (14) /
  24 px (5) for small labels, gear font 117 (119 px, only `0-9 D N P R`). Font height must be <= box height.
- Colours are 16-bit; flat colours draw fastest. Images become rectangles (few colours, small); gradients and images
  under changing text make every update slower (`check` warns). Keep `cost.StaticSeconds` under ~2 s.
- Values redraw only when they change; put them on plain backgrounds.
- Order matters: later elements draw on top.
- Conditions (`Visible`) and data colours (`ColorBind` + `ColorStops`) are live; in previews, SimHub formulas
  (`ncalc:`/`js:`) can't be evaluated, so `PreviewVisible` decides whether such elements show.

## Data

Prefer built-in keys (`speed`, `gear`, `rpm`, `rpmPercent`, `currentLapTime`, `lastLapTime`, `bestLapTime`, `delta`,
`predictedLap`, `position`, `lap`, `fuel`, `fuelRemainingLaps`, `brakeBias`, `tcLevel`, `absLevel`, `engineMap`,
`pitLimiter`...): they render in demo mode. Anything else: `prop:<SimHub property>` or a SimHub formula
`ncalc:...` / `js:...` (live only). Formats: `0`, `0.0`, `int`, `laptime`, `gear`, `delta`, `text`, `time:<fmt>`.

## Style guidance

Race dashes are read in a glance: the gear and a shift cue biggest, lap time and delta next, everything else small and
grouped. High contrast on black; one accent colour; colour only for state (delta red/green, warnings). Align to a grid
(multiples of 4 or 8 px), leave margins, and don't fill every pixel.

References: `docs/dash-format.md` (format), `docs/dash-designer.md` (designer, API, import), `docs/usb-mode.md`.
