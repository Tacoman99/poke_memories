// Procedural track art + parallax backgrounds for the Unity port.
// Usage: node trackart.mjs
// Writes unity/Assets/Art/Track/*.png (item sprites) and
// unity/Assets/Art/Backgrounds/<set>/{sky,far,mid,near}.png (seamless 2048x512 layers).
//
// Everything is drawn from signed-distance shapes with 1px anti-aliasing, so the output is
// deterministic: re-run the script instead of hand-editing the PNGs. Palette follows the
// concept sheet (hot pink, wine, near-black, sage, blush, cream, warm gold).
import fs from 'node:fs';
import path from 'node:path';
import { PNG } from 'pngjs';

const ART = path.resolve('../../unity/Assets/Art');

// ---------- colour + maths helpers ----------
const hex = (h) => { h = h.replace('#', ''); return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16) / 255); };
const mix = (a, b, t) => a.map((v, i) => v + (b[i] - v) * t);
const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
const rgba = (c, a = 1) => [c[0], c[1], c[2], a];
function rng(seed) {
  let s = seed >>> 0;
  return () => { s = (s + 0x6d2b79f5) >>> 0; let t = s; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
const C = (h, a = 1) => rgba(hex(h), a);
const OUTLINE = hex('#3a1a3a');

// ---------- shapes: { b: [x0,y0,x1,y1], f: (x,y) => signed distance (neg inside) } ----------
const pad = (b, t) => [b[0] - t - 2, b[1] - t - 2, b[2] + t + 2, b[3] + t + 2];
const S = {
  circle: (cx, cy, r) => ({ b: [cx - r - 2, cy - r - 2, cx + r + 2, cy + r + 2], f: (x, y) => Math.hypot(x - cx, y - cy) - r }),
  ring: (cx, cy, r, t) => ({ b: [cx - r - t - 2, cy - r - t - 2, cx + r + t + 2, cy + r + t + 2], f: (x, y) => Math.abs(Math.hypot(x - cx, y - cy) - r) - t }),
  ell: (cx, cy, rx, ry) => ({ b: [cx - rx - 2, cy - ry - 2, cx + rx + 2, cy + ry + 2], f: (x, y) => (Math.hypot((x - cx) / rx, (y - cy) / ry) - 1) * Math.min(rx, ry) }),
  rect: (x0, y0, x1, y1, r = 0) => {
    const cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, hw = (x1 - x0) / 2, hh = (y1 - y0) / 2;
    r = Math.min(r, hw, hh);
    return { b: [x0 - 2, y0 - 2, x1 + 2, y1 + 2], f: (x, y) => {
      const qx = Math.abs(x - cx) - (hw - r), qy = Math.abs(y - cy) - (hh - r);
      return Math.hypot(Math.max(qx, 0), Math.max(qy, 0)) + Math.min(Math.max(qx, qy), 0) - r;
    } };
  },
  cap: (ax, ay, bx, by, r) => ({ b: [Math.min(ax, bx) - r - 2, Math.min(ay, by) - r - 2, Math.max(ax, bx) + r + 2, Math.max(ay, by) + r + 2], f: (x, y) => {
    const px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
    const h = clamp((px * dx + py * dy) / (dx * dx + dy * dy || 1));
    return Math.hypot(px - dx * h, py - dy * h) - r;
  } }),
  poly: (pts) => {
    const xs = pts.map((p) => p[0]), ys = pts.map((p) => p[1]);
    return { b: [Math.min(...xs) - 2, Math.min(...ys) - 2, Math.max(...xs) + 2, Math.max(...ys) + 2], f: (x, y) => {
      let d = Infinity, inside = false;
      for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
        const [ax, ay] = pts[j], [bx, by] = pts[i];
        const px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
        const h = clamp((px * dx + py * dy) / (dx * dx + dy * dy || 1));
        d = Math.min(d, Math.hypot(px - dx * h, py - dy * h));
        if ((ay > y) !== (by > y) && x < ((bx - ax) * (y - ay)) / (by - ay) + ax) inside = !inside;
      }
      return inside ? -d : d;
    } };
  },
  grow: (s, t) => ({ b: pad(s.b, t), f: (x, y) => s.f(x, y) - t }),
  union: (...ss) => ({ b: [Math.min(...ss.map((s) => s.b[0])), Math.min(...ss.map((s) => s.b[1])), Math.max(...ss.map((s) => s.b[2])), Math.max(...ss.map((s) => s.b[3]))], f: (x, y) => Math.min(...ss.map((s) => s.f(x, y))) }),
};

// ---------- paints: colour or (x, y) => rgba ----------
const vgrad = (stops, y0, y1) => (x, y) => {
  const t = clamp((y - y0) / (y1 - y0));
  for (let i = 1; i < stops.length; i++) {
    if (t <= stops[i][0]) {
      const [ta, ca] = stops[i - 1], [tb, cb] = stops[i];
      return rgba(mix(ca, cb, (t - ta) / (tb - ta || 1)));
    }
  }
  return rgba(stops[stops.length - 1][1]);
};
const radial = (cx, cy, r, c, a = 1) => (x, y) => rgba(c, a * Math.pow(clamp(1 - Math.hypot(x - cx, y - cy) / r), 2));

// ---------- canvas ----------
class Canvas {
  constructor(w, h, wrap = false) { this.w = w; this.h = h; this.wrap = wrap; this.d = new Float32Array(w * h * 4); }
  // Composites a shape with a paint (premultiplied accumulation).
  add(shape, paint, alpha = 1) {
    const offsets = this.wrap ? [-this.w, 0, this.w] : [0];
    const solid = Array.isArray(paint);
    for (const off of offsets) {
      const x0 = Math.max(0, Math.floor(shape.b[0] + off)), x1 = Math.min(this.w - 1, Math.ceil(shape.b[2] + off));
      const y0 = Math.max(0, Math.floor(shape.b[1])), y1 = Math.min(this.h - 1, Math.ceil(shape.b[3]));
      for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
        const cov = clamp(0.5 - shape.f(x + 0.5 - off, y + 0.5));
        if (cov <= 0) continue;
        const c = solid ? paint : paint(x + 0.5 - off, y + 0.5);
        const a = cov * (c[3] ?? 1) * alpha;
        const i = (y * this.w + x) * 4;
        this.d[i] = c[0] * a + this.d[i] * (1 - a);
        this.d[i + 1] = c[1] * a + this.d[i + 1] * (1 - a);
        this.d[i + 2] = c[2] * a + this.d[i + 2] * (1 - a);
        this.d[i + 3] = a + this.d[i + 3] * (1 - a);
      }
    }
  }
  // Fills everything below a height function (hills, hedges, grass), seamless when wrapped.
  below(fn, paint, alpha = 1) {
    for (let x = 0; x < this.w; x++) {
      const top = fn(x + 0.5);
      for (let y = Math.max(0, Math.floor(top) - 1); y < this.h; y++) {
        const cov = clamp(y + 0.5 - top + 0.5);
        if (cov <= 0) continue;
        const c = Array.isArray(paint) ? paint : paint(x + 0.5, y + 0.5);
        const a = cov * (c[3] ?? 1) * alpha;
        const i = (y * this.w + x) * 4;
        this.d[i] = c[0] * a + this.d[i] * (1 - a);
        this.d[i + 1] = c[1] * a + this.d[i + 1] * (1 - a);
        this.d[i + 2] = c[2] * a + this.d[i + 2] * (1 - a);
        this.d[i + 3] = a + this.d[i + 3] * (1 - a);
      }
    }
  }
  // Dark outline then fill, for the bold cartoon look of gameplay items.
  outlined(shape, fill, t = 3, outline = OUTLINE) {
    this.add(S.grow(shape, t), rgba(outline));
    this.add(shape, fill);
  }
  save(file, grain = 0) {
    const png = new PNG({ width: this.w, height: this.h });
    const r = rng(7);
    for (let i = 0; i < this.w * this.h; i++) {
      const a = this.d[i * 4 + 3];
      const n = grain && a > 0.99 ? (r() - 0.5) * grain : 0;
      for (let k = 0; k < 3; k++) {
        const v = a > 0 ? this.d[i * 4 + k] / a + n : 0;
        png.data[i * 4 + k] = Math.round(clamp(v) * 255);
      }
      png.data[i * 4 + 3] = Math.round(clamp(a) * 255);
    }
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, PNG.sync.write(png));
    console.log('wrote', path.relative(ART, file));
  }
}

