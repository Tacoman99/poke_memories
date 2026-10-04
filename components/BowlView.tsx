import { useEffect, useRef, useState } from 'react';
import type { GameResult, Outfit } from '../types';
import {
  advanceBowl, armLipHandstand, BOWL_COPING, BOWL_DURATION, BOWL_GOALS, BOWL_HALF, BOWL_TRICKS, bowlPoint, bowlPose, bowlTrickProgress,
  createBowl, goalTier, LIP_HANDSTAND, nextGoal, setPump, startBowlTrick, timeToCoping,
} from './bowlplay';
import type { BowlState, BowlTrick } from './bowlplay';
import { drawCompanion, drawSkater } from './gameArt';

interface BowlViewProps {
  outfit: Outfit;
  onEnd: (result: GameResult) => void;
  onExit: () => void;
}

const TRICK_KEYS: Record<string, BowlTrick> = { KeyJ: 'grab', KeyK: 'kick', KeyL: 'spin', KeyI: 'flip' };
const TRICK_BUTTONS: { kind: BowlTrick; key: string }[] = [
  { kind: 'grab', key: 'J' }, { kind: 'kick', key: 'K' }, { kind: 'spin', key: 'L' }, { kind: 'flip', key: 'I' },
];
const TIER_NAMES = ['None yet', ...BOWL_GOALS.map(goal => goal.name)];

const snapshot = (state: BowlState) => {
  const goal = nextGoal(state.score);
  return {
    timeLeft: Math.ceil(state.timeLeft),
    score: Math.floor(state.score),
    tier: goalTier(state.score),
    goal: goal ? `${goal.name} at ${goal.score}` : 'Gold reached ♡',
    goalProgress: goal ? Math.min(100, Math.floor(state.score / goal.score * 100)) : 100,
    combo: state.combo,
    bestCombo: state.bestCombo,
    streak: state.streak,
    bestStreak: state.bestStreak,
    perfects: state.perfects,
    gradeFlash: state.gradeFlashTime > 0 ? state.gradeFlash : null,
    lipArmed: state.lipArmed === 'perfect' || state.lipArmed === 'good',
    fallen: state.fallen > 0,
    bruises: state.bruises,
    airChain: state.airChain,
    queued: state.queuedTrick ? BOWL_TRICKS[state.queuedTrick].name : '',
    trick: state.trick ? BOWL_TRICKS[state.trick].name : '',
    feedback: state.feedbackTime > 0 ? state.feedback : '',
    airborne: state.airborne,
    pumping: state.pumping,
    ended: state.ended,
  };
};

