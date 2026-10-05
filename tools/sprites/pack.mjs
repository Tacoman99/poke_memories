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

// loop: after packing, the frame range is trimmed so the last frame flows into the first
// (smallest pixel difference between frame i and the frame after j, keeping at least min frames).
const LOOPS = { push: { min: 12 }, grind: { min: 8 } };

// Derived clips are built from already-packed frames by transforming them about the wheel-contact
// pivot, so no extra video is needed. Replace with real footage by adding a CLIPS entry later.
// frames(t) -> { src: [clip, frameIndex], rot (deg, + = clockwise), sx, sy, dx, dy } for t in 0..1.
const DERIVED = {
  grab: { frames: 6, make: (t) => ({ src: ['jump', 36 + Math.min(4, Math.round(t * 4))], rot: Math.sin(t * Math.PI) * 6, sx: 1, sy: 1 - Math.sin(t * Math.PI) * 0.12 }) },
  kick: { frames: 6, make: (t) => ({ src: ['jump', 40], rot: -Math.sin(t * Math.PI) * 26, sx: 1, sy: 1, dy: -Math.sin(t * Math.PI) * 14 }) },
  spin: { frames: 12, make: (t) => ({ src: ['jump', 40], rot: 0, sx: Math.cos(t * Math.PI * 2), sy: 1 }) },
  land: { frames: 6, make: (t) => ({ src: ['jump', 48], rot: 0, sx: 1 + Math.sin(t * Math.PI) * 0.06, sy: 1 - Math.sin(t * Math.PI) * 0.16 }) },
  bail: { frames: 10, make: (t) => ({ src: ['jump', 44], rot: -Math.pow(t, 1.6) * 85, sx: 1, sy: 1 - t * 0.12, dx: -t * 40 }) },
};
const PACKED = {};

function writePng(dir, clip, k, png) {
  fs.writeFileSync(path.join(dir, `${clip}_${String(k).padStart(3, '0')}.png`), PNG.sync.write(png));
}

function frameDiff(a, b) {
  let sum = 0;
  for (let i = 0; i < a.data.length; i += 16) sum += Math.abs(a.data[i + 3] - b.data[i + 3]) + (a.data[i + 3] > 0 && b.data[i + 3] > 0 ? (Math.abs(a.data[i] - b.data[i]) + Math.abs(a.data[i + 1] - b.data[i + 1]) + Math.abs(a.data[i + 2] - b.data[i + 2])) / 3 : 0);
  return sum;
}

function bestLoop(frames, min) {
  let best = [0, frames.length - 1], bestScore = Infinity;
  for (let i = 0; i < frames.length; i++)
    for (let j = i + min - 1; j < frames.length - 1; j++) {
      // closing seam, discounting longer loops slightly
      const score = frameDiff(frames[j + 1], frames[i]) / (1 + 0.01 * (j - i));
      if (score < bestScore) { bestScore = score; best = [i, j]; }
    }
  return best;
}

// Affine copy about the wheel-contact pivot (CANVAS/2, CANVAS-CONTACT_MARGIN), bilinear, premultiplied.
function transformFrame(src, { rot = 0, sx = 1, sy = 1, dx = 0, dy = 0 }) {
  const out = new PNG({ width: CANVAS, height: CANVAS });
  const px = CANVAS / 2, py = CANVAS - CONTACT_MARGIN;
  const r = (rot * Math.PI) / 180, cos = Math.cos(r), sin = Math.sin(r);
  const flip = sx < 0 ? -1 : 1, ax = Math.max(Math.abs(sx), 0.04);
  for (let y = 0; y < CANVAS; y++)
    for (let x = 0; x < CANVAS; x++) {
      // invert translate, rotate, then scale
      const ux = x - px - dx, uy = y - py - dy;
      const rx = ux * cos + uy * sin, ry = -ux * sin + uy * cos;
      const fx = (rx / (ax * flip)) + px, fy = ry / sy + py;
      const x0 = Math.floor(fx), y0 = Math.floor(fy);
      if (x0 < 0 || y0 < 0 || x0 + 1 >= CANVAS || y0 + 1 >= CANVAS) continue;
      const tx = fx - x0, ty = fy - y0;
      let R = 0, G = 0, B = 0, A = 0;
      for (const [qx, qy, w] of [[x0, y0, (1 - tx) * (1 - ty)], [x0 + 1, y0, tx * (1 - ty)], [x0, y0 + 1, (1 - tx) * ty], [x0 + 1, y0 + 1, tx * ty]]) {
        const i = (qy * CANVAS + qx) * 4, al = (src.data[i + 3] / 255) * w;
        R += src.data[i] * al; G += src.data[i + 1] * al; B += src.data[i + 2] * al; A += al;
      }
      const o = (y * CANVAS + x) * 4;
      if (A > 0) { out.data[o] = R / A; out.data[o + 1] = G / A; out.data[o + 2] = B / A; }
      out.data[o + 3] = Math.round(A * 255);
    }
  return out;
}

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
  const packed = [];
  pngs.forEach((png, k) => {
    const { contactY } = frames[k];
    const offX = CANVAS / 2 - hipX(png, contactY, standing) * scale;
    const offY = CANVAS - CONTACT_MARGIN - contactY * scale;
    if (offY + frames[k].minY * scale < 0) clipped++;
    const out = new PNG({ width: CANVAS, height: CANVAS });
    sampleInto(png, out, scale, offX, offY);
    packed.push(out);
  });
  const range = LOOPS[clip] ? bestLoop(packed, LOOPS[clip].min) : [0, packed.length - 1];
  const kept = packed.slice(range[0], range[1] + 1);
  kept.forEach((out, k) => writePng(outDir, clip, k, out));
  PACKED[clip] = kept;
  manifest.clips[clip] = { frames: kept.length, packedRange: range, sourceFrames: [from, to], standingPx: standing, scale: +scale.toFixed(4) };
  console.log(`${clip}: ${kept.length} frames (of ${pngs.length}, kept ${range[0]}-${range[1]}), standing ${standing}px -> scale ${scale.toFixed(3)}${clipped ? `, ${clipped} frames clipped at top` : ''}`);
}

for (const [clip, { frames, make }] of Object.entries(DERIVED)) {
  const outDir = path.join(OUT_ROOT, clip);
  fs.rmSync(outDir, { recursive: true, force: true });
  fs.mkdirSync(outDir, { recursive: true });
  for (let k = 0; k < frames; k++) {
    const spec = make(k / (frames - 1));
    writePng(outDir, clip, k, transformFrame(PACKED[spec.src[0]][spec.src[1]], spec));
  }
  manifest.clips[clip] = { frames, derivedFrom: 'jump' };
  console.log(`${clip}: ${frames} derived frames`);
}

fs.writeFileSync(path.join(OUT_ROOT, 'skater_frames.json'), JSON.stringify(manifest, null, 2));