// ---------- scene building blocks ----------
// Periodic height function so hills tile seamlessly: sum of integer harmonics.
const hills = (W, base, harmonics, seed) => {
  const r = rng(seed);
  const parts = harmonics.map(([k, amp]) => [k, amp, r() * Math.PI * 2]);
  return (x) => base + parts.reduce((s, [k, amp, ph]) => s + amp * Math.sin((2 * Math.PI * k * x) / W + ph), 0);
};

function puffTree(cv, x, baseY, h, tones, trunk, r, spread = 0.3) {
  cv.add(S.cap(x, baseY, x + h * 0.03, baseY - h * 0.6, Math.max(2, h * 0.035)), rgba(trunk));
  cv.add(S.cap(x + h * 0.02, baseY - h * 0.45, x + h * 0.2, baseY - h * 0.68, Math.max(1.5, h * 0.02)), rgba(trunk));
  const cy = baseY - h * 0.72, n = 9 + Math.floor(h / 30);
  const blobs = [];
  for (let i = 0; i < n; i++) {
    const a = r() * Math.PI * 2, d = r() * h * spread;
    blobs.push([x + Math.cos(a) * d * 1.2, cy + Math.sin(a) * d * 0.8, h * (0.1 + r() * 0.08)]);
  }
  for (const [bx, by, br] of blobs) cv.add(S.circle(bx + br * 0.15, by + br * 0.25, br), rgba(tones[2]));
  for (const [bx, by, br] of blobs) cv.add(S.circle(bx, by, br), rgba(tones[0]));
  for (const [bx, by, br] of blobs) cv.add(S.circle(bx - br * 0.25, by - br * 0.3, br * 0.55), rgba(tones[1]));
}

