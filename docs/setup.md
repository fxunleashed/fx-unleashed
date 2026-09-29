# FX Unleashed: setting it up, start to finish

This guide takes you from nothing to custom dashes and lights on your FX Pro, one step at a time. Every step says
what you should see, and what to do if you don't. It's the same guide as on fxunleashed.com/start.

> FX Unleashed is an independent community project. It is not affiliated with, endorsed by or supported by Simagic.
> Simagic and FX Pro are trademarks of their owner, used only to say which hardware this works with. The software is
> provided "as is", without warranty of any kind. Use it at your own risk.

**Two ways to use it.** *Standard mode* needs nothing flashed: the rev lights follow each car and the wheel switches to
each car's dash, through SimPro. *Unleashed mode* is everything else in this guide (your own dashes, all 38 lights,
screensavers, the dash button) and needs modified wheel firmware. If you only want standard mode, do steps 1, 2 and 8.

## 1. What you need

- A **Simagic FX Pro** wheel on a **Simagic base** (tested: Alpha EVO), wheel app **1.3.11** (SimPro shows it).
- **SimPro Manager 3** (tested: 3.2.2) and **SimHub** (tested: 9.11; the free version is fine).
- The **wheel's USB cable**, and for the best setup a **data-only adapter** (a USB adapter that blocks the 5 V line),
  or a USB hub port with its power switch off. See step 4 for why.
- Windows 10 or 11.

## 2. Install the plugin

1. Close SimHub.
2. Download the latest release (fxunleashed.com or the GitHub releases page) and unzip it.
3. Copy `User.FXProRpmSync.dll` into SimHub's folder (usually `C:\Program Files (x86)\SimHub\`).
4. Start SimHub. It asks whether to enable the new plugin: say **yes**.

**You should see** "FX Unleashed" in SimHub's left menu. **If not:** Settings > Plugins, tick FX Unleashed, restart
SimHub. If Windows blocked the file, right-click the DLL > Properties > Unblock, then restart SimHub.

From now on the plugin updates itself: a blue banner appears when there's a new version, and one click installs it
(About tab). You can always roll back to the previous version there.

## 3. Before you flash: the risks

Unleashed mode needs a modified version of the wheel's own app. Read this before going on:

> Installing modified firmware on your wheel is at your own risk.
> - It isn't made, tested or supported by Simagic, and it may void your warranty.
> - A bug, an interrupted install or a difference in your hardware could stop the wheel from working correctly. In
>   rare cases you might not be able to recover it yourself.
> - We check every build (every change emulated against the stock firmware, then tested on a real wheel), but we
>   can't test every wheel, base, revision and setup.
> - It only changes the wheel's own app (lights, screen, buttons, USB). It doesn't change your base or force
>   feedback. You can go back to Simagic's stock firmware with SimPro at any time (step 10).
> - Don't unplug or power off the wheel while it installs.
>
> To the extent the law allows, the authors aren't liable for damage to your hardware or any other loss.

### Install the firmware

> **Status:** how the modified firmware is handed out is still being decided (it must never include Simagic's own
> firmware file or its key). The plan is a Firmware card in the plugin that builds the modified file from *your own*
> SimPro copy, checks it by checksum, and walks you through the steps below. Until then, the project shares builds
> directly with testers.

The firmware goes on through SimPro's own "reinstall firmware" button:

1. The wheel on USB (cable in, base on). Check SimPro's copy of the wheel app is the original (the card will do this
   by checksum).
