# Pictures in the screen's RAM (tiles): the plugin side

Status 2026-10-01: working on the user's wheel; merged from branch `screen-ram-tiles` (worktree `E:\Development\SimagicRpmSync-tiles`)
into `usb-mode` the same day. The screen side (the modified screen image with a RAM drive, `twfile`, `ramv`, the
recovery runbook) is in FXProDashes: `docs/screen-images.md`, `docs/screen-speed.md`.

## What it does

On a wheel whose screen image has the RAM drive (setting **"My screen has the RAM drive"**, Wheel tab, off by default:
an unflashed screen never gets any of this. Not sure whether yours is flashed? Press **Test** in the same card: a colour card
with "RAM OK" shows for 5 seconds if it is, and the box is ticked only then. The firmware card below it turns the memory on or
off by a header-only upload, and clears the tick when the memory goes away), the plugin keeps each dash's static layer on the screen as JPEG pictures and
draws it with `sets "ramv: X, Y, ram/NAME"` instead of thousands of `fill`s:

- **Dash tiles** (`Usb/ScreenTiles.cs`, `DashRenderer.EnableTiles`): the static layer rendered in full colour and
  anti-aliased (no colour reduction), cut into a 160 px grid (5 x 3); a one-colour tile is a single `fill`, not a file.
  Plus **band tiles** (the background under a value's text when it isn't one colour: band + text, two commands) and
  **picture tiles** (an `image` element that comes and goes, e.g. a tyre-compound or pit icon, blended over the static
  layer: one command when it shows; `PictureFits` checks nothing under it shows through). File name = hash of the JPEG,
  so equal tiles are shared between dashes. Registry of every tile's bytes for previews and the mirror.
- **Repaints**: an area is put back from grid tiles only when exact fills would need more than `TileRepaintFills` (60);
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
- **Stuck button reports** (stock bug, FXProDashes firmware-notes.md): besides re-arming on connect, the plugin re-arms
  `0x200002B8` whenever no input report has arrived for over 1 s while connected (`KeepInputReports`, logs "the wheel's
  button reports had stopped"). A brief USB hiccup the plugin didn't see left the buttons dead (2026-10-01).
- Dashes tab: Previous/Next red, **"Demo on the wheel"** runs the demo through the shown rotation (dash button cycles);
  API `POST /api/wheel/demo?dash=rotation`.
- SimHub formulas that fail are skipped for 0.3 s (was 5 s: sector results showed up to ~10 s late).
- Demo: SimHub's sector times (`SectorNTime`, `...LastLapTime`, `...BestLapTime`, `...BestTime`) are simulated.
- SimGame feed: LMU tyre temperatures as LMU shows them (see hotlap-dashes.md).

## Open

- Release notes. The install path for users is the firmware card (`Ui/FirmwareCard.cs`, header-only upload, FXProDashes
  `docs/screen-header-flash.md`): built, not yet tried on the wheel; the user's wheel has the RAM drive from FXProDashes
  `tools/usb/screen-flash.ps1`.
- Queue a dash's files when it's added to the rotation.
- JPEG quality (88) / tile size (160) not tuned for speed yet; load time is mostly per-file waits.