const BowlView: React.FC<BowlViewProps> = props => {
  const { onExit } = props;
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const pumpRef = useRef<HTMLButtonElement>(null);
  const trickRef = useRef<(kind: BowlTrick) => void>(() => {});
  const lipRef = useRef<() => void>(() => {});
  const latest = useRef(props);
  latest.current = props;
  const [hud, setHud] = useState(() => snapshot(createBowl()));
  const [error, setError] = useState('');
  const [paused, setPaused] = useState(false);
  const pausedRef = useRef(false);

  useEffect(() => {
    const canvas = canvasRef.current;
    const pumpTarget = pumpRef.current;
    if (!canvas || !pumpTarget) {
      setError('The bowl couldn’t start. Please return and try again.');
      return;
    }
    const ctx = canvas.getContext('2d');
    if (!ctx) {
      setError('Your browser couldn’t open the skating canvas. Please try another browser.');
      return;
    }
    const state = createBowl();
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
    const resume = () => {
      if (!pausedRef.current) return;
      pausedRef.current = false;
      setPaused(false);
      lastTime = 0;
    };
    const inputDown = (id: string) => {
      if (inputs.has(id) || state.ended) return;
      resume();
      inputs.add(id);
      setPump(state, true);
    };
    const inputUp = (id: string) => {
      inputs.delete(id);
      if (inputs.size === 0) setPump(state, false);
    };
    const releaseAll = () => { inputs.clear(); setPump(state, false); };
    const pause = () => {
      releaseAll();
      pausedRef.current = true;
      setPaused(true);
      lastTime = 0;
    };
    const visibility = () => { if (document.hidden) pause(); };
    const trick = (kind: BowlTrick) => {
      if (state.ended) return;
      resume();
      startBowlTrick(state, kind);
    };
    trickRef.current = trick;
    const lip = () => {
      if (state.ended) return;
      resume();
      armLipHandstand(state);
    };
    lipRef.current = lip;
    const key = (event: KeyboardEvent) => ['Space', 'ArrowUp', 'KeyW'].includes(event.code);
    const keyDown = (event: KeyboardEvent) => {
      if (event.code === 'KeyU') {
        if (!event.repeat && !event.metaKey && !event.ctrlKey && !event.altKey) lip();
        return;
      }
      const trickKind = TRICK_KEYS[event.code];
      if (trickKind) {
        if (!event.repeat && !event.metaKey && !event.ctrlKey && !event.altKey) trick(trickKind);
        return;
      }
      if (!key(event)) return;
      const target = event.target;
      if (target instanceof HTMLElement && target.closest('button, input, textarea, select, a, [contenteditable="true"]') &&
          target !== pumpTarget) return;
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
      pumpTarget.setPointerCapture(event.pointerId);
      inputDown(`pointer:${event.pointerId}`);
    };
    const pointerUp = (event: PointerEvent) => inputUp(`pointer:${event.pointerId}`);
    const accessibleClick = (event: MouseEvent) => {
      if (event.detail !== 0) return;
      resume();
    };
    pumpTarget.addEventListener('pointerdown', pointerDown);
    pumpTarget.addEventListener('pointerup', pointerUp);
    pumpTarget.addEventListener('pointercancel', pointerUp);
    pumpTarget.addEventListener('lostpointercapture', pointerUp);
    pumpTarget.addEventListener('click', accessibleClick);
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
        if (!pausedRef.current) {
          advanceBowl(state, dt);
          hudTime += dt;
          if (hudTime >= 0.1 || state.ended) {
            setHud(snapshot(state));
            hudTime = 0;
          }
        }
        if (state.ended) endTime += dt;
        ctx.setTransform(size.dpr, 0, 0, size.dpr, 0, 0);
        drawBowl(ctx, size.width, size.height, state, latest.current.outfit);
        if (state.ended && endTime >= 1.2 && !reported) {
          reported = true;
          const tier = goalTier(state.score);
          latest.current.onEnd({
            mode: 'BOWL', collected: tier, trickScore: Math.floor(state.score), distance: 0, completed: tier > 0,
          });
          return;
        }
        frame = requestAnimationFrame(loop);
      } catch (cause) {
        console.error('Bowl scene failed', cause);
        setError('The bowl stopped unexpectedly. Please return and try again.');
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
      pumpTarget.removeEventListener('pointerdown', pointerDown);
      pumpTarget.removeEventListener('pointerup', pointerUp);
      pumpTarget.removeEventListener('pointercancel', pointerUp);
      pumpTarget.removeEventListener('lostpointercapture', pointerUp);
      pumpTarget.removeEventListener('click', accessibleClick);
    };
  }, []);

  return (
    <div className="fixed inset-0 overflow-hidden bg-rose-100">
      <canvas ref={canvasRef} className="block h-full w-full" aria-label="A pink roller skater carving a skate bowl" role="img" />
      <button ref={pumpRef} type="button" aria-label="Pump. Hold to build speed and air out of the bowl."
        className="absolute inset-0 touch-none focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-inset focus-visible:ring-rose-500"
        disabled={!!error || hud.ended}>
        <span className="sr-only">Pump</span>
      </button>
      <div className="pointer-events-none absolute inset-x-0 top-0 p-3 sm:p-5 text-rose-950"
        style={{ paddingTop: 'max(12px, env(safe-area-inset-top))' }}>
        <div className="rounded-2xl bg-white/90 border border-white shadow-lg px-4 py-3 pr-24 max-w-2xl">
          <div className="font-black text-sm tracking-wider">ROSE BOWL <span className="font-medium text-rose-500">/ {hud.timeLeft}s left</span></div>
          <div className="flex flex-wrap gap-x-4 gap-y-1 mt-1 text-sm font-bold">
            <span>Score: {hud.score}</span>
            <span>Medal: {TIER_NAMES[hud.tier]}</span>
            {hud.combo > 0 && <span className="text-rose-600">Combo ×{Math.min(5, hud.combo)}</span>}
            {hud.bestCombo > 1 && <span className="text-rose-400">Best combo: {hud.bestCombo}</span>}
            {hud.streak > 0 && <span className="text-emerald-600">Perfect streak ×{hud.streak}</span>}
            <span className="text-emerald-700">Perfects: {hud.perfects}</span>
            {hud.bruises > 0 && <span className="text-purple-700">Bruises: {hud.bruises}</span>}
          </div>
          <div className="mt-2">
            <div role="progressbar" aria-label="Progress to next medal" aria-valuemin={0} aria-valuemax={100} aria-valuenow={hud.goalProgress}
              className="h-2 overflow-hidden rounded-full bg-rose-100">
              <div className="h-full rounded-full bg-rose-500 transition-[width]" style={{ width: `${hud.goalProgress}%` }} />
            </div>
            <p className="mt-1 text-xs">Next: {hud.goal} · {BOWL_DURATION} second run</p>
          </div>
        </div>
      </div>
      <button type="button" onClick={onExit} aria-label="Exit bowl and return to menu"
        className="absolute right-3 sm:right-5 rounded-xl bg-rose-950 text-white px-4 py-3 text-sm font-bold shadow-lg focus-visible:ring-4 focus-visible:ring-rose-400"
        style={{ top: 'max(12px, env(safe-area-inset-top))' }}>
        Exit
      </button>
      <div className="pointer-events-none absolute inset-x-0 bottom-0 p-4 text-center"
        style={{ paddingBottom: 'max(16px, env(safe-area-inset-bottom))' }}>
        <p className={`inline-block rounded-2xl px-4 py-3 text-sm font-bold shadow-md transition ${hud.pumping ? 'bg-rose-600 text-white' : 'bg-white/90 text-rose-950'}`}>
          Hold Pump to build speed · Space / ↑ / W · Tricks in the air: J / K / L · Backflip I · Chain several per air<br />
          <span className="text-xs font-medium">Handstand U: press right as she reaches the lip · Mistime it or land mid trick and she falls</span>
        </p>
      </div>
      {!hud.ended && !error && (
        <div className="absolute right-3 sm:right-5 flex flex-col gap-2" style={{ bottom: 'max(104px, calc(env(safe-area-inset-bottom) + 96px))' }}>
          {TRICK_BUTTONS.map(({ kind, key }) => (
            <button key={kind} type="button" aria-label={`${BOWL_TRICKS[kind].name} trick (${key})`}
              onPointerDown={event => { event.preventDefault(); event.stopPropagation(); trickRef.current(kind); }}
              onClick={event => { event.stopPropagation(); if (event.detail === 0) trickRef.current(kind); }}
              className={`touch-none rounded-2xl px-4 py-3 text-sm font-black shadow-lg transition focus-visible:ring-4 focus-visible:ring-rose-400 ${hud.airborne ? 'bg-rose-600 text-white scale-105' : 'bg-white/85 text-rose-900'}`}>
              {BOWL_TRICKS[kind].name} <span className="opacity-70">{key}</span>
            </button>
          ))}
          <button type="button" aria-label={`${LIP_HANDSTAND.name} (U), press as she reaches the lip`}
            onPointerDown={event => { event.preventDefault(); event.stopPropagation(); lipRef.current(); }}
            onClick={event => { event.stopPropagation(); if (event.detail === 0) lipRef.current(); }}
            className={`touch-none rounded-2xl px-4 py-3 text-sm font-black shadow-lg transition focus-visible:ring-4 focus-visible:ring-emerald-400 ${hud.lipArmed ? 'bg-emerald-500 text-white scale-105' : 'bg-white/85 text-emerald-800'}`}>
            {LIP_HANDSTAND.name} <span className="opacity-70">U</span>
          </button>
        </div>
      )}
      {hud.gradeFlash && !paused && !error && (
        <div className="pointer-events-none absolute inset-x-4 text-center" style={{ top: '13%' }} aria-live="polite">
          <p className={`inline-block text-4xl sm:text-5xl font-black tracking-wider drop-shadow-lg ${hud.gradeFlash === 'perfect' ? 'text-emerald-400' : hud.gradeFlash === 'good' ? 'text-amber-400' : 'text-rose-500'}`}
            style={{ WebkitTextStroke: '2px white' }}>
            {hud.gradeFlash === 'perfect' ? 'PERFECT!' : hud.gradeFlash === 'good' ? 'GOOD' : 'MISS'}
          </p>
        </div>
      )}
      {hud.trick && !paused && !error && (
        <div className="pointer-events-none absolute inset-x-4 text-center" style={{ top: '22%' }}>
          <p className="inline-block rounded-2xl bg-white/90 px-4 py-2 text-lg font-black text-rose-600 shadow-md">{hud.trick}!{hud.queued && <span className="text-rose-400"> → {hud.queued}</span>}{hud.airChain > 0 && <span className="text-amber-500"> · Chain ×{hud.airChain + 1}</span>}</p>
        </div>
      )}
      {(hud.feedback || hud.ended) && !paused && !error && (
        <div className="pointer-events-none absolute inset-x-4 text-center" style={{ top: '32%' }}>
          <p className="inline-block rounded-2xl bg-rose-950/90 px-5 py-3 text-white font-bold shadow-lg"
            role={hud.ended ? 'status' : undefined}>
            {hud.ended ? (hud.tier ? `Time! ${TIER_NAMES[hud.tier]} medal ♡` : 'Time! Nice session ♡') : hud.feedback}
          </p>
        </div>
      )}
      {paused && !error && (
        <div className="pointer-events-none absolute inset-x-4 top-1/3 text-center" role="status">
          <p className="inline-block rounded-2xl bg-white px-6 py-4 font-bold text-rose-950 shadow-lg">Paused · tap or press pump to resume</p>
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

function traceProfile(ctx: CanvasRenderingContext2D, toX: (x: number) => number, toY: (h: number) => number) {
  for (let i = 0; i <= 80; i++) {
    const point = bowlPoint(-BOWL_HALF + (2 * BOWL_HALF * i) / 80);
    if (i === 0) ctx.moveTo(toX(point.x), toY(point.h));
    else ctx.lineTo(toX(point.x), toY(point.h));
  }
}

function drawBowl(ctx: CanvasRenderingContext2D, width: number, height: number, state: BowlState, outfit: Outfit) {
  const time = state.elapsed * 1000;
  // Fit the bowl plus headroom for big airs, keeping the skater a readable size.
  const worldWidth = BOWL_COPING.x * 2 + 220;
  const worldHeight = BOWL_COPING.h + 380;
  const k = Math.min(width / worldWidth, (height * 0.8) / worldHeight, 1.6);
  const cx = width / 2;
  const floorY = height * 0.9;
  const toX = (x: number) => cx + x * k;
  const toY = (h: number) => floorY - h * k;
  const deckY = toY(BOWL_COPING.h);

  const sky = ctx.createLinearGradient(0, 0, 0, deckY);
  sky.addColorStop(0, '#9b8fd6'); sky.addColorStop(0.55, '#f6b6c8'); sky.addColorStop(1, '#ffdcab');
  ctx.fillStyle = sky; ctx.fillRect(0, 0, width, height);
  const sunX = width * 0.78, sunY = deckY * 0.55;
  const halo = ctx.createRadialGradient(sunX, sunY, 10, sunX, sunY, 140);
  halo.addColorStop(0, '#fff5d9'); halo.addColorStop(1, '#ffddd000');
  ctx.fillStyle = halo; ctx.beginPath(); ctx.arc(sunX, sunY, 140, 0, 7); ctx.fill();
  ctx.fillStyle = '#fff1ca'; ctx.beginPath(); ctx.arc(sunX, sunY, 45, 0, 7); ctx.fill();
  for (let i = 0; i < 5; i++) {
    const x = ((i * 260 + state.elapsed * 8) % (width + 260)) - 120;
    ctx.fillStyle = '#fff1f280';
    ctx.beginPath(); ctx.ellipse(x, deckY * 0.25 + (i % 3) * 40, 70, 12, 0, 0, 7); ctx.fill();
  }
  // Distant palms and fence for depth.
  ctx.fillStyle = '#c8a4ca';
  for (let i = 0; i < 9; i++) {
    const x = (i + 0.5) * width / 9;
    ctx.fillRect(x - 3, deckY - 90 - (i % 3) * 18, 6, 90 + (i % 3) * 18);
    ctx.beginPath(); ctx.ellipse(x, deckY - 92 - (i % 3) * 18, 26, 9, 0.4, 0, 7); ctx.ellipse(x, deckY - 92 - (i % 3) * 18, 26, 9, -0.4, 0, 7); ctx.fill();
  }

  // Deck and concrete mass around the bowl.
  ctx.fillStyle = '#efd9df';
  ctx.fillRect(0, deckY, width, height - deckY);
  ctx.fillStyle = '#e4c6cf';
  ctx.fillRect(0, deckY, width, 6);
  for (let x = toX(BOWL_COPING.x) + 50; x < width; x += 70) ctx.fillRect(x, deckY, 2, height - deckY);
  for (let x = toX(-BOWL_COPING.x) - 50; x > 0; x -= 70) ctx.fillRect(x, deckY, 2, height - deckY);

  // Bowl interior.
  ctx.beginPath();
  ctx.moveTo(toX(-BOWL_COPING.x), deckY);
  traceProfile(ctx, toX, toY);
  ctx.lineTo(toX(BOWL_COPING.x), deckY);
  ctx.closePath();
  const inside = ctx.createLinearGradient(0, deckY, 0, floorY);
  inside.addColorStop(0, '#c9b6c4'); inside.addColorStop(1, '#f4e3e8');
  ctx.fillStyle = inside; ctx.fill();
  // Heart painted on the flat bottom.
  ctx.save();
  ctx.translate(cx, floorY - 1);
  ctx.scale(k, k * 0.22);
  ctx.fillStyle = '#fb718566';
  ctx.beginPath();
  ctx.moveTo(0, 30);
  ctx.bezierCurveTo(-70, -10, -40, -60, 0, -28);
  ctx.bezierCurveTo(40, -60, 70, -10, 0, 30);
  ctx.fill();
  ctx.restore();
  // Surface line and shading band.
  ctx.beginPath(); traceProfile(ctx, toX, toY);
  ctx.strokeStyle = '#a98a9c'; ctx.lineWidth = 4; ctx.stroke();
  ctx.beginPath(); traceProfile(ctx, toX, y => toY(y) + 10);
  ctx.strokeStyle = '#ffffff55'; ctx.lineWidth = 2; ctx.stroke();
  // Pool tiles and coping.
  for (const side of [-1, 1]) {
    const x = toX(side * BOWL_COPING.x);
    ctx.fillStyle = '#7dd3fc';
    for (let i = 0; i < 4; i++) ctx.fillRect(x - (side > 0 ? 8 : 0) , deckY + 6 + i * 9, 8, 7);
    const shine = ctx.createLinearGradient(x, deckY - 6, x, deckY + 6);
    shine.addColorStop(0, '#f8fafc'); shine.addColorStop(1, '#94a3b8');
    ctx.fillStyle = shine;
    ctx.beginPath(); ctx.arc(x, deckY, 6 * Math.max(0.8, k), 0, 7); ctx.fill();
  }
  // Timing rings at the lip: yellow is Good, green is Perfect.
  const eta = timeToCoping(state);
  const heading = state.s < 0 ? -1 : 1;
  for (const side of [-1, 1]) {
    const x = toX(side * BOWL_COPING.x);
    const active = side === heading && eta <= LIP_HANDSTAND.goodWindow + 0.25;
    const ring = (radius: number, color: string, on: boolean) => {
      ctx.save();
      ctx.globalAlpha = on ? 0.95 : 0.3;
      ctx.strokeStyle = color;
      ctx.lineWidth = on ? 4 : 2;
      ctx.beginPath(); ctx.arc(x, deckY, radius * Math.max(0.8, k), 0, 7); ctx.stroke();
      ctx.restore();
    };
    ring(26, '#fbbf24', active && eta <= LIP_HANDSTAND.goodWindow);
    ring(15, '#34d399', active && eta <= LIP_HANDSTAND.perfectWindow);
    if (state.lipArmed && side === heading) ring(34, state.lipArmed === 'perfect' ? '#34d399' : '#fbbf24', true);
  }

  // Skater.
  const pose = bowlPose(state);
  const footX = toX(pose.x), footY = toY(pose.h);
  if (!state.airborne) {
    ctx.save();
    ctx.globalAlpha = 0.18;
    ctx.fillStyle = '#4c0519';
    ctx.beginPath(); ctx.ellipse(footX, footY + 2, 26 * k, 5 * k, pose.angle, 0, 7); ctx.fill();
    ctx.restore();
  }
  if (state.pumping && !state.airborne) {
    ctx.save();
    ctx.strokeStyle = '#fb7185';
    ctx.lineWidth = 2;
    for (let i = 0; i < 3; i++) {
      const age = (state.elapsed * 4 + i / 3) % 1;
      ctx.globalAlpha = 1 - age;
      ctx.beginPath(); ctx.arc(footX, footY, (14 + age * 30) * k, 0, 7); ctx.stroke();
    }
    ctx.restore();
  }
  if (state.landingPulse > 0) {
    ctx.save();
    ctx.globalAlpha = state.landingPulse;
    ctx.fillStyle = '#fda4af';
    for (let i = 0; i < 8; i++) {
      const a = (i / 8) * Math.PI * 2;
      const r = (1 - state.landingPulse) * 40 * k;
      ctx.beginPath(); ctx.arc(footX + Math.cos(a) * r, footY + Math.sin(a) * r * 0.5, 3, 0, 7); ctx.fill();
    }
    ctx.restore();
  }
  ctx.save();
  ctx.translate(footX, footY);
  const fallT = state.fallen > 0 ? Math.min(1, state.fallen / 0.3) : 0;
  ctx.rotate(pose.angle + fallT * 1.3 * pose.facing);
  ctx.scale(k * pose.facing, k);
  const clock = state.elapsed;
  const stalled = state.stall > 0;
  drawSkater(ctx, 0, 0, time, outfit, false, state.airborne || stalled, 0, state.airborne ? -state.airV : Math.abs(state.v) * 0.2,
    state.landingPulse, state.airborne || stalled ? 1 : 0, 0, { rollTime: clock, airTime: clock, grindTime: clock },
    stalled ? 'handstand' : state.trick, stalled ? 0.5 : bowlTrickProgress(state));
  if (state.bruises > 0) {
    ctx.fillStyle = 'rgba(126, 34, 206, 0.45)';
    const marks: [number, number][] = [[-6, -40], [7, -22], [-4, -62], [5, -52]];
    marks.slice(0, Math.min(4, state.bruises)).forEach(([bx, by]) => {
      ctx.beginPath(); ctx.ellipse(bx, by, 3.2, 2.2, 0.4, 0, 7); ctx.fill();
    });
  }
  ctx.restore();

  drawCompanion(ctx, toX(BOWL_COPING.x) + 70 * k, deckY - 70, time,
    state.bails > 0 && state.feedbackTime > 0 && state.feedback.toLowerCase().includes('bail') ? 'worried' :
      state.combo > 1 ? 'happy' : 'idle', outfit.companion);
}

export default BowlView;