2. Put the modified file in its place (same file name, in SimPro's `firmware\wheel\fx_pro` folder), then in SimPro
   reinstall wheel app 1.3.11 (Device > FX Pro > Firmware).
3. Wait for SimPro to finish. **Don't unplug anything.** If SimPro hangs after the wheel enters boot mode, close
   SimPro and start it again: it installs to a wheel that's already in boot mode.
4. **Put SimPro's original file back right away**, so no later update installs the modified one by accident.

**You should see** the wheel restart normally. On the plugin's Wheel tab (step 6) it shows "patch build 7" (or newer).

## 4. Wiring and power

- **The base powers the wheel.** The USB cable should carry data only: a 5 V-blocking USB adapter, or a hub port whose
  power switch is off (it cuts only the 5 V; data still flows).
- **At start-up:** turn the base on. The wheel starts on the base; about 3 seconds later it notices the PC on its cable
  and restarts into USB mode. The plugin waits 6 seconds for it to finish starting, then takes over when a game runs.
- **Avoid:** plugging the cable into a wheel on a base that is switched **off**. The cable's 5 V then also feeds the base
  through the quick release: the wheel can turn on and off and come up with a garbled screen. With a data-only cable
  this can't happen. If it happens anyway: base off, cable out, wait, start again.

**You should see** the Wheel tab's status go "Wheel on the base" > "Restarting into USB mode" > "Ready".

## 5. Game controls

In USB mode, the wheel's buttons and paddles reach the PC **only through the wheel's own USB controller** ("FX Pro"),
not through the base. So in each game, bind your wheel buttons and paddles once to that controller:

| Game | Where |
|---|---|
| iRacing | Options > Controls: click an action, press the button on the wheel |
| Assetto Corsa Competizione | Options > Controls > Wheel: pick the action, press the button |
| Le Mans Ultimate | Settings > Controls: select the input, press the button |
| Assetto Corsa | Content Manager > Settings > Assetto Corsa > Controls (or the game's Controls page) |
| rFactor 2 | Controls page in the launcher / in game |
| Automobilista 2 | Options > Controls > Configuration > Custom |
| F1 (EA) | Settings > Controls, Vibration & Force Feedback > edit your wheel's preset |

Steering, pedals and force feedback stay on the base as before: nothing changes there.

**The dash button** is button 40. Some games only list 32 buttons; bind it to a plugin action instead (Wheel tab >
Quick controls > Wheel buttons), e.g. "Next dash".

## 6. Turn on Unleashed mode

1. SimHub > FX Unleashed > click the **Unleashed** card. Read the warning and confirm.
2. Wheel tab: the status should say **Ready**, and the wheel line "patch build 7".
3. Press **Run the demo**: a demo lap plays on the wheel with a custom dash and lights. Press it again to stop.

**If** the status says "Firmware not confirmed": your wheel runs an older patch (builds 4-6) that can't report itself.
Press **Test on the wheel (8 s)**: if the demo dash stays steady, tick "My wheel runs the patched firmware". If the
wheel's own dash flickers through it, the wheel runs stock firmware (harmless; go back to step 3).

## 7. Dashes

- **Per car:** Dashes tab > pick a car (or the default list) > add dashes. The first one shows when the car loads;
  the dash button (or a key) steps through the list while driving.
- **The wheel's own dashes** work too: the plugin switches to them and feeds them SimHub's data over USB.
- **Library:** Dashes tab > Library > Browse. Install one in a click, then "Use for this car". No restart.
- **Your own:** "Edit in the designer" opens the dash designer in your browser. When it's done, "Package for the
  library" gets it ready to share.
- **Stream it:** Dashes tab > Stream the wheel's screen: an OBS browser source with the screen and the lights.

## 8. Lights

- **Presets:** Lights tab: pick a preset, or make your own in the editor (per group: rev lights, side lights, buttons,
  encoders). Presets can be set per car and per game.
- **Rev lights** follow each car's real shift lights where the data exists (Lovely Car Data).
- **Alerts:** flags, spotter, pit limiter, ABS/TC, low fuel and more, in the order you choose; add your own from any
  SimHub property or formula.
- **Encoders show their setting** (ABS, TC, bias, map, diff), and buttons can light up while pressed.
- **Other sources:** ATSR-Hub, or any SimHub LED profile (add "FX Pro wheel (USB mode)" in SimHub > Devices, then
  Lights tab > "SimHub device").
- **Brightness:** Wheel tab > Quick controls: a ceiling for every light, and night mode (dimmer screen and lights, on a
  schedule or with ATSR-Hub's night mode).

In standard mode, the rev lights still follow each car, through SimPro.

## 9. Idle and sleep

- **Screensavers** between sessions: the logo, a clock, start lights, your last session, a picture of your own, or
  one from the library (Idle & sleep tab > Library).
- **Sleep:** after a while with no game, the screen and lights go off; they wake when a game starts.

## 10. Updating, and going back to stock

- **Plugin:** the banner offers new versions; one click installs; Roll back is on the About tab.
- **Firmware:** a newer build goes on the same way as the first (step 3). The Wheel tab shows which build the wheel runs.
- **Back to stock:** with SimPro's original file in place (it always is after step 3), reinstall wheel app 1.3.11 in
  SimPro. That's Simagic's own firmware again. Reinstalling firmware always carries a small risk: don't unplug the
  wheel while it installs.

## 11. Troubleshooting

| What you see | What to do |
|---|---|
| Status "Waiting for the wheel" | The USB cable isn't in, or the wheel is off. Plug it in with the base on. |
| Status "Wheel on the base" for more than a few seconds | The cable isn't reaching the PC, or the firmware is older than build 6 (which restarts into USB mode). Unplug and plug the cable in again. |
| "Unknown USB device" | The wheel missed the PC's reset: unplug the cable and plug it in again. |
| Screen frozen on one page, ignores everything | Full power off: base off and the cable out, then start again. |
| Wheel turns on and off, screen garbled | USB into a wheel on a switched-off base: use a data-only cable (step 4); full power off, start again. |
| Parts of a dash stay on screen over another | Pick the dash again, or restart the game; report it with the dash's name. |
| Buttons don't work in the game | Bind them to the wheel's own controller ("FX Pro") in the game (step 5). |
| Dash text cut off | The dash needs a smaller font or bigger box (the designer's check shows where), or more padding (Dashes tab > Position). |
| Lights: "no data from ATSR-Hub" | Add the wheel in ATSR-Hub (it must be the active wheel), or clear the device in the plugin and let it pick again. |
| Lights: "no data from SimHub's device" | Add "FX Pro wheel (USB mode)" in SimHub > Devices and give it an LED profile. |
| "SimPro is reading <game>, not SimHub" (standard mode) | Close the game and start it again, with SimHub already running. |
| SimPro hangs after the wheel enters boot mode | Close SimPro, start it again, reinstall: it installs to a wheel already in boot mode. |

Still stuck? Open an issue on GitHub with the SimHub log (`SimHub\Logs\SimHub.txt`, lines with `[FXProRpmSync]`).
