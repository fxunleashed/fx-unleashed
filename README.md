<p align="center">
  <img src="assets/brand/lockup.png" alt="FX Unleashed" width="460">
</p>

> **FX Unleashed is an independent community project.** It is not affiliated with, endorsed by or supported by
> Simagic. Simagic and FX Pro are trademarks of their owner, used only to say which hardware this works with. The
> software is provided "as is", without warranty of any kind. Use it at your own risk.

# FX Unleashed

A free [SimHub](https://www.simhubdash.com/) plugin for the **Simagic FX Pro**: your own dashes on its 800x480 screen,
all 38 lights in any colour, screensavers, a community library and the dash button, all driven from SimHub.
Website, guide and library: **[fxunleashed.com](https://fxunleashed.com)**.

It has two modes:

| | Standard mode | Unleashed mode |
|---|---|---|
| Wheel firmware | stock, nothing flashed | the FXProDashes wheel app ([firmware](#the-firmware)) |
| Talks to the wheel | through SimPro Manager (over the base) | directly, over the wheel's own USB cable |
| Rev lights | each car's real shift lights | every one of the 38 LEDs, any colour, 30 frames a second |
| Dash | the wheel's own dashes, per car, fed with SimHub's data | your own dashes (designer, SimHub import, library), per car; or the wheel's own, fed over USB |
| Also | | screensavers and sleep, alerts and flags, encoder levels, button press lights, night mode, the dash button as button 40, ATSR-Hub or SimHub LED profiles, a screen mirror for streaming, base rotation/force per car |

## Unleashed mode

- **Your own dashes:** design them in the browser with the built-in designer (it shows them on the wheel while you
  edit and checks they can't flicker or lag), convert SimHub dashes, or install them from the
  [library](https://fxunleashed.com/library/) in a click. Pick one or more per car; the dash button steps through them.
- **Every light:** presets and an editor per group (rev lights, side lights, buttons, encoders), each car's real shift
  lights, alerts (flags, spotter, pit limiter, ABS/TC, low fuel, invalid lap, custom alerts from any SimHub value) in
  your order, encoder rings that show their setting, buttons that light while pressed. Or let ATSR-Hub or any SimHub
  LED profile drive them (the wheel shows up in SimHub's Devices).
- **Between sessions:** screensavers (the logo, a clock, start lights, your last session, a picture, a library item)
  and sleep.
- **Streaming:** an OBS browser source with the wheel's screen and lights, live (`http://127.0.0.1:8899/mirror`).
- **Quick controls:** screen on/off, brightness, a ceiling for every light, night mode, next dash / preset, from a key,
  a wheel button or a Stream Deck.
- **Updates itself:** a banner offers new versions, one click installs, one click rolls back.

The full setup, step by step: [docs/setup.md](docs/setup.md) (also at [fxunleashed.com/start](https://fxunleashed.com/start/)).

### The firmware

Unleashed mode needs a modified version of the wheel's own app (the FXProDashes wheel app, built 4-7). **Read the
[firmware warning](docs/legal/firmware-warning.md) first.** It changes only the wheel's app (lights, screen, buttons,
USB), never the base or force feedback; every change is emulated against the stock firmware before it's tried on a
wheel; going back to stock is SimPro's own reinstall. How it's handed out is still being decided: this repository
never contains Simagic's firmware or its key.

## Requirements

| | |
|---|---|
| Wheel | Simagic **FX Pro**, wheel app 1.3.11 (tested on an Alpha EVO base). The **GT Neo** too: its rev lights in standard mode, and every light in USB mode (hold button 3 while the base powers up). |
| SimPro Manager | **SimPro Manager 3** (tested with V3.2.2), running while you drive. |
| SimHub | Tested with 9.11. The free version is fine. |
| Unleashed mode | the modified wheel app, and the wheel's USB cable to the PC (best: data only, the base powers the wheel). |

## Install

1. Download the latest release from [Releases](https://github.com/fxunleashed/fx-unleashed/releases).
2. **Close SimHub**, then copy `User.FXProRpmSync.dll` from the zip into your SimHub folder
   (default `C:\Program Files (x86)\SimHub\`).
3. Start SimHub. In the **New plugins have been detected** window, turn on **FX Unleashed** and
   **Show in left main menu**, then click **Ok**.
4. Open **FX Unleashed** in SimHub's left menu. Later versions install from the plugin itself (About tab).

## Standard mode: the stock wheel, through SimPro

Start SimPro Manager and SimHub, select your usual preset in SimPro, and drive. That's it: on every car change the
settings page shows the car, where its lights came from, and the shift point / max RPM that were applied.

### Lights that change per gear

Some cars' real shift lights are different in each gear (iRacing's Porsche 911 Cup, for example, lights up much
earlier in 1st than in 6th). The FX Pro has no per-gear mode, so the plugin sends the current gear's lights to SimPro
every time you shift. This is on by default; untick **Switch the lights by gear** to use one set of lights in every
gear. Cars with the same lights in every gear aren't affected.

### Cars without rev light data

Cars that aren't in the database get the style you choose under **Cars without rev light data**:

- **Your SimPro preset:** your preset's own pattern and colors, rescaled to the car.
- **Built-in patterns:** left to right, edges to center, blocks of 3, and others, with color schemes (green/yellow/red,
  F1-style green/red/blue, or custom) and an optional solid or blinking flash at the shift point.

For these cars the shift point is SimHub's redline, which is 95% of max RPM unless you set it in SimHub's
**Car Settings**. Setting it there is the quickest fix for one car.

Untick **Use each car's real rev lights** to use your style for every car.

### Per-car overrides

When a car's lights don't match the game, open **Per-car overrides** while driving it (or pick a saved car) and
choose:

| Override | What it does |
|---|---|
| **Shift earlier / later** | Moves the whole sequence and the flash by an RPM offset. |
| **Set each LED** | Sets the RPM and color of each of the 15 LEDs, plus the shift flash, by hand. |
| **Use a pattern** | Applies one of the fallback patterns to this car, keeping the car's own shift point. |

Overrides are saved per game and car and applied automatically. **Shift earlier / later** keeps a car's per-gear
lights (every gear moves by the offset); the other two use the same lights in every gear.

**Tune while driving:** in SimHub → **Controls and events**, map the actions
`FXProRpmSyncPlugin.CurrentCarLightsLater` / `FXProRpmSyncPlugin.CurrentCarLightsEarlier` to wheel buttons.
Each press moves the current car's lights by 50 rpm and saves it as an override.

### Dash per car

When you get in a car, the wheel's screen switches to the dash saved for it. To save one, either:

- **pick it with the wheel's dash button while driving:** the last dash you pick in a car is used for it next time
  (turn off **Learn from the dash button** if you don't want that), or
- **choose it on the settings page** under **Dash per car** (gallery with SimPro's dash pictures), or press
  **Keep … for this car** to keep what the wheel shows. Chosen dashes aren't overwritten by learning.

Cars without a saved dash leave the wheel on whatever dash it's showing. The dash button still cycles through your preset's dashes;
the plugin only changes which one comes first. Map `FXProRpmSyncPlugin.KeepWheelDashForCurrentCar` to a button to keep
the current dash for the current car. Properties: `WheelDash`, `CurrentCarDash`.

### Dash values from SimHub (optional)

Turn on **Drive the wheel's dash from SimHub** to send SimHub's data to the wheel's dash instead of SimPro's own
game telemetry: gaps to the cars ahead and behind, fuel per lap, tyre data and everything else SimHub knows, in every
game SimHub supports. Units are converted to what the wheel expects (it still shows your SimPro unit settings).

- **Start SimHub before the game:** SimPro picks its data source when a game starts and keeps it until that game
  closes. Turn this on (or just start SimHub with it on) **before** starting the game; when SimPro itself starts
  doesn't matter. If SimPro grabbed the game anyway, the plugin says so within about 10 seconds: a red message at the
  top of its settings page, a SimHub notification and the `FeedProblem` / `FeedProblemText` properties, naming the
  game to close and start again (the only fix).
- **Turning it off mid-game is one-way until the game restarts:** SimPro switches to reading the game and keeps it
  until the game closes, so the plugin asks for confirmation first.
- While it's on, SimPro sees "SimGame" instead of your game: its per-game preset switching doesn't trigger, and the
  dash, rev lights and SimPro's telemetry effects use SimHub's data. Force feedback is unaffected.
- **Gap ahead / behind:** by race position (in your class by default) in races, nearest cars on track otherwise.
- **Game-specific values** (TC2, ARB, diff, engine braking, ERS mode): built in for ACC and iRacing; any other value
  can be taken from a SimHub property.
- **Demo:** with no game running, tick **Demo** to animate every dash value with a simulated lap (shifts, braking,
  lap times and delta, gaps, fuel, tyres, brakes, ERS, flags, a pit stop every 6 laps). Handy for trying dashes.
  It switches itself off when SimHub restarts.

### Your SimPro preset

The first time the plugin sees a preset, it stores that preset's RPM lights and dash order as the **original**. It restores the
original when SimHub exits, when you untick **Enabled**, or when you press **Restore original preset lights**.
If you edit the preset's RPM lights in SimPro, press **Re-capture preset** so the plugin uses the new version.

### SimHub properties

`FXProRpmSyncPlugin.Status`, `CurrentCar`, `AppliedMaxRpm`, `AppliedRedline`, `LightsSource`,
`CurrentCarOverride`, `WheelDash` and `CurrentCarDash` are available for dashboards.

Actions for **Controls and events**: `CurrentCarLightsLater`, `CurrentCarLightsEarlier`, `KeepWheelDashForCurrentCar`,
`ReapplyNow` and `RestoreOriginal`.

## How it works

1. SimHub reports a car change (game + car id + max RPM).
2. The plugin looks the car up in our car light data, read from the game's own files for each game version (AMS2 so
   far), then in Lovely Car Data (exact match, then same series / team if they all share one setup). Cars that
   aren't found get your fallback style. The game data also brings the car's own pit limiter lights (USB mode).
3. Any per-car override is applied.
4. The result is converted to SimPro's format. SimPro stores LED thresholds as a percentage of the **game max RPM
   SimPro reads for the current car**, so the plugin scales to that. It also snaps thresholds to whole percents and
   colors to SimPro's palette, which is what the FX Pro actually displays.
5. The lights are sent live to the wheel through SimPro Manager's local API (`preset_set_dev_config`), without
   saving the preset. For cars with per-gear lights, the current gear's lights are sent on every gear change.

**Dashes:** the FX Pro's dashes are built into the wheel; SimPro only sends the preset's dash list and the live
values. On a car change the plugin sends the dash list with the car's dash first (the wheel shows the first one), and
it watches which dash the wheel shows to learn your dash-button picks. With **Dash values from SimHub** on, the plugin
runs a small helper (`simgame.exe`) that SimPro reads as its built-in "SimGame" source, and fills it with SimHub's
data on every update.

SimPro's API is undocumented; see [CLAUDE.md](CLAUDE.md) for the reverse-engineered details and the FX Pro behavior
measured on the wheel.

## Troubleshooting

- **Plugin not in SimHub's menu:** check that the DLL is directly in the SimHub folder (not a subfolder) and that
  SimHub was closed while you copied it. If you skipped the enable prompt, turn the plugin and
  **Show in left main menu** on in SimHub's **Settings → Plugins**.
- **"No Simagic wheel found":** make sure SimPro Manager is running and shows the wheel.
- **Dash values from SimHub not showing:** the settings page shows what SimPro is reading. If it's your game rather
  than SimGame, close the game, make sure the option is on, and start the game again. If SimPro still doesn't pick
  up SimGame, fully restart SimPro Manager (and, if needed, the wheelbase).
- **Everything else:** check `SimHub\Logs\SimHub.txt` for lines starting with `[FXProRpmSync]`, and include them in
  your issue.

## Building from source

Requires the .NET SDK and a SimHub install (the project references SimHub's DLLs, which can't be redistributed, so CI
can't build it).

```
dotnet build -c Release                             # builds and copies the DLL into SimHub (close SimHub first)
dotnet build -c Release -p:DeployToSimHub=false     # build only
tools/UsbTest/bin/Release/net48/UsbTest.exe OUT     # offline checks (build tools/UsbTest first)
.\release.ps1 X.Y.Z [-Beta N] [-Publish]            # a release: version, build, tests, zip, manifest, notes
python tools/publish-check.py . --history           # nothing secret or Simagic's in the tree or its history
```

Set `SIMHUB_INSTALL_PATH` if SimHub isn't in `C:\Program Files (x86)\SimHub\`. Developer reference:
[CLAUDE.md](CLAUDE.md) and [docs/usb-mode.md](docs/usb-mode.md).

## Contributing

- **Issues:** [open one](https://github.com/fxunleashed/fx-unleashed/issues) with the game, the car, what happened
  versus what you expected, and the `[FXProRpmSync]` lines from `SimHub\Logs\SimHub.txt`.
- **Dashes and screensavers:** submit them to the [library](https://github.com/fxunleashed/fx-unleashed-library).
- **Wrong shift point for a car?** For AMS2 the lights come from the game's own files; tell us if one looks off.
  For other games the data comes from Lovely Car Data: a fix
  [there](https://github.com/Lovely-Sim-Racing/lovely-car-data) helps everyone. Until then, use a per-car override.
- **Pull requests:** keep them focused, and describe how you tested them on a wheel.

## License and credits

- **Plugin code:** [GPL-3.0](LICENSE): no warranty, no liability (sections 15 and 16). See [NOTICE](NOTICE).
- **Car rev light data:** [Lovely Car Data](https://github.com/Lovely-Sim-Racing/lovely-car-data) by Lovely Sim Racing
  and contributors, [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/), downloaded at runtime,
  not bundled.
- **Logo and drawings:** our own (`tools/brand/make_brand.py`).

FX Unleashed is an independent community project, not affiliated with or endorsed by Simagic. Simagic, FX Pro and
SimPro Manager are trademarks of their owners.
