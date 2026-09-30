# Lights that know what the car is doing: plan

Written 2026-09-29. Status: **proposal, nothing built.** Both wheels (FX Pro and GT Neo).

## What ATSR-Hub does (studied from its GT-Neo profile, V3.5.0)

One big SimHub LED profile, a tree of conditional groups:

- **Game running, ignition on, engine on:** the theme on buttons and rings; the rev bar a dim theme-coloured background
  with the car's shift lights on top (standard colours; themes never change them); then effects: pit limiter
  (running / alternating / blinking / breathing over all 73 LEDs), button functions (limiter, indicators, flash,
  wipers, headlights, DRS, push-to-pass), redline flash, TC/ABS, spotter, flags (white and chequered for 5 s), ring
  displays (engine map, fuel, battery, throttle/brake, brake bias, diff, tyres), button press effects.
- **Game running, engine off:** nearly dark; the ignition and starter buttons light up.
- **Engine start:** an optional start-up sweep ("running lights" or "loading lights"). **Ignition off:** an optional
  shutdown sequence.
- **No game:** an optional idle animation (breathing over the whole wheel, running animation, Knight Rider) over the
  theme; a changed theme previews itself for 3 s.

Where it's weak:
- **Every theme is copied into every state**: the 16 themes appear again under the background, the rev background, the
  idle background and the theme preview. Changing one theme means editing it in four places, and a new state means
  copying all 16 again.
- **Priority is wherever a group sits in the tree.** Nothing says "flags beat the spotter"; reordering means moving
  groups.
- **State checks are repeated in each branch**, with game-specific quirks (iRacing `IsOnTrack`, AC ...) inline.
- The pit limiter replaces everything, including flags.

## Proposed structure

Three separate things, each defined once.

### 1. Car state: one detector, not per preset

`Usb/LightStates.cs`: a small state machine fed by `DashValues` every frame, the only place with game quirks.

| State | When | Default look |
|---|---|---|
| **Idle** | no game running (or SimHub has no data) | the theme, with the preset's idle animation |
| **Menu** | game running but in a menu, paused, replay or spectating | the theme, dimmed |
| **Engine off** | on track, ignition off or engine not started | dark, except a "ready" glow on the buttons |
| **Starting** | engine just started (1.5 s) | start-up sweep, then Driving |
| **Driving** | engine running | the theme + shift lights + everything live |
| **Pit limiter** | limiter on (in or out of the pit lane) | the preset's limiter style over the theme |
| **Stopping** | engine switched off after driving (1.5 s) | shutdown sweep, then Engine off |

Signals (all in SimHub's `StatusDataBase` / `GameData`): `GameRunning`, `GameInMenu`, `GamePaused`, `GameReplay`,
`Spectating`, `EngineIgnitionOn`, `EngineStarted`, `IsInPitLane`, `PitLimiterOn`. Games that don't report ignition
count as "engine on" while the car moves or the revs are above idle, so nothing ever sticks dark.

### 2. The theme: what a preset is

What `LightProfile` already has: per-group effect, colours, speed, brightness. Defined once; every state reuses it.

### 3. State looks: small changes to the theme, not copies

Each preset has a short "look" per state, with defaults, so a preset only says what's different:

- **brightness** (e.g. Menu 50%, Engine off 15%),
- **effect override** for the whole wheel or a group (Idle: breathing / flowing wave / scanner / same as driving;
  Engine off: off / glow),
- **rev bar**: dark, or a dim theme tint behind the shift lights (Driving), or an idle sweep (Idle).

### 4. Layers: one explicit order

Every frame is built bottom-up; each layer only touches the LEDs it uses:

1. **Theme** with the state's look.
2. **Rev bar:** theme tint (optional, dim) under the car's shift lights, always in the standard colours.
3. **Ring / encoder info** (optional): a value per encoder (FX Pro Levels today; on the GT Neo a 12-segment gauge
   per ring: fuel, brake bias, engine map, TC, ABS...).
4. **Alerts:** the existing ordered list (the user's arrows set priority), now including pit limiter styles.
5. **Takeovers:** start-up / shutdown sweeps and the 3 s preset preview. They're short and can't hide flags.
6. **Button press lights.**
7. **Brightness limit and night mode** (last, over everything).

### Why this is better than ATSR's tree

- A new state or a new theme is one entry, not 16 copies.
- Priority is a visible list the user can reorder, not tree position.
- Flags and the spotter still show during the pit limiter and transitions (takeovers sit below alerts).
- Game quirks live in one tested detector, with a fallback so no game leaves the wheel dark.
- It fits what we have: `LightProfile` keeps its fields (old profiles load unchanged, with default looks that match
  today: idle = the theme, driving = as now), and `LightEngine.Render` gains a state argument.

## Build order

1. **State detector + tests** (fake `DashValues` sequences: game start, engine start, limiter, pause, stall, exit).
2. **State looks in the engine** with defaults matching today; golden frames unchanged for Driving.
3. **Transitions:** start-up and shutdown sweeps (a few styles), the 3 s preview when the preset changes.
4. **Rev tint** (per preset, off by default) and **pit limiter styles** as an alert style.
5. **Editor:** a "When" strip on the Lights tab (Idle · Menu · Engine off · Driving · Pit limiter) to preview and
   tweak each look; the preview can play each state.
6. **Ring gauges on the GT Neo** (the rings as 12-segment gauges), after the rest.
7. Presets: give each built-in its idle animation and looks (e.g. Aurora breathes when idle, Stealth goes dark with
   the engine off).
