# GT Neo support: plan

Written 2026-09-29. **Status (2026-09-29): phases 1-4 built** (not yet tried on the wheel): wheel models, detection and
the switch, model-driven lights (FX Pro frames checked byte-identical against the code before), the GT Neo USB link,
its pages and presets, the SimHub-device switch. Phase 0 was done offline from SimHub's own GT Neo driver (LED order
and protocol), so no mapping session was needed; where the button lights and rings sit on the wheel is still to be
checked against the drawing. Per-wheel settings are traded in place (`UsbSettings.SwapWheel`) instead of moved into
buckets, so no settings migration was needed. Reference: docs/usb-mode.md "GT Neo".

The plugin stays built around the FX Pro, and supports the Simagic **GT Neo** as a second wheel:
in standard mode (SimPro, already works for rev lights) and in Unleashed mode (every light, over the GT Neo's own
USB). The plugin detects which wheel is connected and shapes its pages to that wheel. With both connected, a switch
button picks which one the pages are for.

Wheel facts: firmware 1.4.4, tested on the maintainer's wheel 2026-09-29.

## What makes the GT Neo different

| | FX Pro | GT Neo |
|---|---|---|
| Unleashed link | Wheel's USB cable, patched firmware (build 4+) | Quick release USB: **hold button 3 at power-up** (SimHub's own GT Neo mode; SimPro shows "controlled by SimHub"). **No firmware change.** |
| USB id | `VID_0483 & PID_0529` "FX Pro Wheel" | `VID_3670 & PID_0805` "GT Neo" |
| SimPro id | `0000000002030000`, `old_device: true` | `0000000002060000`, `old_device: false` |
| Screen | Yes (dashes, screensavers, mirror, wheel dashes) | **None** |
| LEDs | 38: 12 buttons, 5 encoders, 6 side, 15 rev | 74 physical (host ids `0x00-0x48`): 15 rev, 18 button LEDs (SimHub's layout); the physical map is still to be measured |
| LED protocol | RAM table at `Ctrl+0x90` (`F2 0A`), 30 fps | Feature report `F0 .. EC 03`: 13 LEDs per report, R G B, needs an `EC` packet at least every 5 s |
| Colour limit | Brightness 1-90 (firmware cap) | 24-bit colour scaled by the wheel's saved brightness |
| Buttons | Report `01`, bits in bytes 3-7 (40), dash button 40 on build 5+ | Report `01`, bits in bytes 3-7 too; encoders as counters in bytes 19-22 |
| Firmware check | `F1` status + build marker | Serial suffix (`-01010404-` = 1.4.4); allowlist of tested versions |
| Other software | Nothing else drives it | **SimHub's own "Simagic GT Neo" device** drives it in the same mode, and fights our frames (seen 2026-09-29) |

## Review: where the plugin assumes an FX Pro

| Place | Assumption | Change |
|---|---|---|
| `Usb/Lights.cs` `LightEngine` | `Count = 38`, `Leds(group)` hard-coded ranges, `LedGroup` enum of FX Pro groups | LED count and group ranges from the wheel model |
| `Usb/Lights.cs` `LightEffect.Levels` | Encoders 12-16 = ABS, TC, BB, DIFF, MAP | Encoder meanings from the model; unknown ones user-bindable (like DIFF today) |
| `Usb/Lights.cs` `LightPresets` | Presets designed for FX Pro groups | Presets tagged with the wheels they suit; shared ones render on any wheel (missing groups skipped) |
| `Usb/UsbTransport.cs` | `FxUsb.FindPath` one filter; `FxConnection`, `FxLedWriter`, `FxHostScreen` | Per-model link: `FxLink` (today's code) and a new `NeoLink` |
| `Usb/UsbController.cs` | One wheel path; firmware gate (`Patched`, `build`); screen, dashes, saver, wheel-dash feed and LEDs in one loop; `FxLedWriter` created directly | Holds the active model and its link; screen code runs only when the model has a screen; firmware gate per model |
| `Usb/WheelSetup.cs` | "Plug the FX Pro's USB cable", build 6 restart story | Per-model setup text; GT Neo: "power up holding button 3" |
| `Usb/WheelButtons.cs` | FX Pro path, dash button 40 | Reads the active model's device; same bit layout, model-specific button names |
| `Usb/SimHubLedDevice.cs` | One device "FX Pro wheel (USB mode)", 3+15+3 / 12 / 5 / 38 | FX Pro only. For the GT Neo the source "SimHub device" means SimHub's **own** GT Neo device, and the plugin stands aside |
| `Usb/AtsrBridge.cs` | 38-entry map | Map length and default map from the model (ATSR-Hub has a GT-NEO profile) |
| `Ui/WheelView.cs` | FX Pro outline, 38 LED positions | Draws from the model: outline, LED positions, kinds |
| `Ui/UnlockedLightsTab.cs`, `Ui/ButtonLightsCard.cs` | Group names, 38-LED texts, button → LED 0-11 | Groups, texts and LED ranges from the model |
| `Ui/UnlockedWheelTab.cs` | Screen brightness, dash preview, firmware steps | Sections per capability (screen, firmware) |
| `SettingsControl.cs` | Unleashed tabs: Wheel, Dashes, Lights, Idle & sleep; mode card lists "Custom dashes" | Tabs and mode card text per model; GT Neo: Wheel, Lights, Idle & sleep (lights + sleep only), Car tuning, About |
| `Usb/ScreenMirror.cs`, `Usb/DesignerServer.cs` | FX Pro screen and 38 LEDs | Hidden / refused when the active wheel has no screen; `/api/wheel/leds` takes the model's count |
| `QuickControls.cs` | Screen actions (brightness, screen off, dash swap) | No-ops with a log line on a wheel without a screen |
| `UsbSettings` | Layout-bound settings are global: `ButtonLeds`, `WheelButtons`, `AtsrDevice/AtsrMap`, `CustomLights`, `UserLights`, `CarLights/GameLights`, `LightPreset` | Per-wheel buckets for the layout-bound ones (below) |
| Standard mode (`FXProRpmSyncPlugin`, `DashSwitcher`, `DashSection`) | Already takes the first SimPro wheel; skips dashes when there's no `screens` part | Show the Dashes/Dash data tabs only for a wheel with a screen; SimPro's native per-gear mode (`old_device: false`) still untested |

Everything else (car data, rev layouts, alerts, formulas, updater, library, per-car/game resolution, night mode,
sleep, LED ceiling) is wheel-independent and stays as it is.

## Design

### 1. Wheel models

`Usb/WheelModels.cs`: one static descriptor per wheel, the single place wheel differences live.

```
WheelModel { Id ("fxpro" | "gtneo"), Name, UsbFilter, SimProProduct, HasScreen, NeedsFirmware,
             LedCount, Groups: LedGroup -> int[] (only the groups it has), Encoders: name per encoder LED,
             Drawing (outline file, LED positions and kinds), ButtonCount, SetupText, CreateLink(path) }
```

- `LedGroup` stays one enum, as a superset (`Buttons, Encoders, SideLeft, SideRight, Rev`, plus GT Neo groups if its
  map shows them). Settings keep loading because the enum is saved by name.
- `LightEngine` takes a model: `new LightEngine(model)`, `Count => model.LedCount`, `Leds(g) => model.Groups[g]`
  (empty when the wheel lacks the group).
- A profile made on one wheel renders on the other: groups it doesn't have are skipped, and new groups fall back to
  `Off` (as `LightProfile.Group` already does).

### 2. Links

```
interface IWheelLink : IDisposable { void EnableLeds(); void SendLeds(LedColor[] frame, byte ceiling); void DisableLeds();
                                     IScreenSink Screen { get; }   // null on the GT Neo
                                     string Describe(); }
```

- `FxLink` wraps today's `FxConnection` + `FxLedWriter` + `FxHostScreen` + the button magic and `ReleaseInputReports`.
  It's a move, not a rewrite.
- `NeoLink`: `EC 02 01` on enable, `EC 03` in reports of 13 changed LEDs (a full frame is 6 reports; send only what
  changed, and a keepalive every 2 s), `EC 02 00` on disable. Colour × brightness ceiling on the PC side. Never
  sends `F0 [6]=00 [7]=CA` or any `F1` (see the firmware doc's command table).
- Later, if needed: the `80 54/55` frames (2 reports for all LEDs, 4 bits per channel), which SimHub's device
  probably uses. Only if `EC 03` turns out too slow.

### 3. Detection and the active wheel

`Usb/WheelDetector.cs`, polled every 2 s on the USB thread (it replaces `Probe`'s `FxUsb.FindPath`):
- **USB:** every model's `UsbFilter` → present, with its path.
- **Base (SimPro):** `get_device_list` → `product_uuid` of each wheel (moves `WheelSetup`'s SimPro poll here).
- Result: a list of `(Model, Link: Usb | Base, Path, Version)`.

Active wheel:
- `Settings.ActiveWheel` (model id, remembered). Auto rule: if exactly one wheel is found, it becomes active; if
  none, keep the last one (the pages keep their shape while the wheel is off); if more than one, keep the remembered
  one if it's among them, else the first USB one.
- **Switch button:** in the settings header, next to the mode cards, a wheel chip ("GT Neo · USB", "FX Pro · on the
  base"). With more than one wheel found it becomes a switch (click → the other wheel). With one wheel it's a plain
  label.
- Switching: the controller releases the old wheel (`Deactivate`: its LEDs back to its own, screen released), then
  takes the new one. The settings window rebuilds its tabs (`ShowMode()` already rebuilds; the `built` cache gets the
  model id in its key).
- Standard mode follows the same active wheel. SimPro drives only one wheel at a time, so the plugin shows the one
  SimPro reports, and the chip says so.

### 4. Settings per wheel

`UsbSettings.Wheels: Dictionary<string, WheelSettings>`, holding the layout-bound settings: `LightPreset`,
`CustomLights`, `UserLights`, `CarLights`, `GameLights`, `ButtonLeds`, `WheelButtons`, `PressLights`/`PressColor`,
`LightsFrom`, `AtsrDevice`, `AtsrMap`. Everything else stays global.
- Settings migration (`SettingsMigration.cs`, next `SettingsVersion`): today's fields move into `Wheels["fxpro"]`; the
  old file is copied aside, as for every version change.
- `UsbSettings.ActiveLights` / `FXProRpmSyncPlugin.ActiveLightsFor` resolve through the active wheel's bucket.
- User light profiles: stored per wheel, with "Copy to GT Neo / FX Pro" on a profile tile (the renderer handles
  missing groups).

### 5. SimHub's own GT Neo device

The GT Neo's USB mode is SimHub's mode, so many GT Neo owners have SimHub's "Simagic GT Neo" device on. Both writing
at once flickers.
- Detect it: SimHub's device list (the same `PluginManager` device API the D workstream used), or its settings file
  `PluginsData\Common\Devices\<id>\settings.json` with `DeviceTypeName: "Simagic GT Neo"`.
- If it's on and our lights source is built-in or ATSR-Hub: a card on the Wheel tab and the Lights tab, "SimHub's
  own GT Neo device is also driving the lights", with a **Turn it off** button (and "Use SimHub's device instead",
  which switches our source to it).
  - Turn it off through SimHub's device API while SimHub runs (find the call in phase 0, the way D found device
    registration). Never by editing its settings file: SimHub rewrites it on exit.
  - Log it, remember that we did (`WheelSettings.TurnedOffSimHubDevice`), and offer "Turn SimHub's device back on"
    under the lights source picker.
  - If the API can't do it on the user's SimHub version: fall back to the warning with the steps
    (SimHub > Devices > Simagic GT Neo > off).
- The source "SimHub device" on the GT Neo = the plugin sends no LED frames at all. Sleep and the LED ceiling then
  don't apply; say so under the picker.

## Phases

Each phase ends with `tools/UsbTest` (default and `features`) passing, and the FX Pro behaving exactly as before.
Phase 1 and 2 changes are pure refactors for the FX Pro.

### Phase 0: GT Neo facts (the user at the wheel, one session; plus offline work)

1. **LED map.** A script lights ids `0x00-0x48` one by one. The user
   says where each is, or a phone video is timed against the script. Output: id → position, group, kind.
   - Offline first: read what SimHub's own GT Neo driver does. It has the raw 74-LED order and which ones are rev (15) and buttons (18). It also shows which
     protocol it sends.
2. **Buttons and encoders:** button numbers per physical button (for bindings and press lights), encoder counters.
3. **Behaviour:** the wheel's own lights come back 5 s after the last `EC` packet; a 30 fps run for 30 min with no
   resets; brightness scaling (does the saved SimPro brightness cap our colours?).
4. **Inputs:** does the base still get the wheel's buttons in this mode (`joy.cpl`)? This only decides what the setup
   text tells the user about game bindings.
5. **SimHub's device API (offline):** how to find SimHub's GT Neo device instance and turn it off and on again at
   runtime (for design §5).
6. **Drawing:** our own GT Neo outline, drawn from the maintainer's own photo
   or measurements, with the LED positions from step 1.

### Phase 1: wheel models, detection and the switch (FX Pro only in effect)

- `WheelModels.cs` with the FX Pro model (today's numbers), `WheelDetector`, `Settings.ActiveWheel`.
- `UsbController.Probe` → detector; `WheelSetup` texts per model.
- Settings header: wheel chip + switch; tabs per model (`SettingsControl.Tabs()` asks the model: `HasScreen` →
  Dashes tab, screen parts of Idle & sleep and Wheel tab).
- Standard mode: the chip shows SimPro's wheel; Dashes and Dash data tabs only with a screen.
- Tests: detector against fake device lists (none / FX Pro / GT Neo / both, remembered choice), tab lists per model.

### Phase 2: model-driven lights (FX Pro only in effect)

- `LightEngine(model)`, `WheelView(model)`, groups and encoder names from the model; `SimHubLedDevice`,
  `AtsrBridge`, `ButtonLightsCard`, `/api/wheel/leds` sized by the model.
- Per-wheel settings buckets + migration.
- Tests: every built-in preset renders **byte-identical** FX Pro frames before and after (golden frames at fixed
  times with the demo lap); migration round trip on a copy of the user's settings file.

### Phase 3: GT Neo link

- GT Neo model (from phase 0), `NeoLink`, the controller gating the screen by `HasScreen`, firmware check by version
  allowlist (unknown version: lights off, "GT Neo firmware x.y.z isn't tested yet").
- `WheelButtons` on the GT Neo device; setup text ("hold button 3 while the base powers up").
- SimHub-device conflict warning (design §5).
- Recovery: wheel gone (power-off, reset by its USB stall guard) → back to "waiting", nothing else to do. The wheel
  restores its own lights 5 s after we stop.
- Tests: `NeoLink` report bytes (on/off, 13-LED chunks, changed-only, keepalive timing) against a fake HID writer.
- On the wheel (user): lights follow the presets, alerts, rev lights per car; sleep; switching between wheels.

### Phase 4: GT Neo pages and presets

- Wheel tab: GT Neo status, setup steps, the drawing with live LEDs, no screen section.
- Lights tab: groups the GT Neo has; encoder levels if its encoders sit on named functions; press lights.
- Presets for the GT Neo: at least the rev-light styles (car data), a calm ambient, a rainbow, a flags-heavy
  endurance one; the gallery shows the wheel's own drawing.
- ATSR-Hub bridge: default map for ATSR-Hub's GT-NEO layout.
- Idle & sleep: idle lights and sleep only.

### Phase 5: docs and release

- `docs/usb-mode.md` (GT Neo section), `docs/setup.md` (GT Neo path), README wheel table, website /start.
- The mode card per wheel: FX Pro "Unleashed" (as today); GT Neo **"USB"**, "Every light on the wheel, from SimHub",
  with no firmware wording anywhere on its pages (decision 4).
- Rights check (O2): the GT Neo drawing, any GT Neo layout file for ATSR-Hub.

## Effort

| Phase | Size | Needs the user |
|---|---|---|
| 0 | Small | Yes: LED map session, 30-min run, photo |
| 1 | Medium | No (a quick look at the switch at the end) |
| 2 | Medium-large: the widest change, many files | No |
| 3 | Medium | Yes: on-wheel check |
| 4 | Medium: mostly UI and preset design | Yes: preset look |
| 5 | Small | Review |

Phases 1 and 2 can start before phase 0 is done: they only need the FX Pro model. Phase 3 needs the LED map.

## Decisions (the user, 2026-09-29)

1. **Light profiles:** a separate list per wheel, with "Copy to the other wheel" on a profile tile (design §4).
2. **SimHub's own GT Neo device:** the plugin **offers to turn it off** (design §5), not just a warning.
3. **No SimHub LED device of our own for the GT Neo:** SimHub already has one. `SimHubLedDevice.cs` stays FX Pro only.
4. **Name on the GT Neo:** the mode is just called **"USB"**, and nothing on the GT Neo's pages mentions firmware
   (mode card, Wheel tab, setup text, status lines, About texts shown for it). The FX Pro keeps "Unleashed" and its
   firmware steps.
5. **Both wheels connected:** one wheel at a time. The plugin drives only the active wheel; the other keeps its own
   lights.
