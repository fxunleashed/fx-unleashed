# FX Pro dash format (FormatVersion 2)

A dash is one JSON file: an 800x480 page of elements the plugin draws on the FX Pro's screen with the screen's own
commands (USB mode, [usb-mode.md](usb-mode.md)). Files live in `SimHub\PluginsData\Common\FXProRpmSync\Dashes\*.json`;
the designer ([dash-designer.md](dash-designer.md)), `fxdash` and the SimHub importer write them, and people or agents can
write them by hand. Code: `Usb/DashModel.cs` (model), `Usb/DashRenderer.cs` (drawing and checks).

The machine-readable version of this page: `fxdash schema` or `GET /api/schema`.

## The screen

- 800 x 480 pixels, origin top left, 16-bit colour (RGB565: colours are rounded to 5/6/5 bits).
- Nothing is a picture: shapes become `fill` rectangles, text is drawn by the screen in **its own fonts** (by id), and
  images are cut into rectangles of a few colours. Flat colours draw fastest.
- The wheel pads the whole dash (USB mode setting, default 10 px left, 20 px top): keep the layout within
  (800 - left) x (480 - top), i.e. 790 x 460 by default. `check` with the padding reports anything pushed off.
- When the dash starts, its static layer goes out at 25 KB/s (`check`'s `cost.StaticSeconds`, ~0.6 s for the built-in
  Mustang). After that only what changes is redrawn (~1-2 KB/s).

## The file

```json
{
  "FormatVersion": 2,
  "Id": "my-dash",               // file name when saved; unique
  "Name": "My dash",             // shown in the dash list
  "Author": "", "Description": "",
  "Elements": [ ... ],           // drawn in order: later ones on top
  "Images": { "logo@120x40": "<base64 PNG>" },   // only for image elements
  "Source": "SimHub dash ...",   // set by the importer
  "ScriptsFolder": "..."         // JavaScript helpers for js: bindings (imports)
}
```

## Elements

Every element: `Type`, `Name` (shown in messages and the designer), `X`, `Y`, `W`, `H` (pixels), and optionally
`Visible`, `PreviewVisible`, `ColorBind`, `ColorStops`, `Opacity` (0-100, shapes).

| Type | What | Fields |
|---|---|---|
| `rect` | filled rectangle | `Color` |
| `ellipse` | ellipse, or a ring | `Color` (whole ellipse, or the rim when `Border` > 0), `Fill` (inside, optional), `Border` |
| `box` | rounded frame | `Color` (border), `Fill` (inside, optional), `Border`, `Radius` |
| `gradient` | linear gradient | `Colors` (2+ stops), `Angle` (90 = top to bottom, 0 = left to right), `Radius`, `Border` + `Color` |
| `image` | picture | `Image` (key in `Images`), `MaxColors` (2-64, default 8; fewer = faster) |
| `label` | fixed text, no background | `Text`, `Font`, `Color`, `Align` (left / center / right) |
| `value` | text from data | `Bind`, `Format`, `Scale`, `Empty`, `Samples`, `PreviewText`, `Font`, `Color`, `Align`, `PositiveColor` / `NegativeColor`, `Background` |
| `bar` | gauge fill | `Bind`, `Min`, `Max` (may be below `Min`), `Orientation` (horizontal / vertical), `Reverse`, `Color` (fill), `Fill` (empty part, optional) |
| `deltabar` | segments filling from the centre | `Bind`, `Segments` (per side), `SegmentX` (left edges, 2 x Segments) or `Pitch`, `SegmentWidth`, `Range` (value of a full half), `PositiveColor` (left half, value > 0), `NegativeColor` (right half), `SegmentColor` |
| `popup` | box shown for `Duration` s when a watched value changes | `Watch` [{`Bind`, `Label`, `Color`, `Format`}], `Font` (label), `ValueFont`, `Color` (text), `Radius` |

Colours: `"#RRGGBB"`, `"#AARRGGBB"` (alpha blends shapes over what's under them) or colour names.

### Text: fonts and boxes

- `Font` is a screen font id: `fxdash fonts` / `GET /api/fonts` lists all 128 with height and characters. A font's
  height must fit the box (`H`), and every text it shows must fit the width (`W`): text wider than its box wraps onto a
  line the screen doesn't show, so it just disappears.
- Real advance widths are in `FontMetrics.cs`; many of Simagic's fonts are wide (in the "S" fonts a digit is as wide as
  the font is tall). Narrow, readable families: 963 (ids 96-101; 101 = 32 px, 98 = 40, 100 = 50), isf23 (92-95), 992
  (69-72). Gear fonts (`0-9 D N P R` only): 117 (119 px), 102 (128), 38 (104).
- `fxdash suggest-font W H TEXT` / `GET /api/fonts/suggest` picks the tallest font whose TEXT fits a W x H box.
- Values: give `Samples` (the widest texts it can show, e.g. `["8:88.888"]`) so `check` can verify them; `Empty` is
  shown when there's no data; `PreviewText` is what previews show.
