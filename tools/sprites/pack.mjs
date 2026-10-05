// Aligns and scales cut frames into same-size sprite frames for Unity.
// Usage: node pack.mjs   (reads cut/<clip>/, writes ../../unity/Assets/Art/Skater/<clip>/)
//
// Every output frame is CANVAS x CANVAS with the wheel-contact point at
// (CANVAS/2, CANVAS - CONTACT_MARGIN), so the sprite pivot is the same for every
// clip and feet stay on the simulation's foot position.
import fs from 'node:fs';
import path from 'node:path';
import { PNG } from 'pngjs';

const CANVAS = 512;
const CONTACT_MARGIN = 12;
const TARGET_STANDING = 470; // her full standing height in output pixels

// Frame ranges are 1-based, inclusive, at 12fps. Grind skips the slide-in onto the rail.
// standing = her estimated standing height in source pixels. Each video is framed at a
// different zoom; these were matched by comparing head and skate sizes between clips.
const CLIPS = {
  idle: { from: 1, to: 49, standing: 1127 },
  push: { from: 1, to: 61, standing: 1100 },
  jump: { from: 1, to: 49, standing: 1000 },
  grind: { from: 22, to: 48, standing: 980 },
};

const OUT_ROOT = path.resolve('../../unity/Assets/Art/Skater');

// Horizontal anchor: mean x of opaque pixels in the hip band, which moves least between poses.
function hipX(png, contactY, bodyHeight) {
  const { width: w, data: d } = png;
  const top = Math.max(0, Math.round(contactY - bodyHeight * 0.55));
  const bottom = Math.round(contactY - bodyHeight * 0.4);
  let sum = 0, n = 0;
  for (let y = top; y < bottom; y++) for (let x = 0; x < w; x++) if (d[(y * w + x) * 4 + 3] > 128) { sum += x; n++; }
  return n ? sum / n : w / 2;
}

// Bilinear resample of premultiplied colour so transparent edges don't darken.
function sampleInto(src, dst, scale, offX, offY) {
  const { width: sw, height: sh, data: s } = src;
  const d = dst.data;
  for (let y = 0; y < CANVAS; y++) {
    for (let x = 0; x < CANVAS; x++) {
      const fx = (x - offX) / scale, fy = (y - offY) / scale;
      const x0 = Math.floor(fx), y0 = Math.floor(fy);
      if (x0 < 0 || y0 < 0 || x0 + 1 >= sw || y0 + 1 >= sh) continue;
      const tx = fx - x0, ty = fy - y0;
      let r = 0, g = 0, b = 0, a = 0;
      for (const [px, py, wgt] of [[x0, y0, (1 - tx) * (1 - ty)], [x0 + 1, y0, tx * (1 - ty)], [x0, y0 + 1, (1 - tx) * ty], [x0 + 1, y0 + 1, tx * ty]]) {
        const i = (py * sw + px) * 4, al = s[i + 3] / 255 * wgt;
        r += s[i] * al; g += s[i + 1] * al; b += s[i + 2] * al; a += al;
      }
      const o = (y * CANVAS + x) * 4;
      if (a > 0) { d[o] = r / a; d[o + 1] = g / a; d[o + 2] = b / a; }
      d[o + 3] = Math.round(a * 255);
    }
  }
}

const manifest = { canvas: CANVAS, pivot: { x: 0.5, y: CONTACT_MARGIN / CANVAS }, fps: 12, clips: {} };

for (const [clip, { from, to, standing }] of Object.entries(CLIPS)) {
  const stats = JSON.parse(fs.readFileSync(path.join('cut', clip, 'stats.json'), 'utf8'));
  const frames = stats.slice(from - 1, to);
  const pngs = frames.map((s) => PNG.sync.read(fs.readFileSync(path.join('cut', clip, s.file))));

  // One scale per clip so her size doesn't pulse between frames.
  const scale = TARGET_STANDING / standing;

  const outDir = path.join(OUT_ROOT, clip);
  fs.rmSync(outDir, { recursive: true, force: true });
  fs.mkdirSync(outDir, { recursive: true });
  let clipped = 0;
  pngs.forEach((png, k) => {
    const { contactY } = frames[k];
    const offX = CANVAS / 2 - hipX(png, contactY, standing) * scale;
    const offY = CANVAS - CONTACT_MARGIN - contactY * scale;
    if (offY + frames[k].minY * scale < 0) clipped++;
    const out = new PNG({ width: CANVAS, height: CANVAS });
    sampleInto(png, out, scale, offX, offY);
    fs.writeFileSync(path.join(outDir, `${clip}_${String(k).padStart(3, '0')}.png`), PNG.sync.write(out));
  });
  manifest.clips[clip] = { frames: pngs.length, sourceFrames: [from, to], standingPx: standing, scale: +scale.toFixed(4) };
  console.log(`${clip}: ${pngs.length} frames, standing ${standing}px -> scale ${scale.toFixed(3)}${clipped ? `, ${clipped} frames clipped at top` : ''}`);
}

fs.writeFileSync(path.join(OUT_ROOT, 'skater_frames.json'), JSON.stringify(manifest, null, 2));
