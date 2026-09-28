# USB mode: custom dashes and every light on the FX Pro

Status (2026-09-27): working on the wheel (branch `usb-mode`, not released). Custom dash, logo screensaver, built-in
light presets and ATSR-Hub lights all verified on an FX Pro running the FXProDashes build 4 firmware.

USB mode drives the Simagic FX Pro's screen and all 38 of its LEDs straight over the wheel's own USB cable, next to
SimPro (which keeps doing force feedback and settings). It needs the wheel's app firmware patched with the FXProDashes
build 4 image; on stock firmware nothing here works (and nothing breaks either, see [Firmware](#firmware)).

Contents: [What it does](#what-it-does) · [Setup](#setup) · [Dashes](#dashes) · [Dash file format](#dash-file-format) ·
[Lights](#lights) · [ATSR-Hub](#atsr-hub) · [How it talks to the wheel](#how-it-talks-to-the-wheel) ·
[Checking without the wheel](#checking-without-the-wheel) · [Troubleshooting](#troubleshooting) ·
[Before publishing](#before-publishing)

## What it does

| When | Screen | Lights |
|---|---|---|
| A game is running (SimHub has data) | The chosen custom dash (built in: LMGT3 Ford Mustang GT3) | The chosen preset, or ATSR-Hub |
| No game | The plugin's logo with a rev-light sweep (option) | Ambient effects only (option; rev lights dark, no alerts) |
| Demo / Test button | The dash, fed by a simulated lap | As in a game |
| USB mode off, wheel unplugged, SimHub closed | The wheel's own dash (`page dp`) | SimPro's colours |

## Setup

1. Flash the FXProDashes build 4 wheel app through SimPro (FXProDashes `docs/firmware-plan.md`, `firmware-rebuild.md`).
2. Plug the wheel's USB cable into the PC (the wheel stays on the base as usual).
3. SimHub → FXPro RPM Sync → **USB mode**: tick **Use USB mode**, press **Test on the wheel (8 s)**. With the patched
   firmware the demo dash shows steadily; with stock firmware the wheel's own dash flickers through it.
4. Tick **My wheel runs the FXProDashes firmware**. The status line then reads "Standing by" / "Lights on", and
   "Active" once a game runs.

The patch can't be detected: build 4 reports the same `F1` status (app 1.3.11, run mode 0) as stock, and RAM writes have
no read-back. The plugin checks what it can (the wheel's USB interface, VID 0483 PID 0529, app 1.3.11 in run mode) and
asks the user to confirm the rest once.

## Dashes

- Built in: **LMGT3 Ford Mustang GT3**, the MAIN screen of SimHub's dash of that name, scaled to 800x480 and fitted to the
  FX Pro's fonts (verified on the wheel). Fuel on the left; last lap, delta, predicted lap and virtual energy on the
  right; speed, gear and session in the oval; bias, position, lap time and a delta bar; eight setting boxes; a 2 s
  pop-up when TC, TC LON, TC LAT, ABS, map or bias changes.
- **Padding** (left 0-20, top 0-38 px; default 10 / 20): moves the whole dash away from the screen's edges. On the
  user's wheel the top needed 20 px; the part below the divider is squeezed ~18 px so the dash still fits.
- **Logo screensaver** between sessions: `assets/logo-nobg.png` on black, its 13 rev dots running a 4 s sweep.
- User dashes: JSON files in `SimHub\PluginsData\Common\FXProRpmSync\Dashes\*.json` appear in the dash list.
  **Save a copy of this dash to edit** writes the selected dash there as a starting point; **Reload dashes** picks up
  edits. The settings page shows a live preview (drawn the way the screen draws it) and layout warnings.

What the screen can and can't do (all measured on the wheel, FXProDashes `docs/custom-dash.md`):

- Everything is drawn at runtime with the screen's own commands (TJC `fill`, `xstr`); the screen image isn't changed.
- **No new pictures.** Images become `fill` rectangles: fine for flat artwork in a few colours (the logo is ~3,300
  rectangles after sorting its colours into black/red/white), too slow for photos.
- **Fonts are fixed:** 128 anti-aliased fonts in the stock screen image, by id. Their real sizes are in
  `Usb/FontMetrics.cs`: in Simagic's S* fonts a digit is as wide as the font is tall, and text wider than its box
  wraps onto a line the screen doesn't show. Narrow families: 963 (ids 96-101), isf23 (92-95), 992 (69-72).
- **Budget:** the plugin sends at most 25 KB/s to the screen (see [below](#how-it-talks-to-the-wheel)). The Mustang's
  static layer is ~15 KB (~0.6 s); its updates ~1.8 KB/s at 10 per second.

## Dash file format

A dash is a `DashDefinition` (`Usb/DashModel.cs`): JSON, 800x480 screen pixels, elements drawn in order. The built-in
Mustang saved with **Save a copy** is a complete example (~45 KB).

```json
{
  "FormatVersion": 1, "Id": "my-dash", "Name": "My dash", "Author": "...", "Description": "...",
  "Elements": [
    { "Type": "rect",    "X": 30, "Y": 268, "W": 747, "H": 3, "Color": "#428AED" },
    { "Type": "ellipse", "X": 209, "Y": 36, "W": 380, "H": 153, "Color": "#FFFFFF", "Fill": "#1B1F3C", "Border": 4 },
    { "Type": "box",     "X": 27, "Y": 379, "W": 87, "H": 62, "Color": "#3277AE", "Border": 3, "Radius": 10 },
    { "Type": "label",   "X": 0, "Y": 0, "W": 231, "H": 25, "Text": "Fuel Last Lap", "Font": 14, "Color": "#D3D3D3", "Align": "left" },
    { "Type": "value",   "X": 57, "Y": 318, "W": 309, "H": 50, "Bind": "currentLapTime", "Format": "laptime",
      "Font": 100, "Color": "#D3D3D3", "Align": "left", "Empty": "-:--.---", "Samples": ["8:88.888"] },
    { "Type": "deltabar", "Bind": "delta", "Y": 303, "H": 70, "Segments": 7, "SegmentX": [383, 409, ...],
      "SegmentWidth": 20, "Range": 1, "PositiveColor": "#FF0000", "NegativeColor": "#00FF00", "SegmentColor": "#808080" },
    { "Type": "popup", "X": 280, "Y": 229, "W": 237, "H": 167, "Radius": 10, "Font": 101, "ValueFont": 35,
      "Duration": 2, "Color": "#000000",
      "Watch": [ { "Bind": "tcLevel", "Label": "TC", "Color": "#28598B", "Format": "int" } ] }
  ]
}
```

| Type | Drawn | Fields |
|---|---|---|
| `rect` | once | `Color` |
| `ellipse` | once | `Color` (rim), `Fill` (inside), `Border` (rim width) |
| `box` | once | rounded frame: `Color` (border), `Fill`, `Border`, `Radius` |
| `label` | once, no background | `Text`, `Font`, `Color`, `Align` (left / center / right) |
| `value` | on change, solid `Background` (default black; must match what's under it) | `Bind`, `Format`, `Scale`, `Empty`, `Font`, `Color`, `Align`, `PositiveColor` / `NegativeColor` (colour by sign), `Samples` (widest texts, checked) |
| `deltabar` | per segment on change | two halves of `Segments` segments filling from the centre: positive values fill the left half in `PositiveColor`, negative the right in `NegativeColor`; `Range` = value of a full half |
| `popup` | for `Duration` s when a watched value changes, then the area is restored | `Watch` list (`Bind`, `Label`, `Color`, `Format`), `Font` (label), `ValueFont`, `Color` (text) |

Formats: `"0"`, `"0.0"`, `"0.00"`, `"0.000"` (any .NET number format), `int`, `laptime` (m:ss.fff), `gear` (R / N /
number), `delta` (+0.00 / -0.00), `text`.

Bindings (`Usb/DashValues.cs`, same values live and in the demo): `speed`, `gear`, `rpm`, `maxRpm`, `throttle`, `brake`,
`clutch`, `currentLapTime`, `lastLapTime`, `bestLapTime` (seconds), `delta` (s to session best, + = slower; SimHub's
`PersistantTrackerPlugin.SessionBestLiveDeltaSeconds`), `predictedLap` (`EstimatedLapTime_SessionBestBased`),
`position`, `lap`, `completedLaps`, `fuel`, `fuelPercent`, `fuelLastLap`, `fuelThisLap`, `fuelRemainingLaps`
(SimHub's `Computed.Fuel_*`), `virtualEnergy` (LMU, %), `brakeBias`, `absLevel`, `tcLevel`, `tcCut` (ACC, iRacing, LMU),
`tcSlip` (LMU), `engineMap`, `sessionTypeName`, `waterTemp`, `oilTemp`; `throttleMap` and `pas` are demo-only for now.
Anything else: `"prop:<SimHub property>"`, e.g. `"prop:DataCorePlugin.GameRawData.PlayerNativeTelemetry.mVirtualEnergy"`.

Layout checks (settings page, `DashRenderer.Check`): each text's font height and widest text (`Samples`, `Empty`,
labels) against its box, glyphs the font lacks, a value's background covering a shape, overlapping text boxes, and the
dash running off the screen once padded.

## Lights

LED numbering (the firmware's renderer order): **buttons 0-11, encoders 12-16, side lights 17-19 (left of the rev bar)
and 20-22 (right), rev lights 23-37.** The button and side-light left/right split is assumed, not verified; rev LED 23
is the leftmost on the user's wheel (option to flip).

Built-in presets (`Usb/Lights.cs`): Mustang Rainbow, Aurora, Synthwave, Ember, Glacier, Scanner, Stealth, Full
Rainbow. **Customize** copies the current one into an editor: per group (buttons, encoders, left, right, rev) an effect
(solid, breathing, colour wave, rainbow flow, breathing rainbow, scanner, sparkle, off; rev lights also "shift
lights"), 1-4 colours, speed and brightness. Rev lights use the car's real shift lights from the rev light database
(and per-car overrides, per gear) when known, else the profile's colours from SimHub's redline. Alerts blink over
everything, first match wins: ABS (left side, amber 12/s), TC (right side, blue), pit limiter (rev lights), blue and
yellow flags (encoders), low fuel and DRS (off by default).

## ATSR-Hub

USB mode can take the lights from ATSR-Hub EVO instead of its own presets, so ATSR-Hub's shift lights, spotter, flags,
TC/ABS and animations appear on the FX Pro, which SimHub itself can't drive.

Setup:
1. In ATSR-Hub, **Device Hub → add a device → steering wheel → "Simagic FX-Pro"**. That entry is
   `assets/Simagic_FX-Pro.atsrdevice`, copied into ATSR-Hub's preset folder
   `<Documents>\SimHub\ATSR\device-presets\device-presets\steering-wheel-presets\` (Documents may be under OneDrive).
   Free ATSR-Hub drives one wheel (the first one added, or the one picked in the Device Hub); more need premium.
2. In FXPro RPM Sync → USB mode → Lights: **Lights come from: ATSR-Hub**. With one ATSR-Hub device the plugin picks
   it by itself.

How it works (from ATSR-Hub EVO's code, decompiled with ilspycmd 9.1):
- For each device, ATSR-Hub publishes `ATSRHubMain.Device_<name>_Background`, `_Layer1`, `_Layer2`, `_Layer3`: lists of
  `#AARRGGBB` strings, one per LED index of the device's layout, and `ATSRHubMain.NM_Brightness` (0-100, night mode).
  Its own SimHub LED profiles draw the four layers in that order, each over the last where it isn't transparent
  (`LedProfileGenerator`).
- The plugin reads them in `DataUpdate` (~30/s), stacks them the same way (`Usb/AtsrBridge.cs`), maps them to the
  FX Pro's LEDs and sends them over USB. While ATSR-Hub sends nothing, the built-in lights stay on.
- ATSR-Hub finds wheels by USB VID/PID (WMI); a wheel is published when it's free-tier enabled
  (`Settings.Wheels[name].Item1`) or the user has premium.
- **The layout file** (`assets/Simagic_FX-Pro.atsrdevice`): made by `tools/atsr/make_fxpro_preset.py` from ATSR-Hub's
  GSI FPE-V2 preset (same element counts), with the FX Pro's LED numbering, VID 0483 / PID 0529 and a 15th rev LED.
  `tools/atsr/AtsrCheck` runs ATSR-Hub's own `ElementInfoConverter.ConvertFromWheelInfo` on it: 38 LEDs, buttons 0-11,
  encoders 12-16, telemetry rows 17-19 / 20-22, RPM 23 x 15. Button input IDs are unset (-1), so ATSR-Hub's
  button-press effects don't fire.
- **LED map** (optional): 38 ATSR-Hub indexes, one per FX Pro LED in FX Pro order, `-1` = off; for layouts numbered
  differently, or to swap sides.
- Another route that exists: SimHub discovers `IDeviceDescriptorsRegistry` implementations in any DLL in its folder
  (`PluginFinder`), so a plugin could register the FX Pro as a native SimHub LED device (`ILedDeviceManager.Display`
  gets every frame). Not used: ATSR-Hub's properties are simpler and don't depend on SimHub internals.

## How it talks to the wheel

Wheel USB HID (VID 0483, PID 0529), reports of 65 bytes (`Usb/UsbTransport.cs`, from FXProDashes `tools/usb/FxHid.cs`):

- `F2 09 len data`: bytes straight to the screen's UART (TJC commands, each ending `FF FF FF`), up to 61 per report.
- `F2 0A len addr data`: the updater's flash-program command, whose address check is compiled out, used as a RAM store.
  **RAM only** (0x20000000-0x2000FFFF, enforced): an address in flash would really program flash.
- Build 4's control block at 0x20007000: LED mode word (`FXL1`+4 = every LED from RAM, black = off), mirror of the
  stock palettes at +0x10 (written before the mode), LED colours at +0x90 (4 bytes each: B, R, G, brightness 1-90);
  host-screen word at +4 (`FXS1`), active only while a USB screen packet arrived in the last second.
- **Pacing: 25 KB/s to the screen**, 150 ms pause after a `page` command. Unpaced, USB pushes ~30 KB/s, faster than the
  screen draws small fills; the overflow is lost, and a burst right after a page change froze the screen until the
  wheel was power cycled (seen 2026-09-27, fixed by the pacing).
- Taking the screen: keepalive, mode word, 30 ms, lone `FF FF FF` (clears half a command the wheel may have sent), then
  `page 0` (no screen timers), `vis 255,0`, `cls 0`. Giving it back: `page dp`, keepalive stops, mode word 0.
- USB limit: ~500 reports/s in total. Typical load: LEDs 90 reports/s (38 LEDs, 30 frames/s), dash ~40/s.
- If SimHub dies: the screen returns to the wheel within a second (keepalive gate) but stays on page 0 until the next
  page change; the LEDs stay in all-LEDs mode until a power cycle.

## Checking without the wheel

- `tools/UsbTest`: compiles all plugin sources against SimHub's DLLs; `UsbTest.exe OUTDIR` runs the Mustang through the
  renderer with the demo lap (layout checks, USB budget, PNG snapshots, JSON round trip), `saver` the screensaver,
  `atsr` the layer stacking, `ui` renders the settings section to PNG.
- `tools/atsr/AtsrCheck`: ATSR-Hub's own conversion of an `.atsrdevice`.
- The settings page's dash preview uses the same renderer as the wheel (glyph shapes are Segoe UI stand-ins at the
  screen fonts' real widths).

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Screen stuck on a page (e.g. the yellow steering circle) and ignores everything | Screen froze (it happened with unpaced traffic). Power cycle the wheel (off the base, USB out). |
| Dash text cut off | Box too small for the font's real width/height; see the layout warnings. |
| Dash cut off at an edge | Increase padding. |
| Status "Unsupported wheel firmware" | Not app 1.3.11, or the wheel is in its bootloader. |
| Lights: "no data from ATSR-Hub" | ATSR-Hub doesn't publish the wheel (not added, not the active free wheel, or name changed: clear the device and let the plugin pick it again). |
| ATSR-Hub left/right effects mirrored | Enter an LED map that swaps 0-5 with 6-11 and 17-19 with 20-22. |
| Rev lights fill from the wrong side | "Rev lights fill from the right". |

## Before publishing

- Firmware: build 4 has to reach users (FXProDashes `firmware-rebuild.md`); decide how, and document the stock
  round trip.
- Patch detection: a marker in the `F1` status block in a future firmware build would replace the confirmation box.
- Verify on the wheel: button and side-light left/right positions, encoder order; ATSR-Hub button input IDs.
- Dash editor (next step): see FXProDashes `docs/custom-dash.md`.
- README: user-facing section for USB mode; keep the ATSR-Hub preset file in releases.
