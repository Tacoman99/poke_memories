import { useEffect, useRef, useState } from 'react';
import type { GameMode, GameResult, Outfit } from '../types';
import { advanceSimulation, COURSE_LENGTH, COURSE_SECONDS, createSimulation, pressJump, railHeight, releaseJump, SECTION_LENGTH, startTrick, trickProgress, TRICKS } from './gameplay';
import type { Simulation, TrickKind } from './gameplay';
import { drawBall, drawCompanion, drawSkater } from './gameArt';

interface GameViewProps {
  mode: Exclude<GameMode, 'BOWL'>;
  outfit: Outfit;
  onEnd: (result: GameResult) => void;
  onExit: () => void;
}

const METRE = 24;
const SECTIONS = ['Rose Walk', 'Boardwalk Rush', 'Golden Hour'];
const TRICK_KEYS: Record<string, TrickKind> = { KeyJ: 'grab', KeyK: 'kick', KeyL: 'spin' };
const TRICK_BUTTONS: { kind: TrickKind; key: string }[] = [
  { kind: 'grab', key: 'J' }, { kind: 'kick', key: 'K' }, { kind: 'spin', key: 'L' },
];
const snapshot = (state: Simulation) => ({
  distance: Math.floor(state.distance / METRE),
  collected: state.collected,
  points: Math.floor(state.trickScore),
  hearts: state.hearts,
  progress: Math.min(100, Math.floor(state.distance / COURSE_LENGTH * 100)),
  grinding: !!state.rail,
  feedback: state.feedbackTime > 0 ? state.feedback : '',
  ended: state.ended,
  completed: state.completed,
  section: SECTIONS[Math.min(2, Math.floor(state.distance / SECTION_LENGTH) % 3)],
  combo: state.combo,
  multiplier: Math.min(5, state.combo),
  trick: state.trick ? TRICKS[state.trick].name : '',
  bestCombo: state.bestCombo,
  airborne: !state.grounded && !state.rail,
});

