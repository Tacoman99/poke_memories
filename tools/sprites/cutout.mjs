// Removes the flat studio backdrop from extracted video frames.
// Usage: node cutout.mjs <clip> [--rail]
// Reads raw/<clip>/*.png, writes cut/<clip>/*.png (RGBA) and cut/<clip>/stats.json.
//
// Background = pixels reachable from the image border through small colour steps,
// so light areas inside the character (eye whites, skate toe caps) are kept.
import fs from 'node:fs';
import path from 'node:path';
import { PNG } from 'pngjs';

const clip = process.argv[2];
const removeRail = process.argv.includes('--rail');
// The grind clip has a darker studio wall with seams, so it needs looser limits.
const STEP_TOL = removeRail ? 14 : 9; // max colour change between neighbouring background pixels
const GLOBAL_TOL = removeRail ? 95 : 60; // max distance from the frame's average border colour
const FEATHER = 28;     // colour distance over which edge pixels fade in

const dist = (d, i, r, g, b) => Math.hypot(d[i] - r, d[i + 1] - g, d[i + 2] - b);

function borderMean(d, w, h) {
  let r = 0, g = 0, b = 0, n = 0;
  const add = (x, y) => { const i = (y * w + x) * 4; r += d[i]; g += d[i + 1]; b += d[i + 2]; n++; };
  for (let x = 0; x < w; x++) { add(x, 0); add(x, h - 1); }
  for (let y = 0; y < h; y++) { add(0, y); add(w - 1, y); }
  return [r / n, g / n, b / n];
}

function floodBackground(d, w, h, accept) {
  const bg = new Uint8Array(w * h);
  const queue = new Int32Array(w * h);
  let head = 0, tail = 0;
  const seed = (x, y) => { const p = y * w + x; if (!bg[p] && accept(p, -1)) { bg[p] = 1; queue[tail++] = p; } };
  for (let x = 0; x < w; x++) { seed(x, 0); seed(x, h - 1); }
  for (let y = 0; y < h; y++) { seed(0, y); seed(w - 1, y); }
  while (head < tail) {
    const p = queue[head++];
    const x = p % w, y = (p / w) | 0;
    for (const [nx, ny] of [[x + 1, y], [x - 1, y], [x, y + 1], [x, y - 1]]) {
      if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
      const q = ny * w + nx;
      if (!bg[q] && accept(q, p)) { bg[q] = 1; queue[tail++] = q; }
    }
  }
  return bg;
}

// Backdrop visible through gaps (between the legs, under an arm) is not connected to
// the border. Remove enclosed backdrop-coloured regions that are too large to be
// eye whites or highlights.
const HOLE_TOL = removeRail ? 34 : 22, HOLE_MIN_AREA = 600;

// Lowest row containing a run of magenta wheel pixels: the wheel-contact line.
function wheelBottom(d, w, h, bg) {
  for (let y = h - 1; y >= 0; y--) {
    let run = 0;
    for (let x = 0; x < w; x++) {
      const p = y * w + x, i = p * 4;
      const pink = !bg[p] && d[i] > 150 && d[i + 1] < 110 && d[i + 2] > 80 && d[i] - d[i + 1] > 80;
      run = pink ? run + 1 : 0;
      if (run >= 6) return y;
    }
  }
  return h - 1;
}
function removeEnclosedBackdrop(d, w, h, bg, [mr, mg, mb]) {
  const seen = new Uint8Array(w * h);
  const stack = [];
  for (let start = 0; start < w * h; start++) {
    if (bg[start] || seen[start] || dist(d, start * 4, mr, mg, mb) > HOLE_TOL) continue;
    const region = [];
    stack.push(start); seen[start] = 1;
    while (stack.length) {
      const p = stack.pop();
      region.push(p);
      const x = p % w;
      for (const q of [x + 1 < w ? p + 1 : -1, x > 0 ? p - 1 : -1, p + w < w * h ? p + w : -1, p - w]) {
        if (q < 0 || bg[q] || seen[q] || dist(d, q * 4, mr, mg, mb) > HOLE_TOL) continue;
        seen[q] = 1; stack.push(q);
      }
    }
    if (region.length >= HOLE_MIN_AREA) for (const p of region) bg[p] = 1;
  }
}

