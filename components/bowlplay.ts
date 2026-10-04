// Self-contained bowl simulation. The bowl is a flat bottom with a circular
// transition on each side. Position is arc length along that profile, so the
// skater rolls back and forth like a pendulum and launches straight up past
// the coping when she carries enough speed.

export const BOWL_STEP = 1 / 120;
export const BOWL_GRAVITY = 1800;
export const BOWL_FLAT = 150;
export const BOWL_RADIUS = 175;
export const BOWL_MAX_ANGLE = 1.45;
export const BOWL_DURATION = 60;
const PUMP_ACCEL = 420;
const DRAG = 0.1;
const ROLL_DECEL = 18;
const MAX_LAUNCH = 980;
const LANDING_KEEP = 0.97;
const BAIL_KEEP = 0.45;
const FALL_TIME = 0.9;
const CHAIN_BONUS = 100;
const LIP_COMMIT = 1;
const COMBO_WINDOW = 2.2;
const WALL_ARC = BOWL_RADIUS * BOWL_MAX_ANGLE;
export const BOWL_HALF = BOWL_FLAT + WALL_ARC;

export type BowlTrick = 'grab' | 'kick' | 'spin' | 'flip';
export const BOWL_TRICKS: Record<BowlTrick, { name: string; points: number; duration: number }> = {
  grab: { name: 'Pink Grab', points: 120, duration: 0.4 },
  kick: { name: 'Heart Kick', points: 150, duration: 0.35 },
  spin: { name: '360 Twirl', points: 220, duration: 0.5 },
  flip: { name: 'Backflip', points: 400, duration: 0.5 },
};

export type TrickGrade = 'perfect' | 'good';
// Air tricks started this soon after takeoff count as perfect.
export const PERFECT_AIR_WINDOW = 0.15;
const PERFECT_MULTIPLIER = 1.5;
const STREAK_BONUS = 50;
// Lip handstand: press as she reaches the coping. Windows are seconds before she hits it.
export const LIP_HANDSTAND = {
  name: 'Lip Handstand', perfectPoints: 450, goodPoints: 180,
  perfectWindow: 0.1, goodWindow: 0.3, minSpeed: 350, stall: 0.6, exitKeep: 0.95,
} as const;

export const BOWL_GOALS = [
  { tier: 1, name: 'Bronze', score: 2000 },
  { tier: 2, name: 'Silver', score: 6000 },
  { tier: 3, name: 'Gold', score: 12000 },
] as const;

export interface BowlState {
  s: number;
  v: number;
  airborne: boolean;
  side: number;
  air: number;
  airV: number;
  airPeak: number;
  pumping: boolean;
  timeLeft: number;
  elapsed: number;
  ended: boolean;
  accumulator: number;
  score: number;
  trick: BowlTrick | null;
  trickTime: number;
  pendingScore: number;
  pendingTricks: number;
  combo: number;
  comboIdle: number;
  bestCombo: number;
  tricksLanded: number;
  airs: number;
  bails: number;
  pumpPulse: number;
  landingPulse: number;
  feedback: string;
  feedbackTime: number;
  airTime: number;
  trickGrade: TrickGrade | null;
  pendingCombo: number;
  pendingPerfects: number;
  pendingGoods: number;
  lipArmed: TrickGrade | 'miss' | null;
  queuedTrick: BowlTrick | null;
  airChain: number;
  fallen: number;
  bruises: number;
  stall: number;
  stallSpeed: number;
  perfects: number;
  streak: number;
  bestStreak: number;
  gradeFlash: TrickGrade | 'miss' | null;
  gradeFlashTime: number;
}

export function createBowl(): BowlState {
  return {
    s: -BOWL_HALF + 0.001, v: 0, airborne: false, side: -1, air: 0, airV: 0, airPeak: 0,
    pumping: false, timeLeft: BOWL_DURATION, elapsed: 0, ended: false, accumulator: 0, score: 0,
    trick: null, trickTime: 0, pendingScore: 0, pendingTricks: 0, combo: 0, comboIdle: 0,
    bestCombo: 0, tricksLanded: 0, airs: 0, bails: 0, pumpPulse: 0, landingPulse: 0,
    feedback: 'Drop in! Hold Pump to build speed ✨', feedbackTime: 2.5,
    airTime: 0, trickGrade: null, pendingCombo: 0, pendingPerfects: 0, pendingGoods: 0,
    lipArmed: null, queuedTrick: null, airChain: 0, fallen: 0, bruises: 0, stall: 0, stallSpeed: 0, perfects: 0, streak: 0, bestStreak: 0,
    gradeFlash: null, gradeFlashTime: 0,
  };
}

