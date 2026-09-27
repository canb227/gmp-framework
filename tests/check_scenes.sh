#!/usr/bin/env bash
# Loads every scene/resource headlessly; fails on load errors or missing dependencies.
# macOS/Linux counterpart of check_scenes.ps1.
# Usage: tests/check_scenes.sh [path/to/godot]   (or set GODOT)
set -uo pipefail

GODOT="${1:-${GODOT:-$HOME/godot/4.7.2/Godot_mono.app/Contents/MacOS/Godot}}"
PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
LOG_DIR="$PROJECT_DIR/tests/logs"
LOG="$LOG_DIR/check_scenes.log"
IMPORT_LOG="$LOG_DIR/check_scenes_import.log"
mkdir -p "$LOG_DIR"

if [[ ! -x "$GODOT" ]]; then
    echo "Godot not found at: $GODOT (pass a path or set GODOT)" >&2
    exit 2
fi

# Without Blender configured, headless --import aborts the whole reimport batch
# on .blend files. Nothing references them directly, so move them aside while
# importing and always put them back.
STASH="$(mktemp -d)"
MANIFEST="$STASH/manifest"
: > "$MANIFEST"
restore_blends() {
    while IFS='|' read -r stashed original; do
        mv "$stashed" "$original"
    done < "$MANIFEST"
    rm -rf "$STASH"
}
trap restore_blends EXIT
trap 'exit 130' INT TERM

i=0
while IFS= read -r f; do
    i=$((i + 1))
    mv "$f" "$STASH/$i.blend" && echo "$STASH/$i.blend|$f" >> "$MANIFEST"
done < <(find "$PROJECT_DIR" -name "*.blend" -not -path "$PROJECT_DIR/.godot/*")

"$GODOT" --headless --path "$PROJECT_DIR" --import > "$IMPORT_LOG" 2>&1
restore_blends
trap - EXIT

"$GODOT" --headless --path "$PROJECT_DIR" --script res://tests/check_scenes.gd > "$LOG" 2>&1

# Box3D has no macOS build and Steamworks' native lib won't load on arm64; both are expected here.
BAD="$(grep -iE "CHECK_SCENES_FAIL|Cannot open file|Failed loading resource|Unable to load|missing|Parse Error" "$LOG" \
    | grep -viE "box3d|Steamworks")"

grep -E "^CHECK_SCENES " "$LOG"
if [[ -n "$BAD" ]]; then
    echo "$BAD" | head -n 30 | sed $'s/^/\e[31m/;s/$/\e[0m/'
fi
if grep -q "CHECK_SCENES_RESULT:PASS" "$LOG" && [[ -z "$BAD" ]]; then
    printf '\e[32mALL SCENES LOADED\e[0m\n'
    exit 0
fi
printf '\e[31mSCENE CHECK FAILED - see tests/logs/check_scenes.log\e[0m\n'
exit 1
