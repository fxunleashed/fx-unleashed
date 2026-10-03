# Get started

> FX Unleashed is an independent community project. It is not affiliated with, endorsed by or supported by Simagic.
> Simagic and FX Pro are trademarks of their owner, used only to say which hardware this works with. The software is
> provided "as is", without warranty of any kind. Use it at your own risk.

Three steps: **the wheel app, the plugin, and (optional, but we highly recommend it) picture memory for the screen.**
About 15 minutes the first time. Every step says what you should see.

**Just want each car's rev lights and the wheel's own dash matched to the car?** That's *standard mode*: do step 2 only,
then start SimPro and SimHub and drive. Steps 1 and 3 are for *Unleashed mode*: your own dashes, all 38 lights in any
colour, screensavers and the dash button.

## What you need

- A **Simagic FX Pro** on a **Simagic base** (tested on an Alpha EVO), wheel app **1.3.11** (SimPro shows it).
- **SimPro Manager 3** (tested: 3.2.2) and **SimHub** 9.11 or newer (the free version is fine), on Windows 10 or 11.
- The wheel's **USB cable** to the PC ([wiring and power](#wiring-and-power) says how).

## 1. Install the wheel app

*For Unleashed mode. About 10 minutes.*

This replaces the wheel's own app (lights, screen, buttons, USB) with Simagic's app plus our changes. It doesn't touch your
base or force feedback. **Read the [firmware warning](https://fxunleashed.com/firmware/) first: it is your wheel's own program, at your own risk,
and Simagic's original is always one SimPro reinstall away.**

1. Download `FXUnleashed-wheelapp-build9.sfu` from the [wheel app release](https://github.com/fxunleashed/fx-unleashed-firmware/releases/latest)
   and check it: in PowerShell, `Get-FileHash .\FXUnleashed-wheelapp-build9.sfu` must show
   `85E7110BA368495120C7E84D210C194D5E9EEC9EACEB3F8F7C148F020FE0F458`. (Prefer not to download a modified vendor file? The
   [install guide](https://github.com/fxunleashed/fx-unleashed-firmware/blob/main/docs/install.md) makes the identical file
   from your own SimPro copy.)
2. Close SimPro. Open `%LOCALAPPDATA%\SIMAGIC\Simpro3\firmware\wheel\fx_pro\`. **Copy `FXPro_App-V1.3.11.0-00000000.sfu`
   somewhere safe**: it is Simagic's original, and your way back.
3. Copy the downloaded file into that folder as `FXPro_App-V1.3.11.0-00000000.sfu` (same name, replacing the original).
4. Wheel on the base, base on, USB cable in. Start SimPro, then Device > FX Pro > Firmware, and **reinstall wheel app
   1.3.11**. Don't unplug or switch anything off until SimPro says it's done. If it sits at 0% after the wheel enters boot
   mode, close SimPro, open it again and reinstall.
5. **Put the original file back** in that folder right away, so a later SimPro update can't install the modified one by accident.

**You should see** the wheel restart and work as before. (The plugin shows "patch build 9" in step 2.)

## 2. Install the plugin

*About 2 minutes.*

1. Close SimHub.
2. Download the zip from the [latest release](https://github.com/fxunleashed/fx-unleashed/releases/latest) and unzip it. Copy
   `User.FXProRpmSync.dll` into SimHub's folder (usually `C:\Program Files (x86)\SimHub\`).
3. Start SimHub. When it asks about the new plugin, turn on **FX Unleashed** and **Show in left main menu**, then OK.
   (Not there? SimHub > Settings > Plugins. If Windows blocked the file: right-click the DLL > Properties > Unblock, then
   restart SimHub.)
4. **Standard mode works now.** For **Unleashed mode** (step 1 done): plug the wheel's USB cable into the PC with the base on,
   open FX Unleashed in SimHub, click the **Unleashed** card and confirm the warning.
5. On the **Wheel** tab the status should say **Ready** and the wheel line **patch build 9**. Press **Run the demo**: a demo
   lap plays on the wheel's screen and lights.

From now on the plugin updates itself: a banner appears when there's a new version, one click installs it, and **Roll back**
is on the About tab.

*GT Neo:* it needs no wheel app, so skip steps 1 and 3: hold button 3 while the base powers up, plug the USB cable in, and
do step 2. It is newer and less tested than the FX Pro.

## 3. Turn on picture memory

***Optional, but highly recommended.*** *About 3 minutes. Needs step 1.*

Without it, dashes are drawn live with rectangles: they take a few seconds to appear and use fewer colours. With it the
plugin keeps each dash's backgrounds and pictures **in the screen itself**: full colour, drawn at once, switching between
dashes is instant, and screensavers look their best. The library's picture-heavy dashes (NOCTURNE, HALO, SLIPSTREAM, APEX)
are made for it.

It rewrites the first part of the screen's own image (its header), once, and the screen checks itself afterwards. If that
check fails, the screen shows no dash until Simagic's header is put back; the plugin's **Screen recovery** does that, and
nothing is lost. It is newer than the rest, so do it when you can sit with the wheel for five minutes.

1. Wheel on the base, USB in, **no game running**. SimHub > FX Unleashed > **Wheel** tab.
2. Press **Check my screen (safe, 5 s)**. A green card on a red screen means it already has picture memory: you're done.
3. In the **Firmware** card ("Screen memory and recovery"): open *Before you start: the risks*, switch on **I've read the risks
   and want to go ahead**, then press **Turn picture memory on**.
4. Follow the steps the plugin shows. It sends the header (10 s). The screen checks its image (up to a minute) and restarts
   into its dash: press **It restarted into the dash**. Then switch the **base off and unplug the wheel's USB for 5
   seconds**, plug it back in and switch the base on. The plugin carries on by itself and checks the screen.

**You should see** "Done: picture memory is on". Each dash loads once the first time it shows (a few seconds), then shows
instantly until the wheel loses power. You can turn it off again in the same card.

**If the screen shows "Update Failed" or no dash:** open **Screen recovery** in the same card and follow its four steps. It puts
Simagic's header back.

## 4. Pick your dashes and lights

- **Dashes tab:** pick a car (or the default list) and add dashes. The first one shows when the car loads; the dash button
  (button 40) steps through the list while you drive.
- **Library:** Dashes tab > Library > Browse, then **Install** (no restart) and **Use for this car**. You can also install from
  [fxunleashed.com/library](https://fxunleashed.com/library/).
- **Your own:** **Edit in the designer** opens it in your browser and shows the dash on the wheel as you edit; SimHub dashes
  can be converted.
- **Share:** **Share...** saves a dash as one file for a friend (they drop it on the Dashes tab, or press **Import a file...**).
  **Package for the library** makes a file for the library's submit form: [how to submit](https://fxunleashed.com/docs/library-submit/).
- **Lights tab:** pick a preset or edit one per group (rev lights, side lights, buttons, encoders), per car and per game. Rev
  lights follow each car's real shift lights where the data exists. Alerts (flags, spotter, pit limiter, ABS/TC, low fuel...)
  come in the order you choose. ATSR-Hub or any SimHub LED profile can drive the lights instead (SimHub > Devices > add
  "FX Pro wheel (USB mode)", then Lights tab > SimHub device).
- **Idle & sleep tab:** screensavers between sessions (the logo, a clock, your own picture, a library item) and sleep.
- **Wheel tab > Quick controls:** a brightness ceiling for every light, night mode, and wheel buttons for next dash and more.
- **Streaming tab:** an OBS browser source with the wheel's screen and lights.

## Wiring and power

- **The base powers the wheel.** The USB cable should carry data only: a 5 V-blocking USB adapter, or a hub port whose power
  switch is off (data still flows).
- **At start-up** turn the base on. The wheel starts on the base, notices the PC on its cable about 3 seconds later and
  restarts into USB mode. The Wheel tab goes "Wheel on the base", "Restarting into USB mode", "Ready".
- **Avoid** plugging the cable into a wheel on a base that is switched **off**: the cable's 5 V then also feeds the base through
  the quick release, and the wheel can turn on and off and come up with a garbled screen. If it happens: base off, cable out,
  wait, start again.

## Game controls

In USB mode the wheel's buttons and paddles reach the PC **only through the wheel's own USB controller** ("FX Pro"), not
through the base. In each game, bind your wheel buttons and paddles once to that controller:

| Game | Where |
|---|---|
| iRacing | Options > Controls: click an action, press the button on the wheel |
| Assetto Corsa Competizione | Options > Controls > Wheel: pick the action, press the button |
| Le Mans Ultimate | Settings > Controls: select the input, press the button |
| Assetto Corsa | Content Manager > Settings > Assetto Corsa > Controls (or the game's Controls page) |
| rFactor 2 | Controls page in the launcher / in game |
| Automobilista 2 | Options > Controls > Configuration > Custom |
| F1 (EA) | Settings > Controls, Vibration & Force Feedback > edit your wheel's preset |

Steering, pedals and force feedback stay on the base as before. The dash button is button 40; some games only list 32
buttons, so bind it to a plugin action instead (Wheel tab > Quick controls > Wheel buttons), e.g. "Next dash".

## Updating, and going back to stock

- **Plugin:** the banner offers new versions; one click installs; **Roll back** is on the About tab.
- **Wheel app:** a newer build goes on the same way as the first (step 1). The Wheel tab shows which build the wheel runs.
- **Back to stock:** with SimPro's original file in place (it is, after step 1.5), reinstall wheel app 1.3.11 in SimPro. That is
  Simagic's own app again. Reinstalling firmware always carries a small risk: don't unplug the wheel while it installs.
- **Picture memory off:** Wheel tab > Firmware card > **Turn it off**.

## Troubleshooting

| What you see | What to do |
|---|---|
| Status "Waiting for the wheel" | The USB cable isn't in, or the wheel is off. Plug it in with the base on. |
| Status "Wheel on the base" for more than a few seconds | The cable isn't reaching the PC. Unplug it and plug it in again. |
| "Unknown USB device" | The wheel missed the PC's reset: unplug the cable and plug it in again. |
| The wheel line doesn't say "patch build 9" | The wheel app isn't installed (or is an old build): repeat step 1. |
| Screen frozen on one page, ignores everything | Full power off: base off and the cable out, then start again. |
| Wheel turns on and off, screen garbled | USB into a wheel on a switched-off base: use a data-only cable; full power off, start again. |
| Parts of a dash stay on screen over another | Pick the dash again, or restart the game; report it with the dash's name. |
| Buttons don't work in the game | Bind them to the wheel's own controller ("FX Pro") in the game. |
| Dash text cut off | The dash needs a smaller font or a bigger box (the designer's check shows where), or more padding (Dashes tab > Position). |
| Lights: "no data from ATSR-Hub" | Add the wheel in ATSR-Hub (it must be the active wheel), or clear the device in the plugin and let it pick again. |
| Lights: "no data from SimHub's device" | Add "FX Pro wheel (USB mode)" in SimHub > Devices and give it an LED profile. |
| "SimPro is reading <game>, not SimHub" (standard mode) | Close the game and start it again, with SimHub already running. |
| SimPro hangs after the wheel enters boot mode | Close SimPro, start it again, reinstall: it installs to a wheel already in boot mode. |

Still stuck? [Open an issue](https://github.com/fxunleashed/fx-unleashed/issues) with the SimHub log
(`SimHub\Logs\SimHub.txt`, the lines with `[FXProRpmSync]`).
