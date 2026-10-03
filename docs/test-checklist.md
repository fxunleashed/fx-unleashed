# What to try on the wheel

Written 2026-10-02 after a session that changed a lot of the lights without the wheel in reach. Everything below was checked as far
as it can be without hardware (`tools/UsbTest`: 756 checks, and every scenario drawn as a picture); this is what's left for a person
with the wheel and the games. Tick things off here; `tools/session-report.py` reads the SimHub log afterwards and says which of the
session-only ones showed up.

## 0. Deploy

1. Close SimHub (it locks the DLL), then in the repo: `dotnet build -c Release`. Check it landed:
   `cmp "C:/Program Files (x86)/SimHub/User.FXProRpmSync.dll" bin/Release/net48/User.FXProRpmSync.dll`.
2. Start SimHub. The UI profiler is off (its flag file `PluginsData\Common\FXProRpmSync\ui-profile.on` is gone), so the log is quiet.
3. Lights tab. **Lights come from: FX Unleashed** for these tests (with ATSR-Hub as the source, ATSR-Hub's own theme animates and our
   presets don't apply; only the spotter overlay does).

## 1. No game needed: "Try the lights" (Lights tab, bottom card)

Each button plays a scripted situation on the wheel through the same code a real session uses, with your selected preset, and the card
says what to look for. The wheel must be connected in USB mode (the card says so when it isn't). The same from a script:
`GET http://localhost:8899/api/wheel/scenario` lists them, `?id=pit-speed` plays one, `?stop=1` ends it.

| Scenario | You should see |
|---|---|
| Driving: still lights, dark sides | the rev bar moves; buttons and encoders hold one steady look; the 3 + 3 lights beside the rev bar are OFF |
| Engine and menu states | idle look; dimmer in a menu; nearly dark engine off; start-up animation that settles into the driving look; limiter lights; shutdown |
| Spotter | left car: the six buttons on the left side of the wheel orange; right car: the six on the right; both; back to the theme when clear. The three small lights beside the rev bar stay dark (they're for TC / ABS) |
| Turn indicators | left / right side lights blink amber with the indicator |
| Every alert in turn | ABS, TC, 7 flags, low fuel, DRS, rev limiter, invalid lap, stalled: each lights its own part (2 s each) |
| Pit speed bar (limit known) | pointer: cyan left of the middle below 60 km/h, green in the middle at it, red filling right above it; gone once stopped |
| Pit speed bar learns the limit | first second: the limiter's blue lights; then a green pointer in the middle, moving right/left as speed changes |
| Launch aid (target 5000) | amber pointer left under 5000 rpm, green in the middle on it, red right above it; gone past 30 km/h |
| Lift and coast (LMU) | magenta fills from both ends |
| Refuelling | cyan fill left to right with the tank, goes ~1.5 s after the fuel stops |
| Brake bias change | amber pointer right (more front) / left (less) for ~2.5 s; nothing while braking and drifting |
| Encoder levels | encoders show ABS, TC, bias, DIFF, map in the preset's low-to-high colours; a changed one dips for a second |
| Ligier P320 | builds up from both outer ends evenly (green, yellow, blue in the middle); above 6700 the whole bar blinks out and back |
| Car with a red flash | builds up left to right; steady red above the shift point |
| iRacing car from the game's numbers | first light at 6000, last at 7000, flash at 7500 |
| Lap invalidated | the dash's LAP INVALID overlay and the red encoder alert |

If one looks wrong, the picture of it from the offline run is `OUT/scenarios/<id>.png` (see section 4).

## 2. Needs a real session

| Feature | How | What you should see / the log |
|---|---|---|
| Spotter from SimHub | AMS2 (or iRacing, ACC) race with cars alongside, lights source FX Unleashed, spotter alerts on in the preset | the six buttons on that side orange; log `spotter: car on the left yes, ...`. If the lights don't show but the log does, it's the preset (alerts list); if the log never shows, SimHub isn't reporting it |
| Spotter over ATSR-Hub | Lights from ATSR-Hub, same session | the six buttons on that side orange over ATSR's frame; the Lights tab says "+ spotter" |
| First lap never "invalid" | start a session, drive the first lap, watch SLIPSTREAM / APEX / NOCTURNE | no LAP INVALID on lap 1; log `lap 1 flagged invalid before any lap was completed` if the game did flag it; a cut on lap 2+ still shows it |
| Pit speed, AMS2 / LMU | drive into the pit lane with the limiter ON, hold it for a few seconds | pointer appears about a second after the speed settles; next time on that track it's there from the start. `session-report.py` shows the learned km/h under "Learned" |
| Pit speed, iRacing | pit lane | pointer from the first moment (iRacing gives the limit) |
| iRacing car without Lovely data | load a car not in Lovely (log line says "the game's own shift lights") | first rev light at the game's first rpm, last at its last, flash at its blink |
| Cars with a blinking-out redline | F1 24/25, LMU, ACC GT3 (174 of 951 cars) | the whole bar blinks dark at the limit (a colour flash before) |
| P320 and the 19 other AMS2 records | AMS2: Ligier JS P320, BMW M4 GT4, Lola B05/40, Ferrari 458, McLaren F1 GTR | P320: 10 mirrored lights; M4 GT4 / Lola: no stray middle light; tacho cars: a few real shift lights, not the whole bar. The plugin refreshes the library within 6 h or on restart |
| Mirrored cars | any symmetric car (P320, BMW M4 GT3, LMU hypercars) | left and right build up together |
| Launch aid | switch it on (Rev bar extras), set a target (Launch card), standing start | pointer as in the scenario |
| Lift and coast | LMU hybrid car, lift off | magenta fill |
| Refuel / brake bias on a real car | pit box with refuelling; change bias on a straight | as in the scenarios |
| Preset options persist | tick/untick "Hold still", "Side lights dark", the extras; restart SimHub | they stay |

## 3. Reading the result

```
python tools/session-report.py --hours 6
```

Close SimHub first if you want the learned pit speeds (it rewrites the settings file on exit). It lists every car loaded and where its
lights came from (our library, Lovely, the game's own numbers, or "NOT FOUND"), spotter changes, hidden first-lap flags, USB
drops, warnings and errors.

## 4. What the offline run shows

`UsbTest.exe OUT` (after `dotnet build -c Release` in `tools/UsbTest`) plays every scenario on the FX Pro and the GT Neo, checks the key
moments, and writes `OUT/scenarios/*.png`: a row of the wheel's LEDs every half second as the driver sees them (buttons, encoders,
left lights, rev bar, right lights). Flashing things are sampled every 0.5 s, so a fast blink can look steady or absent in a picture; the
checks watch every frame. `UsbTest.exe OUT audit <lovely data dir> <ams2.json>` maps every car record and flags lopsided or lost lights.

What none of this proves: the wheel hardware (LED order was mapped with a camera on 2026-09-27), what SimHub actually reports in each game,
and how the game's own flags behave.

## 5. Still never tried on the wheel or in a game (from NEXT.md, dated 2026-09-29)

Per-game/per-car light presets (E), alerts and encoder lights and button feedback (F), the SimHub LED device (D), the screen mirror (H),
shift point calibration (S), GT Neo support (Q), the "SimPro grabbed the game" warning (P), quick controls and actions (A), base settings
per car (I). Some may have been tried since; this file doesn't know.