- Vertical alignment is always centred in the box.
- Value backgrounds are automatic: the one colour under the box, or, over an image/gradient, the area is redrawn before
  each new text (slower: `check` warns). `Background` forces a colour.

### Data: bindings and formats

`Bind`, `ColorBind`, `Visible` and pop-up watches take a binding:

- a key: `speed`, `gear`, `gearText`, `rpm`, `maxRpm`, `rpmPercent`, `throttle`, `brake`, `clutch`, `currentLapTime`,
  `lastLapTime`, `bestLapTime` (s), `delta` (s to session best, + = slower), `predictedLap`, `position`, `lap`,
  `completedLaps`, `fuel`, `fuelPercent`, `fuelLastLap`, `fuelThisLap`, `fuelRemainingLaps`, `virtualEnergy` (LMU, %),
  `brakeBias`, `absLevel`, `tcLevel`, `tcCut`, `tcSlip` (LMU), `engineMap`, `sessionTypeName`, `waterTemp`, `oilTemp`,
  `absActive`, `tcActive`, `pitLimiter`, `gameRunning` (full list with descriptions: `fxdash bindings`);
- `"prop:<SimHub property>"`, e.g. `"prop:DataCorePlugin.GameRawData.PlayerNativeTelemetry.mVirtualEnergy"`;
- a SimHub formula: `"ncalc:<NCalc>"` (e.g. `"ncalc:[DataCorePlugin.GameData.NewData.Rpms] > 7000"`) or
  `"js:<JavaScript>"` (a function body with `return`, SimHub's `$prop('...')` available). Evaluated by SimHub's own
  engine while SimHub runs (not in fxdash/offline previews, which use `PreviewText`/`PreviewVisible` instead).

Formats: any .NET number format (`"0"`, `"0.0"`, `"0.00"`), `int`, `laptime` (m:ss.fff from seconds), `time:<fmt>`
(TimeSpan format from seconds, e.g. `time:mm\:ss\.fff`), `gear` (R / N / number), `delta` (+0.00 / -0.00), `text`.
`Scale` multiplies before formatting.

### Conditions and data colours

- `Visible`: a binding or a list; the element shows while all are true (a number other than 0, `true`, a text other
  than "", "0", "false"). Hiding it redraws what was under it.
- `PreviewVisible`: `false` = hidden in previews where the condition can't be evaluated (SimHub formulas offline).
  The importer sets it for pop-ups and warnings.
- `ColorBind`: a binding giving a colour (`"#FF0000"`, a colour name) or a number mapped through `ColorStops`
  `[{"Value": 0, "Color": "#00FF00"}, {"Value": 100, "Color": "#FF0000"}]` (blended between). Replaces `Color` for
  text, rects and bars, the fill for boxes/ellipses with a `Fill`.

## Checks

`fxdash check DASH.json --pad 10,20` / `POST /api/check?left=10&top=20` returns `issues` (level `error` = the screen
will show it wrong, `warning` = costly or doubtful) and `cost` (`StaticSeconds`, `StaticFills`, values on busy
backgrounds). Errors: text wider/taller than its box, missing glyphs, unknown types/fonts/images, text off the screen.
Warnings: overlapping always-shown text, values without `Samples`, unknown data keys, busy value backgrounds, slow
static layers.

## Example

```json
{
  "FormatVersion": 2, "Id": "minimal", "Name": "Minimal",
  "Elements": [
    { "Type": "box", "Name": "frame", "X": 250, "Y": 40, "W": 290, "H": 250, "Color": "#428AED", "Border": 4, "Radius": 16 },
    { "Type": "value", "Name": "gear", "Bind": "gear", "Format": "gear", "X": 300, "Y": 60, "W": 190, "H": 125, "Font": 117,
      "Color": "#FFFFFF", "Align": "center", "Samples": ["N", "R", "8"], "Empty": "N" },
    { "Type": "value", "Name": "speed", "Bind": "speed", "Format": "0", "X": 300, "Y": 200, "W": 190, "H": 50, "Font": 100,
      "Color": "#D3D3D3", "Align": "center", "Samples": ["388"] },
    { "Type": "bar", "Name": "revs", "Bind": "rpmPercent", "Min": 60, "Max": 100, "X": 20, "Y": 320, "W": 750, "H": 30,
      "Color": "#00FF40", "Fill": "#202020",
      "ColorBind": "rpmPercent", "ColorStops": [{ "Value": 80, "Color": "#00FF40" }, { "Value": 95, "Color": "#FF0020" }] },
    { "Type": "label", "Name": "pit", "Text": "PIT LIMITER", "X": 280, "Y": 380, "W": 230, "H": 40, "Font": 98,
      "Color": "#0040FF", "Align": "center", "Visible": "pitLimiter" }
  ]
}
```