function cut(file, out) {
  const png = PNG.sync.read(fs.readFileSync(file));
  const { width: w, height: h, data: d } = png;
  const [mr, mg, mb] = borderMean(d, w, h);

  let bg = floodBackground(d, w, h, (q, p) => {
    const i = q * 4;
    if (dist(d, i, mr, mg, mb) > GLOBAL_TOL) return false;
    if (p < 0) return true;
    const j = p * 4;
    return dist(d, i, d[j], d[j + 1], d[j + 2]) < STEP_TOL;
  });

  if (removeRail) {
    // The grind rail and its posts are low-saturation grey and touch the frame edge.
    // Limited to the lower part of the frame so knee-pad highlights survive.
    const railTop = Math.round(h * 0.6);
    const grey = (q) => {
      if (q / w < railTop) return false;
      const i = q * 4, mx = Math.max(d[i], d[i + 1], d[i + 2]), mn = Math.min(d[i], d[i + 1], d[i + 2]);
      // Mid greys only: her black clothes and pads are darker than the rail.
      return mx - mn < 22 && mx > 70 && mx < 175;
    };
    const rail = floodBackground(d, w, h, (q) => bg[q] || grey(q));
    bg = rail;
  }

  removeEnclosedBackdrop(d, w, h, bg, [mr, mg, mb]);
  const contactY = wheelBottom(d, w, h, bg);
  // Nothing of the character hangs below the wheels: clear floor shadows, rail posts and seams.
  bg.fill(1, (contactY + 2) * w);

  // Alpha: background = 0, everything else opaque, with a soft edge next to the background.
  let minX = w, minY = h, maxX = -1, maxY = -1, sumX = 0, n = 0;
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const p = y * w + x, i = p * 4;
      if (bg[p]) { d[i + 3] = 0; continue; }
      const edge = (x > 0 && bg[p - 1]) || (x < w - 1 && bg[p + 1]) || (y > 0 && bg[p - w]) || (y < h - 1 && bg[p + w]);
      if (edge) {
        const a = Math.min(1, dist(d, i, mr, mg, mb) / FEATHER);
        if (a < 0.08) { d[i + 3] = 0; continue; }
        // Remove the backdrop colour that was blended into the edge pixel.
        d[i] = Math.max(0, Math.min(255, (d[i] - (1 - a) * mr) / a));
        d[i + 1] = Math.max(0, Math.min(255, (d[i + 1] - (1 - a) * mg) / a));
        d[i + 2] = Math.max(0, Math.min(255, (d[i + 2] - (1 - a) * mb) / a));
        d[i + 3] = Math.round(a * 255);
      } else d[i + 3] = 255;
      minX = Math.min(minX, x); maxX = Math.max(maxX, x);
      minY = Math.min(minY, y); maxY = Math.max(maxY, y);
      sumX += x; n++;
    }
  }
  fs.writeFileSync(out, PNG.sync.write(png));
  return { file: path.basename(file), minX, minY, maxX, maxY, contactY, centroidX: n ? sumX / n : 0, pixels: n };
}

const inDir = path.join('raw', clip), outDir = path.join('cut', clip);
fs.mkdirSync(outDir, { recursive: true });
const stats = fs.readdirSync(inDir).filter((f) => f.endsWith('.png')).sort()
  .map((f) => cut(path.join(inDir, f), path.join(outDir, f)));
fs.writeFileSync(path.join(outDir, 'stats.json'), JSON.stringify(stats, null, 1));
const hs = stats.map((s) => s.maxY - s.minY).sort((a, b) => a - b);
console.log(`${clip}: ${stats.length} frames, figure height median ${hs[hs.length >> 1]}px (min ${hs[0]}, max ${hs.at(-1)})`);
