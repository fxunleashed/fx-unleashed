---
name: create-dash
description: Create a dash for the Simagic FX Pro wheel screen (FX Unleashed, Unleashed mode) end to end, from a reference image or from a SimHub dash, and get it to run on the wheel with no flashing and low USB traffic. Use for any request to make, convert, fix or tune an FX Pro dash.
argument-hint: "<reference image path | SimHub dash name> [dash name]"
---

# /create-dash: an FX Pro dash, end to end

Input: `$ARGUMENTS`. It is either a **reference image** (a photo or mock-up of a dash) or a **SimHub dash** (a name
from `fxdash simhub`, or a `.djson` / `.simhubdash` path). A second argument, if any, is the dash's name.

The finished dash is one JSON file. It must pass four gates before you hand it over:
1. `fxdash check` has no errors **and no warnings** (the designer shows warnings to users: a dash that ships has none);
2. `fxdash fit-bands` changes nothing;
3. `fxdash verify` says `"Ok": true`;
4. the renders look right.

Don't stop at "it renders". The wheel draws on a slow screen, and the look alone hides the things that ruin it
(flashing text, lag).

## 0. Setup

Everything runs from the plugin repo's root. It needs SimHub installed, not running.

```
dotnet build -c Release tools/fxdash/fxdash.csproj
set FX=tools\fxdash\bin\Release\net48\fxdash.exe        (bash: FX=tools/fxdash/bin/Release/net48/fxdash.exe)
```

Every command prints JSON on stdout. In Git Bash, redirect it to a file before parsing it: piping it straight into
another program can lose the output. Work in a scratch folder; the final file goes to
`C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSync\Dashes\<Id>.json`.

Read once: `docs/dash-format.md` (the format), `fxdash schema`, `fxdash bindings`. For a template that passes every
gate, use `docs/examples/example-gt.json`: gear in a frame, labelled values, lap times, a fuel bar, and a pop-up done
right.

## 1a. From a SimHub dash

```
$FX simhub > dashes.json                    # installed dashes: find the name
$FX simhub-screens "NAME"                   # screens; the importer picks the main in-game one unless --screen
$FX import "NAME" out.json --fit 790,460 --png out.png > report.json
$FX tune out.json > tune.json               # the automatic fixes below (1-5); lists what it changed
```

- `--fit 790,460` scales it into the area the wheel shows: 800x480 minus the default 10 px left and 20 px top
  padding. Always use it.
