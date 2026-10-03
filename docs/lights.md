# Lights in SimHub and ATSR-Hub

> **Where this fits:** an optional part of step 4 of the [setup guide](https://fxunleashed.com/start/#4-pick-your-dashes-and-lights). It needs the
> plugin ([step 2](https://fxunleashed.com/start/#2-install-the-plugin)) and Unleashed mode, so the custom firmware too
> ([step 1](https://fxunleashed.com/start/#1-install-the-custom-firmware)). If you just want good lights, the plugin's own presets (Lights tab)
> already do rev lights, flags, spotter and alerts: nothing here is needed.

The FX Pro has 38 lights. The plugin can drive them from three places, and you pick one on the **Lights** tab (**Lights come from**):

| Source | What it is | Pick it if |
|---|---|---|
| **Built-in** (default) | The plugin's own presets and editor: rev lights from each car's real shift lights, alerts, per car and per game | You want it to just work |
| **SimHub device** | The FX Pro as a device in SimHub's **Devices**, so SimHub's LED editor and any SimHub LED profile drive it | You already build or download SimHub LED profiles |
| **ATSR-Hub** | ATSR-Hub EVO's lights, themes and animations on the FX Pro | You use ATSR-Hub on your other gear and want the same look |

Whichever you pick, a car alongside still lights the spotter on the wheel's side buttons, and the Wheel tab's brightness ceiling and night mode still apply.

## The FX Pro in SimHub's Devices

1. Unleashed mode on, the wheel connected: the Wheel tab says **Ready**.
2. In SimHub open **Devices** and add a new device: under the brand **FX Unleashed** pick **FX Pro wheel (USB mode)**. (Not in the list? The plugin
   must be on, and SimHub restarted once after installing it. Only one can be added.)
3. Switch the device on.
4. In FX Unleashed, **Lights** tab: **Lights come from: SimHub device**. The line under it should say "SimHub device".
5. Back in SimHub, on the device, choose LED profiles as for any SimHub device, and use the LED editor's test to see them on the wheel.

**How SimHub's parts land on the wheel:**

| In SimHub | Wheel LEDs |
|---|---|
| **Telemetry LEDs** (21, "Side lights + rev lights") | 1 to 3: the left side lights, top to bottom. 4 to 18: the 15 rev lights, left to right. 19 to 21: the right side lights, top to bottom |
| **Buttons** (12) | 1 to 6 the left cluster, 7 to 12 the right cluster |
| **Encoders** (5) | ABS, TC, BB, DIFF, MAP |
| **Individual LEDs** (38, optional: switch it on in the device's settings) | One colour per LED, in the firmware's order: 0 to 11 buttons, 12 to 16 encoders, 17 to 19 left side, 20 to 22 right side, 23 to 37 rev lights. These override the groups |

Good to know:
- SimHub's LED profiles keep the lights off out of game by default, so nothing shows until a game runs (or you test). SimHub's default button
  and encoder colours (white) do show.
- A section's brightness of 0 in SimHub means off. The firmware's own limit is 90, and the plugin's brightness ceiling (Wheel tab) comes on top.
- The plugin sends SimHub's lights over the wheel's own USB cable, so the wheel must be in Unleashed mode. If the line says "no data from SimHub's device",
  the device is not added or not switched on in SimHub.

## ATSR-Hub

[ATSR-Hub EVO](https://github.com/ATSR-Alex/ATSR-Hub-EVO) (by ATSR-Alex, not part of this project) is a SimHub plugin with lights, themes and animations for many
wheels. It doesn't know the FX Pro, so we publish a layout file for it.

1. **Download the layout:** [Simagic_FX-Pro.atsrdevice](https://fxunleashed.com/downloads/Simagic_FX-Pro.atsrdevice). (It is also in every plugin release zip.)
2. **Copy it** into ATSR-Hub's preset folder, then restart SimHub:
   `Documents\SimHub\ATSR\device-presets\device-presets\steering-wheel-presets\`
   (if your Documents folder is in OneDrive, it is in there).
3. In ATSR-Hub's **Device Hub**, add a device: steering wheel, then the entry **Simagic FX-Pro**. The free ATSR-Hub drives one wheel (the first one
   added, or the one picked in the Device Hub); more need ATSR-Hub premium.
4. In FX Unleashed, **Lights** tab: **Lights come from: ATSR-Hub**. With one ATSR-Hub device the plugin picks it by itself; with several, choose it in the list.
5. Start a game: ATSR-Hub's shift lights, flags, spotter and animations show on the wheel. Until ATSR-Hub sends something, the built-in lights stay on.

**What the layout gives ATSR-Hub:** the FX Pro's 38 lights as ATSR-Hub sees them: 12 buttons, 5 encoders, the 6 side lights and the 15 rev lights, with the wheel's
USB ids so ATSR-Hub finds it. ATSR-Hub's button-press effects don't fire (the layout has no button inputs), and its animation theme is ATSR-Hub's own, not ours.
If your layout numbers the lights differently, the Lights tab has an optional **LED map** (38 ATSR-Hub indexes, one per wheel LED, `-1` = off).

## If something looks wrong

| What you see | What to do |
|---|---|
| "no data from SimHub's device" | Add **FX Pro wheel (USB mode)** in SimHub's Devices and switch it on |
| "no data from ATSR-Hub" | Add the wheel in ATSR-Hub's Device Hub (it must be the active wheel), or clear the device in the plugin and let it pick again |
| Lights from SimHub or ATSR-Hub stay dark out of a game | Normal for most profiles. Start a game, or use the test in SimHub's LED editor |
| The wheel isn't in USB mode | The Wheel tab says why (cable, base, custom firmware): see [wiring and power](https://fxunleashed.com/start/#wiring-and-power) |
| Anything else | Ask in the [Discord](https://discord.gg/P9Rz6fXrRc) or [open an issue](https://github.com/fxunleashed/fx-unleashed/issues) |

Developer notes on how both work (the properties ATSR-Hub publishes, the device's driver): [usb-mode.md](usb-mode.md).