function leaf(cx, cy, angle, len, wid, droop) {
  const up = [], down = [];
  for (let i = 0; i <= 12; i++) {
    const t = i / 12;
    const px = cx + Math.cos(angle) * len * t, py = cy + Math.sin(angle) * len * t + droop * t * t;
    const w = wid * Math.pow(Math.sin(Math.PI * Math.min(1, t * 0.92 + 0.04)), 0.7);
    up.push([px, py - w]); down.push([px, py + w]);
  }
  return S.poly([...up, ...down.reverse()]);
}

function palm(cv, x, baseY, h, trunk, fronds, r, lean = 0.12) {
  const tipX = x + h * lean, tipY = baseY - h;
  const pts = [];
  for (let i = 0; i <= 10; i++) { const t = i / 10; pts.push([x + (tipX - x) * t * t, baseY - h * t]); }
  for (let i = 1; i < pts.length; i++) cv.add(S.cap(...pts[i - 1], ...pts[i], Math.max(2, h * 0.03 * (1 - 0.35 * i / 10))), rgba(trunk));
  for (let i = 0; i < 9; i++) {
    const a = -Math.PI + (i / 8) * Math.PI;
    cv.add(leaf(tipX, tipY, a, h * (0.38 + r() * 0.1), h * 0.045, h * 0.2), rgba(fronds[i % 2]));
  }
  cv.add(S.circle(tipX, tipY + 3, Math.max(3, h * 0.025)), rgba(trunk));
}

const glow = (cv, x, y, rad, c, a = 0.8) => cv.add(S.circle(x, y, rad), radial(x, y, rad, c, a));

function lamp(cv, x, baseY, h, pole, bulb, glowColor, glowA = 0.7) {
  cv.add(S.rect(x - 3, baseY - h, x + 3, baseY, 2), rgba(pole));
  cv.add(S.rect(x - 9, baseY - h - 4, x + 9, baseY - h + 3, 3), rgba(pole));
  glow(cv, x, baseY - h + 12, 52, glowColor, glowA);
  cv.add(S.ell(x, baseY - h + 11, 7, 9), rgba(bulb));
}

function stringLights(cv, x0, y0, x1, y1, sag, colors, wire, r, n = 9) {
  const pts = [];
  for (let i = 0; i <= n; i++) { const t = i / n; pts.push([x0 + (x1 - x0) * t, y0 + (y1 - y0) * t + Math.sin(Math.PI * t) * sag]); }
  for (let i = 1; i < pts.length; i++) cv.add(S.cap(...pts[i - 1], ...pts[i], 1), rgba(wire));
  pts.forEach(([px, py], i) => {
    const c = colors[i % colors.length];
    glow(cv, px, py + 5, 18, c, 0.55);
    cv.add(S.circle(px, py + 5, 4), rgba(c));
  });
}

function skyline(cv, x0, x1, baseY, minH, maxH, color, window, r, litChance = 0.4) {
  for (let x = x0; x < x1;) {
    const w = 46 + r() * 70, h = minH + r() * (maxH - minH);
    cv.add(S.rect(x, baseY - h, x + w, baseY + 4), rgba(color));
    if (r() < 0.35) cv.add(S.rect(x + w * 0.3, baseY - h - 22, x + w * 0.7, baseY - h + 2), rgba(color));
    if (r() < 0.25) cv.add(S.cap(x + w / 2, baseY - h, x + w / 2, baseY - h - 34 - r() * 20, 1.5), rgba(color));
    for (let wy = baseY - h + 14; wy < baseY - 14; wy += 18)
      for (let wx = x + 8; wx < x + w - 10; wx += 14)
        if (r() < litChance) cv.add(S.rect(wx, wy, wx + 6, wy + 9), rgba(window));
    x += w + 4 + r() * 14;
  }
}

// ---------- backgrounds ----------
const W = 2048, H = 512;

