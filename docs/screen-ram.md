# Pictures in the screen's RAM (tiles): the plugin side

Working on the maintainer's wheel. This page is the plugin side; the screen side (the screen image with a RAM drive,
`twfile`, `ramv`) is part of the wheel firmware work.

## What it does

On a wheel whose screen image has the RAM drive (setting **"My screen has the RAM drive"**, Wheel tab, off by default:
an unflashed screen never gets any of this. Not sure whether yours is flashed? Press **Test** in the same card: a colour card
with "RAM OK" shows for 5 seconds if it is, and the box is ticked only then. The firmware card below it turns the memory on or
off by a header-only upload, and clears the tick when the memory goes away), the plugin keeps each dash's static layer on the screen as JPEG pictures and
draws it with `sets "ramv: X, Y, ram/NAME"` instead of thousands of `fill`s:

- **Dash tiles** (`Usb/ScreenTiles.cs`, `DashRenderer.EnableTiles`): the static layer rendered in full colour and
  anti-aliased (no colour reduction), cut into a 160 px grid (5 x 3); a one-colour tile is a single `fill`, not a file.
  Plus **band tiles** (the background under a value's text when it isn't one colour: band + text, two commands) and
  **pictures of shapes that come and go** (an icon, a logo, a gradient: anything but a plain rectangle, an oval or a
  rounded box, which the screen draws itself, see below; one per colour stop), blended over the static layer and the
  shapes of its own overlay under it
  (and a variant per mix of the overlays inside its own, at most `MaxVariants` 12): one command when it shows;
  `PictureFits` checks that exactly that is under it. Not for a shape over a bar, nor one 16 smooth rectangles draw
  (`SmoothShapeFills`). `fxdash pictures DASH [--files DIR]` lists them. File name = hash of the JPEG, so equal tiles
  are shared between dashes. Registry of every tile's bytes for previews and the mirror.
