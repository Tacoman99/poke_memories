#!/bin/bash
# usage: shot.sh <name> [w h]  captures the Game view into unity/Assets/Temp/shots/<name>.png
cd /c/Users/jorge/poke_memories-bowl/unity
mkdir -p Temp/shots
if [ -n "$2" ]; then unity command capture_game_view --save_path "Temp/shots/$1.png" --width $2 --height $3 >/dev/null 2>&1
else unity command capture_game_view --save_path "Temp/shots/$1.png" >/dev/null 2>&1; fi
