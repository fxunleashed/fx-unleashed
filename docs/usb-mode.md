# USB mode: custom dashes and every light on the FX Pro

Custom dash, logo screensaver, built-in light presets and ATSR-Hub lights are verified on an FX Pro running the FX
Unleashed wheel app patch.

USB mode drives the Simagic FX Pro's screen and all 38 of its LEDs straight over the wheel's own USB cable, next to
SimPro (which keeps doing force feedback and settings). It needs the wheel's app patched with the FX Unleashed wheel
app patch (build 4 or later); on stock firmware nothing here works (and nothing breaks either, see [Firmware](#firmware)).

Contents: [What it does](#what-it-does) · [Setup](#setup) · [Dashes](#dashes) · [Dash file format](#dash-file-format) ·
[Lights](#lights) · [ATSR-Hub](#atsr-hub) · [Dash designer](dash-designer.md) · [How it talks to the wheel](#how-it-talks-to-the-wheel) · [GT Neo](#gt-neo) ·
[Checking without the wheel](#checking-without-the-wheel) · [Troubleshooting](#troubleshooting)

## What it does

| When | Screen | Lights |
|---|---|---|
| A game is running (SimHub has data) | The chosen custom dash (built in: LMGT3 Ford Mustang GT3) | The chosen preset, or ATSR-Hub |
| No game | The plugin's logo with a rev-light sweep (option) | Ambient effects only (option; rev lights dark, no alerts) |
| Demo / Test button | The dash, fed by a simulated lap (every value, SimHub formulas included) | As in a game |
| USB mode off, wheel unplugged, SimHub closed | The wheel's own dash (`page dp`) | SimPro's colours |

## Setup

1. Install the FX Unleashed wheel app patch (build 9 is current; builds 4 to 9 each include the one before) through
   SimPro, following the install steps at fxunleashed.com/start (the download and details are on fxunleashed.com/firmware).
2. Plug the wheel's USB cable into the PC (the wheel stays on the base as usual). The wheel only picks USB mode when it
   powers up with the cable in: one that started on the base ignores a cable plugged in later (the PC sees no
   device), so power it up with the cable in. Build 6 and later
   restart it into USB mode on its own when the cable goes in; the plugin then waits `BootGrace` (6 s) before
   talking to it.
3. SimHub → FXPro RPM Sync → **USB mode**: tick **Use USB mode**, press **Test on the wheel (8 s)**. With the patched
   firmware the demo dash shows steadily; with stock firmware the wheel's own dash flickers through it.
4. Tick **My wheel runs the patched firmware**. The status line then reads "Standing by" / "Lights on", and
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
- User dashes: JSON files in `SimHub\PluginsData\Common\FXProRpmSync\Dashes\*.json` appear in the dash list. Make
  them in the **dash designer** (button on the settings page; [dash-designer.md](dash-designer.md)), which also imports
  SimHub dashes and shows the dash on the wheel while you edit. The settings page shows a live preview and warnings.

What the screen can and can't do (all measured on the wheel):

- Everything is drawn at runtime with the screen's own commands (TJC `fill`, `xstr`); the screen image isn't changed.
- **No new pictures.** Images become `fill` rectangles: fine for flat artwork in a few colours (the logo is ~3,300
  rectangles after sorting its colours into black/red/white), too slow for photos.
- **Fonts are fixed:** 128 anti-aliased fonts in the stock screen image, by id. Their real sizes are in
  `Usb/FontMetrics.cs`: in Simagic's S* fonts a digit is as wide as the font is tall, and text wider than its box
  wraps onto a line the screen doesn't show. Narrow families: 963 (ids 96-101), isf23 (92-95), 992 (69-72).
- **Budget:** the plugin sends at most 25 KB/s to the screen (see [below](#how-it-talks-to-the-wheel)). The Mustang's
  static layer is ~15 KB (~0.6 s); its updates ~1.8 KB/s at 10 per second.

## Dash file format

Moved to [dash-format.md](dash-format.md) (FormatVersion 2: conditions, data colours, bars, gradients, images, SimHub
formula bindings). Designing, importing SimHub dashes, the HTTP API and `fxdash`: [dash-designer.md](dash-designer.md).

## Lights

**Per car state** (2026-09-29, [light-states-plan.md](light-states-plan.md)): the lights follow what the car is
doing (no game, menu, engine off, engine start, driving, pit limiter, engine stop). Each preset has its own look when
parked, a start-up and shutdown animation, and pit limiter lights; a car's own pit limiter lights can be saved from the
Lights tab ("Pit limiter lights for this car", `UsbSettings.CarLimiters`). The Lights tab previews every state.

LED numbering (the firmware's renderer order), **mapped with a camera on the wheel (2026-09-27)**, as the driver sees it:

| LEDs | Where |
|---|---|
| 0-3 | left lower cluster, bottom to top (bottom, middle, inner "OK", outer) |
| 4, 5 | left top pair: outer (mic), inner (PIT) |
| 6, 7 | right top pair: inner, outer |
| 8-11 | right lower cluster, top to bottom (outer, inner, middle, bottom) |
| 12-16 | encoders ABS, TC, BB, DIFF, MAP |
| 17-19 / 20-22 | the lights left / right of the rev bar, top to bottom |
| 23-37 | rev lights, left to right |

`Ui/WheelView.cs` draws them there, and `assets/Simagic_FX-Pro.atsrdevice` places them the same way (re-import it in
ATSR-Hub after an update). To re-map (another wheel, a camera pointed at it): POST 38 colours to
`/api/wheel/leds?seconds=N` (designer API) one LED at a time and find each in the picture.

Built-in presets (`Usb/Lights.cs`): Prism, Aurora, Synthwave, Ember, Glacier, Scanner, Stealth, Full
Rainbow. **Customize** copies the current one into an editor: per group (buttons, encoders, left, right, rev) an effect
(solid, breathing, colour wave, rainbow flow, breathing rainbow, scanner, sparkle, off; rev lights also "shift
lights"), 1-4 colours, speed and brightness. Rev lights use the car's real shift lights from the rev light database
(and per-car overrides, per gear) when known, else the profile's colours from SimHub's redline. Alerts blink over
everything, first match wins: ABS (left side, amber 12/s), TC (right side, blue), pit limiter (rev lights), blue and
yellow flags (encoders), low fuel and DRS (off by default).

**While driving** (2026-10-02): a preset's buttons and encoders hold one still frame of their effect (`StillWhileDriving`:
pulses and sweeps become steady colour, gradients, plasma, flames and stars freeze) and the three side lights each side stay
dark (`SidesDarkWhileDriving`: they're for alerts). Both are per profile, on by default, and cover every in-car state (start,
drive, pit limiter, stop) so the start-up settles into the look it ends in; parked looks still animate. Alerts, the limiter
lights and the levels' flash on a change are the only things that move. `tools/UsbTest` checks every built-in preset on both
wheels. With ATSR-Hub as the light source the animation is ATSR-Hub's own theme, not ours.

**Rev bar extras** (`Usb/RevExtras.cs`, a profile's `Extras`): in some situations the rev bar shows something else, and the
shift lights win again within 8% of the shift point (except for the first two):

| Extra | When | Shows |
|---|---|---|
| Pit speed bar | in the pit lane, over 5 km/h | a pointer: middle = the limit, left below it (cyan), green within 2 km/h, right above it (red, filled from the middle) |
| Launch aid (off by default) | first gear, under 30 km/h, not in the pit lane | a pointer for revs (or throttle, or clutch) against the car's target (Lights tab > "Launch aid for this car"): amber below, green on, red above; no target saved = 70% of max revs |
| Lift and coast | LMU, progress over 2.5% | the bar fills from both ends (magenta) |
| Fuel while refuelling | stopped, fuel rising | the tank filling (cyan) |
| Brake bias change | 2.5 s after the bias moves | a pointer for the change from where the car started (+-4%) |

The pit speed limit comes from the game when it gives one (iRacing's track info, the F1 games' session packet, SimHub's
`PitLimiterSpeed`); AMS2 and LMU give none, so it's **learned from the speed the pit limiter holds** (steady for a second in the
pit lane) and remembered per game and track (`UsbSettings.PitSpeeds`). ATSR-Hub guesses 60 km/h for those games instead.
`RevExtrasState` keeps the history (learned limit, refuelling, bias reference), `RevBars` draws the pictures.

**Try the lights** (`Usb/LightScenarios.cs`, Lights tab bottom card, `GET /api/wheel/scenario[?id=|stop=1]`): scripted situations (pit lane with a
known and a learned limit, launch, lift and coast, refuel, bias, spotter, indicators, every alert, the engine states, a mirrored
blinking-out car, an iRacing car on the game's numbers) played on the wheel through the same pipeline as a session (`ScenarioRun`: car
state, extras history, light engine, with the player's preset and the alerts/extras the scenario shows switched on in a copy). `UsbTest`
plays them all and draws `OUT/scenarios/*.png`. What to try with the wheel and games: [test-checklist.md](test-checklist.md);
`tools/session-report.py` summarises a session from the log.

**Spotter** (2026-10-03): a car alongside lights the **six buttons on its side** of the wheel (FX Pro buttons 0-5 left, 6-11 right; GT Neo grips 5-9
left, 0-4 right), on every built-in preset, so it can't be missed, **red and flashing twice a second** (250 ms lit, lit first, then fully dark: ATSR-Hub's spotter does the same; every alert's cycle starts when it comes on). The three small lights beside the rev bar are kept for warnings like
TC (right) and ABS (left), as on a real car's dash; the turn indicators (off by default) use them too. Two alert-only groups carry this
(`LedGroup.ButtonsLeft/ButtonsRight`, offered in the alert editor); saved lights that still have the spotter on the small lights are moved
to the buttons at start-up (`LightsRepair.UpgradeSpotter`, a copy of the settings first), one the player set up differently is left alone.

**Alerts** added: left and right turn indicators (off by default, the small lights; the game blinks them). The spotter is ignored in
time trial, hot lap and lone qualifying. SimHub works out the spotter itself for games that don't give one (a car within 10 m, 45-135
degrees either side, over 5 km/h); a log line records each change (`[FXProRpmSync] spotter: ...`).

**Car data fixes** (2026-10-02, from comparing with ATSR-Hub): ids match as plain words ("Ligier JS P320" finds Lovely's
`ligier_js_p320`); a record whose lights share a position is ignored; a car with no redline colour but a blink interval blinks its
lit lights out at the redline (`RpmLayout.FlashDark`, 174 of 951 cars; SimPro can only flash a colour, so it gets none); the flash
can't come before the last light; mirrored cars stay mirrored on the wheel's 15 LEDs (`RpmLayout.Mirrored`). The first lap of a
session never reads as invalid. **iRacing cars Lovely lacks** (it has 85) use the game's own shift light numbers instead of a guess from the
redline: the first light, last light and blink rpm from the session info (`DriverInfo.DriverCarSLFirstRPM` / `SLLastRPM` / `SLBlinkRPM`), with the
preset's pattern laid between them (`RpmLayout.FromAnchors`). Each car change writes one log line saying which data built the lights and where the
15 LEDs sit (`[FXProRpmSync] car ...`). **`UsbTest.exe OUT audit <lovely data dir> <ams2.json>`** pushes every record through the mapping and
lists lopsided, out-of-order or lost lights (3 Lovely files are unreadable: their colour list is one short).

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

How it works (the properties ATSR-Hub EVO publishes in SimHub):
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
- **Spotter over ATSR-Hub's lights** (`UsbSettings.SpotterOverExternal`, on by default; also over SimHub's device): a car alongside
  draws the active preset's spotter alerts (the six buttons on that side) on top of the frame. Nothing else of the preset's alerts is drawn over it.
  ATSR-Hub's own spotter lights buttons 0-5 (left) and 6-11 (right) in red when SimHub's spotter flag is on.

## SimHub LED device

The FX Pro also shows up in SimHub's own **Devices** (brand "FX Unleashed", "FX Pro wheel (USB mode)"), so SimHub's LED
editor, any SimHub LED profile, and ATSR-Hub through SimHub drive its lights (`Usb/SimHubLedDevice.cs`).

- Registered like SimHub's built-in devices: a public
  `IDeviceDescriptorsRegistry` in the plugin DLL (found by SimHub's `PluginFinder`, which scans per type and catches
  load errors) returns one `DeviceDescriptor` whose factory builds a `LedModuleDevice` with
  `LedModuleSettings<FXProLedDriver>`. SimHub calls `GetDevices` without a try/catch (an exception there breaks its whole
  device list), so it can't throw; the driver is created in its own method, so a SimHub with another
  `ILedDeviceManager` loses only this device.
- Layout: telemetry LEDs 21 split 3 + 15 + 3 (left side lights 17-19, rev bar 23-37, right side lights 20-22), 12 button
  LEDs (0-11), 5 encoders (12-16), 38 individual LEDs in the firmware's order (these override the groups).
- Every frame SimHub computes goes through `FXProLedDriver.Display` to `UsbController.PublishDevice`, used when the Lights
  tab says "Lights come from: SimHub device" (the LED ceiling and press lights still apply). Connected = USB mode on and
  the wheel found.
- **Not yet tried inside SimHub on the wheel.** When it is, the ATSR-Hub bridge below can go.

## How it talks to the wheel

Wheel USB HID (VID 0483, PID 0529), reports of 65 bytes (`Usb/UsbTransport.cs`):

- `F2 09 len data`: bytes straight to the screen's UART (TJC commands, each ending `FF FF FF`), up to 61 per report.
- `F2 0A len addr data`: the updater's flash-program command, whose address check is compiled out, used as a RAM store.
  **RAM only** (0x20000000-0x2000FFFF, enforced): an address in flash would really program flash.
- Build 4's control block at 0x20007000: LED mode word (`FXL1`+4 = every LED from RAM, black = off), mirror of the
  stock palettes at +0x10 (written before the mode), LED colours at +0x90 (4 bytes each: B, R, G, brightness 1-90);
  host-screen word at +4 (`FXS1`), active only while a USB screen packet arrived in the last second.
- **Pacing: 25 KB/s to the screen**, 150 ms pause after a `page` command. Unpaced, USB pushes ~30 KB/s, faster than the
  screen draws small fills; the overflow is lost, and a burst right after a page change froze the screen until the
  wheel was power cycled (seen 2026-09-27, fixed by the pacing). While a screen command waits for the pacing, the LED
  frames keep going out (`FxHostScreen.Waiting`): a dash that sends too much slows its own updates, never the lights.
- The renderer sends only what changed: a value redraws the strip its old and new text cover (not its whole box), the
  elements under that strip are drawn back in place and only those above are marked for a redraw (so two overlapping
  elements can't keep redrawing each other), and anything over a solid pop-up waits until it's uncovered. Screen
  commands are at most 58 characters; longer text is cut. `tools/UsbTest <dir> traffic DASH.json [diverge]` measures
  a dash's traffic over the demo lap and checks every update against a full redraw.
- No flashing: text is redrawn in one step (one `xstr` with the band's background) wherever the rows it's drawn in
  are one colour. Bars never paint under text drawn over them (the text shows the bar's colour at its centre), a
  shape draws only what no solid shape above it covers, and a repaint starts from the topmost solid pop-up under it.
  Text rows crossing a border line can't be redrawn without wiping the line: `fxdash fit-bands` fixes the layout.
  `UsbTest <dir> traffic DASH.json flash [seconds]` replays every command and reports any pixel that blinks.
- Demo SimHub formulas are evaluated round-robin within ~4 ms per frame, so a dash with hundreds of them can't stall
  the lights either.
- Taking the screen: keepalive, mode word, 30 ms, lone `FF FF FF` (clears half a command the wheel may have sent), then
  `page 0` (no screen timers), `vis 255,0`, `cls 0`. Giving it back: `page dp`, keepalive stops, mode word 0.
- USB limit: ~500 reports/s in total. Typical load: LEDs 90 reports/s (38 LEDs, 30 frames/s), dash ~40/s.
- If SimHub dies: the screen returns to the wheel within a second (keepalive gate) but stays on page 0 until the next
  page change; the LEDs stay in all-LEDs mode until a power cycle.

## Auto calibration (shift points)

Added 2026-09-29. Car tuning > "Find this car's shift points" (`ShiftCalibrator.cs`, `Ui/CalibrationCard.cs`; both
modes, any wheel). Not learned from when the driver shifts (that learns habits, or the lights' own point back): from
the car's telemetry while calibrating (DataUpdate, flat out = throttle ≥ 95%, no brake or clutch, 0.3 s after a
shift):
- each gear's ratio (RPM per km/h) and its pull (m/s², from speed over 0.25 s) in 100-RPM buckets; the limiter = the
  highest RPM at full throttle;
- the best upshift from gear g: where the next gear, at the RPM it lands on (x ratio), pulls harder. Measured directly
  where the next gear has data there; otherwise from the engine's torque curve (pull ÷ ratio, each RPM from the lowest
  gear that covered it), comparing both gears' force at the same speed so air resistance drops out. Flat-out pulls
  only enter a gear partway up its revs, so for gears above first this is the usual way. No crossing before the
  limiter = shift at the limiter;
- Apply saves a `Calibrated` override (`CarOverride.ShiftByGear`): the car's own light pattern moved per gear so it
  flashes at the measured point (spacing kept); unmeasured gears take the nearest measured one.
- Checked against a simulated car (`tools/UsbTest/CalibrationTests.cs`): within 80 RPM of the physics answer.
- It finds the fastest shift point, which isn't always where a game's own dash flashes (some flash at the limiter).

## GT Neo

Added 2026-09-29 (plan and decisions: [gt-neo-plan.md](gt-neo-plan.md)). The GT Neo has no screen and needs no
firmware: its USB mode is stock. The settings page calls it **"USB"** and never mentions firmware on the GT Neo's pages.

- **Which wheel:** `Usb/WheelDetector.cs` checks every 2 s for each wheel's own USB device (FX Pro `VID_0483&PID_0529`,
  GT Neo `VID_3670&PID_0805`) and asks SimPro (`get_device_list`) which wheels are on the base (product ids
  `0000000002030000` / `0000000002060000`). The only wheel found becomes the active one (`UsbSettings.ActiveWheel`);
  with none found the pages keep their wheel; with both, the header chip turns into a switch ("GT Neo · switch to FX
  Pro"). `FXProRpmSyncPlugin.SwitchWheel` does the switch.
- **Per wheel:** `Usb/WheelModels.cs` holds everything that differs (LED count, groups, rings, screen, names).
  `LightEngine`, `WheelView`, the Lights and Wheel tabs, `ButtonLightsCard`, the ATSR-Hub map and the controller all
  take the active model. Settings tied to a wheel's LEDs and buttons (light preset, own lights, per-car/game lights,
  button map and bindings, lights source, ATSR-Hub device and map) stay in their usual `UsbSettings` fields for the
  active wheel; the other wheel's are kept in `UsbSettings.Wheels` and traded on a switch (`SwapWheel`). Old settings
  files load as the FX Pro's, unchanged. "Copy to the GT Neo / FX Pro" on the Lights tab copies a profile across.
- **Connecting:** switch the base off and on while holding **button 3** on the wheel (about 2 s). It then shows up on
  USB through the quick release until the base is switched off. SimHub's own GT Neo device uses the same mode.
- **LEDs** (`Usb/NeoTransport.cs`, same order as SimHub's own GT Neo driver): 73, ids 0-9 button lights, 10-57 four
  rings of 12 around the encoders (10, 22, 34, 46 first), 58-72 rev lights left to right. Feature report `F0`, 64
  bytes, byte 6 `EC`: `EC 02 01/00` takes/releases the LEDs, `EC 03 n` + n x (id, R, G, B), 13 per report. Only
  changed LEDs are sent; every second "host mode on" and the whole frame go out again. That overrules SimHub's own GT
  Neo device, which sends `EC 02 00` when it's switched off (the wheel then ignores `EC 03` until the next `EC 02 01`;
  seen 2026-09-29), and keeps the wheel from taking its LEDs back (it does after 5 s without an `EC` packet).
  Brightness is applied on the PC; the wheel also scales by its own brightness from SimPro (SimHub asks for 100%
  there). Never sent: `F0 [6]=00 [7]=CA` (hangs the wheel) or `F1` (update).
- **Effects** run per segment: each ring on its own, so rainbows and chasers go round the rings. Presets:
  `LightPresets.GtNeo` (ids `neo-...`). The GT Neo has no side lights; alerts on them use the ends of the rev bar (4
  LEDs each side). The Levels effect makes each ring a 12-segment gauge (`RingGauge`; docs/light-states-plan.md);
  on the FX Pro it keeps showing ABS/TC/BB/DIFF/MAP on its encoder lights.
- **SimHub's own GT Neo device** (SimHub > Devices > Simagic GT Neo) drives the same LEDs, so both at once flicker.
  The Lights tab offers **Turn it off** or **Use SimHub's device instead**, and **Turn it back on** later
  (`Usb/SimHubDevices.cs`: SimHub's `DevicesPlugin`, `DeviceInstance.Enabled`, then `DevicesPlugin.SaveSettings`). With
  "SimHub's GT Neo device" as the source the plugin sends no LED frames.
- **Buttons:** report 01, bits in bytes 3-7, as on the FX Pro (`WheelButtons` reads the active wheel's device).
- **Drawing:** traced from SimPro's front picture of the GT Neo (`tools/brand/trace_gtneo.py` -> `assets/gtneo-outline.svg`,
  positions in `WheelView.BuildGtNeo`), like the FX Pro's. The picture's LEDs are transparent cut-outs, so the rev slats,
  ring segments and buttons are measured. LED order checked on the wheel with colour patterns (2026-09-29): buttons 0-4
  the right grip from the bottom up, 5-9 the left grip from the top down; rings 10/22/34/46 upper left, upper right,
  lower left, lower right, each from 12 o'clock clockwise; rev 58-72 left to right.
- **Not yet tried on the wheel:** the plugin driving it (presets, alerts, sleep), the SimHub device switch, button
  bindings. Offline checks: `tools/UsbTest` `features` ("GT Neo ..."), `UI_WHEEL=gtneo UsbTest.exe OUT ui`.

## Library, screen mirror, wheel dash values, base per car

- **Library** (`Usb/Library.cs`, `Ui/LibraryPanel.cs`, `Ui/PackageDialog.cs`): the community library repo
  (fx-unleashed-library: `dashes/<id>/{dash.json, meta.json, preview.png}`, `savers/...`, `index.json`). Browse on the
  Dashes and Idle tabs; install writes the dash into the dashes folder (or a screensaver into the savers folder) and
  reloads: no restart. Every download is checked against the index's sha256 and the library rules (no `js:` or
  scripts folder, no newer dash format, size caps, `MinPlugin`). "Package for the library" (Dashes tab, or
  `fxdash package`) writes an item folder with the measured cost and a rendered preview. The base URL can be a local
  folder (`UsbSettings.LibraryUrl`), used by `UsbTest` (`FXU_LIBRARY=<checkout>`).
- **Website install**: `POST /api/library/install?kind=&id=` on the designer server; the item is looked up by id in the
  library's own index (never a URL from the request) and the user confirms in a dialog. **Designer server security:**
  requests carrying an `Origin` must come from the server's own pages; only `/api/library/*` answers
  `https://fxunleashed.com` (CORS + Private Network Access preflight). Before this, the server answered every request
  with `Access-Control-Allow-Origin: *` and no Origin check, so any web page could send it simple POSTs.
- **Screen mirror** (`Usb/ScreenMirror.cs`): every command `FxHostScreen` sends is replayed on a simulated screen;
  `GET /mirror` (OBS browser source), `/api/wheel/frame.png`, `/api/wheel/mirror`. While the wheel shows one of its own
  dashes: SimPro's picture of it (not live values). **No options in the address**: how the page looks is
  `UsbSettings.Mirror` (`Usb/MirrorSettings.cs`), set in SimHub on the Streaming tab (`Ui/UnlockedStreamTab.cs`, with an animated
  drawing of the page, `Ui/MirrorSketch.cs`) and sent in `/api/wheel/mirror` as `options`; the page re-applies them on
  every poll, so OBS follows a change at once. Frame: none / thin line / bezel / carbon / neon glow (bezel and carbon are
  housings that hold the lights, the others float), with an accent colour or "follow the rev lights" (the lead rev LED);
  rev lights and side lights each on or off, style dots / bars / line, rev lights above or below; background see-through /
  dark / green screen; corner radius; hide while idle; updates per second. The buttons' and encoders' lights ("all") and the
  status line are gone. Geometry lives twice (the page's `applyOptions()` and `MirrorSketch.Rebuild()`): change both.
  `UsbTest <dir> mirror [port] [seconds] [options.json]` serves it with the demo lap (options re-read when the file
  changes); set `FXDASH_DESIGNER_DIR=Usb/Designer` to edit the page without rebuilding.
- **Wheel dash values** (`Ui/WheelValuesPanel.cs`): any value the wheel's own dashes draw can come from another SimHub
  property or a formula (`ncalc:`/`js:`), in natural units (gaps in seconds, fuel per lap in litres); stored in
  `Settings.Feed.Overrides`, shared with standard mode's SimGame feed. Dashes tab > Values.
- **Base per car** (`BaseSwitcher.cs`, Car tuning tab): rotation (`max_wheel_angle` + `wheel_angle_limit`) and overall
  force (`total_force`) of the base per car or game, through SimPro's `servos` part (live, never saved into the preset),
  restored on exit / off / Restore; a user edit in SimPro is re-captured without our car values. Off by default;
  **not yet tried on a base**.
- **Setup status** (`Usb/WheelSetup.cs`): while the wheel isn't on USB, "Wheel on the base" (SimPro lists a wheel),
  "Restarting into USB mode" (it just left the base), "Unknown USB device" (Windows' VID_0000/PID_0002).

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

