#!/bin/bash
# usage: shot.sh <dir> <name>   (captures game view into shots/<dir>/<name>.png)
cd /c/Users/jorge/poke_memories-wp4/unity
mkdir -p Assets/Temp/shots ../shots/$1
unity command capture_game_view --save_path "Temp/shots/$2.png" >/dev/null 2>&1
cp Assets/Temp/shots/$2.png ../shots/$1/$2.png
