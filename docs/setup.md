# Get started

> FX Unleashed is an independent community project. It is not affiliated with, endorsed by or supported by Simagic.
> Simagic and FX Pro are trademarks of their owner, used only to say which hardware this works with. The software is
> provided "as is", without warranty of any kind. Use it at your own risk.

Three steps: **the custom firmware, the plugin, and (optional, but we highly recommend it) picture memory, the screen's RAM patch.**
About 15 minutes the first time. Every step says what you should see.

**Just want each car's rev lights and the wheel's own dash matched to the car?** That's *standard mode*: do step 2 only,
then start SimPro and SimHub and drive. Steps 1 and 3 are for *Unleashed mode*: your own dashes, all 38 lights in any
colour, screensavers and the dash button.

## What you need

- A **Simagic FX Pro** on a **Simagic base** (tested on an Alpha EVO), firmware **1.3.11** (SimPro shows it).
- **SimPro Manager 3** (tested: 3.2.2) and **SimHub** 9.11 or newer (the free version is fine), on Windows 10 or 11.
- The wheel's **USB cable** to the PC ([wiring and power](#wiring-and-power) says how).

## 1. Install the custom firmware

*For Unleashed mode. About 10 minutes.*

The wheel runs its own firmware. Unleashed mode needs our **custom firmware**: Simagic's own firmware with our changes. It doesn't
touch your base or your force feedback, and you can always flash Simagic's original back. **It is your wheel's own program, so this is
at your own risk: please read the [short notice](https://fxunleashed.com/firmware/#risks) first.** Do it when you have ten quiet
minutes, with the wheel on its base, the base on, and the wheel's USB cable plugged into the PC.

1. **Download the custom firmware:** [Download](https://github.com/fxunleashed/fx-unleashed-firmware/releases/latest). It is one
   small file ending in `.sfu`.
2. **Close SimPro** completely (also from its icon by the clock).
3. **Open SimPro's firmware folder.** Press the **Windows key + R**, paste
   `%LOCALAPPDATA%\SIMAGIC\Simpro3\firmware\wheel\fx_pro` and press Enter. A folder opens with one file in it,
   `FXPro_App-V1.3.11.0-00000000.sfu`. That is Simagic's original firmware.
4. **Keep Simagic's original:** copy that file to your Desktop. It is your way back.
5. **Swap in ours:** rename the file you downloaded to exactly `FXPro_App-V1.3.11.0-00000000.sfu` (right-click it, Rename), drag
   it into the open folder and choose **Replace the file in the destination**. (If your file names show no `.sfu` ending, leave
   it off when you type the name.)
6. **Flash it with SimPro:** start SimPro and open **Settings > Update**. Scroll to the bottom, to **Manual Firmware Flash**. On
   the **FX PRO** row press **Flash**, then select `FXPro_App-V1.3.11.0-00000000.sfu` in the folder from step 3 (paste that
   folder path into the window's address bar to get there). Wait until SimPro says it's done, and don't unplug or switch anything
   off meanwhile. (If it sits at 0% after the wheel goes into boot mode, close SimPro, open it again and flash again.)
7. **Put Simagic's original back:** copy the file from your Desktop into the same folder and choose **Replace**. Then SimPro's own
   copy is Simagic's again, and a later SimPro update can't flash ours by accident.

**You should see** the wheel restart and work as before. (In step 2 the plugin's Wheel tab will show a build number for it.) Next:
step 2, the plugin, and then step 3, the screen's RAM patch (optional, highly recommended).

<details>
<summary>For the curious: what this file is, how to check it, how to make it yourself</summary>

The file is Simagic's own firmware 1.3.11 with our changes (lights, screen, buttons, USB). It is Simagic's software, shared by a
community project without Simagic's involvement, and it may be taken down, so keep a copy. Its fingerprint (SHA-256) and size are
on the [firmware page](https://fxunleashed.com/firmware/#check) if you want to check your download.

Rather not download a modified file? The [firmware repository](https://github.com/fxunleashed/fx-unleashed-firmware) has a tool
that makes the identical file from your own copy of Simagic's original, and checks it matches. What each build changes and how
it was checked are on the [firmware page](https://fxunleashed.com/firmware/).

</details>

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
5. On the **Wheel** tab the status should say **Ready** and a build number for the custom firmware on the wheel line. Press **Run the demo**: a demo
   lap plays on the wheel's screen and lights.
6. **Next, step 3:** the screen's RAM patch makes dashes appear at once and in full colour. It's optional, but we highly
   recommend it.

From now on the plugin updates itself: a banner appears when there's a new version, one click installs it, and **Roll back**
is on the About tab.

*GT Neo:* it needs no custom firmware, so skip steps 1 and 3: hold button 3 while the base powers up, plug the USB cable in, and
do step 2. It is newer and less tested than the FX Pro.

*FX:* it needs no custom firmware either, so skip steps 1 and 3: update it to app 1.3.5 in SimPro if it runs an older one,
plug its USB cable in, and do step 2. Its lights use the wheel's own 8 colours. It is the newest wheel here and not yet
tried with the plugin.

## 3. Turn on picture memory

***Optional, but highly recommended.*** *About 3 minutes. Needs steps 1 and 2.* This is the screen's **RAM patch**: the plugin
installs it for you, from its own Wheel tab, and walks you through it.

Without it, dashes are drawn live and take a few seconds to appear, with fewer colours. With it the screen keeps each dash's
pictures **in its own memory**: full colour, drawn at once, instant switching between dashes, and screensavers look their best.
The library's picture-heavy dashes (NOCTURNE, HALO, SLIPSTREAM, APEX) are made for it.

It makes one small change inside the screen, once. If the screen doesn't accept it, it shows no dash until you press **Screen
recovery** in the same card, which puts Simagic's original back: nothing is lost. It is newer than the rest, so do it when you
can sit with the wheel for five minutes.

1. Wheel on the base, USB in, **no game running**. SimHub > FX Unleashed > **Wheel** tab.
2. Press **Check my screen (safe, 5 s)**. The screen turns red. If a **green card** appears in the middle, the RAM patch is already
   installed: skip the rest of this step and go on to step 4. If it stays a **solid red** screen, the patch isn't installed yet:
   carry on with the steps below.
3. In the **Firmware** card ("Screen memory and recovery"): open *Before you start: the risks*, switch on **I've read the risks
   and want to go ahead**, then press **Turn picture memory on**.
4. Follow the steps the plugin shows. It sends the change (10 seconds), then the screen checks itself (up to a minute) and
   restarts into its dash: press **It restarted into the dash**. Then switch the **base off and unplug the wheel's USB for 5
   seconds**, plug it back in and switch the base on. The plugin carries on by itself and checks the screen.

**You should see** "Done: picture memory is on". Each dash loads once the first time it shows (a few seconds), then shows
instantly until the wheel loses power. You can turn it off again in the same card.

**If the screen shows "Update Failed" or no dash:** open **Screen recovery** in the same card and follow its four steps. It puts
Simagic's header back.

## 4. Pick your dashes and lights

- **Dashes tab:** pick a car (or the default list) and add dashes. The first one shows when the car loads; the dash button
  steps through the list while you drive.
- **Library:** Dashes tab > Library > Browse, then **Install** (no restart) and **Use for this car**. You can also install from
  [fxunleashed.com/library](https://fxunleashed.com/library/).
- **Your own:** **Edit in the designer** opens it in your browser and shows the dash on the wheel as you edit; SimHub dashes
  can be converted.
- **Share:** **Share...** saves a dash as one file for a friend (they drop it on the Dashes tab, or press **Import a file...**).
  **Package for the library** makes a file for the library's submit form (you need a free GitHub account):
  [how to submit](https://fxunleashed.com/docs/library-submit/).
- **Lights tab:** pick a preset or edit one per group (rev lights, side lights, buttons, encoders), per car and per game. Rev
  lights follow each car's real shift lights where the data exists. Alerts (flags, spotter, pit limiter, ABS/TC, low fuel...)
  come in the order you choose. ATSR-Hub or any SimHub LED profile can drive the lights instead: the [lights guide](https://fxunleashed.com/docs/lights/)
  has the steps (SimHub > Devices > add "FX Pro wheel (USB mode)", the ATSR-Hub profile to download, and where each light lands).
- **Idle & sleep tab:** screensavers between sessions (the logo, a clock, your own picture, a library item) and sleep. It has its own
  screensaver library, separate from the dashes; **Share...** on one of your own screensavers submits it.
- **Wheel tab > Quick controls:** a brightness ceiling for every light, night mode, and wheel buttons for next dash and more.
- **Streaming tab:** an OBS browser source with the wheel's screen and lights.

## Wiring and power

- **The simple rule:** switch the base on first. The wheel starts on the base, notices the PC on its USB cable about 3 seconds
  later and restarts into USB mode. The Wheel tab goes "Wheel on the base", "Restarting into USB mode", "Ready".
- **Don't** plug the cable in while the base is **off**: the cable's power can then feed the base through the quick release, and
  the wheel can turn on and off and show a garbled screen. If that happens: base off, unplug the cable, wait a moment and start
  again.
- **For the tidy-minded:** the base should be what powers the wheel, so the best cable carries data only (a USB adapter that
  blocks the 5 V, or a hub port whose power switch is off; data still flows).

## Game controls

In Unleashed mode the wheel's buttons and paddles reach the PC **only through the wheel's own USB controller** ("FX Pro"), not
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

Steering, pedals and force feedback stay on the base as before. The dash button has a button number of its own (41 with
the current custom firmware; the Wheel tab shows it and lets you change it). Some games only list 32 buttons, so bind it to a plugin
action instead (Wheel tab > Quick controls > Wheel buttons), e.g. "Next dash". Dashes with pages (the built-in Mustang:
tyres and brakes, or the delta bar) flip with "Next page" / "Previous page" (Dashes tab, Switch while driving).

## Updating, and going back to stock

- **Plugin:** the banner offers new versions; one click installs; **Roll back** is on the About tab.
- **Custom firmware:** a newer build goes on the same way as the first (step 1). The Wheel tab shows which build the wheel runs.
- **Back to stock:** flash Simagic's original the same way as in step 1 (Settings > Update > Manual Firmware Flash, **Flash** on the FX PRO row), selecting Simagic's original file in SimPro's firmware folder (you put it back at the end of step 1). That is Simagic's own firmware again. Flashing always carries a small risk: don't unplug the wheel while it installs. Turn picture memory off first (next bullet) if you turned it on.
- **Picture memory off:** Wheel tab > Firmware card > **Turn it off**.

## Troubleshooting

| What you see | What to do |
|---|---|
| Status "Waiting for the wheel" | The USB cable isn't in, or the wheel is off. Plug it in with the base on. |
| Status "Wheel on the base" for more than a few seconds | The cable isn't reaching the PC. Unplug it and plug it in again. |
| "Unknown USB device" | The wheel missed the PC's reset: unplug the cable and plug it in again. |
| The Wheel tab shows no build number for the custom firmware | The custom firmware isn't installed (or is an old one): repeat step 1. |
| Screen frozen on one page, ignores everything | Full power off: base off and the cable out, then start again. |
| Wheel turns on and off, screen garbled | USB into a wheel on a switched-off base: use a data-only cable; full power off, start again. |
| Parts of a dash stay on screen over another | Pick the dash again, or restart the game; report it with the dash's name. |
| Buttons don't work in the game | Bind them to the wheel's own controller ("FX Pro") in the game. |
| Dash text cut off | The dash needs a smaller font or a bigger box (the designer's check shows where), or more padding (Dashes tab > Position). |
| Lights: "no data from ATSR-Hub" | Add the wheel in ATSR-Hub (it must be the active wheel), or clear the device in the plugin and let it pick again. |
| Lights: "no data from SimHub's device" | Add "FX Pro wheel (USB mode)" in SimHub > Devices and give it an LED profile. |
| "SimPro is reading <game>, not SimHub" (standard mode) | Close the game and start it again, with SimHub already running. |
| SimPro hangs after the wheel enters boot mode | Close SimPro, start it again and flash again: it flashes a wheel that's already in boot mode. |

Still stuck? Ask in the [Discord](https://discord.gg/P9Rz6fXrRc), or [open an issue](https://github.com/fxunleashed/fx-unleashed/issues)
with the SimHub log (`SimHub\Logs\SimHub.txt`, the lines with `[FXProRpmSync]`).
