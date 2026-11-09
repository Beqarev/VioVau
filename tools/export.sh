#!/usr/bin/env bash
# Build Violet Vaults for Windows, macOS and Linux, VERIFY each build, and zip them for friends.
#
#   tools/export.sh            (from the repo root)
#   GODOT=/path/to/Godot tools/export.sh
#
# Needs Godot 4.7.x .NET and its export templates: either installed via the editor
# (Editor → Manage Export Templates) or unpacked into build/godot-templates/.
# Output: build/VioletVaults-{windows,macos,linux}.zip (each with a PLAYING.txt for friends).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
G="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
OUT="$ROOT/build"
TEMPLATES="$OUT/godot-templates"
PRESETS="$ROOT/game/export_presets.cfg"
README="$ROOT/tools/friends-readme.txt"

fail() { echo "❌ $*" >&2; exit 1; }
step() { echo; echo "▶ $*"; }

[ -x "$G" ] || fail "Godot not found at $G (set GODOT=...)"

# ── 1. Presets ────────────────────────────────────────────────────────────────
if [ ! -f "$PRESETS" ]; then
  step "Creating game/export_presets.cfg from the example"
  cp "$ROOT/game/export_presets.cfg.example" "$PRESETS"
  if [ -d "$TEMPLATES" ]; then
    # Point each preset at the locally unpacked templates (order in the file: Windows, macOS, Linux).
    python3 - "$PRESETS" "$TEMPLATES" <<'PY'
import sys
path, t = sys.argv[1], sys.argv[2]
pairs = [(f"{t}/windows_debug_x86_64.exe", f"{t}/windows_release_x86_64.exe"),
         (f"{t}/macos.zip", f"{t}/macos.zip"),
         (f"{t}/linux_debug.x86_64", f"{t}/linux_release.x86_64")]
lines, i = open(path).read().split("\n"), 0
for n, line in enumerate(lines):
    if line.startswith("custom_template/debug=") and i < len(pairs):
        lines[n] = f'custom_template/debug="{pairs[i][0]}"'
    elif line.startswith("custom_template/release=") and i < len(pairs):
        lines[n] = f'custom_template/release="{pairs[i][1]}"'; i += 1
open(path, "w").write("\n".join(lines))
PY
  fi
fi

# ── 2. Compile and import ─────────────────────────────────────────────────────
step "Building C# and importing assets"
(cd "$ROOT" && dotnet build VioVau.sln -c Release -v quiet -nologo) >/dev/null
"$G" --headless --path "$ROOT/game" --import >/dev/null 2>&1 || true

# ── 3. Export ─────────────────────────────────────────────────────────────────
rm -rf "$OUT/VioletVaults-windows" "$OUT/VioletVaults-linux" "$OUT"/VioletVaults-*.zip "$OUT/VioletVaults-macos"
mkdir -p "$OUT/VioletVaults-windows" "$OUT/VioletVaults-linux"

export_one() {
  local preset="$1" target="$2"
  step "Exporting $preset"
  "$G" --headless --path "$ROOT/game" --export-release "$preset" "$target" >"$OUT/export-$(echo "$preset" | tr ' ' '_').log" 2>&1 \
    || fail "$preset export failed — see $OUT/export-$(echo "$preset" | tr ' ' '_').log"
}

export_one "Windows Desktop" "$OUT/VioletVaults-windows/VioletVaults.exe"
export_one "Linux" "$OUT/VioletVaults-linux/VioletVaults.x86_64"
export_one "macOS" "$OUT/VioletVaults-macos.zip"

# ── 4. Verify (a build without the game's code or data is useless) ────────────
step "Verifying builds"
size_of() { stat -f%z "$1" 2>/dev/null || stat -c%s "$1"; }

check_dir_build() {
  local dir="$1" platform="$2" binary="$3"
  [ -f "$dir/$binary" ] || fail "$platform: $binary missing"
  ls "$dir"/data_VioVau_*/VioVau.dll >/dev/null 2>&1 || fail "$platform: game code (data_VioVau_*/VioVau.dll) missing"
  local bytes; bytes=$(size_of "$dir/$binary")
  [ "$bytes" -gt 60000000 ] || fail "$platform: $binary is only $bytes bytes (data pack not embedded?)"
  echo "  ✅ $platform: $binary $(du -h "$dir/$binary" | cut -f1), game code present"
}
check_dir_build "$OUT/VioletVaults-windows" "Windows" "VioletVaults.exe"
check_dir_build "$OUT/VioletVaults-linux" "Linux" "VioletVaults.x86_64"

listing=$(unzip -l "$OUT/VioletVaults-macos.zip")
echo "$listing" | grep -q "Contents/Resources/data_VioVau_macos_arm64/VioVau.dll" || fail "macOS: game code missing for Apple Silicon"
echo "$listing" | grep -q "Contents/Resources/data_VioVau_macos_x86_64/VioVau.dll" || fail "macOS: game code missing for Intel"
pck=$(echo "$listing" | awk '/\.pck$/ {print $1}' | head -1)
[ -n "$pck" ] && [ "$pck" -gt 1000000 ] || fail "macOS: data pack is ${pck:-missing} bytes (assets not exported?)"
echo "  ✅ macOS: data pack $((pck / 1024)) KB, game code for arm64 + x86_64"

# ── 5. Zip for friends ────────────────────────────────────────────────────────
step "Zipping"
cp "$README" "$OUT/VioletVaults-windows/PLAYING.txt"
cp "$README" "$OUT/VioletVaults-linux/PLAYING.txt"
(cd "$OUT" && zip -qr VioletVaults-windows.zip VioletVaults-windows && zip -qr VioletVaults-linux.zip VioletVaults-linux)
(cd "$OUT" && cp "$README" PLAYING.txt && zip -q VioletVaults-macos.zip PLAYING.txt && rm PLAYING.txt)

echo
echo "Done:"
ls -lh "$OUT"/VioletVaults-*.zip | awk '{print "  " $5 "  " $9}'
