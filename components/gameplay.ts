export type PlayMode = 'COURSE' | 'ENDLESS';
export const STEP = 1 / 120;
export const COURSE_LENGTH = 10800;
export const BASE_SPEED = 360;
export const MAX_SPEED = 460;
export const SECTION_LENGTH = 3600;
export const COURSE_SECONDS = COURSE_LENGTH / BASE_SPEED;
export const SKATER_HEIGHT = 126;
export const PLAYER_LEFT = 22;
export const PLAYER_RIGHT = 35;
export const GRAVITY = 1800;
export const JUMP_SPEED = 650;
export const MIN_JUMP_SPEED = 580;
export const KICK_SPEED = 860;
const COYOTE = 0.1;
const BUFFER = 0.14;
const COMBO_WINDOW = 0.6;

export type TrickKind = 'grab' | 'kick' | 'spin';
export const TRICKS: Record<TrickKind, { name: string; points: number; duration: number }> = {
  grab: { name: 'Pink Grab', points: 120, duration: 0.4 },
  kick: { name: 'Heart Kick', points: 150, duration: 0.35 },
  spin: { name: '360 Twirl', points: 220, duration: 0.5 },
};

export interface TrackItem {
  id: string;
  kind: 'ball' | 'cone' | 'barrier' | 'rail' | 'stairs' | 'kicker' | 'gap';
  x: number;
  width: number;
  y: number;
  rise?: number;
  taken?: boolean;
}

export interface Simulation {
  mode: PlayMode;
  distance: number;
  elapsed: number;
  foot: number;
  velocity: number;
  rail: string | null;
  grounded: boolean;
  coyote: number;
  buffer: number;
  held: boolean;
  invulnerable: number;
  hearts: number;
  collected: number;
  trickScore: number;
  completed: boolean;
  ended: boolean;
  accumulator: number;
  nextPattern: number;
  items: TrackItem[];
  feedback: string;
  feedbackTime: number;
  landings: number;
  landingPulse: number;
  airBlend: number;
  grindBlend: number;
  rollTime: number;
  airTime: number;
  grindTime: number;
  trick: TrickKind | null;
  trickTime: number;
  pendingScore: number;
  pendingTricks: number;
  combo: number;
  comboScore: number;
  comboIdle: number;
  tricksLanded: number;
  bestCombo: number;
}

export const isGrindable = (item: TrackItem) => item.kind === 'rail' || item.kind === 'stairs';

export function pattern(index: number): TrackItem[] {
  const base = index * SECTION_LENGTH;
  const variant = index % 3;
  const items: TrackItem[] = [];
  const add = (kind: TrackItem['kind'], x: number, y: number, width: number, rise?: number) =>
    items.push({ id: `${index}:${items.length}`, kind, x: base + x, y, width, rise });
  const trail = (xs: number[], y = -48) => xs.forEach(x => add('ball', x, y, 28));
  const grind = (x: number, width: number, y: number, rise = 0) => {
    add('rail', x, y, width, rise);
    for (let offset = 90; offset < width - 20; offset += 100) {
      add('ball', x + offset, y + rise * offset / width - 45, 28);
    }
  };
  const stairs = (x: number, width: number, y: number, rise: number) => {
    add('stairs', x, y, width, rise);
    for (let offset = 70; offset < width - 20; offset += 90) {
      add('ball', x + offset, y + rise * offset / width - 45, 28);
    }
  };
  if (variant === 0) {
    trail([300, 390]);
    stairs(620, 300, -76, 28);
    grind(1330, 440, -58);
    add('cone', 2230, -36, 32);
    trail([2220, 2310], -105);
    add('barrier', 2820, -52, 70);
    trail([2810, 2900], -112);
    add('kicker', 3150, -34, 60);
    trail([3300, 3390], -230);
  } else if (variant === 1) {
    grind(520, 520, -78, -16);
    add('cone', 1450, -36, 32);
    trail([1440, 1530], -100);
    add('cone', 2020, -36, 32);
    add('cone', 2080, -36, 32);
    trail([2010, 2100, 2190], -110);
    add('barrier', 2740, -52, 70);
    trail([2730, 2820], -112);
    add('gap', 3150, 0, 120);
    trail([3150, 3240], -120);
  } else {
    grind(470, 380, -66, 12);
    add('cone', 1260, -36, 32);
    trail([1250, 1340], -105);
    grind(1870, 460, -88, -10);
    add('barrier', 2750, -52, 70);
    trail([2740, 2830], -112);
    add('cone', 3310, -36, 32);
    trail([3300, 3390], -100);
  }
  return items;
}

