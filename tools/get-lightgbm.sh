#!/usr/bin/env bash
# Downloads LightGBM's official native library from the LightGBM wheel on PyPI, verifies its SHA-256,
# and copies it to native/<platform>/. Linux x86_64 and macOS (arm64 / x86_64) are supported.
# macOS also needs OpenMP:  brew install libomp
# Usage (from the repo root):  bash tools/get-lightgbm.sh
set -euo pipefail
VERSION="${1:-4.5.0}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64)  PATTERN="manylinux_2_28_x86_64"; DEST="$ROOT/native/linux-x64"; LIB="lib_lightgbm.so" ;;
  Darwin-arm64)  PATTERN="macosx_.*_arm64";        DEST="$ROOT/native/osx";       LIB="lib_lightgbm.dylib" ;;
  Darwin-x86_64) PATTERN="macosx_.*_x86_64";       DEST="$ROOT/native/osx";       LIB="lib_lightgbm.dylib" ;;
  *) echo "Unsupported platform $(uname -s)-$(uname -m)"; exit 1 ;;
esac
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
python3 - "$VERSION" "$PATTERN" "$TMP" <<'PY'
import hashlib, json, re, sys, urllib.request
version, pattern, tmp = sys.argv[1:4]
meta = json.load(urllib.request.urlopen(f"https://pypi.org/pypi/lightgbm/{version}/json"))
wheel = next(u for u in meta["urls"] if re.search(pattern, u["filename"]))
print("Downloading", wheel["filename"])
data = urllib.request.urlopen(wheel["url"]).read()
digest = hashlib.sha256(data).hexdigest()
if digest != wheel["digests"]["sha256"]:
    sys.exit(f"SHA-256 mismatch: {digest}")
open(f"{tmp}/lightgbm.zip", "wb").write(data)
PY
mkdir -p "$TMP/x" "$DEST"
python3 -c "import zipfile,sys; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" "$TMP/lightgbm.zip" "$TMP/x"
cp "$(find "$TMP/x" -name "$LIB" | head -1)" "$DEST/"
echo "Installed $LIB (LightGBM $VERSION) -> $DEST"
echo "Now run:  dotnet build && dotnet run --project src/Occlet.Approach2"
