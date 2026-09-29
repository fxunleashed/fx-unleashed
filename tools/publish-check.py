#!/usr/bin/env python3
"""Before anything is published (NEXT.md O4 item 4): nothing secret or Simagic's in a folder, a zip, or a git history.

    python tools/publish-check.py <folder | file.zip | git repo> [...] [--history]

Looks for, in every file (and with --history, in every blob ever committed):
  - the firmware AES key and IV, as raw bytes and as hex text. They're read from the private decryptor on this PC
    (%USERPROFILE%\\Documents\\FXPro-firmware-backup\\SfuDecrypt\\Program.cs) and never printed;
  - the decryptor itself (SfuDecrypt), SimPro's firmware header (SIMPROSFU100);
  - Simagic firmware and screen images: .sfu / .tft / .hex / .bin files, and the stock wheel app by its sha256
    (encrypted and decrypted).
Exit code 1 if anything is found. Prints only where, never what.
"""
import hashlib
import io
import os
import re
import subprocess
import sys
import zipfile

PRIVATE = os.path.join(os.path.expanduser("~"), "Documents", "FXPro-firmware-backup", "SfuDecrypt", "Program.cs")
# FXPro_App 1.3.11: the .sfu and its decrypted image (firmware-rebuild.md)
STOCK_PREFIXES = ["16dd09cf2e76d6ee", "b9238ae0b596b443"]
BAD_EXT = (".sfu", ".tft", ".hex", ".bin")


def secrets():
    """The key and IV from the private decryptor, as byte patterns (raw, hex upper, hex lower)."""
    if not os.path.isfile(PRIVATE):
        print("note: private decryptor not found; key/IV not checked (run this on the machine that has it)")
        return []
    src = open(PRIVATE, encoding="utf-8").read()
    pats = []
    for m in re.finditer(r'FromHexString\("([0-9A-Fa-f ]+)"', src):
        h = m.group(1).replace(" ", "")
        if len(h) < 16:
            continue
        raw = bytes.fromhex(h)
        pats += [raw, h.upper().encode(), h.lower().encode()]
        # 128-bit use of the first half of a 256-bit key
        if len(raw) == 32:
            pats += [raw[:16], h[:32].upper().encode(), h[:32].lower().encode()]
    return pats


def check_blob(name, data, pats, found):
    low = name.lower()
    if low.endswith(BAD_EXT):
        found.append(f"{name}: firmware/screen image file type")
    sha = hashlib.sha256(data).hexdigest()
    if any(sha.startswith(p) for p in STOCK_PREFIXES):
        found.append(f"{name}: Simagic's stock wheel app")
    # a real .sfu starts with its header; mentions of the name in docs are fine
    if data[:12] == b"SIMPROSFU100":
        found.append(f"{name}: a SimPro firmware file (SIMPROSFU100 header)")
    # the decryptor itself: its name together with AES code (docs that name it are fine)
    code = low.endswith((".cs", ".py", ".ps1", ".exe", ".dll", ".js", ".ts")) and not low.endswith("publish-check.py")
    if code and b"SfuDecrypt" in data and any(k in data for k in (b"Aes.Create", b"CreateDecryptor", b"CreateEncryptor", b"AES_CTR", b"Rijndael")):
        found.append(f"{name}: looks like the firmware decryptor")
    for i, p in enumerate(pats):
        if p in data:
            found.append(f"{name}: contains the firmware key or IV (pattern {i})")
            break
    if low.endswith(".zip"):
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as z:
                for info in z.infolist():
                    if not info.is_dir():
                        check_blob(f"{name}!{info.filename}", z.read(info), pats, found)
        except zipfile.BadZipFile:
            pass


def walk(path, pats, found):
    if os.path.isfile(path):
        check_blob(path, open(path, "rb").read(), pats, found)
        return
    for root, dirs, files in os.walk(path):
        dirs[:] = [d for d in dirs if d not in (".git", "node_modules", "bin", "obj", ".vs", "dist", ".astro")]
        for f in files:
            p = os.path.join(root, f)
            try:
                check_blob(os.path.relpath(p, path), open(p, "rb").read(), pats, found)
            except OSError:
                pass


def history(repo, pats, found):
    """Every blob in every commit (all branches and tags)."""
    out = subprocess.run(["git", "-C", repo, "rev-list", "--objects", "--all"], capture_output=True, text=True, check=True).stdout
    seen = set()
    for line in out.splitlines():
        parts = line.split(" ", 1)
        if len(parts) < 2 or parts[0] in seen:
            continue
        seen.add(parts[0])
        kind = subprocess.run(["git", "-C", repo, "cat-file", "-t", parts[0]], capture_output=True, text=True).stdout.strip()
        if kind != "blob":
            continue
        data = subprocess.run(["git", "-C", repo, "cat-file", "blob", parts[0]], capture_output=True).stdout
        check_blob(f"history:{parts[1]}", data, pats, found)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        print(__doc__)
        sys.exit(2)
    pats = secrets()
    found = []
    for a in args:
        walk(a, pats, found)
        if "--history" in sys.argv and os.path.isdir(os.path.join(a, ".git")):
            history(a, pats, found)
    for f in sorted(set(found)):
        print("FOUND", f)
    print(f"{'FAIL' if found else 'ok'}: {len(set(found))} problem(s) in {', '.join(args)}")
    sys.exit(1 if found else 0)


if __name__ == "__main__":
    main()