// Wall angle at an arc position: 0 on the flat, rising to BOWL_MAX_ANGLE at the coping.
export function bowlAngle(s: number): number {
  const d = Math.abs(s) - BOWL_FLAT;
  return d <= 0 ? 0 : Math.min(BOWL_MAX_ANGLE, d / BOWL_RADIUS);
}

// Horizontal offset from the bowl centre and height above the bottom.
export function bowlPoint(s: number): { x: number; h: number } {
  const sign = s < 0 ? -1 : 1;
  const d = Math.abs(s) - BOWL_FLAT;
  if (d <= 0) return { x: s, h: 0 };
  const theta = Math.min(BOWL_MAX_ANGLE, d / BOWL_RADIUS);
  return { x: sign * (BOWL_FLAT + BOWL_RADIUS * Math.sin(theta)), h: BOWL_RADIUS * (1 - Math.cos(theta)) };
}

export const BOWL_COPING = bowlPoint(BOWL_HALF);

export interface BowlPose {
  x: number;
  h: number;
  angle: number;
  facing: number;
}

// Screen-agnostic pose: canvas rotation that keeps her feet on the wall, and facing direction.
export function bowlPose(state: BowlState): BowlPose {
  if (state.stall > 0) {
    return { x: state.side * BOWL_COPING.x, h: BOWL_COPING.h, angle: 0, facing: -state.side };
  }
  if (state.airborne) {
    const rising = state.airV > 0;
    return {
      x: state.side * BOWL_COPING.x,
      h: BOWL_COPING.h + state.air,
      angle: -state.side * BOWL_MAX_ANGLE,
      facing: rising ? state.side : -state.side,
    };
  }
  const point = bowlPoint(state.s);
  const sign = state.s < 0 ? -1 : 1;
  return { x: point.x, h: point.h, angle: -sign * bowlAngle(state.s), facing: state.v >= 0 ? 1 : -1 };
}

function say(state: BowlState, text: string, time = 1.3) {
  state.feedback = text;
  state.feedbackTime = time;
}

export function setPump(state: BowlState, held: boolean) {
  state.pumping = held && !state.ended;
}

function flash(state: BowlState, grade: TrickGrade | 'miss') {
  state.gradeFlash = grade;
  state.gradeFlashTime = 0.9;
}

function breakStreak(state: BowlState) {
  state.streak = 0;
}

// Knocked down after a bad landing or a slammed lip trick.
function crash(state: BowlState, text: string) {
  state.bails++;
  state.bruises++;
  state.fallen = FALL_TIME;
  state.trick = null;
  state.trickGrade = null;
  state.trickTime = 0;
  state.queuedTrick = null;
  state.airChain = 0;
  state.lipArmed = null;
  state.pendingScore = 0;
  state.pendingTricks = 0;
  state.pendingCombo = 0;
  state.pendingPerfects = 0;
  state.pendingGoods = 0;
  state.combo = 0;
  breakStreak(state);
  flash(state, 'miss');
  say(state, text, 1.6);
}

// While a trick is running, one more press queues the next trick in the chain.
export function startBowlTrick(state: BowlState, kind: BowlTrick): boolean {
  if (state.ended || !state.airborne || state.fallen > 0) return false;
  if (state.trick) {
    if (state.queuedTrick) return false;
    state.queuedTrick = kind;
    return true;
  }
  state.trick = kind;
  state.trickTime = 0;
  state.trickGrade = state.airTime <= PERFECT_AIR_WINDOW ? 'perfect' : 'good';
  return true;
}

// Seconds until she reaches the coping on her current line, or Infinity if she isn't heading there.
export function timeToCoping(state: BowlState): number {
  if (state.airborne || state.stall > 0) return Infinity;
  const sign = state.s < 0 ? -1 : 1;
  const out = state.v * sign;
  if (out <= 0) return Infinity;
  return (BOWL_HALF - Math.abs(state.s)) / out;
}

// Handstand on the coping. Press it right as she reaches the lip.
export function armLipHandstand(state: BowlState): boolean {
  if (state.ended || state.airborne || state.stall > 0 || state.lipArmed || state.fallen > 0) return false;
  const sign = state.s < 0 ? -1 : 1;
  const speed = state.v * sign;
  const eta = timeToCoping(state);
  if (eta > LIP_COMMIT) {
    say(state, 'Ride up toward the lip first', 0.8);
    return false;
  }
  if (eta > LIP_HANDSTAND.goodWindow || speed < LIP_HANDSTAND.minSpeed) {
    // She commits anyway and is going to slam on the coping.
    state.lipArmed = 'miss';
    breakStreak(state);
    flash(state, 'miss');
    say(state, speed < LIP_HANDSTAND.minSpeed ? 'Not enough speed! Uh oh…' : 'Too early! Uh oh…', 1);
    return false;
  }
  state.lipArmed = eta <= LIP_HANDSTAND.perfectWindow ? 'perfect' : 'good';
  return true;
}

