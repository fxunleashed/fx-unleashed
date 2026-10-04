"""Refreshes the dashes that come inside the plugin (Usb/Bundled/, embedded in the DLL as resources).

    python tools/bundle-dashes.py <path to the fx-unleashed-library working copy>

Every dash of the library (dashes/<id>/dash.json + meta.json) is copied as Usb/Bundled/<id>.json, with the name, author,
description, source and version of its meta.json (the credit the plugin shows).
- Their plugin id is "lib-<id>", the one a library install gives it, so the library shows them as installed and a newer
  version installed from the library takes their place.
- KEEP_OWN_ID: the two LMU conversions were on the maintainer's PC (and in saved settings) before the library had them, so
  they keep their own id; the library still shows them as installed (InLibrary).
- Usb/Bundled/index.json lists them all (name, author, version, source, sha256 of the file). Nothing else is generated.
"""
import hashlib
import json
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Usb", "Bundled")

KEEP_OWN_ID = {"lmgt3-mclaren-720s", "toyota-gr010-hybrid"}
# Library items whose dash is built into the plugin's code (BuiltInDashes), so there is nothing to bundle
IN_CODE = {"lmgt3-mustang"}


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def main(library):
    os.makedirs(OUT, exist_ok=True)
    entries = []
    folder = os.path.join(library, "dashes")
    for item in sorted(os.listdir(folder)):
        meta = json.load(open(os.path.join(folder, item, "meta.json"), encoding="utf-8"))
        if meta.get("Kind", "dash") != "dash" or item in IN_CODE:
            continue
        target = os.path.join(OUT, item + ".json")
        shutil.copyfile(os.path.join(folder, item, "dash.json"), target)
        entries.append({"Id": item, "Library": item not in KEEP_OWN_ID, "InLibrary": True, "Name": meta["Name"], "Author": meta.get("Author"), "Description": meta.get("Description"),
                        "Version": meta.get("Version", "1.0.0"), "Source": meta.get("Source"), "License": meta.get("License"), "Sha256": sha(target)})
    with open(os.path.join(OUT, "index.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(entries, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print("bundled", len(entries), "dashes:", ", ".join(e["Id"] for e in entries))


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    main(sys.argv[1])
