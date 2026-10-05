# FX Pro dash format (FormatVersion 3)

> **Where this fits:** a reference for people who write dashes by hand or with an agent. If you just want to use or make dashes, start with the
> [setup guide](https://fxunleashed.com/start/) (custom firmware, plugin, the screen's RAM patch, then dashes) and the
> [dash designer](https://fxunleashed.com/docs/dash-designer/); to share yours, see [share and submit](https://fxunleashed.com/docs/library-submit/).

A dash is one JSON file: an 800x480 page of elements the plugin draws on the FX Pro's screen with the screen's own
commands (Unleashed mode, [developer reference](usb-mode.md)). Files live in `SimHub\PluginsData\Common\FXProRpmSync\Dashes\*.json`;
the designer ([dash-designer.md](dash-designer.md)), `fxdash` and the SimHub importer write them, and people or agents can
write them by hand. Code: `Usb/DashModel.cs` (model), `Usb/DashRenderer.cs` (drawing and checks).

The machine-readable version of this page: `fxdash schema` or `GET /api/schema`.

## The screen

- 800 x 480 pixels, origin top left, 16-bit colour (RGB565: colours are rounded to 5/6/5 bits).
- Without the screen's RAM patch nothing is a picture: shapes become `fill` rectangles, text is drawn by the screen in **its own
  fonts** (by id), and images are cut into rectangles of a few colours. Flat colours draw fastest. Ovals and rounded boxes
  that come and go (a `Visible` condition) are drawn by the screen itself, smoothed, in a few commands, with or without
  the patch ([screen-ram.md](screen-ram.md), "Ovals and rounded boxes"). With the RAM patch (picture
  memory, [setup guide step 3](https://fxunleashed.com/start/#3-turn-on-picture-memory)) images are kept in the screen as JPEG
  tiles instead: full colour, drawn at once ([screen-ram.md](screen-ram.md)).
- The wheel pads the whole dash (a setting, default 10 px left, 20 px top): keep the layout within
  (800 - left) x (480 - top), i.e. 790 x 460 by default. `check` with the padding reports anything pushed off.
- When the dash starts, its static layer goes out at 25 KB/s (`check`'s `cost.StaticSeconds`, ~0.6 s for the built-in
  Mustang). After that only what changes is redrawn (~1-2 KB/s).

## The file

```json
{
  "FormatVersion": 3,             // 3 = uses pages; a dash without pages is saved as 2 (older plugins load it)
  "Id": "my-dash",               // file name when saved; unique
  "Name": "My dash",             // shown in the dash list
  "Author": "", "Description": "",
  "Elements": [ ... ],           // drawn in order: later ones on top
  "Images": { "logo@120x40": "<base64 PNG>" },   // only for image elements
  "Source": "SimHub dash ...",   // set by the importer
  "ScriptsFolder": "...",        // JavaScript helpers for js: bindings (imports)
  "Pages": ["Tyres", "Delta"],   // optional: pages the driver flips through (see Pages)
  "PageSets": [ { "Name": "Laptimes", "Pages": ["Last", "Best"] } ]   // optional: more sets of pages (see Pages)
}
```

A dash is saved with the lowest format that holds what it uses: format 3 only when it has pages, 4 when it has more
than one set of pages, so a dash without them still loads in plugins from before them. A plugin refuses a dash made for a newer format rather than half-load it.

## Elements

Every element: `Type`, `Name` (shown in messages and the designer), `X`, `Y`, `W`, `H` (pixels), and optionally
`Visible`, `PreviewVisible`, `ColorBind`, `ColorStops`, `Opacity` (0-100, shapes).

| Type | What | Fields |
|---|---|---|
| `rect` | filled rectangle | `Color` |
| `ellipse` | ellipse, or a ring | `Color` (whole ellipse, or the rim when `Border` > 0), `Fill` (inside, optional), `Border`. With a `Visible` condition the screen draws it itself (smoothed, ~1.3 KB, any colour); at least 8 x 8 px, inside 8 x 8 too when it has a `Border` |
| `box` | rounded frame | `Color` (border), `Fill` (inside, optional), `Border`, `Radius`. With a `Visible` condition and `Radius` 2-24 the screen draws it itself (two fills and a smoothed circle per corner, ~150 B) |
| `gradient` | linear gradient | `Colors` (2+ stops), `Angle` (90 = top to bottom, 0 = left to right), `Radius`, `Border` + `Color` |
| `image` | picture | `Image` (key in `Images`), `MaxColors` (2-64, default 8; fewer = faster) |
| `label` | fixed text, no background | `Text`, `Font`, `Color`, `Align` (left / center / right) |
| `value` | text from data | `Bind`, `Format`, `Scale`, `Empty`, `Samples`, `PreviewText`, `Font`, `Color`, `Align`, `PositiveColor` / `NegativeColor`, `Background` |
| `bar` | gauge fill | `Bind`, `Min`, `Max` (may be below `Min`), `Orientation` (horizontal / vertical), `Reverse`, `Color` (fill), `Fill` (empty part, optional) |
| `deltabar` | segments filling from the centre | `Bind`, `Segments` (per side), `SegmentX` (left edges, 2 x Segments) or `Pitch`, `SegmentWidth`, `Range` (value of a full half), `PositiveColor` (left half, value > 0), `NegativeColor` (right half), `SegmentColor` |
| `popup` | box shown for `Duration` s when a watched value changes | `Watch` [{`Bind`, `Label`, `Color`, `Format`}], `Font` (label), `ValueFont`, `Color` (text), `Radius` |
| `dim` | while shown (`Visible`), the whole screen is darker by `Opacity` % (0-95), with the backlight: nothing is redrawn. SimHub dashes darken themselves with a see-through black layer (headlights on); the importer turns that into a `dim` | `Opacity`, `Visible` (no box) |

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
  engine while SimHub runs. Previews (preview mode) show `PreviewText` / follow `PreviewVisible` instead.
  **Library rule:** a dash in the library (or imported from a file) may only carry *checked* `js:` formulas: made of `if`/`else`,
  `return`, `var`/`let`/`const`, numbers, texts, `root.name` (memory kept between updates), the usual operators,
  `$prop('<written-out property>')` and `Math.*`, nothing else (no loops, functions, `eval`, indexing...), at most 2000 characters
  and 16 scripts per dash. `ScriptsFolder` is never allowed. The rules and the refusal messages are in `Usb/ScriptCheck.cs`;
  the library's [SCRIPTS.md](https://github.com/fxunleashed/fx-unleashed-library/blob/main/SCRIPTS.md) explains them to authors.

**Demo mode** (the Demo button, the designer's Demo lap and Show on wheel without a game, `fxdash render --mode demo`)
fills every binding from the simulated lap: built-in keys directly; `prop:`, `ncalc:` and `js:` bindings are evaluated
with the engines SimHub ships (NCalc, Jint) over simulated SimHub properties (the demo lap's speed, laps, fuel... plus
tyre/brake temperatures, pressures, wear, settings, flags and more guessed from the property's name) and SimHub's
formula functions (`isnull`, `format`, `changed`, `toshorttime`, `driver...`), loading the dash's
`ScriptsFolder` scripts. A binding that still can't be worked out (an unknown plugin's property, say) shows its
`PreviewText`, with its numbers moving a little every few seconds; such a condition follows `PreviewVisible`.
So `PreviewText` is worth setting to a typical value.

Formats: any .NET number format (`"0"`, `"0.0"`, `"0.00"`), `int`, `laptime` (m:ss.fff from seconds), `time:<fmt>`
(TimeSpan format from seconds, e.g. `time:mm\:ss\.fff`), `gear` (R / N / number), `delta` (+0.00 / -0.00), `text`.
`Scale` multiplies before formatting.

### Conditions and data colours

- `Visible`: a binding or a list; the element shows while all are true (a number other than 0, `true`, a text other
  than "", "0", "false"). Hiding it redraws what was under it.
- `PreviewVisible`: `false` = hidden in previews where the condition isn't evaluated (SimHub formulas), and in the
  demo where it can't be.
  The importer sets it for pop-ups and warnings.
- `page:N` in `Visible`: the element is on page N (0 = the first) of the dash's `Pages`; `page2:N`, `page3:N`,
  `page4:N`: on page N of another set of pages. See Pages.
- `ColorBind`: a binding giving a colour (`"#FF0000"`, a colour name) or a number mapped through `ColorStops`
  `[{"Value": 0, "Color": "#00FF00"}, {"Value": 100, "Color": "#FF0000"}]` (blended between). Replaces `Color` for
  text, rects and bars, the fill for boxes/ellipses with a `Fill`.

### Pages

A dash can have pages the driver flips through while driving, like a SimHub widget whose screens are flipped with its
screen commands (the Mustang flips its strip above the settings row between tyres / brakes and the delta bar):

- `"Pages": ["Tyres", "Delta"]` names them, in order.
- An element on a page has the condition `"page:N"` (0 = the first) in `Visible`, with any other conditions it has.
  Elements without one show on every page.
- The driver flips with a wheel button (Dashes tab, "Next page" / "Previous page"), or SimHub's actions
  `FXProRpmSyncPlugin.UsbDashNextPage` / `UsbDashPreviousPage`; the dash comes back on the page it was left on. The
  property `FXProRpmSyncPlugin.UsbDashPage` gives the page's name.
- A flip is drawn like any condition: what leaves is put back, what comes is drawn, the rest stays. Keep a page to one
  area (a strip, a panel) and a flip is a few KB. `fxdash verify` flips the pages during its lap and reports
  `PageFlips` / `WorstPageFlipBytes`; checks never call elements on different pages overlapping.
- Previews show page 0 (`fxdash render --page N`, `/api/render?page=N`, the designer's page buttons).

#### More sets of pages

Parts of a dash can flip on their own, like SimHub widgets on different screen commands (the AMR flips its fuel, lap
times and tyres with three buttons):

- `"PageSets": [ { "Name": "Laptimes", "Pages": ["Last", "Current", "Predicted", "Best"] }, ... ]`: up to three more
  sets. The first is set 2, and its elements carry `"page2:N"`; then `"page3:N"`, `"page4:N"`. `Pages` / `"page:N"`
  stay set 1. An element can be on a page of more than one set.
- "Next page" / "Previous page" flip every set (each wraps on its own pages); "Next page 1" / "Previous page 1" to
  "Next page 4" (wheel buttons, or SimHub's `UsbDashNextPage1` / `UsbDashPreviousPage1` to `...4`) flip one set. They're
  generic: bound once, "Next page 2" flips set 2 of any dash that has one. Each set comes back on the page it was left on.
- `page2` (to `page4`) gives that set's page now, as `page` does for set 1.
- Format 4 (plugin 0.7.0). The importer makes each widget on its own screen commands a set, numbered by the commands
  (the lowest first), so set 1 is the widget on SimHub's first commands.
- Checks never call elements on different pages of the same set overlapping (on different sets they can show
  together). `fxdash verify`, `render --page N` and the designer's page buttons show flip N: page N of every set, each
  wrapping on its own pages, so flips 0 to the biggest set's count show every page of every set.

## Checks

`fxdash check DASH.json --pad 10,20` / `POST /api/check?left=10&top=20` returns `issues` (level `error` = the screen
will show it wrong, `warning` = costly or doubtful) and `cost` (`StaticSeconds`, `StaticFills`, values on busy
backgrounds). Errors: text wider/taller than its box, missing glyphs, unknown types/fonts/images, text off the screen.
Warnings: overlapping always-shown text, values without `Samples`, unknown data keys, busy value backgrounds, slow
static layers.

`fxdash verify DASH.json [--tiles] [--overlays] [--demo]` plays a demo lap on a simulated screen (traffic, flashes, drawing
errors). `--demo` runs exactly what Demo on the wheel shows (the lap with the overlays taking turns, every other overlay held off
during a turn) and lists updates where something came or went and other things were redrawn (`ChangeFlashes`) and the
costliest ones (`SlowChanges`); `FXDASH_TRACE_AT=<seconds>` or `=FROM-TO` prints every command those updates send. `--overlays` then brings up every overlay in turn (each set of conditions elements share: flags, pit
screens, warnings, ignition screens) on every page while the lap keeps running, the way the demo on the wheel does:
what each sends when it comes and goes, and whether anything flashes while it shows. An overlay over most of the
screen can't come or go for less than drawing the dash takes, so those are listed as `Notes`, not problems.
`fxdash pictures DASH.json` lists what goes on the screen's RAM drive for shapes that come and go: each gradient or
picture with a condition is kept there drawn over its own overlay (and over the overlays inside it), so it shows with
one command. Ovals and rounded boxes (`ellipse`, `box` with a `Radius` of 2-24) with a condition need no picture:
the screen draws them itself, smoothed, in a few commands (any colour, nothing on the RAM drive). A ring with no
`Fill`, or one with `Opacity` under 100, needs one colour under it (its middle, or all around it); else it's drawn with
rectangles as before. A shape always shown (no `Visible`) is drawn as before too: text redrawn over its smoothed edge
all the time would cost a rectangle a pixel.

## Sharing a dash

A dash is one self-contained JSON file (its pictures are inside as base64), so sharing is just sending the file.

- **A file for a friend:** in the plugin's Dashes tab pick your dash and press **Share...**. It writes `<id>.fxdash.json`
  and puts the file on the clipboard, so you can paste it into Discord or an email. The other person drops it onto the
  Dashes tab, or presses **Import a file...**. The plugin applies the same rules as for the library (format, size, no
  scripts, nothing newer than it understands); an imported dash never overwrites one you have: if its id is taken it
  gets a new one. Converted work (a SimHub import) asks you to confirm you may share it.
- **A link for a library item:** **Copy link** on a library item gives `fxunleashed.com/library/#dash-<id>`: a page with
  the preview, an Install button that talks to the plugin on that PC, and a download.
- **Everyone:** **Package for the library** writes `<id>.fxdash.zip`. Attach it to the library's
  [Submit a dash](https://github.com/fxunleashed/fx-unleashed-library/issues/new?template=submit-dash.yml) form (no Git
  needed, but a free GitHub account is: the form is a GitHub page). If it passes the checks it is published on the spot; to
  update it later, submit again with a higher Version. The steps: [share and submit](https://fxunleashed.com/docs/library-submit/).

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