function sky(stops, file) {
  const cv = new Canvas(64, H);
  cv.add(S.rect(0, 0, 64, H), vgrad(stops, 0, H));
  cv.save(file, 0.012);
}

function cloudBank(cv, r, n, yMin, yMax, color, alpha) {
  for (let i = 0; i < n; i++) {
    const x = (i + r()) * (W / n), y = yMin + r() * (yMax - yMin), s = 0.7 + r() * 0.7;
    for (const [dx, dy, rad] of [[-50, 4, 24], [-20, -8, 34], [16, -12, 38], [52, 0, 28], [0, 10, 36]])
      cv.add(S.circle(x + dx * s, y + dy * s, rad * s), rgba(color), alpha);
    cv.add(S.ell(x, y + 22 * s, 82 * s, 12 * s), rgba(color), alpha);
  }
}

function roseWalk() {
  const dir = path.join(ART, 'Backgrounds/RoseWalk');
  sky([[0, hex('#f7b6cc')], [0.55, hex('#fbd3d8')], [1, hex('#fff0e2')]], path.join(dir, 'sky.png'));

  let r = rng(11);
  const far = new Canvas(W, H, true);
  cloudBank(far, r, 6, 60, 190, hex('#fff7f2'), 0.8);
  far.below(hills(W, 372, [[2, 20], [5, 10], [9, 5]], 3), C('#efb4c8'));
  for (let x = 40; x < W; x += 150 + r() * 120) puffTree(far, x, hills(W, 372, [[2, 20], [5, 10], [9, 5]], 3)(x) + 6, 90 + r() * 40, [hex('#f4a6c2'), hex('#f9c4d6'), hex('#e58aae')], hex('#a8657f'), r, 0.28);
  far.below(hills(W, 428, [[3, 16], [7, 8], [11, 4]], 5), C('#e49ab7'));
  far.save(path.join(dir, 'far.png'));

  r = rng(12);
  const mid = new Canvas(W, H, true);
  mid.below(hills(W, 452, [[4, 8], [9, 4]], 6), C('#8db795'));
  const blossom = [hex('#ee7fae'), hex('#f8b3cb'), hex('#c95a8d')];
  for (const x of [120, 1000, 1900]) puffTree(mid, x, 470, 230 + r() * 40, blossom, hex('#6b2c4a'), r, 0.34);
  for (const x of [610, 1560]) {
    // Rose arch: posts, ring and climbing roses.
    mid.add(S.rect(x - 78, 170, x - 66, 480, 3), C('#fff6ee'));
    mid.add(S.rect(x + 66, 170, x + 78, 480, 3), C('#fff6ee'));
    mid.add(S.ring(x, 200, 72, 6), C('#fff6ee'));
    mid.add(S.rect(x - 84, 470, x + 84, 480, 3), C('#e7d3cc'));
    for (let i = 0; i < 26; i++) {
      const a = r() * Math.PI, rad = 72 + (r() - 0.5) * 14, py = i < 18 ? 200 - Math.sin(a) * rad : 220 + r() * 220, px = i < 18 ? x - Math.cos(a) * rad : x + (r() < 0.5 ? -72 : 72) + (r() - 0.5) * 10;
      mid.add(S.circle(px, py, 9 + r() * 4), C('#5f8a70'));
      mid.add(S.circle(px + 1, py - 1, 6 + r() * 3), r() < 0.5 ? C('#ff4f93') : C('#f9a6c4'));
      mid.add(S.circle(px + 1, py - 1, 2.5), C('#c02a6a'));
    }
  }
  mid.save(path.join(dir, 'mid.png'));

  r = rng(13);
  const near = new Canvas(W, H, true);
  for (const x of [330, 1360]) lamp(near, x, 500, 190, hex('#6b2c4a'), hex('#fff1c0'), hex('#ffe9a8'), 0.55);
  near.below(hills(W, 468, [[6, 6], [13, 3]], 8), C('#6f9c7c'));
  for (let x = 20; x < W; x += 30 + r() * 28) {
    const y = hills(W, 468, [[6, 6], [13, 3]], 8)(x) + 4;
    near.add(S.circle(x, y, 20 + r() * 12), C('#6f9c7c'));
    near.add(S.circle(x - 6, y - 8, 12 + r() * 6), C('#86b592'));
  }
  for (let i = 0; i < 70; i++) {
    const x = r() * W, y = 452 + r() * 50, big = r() < 0.5;
    near.add(S.circle(x, y, big ? 10 : 7), r() < 0.55 ? C('#ff4f93') : C('#f9a6c4'));
    near.add(S.circle(x + 1, y - 1, big ? 5 : 3.5), C('#c02a6a'));
  }
  // Picket fence along the very front.
  near.add(S.rect(0, 490, W, 498, 2), C('#fff3ea'));
  for (let x = 6; x < W; x += 34) near.add(S.poly([[x, 512], [x, 478], [x + 11, 466], [x + 22, 478], [x + 22, 512]]), C('#fff8f1'));
  near.save(path.join(dir, 'near.png'));
}

