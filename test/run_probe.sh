#!/usr/bin/env bash
# In-game test of POG Player Limit: launches the game through Steam with the Gale profile, lets
# PlayerLimitProbe drive it (see PlayerLimitProbe/Probe.cs), screenshots the window after every
# "[PROBE] <n>" line, closes the game, removes the probe and prints the log lines.
#
#   test/run_probe.sh <out dir>
#
# Before it runs: build both projects (Release), make sure the user agreed to the launch (the game
# takes the screen for 2-3 minutes). It refuses to start if the game is already running.
# It saves MaxPlayers = 10 in the preferences first, so step 0 checks the value applied at game
# start; the probe restores the default at the end and the script checks that it did.
set -u
PROFILE="${POG_PROFILE:-$HOME/.local/share/com.kesomannen.gale/pit-of-goblin/profiles/Default}"
OUT="$1"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(dirname "$HERE")"
LOG="$PROFILE/MelonLoader/Latest.log"
CFG="$PROFILE/UserData/MelonPreferences.cfg"
MOD_DLL="$REPO/bin/Release/net6.0/POGPlayerLimit.dll"
PROBE_DLL="$HERE/PlayerLimitProbe/bin/Release/net6.0/PlayerLimitProbe.dll"
STARTUP_VALUE="10.0"

# 'Pit of Goblin.e' is the 15-character process name; a -f pattern also matches shells that
# merely mention the exe.
if pgrep -x 'Pit of Goblin.e' >/dev/null; then echo "game already running (the user may be playing): abort"; exit 1; fi
for f in "$MOD_DLL" "$PROBE_DLL"; do [ -f "$f" ] || { echo "missing build: $f"; exit 1; }; done
mkdir -p "$OUT"
cp "$CFG" "$OUT/MelonPreferences.cfg.before"

# The value the game should pick up at start (game closed, so nothing rewrites the file meanwhile).
python3 -I - "$CFG" "$STARTUP_VALUE" <<'EOF'
import re, sys
path, value = sys.argv[1], sys.argv[2]
text = open(path, encoding="utf-8").read()
section = "[POG_Player_Limit]"
if section in text:
    text = re.sub(r"(\[POG_Player_Limit\][^\[]*?MaxPlayers\s*=\s*)[^\n]*", r"\g<1>" + value, text)
else:
    text = text.rstrip("\n") + f"\n\n{section}\nMaxPlayers = {value}\n"
open(path, "w", encoding="utf-8").write(text)
EOF
grep -A1 -F "[POG_Player_Limit]" "$CFG"

# Copy to a temporary name, then rename: never overwrite a DLL in place.
cp "$MOD_DLL" "$PROFILE/Mods/.pl.tmp" && mv -f "$PROFILE/Mods/.pl.tmp" "$PROFILE/Mods/POGPlayerLimit.dll"
cp "$PROBE_DLL" "$PROFILE/Mods/PlayerLimitProbe.dll"
sha256sum "$MOD_DLL" "$PROFILE/Mods/POGPlayerLimit.dll"

T0=$(date +%s)
(setsid steam -applaunch 3253530 --melonloader.basedir "Z:$PROFILE" >/dev/null 2>&1 &)
# The previous run's log holds the same marker: require a log newer than the launch.
for _ in $(seq 1 120); do
  if [ -f "$LOG" ] && [ "$(stat -c %Y "$LOG")" -gt "$T0" ] && grep -q "MODS button injected" "$LOG"; then break; fi
  sleep 2
done
WIN=$(xdotool search --name "^Pit of Goblin$" | tail -1)
sleep 2; import -silent -window "$WIN" "$OUT/start.png"

seen=""
for _ in $(seq 1 600); do
  for n in $(grep -a -o "\[PROBE\] [0-9]" "$LOG" | awk '{print $2}'); do
    case " $seen " in *" $n "*) ;; *) seen="$seen $n"; sleep 2; import -silent -window "$WIN" "$OUT/step_$n.png";; esac
  done
  grep -q "\[PROBE\] done" "$LOG" && break
  sleep 0.5
done
sleep 2   # POG Config saves preferences 0.5 s after the last change

GP=$(pgrep -x 'Pit of Goblin.e'); [ -n "$GP" ] && kill $GP
sleep 6
pgrep -x 'Pit of Goblin.e' >/dev/null && echo "game still running" || echo "game closed"
rm -f "$PROFILE/Mods/PlayerLimitProbe.dll"
cp "$LOG" "$OUT/Latest.log"
echo "--- log"
grep -a -E "PROBE|POG Player Limit|POGPlayerLimit|ERROR|Exception" "$LOG" | cut -c1-260
echo "--- saved preference after the run"
grep -A1 -F "[POG_Player_Limit]" "$CFG"
