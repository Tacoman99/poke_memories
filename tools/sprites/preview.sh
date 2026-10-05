#!/usr/bin/env bash
# Renders preview/<clip>.png: every 6th cut frame composited on teal, in one strip.
set -e
cd "$(dirname "$0")"
FFMPEG=${FFMPEG:-ffmpeg}
mkdir -p preview
for clip in "$@"; do
  "$FFMPEG" -v error -f lavfi -i color=c=0x2a9d8f:s=720x1280:r=12 -framerate 12 -i "cut/$clip/%03d.png" \
    -filter_complex "[0][1]overlay=shortest=1,select='not(mod(n\,6))',scale=240:-1,tile=8x1" \
    -frames:v 1 "preview/$clip.png" -y
done