function boardwalk() {
  const dir = path.join(ART, 'Backgrounds/Boardwalk');
  sky([[0, hex('#f46d9a')], [0.5, hex('#ff9d86')], [1, hex('#ffd9a0')]], path.join(dir, 'sky.png'));

  let r = rng(21);
  const far = new Canvas(W, H, true);
  glow(far, 1400, 300, 260, hex('#fff0c0'), 0.7);
  far.add(S.circle(1400, 300, 86), C('#fff4cf'));
  for (let i = 0; i < 7; i++) far.add(S.ell(r() * W, 110 + r() * 150, 90 + r() * 90, 6 + r() * 4), C('#ffc4a8'), 0.85);
  // Ferris wheel.
  const fx = 480, fy = 250, fr = 128;
  far.add(S.cap(fx, fy, fx - 70, 372, 5), C('#b5486f'));
  far.add(S.cap(fx, fy, fx + 70, 372, 5), C('#b5486f'));
  for (let i = 0; i < 12; i++) {
    const a = (i / 12) * Math.PI * 2;
    far.add(S.cap(fx, fy, fx + Math.cos(a) * fr, fy + Math.sin(a) * fr, 1.6), C('#d9628b'));
    far.add(S.rect(fx + Math.cos(a) * fr - 8, fy + Math.sin(a) * fr - 2, fx + Math.cos(a) * fr + 8, fy + Math.sin(a) * fr + 16, 4), i % 2 ? C('#fff1d6') : C('#ff7aa8'));
  }
  far.add(S.ring(fx, fy, fr, 4), C('#d9628b'));
  far.add(S.ring(fx, fy, fr * 0.6, 2.5), C('#e57a9f'));
  far.add(S.circle(fx, fy, 10), C('#b5486f'));
  // Sea.
  far.add(S.rect(0, 352, W, H), vgrad([[0, hex('#d9618a')], [1, hex('#8f3a77')]], 352, H));
  for (let i = 0; i < 80; i++) { const x = r() * W, y = 358 + r() * 130, l = 14 + r() * 40; far.add(S.cap(x, y, x + l, y, 1.5), C('#ffe2b8'), 0.35 + r() * 0.35); }
  for (const x of [900, 1700]) { far.add(S.poly([[x, 330], [x, 360], [x + 28, 360]]), C('#fff4e6')); far.add(S.poly([[x - 3, 335], [x - 3, 360], [x - 22, 360]]), C('#ffd9c8')); far.add(S.rect(x - 24, 360, x + 30, 366, 3), C('#7a2f5e')); }
  far.save(path.join(dir, 'far.png'));

  r = rng(22);
  const mid = new Canvas(W, H, true);
  mid.add(S.rect(0, 456, W, H), C('#a8506f'));
  const wall = ['#fff1e0', '#bfe3d0', '#f9c6cf', '#ffe3a1', '#d9c6ef'];
  let x = 20, k = 0;
  const tops = [];
  while (x < W - 120) {
    const w = 150 + r() * 70, h = 170 + r() * 90, c = hex(wall[k++ % wall.length]);
    mid.add(S.rect(x, 468 - h, x + w, 470, 2), rgba(c));
    mid.add(S.rect(x, 468 - h, x + w, 468 - h + 10, 2), rgba(mix(c, hex('#c04a7a'), 0.35)));
    // Awning stripes.
    const ay = 468 - h * 0.48, n = 8, sw = (w + 16) / n;
    for (let i = 0; i < n; i++) mid.add(S.poly([[x - 8 + i * sw, ay], [x - 8 + (i + 1) * sw, ay], [x - 14 + (i + 1) * sw, ay + 34], [x - 14 + i * sw, ay + 34]]), i % 2 ? C('#fff6ee') : C('#ee3f86'));
    mid.add(S.rect(x + 18, ay + 48, x + w - 18, 460, 4), C('#ffeaa8'), 0.9);
    mid.add(S.rect(x + 18, ay + 48, x + w - 18, ay + 56, 3), C('#a0456e'));
    mid.add(S.rect(x + w * 0.5 - 20, 468 - h + 34, x + w * 0.5 + 20, 468 - h + 52, 4), C('#c8467a'));
    tops.push([x + w / 2, 468 - h]);
    x += w + 10 + r() * 24;
  }
  for (let i = 1; i < tops.length; i++)
    stringLights(mid, tops[i - 1][0], tops[i - 1][1] + 10, tops[i][0], tops[i][1] + 10, 22, [hex('#ffe08a'), hex('#ff6aa3'), hex('#fff2c8')], hex('#7a2f5e'), r, 8);
  for (const px of [40, 1010, 1980]) palm(mid, px, 470, 300, hex('#7a3a52'), [hex('#4f8f6b'), hex('#3f7a5c')], r, 0.1);
  mid.save(path.join(dir, 'mid.png'));

  r = rng(23);
  const near = new Canvas(W, H, true);
  for (const ux of [420, 1500]) {
    near.add(S.cap(ux, 430, ux + 4, 512, 3), C('#fff1e0'));
    for (let i = 0; i < 6; i++) {
      const a0 = Math.PI + (i / 6) * Math.PI, a1 = Math.PI + ((i + 1) / 6) * Math.PI;
      near.add(S.poly([[ux, 372], [ux + Math.cos(a0) * 96, 430 + Math.sin(a0) * 58], [ux + Math.cos(a1) * 96, 430 + Math.sin(a1) * 58]]), i % 2 ? C('#fff6ee') : C('#ee3f86'));
    }
  }
  for (const lx of [220, 980, 1780]) lamp(near, lx, 500, 180, hex('#6b2c4a'), hex('#fff1c0'), hex('#ffe08a'), 0.6);
  near.add(S.rect(0, 440, W, 450, 4), C('#fff1e0'));
  near.add(S.rect(0, 470, W, 476, 3), C('#fff1e0'));
  for (let px = 16; px < W; px += 64) near.add(S.rect(px, 438, px + 12, 512, 4), C('#6b2c4a'));
  near.add(S.rect(0, 496, W, H), C('#9b4a62'));
  near.save(path.join(dir, 'near.png'));
}