function startStall(state: BowlState, side: number) {
  const grade = state.lipArmed === 'perfect' ? 'perfect' : 'good';
  state.lipArmed = null;
  state.side = side;
  state.stallSpeed = Math.abs(state.v);
  state.stall = LIP_HANDSTAND.stall;
  state.v = 0;
  state.combo += grade === 'perfect' ? 2 : 1;
  state.tricksLanded++;
  let points: number = grade === 'perfect' ? LIP_HANDSTAND.perfectPoints : LIP_HANDSTAND.goodPoints;
  points *= Math.min(3, state.combo);
  if (grade === 'perfect') {
    state.perfects++;
    state.streak++;
    points += STREAK_BONUS * state.streak;
  } else {
    breakStreak(state);
  }
  state.score += points;
  state.bestCombo = Math.max(state.bestCombo, state.combo);
  state.bestStreak = Math.max(state.bestStreak, state.streak);
  state.comboIdle = 0;
  flash(state, grade);
  say(state, `${grade === 'perfect' ? 'PERFECT' : 'Good'} ${LIP_HANDSTAND.name}! +${points} ✨`);
}

export function bowlTrickProgress(state: BowlState): number {
  return state.trick ? Math.min(1, state.trickTime / BOWL_TRICKS[state.trick].duration) : 0;
}

export function goalTier(score: number): number {
  let tier = 0;
  for (const goal of BOWL_GOALS) if (score >= goal.score) tier = goal.tier;
  return tier;
}

export function nextGoal(score: number) {
  return BOWL_GOALS.find(goal => score < goal.score) ?? null;
}

function land(state: BowlState) {
  state.airborne = false;
  state.landingPulse = 1;
  state.s = state.side * (BOWL_HALF - 0.001);
  state.v = -state.side * Math.abs(state.airV) * LANDING_KEEP;
  state.air = 0;
  state.airChain = 0;
  if (state.trick) {
    state.v *= BAIL_KEEP / LANDING_KEEP;
    crash(state, 'Bail! She slammed and got a bruise 🤕 Finish tricks before landing');
    return;
  }
  state.queuedTrick = null;
  const heightBonus = Math.floor(state.airPeak / 10);
  if (state.pendingTricks) {
    state.combo += state.pendingCombo;
    state.tricksLanded += state.pendingTricks;
    let banked = state.pendingScore * Math.min(3, state.combo) + heightBonus;
    const clean = state.pendingGoods === 0;
    if (clean) {
      state.perfects += state.pendingPerfects;
      state.streak += state.pendingPerfects;
      banked += STREAK_BONUS * state.streak;
    } else {
      state.perfects += state.pendingPerfects;
      breakStreak(state);
    }
    state.score += banked;
    state.bestCombo = Math.max(state.bestCombo, state.combo);
    state.bestStreak = Math.max(state.bestStreak, state.streak);
    state.comboIdle = 0;
    flash(state, clean ? 'perfect' : 'good');
    say(state, `${clean ? 'PERFECT! ' : ''}Landed +${banked} · combo x${Math.min(5, state.combo)} ✨`);
  } else {
    state.score += heightBonus;
    if (heightBonus) say(state, `Air +${heightBonus}`, 0.8);
  }
  state.pendingScore = 0;
  state.pendingTricks = 0;
  state.pendingCombo = 0;
  state.pendingPerfects = 0;
  state.pendingGoods = 0;
}

