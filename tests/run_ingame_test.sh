#!/usr/bin/env bash
# Scripted in-game test (singleplayer, host side).
#
# Builds the plugin and the test driver, assembles a minimal throwaway BepInEx profile in tests/.profile
# (BepInEx core + HookGenPatcher + RoR2BepInExPack + MMHOOK copied from SOURCE_PROFILE, plus this mod and
# the driver), launches the game through Steam with that profile, waits for the driver to finish and prints
# the results. Screenshots end up in tests/.profile/BepInEx/kltest/.
#
# The game window opens on your screen; don't click in it while the test runs (about 3-5 minutes).
# Note: the test plays two short runs on your real save (one ends in a Defeat).
#
# Usage: tests/run_ingame_test.sh [SOURCE_PROFILE]
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT=$(pwd)
SOURCE_PROFILE=${1:-"$HOME/.config/r2modmanPlus-local/RiskOfRain2/profiles/Hallowed Ready"}
SRC="$SOURCE_PROFILE/BepInEx"
DEST="$ROOT/tests/.profile/BepInEx"
TIMEOUT=${TIMEOUT:-900}

die() { echo "run_ingame_test.sh: $*" >&2; exit 1; }

[[ -d "$SRC/core" ]] || die "no BepInEx in '$SOURCE_PROFILE'"
pgrep -f '[R]isk of Rain 2.exe' >/dev/null && die "the game is already running; close it first"

dotnet build -c Release -p:ProfileDir="$SOURCE_PROFILE" >/dev/null
dotnet build tests/TestDriver -c Release -p:ProfileDir="$SOURCE_PROFILE" >/dev/null

rm -rf "$DEST"
mkdir -p "$DEST/config" "$DEST/patchers" "$DEST/plugins/ELEVATED-KarmaLocking" "$DEST/plugins/KarmaLockingTestDriver"
cp -r "$SRC/core" "$DEST/"
cp "$SRC/config/BepInEx.cfg" "$DEST/config/"
cp -r "$SRC/patchers/RiskofThunder-HookGenPatcher" "$DEST/patchers/"
cp -r "$SRC/plugins/RiskofThunder-RoR2BepInExPack" "$SRC/plugins/MMHOOK" "$DEST/plugins/"
# Optional: skip the intro to save time.
[[ -d "$SRC/plugins/ELEVATED-RemoveIntroCinematic" ]] && cp -r "$SRC/plugins/ELEVATED-RemoveIntroCinematic" "$DEST/plugins/"
cp bin/Release/KarmaLocking.dll icon.png "$DEST/plugins/ELEVATED-KarmaLocking/"
cp tests/TestDriver/bin/Release/KarmaLocking.TestDriver.dll "$DEST/plugins/KarmaLockingTestDriver/"

LOG="$DEST/LogOutput.log"
echo "Launching Risk of Rain 2 with the test profile..."
steam -applaunch 632360 --doorstop-enabled true --doorstop-target-assembly "Z:$DEST/core/BepInEx.Preloader.dll" >/dev/null 2>&1 &

# Wait for the game to start, then for the driver to finish (it quits the game) or the timeout.
for _ in $(seq 1 60); do pgrep -f '[R]isk of Rain 2.exe' >/dev/null && break; sleep 2; done
start=$SECONDS
until { [[ -f "$LOG" ]] && grep -q 'DONE passed' "$LOG"; } || ! pgrep -f '[R]isk of Rain 2.exe' >/dev/null; do
  (( SECONDS - start > TIMEOUT )) && die "timed out after ${TIMEOUT}s; see $LOG"
  sleep 3
done

echo
cat "$DEST/kltest/results.txt" 2>/dev/null || die "no results; see $LOG"
echo "Screenshots: $DEST/kltest/"
grep -q 'failed=0' "$DEST/kltest/results.txt"