function goldenHour() {
  const dir = path.join(ART, 'Backgrounds/GoldenHour');
  sky([[0, hex('#5b2a6e')], [0.3, hex('#a73a85')], [0.62, hex('#ff7a6a')], [0.85, hex('#ffb35a')], [1, hex('#ffd98a')]], path.join(dir, 'sky.png'));

  let r = rng(31);
  const far = new Canvas(W, H, true);
  glow(far, 1000, 360, 330, hex('#fff0a8'), 0.55);
  far.add(S.circle(1000, 360, 128), vgrad([[0, hex('#fff6c4')], [1, hex('#ffb84d')]], 232, 488));
  for (let i = 0; i < 6; i++) far.add(S.ell(r() * W, 110 + r() * 170, 120 + r() * 120, 7 + r() * 6), i % 2 ? C('#ff9a8a') : C('#c9487f'), 0.75);
  for (let i = 0; i < 7; i++) { const x = r() * W, y = 90 + r() * 140; far.add(S.cap(x - 12, y - 5, x, y, 1.6), C('#3a1a3a')); far.add(S.cap(x, y, x + 12, y - 5, 1.6), C('#3a1a3a')); }
  far.below(hills(W, 372, [[3, 38], [6, 18], [11, 7]], 41), C('#a14283'));
  far.below(hills(W, 418, [[2, 24], [5, 14], [9, 6]], 43), C('#7d2f74'));
  for (const bx of [300, 1620]) { // hot-air balloons
    far.add(S.cap(bx - 14, 232, bx - 8, 252, 1), C('#5a1e50')); far.add(S.cap(bx + 14, 232, bx + 8, 252, 1), C('#5a1e50'));
    far.add(S.circle(bx, 200, 36), C('#ff5d9e')); far.add(S.ell(bx, 200, 14, 36), C('#ffd27a')); far.add(S.rect(bx - 8, 252, bx + 8, 264, 2), C('#5a1e50'));
  }
  far.save(path.join(dir, 'far.png'));

  r = rng(32);
  const mid = new Canvas(W, H, true);
  skyline(mid, 0, W - 60, 470, 90, 260, hex('#5d2260'), hex('#ffd27a'), r, 0.35);
  mid.below(hills(W, 462, [[3, 6], [8, 3]], 44), C('#4a1a4f'));
  for (const px of [180, 760, 1330, 1850]) palm(mid, px, 480, 280 + r() * 60, hex('#3a1440'), [hex('#4a1a4f'), hex('#3a1440')], r, (r() - 0.5) * 0.3);
  mid.save(path.join(dir, 'mid.png'));

  r = rng(33);
  const near = new Canvas(W, H, true);
  const dark = hex('#2e1233');
  for (const lx of [300, 1180, 1900]) lamp(near, lx, 500, 190, dark, hex('#ffe9a8'), hex('#ffc15a'), 0.7);
  stringLights(near, 300, 330, 1180, 330, 36, [hex('#ffd27a'), hex('#ff8fb8')], hex('#2e1233'), r, 18);
  stringLights(near, 1180, 330, 1900, 330, 30, [hex('#ffd27a'), hex('#ff8fb8')], hex('#2e1233'), r, 14);
  // Bench.
  near.add(S.rect(520, 438, 640, 450, 3), rgba(dark));
  near.add(S.rect(520, 410, 640, 420, 3), rgba(dark));
  near.add(S.rect(526, 420, 534, 470, 2), rgba(dark)); near.add(S.rect(626, 420, 634, 470, 2), rgba(dark));
  const grass = hills(W, 478, [[9, 3], [21, 2]], 47);
  near.below(grass, rgba(dark));
  for (let x = 0; x < W; x += 9) near.add(S.poly([[x, 486], [x + 4 + r() * 4, 486 - 22 - r() * 26], [x + 12, 486]]), rgba(dark));
  near.save(path.join(dir, 'near.png'));
}