export function createSimulation(mode: PlayMode): Simulation {
  return {
    mode, distance: 0, elapsed: 0, foot: 0, velocity: 0, rail: null,
    grounded: true, coyote: COYOTE, buffer: 0, held: false,
    invulnerable: 0, hearts: 3, collected: 0, trickScore: 0,
    completed: false, ended: false, accumulator: 0, nextPattern: 0,
    items: [], feedback: 'Rose Walk · Let’s roll!', feedbackTime: 2, landings: 0, landingPulse: 0,
    airBlend: 0, grindBlend: 0, rollTime: 0, airTime: 0, grindTime: 0,
    trick: null, trickTime: 0, pendingScore: 0, pendingTricks: 0,
    combo: 0, comboScore: 0, comboIdle: 0, tricksLanded: 0, bestCombo: 0,
  };
}

export function speedFor(state: Simulation): number {
  return state.mode === 'COURSE' ? BASE_SPEED : Math.min(MAX_SPEED, BASE_SPEED + state.distance / 180);
}

export function pressJump(state: Simulation): void {
  if (state.ended || state.held) return;
  state.held = true;
  state.buffer = BUFFER;
  if (state.grounded || state.coyote > 0) jump(state);
}

export function releaseJump(state: Simulation): void {
  if (!state.held) return;
  state.held = false;
  if (state.velocity < -MIN_JUMP_SPEED) state.velocity = -MIN_JUMP_SPEED;
}

// Tricks only start in the air and only bank on landing, so a trick still in
// progress at touchdown is a bail.
export function startTrick(state: Simulation, kind: TrickKind): boolean {
  if (state.ended || state.grounded || state.trick) return false;
  state.trick = kind;
  state.trickTime = 0;
  return true;
}

export function trickProgress(state: Simulation): number {
  return state.trick ? Math.min(1, state.trickTime / TRICKS[state.trick].duration) : 0;
}

export function overlaps(x: number, item: TrackItem): boolean {
  return x + PLAYER_RIGHT >= item.x && x - PLAYER_LEFT <= item.x + item.width;
}

export function railHeight(rail: TrackItem, x: number): number {
  const contact = Math.max(rail.x, Math.min(x, rail.x + rail.width));
  return rail.y + (rail.rise ?? 0) * (contact - rail.x) / rail.width;
}

function jump(state: Simulation): void {
  state.velocity = state.held ? -JUMP_SPEED : -MIN_JUMP_SPEED;
  state.rail = null;
  state.grounded = false;
  state.coyote = 0;
  state.buffer = 0;
  state.landingPulse = 0;
  state.airTime = 0;
}

function hurt(state: Simulation, text: string): void {
  state.hearts--;
  state.invulnerable = 1.8;
  feedback(state, state.hearts ? text : 'Out of hearts', 1.8);
  if (!state.hearts) state.ended = true;
}