function step(state: BowlState) {
  const dt = BOWL_STEP;
  state.elapsed += dt;
  state.timeLeft = Math.max(0, state.timeLeft - dt);
  state.feedbackTime = Math.max(0, state.feedbackTime - dt);
  state.landingPulse = Math.max(0, state.landingPulse - dt * 4);
  state.pumpPulse = Math.max(0, state.pumpPulse - dt * 3);
  state.gradeFlashTime = Math.max(0, state.gradeFlashTime - dt);
  state.fallen = Math.max(0, state.fallen - dt);

  if (state.stall > 0) {
    state.stall -= dt;
    if (state.stall <= 0) {
      // Drop back in off the coping with most of her speed.
      state.stall = 0;
      state.s = state.side * (BOWL_HALF - 0.001);
      state.v = -state.side * state.stallSpeed * LIP_HANDSTAND.exitKeep;
      state.landingPulse = 1;
    }
  } else if (state.airborne) {
    state.airTime += dt;
    state.airV -= BOWL_GRAVITY * dt;
    state.air += state.airV * dt;
    state.airPeak = Math.max(state.airPeak, state.air);
    if (state.trick) {
      state.trickTime += dt;
      const trick = BOWL_TRICKS[state.trick];
      if (state.trickTime >= trick.duration) {
        const perfect = state.trickGrade === 'perfect';
        state.pendingScore += Math.round(trick.points * (perfect ? PERFECT_MULTIPLIER : 1));
        state.pendingTricks++;
        state.pendingCombo += perfect ? 2 : 1;
        if (perfect) state.pendingPerfects++;
        else state.pendingGoods++;
        state.airChain++;
        if (state.airChain >= 2) {
          state.pendingScore += CHAIN_BONUS * (state.airChain - 1);
          say(state, `${trick.name}! Chain x${state.airChain} 🔥`, 0.8);
        } else {
          say(state, `${perfect ? 'Perfect ' : ''}${trick.name}!`, 0.7);
        }
        state.trick = null;
        state.trickGrade = null;
        state.trickTime = 0;
        if (state.queuedTrick) {
          // A trick linked straight from the last one keeps the timing grade.
          state.trick = state.queuedTrick;
          state.trickGrade = perfect ? 'perfect' : 'good';
          state.queuedTrick = null;
        }
      }
    }
    if (state.air <= 0 && state.airV < 0) land(state);
  } else {
    const sign = state.s < 0 ? -1 : 1;
    const theta = bowlAngle(state.s);
    // Gravity along the wall pulls her back toward the centre.
    state.v += -sign * BOWL_GRAVITY * Math.sin(theta) * dt;
    if (state.pumping && state.fallen <= 0) {
      // Holding pump always adds speed along her line; when nearly stopped it pushes her toward the centre.
      const onFlat = Math.abs(state.s) < BOWL_FLAT;
      const dir = Math.abs(state.v) > 20 || (onFlat && state.v !== 0) ? Math.sign(state.v) : -sign;
      state.v += dir * PUMP_ACCEL * dt;
      state.pumpPulse = 1;
    }
    const loss = (DRAG * Math.abs(state.v) + ROLL_DECEL) * dt;
    state.v = Math.sign(state.v) * Math.max(0, Math.abs(state.v) - loss);
    state.s += state.v * dt;
    if (state.lipArmed && state.v * sign <= 0) {
      if (state.lipArmed === 'miss') {
        crash(state, 'Bail! She fell off the handstand 🤕');
      } else {
        state.lipArmed = null;
        breakStreak(state);
        flash(state, 'miss');
        say(state, 'Missed the lip!', 1);
      }
    }
    if (Math.abs(state.s) >= BOWL_HALF) {
      const side = state.s < 0 ? -1 : 1;
      if (state.v * side > 0 && state.lipArmed === 'miss') {
        // Slammed on the coping: she tumbles back down the wall.
        state.v = -side * Math.abs(state.v) * 0.3;
        crash(state, 'Bail! Handstand slam, ouch 🤕');
      } else if (state.v * side > 0 && state.lipArmed) {
        startStall(state, side);
      } else if (state.v * side > 0) {
        // Past the coping: launch straight up out of the wall.
        state.side = side;
        state.airborne = true;
        state.airV = Math.min(MAX_LAUNCH, Math.abs(state.v));
        state.air = 0;
        state.airPeak = 0;
        state.airTime = 0;
        state.airChain = 0;
        state.queuedTrick = null;
        state.airs++;
        state.v = 0;
      }
      state.s = side * (BOWL_HALF - 0.001);
    }
    state.comboIdle += dt;
    if (state.combo && state.comboIdle > COMBO_WINDOW) state.combo = 0;
  }

  if (state.timeLeft <= 0 && !state.airborne && state.stall <= 0) {
    state.ended = true;
    state.pumping = false;
    say(state, `Time! ${state.score} points`, 3);
  }
}

export function advanceBowl(state: BowlState, seconds: number): void {
  state.accumulator += Math.max(0, Math.min(0.1, Number.isFinite(seconds) ? seconds : 0));
  while (state.accumulator + 1e-10 >= BOWL_STEP && !state.ended) {
    step(state);
    state.accumulator = Math.max(0, state.accumulator - BOWL_STEP);
  }
}