// ---------- gameplay items (drawn at 6 px per sim pixel, aspect matches the sim sizes) ----------
const ITEMS = path.join(ART, 'Track');
const dk = hex('#3a1a3a');

function ball() {
  const n = 168, cv = new Canvas(n, n), c = n / 2;
  cv.add(S.circle(c, c, 78), rgba(OUTLINE));
  cv.add(S.circle(c, c, 71), vgrad([[0, hex('#ff5a7d')], [1, hex('#d8213f')]], 20, 90));
  const bottom = (x, y) => (y > c ? rgba(mix(hex('#ffffff'), hex('#ead3dc'), clamp((y - c) / 70))) : [0, 0, 0, 0]);
  cv.add(S.circle(c, c, 71), bottom);
  cv.add(S.rect(c - 71, c - 6, c + 71, c + 6), rgba(OUTLINE));
  cv.add(S.circle(c, c, 24), rgba(OUTLINE));
  cv.add(S.circle(c, c, 15), C('#ffffff'));
  cv.add(S.circle(c - 4, c - 4, 6), C('#ffe3ea'));
  cv.add(S.ell(c - 32, c - 38, 17, 9), C('#ffffff'), 0.7);
  cv.save(path.join(ITEMS, 'ball.png'));
}

function cone() {
  const w = 192, h = 216, cv = new Canvas(w, h);
  cv.add(S.rect(6, h - 28, w - 6, h - 4, 9), rgba(OUTLINE));
  cv.add(S.rect(12, h - 24, w - 12, h - 10, 6), C('#c8541e'));
  const body = S.poly([[w * 0.5 - 12, 10], [w * 0.5 + 12, 10], [w - 26, h - 22], [26, h - 22]]);
  cv.outlined(body, (x, y) => rgba(mix(hex('#ff9246'), hex('#e0662b'), clamp((x - 40) / 110))), 6);
  cv.add(S.poly([[w * 0.5 - 34, 82], [w * 0.5 + 34, 82], [w * 0.5 + 44, 118], [w * 0.5 - 44, 118]]), C('#fff6ee'));
  cv.add(S.poly([[w * 0.5 - 62, 150], [w * 0.5 + 62, 150], [w * 0.5 + 70, 182], [w * 0.5 - 70, 182]]), C('#fff6ee'));
  cv.add(S.cap(w * 0.5 - 10, 24, 52, h - 44, 4), C('#ffc08a'), 0.7);
  cv.save(path.join(ITEMS, 'cone.png'));
}

function barrier() {
  const w = 420, h = 312, cv = new Canvas(w, h);
  for (const lx of [44, w - 44]) {
    cv.outlined(S.rect(lx - 16, 120, lx + 16, h - 14, 6), C('#8a6a7a'), 5);
    cv.outlined(S.rect(lx - 34, h - 30, lx + 34, h - 8, 8), C('#5a3a5a'), 5);
  }
  const board = S.rect(10, 10, w - 10, 190, 20);
  cv.outlined(board, C('#fff6ee'), 6);
  const stripe = (x, y) => (((x + y) % 120 + 120) % 120 < 60 ? C('#ee3f86') : [0, 0, 0, 0]);
  cv.add(board, stripe);
  cv.add(S.rect(10, 10, w - 10, 50, 20), C('#ffffff'), 0.3);
  cv.save(path.join(ITEMS, 'barrier.png'));
}

