# FX Unleashed: SimHub plugin for the Simagic FX Pro (guide for Claude Code and contributors)

A free SimHub plugin for the **Simagic FX Pro** (also the GT Neo and the FX). SimHub supplies the car, the RPM and the session
data; the plugin drives the wheel in two ways:

- **Standard mode:** per-car rev lights and a dash per car, pushed into **SimPro Manager 3** through its local API.
  Needs nothing flashed.
- **Unleashed mode** ("USB mode", `Usb/`): custom dashes on the screen and every one of the 38 LEDs, over the wheel's
  own USB HID. Needs the wheel running the FX Unleashed custom firmware (see "What never goes in this repo").

User docs: [README.md](README.md), [docs/setup.md](docs/setup.md). Developer reference for USB mode:
[docs/usb-mode.md](docs/usb-mode.md). Repos: this one (plugin), `fx-unleashed-library` (dashes, screensavers, car
light data), `fx-unleashed-site` (fxunleashed.com).

## Build, test, release

```
dotnet build -c Release                             # net48 DLL, copied into SimHub's folder (close SimHub first)
dotnet build -c Release -p:DeployToSimHub=false     # build only
tools/UsbTest/bin/Release/net48/UsbTest.exe OUT     # offline checks (build tools/UsbTest first); fail = don't ship
.\release.ps1 X.Y.Z [-Beta N] [-Publish]            # version, build, tests, zip, manifest, notes
python tools/publish-check.py . --history           # nothing secret or Simagic's in the tree or its history
```

- It references SimHub's own DLLs (`Private=False`, they can't be redistributed), so **CI can't build it** and releases
  are built on a maintainer's PC. `SIMHUB_INSTALL_PATH` overrides `C:\Program Files (x86)\SimHub\`.