- Read `report.json`: `report.SkippedTypes` (what couldn't be converted), `report.Notes` (what the importer changed),
  `check`. SimHub items with no FX Pro equivalent (maps, graphs) are skipped; say which ones in your summary.
- What the importer carries over by itself (check it in the report, don't redo it):
  - **pages**: widget screens the original flips with a pair of commands (Redadeg's `ueberLeiste`: Tyres / Delta) become
    `Pages` with `"page:N"` conditions; only widgets flipped by the same command pair count (a "Low NRG" widget with its
    own commands stays an overlay);
  - **overlay screens** (flags, pit, ignition): imported with their trigger condition and a background rect;
  - **blinking** (`BlinkEnabled`): `ncalc:blink('name-n', delay ?? 250, true)` (SimHub's default delay is 250 ms);
  - **layer opacity** multiplies into the colours; a big black see-through rect becomes a `dim` element (the screen's
    backlight goes down while it shows: `DimPercent`);
  - **leaderboard items** (driver ahead/behind in class, names, best laps, gaps) as `ncalc:` formulas with SimHub's
    leaderboard functions; speed text as `ncalc:[SpeedLocal]` (the user's unit).
- **NCalc strings: a backslash escapes.** A .NET time format inside a formula needs it doubled
  (`'mm\\:ss\\.fff'`), or SimHub fails live with "no viable alternative at character ':'" while the demo looks fine.
  The importer doubles them; anything you write by hand, check with SimHub's parser (the UsbTest check "formulas
  parse" in `tools/UsbTest/PagesTests.cs` runs every formula of a dash through it).
- The importer hides pop-ups in previews (`PreviewVisible: false`), shrinks colliding labels and values, and turns
  `library:` images into rectangles. Look at `out.png`, and at the SimHub dash's own picture (`<name>.djson.png` in
  `C:\Program Files (x86)\SimHub\DashTemplates\<dash>\`).
- Run `$FX verify out.json` right away as a baseline. It tells you where the import costs and flashes.

**An import never passes as it comes out.** `fxdash tune` does the fixes every import needs:
- samples from a demo lap;
- fonts re-picked;
- labels that fit no font widened;
- the gear font;
- value text running into other text trimmed;
- pictures that toggle often (ABS/TC working icons) turned into coloured labels or lamps;
- the same value drawn twice in two colours under two conditions (SimHub's delta colouring) merged into one value
  with a `ColorBind`;
- anything `check` still calls an overlap: the value's box trimmed from the side that loses least, a smaller font
  if needed, or the label moved a few px;
- values on a busy background (over 80 fills per change) given a plain `Background`, the colour under most of it.

The importer leaves out fixed text turned on its side (SimHub's `Rotation` 90/270: watermarks along a panel's edge);
the screen can't draw it turned. Labels too long for their box lose a bracketed unit first, then get abbreviated.
`fit-bands`, when no nudge or smaller font keeps a value's text off a line, gives the value a solid `Background`.

Run it first, then the loop in 3. What's left after it, put in a tune script, not hand edits: re-importing and
re-tuning is then one command, and the reasons stay written down. The worked example is
`docs/examples/lmgt3-mclaren-tune.py`. Its import went from 16 KB/s, 727 of 1200 updates flashing and RPM showing
"573", to 5 KB/s, no flashes, and a gear as big as the original's. What every import needs, and what `tune` automates:

1. **Real `Samples` for every value.** The importer copies SimHub's preview text, which is often `"0"`, so `check`
   can't tell that "5730" doesn't fit: the screen dropped the last digit and showed "573". Write each value's widest
   real text (`"8888"` rpm, `"388"` speed, `"8:88.888"` lap, `"+88.88"` delta, `"88.8"` bias).
2. **Fonts per value** with `suggest-font`, preferring the narrow 963 family. The importer often picks the wide "S"
   fonts, which show "59" as "5 9".
3. **Labels** that fit no font (`suggest-font` returns `-1`): the screen's small fonts are wide. Widen the box around
   its centre if there's room, or shorten the text.
4. **The gear**: give it the whole column it sits in, and a gear font (102 = 128 px, 117 = 119 px).
5. **Whatever `verify` flags** (see 3.), typically icons toggled by fast conditions, and value boxes running into
   the label next to them.

Then go on at 2.

## 1b. From a reference image

1. Look at the image (Read tool). Name every item: what it shows (speed, gear, lap time...), where it is, its colour,
   how big it is.
2. Scale the image to the drawable area and put a grid on it, so you can read coordinates:
   ```python
   from PIL import Image, ImageDraw
   im = Image.open("ref.png").convert("RGB"); im.thumbnail((790, 460))
   c = Image.new("RGB", (790, 460)); c.paste(im, ((790 - im.width) // 2, (460 - im.height) // 2))
   d = ImageDraw.Draw(c)
   for x in range(0, 790, 40): d.line([(x, 0), (x, 459)], fill=(60, 60, 60))
   for y in range(0, 460, 40): d.line([(0, y), (789, y)], fill=(60, 60, 60))
   c.save("ref_grid.png")
   ```
3. Map each item to an element (`value` with a built-in key from `fxdash bindings`, `label`, `box`, `rect`, `bar`...).
   Snap positions to a 4 px grid, and give every text box room (see the rules below).
4. Write the JSON, starting from `docs/examples/example-gt.json`.
5. After rendering (step 4 below), compare side by side:
   ```python
   a = Image.open("ref_grid.png"); b = Image.open("dash.png").crop((0, 0, 790, 460))
   w = Image.new("RGB", (790, 930)); w.paste(a, (0, 0)); w.paste(b, (0, 470)); w.save("compare.png")
   ```

## 2. The rules

The screen is a serial display fed at **25 KB/s**. It draws text in **its own fonts**, and redraws a changing value
by sending new drawing commands. Everything the renderer does is built around that. These rules are what separates a
dash that flashes and lags from one that doesn't. They were all measured on the wheel.

### Text boxes

- **Font height must fit the box, and every text must fit its width.** Text wider than its box is wrapped onto a line
  the screen doesn't show, so it silently disappears. Give every `value` its widest `Samples` (`["8:88.888"]`,
  `["388"]`, `["+88.88"]`) and a typical `PreviewText`.
- **Pick fonts with `$FX suggest-font W H "WIDEST TEXT"`.** Useful ones (height, full ASCII unless noted):
  - 963 family, narrow, for numbers: 101 = 32 px, 98 = 40, 96 = 44, 97 = 46, 100 = 50, 99 = 64.
  - Small labels: 10 / 12 = 16 px, 14 = 20 px, 60 = 24 px (the least wide), 5 = 24 px, 2 = 28 px. **There is no
    narrow font under 32 px.** Small labels look letter-spaced ("L A S T  L A P"); that's the screen, not a bug.
    Keep them short.
  - Big: 92 = 70, 90 = 80, 35 = 100.
  - Gear: 102 = 128 px, 117 = 119 px, digits `0-9 D N P R` only, so give a gear value `"Empty": "N"`.
  - Not every font has every character: `fxdash fonts` lists each font's `chars`. A missing glyph (`%`, `.`, `-`) is
    a `check` error, because the text would vanish.
- **Leave clear rows between text and any line.** The screen draws a font's full height as a band centred in the
  box. If a box border, a divider or another element's edge runs through that band, every update has to wipe the
  line and draw it again, and the wheel shows that as a flash. Make value boxes smaller than the frame around them,
  with at least 2 px between the font band and the frame's border on every side. `fit-bands` fixes what you miss.
- **One screen command is at most 58 characters**, and the box, font and colours take ~40 of them: a label of 15-20
  characters can already be cut, even in a box wide enough ("PRESS IGNITION TO START" showed "PRESS IGNITION"; SLIPSTREAM's
  "THIS LAP WON'T COUNT" on its red panel "THIS LAP WON'T "). `check` reports it ("is cut to its first N characters").
  Keep texts short, or split one into two labels with the same `Visible` (they blink together), both left-aligned:
  the first at the line's start, the second at start + width(whole) - width(tail). The screen's widths add up (parts +
  the space = the whole, `fxdash fonts --sample`), so the line reads as one, still centred.

### Keep changing text on flat colour

- A value is redrawn in one step (its text band with its background) only when everything under that band is **one
  colour**. Over an **image, gradient or anything multicoloured**, it has to wipe the area, redraw what's under it,
  then draw the text. That's slow, and it flashes. Put images and gradients only under static labels, never under
  values.
- **Text on a bar** (a number printed on its gauge) is drawn on the bar's colour at the text's centre, as a solid
  band: the bar doesn't show behind the digits pixel by pixel. It doesn't flash, but it looks like a box on the bar.
  Prefer labels and values beside bars.
- **Don't overlap boxes.** A value's box must not overlap another element's box, even by 1 px. Keep 2+ px between
  neighbours. (One speed box overlapping a headlight icon cost 250 KB/s on the Toyota import; the McLaren's ARB
  values ran 12 px into the label below and flashed with it.) `check` warns "X overlaps Y". Fix every one where X
  or Y changes.
- **Icons that come and go often** (shown while ABS/TC works, blinking warnings) redraw their whole picture at every
  toggle. The McLaren's ABS icon alone cost 10 KB/s. Use a coloured `label` or a plain shape for those; keep
  pictures for things that stay.
- Elements are drawn in order, later on top. Frames and backgrounds go first, then their text.

### Shapes: let the screen draw ovals and rounded boxes

The screen smooths two of its own commands: `draw_h` (the gauge needle, really any six-point shape symmetric about its
axis) and `cirs` (a filled circle). The plugin builds ovals and rounded boxes out of them, so they need no picture, no
RAM and no colour reduction (docs/screen-ram.md, "Ovals and rounded boxes"):

- **An `ellipse`, or a `box` with `Radius` 2-24, that has a `Visible` condition is drawn by the screen itself**: an oval
  is plain fills inside plus thin smoothed bands along its edge (~1.3 KB; a ring of rim and fill ~2.8 KB), a rounded box
  two fills and a `cirs` per corner (~150 B). Any colour, so a `ColorBind` with `ColorStops` (a speed-coloured pit
  oval) costs nothing on the RAM drive; a colour change is ~2-6 KB (the old smoothed edge is wiped first).
- So **draw ovals, rings, frames and panels as `ellipse` / `box` elements, never as images.** An imported SimHub dash
  whose oval or frame is a PNG: replace it with the shape (keep pictures for real artwork: a logo, an icon).
- **A ring (`Border`) is the outer shape in its rim colour, then the inner one in its `Fill`.** A ring with no `Fill`
  paints its middle with the one colour under it, so put it over a filled shape (the Mustang's black ring over the
  coloured pit oval of the same overlay); over anything multicoloured it falls back to rectangles: ~37 KB for a big
  oval. The same for `Opacity` under 100: it needs one plain colour all around it (mixed into its colours).
- **Sizes:** an oval at least 8 x 8 px (its inside 8 x 8 too, with a border); a box's `Radius` at most 24 (a `cirs`
  corner smooths its whole disc: bigger corners cost too much screen time, so they're drawn with rectangles).
- **Shapes always shown** (no `Visible`) are drawn as before, with plain hard-edged fills: values redrawn over a smoothed
  edge all the time would cost a rectangle a pixel (HALO's delta disk tripled its traffic that way). Static ones go
  into the static layer (tiles with the RAM patch, fills without). An always-shown oval may still change colour; it
  just isn't smoothed.
- **Keep text off a shape's edge.** A value whose text band touches a smoothed edge (the inside of a ring) has a
  background of many colours: every change repaints. 2 px clear of the edge; `fit-bands` nudges what you miss.
- **Screen time:** smoothing costs ~2 us a pixel (a plain fill ~0.07); the plugin paces it like bytes. An overlay of
  three or four stacked big ovals (the Mustang's pit screen) takes ~0.3 s to come up. Fine for overlays; for something
  that toggles every update (a blinking oval), blink a label on it instead.

### Pop-ups (setting changed, flags, lap summary)

- A pop-up is a `box` or `rect` with an **opaque `Fill`**, then its label and value on top. All of them carry
  **exactly the same `Visible` list** and `"PreviewVisible": false`, and come **after** everything they cover in
  `Elements`.
- **Never an outline-only pop-up box** (no `Fill`) over values: everything under it keeps updating through it, and
  the pop-up flashes.
- **A pop-up must fully cover the values it overlaps, or not touch them.** A value half under a pop-up has to redraw
  the pop-up after every update, so it flashes. Size pop-ups to whole panels or cells.
- Stacked pop-ups in one spot (TC, ABS, bias...) are fine; the same size each is best.
- **A plain banner with a border** (SimHub's text box with a background and a border: PIT LIMITER): two `rect`s, the
  border colour, then the fill inset by the border. A `box` without `Radius` that comes and goes becomes a picture on
  the RAM drive (the Ginetta's banner: 7 KB); `rect`s are fills and take none.
- **An overlay that covers SimHub's whole width covers the wheel's** (0..790; whole screens 0,0 790x460), not just the
  dash's scaled width (12..778 for 1200 x 720). Values in the side margins are otherwise half under it and flash (the
  Ginetta's oil and water numbers under the pit banner: 15 flashes in the demo).
- **A shape the original draws under a cell's frame** (a red "TC working" box under the cell's purple frame): a frame
  that's always shown is not drawn again over a shape that comes and goes, so inset the shape inside the frame's border.
- Conditions: `"ncalc:changed(2000, [BrakeBias])"` shows for 2 s after a change. Flags: `ncalc:[Flag_Yellow]` etc.
  Built-in keys work too (`"pitLimiter"`).

### Data

- Prefer built-in keys (`fxdash bindings`). Otherwise use `prop:<SimHub property>`, or a SimHub formula
  (`ncalc:` / `js:`), exactly as in SimHub dashes. Live, SimHub evaluates them. In the demo they're evaluated over
  simulated data. What the demo can't simulate (another plugin's properties) shows `PreviewText`, so set it to a
  realistic value.
- Formats: `0`, `0.0`, `int`, `laptime`, `delta`, `gear`, `text`, `time:<fmt>`. Don't show more decimals than a driver
  reads: every visible change of a digit is a redraw. Temperatures `0`, pressures `0.0`, times `laptime`.
- Colour: 16-bit (RGB565). Colours by value: `ColorBind` + `ColorStops`. Delta colours: `PositiveColor` /
  `NegativeColor`.
- **SimHub's format string applies to numbers only**: a formula that returns text (`format([x], '0.0')`) is shown as it
  is. The wheel formats numeric text too ("26.9" under `0.00` becomes "26.90"). When an item's formula returns text for
  some cases (tyre pressures per unit), format every case in the formula and use `"Format": "text"`.
- **A zero lap time:** SimHub formats it ("0.00.000" before the first lap); the wheel shows the value's `Empty` for a
  time of 0, and imports leave `Empty` blank. Set `Empty` to the zero time in the original's format. The gates don't
  see this; a parity script should (ginetta_g61_parity.py does).

### Static cost

- Images are cut into rectangles of a few colours (`MaxColors`, default 8). Big multicolour images and gradients
  make the dash slow to appear. `check`'s `cost.StaticSeconds` should stay under ~2 s.

## 3. Check and fix: the loop

```
$FX check dash.json --pad 10,20 > check.json      # errors: text that doesn't fit, missing glyphs, off-screen...
$FX fit-bands dash.json                           # nudges values or picks a slightly smaller font so text rows
                                                  # clear border lines; rewrites the file, lists changes
$FX verify dash.json > verify.json                # 120 s demo lap on a simulated wheel (exit 1 if not ok)
$FX verify dash.json --overlays --tiles > v2.json # then every overlay in turn on every page, as on a wheel with
                                                  # the RAM patch; drop --tiles for a wheel without it
$FX verify dash.json --tiles --demo --seconds 300 > v3.json   # exactly the wheel's demo: the lap with the overlays
                                                  # taking turns, several at once as it happens there
```

**`--demo`** is what the user sees when they press Demo, and what to run when they report flashes or redraws in it. The
demo (`OverlayShowcase.Apply`) gives each overlay a 3 s turn with 2 s of plain dash between; during a turn every other
overlay is held off (its conditions false, its take-turns negations true), so the lap's own setting pop-ups and lap
summaries never stack on a flag or pit screen. Only the demo does that: on a real drive overlays come as the data says.
`ChangeFlashes`: updates where something came or went and pixels *outside* what changed were wiped and drawn again (the
elements there in `At`); `SlowChanges`: the costliest of those updates. `FXDASH_TRACE_AT=<seconds>` (or `FROM-TO`) prints
every command those updates send and why (repaints, pictures used or not, what got marked). What it found in the Mustang
(2026-10-04), all renderer fixes now: a blinking label put back four whole tiles each blink (its box's solid background
wasn't seen because the repaint used the label's whole box, into the rounded corners, instead of its text); a pop-up
going under the pit screen repainted everything because rounded boxes never counted as covering; boxes that can't have
a picture (a bar under them) were drawn anti-aliased, three times the rectangles. After the fixes its 300 s demo has no
flashing update and a busiest second of 7.5 KB (was 23.5 KB); what's left is an 8-pixel sliver over the TC labels when
a pop-up's area comes back from tiles (fills there would take ~300 rectangles: slower, so tiles stay).

What the renderer does for these now (so a dash doesn't need to work around them):
- an element that hides puts back only its text's ink (a label) or its box (a shape);
- a repaint looks first for a solid shape still shown under all of the area (an overlay's box): then only that box and
  what's on it are drawn, never tiles or static labels;
- a rounded box counts as covering all but its corners (`SolidParts`): what goes away under it is put back only where
  it shows.
- an oval going from over an oval the screen draws that holds all of it (a pop-up over the dash's own disc): that one is
  drawn again over it, wiped first in the one colour around it, not its box put back; once per update;
- an oval the screen draws that can't be wiped clean (nothing of one colour round it) is never drawn again over itself
  (its edge would darken): only its drawn-over part, with fills, or its whole area put back first;
- a shape with no pixels where something was drawn (an oval's box corner under a blinking arrow) isn't marked;
- a step of RGB565 off a text band's colour (where smoothed shapes meet) is taken as that colour, not patched;
- in `verify`, the first update (every value drawn for the first time) is loading, not the busiest second, and a page
  flip in the same update as an overlay's coming or going isn't counted as the flip's cost.

**A dash with overlays** (flags, pit screens, warnings, start-up screens: most SimHub imports) needs `--overlays`: the demo
lap never reaches most of them. It brings each one up over the running lap (its parent first, staged items on their
timing) and reports, per overlay and page, `ShowBytes` / `HideBytes` (with `ShowBy` / `HideBy`: what sent them),
flashes while it shows, and drawing errors. `Events` lists every condition change of the plain lap with its bytes.
Diagnostics: `FXDASH_TRACE=<text in an overlay's name>` prints what its updates draw and why (`FXDASH_TRACE_ALL=1`:
every update; `FXDASH_TRACE_CMDS=1`: every command too; lines like `#154 drawn by the screen (57 commands)` or `drawn
again where it was drawn over` are the screen-drawn shapes); `FXDASH_DUMP=<folder>` saves both screens
(`incremental.png`, `full.png`) when they stop matching, in the lap and in the sweep; `fxdash pictures dash.json` lists
the RAM-drive pictures of shapes that come and go; `FXDASH_NATIVE=0` runs any fxdash command with ovals and boxes as
pictures and fills, as before (compare traffic and RAM both ways). The rules it taught (all in tools/dashes/make_mustang.py):
- a value or bar half under an overlay's box hides while it shows ("take turns": `ncalc:!(<the box's conditions>)`);
  a value fully under it needs nothing;
- a value on its own overlay's box gets the box's colour as `Background`;
- a value box must not reach into the overlay's own lines or small labels (each change redraws them);
- a frame drawn over a bar only shows with the bar (black on black otherwise: same look, no redraw over the value);
- pictures on overlays: keep them to the colours they really have (`MaxColors`), for wheels without the RAM patch.

**Pages** (`Pages` + `"page:N"` in `Visible`, docs/dash-format.md): verify flips them during the lap
(`WorstPageFlipBytes`); `fxdash render --page N`; checks never call elements on different pages overlapping.

`verify.json`:
- `AvgBytesPerSecond`: aim under ~5000 (the Mustang uses ~1400, the Toyota GR010 ~3700). Over 12000 is a problem.
- `WorstSecondBytes`: must stay under 25000, or updates fall behind.
- `FlashingUpdates`: must be **0**. `Flashes` lists when, where, and the elements at that spot.
- `RedrawMismatch`: must be **null**. Otherwise it's a renderer bug: report it, don't work around it.
- `Traffic`: the top senders (bytes per second, redraws per second, including what they make redraw). A value near
  10 draws/s is changing every update; check its format and what's under it.

What typically comes back, and the fix:

| Symptom | Cause | Fix |
|---|---|---|
| `check`: "font N is H px tall, box B" | box shorter than the font | taller box, or `suggest-font` |
| `check`: "is W px wide, box B" | text wider than the box | wider box or smaller font |
| `check`: "font N has no glyph for ..." | font lacks a character (gear fonts: digits only) | another font; `Empty` without that character |
| `suggest-font` gives `-1` | the text fits no font at that box size | widen the box (around its centre), or shorten the text |
| a value's digits missing on the wheel/render | `Samples` narrower than the real text | real widest `Samples`, then `check` |
| a value blank in the demo | its `Visible` condition is false there (e.g. tyre wear shown only below 81%) | read `Visible` first; previews follow `PreviewVisible` |
| `fit-bands` lists changes | text rows crossed a line | accept them, or move the frame yourself |
| `fit-bands`: "no nearby position or font" | box packed between lines | make the box/frame taller or move it |
| flash on a value | text band on a line, image, gradient or bar edge | as above: flat colour under text |
| flash at a pop-up | pop-up without `Fill`, or half over a value | opaque `Fill`; cover whole cells |
| high traffic on one element | value over an image, overlapping another box, or an icon toggling | flat colour under it; separate the boxes; label instead of the icon |
| an overlay's oval or ring sends 30-40 KB when it shows | a ring with no `Fill` (or with `Opacity`) over a multicoloured area: drawn with rectangles | give it a `Fill`, or put it over a filled shape of the same overlay |
| `fxdash pictures` lists an oval or a frame | it has no `Visible`, it's an `image`, its `Radius` is over 24, or it's under 8 px | an `ellipse` / `box` with a condition and `Radius` <= 24 |
| a value on an oval repaints at every change | its text band touches the oval's smoothed edge | move it 2 px further in (`fit-bands`) |

Repeat until `check` has 0 errors and 0 warnings, `fit-bands` returns `"changes": []` and `verify` has `"Ok": true`
(with `--overlays` too, when the dash has overlays; its `Notes` are overlays over most of the screen that come or go for
no more than drawing the dash takes: expected, not a fault).

**Don't run `fxdash tune` on a dash with many overlays or pages:** its overlap fixes don't know that overlays exclude each
other, and shrink values to clear things never shown with them (the Ginetta's lap times and speed, shrunk to clear an
ignition logo they never show with). Do the fixes in a script instead (make_mustang.py, make_ginetta_g61.py). And check
what `tune` trimmed on any dash: a number SimHub draws bottom-aligned over its whole cell, label at the top, overlaps
the label's box; `tune` cut the number's box from the right to clear it, and the digit sat at the cell's left edge (the
Ginetta's TC and FPS). The fix is the original's place: the number under the label, across the cell.

`verify --demo` without `--tiles` reports busy seconds and page flips the size of a full redraw (30-60 KB) when a
full-screen overlay goes away: expected on a wheel without the RAM drive (the Mustang too). The demo gate is
`--tiles --demo`; `--overlays` without `--tiles` is the gate for wheels without the patch.

### A 1:1 conversion: a build script and a parity script

When the user wants a SimHub dash converted faithfully, write two scripts in `tools/dashes/` (worked example: the
built-in Mustang, `make_mustang.py` + `mustang_parity.py`):
- `make_<dash>.py`: import -> numbered fixes, each with its reason (identity and pages, fallbacks for nulls, the delta
  bar, grids, take turns, label clearance...) -> `fit-bands` twice -> the output file. Re-running it is the only way the
  dash changes; never hand-edit the output.
- `<dash>_parity.py`: goes through the original's items one by one and compares what each shows (binding / formula,
  format, colours and colour formulas, visibility, blink, page) with the converted element; exits 1 on any difference
  not on its list of explained ones (a font the screen lacks, a picture made a lamp). Keep inherited bugs of the
  original (the Mustang's tyre-wear average formula) and list them as explained: parity means the same behaviour.
- Run both, then all gates, before every hand-over. A built-in dash lives in `Usb/BuiltIn/<id>.json` (embedded in the
  plugin, fxdash and UsbTest); its UsbTest checks are in `tools/UsbTest/PagesTests.cs`.
- **Prove the parity script works:** run it on a copy with a wrong colour stop, a wrong format and a dropped condition;
  it must exit 1 and name all three.
- **Sizes and places, measured from the original** (worked example: `make_ginetta_g61.py`, `measure` / `choose` /
  `place` / `place_group`). Render each original text with its own font (Windows' `LSANS.TTF` for Lucida Sans,
  `arial.ttf`; size = FontSize x 766/1200) in its box with its alignments (PIL `getbbox(text, anchor='la')`), and take
  its ink box on the wheel. Pick the screen font closest in height (~ the em) and width (`sampleWidth` against the
  original's advance) that fits the area the item may use: inside its frame's lines with 2 px to spare, clear of its
  label. Centre the font's band on the ink, 0.06 x its height lower (capitals sit above a band's middle). **Items the
  original draws in one size get one font** (score summed over the group): a per-item choice gave fuel-per-lap and fuel
  level, and the three tyre pages, different sizes. The screen's small fonts are wide (16 px a letter): a label can need
  more room than the original's (the wheel shows 790 px, the scaled original 766 of them).
- **SimHub's overlay screens are opaque** (their background colour), whatever is see-through on them. Mix their
  see-through rectangles into one opaque background (black + 40 % light grey = #545454), and pre-mix a see-through logo
  over that colour into its own picture, keeping its fully transparent pixels transparent: the wheel draws pictures
  opaque (a 30 % logo came out at full strength on a black square), and a see-through rectangle mixes with the dash
  under it (the dash showed through). Give such a logo `MaxColors` 2 for wheels without the RAM patch (107 KB of
  rectangles in 6 colours, 38 KB in 2; with the patch it's a full-colour picture either way).
- **A tyre widget's frame that's the same on every page**: one frame, always shown. A page flip then redraws only the
  numbers.

- **Shared helpers: `tools/dashes/convert_kit.py`** (worked example: `make_lmgt3_aston.py` + `lmgt3_aston_parity.py`, the AMR). `Original` walks the
  SimHub dash (screens, layers, widgets as pages) with each text item's box, font, size and weight on the wheel;
  `match` finds the item an element came from; `place` / `place_group` / `choose` pick and place fonts; `text_picture`
  draws a text as the original does. Fixes every 1:1 conversion needs: `take_turns` (an element under an overlay's box
  hides while it shows; opaque pictures and ovals count as covering), `backgrounds_from_overlays`, `snap_into_panels`,
  `keep_inside_boxes`, `fit_in_ellipse` (a gear inside its oval, clear of the lettering on it), `clear_of_shapes_after`
  (a value's band off the shapes of its own overlay drawn after it: the AMR's start-screen speed over its start lights),
  `flatten_picture` (art noise: 67 greens within 1 of each other made a dial number redraw 686 rectangles),
  `box_as_rects`, `split_label`, `crop_to_screen`. Write new ones there, not in a dash's script.
- **`tools/dashes/simhub_ref.py` draws the original screen** at wheel size (`--true "formula"` for an overlay, `--page
  Widget=N`): compare it side by side with `fxdash render` for every overlay and page, not just the main screen.
- **Fixed text in the original's font, as a picture** (the user: "the text looks so weird, a lot of spacing between the
  letters"): under 32 px the screen has only its S fonts, spaced like a typewriter. A caption always shown or smaller
  than 25 px becomes a picture of its text in the original's font (`text_picture`): in the static layer (the RAM drive's
  tiles: free; without it rectangles in `MaxColors` 2, its colour and black, 3 over faint art). Values stay screen
  text, in the tight fonts only (`convert_kit.tight`). A character no screen font has (the AMR's page dots "•"): a
  picture too (the import leaves such texts empty: check every empty label).
- **Sets of pages** (format 4): widgets the driver flips on their own SimHub commands import as sets (`Pages` = set 1,
  `PageSets` = sets 2-4, conditions `page:N` / `page2:N`..); "Next page" flips them all, `UsbDashNextPage1..4` one each.
  Name the sets and pages in the script.
- **A zero time**: SimHub formats it ("0.00.000"); the wheel shows `Empty` for a time of 0. Set `Empty` to the format's
  zero, and add it to `Samples` (it must fit too).
- **Without the RAM patch, art behind overlays is the cost.** The AMR's centre pop-ups sit on a disc with thin rim
  lines in its ring picture; putting it back took 1,900 rectangles (41 KB, 1.6 s) at every pop-up's end. Rebuilt as two
  ovals the screen draws (`Visible: ["ncalc:true"]`: the screen draws only shapes with a condition itself) with the
  picture dark under them: 24 KB, and with the renderer's "oval going from over a screen-drawn oval that holds it"
  rule (wiped and drawn again, not its box put back) ~9 KB. Stack such ovals so each lies on one colour (the AMR: the
  light line's oval filled green, then the light blue one on the green): one that can't be wiped clean is put back with
  fills, or its whole area. Look for this whenever an overlay's hide is costly in plain `verify`: measure the art in the
  picture, rebuild it as shapes, blank it in the picture (the RAM drive went from 213 to 175 KB too).
- **Overlays that can't really show together still meet in the sweep** (`--overlays` forces each one on over the lap).
  Decide which gives way the way a race would go, and write it down (the AMR's race summary waits for the start screen).
- **Captions in pictures without the RAM patch:** 2 colours unless over faint art that would take their grey; a third
  colour where they only touch a line doubled their rectangles.

### The RAM drive (wheels with the RAM patch)

With the patch, the static layer is kept as JPEG tiles and every shape that comes and goes with a fixed look and that
the screen can't draw itself (a logo, an icon, a gradient; one look per colour stop) as its own picture, so an overlay
appears with a few commands instead of thousands of fills. Ovals and rounded boxes need none (see "Shapes" above). The
drive holds 350 KB **accounted: each file's bytes plus 512 B** (measured: holes in the dash at 144 files / 333 KB),
shared by every dash loaded, and each file also carries ~600 B of JPEG tables: **small files are mostly overhead**.
What fills it, and what doesn't help:
- **Every distinct look is a file.** Inner overlays and colour stops multiply the looks of a picture (`MaxVariants` 12);
  a shape that could have a picture but is drawn with 16 smooth rectangles or fewer (`SmoothShapeFills`) gets none:
  ~0.5 KB, as fast. (60 was tried: the lap summary's boxes were seen drawing in.) A shape over a bar or delta bar gets
  no picture at all and is drawn with plain (not anti-aliased) fills.
- **The Mustang, before and after the screen drew its ovals and boxes:** 51 files / 158 KB (35 KB tiles, 123 KB
  pictures, ~100 KB of them ovals and frames in every colour and inner-overlay mix: the pit ring alone had six looks,
  25 KB, the ignition oval four, one per indicator arrow) -> 25 files / 56 KB (tiles, the Ford script, the icons).
  Traffic stayed the same (~2.4 KB/s); the demo's busiest second went from 6 to 10 KB.
- `fxdash pictures dash.json [--files DIR]` lists them: `uniqueBytes`, `variants`, `with` (shapes baked in),
  `inner` (overlays it has variants with), `fills` / `smoothFills` (the cost without it). `--files` writes the files
  out to look at. The designer's Screen RAM tab shows the same.
- **The same picture twice is two files** unless it's the same pixels: two copies of a logo 1 px apart over a 1 px
  different oval (the Mustang's two Ford scripts) cost twice. Keep repeated artwork at the same size and spot over the
  same background.
- **JPEG quality is a weak lever:** pictures at q75 -> q45 took the Mustang from 150 to 123 KB. Don't trade looks for it.
- **Transparent pictures don't help** (tried 2026-10-04): the screen draws `hmipicxi` files with a 7-bit alpha mask, but
  a large oval's mask alone is 1.5-2.7 KB and the files must be 4:2:2; per shape they came to 126 KB against 115 KB baked.
- What does help: shapes instead of pictures of them, fewer looks, and no picture for things shown once a session if
  the user accepts them appearing slower.
- **What the numbers mean:** `check`'s `cost.RamBytes`, the designer and the settings page show the files' data; the
  drive counts 512 B more per file (the Ginetta: 46 KB shown, 18 files, 55 KB counted). Say both when the user gives a
  budget.
- **The static layer is 160 px grid tiles, and every cell that isn't one colour is a file** (1.3-4 KB each, mostly JPEG
  tables): a dash of frames and lines takes all 15, ~30-40 KB with the overhead. On top: band tiles (a value whose text
  band crosses a line or picture: gone once its text clears them) and area tiles (the static picture under a box that
  comes and goes, 1.5-2 KB each). The Ginetta import went from 69 to 46 KB with no loss of look: text off the lines,
  the bordered banner as `rect`s, the two logos pre-mixed.

## 4. Look at it

```
$FX render dash.json preview.png                        # designer view: preview texts, pop-ups hidden
$FX render dash.json demo.png --mode demo --seconds 97  # after a simulated lap (lap times, delta set)
```

`fxdash render` draws every text after all shapes, so a copy with an overlay forced on shows the dash's texts over it.
To see an overlay as the wheel draws it (and the ignition screens, pop-ups...): `fxdash serve --port 8897` in the
background, `POST /api/overlays` (body: the dash) for the overlay numbers, then `POST
/api/render?mode=demo&seconds=97&overlay=K&tiles=1` (drop `tiles` for a wheel without the RAM patch: pictures drawn
with rectangles in `MaxColors`). Stop the server before rebuilding fxdash (it locks the exe).

Open both PNGs. The renders draw text with a Windows font scaled to each screen font. Boxes, positions and colours
are exact; glyph shapes are approximate, and digits look a little smaller than on the wheel. Judge them like a
designer. The gear and shift information are biggest, lap time and delta next,
everything else small and grouped. Keep high contrast on black, one accent colour, and colour only for state. Align
to a grid and leave margins. For a reference image, compare with `compare.png` (1b) and iterate. For an import,
compare with the SimHub dash's own preview (`<dash>.djson.png` in its folder).

## 5. Install and show

- Set `Id` (lowercase, dashes: the file name), `Name` and `Description`.
- Copy the file to `C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSync\Dashes\<Id>.json`. In SimHub:
  FX Unleashed, Dashes tab, the dash list's refresh button (no restart needed). Pick it, then press Demo.
- While SimHub runs, the designer API does the same: `PUT http://127.0.0.1:8899/api/dashes/<Id>` (body: the dash),
  and `POST /api/wheel/show?left=10&top=20` (body: the dash) puts it on the wheel for a minute.
  `POST /api/verify?seconds=60` runs `verify` (`&tiles=1` as on a wheel with the RAM patch) and `POST /api/fit-bands` runs `fit-bands`.
  The designer shows the same three gates live (the Checks and Wheel traffic pills), so a person can follow your work there.
- The designer (`http://127.0.0.1:8899/` while SimHub runs) has all of it for a person: the pages editor (add, rename,
  reorder, move elements, delete), the overlay picker (the dash with one overlay forced on, parents included), the
  Screen RAM tab (pictures and budget), and in the Traffic drawer the page-flip card, the overlay check (`verify
  --overlays`) and "Make them take turns" when an overlay flashes (`DashTune.TakeTurns`). `/api/overlays`, `/api/pictures`, `/api/take-turns`,
  `/api/render?page=&overlay=&tiles=1` and `/api/wheel/show?page=&overlay=` are the same over HTTP.
- Report back:
  - the gates' results (errors 0, fit-bands unchanged, verify Ok, with average and worst traffic, and `--overlays`);
  - the renders;
  - what was skipped or changed (import notes, fit-bands changes);
  - which bindings only work with a given game or plugin.

## Why the rules (what went wrong before)

- The first Toyota GR010 import sent ~310 KB/s to a 25 KB/s screen. The speed value overlapped an icon, and
  neighbouring boxes overlapping by 1 px kept redrawing each other. The USB loop stalled and took the wheel's LEDs
  down to ~1 Hz. The renderer now handles both, but overlaps still cost traffic.
- Values flashed on every update. Their boxes were as tall as their font, so the frame's border lines ran through the
  text rows (fixed with `fit-bands`). Text on the SoC bar was wiped whenever the bar moved. A lap pop-up had an
  outline-only box while values kept changing under it.
- The McLaren 720S import needed all of section 1a: RPM cut to "573" by preview-text `Samples`, letter-spaced "S"
  fonts, a 54 px wide gear, a 10 KB/s ABS icon and ARB boxes running into a label. `verify` found every traffic and
  flash problem. `docs/examples/lmgt3-mclaren-tune.py` has the fixes, with the reasons.
- `verify` catches all of these, so run it every time.
- The Mustang 1:1 conversion (pages, 60+ overlays): the importer's first try had values half under flag ovals
  (redrawn through them), backgrounds the wrong colour on overlay boxes, labels touched by popup values, and a time
  formula SimHub rejected live (NCalc backslashes). The pit oval's colour change cost 12 KB because text over it
  blocked its picture (now: a costly shape's picture goes over the text, which is drawn again). `verify --overlays`
  and the parity script found each one.
- The Mustang's RAM drive (2026-10-04): 158 KB, two thirds of it pictures of ovals and frames, one per colour and per
  mix of what's inside them. The screen turned out to smooth its own `draw_h` polygons and `cirs` circles; drawn with
  those, the ovals and frames left the drive (56 KB). A first try smoothed whole ovals and overflowed the screen at
  10 KB/s (smoothing costs per pixel), so only the edge is smoothed now; that keeps up at the full 25 KB/s on the wheel.

- The Ginetta G61 batch conversion (2026-10-05) passed every gate and still wasn't the original: `tune` had pushed TC
  and FPS to their cells' left edges, sizes differed inside groups the original draws alike, psi pressures read "26.90",
  the tyre widget's other two pages and both ignition screens were missing, and "PRESS IGNITION TO START" was cut by
  the screen command's length (`check` didn't know; it does now). The 1:1 rebuild with measured sizes and a parity
  script found each one. The no-RAM ignition screens also showed a renderer bug: colour reduction ignored transparency
  (black squares around logos), fixed in `Quantize`.

References: `docs/dash-format.md` (format), `docs/dash-designer.md` (designer, API, import), `docs/usb-mode.md`
(how it reaches the wheel), `tools/UsbTest` (`traffic DASH.json flash|diverge|blame` for deeper digging).

## Designing from scratch in code

The firmware has car-specific fonts (ir18, 992, 296, f3, bmw, w12, c8r...): `fxdash fonts` lists
their heights and glyphs; the big gear ones only have digits and D N P R.