- **Ovals and rounded boxes the screen draws itself** (`Usb/ScreenShapes.cs`, `DashRenderer.NativeCapable`, with or
  without the RAM drive): the screen's own drawing smooths the edges of `draw_h` polygons (the gauge needle command:
  any six-point shape symmetric about its axis) and `cirs` circles, and blends `fill`, `draw_h` and `cirs` with the
  `aph=N` alpha (0-127; not used yet). An oval is plain fills inside plus thin smoothed `draw_h` bands along its edge
  (~1.3 KB, a Ford ring ~2.8 KB), a rounded box two fills and a `cirs` per corner (~150 B); a ring is the outer shape in
  its rim colour then the inner one in its fill (a ring with no fill: the one colour under its middle; a see-through
  shape: its colours mixed with the one colour under it; else fills as before). No file, no RAM, the colour is just
  an argument: the Mustang went from 51 files / 158 KB to 25 / 56 KB. Only for shapes that come and go, as pictures
  were (one always shown gets text redrawn on it all the time, and its smoothed edge put back with fills is a fill a
  pixel: HALO's delta disk). Smoothing costs ~2 us a pixel (a fill ~0.07), so `FxHostScreen` pays it as bytes
  (`ScreenShapes.SmoothPixels`). Smoothed edges blend with what's on the screen, so a shape is never drawn whole over
  itself: drawn over in part (MarkAbove's `Damage`), that part goes back with fills of its pixels (which come from
  running its own commands on a preview screen, `NativeRender`, so they match); changed colour, its old edge is wiped
  first (the shape 2 px larger in the colour around it, or a rounded box's corner squares put back with fills; a
  ring's fill alone wiped with its rim's colour). `FXDASH_NATIVE=0` (fxdash) turns it off for comparisons.
- **Repaints**: when a solid shape still shown covers all of the area (an overlay's box under its blinking label),
  only that shape and what's on it are drawn; a hidden label puts back only its text's ink, and a rounded box counts
  as covering all but its corners. Otherwise an area is put back from grid tiles only when exact fills would need more
  than `TileRepaintFills` (60);
  pixels put back around text use the colour-reduced layer (`Composite(coarse: true)`), so an anti-aliased line through
  a value's band doesn't become one fill per pixel.
- **Screensavers** (`ITiledSaver`: the painted ones in `ArtSavers.cs` and the logo `ScreenSaver`): their art as tiles
  (`SaverTiles`), ~1 KB to draw instead of 20-90 KB of rectangles. The logo is drawn in full colour from tiles.
- **The drive** (`Usb/ScreenRam.cs`): 384 KB, budget 336 KB (room for a file's temporary `.tm` copy). Files are tracked in
  settings (`Usb.ScreenRam`) with a **power-loss token** in marker word `0x20000850` (status byte 0x20): a reconnect
  with the token gone = the wheel lost power, files forgotten. LRU eviction (`delfile`) when full.
- **Upload** (`ScreenRam.Upload`): `twfile "ram/NAME",SIZE`, then 4 KB packets (header `3A A1 BB 44 7F FF FE`, crc 0,
  id u16, len u16) with waits **arm/packet/done = 30/15/40 ms** (`ArmMs/PacketMs/DoneMs`; tuning below). File data is
  sent unpaced (`FxHostScreen.Raw`), commands paced as always.
- **Unstick** (`ScreenRam.Unstick`): after every batch: 4 KB of zeros, the abort packet (size 0, id FFFF), a lone
  terminator, `delfile` of the half-written `.tm`. Returns the screen to command mode whatever an upload left it in
  (seen: a lost file left it swallowing every command, "stuck on LOADING").

## When things load

1. **A dash shows**: its missing files go up first behind the loading screen (one file per `Held`, see below), then
   it's drawn from tiles. 15-30 files, 2-6 s the first time after a power-on; instant afterwards.
2. **The rest of the rotation** (setting "Preload my rotation", on by default) loads in the background **only while
   the car is stopped, in the pit lane or in a menu**: each file pauses the dash ~0.3 s (choppy on track, user
   feedback). Waits doubled there (no hurry).
3. **A screensaver shows** (`PrepareSaverTiles`): its own tiles **and the whole rotation** load together behind the
   loading screen (title "Your dashes"), ~15-20 s after a power-on; then the saver runs smoothly and the first dash
   shows at once. (Loading behind a running saver made it choppy for half a minute: user picked this instead.)
4. Not yet: a dash added to the rotation isn't queued until the next dash/saver change (offered to the user).
5. **Per dash: with or without the RAM** (`UsbSettings.NoRamDashes`, `DashUsesRam(id)` / `RamFor(id)`; a switch on each
   rotation tile and in the focus panel of the Dashes tab, shown while the drive is on). A dash set to skip it is drawn
   live with rectangles: nothing of it is uploaded, no screen RAM, no loading screen, previews draw it with fills too.
   Meant for simple dashes. The rest of the rotation still preloads behind it (the dash is redrawn with fills after
   each file). Not counted in the rotation's RAM total or the preload.

## The loading screen

Plain on purpose (`UsbController.DrawLoading`): "LOADING DASH" / "LOADING" and a 300 x 12 progress bar, ~200 bytes;
**the wheel's rev lights fill along** (`LoadingOverlay` in `SendLeds`, green/amber/red thirds).
Redrawn after every file: the screen repaints its page (page 0's "Check1" picture) after each file received or
deleted, so the frame is held with `ref_stop` ... `ref_star` (`Held`) and drawn again inside it.

Tried and removed:
- a painted rev-counter loading screen (7 big filled circles, ~1 Mpx): the screen was still painting when the next
  file's `twfile` arrived, files went astray, the screen got stuck with Check1 behind it;
- a loading screen kept in RAM (art loaded first): the user doesn't want a loading screen that itself needs loading;
- several files per `Held` (fewer redraws): files lost, screen stuck. **One file per hold.**

Rule from both: before an upload the screen must be idle; keep whatever is drawn right before `twfile` small.

## Lessons (all seen on the wheel)

| What | Why | Now |
|---|---|---|
| White screen after flashing ramfs v1 | header CRC at 0xC4 not updated | `make_ramfs.py`/`tftseal.py` fix both CRCs; recovery runbook in screen-images.md |
| Waits 0/0/0: stuck on LOADING until power cycle | packets sent before the screen took the command | 30/15/40 ms (60/30/60 and 30/15/40 clean, 15/5/20 lost files) |
| Check1 flashing during loading | page repaint after each file | `Held` around each file |
| Check1 behind the dash after the RAM test | a `delfile` repaints the page on the **next refresh**, after what's drawn right after it | `Delete`/`Unstick` wait `max(40, DoneMs)` after a delete |
| Mirror showed boxes around values | 24-bit JPEG decode vs exact flat colour | previews round pictures to RGB565 like the screen (`PreviewScreen.To565`); on the wheel they match |
| Mustang "Low NRG" flashing the value under it | each blink put back 4 grid tiles (the label's whole box reached into its red box's rounded corners, so the box wasn't seen as covering it) | hidden text puts back its ink; a covering shape is looked for first |
| Mustang setting pop-ups and lap summaries drawing in | anti-aliased fills for boxes that can't have a picture (a delta bar under them), ~60 rectangles | small-shapes rule only for picture candidates, 16 rectangles at most |
| Overlays stacked in the demo | the lap's own pop-ups came up during a showcase turn | the demo holds other overlays off during a turn |
| 488 slow to show its numbers | tyre-compound icons (4 stacked, all "visible" in the demo) drawn with ~840 fills | picture tiles; stacked states of one icon: the top one counts |

## Tuning and tools

- Designer API (port 8899): `GET /api/wheel/ram` (files, bytes, waits), `POST /api/wheel/ram/waits?arm=&packet=&done=`
  (this session only), `POST /api/wheel/ram/clear`; `GET /api/props?names=...` (SimHub properties now),
  `GET /api/eval?f=ncalc:...` (a formula with SimHub's own engine, `{value,type}` or `{error}`).
- `tools/UsbTest OUT tiles [filter] [threshold]`: every dash with fills vs tiles (bytes, first-values cost, drift vs a
  fresh tile drawing, traffic); `tiles savers`: screensavers fills vs tiles.
- `UsbTest OUT traffic DASH.json first`: what the first update after a dash appears costs, per element
  (`first_cmds.txt`).
- Settings page: Wheel tab card "Pictures in the screen's memory" (Test 5 s, drive switch, preload switch, usage);
  Dashes tab shows each dash's RAM and the rotation's total vs the budget; the designer's "Screen RAM" pill.

## Also in this branch (not RAM-specific)

- **Build query retry**: right after a power cycle the wheel app doesn't answer the build query; the plugin asks
  again for ~6 s (it used to give up: no build = the dash button stayed in its old slot, dead).
- **Stuck button reports** (a stock firmware bug): besides re-arming on connect, the plugin re-arms
  `0x200002B8` whenever no input report has arrived for over 1 s while connected (`KeepInputReports`, logs "the wheel's
  button reports had stopped"). A brief USB hiccup the plugin didn't see left the buttons dead (2026-10-01).
- Dashes tab: Previous/Next red, **"Demo on the wheel"** runs the demo through the shown rotation (dash button cycles);
  API `POST /api/wheel/demo?dash=rotation`.
- SimHub formulas that fail are skipped for 0.3 s (was 5 s: sector results showed up to ~10 s late).
- Demo: SimHub's sector times (`SectorNTime`, `...LastLapTime`, `...BestLapTime`, `...BestTime`) are simulated.
- SimGame feed: LMU tyre temperatures as LMU shows them (see hotlap-dashes.md).

## Open

- Release notes. The install path for users is the firmware card (`Ui/FirmwareCard.cs`, header-only upload): built,
  not yet tried on a second wheel.
- Queue a dash's files when it's added to the rotation.
- JPEG quality (88) / tile size (160) not tuned for speed yet; load time is mostly per-file waits.