- SimHub locks the DLL while it runs: close it before a deploy and check the copy landed.
- Log lines are prefixed `[FXProRpmSync]` in `SimHub\Logs\SimHub.txt`. Settings:
  `SimHub\PluginsData\Common\FXProRpmSyncPlugin.GeneralSettings.json` (SimHub rewrites it on exit: edit it only while
  SimHub is closed). Caches and data: `SimHub\PluginsData\Common\FXProRpmSync\`.
- `tools/UsbTest` compiles all plugin sources against SimHub's DLLs and checks the logic offline: dashes drawn through
  the real renderer, the lights engine, the updater, the settings UI rendered to PNG. Run it before every change that
  touches `Usb/`, `Ui/` or the updater. What it can't check is the wheel itself: say in a pull request what you tried
  on a wheel and what you couldn't.
- `tools/fxdash` is the dash command line (check, verify traffic, package for the library). The `/create-dash` skill
  (`.claude/skills/create-dash`) builds a dash end to end from a reference image or a SimHub dash.

## Rules that must hold

1. **Never rename the DLL (`User.FXProRpmSync.dll`) or the plugin class (`FXProRpmSyncPlugin`).** SimHub keys plugin
   activation and control bindings by them and the updater swaps the DLL by name. The display name ("FX Unleashed")
   is free to change.
2. **Settings and files only move forward.** `Settings.SettingsVersion` with `SettingsMigration.cs` (the old file is
   copied aside before a version change); dashes carry `FormatVersion`; the library has a `Schema`. Anything newer than
   the running plugin understands is refused whole, never half-loaded.
3. **The version lives in one place:** `<Version>` in `FXProRpmSync.csproj` (SemVer; `release.ps1` sets it). Tags are
   `vX.Y.Z`, pre-releases `vX.Y.Z-beta.N`.
4. **Dashes and library items run no code of their own:** a `js:` formula is allowed only if it passes `Usb/ScriptCheck.cs`
   (a short allow-list of safe parts, the same rules as the library's CI, shared test cases in `tools/UsbTest/script-vectors.json`);
   no scripts folder, no other executable content. 1 MB cap. The format is in [docs/dash-format.md](docs/dash-format.md).
5. **The updater installs only on a click, after every check passes** (hash, size, version, `minSimHub`), keeps the old
   DLL as `.old` for Roll back, and a start-up marker rolls back by itself after three starts that didn't finish. Don't
   weaken any of it.
6. **Build for people who don't know the code:** every state has a message that says what happened and what to do
   (see the wiring and firmware cards in `Ui/`). Nothing may throw out of `Init` or the SimHub LED device's
   `GetDevices`: failures show a plain page, not a stack trace.
7. **Keep the UI visual:** animated previews, not just lists of numbers. The settings UI is WPF built in code (no XAML).

## What never goes in this repo, a pull request or a release

- Any Simagic binary or image: firmware (`.sfu`, `.tft`, `.bin`, `.hex`), original or modified, or anything extracted
  from one. The custom firmware is published only as a release file of the separate `fx-unleashed-firmware`
  repository (with the patch that makes the same file from a user's own copy); never in this repository or in a plugin
  release.
- Encryption keys, decryption tools, decompiler output or disassembly, pasted or paraphrased line by line. Facts about
  how the wheel or SimPro behave (measured on the wheel, observed on the wire or at SimPro's local API) are fine; code
  is not.
- Other people's dashes, pictures, logos or data without their written permission and credit (`Source` and
  `Permission` in the item's `meta.json`). Simagic's pictures and marks are not ours to use.
- Secrets, local paths, personal data.

Run `python tools/publish-check.py . --history` before pushing anything that touches assets or tools, and on every
release zip. If in doubt, leave it out and ask in an issue.

## Files

| File | What |
|---|---|
| `FXProRpmSyncPlugin.cs` | SimHub plugin and settings. `DataUpdate` detects car changes; a background worker does all HTTP. Overrides API, SimHub actions and properties, preset capture and restore. |
| `SimProClient.cs` | SimPro's local API client. |
| `DashSwitcher.cs`, `DashCatalog.cs`, `DashSection.cs` | Standard mode: dash per car (switching, learning, SimPro's dash names and pictures). |
| `SimGameFeed.cs`, `SimProTelemetry.cs`, `SimHubFeedMapper.cs`, `FeedSection.cs`, `SimGameStub/` | Standard mode: SimHub's data into SimPro's built-in "SimGame" source (shared memory plus a tiny helper exe). |
| `CarLights.cs`, `CarLedDatabase.cs` | Per-car light data: ours (library `cars/`, read from the games' own files) then Lovely Car Data (CC BY-NC-SA 4.0, downloaded at run time). |
| `RpmLayout.cs`, `RpmLightsMapper.cs`, `LedPatterns.cs` | Rev light model, conversion to SimPro's `rpm_lights`, fallback patterns. |
| `SettingsControl.cs`, `OverridesSection.cs`, `LedStrip.cs`, `Ui/` | Settings UI. |
| `Updater.cs`, `SettingsMigration.cs`, `Legal.cs` | In-plugin updates and roll back; settings migrations; embedded legal texts (`docs/legal/*.md`). |
| `Usb/` | Unleashed mode: `UsbTransport` (HID), `UsbController` (the thread that owns screen and LEDs while a game runs), `DashModel`/`DashRenderer`/`DashValues`/`FontMetrics` (dashes), `ScreenShapes` (ovals and rounded boxes drawn by the screen itself, smoothed) / `ScreenTiles`/`ScreenRam` (pictures on the screen's RAM drive), `Lights`/`LightStates`/`RevExtras` (LED engine), `Library` (online library client), `DashTools`/`DesignerServer`/`Designer/` (the browser dash designer), `ScreenMirror` (OBS source), `SimHubLedDevice`, `AtsrBridge`, `WheelModels`/`WheelDetector` (FX Pro, GT Neo, FX), `FxTransport` (the FX's LEDs), `ScreenFlasher`/`ScreenImages` (the screen's RAM-drive header). |
| `tools/` | `UsbTest` (offline checks), `fxdash`, `atsr`, `brand` (our own logo drawing), `dashes` (generators for the library dashes), `publish-check.py`, `session-report.py`. |

## Standard mode: how a car change is handled

1. `DataUpdate` builds a target (game, car id, SimHub's max RPM and redline); the worker picks it up.
2. The selected SimPro preset's `rpm_lights` is read; the preset's **original** is captured once per preset and
   restored on exit. The plugin never saves into the preset; a change made in SimPro is detected and re-captured.
3. Scale max = SimPro's current game max RPM (`game_get_running_list` → `maxCarSpeed`), SimHub's as a fallback.
4. Base layout, first hit wins: our car light data (games' own files), Lovely Car Data (exact, then same series/team
   only if they all agree), else the chosen fallback style with SimHub's redline as the shift point. Only real
   per-car data is shown as real: no guessed flashes or stages.
5. A per-car override applies (offset, hand-set LEDs, or a pattern).
6. `RpmLightsMapper.ToSimPro` → `preset_set_dev_config` (applies live).

## SimPro's local API (undocumented; measured on the wheel with SimPro 3.2.2)

- `POST http://127.0.0.1:4010/simpro/api/v3/<method>`, JSON body, no auth. Response `{status, message, result}`.
  The UI is a web app served from the same port; its method names are an enum in `/simpro/js/main.*.js`.
- Useful: `get_device_list`, `preset_get_selected_dev_config`, `preset_get_dev_config`, `preset_set_dev_config`,
  `game_get_running_list`, `get_dev_dash_page`, `dash_get_list`. Device ids are `{device_uuid, product_uuid}`.
- `preset_set_dev_config {device_uuid, product_uuid, preset_uuid, part_type, part_id, config}` sends one part live.
  Read-backs lag writes by about a second: never compare right after a set.
- **Rev lights** (`part_type: "rpm_lights"`): thresholds `value[i]` are on a fixed 0..20000 = 0..100% scale of the
  game's max RPM; the FX Pro resolves them to **whole percents, rounding down**; colours must be SimPro's palette
  (`#ff0054 #ff6c00 #fffd51 #00ff84 #00fffc #0006ff #6000ff #eeeeee #000000`) or they show as off; the flash is the
  `rpm_redlines` stages. The FX Pro has no per-gear mode, so for cars whose lights differ per gear the plugin pushes the
  current gear's curve on every gear change.
- **Dash** (`part_type: "screens"`): the wheel shows the first entry of `selected_dashs`, so the plugin sends the
  rotation starting at the car's dash. Each push makes the wheel save to flash: push only when the wanted rotation
  changed.
- Measure, don't guess: SimHub's local API (`http://localhost:8888/api/getgamedata`) gives live telemetry; don't
  calibrate against a person reading an in-game HUD (it lags).

## Unleashed mode in short

The plugin talks to the wheel's USB HID directly (VID 0483, PID 0529 for the FX Pro) next to SimPro: a custom dash
drawn with the screen's own commands, every LED's colour at 30 frames a second. Details, protocol, the dash file
format, budgets and troubleshooting are in [docs/usb-mode.md](docs/usb-mode.md); the screen's RAM drive in
[docs/screen-ram.md](docs/screen-ram.md); dashes in [docs/dash-format.md](docs/dash-format.md) and
[docs/dash-designer.md](docs/dash-designer.md).

- **Screen pacing is required:** at most 25 KB/s, 150 ms after a page change. Unpaced traffic makes the screen drop
  commands and can freeze it until a power cycle.
- **Lights while driving:** buttons and encoders stay still, side lights dark; only alerts and the rev bar move.
- **Never leave anything in a half-applied state:** on a stop, an error or an unplug the wheel's own dash and
  SimPro's LED colours are restored.
- **Firmware changes are the user's decision.** The plugin never flashes the wheel's app. The screen card only changes
  the screen's RAM-drive header, after a clear warning. Anything that touches firmware needs a clear warning, a way
  back, and a maintainer's review.

## Working here

- Match the surrounding code: comment density, naming, the existing helpers. Small focused changes; no drive-by
  reformatting.
- Test before claiming something works; say what you did not test.
- Commit messages say why. One concern per pull request.
- Don't run the plugin against someone's wheel or SimHub without them asking: a push to the wheel writes flash.
