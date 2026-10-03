"""Builds assets/Simagic_FX-Pro.atsrdevice: an ATSR-Hub EVO wheel layout for the FX Pro in USB mode.

ATSR-Hub publishes each wheel's LED colours per LED index of its layout (docs/usb-mode.md, "ATSR-Hub"). This layout
numbers the LEDs the way the FX Pro's firmware does, so USB mode can pass them straight through:
buttons 0-5 left / 6-11 right (top to bottom), encoders 12-16, side lights 17-19 left / 20-22 right, rev lights 23-37.

Made from ATSR-Hub's own GSI FPE-V2 preset (6 buttons a side, 5 encoders, 3+3 side lights, 14 rev LEDs), which gives
the element positions ATSR-Hub draws; only the LED indexes, counts, USB id and a 15th rev LED are changed.

Usage: python make_fxpro_preset.py [template.atsrdevice] [out.atsrdevice]
  template default: <Documents>/SimHub/ATSR/device-presets/device-presets/steering-wheel-presets/GSI_FPE-V2.atsrdevice
Then check it with tools/atsr/AtsrCheck and copy it into that steering-wheel-presets folder: ATSR-Hub lists it as
"Simagic FX-Pro" when adding a wheel.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def documents():
    """The real Documents folder (follows OneDrive redirection, like .NET's SpecialFolder.Personal)."""
    try:
        import ctypes
        buf = ctypes.create_unicode_buffer(260)
        ctypes.windll.shell32.SHGetFolderPathW(None, 5, None, 0, buf)  # CSIDL_PERSONAL
        return buf.value
    except Exception:
        return os.path.join(os.path.expanduser('~'), 'Documents')


def main():
    template = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        documents(), 'SimHub', 'ATSR', 'device-presets', 'device-presets', 'steering-wheel-presets', 'GSI_FPE-V2.atsrdevice')
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, '..', '..', 'assets', 'Simagic_FX-Pro.atsrdevice')
    j = json.load(open(template, encoding='utf-8-sig'))
    s = j['deviceSettings']
    ei = s['ElementInformation']
    # Buttons: ATSR-Hub slots 0-6 are the left side, 7-13 the right (its maxButtons = 7).
    for i in range(6):
        for slot, idx in ((i, i), (7 + i, 6 + i)):
            e = ei['Button'][slot]
            e['Index'], e['LEDCount'], e['InputIDs'] = idx, 1, [-1, -1, -1]
    for k in range(5):
        e = ei['EncoderSingle'][k]
        e['Index'], e['LEDCount'], e['InputIDs'] = 12 + k, 1, [-1, -1, -1]
    for k in range(3):
        ei['Telemetry'][k]['Index'] = 17 + k
        ei['Telemetry2'][k]['Index'] = 20 + k
    rpm = ei['RPM']
    rpm[0]['Index'], rpm[0]['LEDCount'] = 23, 15  # one element: first LED + count
    a, b = rpm[12]['Coordinates'], rpm[13]['Coordinates']
    rpm[14]['Coordinates'] = [2 * b[0] - a[0], 2 * b[1] - a[1]]  # where ATSR-Hub draws the 15th rev LED
    s['RPMCount'] = 15
    s['VID'], s['PID'] = '0483', '0529'
    j['presetName'] = 'Simagic FX Pro (FX Unleashed)'

    covered = sorted(i for k in ei for e in ei[k] if e['IsElement'] for i in range(e['Index'], e['Index'] + e['LEDCount']))
    assert covered == list(range(38)), covered
    json.dump(j, open(out, 'w', encoding='utf-8'), indent=2)
    print('wrote', os.path.abspath(out), '- 38 LEDs, each used once')


if __name__ == '__main__':
    main()
