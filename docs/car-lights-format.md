# Car light data from the games (format 1)

Per car: the dash's rev lights (when each light comes on, in what colour) and what the dash shows with the pit
limiter on, **taken from the game's own files** for every game version. Made by the extraction tools (one per game,
private repo `fx-unleashed-cardata`), published in the library repo (`fx-unleashed-library/cars/`), downloaded by
the plugin (`CarLightsDatabase`, refreshed every 6 hours, checked against the index's sha256, cached in
`PluginsData\Common\FXProRpmSync\CarLights\`). The plugin prefers it to Lovely Car Data; a car it doesn't have falls
back to Lovely, then to the light preset's pattern.

| Game | Tool | What it reads |
|---|---|---|
| AMS2 (`ams2`) | `python -m cardata ams2 update` | `Vehicles/**/*.crd` (names), `<car>_cockpit.bin` (the RPM bar range; Reiza's cars inside `HRDFPERSISTENT.bff`, mods loose), `GUI/display_*.bgui` (the dash: rev steps, limiter lights), the lights' textures (colours) |
| iRacing (`iracing`) | `python -m cardata iracing update` | each car's own model: the switches that draw the dash from RPM, gear, pit limiter and time (which light, from what RPM, in what colour, per gear, the blink), the lights' textures (colours), and the car's script (the pit limiter's speed steps, shift lamps the script switches) |

## Files

```
cars/index.json      {"schema": 1, "games": {"ams2": {"file": "cars/ams2.json", "sha256", "count",
                      "simhubGame": "Automobilista2", "gameVersion": "1.6.9.96", "updated"}}}
cars/<game>.json     the game's cars (below)
cars/CHANGELOG.md    what each update added and changed, newest first
```

`<game>.json`:

```json
{
 "schema": 1, "game": "ams2", "simhubGame": "Automobilista2",
 "gameVersion": "1.6.9.96", "steamBuild": "25391793", "updated": "...", "count": 466,
 "cars": [ { ...a car... } ]
}
```

A plugin that reads format 1 ignores a file or index with a higher `schema` (and keeps what it has).

## A car

```json
{
 "carId": "BMW M4 GT3",                  // what SimHub reports as the car id for this game (the match key)
 "aliases": ["BMW_M4_GT3"],              // other names it goes by (the game's internal id)
 "manufacturer": "BMW", "class": "GT3", "classId": "GT3_Gen2", "year": 2023,
 "source": "reiza",                      // AMS2: "reiza" or "mod" (a mod installed where the data was made)
 "dash": "shift-lights",                 // why rev may be null: tacho | no-range | no-dash | no-lights | no-cockpit
 "rev": {
   "range": [6200, 7000],                // the dash's first and last step
   "steps": 6,
   "leds": [                             // every light of the bar, in bar order
     {"pos": 0.0, "stages": [[6200, "#35FF0B"], [7000, "#FF0000"]]},
     {"pos": 0.4506, "stages": [[6840, "#FF0000"]]}
   ],
   "colourGuessed": true,                // optional: some colours couldn't be read and follow green, yellow, red
   "layout": "step-order",               // optional: not a straight bar (a grid, two columns); pos = step order,
                                         // or "mirrored-column": one column on the dash screen shown on both halves
                                         // of the row, step 1 at the ends (best guess: the screen texture's mapping
                                         // onto the wheel model isn't readable)
   "withTacho": true,                    // optional: shift lights hung on a sweep tacho (range starts at 0)
   // iRacing (all optional):
   "blink": [{"rpm": 11650, "period": 0.5,          // from this rpm the row blinks; one cycle takes `period` s
              "frames": [{"s": 0.25, "leds": [null, null]}, {"s": 0.25, "leds": ["#268DFF", "#268DFF"]}]}],
   "gears": {"R": {...}, "N": {...}, "1": {...}},  // the lights per gear when they differ: each a rev object of
                                                    // its own (range, steps, leds, blink); the outer one is then
                                                    // the curve most forward gears share (else the middle gear's)
   "extra": [{"stages": [[7700, "#FF121A"]]}],     // lights apart from the row (a separate shift lamp), "extraBlink"
   "lamp": true, "face": true,                     // one lamp the car's script switches on; `face`: it is the tacho's
   "lampByTrackType": {"4": 7700}                  // face changing colour; another rpm on iRacing track type 4
 },
 "limiter": {                            // null: the dash shows nothing special
   "onBar": true,                        // the rev bar's lights change pattern
   "leds": [{"pos": 0.0, "colour": "#FF429E"}, {"pos": 0.2619, "colour": "#35FF0B"}],
   "overSpeed": [{"leds": [...]}, ...],  // optional: extra layers over it (probably near / over the pit speed)
   // iRacing (all optional):
   "blink": {"period": 0.5, "frames": [...]},      // the limiter lights blink (frames as for rev)
   "bySpeed": [                                    // what the row shows at each speed step, slowest first; `leds`
     {"from": null, "leds": [...]},                // above is the step in force just under the limit
     {"from": 0.5, "rel": true, "leds": [...], "blink": {...}}   // from 0.5 km/h over the limit (`rel`); without
   ],                                              // `rel`, from an absolute speed in km/h
   "speedStates": [{"speedState": 2, "leds": [...]}],   // the same when the script's speed steps couldn't be read
   "withRev": true                                 // the rev lights keep working under the limiter's lights
 },
 "anchors": {"base": [9500, 11700, 11825, 11950],  // iRacing: the script's four numbers (first, shift, last, blink:
             "gears": {"1": [...]}},               // what the SDK reports), per gear when the script sets them
 "carNumber": 99,                                  // iRacing's car id
 "notes": ["..."],                                 // how the record was read, what was assumed
 "since": "1.6.9.96", "changed": "1.6.9.96",   // game version first seen / last changed
 "missingSince": "1.7.0.1"                     // optional: no longer found (a removed car, an uninstalled mod)
}
```

- **`pos`**: where along the bar, 0 to 1. A horizontal bar runs left to right (symmetric patterns stay symmetric);
  a vertical one is turned so its first step is at 0.
- **`stages`**: `[rpm, colour]` from the first change to the last. A light that's lit stays lit; a later stage
  changes its colour (the M4 GT3's last step turns every light red). A light already in that colour doesn't repeat
  the stage. A `null` colour switches the light off (the BMW M Hybrid V8's lit pair moves inward: `[[7100,
  "#FFBE12"], [7400, null], [7850, "#00A1FF"]]`); the plugin keeps it dark from there until the flash.
- **Colours** are what an LED should show: the dominant hue of the light's texture (or of its part of a sprite sheet)
  at full brightness, `#RRGGBB`.
- **Limiter off the bar** (`"onBar": false`): the dash has pit lamps of its own elsewhere:
  `{"onBar": false, "colour": "#40FF00", "lights": 6}`.

### How the plugin uses it

- **Finding the car**: by SimHub's game name (`simhubGame`) and car id (or model), exact or one of the `aliases`.
  Failing that, a reported name that is a known name plus one short word (AMS2 reports "Formula V8 Gen2 Model1" for
  "Formula V8 Gen2") matches the longest such name; SimHub's log says so once per name, to add it as an alias.

- **Rev lights** (`CarLightsDatabase.ToProfile`): the lights in `pos` order become a car profile like Lovely's; a gap
  of about two spacings becomes one unused slot, three two, so a gap stays a gap on the wheel. When every light ends
  in one colour and the last step turns lights to it, that step is the flash (`Colors[0]`, `GearRpm[0]`).
  `RpmLayout.FromProfile` then maps it onto the wheel's 15 rev LEDs as for any car.
- **Pit limiter** (`CarLightsDatabase.ToLimiter`): on the bar = `LimiterStyle.CarPattern`, a colour per rev LED
  through the same mapping, steady; off the bar = `Solid` in its colour. A limiter the user saved for the car wins
  (`CarLimiterFor`). `overSpeed` isn't shown yet.

## AMS2 notes

- **Step RPMs** are evenly spaced over `range`: step 1 at the start, the last step at the end. Checked in the game on
  the BMW M4 GT3 (2026-09-29).
- `dash: "tacho"`: the range starts at 0 (a sweep tacho) and no shift lights hang on it: no rev lights for the wheel.
  Many sweeps carry real shift lights as lamps on some of their steps (the Formula Vee Gen2: 13 lamps over a 71-step
  0-7100 bar); those are the car's shift lights (`withTacho`), timed by the whole bar, when there are at most 24 and
  the first comes on above a quarter of the range.
- `dash: "no-range"`: the cockpit file has no RPM bar range (its other layout: GT4s, CART cars and others).
- Blinking isn't in the data: the flash and the limiter are shown steady.

## iRacing notes

- **Car id**: the car's folder under `cars\`, a nested one with a space (`stockcars chevyss`), as SimHub reports it;
  the car's name and short name are aliases.
- **What it is**: the sim draws the dash from the car's model, a tree of switches each picking what to draw from one
  value (RPM, gear, pit limiter on, its speed step, time). The tool sets those values and reads which lights are
  drawn in which colour: every RPM step of every gear, the blink's frames, the limiter's look at each speed step.
  Nothing is measured or estimated. Checked against Lovely Car Data: 45 of 82 cars identical in every gear, most of
  the rest within its rounding.
- **`anchors` are not the lights.** The script's four numbers (what the SDK reports as `DriverCarSLFirstRPM` etc.)
  often differ from when the dash lights really come on (the IR18: 9500 against 9550 for the first light).
- **Gears**: `R`, `N`, `1`... (the sim's gear value 0, 1, 2...). Many top gears never ask for a shift: their lights
  sit at the limiter. A light a gear never lights is missing from that gear's `leds`.
- **A light switched off again** has a `null` stage, as in AMS2; a light that changes colour has a stage per colour.
- **Colours**: the dominant hue of the light's texture rectangle; the lens drawn under an unlit light (grey, pale)
  isn't a colour. `colourGuessed`: the car's cockpit textures aren't installed where the data was made (a car not
  owned there), so the colours follow green, yellow, red and its limiter lights are left out.
- **Direction**: lights in the car's own space run left to right. A row modelled in a steering wheel's own space
  is turned so the first light to come on is at 0 (a note says so): right for every one-sided row, untested where
  it would matter.
- **Not included**: cars whose pack can't be read where the data was made (some variants and the pace cars), rev
  bars drawn on a screen (`dash: "tacho"`), and what the dash does with warnings, flags or the ignition off.