// Returns false when the skater bailed a trick on touchdown.
function land(state: Simulation): boolean {
  if (state.trick) {
    state.trick = null;
    state.pendingScore = 0;
    state.pendingTricks = 0;
    state.combo = 0;
    state.comboScore = 0;
    if (state.invulnerable === 0) hurt(state, 'Bailed it! Finish the trick before landing');
    else feedback(state, 'Bailed it!');
    return false;
  }
  if (state.pendingTricks) {
    state.combo += state.pendingTricks;
    const banked = state.pendingScore * Math.min(5, state.combo);
    state.trickScore += banked;
    state.comboScore += banked;
    state.tricksLanded += state.pendingTricks;
    state.bestCombo = Math.max(state.bestCombo, state.combo);
    feedback(state, `Landed! +${Math.round(banked)} · combo x${Math.min(5, state.combo)} ✨`, 1.4);
    state.pendingScore = 0;
    state.pendingTricks = 0;
  }
  state.comboIdle = 0;
  return true;
}

function overGap(state: Simulation): TrackItem | undefined {
  const center = state.distance + (PLAYER_RIGHT - PLAYER_LEFT) / 2;
  return state.items.find(item => item.kind === 'gap' && center > item.x && center < item.x + item.width);
}

function feedback(state: Simulation, text: string, seconds = 1.3): void {
  state.feedback = text;
  state.feedbackTime = seconds;
}

function fillTrack(state: Simulation, lookAhead: number): void {
  while (state.nextPattern * SECTION_LENGTH < state.distance + lookAhead + SECTION_LENGTH &&
    (state.mode === 'ENDLESS' || state.nextPattern < 3)) {
    state.items.push(...pattern(state.nextPattern++));
  }
}