function kicker() {
  const w = 360, h = 204, cv = new Canvas(w, h);
  const wedge = S.poly([[8, h - 8], [w - 8, 10], [w - 8, h - 8]]);
  cv.outlined(wedge, vgrad([[0, hex('#f6a8c6')], [1, hex('#e1709c')]], 20, h), 7);
  cv.add(S.poly([[8, h - 8], [w - 8, 10], [w - 8, 34], [40, h - 8]]), C('#ffd1e1'));
  cv.add(S.cap(14, h - 14, w - 14, 16, 3), C('#8a1e4e'));
  for (let i = 0; i < 5; i++) cv.add(S.poly([[w - 70, 60 + i * 28 + 20], [w - 20, 60 + i * 28 - 0], [w - 20, 60 + i * 28 + 14], [w - 70, 60 + i * 28 + 34]]), i % 2 ? C('#ffe08a') : C('#3a1a3a'));
  cv.save(path.join(ITEMS, 'kicker.png'));
}

function gap() {
  const w = 256, h = 512, cv = new Canvas(w, h);
  cv.add(S.rect(0, 0, w, h), vgrad([[0, hex('#6a2a58')], [0.25, hex('#3b1740')], [1, hex('#1c0a22')]], 0, h));
  cv.add(S.rect(0, 0, 22, h), C('#f0a6c0'), 0.9);
  cv.add(S.rect(w - 22, 0, w, h), C('#f0a6c0'), 0.9);
  cv.add(S.rect(0, 0, 22, 14), C('#fff1f6'));
  cv.add(S.rect(w - 22, 0, w, 14), C('#fff1f6'));
  cv.add(S.rect(22, 0, 34, h), C('#8a1e4e'), 0.55);
  cv.add(S.rect(w - 34, 0, w - 22, h), C('#8a1e4e'), 0.55);
  for (let i = 0; i < 9; i++) cv.add(S.poly([[40 + i * 22, 0], [40 + i * 22 + 11, 36 + (i % 3) * 8], [40 + i * 22 + 22, 0]]), C('#2a0e30'));
  cv.save(path.join(ITEMS, 'gap.png'));
}

function railBar() {
  const w = 512, h = 48, cv = new Canvas(w, h);
  cv.add(S.rect(0, 0, w, h, 22), rgba(OUTLINE));
  cv.add(S.rect(5, 5, w - 5, h - 5, 18), vgrad([[0, hex('#d8d4de')], [0.45, hex('#8f8a9c')], [1, hex('#4e4a5c')]], 5, h - 5));
  cv.add(S.rect(20, 10, w - 20, 18, 4), C('#ffffff'), 0.65);
  cv.save(path.join(ITEMS, 'rail_bar.png'));
}

function railPost() {
  const w = 48, h = 256, cv = new Canvas(w, h);
  cv.add(S.rect(8, 0, w - 8, h - 6, 4), rgba(OUTLINE));
  cv.add(S.rect(13, 0, w - 13, h - 10, 2), (x) => rgba(mix(hex('#bdb8c8'), hex('#5e5a6c'), clamp((x - 13) / 22))));
  cv.add(S.rect(0, h - 20, w, h, 6), rgba(OUTLINE));
  cv.add(S.rect(4, h - 17, w - 4, h - 4, 4), C('#6e6a7c'));
  cv.save(path.join(ITEMS, 'rail_post.png'));
}

function step() {
  const w = 256, h = 128, cv = new Canvas(w, h);
  cv.outlined(S.rect(4, 4, w - 4, h - 4, 10), vgrad([[0, hex('#f6c6d6')], [1, hex('#d98ab0')]], 4, h), 5);
  cv.add(S.rect(10, 10, w - 10, 34, 8), C('#ffe3ec'));
  cv.add(S.rect(10, 34, w - 10, 40, 2), C('#a0456e'), 0.5);
  cv.save(path.join(ITEMS, 'step.png'));
}

function ground() {
  const w = 512, h = 512, cv = new Canvas(w, h, true);
  cv.add(S.rect(0, 0, w, h), vgrad([[0, hex('#f9d6df')], [0.12, hex('#f4bccb')], [1, hex('#d98fab')]], 0, h));
  cv.add(S.rect(0, 0, w, 14), C('#fff1f4'));
  cv.add(S.rect(0, 14, w, 24), C('#b5527c'), 0.6);
  cv.add(S.rect(0, 24, w, 30), C('#8a1e4e'), 0.28);
  for (const x of [0, 256]) cv.add(S.rect(x - 2, 30, x + 2, h), C('#a8527a'), 0.35);
  for (const y of [150, 280, 410]) cv.add(S.rect(0, y - 1.5, w, y + 1.5), C('#a8527a'), 0.28);
  cv.save(path.join(ITEMS, 'ground.png'), 0.01);
}

roseWalk();
boardwalk();
goldenHour();
ball(); cone(); barrier(); kicker(); gap(); railBar(); railPost(); step(); ground();