const GameView: React.FC<GameViewProps> = props => {
  const { mode, onExit } = props;
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const jumpRef = useRef<HTMLButtonElement>(null);
  const trickRef = useRef<(kind: TrickKind) => void>(() => {});
  const simulation = useRef(createSimulation(mode));
  const latest = useRef(props);
  latest.current = props;
  const [hud, setHud] = useState(() => snapshot(simulation.current));
  const [error, setError] = useState('');
  const [paused, setPaused] = useState(false);
  const pausedRef = useRef(false);

  useEffect(() => {
    const canvas = canvasRef.current;
    const jumpTarget = jumpRef.current;
    if (!canvas || !jumpTarget) {
      setError('The skating scene couldn’t start. Please return and try again.');
      return;
    }
    const ctx = canvas.getContext('2d');
    if (!ctx) {
      setError('Your browser couldn’t open the skating canvas. Please try another browser.');
      return;
    }
    const state = createSimulation(mode);
    simulation.current = state;
    setHud(snapshot(state));
    setError('');
    let active = true, frame = 0, lastTime = 0, hudTime = 0, endTime = 0;
    let reported = false;
    const inputs = new Set<string>();
    const size = { width: 0, height: 0, dpr: 0 };
    const resize = () => {
      const rect = canvas.getBoundingClientRect();
      const dpr = Math.min(window.devicePixelRatio || 1, 3);
      if (rect.width === size.width && rect.height === size.height && dpr === size.dpr) return;
      size.width = rect.width;
      size.height = rect.height;
      size.dpr = dpr;
      canvas.width = Math.round(rect.width * dpr);
      canvas.height = Math.round(rect.height * dpr);
    };
    const inputDown = (id: string) => {
      if (inputs.has(id) || state.ended) return;
      if (pausedRef.current) {
        pausedRef.current = false;
        setPaused(false);
        lastTime = 0;
      }
      if (inputs.size === 0) pressJump(state);
      inputs.add(id);
    };
    const inputUp = (id: string) => {
      inputs.delete(id);
      if (inputs.size === 0) releaseJump(state);
    };
    const releaseAll = () => { inputs.clear(); releaseJump(state); };
    const pause = () => {
      releaseAll();
      pausedRef.current = true;
      setPaused(true);
      lastTime = 0;
    };
    const visibility = () => { if (document.hidden) pause(); };
    const trick = (kind: TrickKind) => {
      if (state.ended) return;
      if (pausedRef.current) {
        pausedRef.current = false;
        setPaused(false);
        lastTime = 0;
      }
      startTrick(state, kind);
    };
    trickRef.current = trick;
    const key = (event: KeyboardEvent) => ['Space', 'ArrowUp', 'KeyW'].includes(event.code);
    const keyDown = (event: KeyboardEvent) => {
      const trickKind = TRICK_KEYS[event.code];
      if (trickKind) {
        if (!event.repeat && !event.metaKey && !event.ctrlKey && !event.altKey) trick(trickKind);
        return;
      }
      if (!key(event)) return;
      const target = event.target;
      if (target instanceof HTMLElement && target.closest('button, input, textarea, select, a, [contenteditable="true"]') &&
          target !== jumpTarget) return;
      event.preventDefault();
      if (!event.repeat) inputDown(`key:${event.code}`);
    };
    const keyUp = (event: KeyboardEvent) => {
      if (!key(event)) return;
      if (inputs.has(`key:${event.code}`)) event.preventDefault();
      inputUp(`key:${event.code}`);
    };
    const pointerDown = (event: PointerEvent) => {
      if (event.button !== 0) return;
      event.preventDefault();
      jumpTarget.setPointerCapture(event.pointerId);
      inputDown(`pointer:${event.pointerId}`);
    };
    const pointerUp = (event: PointerEvent) => inputUp(`pointer:${event.pointerId}`);
    const accessibleClick = (event: MouseEvent) => {
      // Keyboard Enter and assistive technology activate via click, not pointer.
      if (event.detail !== 0) return;
      inputDown('activate');
      inputUp('activate');
    };
    jumpTarget.addEventListener('pointerdown', pointerDown);
    jumpTarget.addEventListener('pointerup', pointerUp);
    jumpTarget.addEventListener('pointercancel', pointerUp);
    jumpTarget.addEventListener('lostpointercapture', pointerUp);
    jumpTarget.addEventListener('click', accessibleClick);
    window.addEventListener('keydown', keyDown);
    window.addEventListener('keyup', keyUp);
    window.addEventListener('blur', pause);
    document.addEventListener('visibilitychange', visibility);
    window.addEventListener('resize', resize);
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(resize);
    observer?.observe(canvas);
    resize();
    pausedRef.current = document.hidden;
    setPaused(document.hidden);

    const loop = (time: number) => {
      if (!active) return;
      try {
        resize();
        if (size.width <= 0 || size.height <= 0) {
          lastTime = 0;
          frame = requestAnimationFrame(loop);
          return;
        }
        const dt = lastTime ? Math.max(0, Math.min(0.1, (time - lastTime) / 1000)) : 0;
        lastTime = time;
        // A minimum logical width gives phones the same reaction distance.
        const width = Math.max(760, size.width);
        const scale = size.width / width;
        const height = size.height / scale;
        if (!pausedRef.current) {
          advanceSimulation(state, dt, width);
          hudTime += dt;
          if (hudTime >= 0.1 || state.ended) {
            setHud(snapshot(state));
            hudTime = 0;
          }
        }
        if (state.ended) endTime += dt;
        ctx.setTransform(size.dpr * scale, 0, 0, size.dpr * scale, 0, 0);
        drawScene(ctx, width, height, state, latest.current.outfit);
        if (state.ended && endTime >= 1.2 && !reported) {
          reported = true;
          latest.current.onEnd({
            mode: state.mode, collected: state.collected, trickScore: Math.floor(state.trickScore),
            distance: Math.floor(state.distance / METRE), completed: state.completed,
          });
          return;
        }
        frame = requestAnimationFrame(loop);
      } catch (cause) {
        console.error('Skating scene failed', cause);
        setError('The skating scene stopped unexpectedly. Please return and try again.');
      }
    };
    frame = requestAnimationFrame(loop);
    return () => {
      active = false;
      cancelAnimationFrame(frame);
      observer?.disconnect();
      releaseAll();
      window.removeEventListener('resize', resize);
      window.removeEventListener('keydown', keyDown);
      window.removeEventListener('keyup', keyUp);
      window.removeEventListener('blur', pause);
      document.removeEventListener('visibilitychange', visibility);
      jumpTarget.removeEventListener('pointerdown', pointerDown);
      jumpTarget.removeEventListener('pointerup', pointerUp);
      jumpTarget.removeEventListener('pointercancel', pointerUp);
      jumpTarget.removeEventListener('lostpointercapture', pointerUp);
      jumpTarget.removeEventListener('click', accessibleClick);
    };
  }, [mode]);

  return (
    <div className="fixed inset-0 overflow-hidden bg-rose-100">
      <canvas ref={canvasRef} className="block h-full w-full" aria-label="A pink roller skater and her Pokémon friend in Sunset Park" role="img" />
      <button ref={jumpRef} type="button" aria-label="Jump. Hold for a higher jump; release for a shorter hop."
        className="absolute inset-0 touch-none focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-inset focus-visible:ring-rose-500"
        disabled={!!error || hud.ended}>
        <span className="sr-only">Jump</span>
      </button>
      <div className="pointer-events-none absolute inset-x-0 top-0 p-3 sm:p-5 text-rose-950"
        style={{ paddingTop: 'max(12px, env(safe-area-inset-top))' }}>
        <div className="rounded-2xl bg-white/90 border border-white shadow-lg px-4 py-3 pr-24 max-w-2xl">
          <div className="font-black text-sm tracking-wider">{mode === 'COURSE' ? 'SUNSET COURSE' : 'ENDLESS SKATE'} <span className="font-medium text-rose-500">/ {hud.section}</span></div>
          <div className="flex flex-wrap gap-x-4 gap-y-1 mt-1 text-sm font-bold">
            <span>{hud.distance}m{mode === 'COURSE' ? ' / 450m' : ''}</span>
            <span>Pokéballs: {hud.collected}</span>
            <span>Score: {hud.points}</span>
            {hud.combo > 0 && <span className="text-rose-600">Combo ×{hud.multiplier}</span>}
            {hud.bestCombo > 1 && <span className="text-rose-400">Best combo: {hud.bestCombo}</span>}
          </div>
          <div className="mt-1 text-xs font-semibold">
            <span aria-label={`${hud.hearts} of 3 hearts`}>{'♥'.repeat(hud.hearts)}{'♡'.repeat(3 - hud.hearts)}</span>
            {' '}3 hearts · brief protection after a hit
          </div>
          {mode === 'COURSE' && (
            <div className="mt-2">
              <div role="progressbar" aria-label="Course progress" aria-valuemin={0} aria-valuemax={100} aria-valuenow={hud.progress}
                className="h-2 overflow-hidden rounded-full bg-rose-100">
                <div className="h-full rounded-full bg-rose-500 transition-[width]" style={{ width: `${hud.progress}%` }} />
              </div>
              <p className="mt-1 text-xs">{hud.progress}% · finish the {COURSE_SECONDS} second course</p>
            </div>
          )}
        </div>
      </div>
      <button type="button" onClick={onExit} aria-label="Exit skate and return to menu"
        className="absolute right-3 sm:right-5 rounded-xl bg-rose-950 text-white px-4 py-3 text-sm font-bold shadow-lg focus-visible:ring-4 focus-visible:ring-rose-400"
        style={{ top: 'max(12px, env(safe-area-inset-top))' }}>
        Exit
      </button>
      <div className="pointer-events-none absolute inset-x-0 bottom-0 p-4 text-center"
        style={{ paddingBottom: 'max(16px, env(safe-area-inset-bottom))' }}>
        <p className="inline-block rounded-2xl bg-white/90 px-4 py-3 text-sm font-bold text-rose-950 shadow-md">
          Tap / hold to jump · Space / ↑ / W · Tricks in the air: J / K / L<br />
          <span className="text-xs font-medium">Chain tricks and grinds for a combo. Land before the trick ends or you bail!</span>
        </p>
      </div>
      {!hud.ended && !error && (
        <div className="absolute right-3 sm:right-5 flex flex-col gap-2" style={{ bottom: 'max(104px, calc(env(safe-area-inset-bottom) + 96px))' }}>
          {TRICK_BUTTONS.map(({ kind, key }) => (
            <button key={kind} type="button" aria-label={`${TRICKS[kind].name} trick (${key})`}
              onPointerDown={event => { event.preventDefault(); event.stopPropagation(); trickRef.current(kind); }}
              onClick={event => { event.stopPropagation(); if (event.detail === 0) trickRef.current(kind); }}
              className={`touch-none rounded-2xl px-4 py-3 text-sm font-black shadow-lg transition focus-visible:ring-4 focus-visible:ring-rose-400 ${hud.airborne ? 'bg-rose-600 text-white scale-105' : 'bg-white/85 text-rose-900'}`}>
              {TRICKS[kind].name} <span className="opacity-70">{key}</span>
            </button>
          ))}
        </div>
      )}
      {hud.trick && !paused && !error && (
        <div className="pointer-events-none absolute inset-x-4 text-center" style={{ top: '22%' }}>
          <p className="inline-block rounded-2xl bg-white/90 px-4 py-2 text-lg font-black text-rose-600 shadow-md">{hud.trick}!</p>
        </div>
      )}
      {(hud.feedback || hud.grinding) && !paused && !error && (
        <div className="pointer-events-none absolute inset-x-4 text-center" style={{ top: '32%' }}>
          <p className="inline-block rounded-2xl bg-rose-950/90 px-5 py-3 text-white font-bold shadow-lg"
            role={hud.ended ? 'status' : undefined}>
            {hud.ended ? (hud.completed ? 'Course cleared! You made it ♡' : 'Out of hearts · nice skating!') :
              hud.grinding ? '✨ Grinding! +60 points / second' : hud.feedback}
          </p>
        </div>
      )}
      {paused && !error && (
        <div className="pointer-events-none absolute inset-x-4 top-1/3 text-center" role="status">
          <p className="inline-block rounded-2xl bg-white px-6 py-4 font-bold text-rose-950 shadow-lg">Paused · tap or press jump to resume</p>
        </div>
      )}
      {error && (
        <div className="absolute inset-x-4 top-1/3 rounded-2xl bg-white p-6 text-center text-rose-950 shadow-xl" role="alert">
          <p>{error}</p>
          <button type="button" onClick={onExit} className="mt-4 rounded-xl bg-rose-600 px-5 py-3 font-bold text-white">Return to menu</button>
        </div>
      )}
    </div>
  );
};

