# The hot-lap dashes: SLIPSTREAM and APEX

Status 2026-10-01: both installed on the user's wheel (`fx-slipstream`, `fx-apex`), being tested in LMU. Made from
scratch with generator scripts, `tools/dashes/make_slipstream.py` and `make_apex.py` (output committed next to them);
run `python make_X.py out.json`, then the /create-dash gates (`fxdash check --pad 10,20`, `fit-bands`, `verify`, render),
then `PUT /api/dashes/<id>` while SimHub runs.

## SLIPSTREAM (the user's favourite: "looks great")

A livery look built on one painted background picture (PIL, `slipstream-art@790x460`, MaxColors 16):
- left: a slanted graphite plate (parallelogram fully on screen; one off the left edge showed a notch and a fringe on
  the wheel) with a red edge and a tight red glow; gear in font 18 (f175a, 212 px); lap; speed and the lap time to
  tenths below;
- right: hero delta (font 47, w12_b 112 px, `Background` black), a 12-segment delta tape; PRED / LAST / BEST on slanted
  plates; **sector blocks**; **tyre heat map** (four blocks coloured by temperature band, pressures under);
- bottom strip: TC, ABS, BB, MAP, FUEL (laps), each sized to its text;
- overlays (opaque, cover whole cells): LAP INVALID over the delta, NEW BEST (purple, 5 s) over the plates, PIT LIMITER
  over the gear plate.
Gates: 0 flashes, ~1.6 KB/s average, worst second ~17 KB (an overlay appearing), 21 KB of screen RAM, 1.4 s static
without the RAM drive.

## APEX

Panels in charcoal with a cyan accent: timing column (delta, delta bar, predicted, last/best/lap), gear well and speed /
lap time, sectors and a 2x2 tyre grid, settings row; same overlays and data as SLIPSTREAM. The on-screen rev bar was
removed (the wheel has rev lights). ~1.8 KB/s. The user wasn't a fan of the look; kept as asked.

## Data (both dashes; SimHub formulas)

- **Sectors** use SimHub's own sector times (`DataCorePlugin.GameData.NewData.`):
  `Sector1Time`/`Sector2Time` (this lap, null until done; S3 has none: it ends with the lap), `SectorNLastLapTime`,
  `SectorNBestLapTime` (that sector on the session best lap), `SectorNBestTime` (best single sector). All seconds as
  TimeSpans. They update the moment you cross the split (logged live 2026-10-01, same sample as `CurrentSectorIndex`
  and LMU's raw `mCurSector1/2`). `currentlapgetsectortime()` (used first) came late.
- Each lap starts empty; S1/S2 show this lap's result; **S3 shows the lap just finished for 8 s** (`S3_SHOW`).
- Colours: **purple** (`#F24BFF`, bright, close to pink: the user asked for it brighter) = at or under the best single
  sector (+0.5 ms), **green** = faster than on the best lap, **red** = slower. The number is the difference to the best
  lap's sector.
- **No reference yet** (first lap of a session: SimHub clears the sector references at a session start but keeps
  `BestLapTime`): the plain sector time in light grey.
- **Live delta in the current sector**: `SessionBestLiveDeltaSeconds` minus what this lap's finished sectors gained or
  lost against the best lap; SLIPSTREAM darkens the block and shows it green/red/grey (within 5 ms), APEX shows it in
  the row. Needs a reference and the earlier sectors.
- **SimHub NCalc and missing values** (checked with `/api/eval` on live data): a missing property is null, functions and
  arithmetic pass null through without an error (`timespantoseconds(null)` = null, `null - x` = null, `TimeSpan = 1` =
  null). So `isnull(timespantoseconds([X]), -1) = -1` = "X is missing". Visible conditions treat null as false.
- **Tyre temperatures, LMU**: SimHub's `TyreTemperature*` is the surface average (~50 C when the game showed ~80).
  LMU's own dashes show `((inner1 + inner2 + inner3) / 3 + carcass) / 2 - 273.15` from
  `GameRawData.CurrentPlayerTelemetry.mWheels0N.mTireInnerLayerTemperature0K` and `.mTireCarcassTemperature` (Kelvin,
  wheels 01-04 = FL FR RL RR). The dashes use it when `[DataCorePlugin.CurrentGame] = 'LMU'`, SimHub's value otherwise;
  the SimGame feed does the same in code (`SimHubFeedMapper.LmuTyreTemperatures`). Bands: <70 blue, 70-80 cyan,
  80-100 green, 100-110 amber, >110 red (bands, not a blend: a blend repainted each block every degree).

## Open (where we are)

- **Sector results still take "a few seconds" to show on the wheel** (user, 2026-10-01, after the 0.3 s formula fix).
  Ruled out: SimHub's data (`SectorNTime` changes in the same sample as `CurrentSectorIndex`, logged at 5 Hz) and the
  formula rate (10/s, `FXProRpmSyncPlugin.DataUpdate`). Next: run `tools/dashes/watch_sectors.py OUT.txt` while driving
  (logs SimHub's sector data and the colour of each SLIPSTREAM block in the screen mirror, `/api/wheel/frame.png`, at
  4 Hz; it needs the wheel connected and a dash on screen) to see whether the delay is in the plugin's values, the
  renderer or the screen. Suspects: the renderer's order/budget when the live overlay hides and the block redraws, or
  `Visible` truthiness of a TimeSpan.

- Being verified in a real LMU session: tyre temperatures vs the game, sector timing after the 0.3 s formula fix, the
  first-lap sector times, the live sector delta.
- SimHub's `SessionBestLiveDeltaSeconds` read -85.3, then -12.1, right after the first lap of a new session (compared
  against the out lap?): the big delta and the live sector delta are wrong until a real reference exists. Look at
  LMU's raw `mDeltaBest`, or show the delta only once a valid best lap exists.
- Non-USB path: the SimGame feed has no sector delta to give the wheel's own dashes (no such field shown); only tyre
  temperatures were ported.
- The demo's lap delta and its simulated sector times aren't consistent (live sector numbers look large in the demo).
