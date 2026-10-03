"""
Reads SimHub's log after a driving session and says which of the plugin's session-only features actually showed up, so each can
be ticked off in docs/test-checklist.md without scrolling through the log.

    python tools/session-report.py [--hours 6] [--logs "C:/Program Files (x86)/SimHub/Logs"] [--settings <GeneralSettings.json>]

What it looks for (all written by the plugin as "[FXProRpmSync] ..."):
  car <game | id>: <where its lights came from>; rev LEDs at ...      one line per car change
  spotter: car on the left yes/no, on the right yes/no               SimHub's spotter flags changing
  <game>: lap N flagged invalid before any lap was completed          the first-lap rule hiding an invalid flag
  USB write failed / wheel lost power / connected to the wheel         the wheel dropping off USB
plus warnings and errors from the plugin and ATSR-Hub, and what's been learned in the settings (pit speeds, launch targets).
"""
import argparse
import collections
import glob
import json
import os
import re
import sys
import time

DEFAULT_LOGS = r"C:\Program Files (x86)\SimHub\Logs"
DEFAULT_SETTINGS = r"C:\Program Files (x86)\SimHub\PluginsData\Common\FXProRpmSyncPlugin.GeneralSettings.json"
STAMP = re.compile(r"^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d),\d+\] (\w+) - (.*)$")


def read_lines(folder, hours):
    cutoff = time.time() - hours * 3600
    files = [f for f in glob.glob(os.path.join(folder, "SimHub*.txt")) if os.path.getmtime(f) >= cutoff]
    lines = []
    for f in sorted(files, key=os.path.getmtime):
        with open(f, encoding="utf-8", errors="replace") as fh:
            for raw in fh:
                m = STAMP.match(raw.rstrip("\n"))
                if m:
                    ts = time.mktime(time.strptime(m.group(1), "%Y-%m-%d %H:%M:%S"))
                    if ts >= cutoff:
                        lines.append((m.group(1), m.group(2), m.group(3)))
    return lines


def source_kind(text):
    t = text.lower()
    if "game data" in t:
        return "the game's own dash data (our library)"
    if "the game's own shift lights" in t:
        return "the game's own shift light numbers (iRacing)"
    if "car database" in t and "not in car database" not in t:
        return "Lovely Car Data"
    if "not in car database" in t:
        return "NOT FOUND: a generic pattern from the redline"
    return text.split(";")[0][:60]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--hours", type=float, default=6)
    ap.add_argument("--logs", default=DEFAULT_LOGS)
    ap.add_argument("--settings", default=DEFAULT_SETTINGS)
    a = ap.parse_args()

    lines = read_lines(a.logs, a.hours)
    mine = [(t, lvl, msg) for t, lvl, msg in lines if "[FXProRpmSync]" in msg and "UI " not in msg[:40]]
    print(f"{len(lines)} log lines in the last {a.hours:g} h, {len(mine)} from the plugin\n")

    cars = [(t, m) for t, _, m in mine if m.startswith("[FXProRpmSync] car ")]
    print(f"== Cars loaded ({len(cars)}) and where their lights came from")
    seen = collections.OrderedDict()
    for t, m in cars:
        body = m[len("[FXProRpmSync] car "):]
        key, _, rest = body.partition(": ")
        seen[key] = (t, rest)
    for key, (t, rest) in seen.items():
        flash = "blinks out" if "lights blink out" in rest else ("flash" if "; flash" in rest else "no flash")
        print(f"  {t[11:]}  {key:48} {source_kind(rest)}; {flash}")
    if not cars:
        print("  none: no car was applied (is USB mode on? was a game running? was this the new build?)")

    sp = [(t, m) for t, _, m in mine if "] spotter:" in m]
    print(f"\n== Spotter ({len(sp)} changes from SimHub)")
    left = sum(1 for _, m in sp if "left yes" in m)
    right = sum(1 for _, m in sp if "right yes" in m)
    print(f"  left on {left}x, right on {right}x" + ("" if sp else "  -> SimHub never reported a car alongside"))
    for t, m in sp[:6]:
        print("  ", t[11:], m.replace("[FXProRpmSync] ", ""))

    lap = [(t, m) for t, _, m in mine if "flagged invalid before any lap was completed" in m]
    print(f"\n== First lap flagged invalid by the game, hidden by the plugin ({len(lap)})")
    for t, m in lap:
        print("  ", t[11:], m.replace("[FXProRpmSync] ", ""))
    if not lap:
        print("  none: either the game didn't flag lap 1, or it wasn't a session start")

    usb = [(t, m) for t, _, m in mine if any(k in m for k in ("USB write failed", "lost power", "connected to the wheel", "released the wheel"))]
    fails = sum(1 for _, m in usb if "write failed" in m)
    print(f"\n== USB ({fails} write failures)")
    for t, m in usb[-8:]:
        print("  ", t[11:], m.replace("[FXProRpmSync] ", ""))

    bad = [(t, lvl, m) for t, lvl, m in lines if lvl in ("WARN", "ERROR") and ("FXProRpmSync" in m or "ATSR" in m) and "USB write failed" not in m]
    print(f"\n== Warnings and errors from the plugin / ATSR-Hub ({len(bad)})")
    counts = collections.Counter(m[:140] for _, _, m in bad)
    for m, n in counts.most_common(10):
        print(f"  {n:3}x {m}")

    try:
        u = json.load(open(a.settings, encoding="utf-8-sig"))["Usb"]
        print("\n== Learned in the settings")
        print("  pit speeds (km/h):", json.dumps(u.get("PitSpeeds") or {}))
        print("  launch targets:", json.dumps({k: v for k, v in (u.get("CarLaunch") or {}).items()}))
        print("  lights from:", u.get("LightsFrom"), "| spotter over other lights:", u.get("SpotterOverExternal"))
    except Exception as e:  # SimHub rewrites the file on exit; close SimHub first for the latest
        print("\n(settings not read:", e, ")")


if __name__ == "__main__":
    sys.exit(main())