function drawScene(ctx: CanvasRenderingContext2D, width: number, height: number, state: Simulation, outfit: Outfit) {
  const ground = Math.max(300, height * 0.76);
  const time = state.elapsed * 1000;
  const playerX = width * 0.19;
  const screenX = (x: number) => x - state.distance + playerX;
  const section = Math.min(2, Math.floor(state.distance / SECTION_LENGTH) % 3);
  ctx.globalAlpha = 1;
  const sky = ctx.createLinearGradient(0, 0, 0, ground);
  sky.addColorStop(0, ['#a896d8', '#839acb', '#c088ba'][section]);
  sky.addColorStop(0.48, '#f6b6c8'); sky.addColorStop(1, '#ffdcab');
  ctx.fillStyle = sky; ctx.fillRect(0, 0, width, height);
  const sunY = ground * 0.46;
  const halo = ctx.createRadialGradient(width * 0.76, sunY, 15, width * 0.76, sunY, 155);
  halo.addColorStop(0, '#fff5d9'); halo.addColorStop(1, '#ffddd000');
  ctx.fillStyle = halo; ctx.beginPath(); ctx.arc(width * 0.76, sunY, 155, 0, 7); ctx.fill();
  ctx.fillStyle = '#fff1ca'; ctx.beginPath(); ctx.arc(width * 0.76, sunY, 53, 0, 7); ctx.fill();
  for (let i = 0; i < 7; i++) {
    const x = ((i * 240 - state.distance * 0.07) % (width + 280) + width + 280) % (width + 280) - 100;
    ctx.fillStyle = '#fff1f280';
    ctx.beginPath(); ctx.ellipse(x, ground * 0.23 + (i % 3) * 45, 70, 13, -0.03, 0, 7); ctx.fill();
  }
  for (let layer = 0; layer < 2; layer++) {
    ctx.fillStyle = layer ? '#ba93ba' : '#c8a4ca';
    ctx.beginPath(); ctx.moveTo(0, ground);
    for (let x = 0; x <= width + 20; x += 20) {
      ctx.lineTo(x, ground * (layer ? 0.79 : 0.68) + Math.sin((x + state.distance * (layer ? 0.14 : 0.08)) / 145) * 30);
    }
    ctx.lineTo(width, ground); ctx.fill();
  }
  ctx.fillStyle = section === 1 ? '#91b6c4' : '#d9b9ba'; ctx.fillRect(0, ground - 82, width, 90);
  if (section === 1) {
    ctx.strokeStyle = '#d9eef080'; ctx.lineWidth = 2;
    for (let row = 0; row < 4; row++) {
      for (let i = -1; i < width / 150 + 1; i++) {
        const x = i * 150 - (state.distance * 0.18 + row * 31) % 150;
        ctx.beginPath(); ctx.moveTo(x, ground - 76 + row * 14); ctx.lineTo(x + 60, ground - 76 + row * 14); ctx.stroke();
      }
    }
  }
  for (let i = -1; i < Math.ceil(width / 210) + 2; i++) {
    const x = i * 210 - (state.distance * 0.35 % 210);
    const y = ground - 105;
    ctx.fillStyle = '#8c657b'; ctx.fillRect(x - 5, y - 25, 10, 125);
    ctx.fillStyle = section === 2 ? '#eaa374' : i % 2 ? '#d783ab' : '#f1a4bd';
    for (const [dx, dy, r] of [[-22, -38, 33], [23, -43, 39], [0, -65, 37]]) {
      ctx.beginPath(); ctx.arc(x + dx, y + dy, r, 0, 7); ctx.fill();
    }
    ctx.fillStyle = '#ffd6e5'; ctx.beginPath(); ctx.arc(x - 14, y - 65, 19, 0, 7); ctx.fill();
    ctx.strokeStyle = '#986a82'; ctx.lineWidth = 4;
    ctx.beginPath(); ctx.moveTo(x + 70, ground - 45); ctx.lineTo(x + 145, ground - 45); ctx.stroke();
    for (const bx of [x + 78, x + 137]) {
      ctx.beginPath(); ctx.moveTo(bx, ground - 44); ctx.lineTo(bx, ground - 15); ctx.stroke();
    }
  }
  ctx.strokeStyle = '#a57590'; ctx.lineWidth = 2;
  ctx.beginPath(); ctx.moveTo(0, ground - 20); ctx.lineTo(width, ground - 20); ctx.stroke();
  for (let x = -state.distance * 0.5 % 90; x < width; x += 90) {
    ctx.beginPath(); ctx.moveTo(x, ground - 20); ctx.lineTo(x, ground - 65); ctx.stroke();
  }
  const pavement = ctx.createLinearGradient(0, ground, 0, height);
  pavement.addColorStop(0, '#f8d3d5'); pavement.addColorStop(1, '#e0a8bc');
  ctx.fillStyle = pavement; ctx.fillRect(0, ground, width, height - ground);
  if (section === 1) {
    ctx.strokeStyle = '#ba869348'; ctx.lineWidth = 2;
    for (let x = -state.distance % 70; x < width; x += 70) {
      ctx.beginPath(); ctx.moveTo(x, ground); ctx.lineTo(x - 35, height); ctx.stroke();
    }
  }
  ctx.fillStyle = '#fff0e3'; ctx.fillRect(0, ground - 4, width, 5);
  ctx.strokeStyle = '#fff1f2a0'; ctx.lineWidth = 3; ctx.setLineDash([45, 40]);
  ctx.lineDashOffset = state.distance;
  ctx.beginPath(); ctx.moveTo(0, ground + 53); ctx.lineTo(width, ground + 53); ctx.stroke(); ctx.setLineDash([]);
  for (const item of state.items) {
    if (item.taken && item.kind !== 'kicker') continue;
    const x = screenX(item.x);
    if (x + item.width < -50 || x > width + 50) continue;
    if (item.kind === 'gap') {
      const depth = height - ground;
      const pit = ctx.createLinearGradient(0, ground, 0, height);
      pit.addColorStop(0, '#4a1d34'); pit.addColorStop(1, '#1f0a17');
      ctx.fillStyle = pit; ctx.fillRect(x, ground - 4, item.width, depth + 4);
      ctx.fillStyle = '#d8a3b4';
      ctx.fillRect(x - 6, ground - 5, 6, depth + 5); ctx.fillRect(x + item.width, ground - 5, 6, depth + 5);
      ctx.fillStyle = '#fff1f2'; ctx.font = 'bold 12px system-ui'; ctx.textAlign = 'center';
      ctx.fillText('GAP · JUMP!', x + item.width / 2, ground - 16);
      continue;
    }
    ctx.fillStyle = '#8c416526';
    ctx.beginPath(); ctx.ellipse(x + item.width / 2, ground + 7, item.width / 2 + 12, 6, 0, 0, 7); ctx.fill();
    if (item.kind === 'stairs') {
      const steps = 6, top = 52;
      for (let i = 0; i < steps; i++) {
        const sx = x + (item.width / steps) * i;
        const sh = top * (1 - i / steps);
        ctx.fillStyle = i % 2 ? '#e7c3cf' : '#efd2db';
        ctx.fillRect(sx, ground - sh, item.width / steps + 1, sh);
        ctx.fillStyle = '#fff7f9'; ctx.fillRect(sx, ground - sh, item.width / steps + 1, 3);
      }
      ctx.strokeStyle = '#b98a9a'; ctx.lineWidth = 2; ctx.strokeRect(x, ground - top, item.width, top);
      ctx.strokeStyle = '#8c557b'; ctx.lineWidth = 6; ctx.lineCap = 'round';
      for (const offset of [18, item.width / 2, item.width - 18]) {
        const railTop = ground + railHeight(item, item.x + offset);
        const base = ground - top * (1 - offset / item.width);
        ctx.beginPath(); ctx.moveTo(x + offset, railTop + 3); ctx.lineTo(x + offset, base); ctx.stroke();
      }
      const y0 = ground + item.y, y1 = y0 + (item.rise ?? 0);
      ctx.strokeStyle = '#7b416a'; ctx.lineWidth = 10;
      ctx.beginPath(); ctx.moveTo(x, y0 + 3); ctx.lineTo(x + item.width, y1 + 3); ctx.stroke();
      ctx.strokeStyle = '#f9a8d4'; ctx.lineWidth = 7;
      ctx.beginPath(); ctx.moveTo(x, y0); ctx.lineTo(x + item.width, y1); ctx.stroke();
      ctx.fillStyle = '#6b3757'; ctx.font = 'bold 12px system-ui'; ctx.textAlign = 'center';
      ctx.fillText('HANDRAIL', x + item.width / 2, y0 - 18);
    } else if (item.kind === 'kicker') {
      const h = -item.y;
      const ramp = ctx.createLinearGradient(x, ground, x + item.width, ground - h);
      ramp.addColorStop(0, '#f9a8d4'); ramp.addColorStop(1, '#db2777');
      ctx.fillStyle = ramp;
      ctx.beginPath(); ctx.moveTo(x, ground); ctx.quadraticCurveTo(x + item.width * 0.7, ground - 4, x + item.width, ground - h);
      ctx.lineTo(x + item.width, ground); ctx.closePath(); ctx.fill();
      ctx.strokeStyle = '#831843'; ctx.lineWidth = 3;
      ctx.beginPath(); ctx.moveTo(x + item.width, ground - h); ctx.lineTo(x + item.width, ground); ctx.stroke();
      ctx.strokeStyle = '#fde7f3'; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.moveTo(x + 6, ground - 2); ctx.quadraticCurveTo(x + item.width * 0.7, ground - 6, x + item.width - 2, ground - h + 2); ctx.stroke();
      ctx.fillStyle = '#6b3757'; ctx.font = 'bold 12px system-ui'; ctx.textAlign = 'center';
      ctx.fillText('KICKER ↗', x + item.width / 2, ground - h - 14);
    } else if (item.kind === 'rail') {
      const y = ground + item.y;
      const endY = y + (item.rise ?? 0);
      ctx.strokeStyle = '#8c557b'; ctx.lineWidth = 8; ctx.lineCap = 'round';
      for (const offset of [26, item.width / 2, item.width - 26]) {
        const top = ground + railHeight(item, item.x + offset);
        ctx.beginPath(); ctx.moveTo(x + offset, top + 4); ctx.lineTo(x + offset, ground - 3); ctx.stroke();
        ctx.fillStyle = '#986482'; ctx.fillRect(x + offset - 12, ground - 4, 24, 5);
      }
      ctx.strokeStyle = '#7b416a'; ctx.lineWidth = 12;
      ctx.beginPath(); ctx.moveTo(x, y + 4); ctx.lineTo(x + item.width, endY + 4); ctx.stroke();
      ctx.strokeStyle = '#f472b6'; ctx.lineWidth = 9;
      ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x + item.width, endY); ctx.stroke();
      ctx.strokeStyle = '#ffe7f4'; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.moveTo(x, y - 3); ctx.lineTo(x + item.width, endY - 3); ctx.stroke();
      ctx.fillStyle = '#6b3757'; ctx.font = 'bold 12px system-ui'; ctx.textAlign = 'center';
      ctx.fillText('JUMP → LAND → GRIND', x + item.width / 2, y - 21);
    } else if (item.kind === 'ball') {
      drawBall(ctx, x + item.width / 2, ground + item.y);
    } else if (item.kind === 'barrier') {
      const y = ground + item.y;
      ctx.strokeStyle = '#74445a'; ctx.lineWidth = 5;
      for (const dx of [9, item.width - 9]) {
        ctx.beginPath(); ctx.moveTo(x + dx, y + 8); ctx.lineTo(x + dx - 5, ground - 1); ctx.stroke();
      }
      ctx.fillStyle = '#fff1d1'; ctx.strokeStyle = '#af5d50'; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.roundRect(x, y, item.width, 25, 4); ctx.fill(); ctx.stroke();
      ctx.save();
      ctx.clip();
      ctx.strokeStyle = '#f67569'; ctx.lineWidth = 13;
      for (let dx = -10; dx <= item.width + 20; dx += 26) {
        ctx.beginPath(); ctx.moveTo(x + dx, y + 25); ctx.lineTo(x + dx + 20, y); ctx.stroke();
      }
      ctx.restore();
    } else {
      ctx.fillStyle = '#fb923c';
      ctx.beginPath(); ctx.moveTo(x + 16, ground + item.y); ctx.lineTo(x + 32, ground);
      ctx.lineTo(x, ground); ctx.closePath(); ctx.fill();
      ctx.strokeStyle = '#fff7ed'; ctx.lineWidth = 5;
      ctx.beginPath(); ctx.moveTo(x + 10, ground - 20); ctx.lineTo(x + 22, ground - 20); ctx.stroke();
      ctx.fillStyle = '#c95f43'; ctx.fillRect(x - 3, ground - 4, 38, 6);
    }
  }
  if (state.mode === 'COURSE') {
    const finishX = screenX(COURSE_LENGTH);
    if (finishX < width + 150) {
      ctx.fillStyle = '#7b416a'; ctx.fillRect(finishX - 5, ground - 175, 7, 178);
      ctx.fillStyle = '#fff1f2'; ctx.fillRect(finishX, ground - 175, 145, 42);
      ctx.fillStyle = '#be185d'; ctx.font = 'bold 17px system-ui'; ctx.textAlign = 'center';
      ctx.fillText('FINISH ♡', finishX + 72, ground - 148);
      for (let row = 0; row < 4; row++) for (let col = 0; col < 2; col++) {
        ctx.fillStyle = (row + col) % 2 ? '#fff1f2' : '#9d174d';
        ctx.fillRect(finishX + col * 10, ground + row * 12, 10, 12);
      }
    }
  }
  ctx.fillStyle = '#8c41652e';
  ctx.beginPath(); ctx.ellipse(playerX, ground + 7, Math.max(17, 31 + state.foot * 0.07), 7, 0, 0, 7); ctx.fill();
  const rail = state.items.find(item => item.id === state.rail);
  const footY = ground + state.foot;
  if (state.grounded && !rail) {
    ctx.save(); ctx.strokeStyle = '#ffffff90'; ctx.lineWidth = 2;
    for (let i = 0; i < 5; i++) {
      const phase = (state.elapsed * 3 + i * 0.21) % 1;
      ctx.globalAlpha = 1 - phase;
      ctx.beginPath(); ctx.moveTo(playerX - 28 - phase * 65, ground + 3 + i * 2);
      ctx.lineTo(playerX - 45 - phase * 65, ground + 3 + i * 2); ctx.stroke();
    }
    ctx.restore();
  }
  if (rail) {
    ctx.save(); ctx.strokeStyle = '#fff4b3'; ctx.lineWidth = 2; ctx.shadowColor = '#fbbf24'; ctx.shadowBlur = 10;
    for (let i = 0; i < 11; i++) {
      const age = (state.elapsed * 5 + i * 0.13) % 1;
      const x = playerX - 10 - age * 90;
      const y = footY + age * age * 22 - Math.sin(i * 2) * age * 15;
      ctx.globalAlpha = 1 - age;
      ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x - 7, y + 3); ctx.stroke();
    }
    ctx.restore();
  }
  ctx.save();
  if (state.invulnerable > 0) ctx.globalAlpha = 0.65 + Math.sin(time * 0.025) * 0.25;
  drawSkater(ctx, playerX, footY, time, outfit, !!rail, !state.grounded,
    rail ? (rail.rise ?? 0) / rail.width : 0, state.velocity, state.landingPulse,
    state.airBlend, state.grindBlend, { rollTime: state.rollTime, airTime: state.airTime, grindTime: state.grindTime },
    state.trick, trickProgress(state));
  ctx.restore();
  drawCompanion(ctx, playerX - 58, footY - 105, time,
    state.invulnerable > 0 ? 'worried' : rail ? 'grind' : state.feedback.startsWith('+1') && state.feedbackTime > 0 ? 'happy' : 'idle',
    outfit.companion);
}

export default GameView;
