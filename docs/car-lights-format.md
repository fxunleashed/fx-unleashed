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
| iRacing | planned | |

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
   "layout": "step-order",               // optional: not a straight bar (a grid, two columns); pos = step order
   "withTacho": true                     // optional: shift lights hung on a sweep tacho (range starts at 0)
 },
 "limiter": {                            // null: the dash shows nothing special
   "onBar": true,                        // the rev bar's lights change pattern
   "leds": [{"pos": 0.0, "colour": "#FF429E"}, {"pos": 0.2619, "colour": "#35FF0B"}],
   "overSpeed": [{"leds": [...]}, ...]   // optional: extra layers over it (probably near / over the pit speed)
 },
 "since": "1.6.9.96", "changed": "1.6.9.96",   // game version first seen / last changed
 "missingSince": "1.7.0.1"                     // optional: no longer found (a removed car, an uninstalled mod)
}
```

- **`pos`**: where along the bar, 0 to 1. A horizontal bar runs left to right (symmetric patterns stay symmetric);
  a vertical one is turned so its first step is at 0.
- **`stages`**: `[rpm, colour]` from the first change to the last. A light that's lit stays lit; a later stage
  changes its colour (the M4 GT3's last step turns every light red). A light already in that colour doesn't repeat
  the stage.
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
