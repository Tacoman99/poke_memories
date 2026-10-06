#!/bin/bash
# usage: shot.sh <name>  captures the Game view into unity/Assets/Temp/shots/<name>.png
cd /c/Users/jorge/poke_memories-bowl/unity
mkdir -p Temp/shots
unity command capture_game_view --save_path "Temp/shots/$1.png" >/dev/null 2>&1