export function stepSimulation(state: Simulation, dt = STEP): void {
  if (state.ended) return;
  const oldX = state.distance;
  const oldFoot = state.foot;
  const wasGrounded = state.grounded;
  state.elapsed += dt;
  state.distance += speedFor(state) * dt;
  state.invulnerable = Math.max(0, state.invulnerable - dt);
  state.feedbackTime = Math.max(0, state.feedbackTime - dt);
  state.landingPulse = Math.max(0, state.landingPulse - dt * 7);
  state.coyote = Math.max(0, state.coyote - dt);
  state.buffer = Math.max(0, state.buffer - dt);
  if (state.trick) {
    state.trickTime += dt;
    const trick = TRICKS[state.trick];
    if (state.trickTime >= trick.duration) {
      state.pendingScore += trick.points;
      state.pendingTricks++;
      feedback(state, `${trick.name}! hold the landing`, 0.9);
      state.trick = null;
      state.trickTime = 0;
    }
  }
  if (state.grounded || state.rail) state.coyote = COYOTE;
  if (state.buffer > 0 && state.coyote > 0) jump(state);

  const supportingRail = state.items.find(item => item.id === state.rail);
  if (supportingRail && overlaps(state.distance, supportingRail)) {
    state.foot = railHeight(supportingRail, state.distance);
    state.velocity = 0;
    state.grounded = true;
    state.trickScore += 60 * dt;
  } else {
    state.rail = null;
    state.grounded = false;
    state.velocity = Math.min(850, state.velocity + GRAVITY * dt);
    state.foot += state.velocity * dt;
    if (state.velocity >= 0) {
      // Solve the swept foot/top crossing, then check the same footprint at
      // that instant. This catches rail edges without snapping onto their sides.
      let landing: { rail: TrackItem; fraction: number; height: number } | null = null;
      for (const rail of state.items) {
        if (!isGrindable(rail)) continue;
        const before = oldFoot - railHeight(rail, oldX);
        const after = state.foot - railHeight(rail, state.distance);
        if (before > 0.001 || after < 0 || after <= before) continue;
        const fraction = Math.max(0, Math.min(1, -before / (after - before)));
        const crossingX = oldX + (state.distance - oldX) * fraction;
        if (!overlaps(crossingX, rail)) continue;
        if (!landing || fraction < landing.fraction) {
          landing = { rail, fraction, height: railHeight(rail, state.distance) };
        }
      }
      if (landing && overlaps(state.distance, landing.rail)) {
        state.foot = landing.height;
        state.velocity = 0;
        state.rail = landing.rail.id;
        state.grindTime = 0;
        state.grounded = true;
        state.landings++;
        state.landingPulse = 1;
        const hadTricks = state.pendingTricks > 0;
        if (land(state) && !hadTricks) feedback(state, landing.rail.kind === 'stairs' ? 'Handrail grind! ✨' : 'Clean landing! ✨');
      }
    }
    const gap = state.foot >= 0 && state.invulnerable === 0 ? overGap(state) : undefined;
    if (gap) {
      state.trick = null;
      hurt(state, 'Fell in the gap! Hop out');
      if (!state.ended) {
        state.foot = 0;
        state.velocity = -MIN_JUMP_SPEED;
        state.grounded = false;
        state.airTime = 0;
      }
    } else if (state.foot >= 0) {
      if (!wasGrounded && state.velocity > 200) state.landingPulse = 1;
      state.foot = 0;
      state.velocity = 0;
      state.grounded = true;
      if (!wasGrounded) land(state);
    }
  }
  if (state.grounded && !state.rail && !state.ended) {
    const kicker = state.items.find(item => item.kind === 'kicker' && !item.taken && overlaps(state.distance, item));
    if (kicker && state.foot >= -1) {
      kicker.taken = true;
      state.velocity = -KICK_SPEED;
      state.grounded = false;
      state.coyote = 0;
      state.buffer = 0;
      state.airTime = 0;
      feedback(state, 'Kicker launch! Throw a trick ✨', 1.1);
    }
  }
  if (state.grounded && !state.rail && state.combo) {
    state.comboIdle += dt;
    if (state.comboIdle > COMBO_WINDOW) {
      state.combo = 0;
      state.comboScore = 0;
    }
  }
  if (state.grounded) {
    state.coyote = COYOTE;
    if (state.buffer > 0) jump(state);
  }
  const blend = 1 - Math.exp(-14 * dt);
  state.airBlend += ((state.grounded ? 0 : 1) - state.airBlend) * blend;
  state.grindBlend += ((state.rail ? 1 : 0) - state.grindBlend) * blend;
  if (state.grounded && !state.rail) state.rollTime += dt * speedFor(state) / BASE_SPEED;
  if (!state.grounded) state.airTime = (wasGrounded ? 0 : state.airTime) + dt;
  if (state.rail) state.grindTime += dt;

  for (const item of state.items) {
    if (item.taken || item.kind === 'rail' || item.kind === 'kicker' || item.kind === 'gap' || item.id === state.rail || !overlaps(state.distance, item)) continue;
    if (item.kind === 'ball') {
      if (item.y + 14 >= state.foot - SKATER_HEIGHT && item.y - 14 <= state.foot) {
        item.taken = true;
        state.collected++;
        feedback(state, '+1 Pokéball ♡', 0.8);
      }
    } else if (state.foot > (item.kind === 'stairs' ? railHeight(item, state.distance) : item.y) + 4 && state.invulnerable === 0) {
      item.taken = true;
      hurt(state, 'Ouch! Keep rolling · briefly protected');
    }
  }
  state.items = state.items.filter(item => item.x + item.width >= state.distance - 180);
  if (!state.ended && state.mode === 'COURSE' && state.distance >= COURSE_LENGTH) {
    state.distance = COURSE_LENGTH;
    state.completed = true;
    state.ended = true;
    feedback(state, 'Sunset Course cleared! ♡', 10);
  }
}

// Fixed steps keep collision, timers, and scoring identical across refresh
// rates. A suspended tab never advances more than 100ms on its return.
export function advanceSimulation(state: Simulation, seconds: number, lookAhead = 1200): void {
  fillTrack(state, lookAhead);
  state.accumulator += Math.max(0, Math.min(0.1, Number.isFinite(seconds) ? seconds : 0));
  while (state.accumulator + 1e-10 >= STEP && !state.ended) {
    stepSimulation(state);
    state.accumulator = Math.max(0, state.accumulator - STEP);
  }
}
