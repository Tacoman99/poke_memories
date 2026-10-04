// Run with: node --test components/gameplay.test.mjs
// TypeScript is already a project build dependency; no extra test runner needed.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import ts from 'typescript';

const source = readFileSync(new URL('./gameplay.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.ESNext } }).outputText;
const {
  advanceSimulation, BASE_SPEED, COURSE_LENGTH, COURSE_SECONDS, createSimulation, GRAVITY, JUMP_SPEED, MIN_JUMP_SPEED, MAX_SPEED, PLAYER_LEFT, PLAYER_RIGHT, SECTION_LENGTH,
  overlaps, pattern, pressJump, railHeight, releaseJump, speedFor, STEP, stepSimulation,
  startTrick, TRICKS, KICK_SPEED, trickProgress, isGrindable,
} = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`);

const rail = (x = 0, rise = 0) => ({ id: 'test-rail', kind: 'rail', x, y: -60, width: 420, rise });
const falling = (x, foot = -62) => {
  const state = createSimulation('COURSE');
  Object.assign(state, { distance: x, foot, velocity: 360, grounded: false, coyote: 0 });
  return state;
};

test('animation clocks freeze the push cycle in air and on rails without affecting physics', () => {
  const state = createSimulation('COURSE');
  for (let i = 0; i < 30; i++) stepSimulation(state);
  const rolling = state.rollTime;
  assert.ok(Math.abs(rolling - 30 * STEP) < 1e-9);
  pressJump(state);
  for (let i = 0; i < 30; i++) stepSimulation(state);
  assert.equal(state.rollTime, rolling);
  assert.ok(Math.abs(state.airTime - 30 * STEP) < 1e-9);
  const grind = falling(90);
  grind.rollTime = rolling;
  grind.grindTime = 4;
  grind.items = [rail()];
  stepSimulation(grind);
  assert.equal(grind.grindTime, STEP, 'a new rail starts a fresh balance loop');
  for (let i = 0; i < 20; i++) stepSimulation(grind);
  assert.equal(grind.rollTime, rolling);
  const balanceTime = grind.grindTime;
  pressJump(grind);
  stepSimulation(grind);
  assert.equal(grind.grindTime, balanceTime, 'blend-out keeps the departing balance pose');
  assert.equal(grind.airTime, STEP);
});

test('endless rolling cadence follows travel speed', () => {
  const state = createSimulation('ENDLESS');
  state.distance = 18000;
  stepSimulation(state);
  assert.ok(Math.abs(state.rollTime - STEP * MAX_SPEED / BASE_SPEED) < 1e-9);
});

test('descending feet actually cross the rail top and transition into grinding', () => {
  const state = falling(90);
  state.items = [rail()];
  stepSimulation(state);
  assert.equal(state.rail, 'test-rail');
  assert.equal(state.foot, -60);
  assert.equal(state.velocity, 0);
  assert.equal(state.landings, 1);
  assert.equal(state.trickScore, 0);
  for (let i = 0; i < 60; i++) stepSimulation(state);
  assert.equal(state.rail, 'test-rail');
  assert.ok(Math.abs(state.trickScore - 30) < 1e-8);
  assert.equal(state.collected, 0, 'grind points never become Pokéballs');
});

test('sloped rail follows the contact height after the descending crossing', () => {
  const item = rail(0, -18);
  const state = falling(100, railHeight(item, 100) - 1);
  state.items = [item];
  stepSimulation(state);
  assert.equal(state.rail, item.id);
  for (let i = 0; i < 20; i++) stepSimulation(state);
  assert.equal(state.foot, railHeight(item, state.distance));
});

test('ascending feet and a side collision from below cannot attach to a rail', () => {
  for (const [foot, velocity] of [[-62, -200], [-30, 200]]) {
    const state = falling(80, foot);
    state.velocity = velocity;
    state.items = [rail()];
    stepSimulation(state);
    assert.equal(state.rail, null);
    assert.equal(state.landings, 0);
  }
});

test('footprint overlap is consistent at rail entry and exit edges', () => {
  const item = rail(100);
  const entry = item.x - PLAYER_RIGHT;
  const exit = item.x + item.width + PLAYER_LEFT;
  assert.equal(overlaps(entry, item), true);
  assert.equal(overlaps(entry - 0.1, item), false);
  assert.equal(overlaps(exit, item), true);
  assert.equal(overlaps(exit + 0.1, item), false);
  for (const x of [entry, exit - BASE_SPEED * STEP]) {
    const state = falling(x, -61);
    state.items = [item];
    stepSimulation(state);
    assert.equal(state.rail, item.id);
  }
  const outside = falling(entry - 10, -61);
  outside.items = [item];
  stepSimulation(outside);
  assert.equal(outside.rail, null);
});

test('jumping from an actually landed rail detaches immediately and rises', () => {
  const state = falling(90);
  state.items = [rail()];
  stepSimulation(state);
  pressJump(state);
  stepSimulation(state);
  assert.equal(state.rail, null);
  assert.equal(state.grounded, false);
  assert.ok(state.velocity < -500);
  assert.ok(state.foot < -60);
  releaseJump(state);
  assert.equal(state.velocity, -MIN_JUMP_SPEED);
});

test('coyote jump works just after leaving the end of a rail', () => {
  const state = falling(100 + 420 + PLAYER_LEFT - BASE_SPEED * STEP, -61);
  state.items = [rail(100)];
  stepSimulation(state);
  assert.ok(state.rail);
  stepSimulation(state);
  assert.equal(state.rail, null);
  assert.ok(state.coyote > 0);
  pressJump(state);
  stepSimulation(state);
  assert.ok(state.velocity < -500);
});

test('buffered press immediately before rail landing is consumed as a jump', () => {
  const state = falling(90);
  state.items = [rail()];
  pressJump(state);
  stepSimulation(state);
  assert.equal(state.landings, 1, 'the test actually lands before the buffered jump');
  assert.equal(state.rail, null);
  assert.equal(state.buffer, 0);
  assert.ok(state.velocity < -500);
});

test('ground landing consumes a buffer; an expired buffer does not autojump', () => {
  const buffered = falling(0, -2);
  pressJump(buffered);
  stepSimulation(buffered);
  assert.equal(buffered.grounded, false);
  assert.ok(buffered.velocity < 0);
  const expired = falling(0, -2);
  expired.buffer = STEP / 2;
  stepSimulation(expired);
  assert.equal(expired.grounded, true);
  assert.equal(expired.velocity, 0);
});

test('release cancels held input and cuts a jump exactly once', () => {
  const state = createSimulation('COURSE');
  pressJump(state);
  stepSimulation(state);
  releaseJump(state);
  assert.equal(state.held, false);
  assert.equal(state.velocity, -MIN_JUMP_SPEED);
  releaseJump(state);
  assert.equal(state.velocity, -MIN_JUMP_SPEED);
});

test('collection is an actual count; one contact only collects a ball once', () => {
  const state = createSimulation('COURSE');
  state.items = [{ id: 'ball', kind: 'ball', x: 0, y: -45, width: 28 }];
  for (let i = 0; i < 10; i++) stepSimulation(state);
  assert.equal(state.collected, 1);
  assert.equal(state.trickScore, 0);
});

test('three hits end the run and invulnerability protects repeated contacts', () => {
  const state = createSimulation('COURSE');
  state.items = [0, 1, 2].map(i => ({ id: `cone:${i}`, kind: 'cone', x: i * 10, y: -36, width: 32 }));
  stepSimulation(state);
  assert.equal(state.hearts, 2);
  for (let i = 0; i < 15; i++) stepSimulation(state);
  assert.equal(state.hearts, 2);
  assert.ok(state.invulnerable > 0);
  state.invulnerable = 0;
  state.items = [{ id: 'hit2', kind: 'cone', x: state.distance, y: -36, width: 32 }];
  stepSimulation(state);
  state.invulnerable = 0;
  state.items = [{ id: 'hit3', kind: 'cone', x: state.distance, y: -36, width: 32 }];
  stepSimulation(state);
  assert.equal(state.hearts, 0);
  assert.equal(state.ended, true);
  assert.equal(state.completed, false);
});

function playCourse(fps) {
  const state = createSimulation('COURSE');
  const hazards = [0, 1, 2].flatMap(pattern).filter(item => item.kind !== 'ball');
  const events = [];
  let previous = -Infinity;
  for (const item of hazards) {
    // One sustained jump clears the paired cones in Boardwalk Rush.
    if (item.x - previous < 200) continue;
    const lead = item.kind === 'rail' ? 156 : 108;
    const start = Math.round((item.x - lead) / BASE_SPEED * 30) / 30;
    events.push({ time: start, press: true }, { time: start + 0.72, press: false });
    previous = item.x;
  }
  let eventIndex = 0;
  for (let frame = 0; frame < fps * 50 && !state.ended; frame++) {
    while (eventIndex < events.length && events[eventIndex].time <= frame / fps + 1e-8) {
      const event = events[eventIndex++];
      if (event.press) pressJump(state); else releaseJump(state);
    }
    advanceSimulation(state, 1 / fps);
  }
  return state;
}

test('faster course can be cleared through all three sections without damage', () => {
  const state = playCourse(60);
  assert.equal(state.completed, true);
  assert.equal(state.ended, true);
  assert.equal(state.distance, COURSE_LENGTH);
  assert.equal(BASE_SPEED, 360);
  assert.equal(COURSE_SECONDS, 30);
  assert.ok(Math.abs(state.elapsed - COURSE_SECONDS) < STEP * 2);
  assert.ok(state.landings >= 4);
  assert.equal(state.hearts, 3, 'the authored route can be cleared without damage');
  assert.ok(state.collected > 10);
  assert.ok(state.trickScore > 100);
  const finalDistance = state.distance;
  advanceSimulation(state, 0.1);
  assert.equal(state.distance, finalDistance, 'completion stays latched');
});

test('actual course outcome and timers are frame independent at 30, 60 and 120Hz', () => {
  const runs = [30, 60, 120].map(playCourse);
  for (const state of runs.slice(1)) {
    for (const field of ['completed', 'distance', 'collected', 'hearts', 'landings']) {
      assert.equal(state[field], runs[0][field], field);
    }
    assert.ok(Math.abs(state.trickScore - runs[0].trickScore) < 1e-8);
    assert.ok(Math.abs(state.elapsed - runs[0].elapsed) < 1e-8);
  }
});

test('large, negative or invalid frame deltas cannot destabilize the simulation', () => {
  const state = createSimulation('COURSE');
  advanceSimulation(state, 10);
  assert.ok(state.elapsed <= 0.1 + 1e-8);
  assert.ok(state.distance <= BASE_SPEED * 0.1 + 0.001);
  advanceSimulation(state, -10);
  advanceSimulation(state, Number.NaN);
  assert.ok(Number.isFinite(state.distance));
  assert.ok(state.elapsed <= 0.1 + 1e-8);
});

test('endless deterministically cycles authored patterns with capped difficulty', () => {
  assert.deepEqual(pattern(0), pattern(0));
  assert.deepEqual(pattern(3).map(item => ({ ...item, id: item.id.replace('3:', '0:'), x: item.x - COURSE_LENGTH })), pattern(0));
  const state = createSimulation('ENDLESS');
  state.distance = COURSE_LENGTH + 1000;
  advanceSimulation(state, 1 / 60);
  assert.equal(state.completed, false);
  assert.equal(state.ended, false);
  assert.ok(state.items.some(item => item.x > COURSE_LENGTH));
  state.distance = 1000000;
  assert.equal(speedFor(state), MAX_SPEED);
  assert.equal(speedFor(createSimulation('COURSE')), BASE_SPEED);
});

test('course sections have distinct hazards and rails, not the same repeated route', () => {
  const sections = [0, 1, 2].map(index => pattern(index).filter(item => item.kind !== 'ball'));
  assert.deepEqual(sections.map(items => items.filter(item => item.kind === 'rail').length), [1, 1, 2]);
  assert.ok(sections.every(items => items.some(item => item.kind === 'barrier')));
  const layouts = sections.map((items, index) => items.map(item => `${item.kind}:${item.x - index * SECTION_LENGTH}`).join(','));
  assert.equal(new Set(layouts).size, 3);
});

test('jump responds on press and an immediate tap has the same arc as a one step tap', () => {
  const immediate = createSimulation('COURSE');
  pressJump(immediate);
  assert.equal(immediate.velocity, -JUMP_SPEED);
  assert.equal(immediate.grounded, false);
  releaseJump(immediate);
  assert.equal(immediate.velocity, -MIN_JUMP_SPEED);
  const nextFrame = createSimulation('COURSE');
  pressJump(nextFrame);
  stepSimulation(nextFrame);
  releaseJump(nextFrame);
  assert.equal(nextFrame.velocity, immediate.velocity);
});

test('quick taps clear both cones and barriers at practical approach distances', () => {
  for (const [kind, width, y] of [['cone', 32, -36], ['barrier', 70, -52]]) {
    const state = createSimulation('COURSE');
    state.items = [{ id: 'hazard', kind, width, y, x: 90 }];
    pressJump(state);
    releaseJump(state);
    let peak = 0;
    for (let i = 0; i < 120; i++) {
      stepSimulation(state);
      peak = Math.min(peak, state.foot);
    }
    assert.equal(state.hearts, 3, kind);
    assert.ok(-peak > 85 && -peak < 100, `tap peak: ${-peak}`);
    assert.equal(state.grounded, true);
  }
});

test('release near the apex does not abruptly alter upward velocity', () => {
  const state = createSimulation('COURSE');
  pressJump(state);
  while (state.velocity < -150) stepSimulation(state);
  const before = state.velocity;
  releaseJump(state);
  assert.equal(state.velocity, before);
  const oldVelocity = state.velocity;
  stepSimulation(state);
  assert.ok(Math.abs(state.velocity - oldVelocity - GRAVITY * STEP) < 0.001);
});

test('jump animation blends in instead of switching pose in one frame', () => {
  const state = createSimulation('COURSE');
  pressJump(state);
  stepSimulation(state);
  assert.ok(state.airBlend > 0 && state.airBlend < 0.2);
  assert.equal(state.grindBlend, 0);
});

const airborne = (foot = -150, velocity = -200) => {
  const state = createSimulation('COURSE');
  Object.assign(state, { foot, velocity, grounded: false, coyote: 0 });
  return state;
};
const stepUntilGrounded = state => {
  for (let i = 0; i < 400 && !state.grounded; i++) stepSimulation(state);
};

test('tricks only start in the air and one at a time', () => {
  const ground = createSimulation('COURSE');
  assert.equal(startTrick(ground, 'grab'), false);
  const air = airborne();
  assert.equal(startTrick(air, 'grab'), true);
  assert.equal(startTrick(air, 'spin'), false);
  assert.equal(trickProgress(air), 0);
  stepSimulation(air);
  assert.ok(trickProgress(air) > 0 && trickProgress(air) < 1);
});

test('finished tricks bank on landing with the combo multiplier', () => {
  const state = airborne(-400, -500);
  startTrick(state, 'grab');
  for (let i = 0; i < Math.ceil(TRICKS.grab.duration / STEP) + 1; i++) stepSimulation(state);
  assert.equal(state.trick, null);
  assert.equal(state.pendingTricks, 1);
  assert.equal(state.trickScore, 0, 'nothing banks mid air');
  startTrick(state, 'kick');
  stepUntilGrounded(state);
  assert.equal(state.hearts, 3);
  assert.equal(state.tricksLanded, 2);
  assert.equal(state.combo, 2);
  assert.equal(state.trickScore, (TRICKS.grab.points + TRICKS.kick.points) * 2);
  assert.equal(state.bestCombo, 2);
});

test('landing mid trick is a bail that costs a heart and clears the combo', () => {
  const state = airborne(-4, 300);
  state.combo = 3;
  state.pendingScore = 120;
  state.pendingTricks = 1;
  startTrick(state, 'spin');
  stepUntilGrounded(state);
  assert.equal(state.hearts, 2);
  assert.equal(state.trick, null);
  assert.equal(state.combo, 0);
  assert.equal(state.trickScore, 0);
});

test('combo decays after rolling flat past the window', () => {
  const state = createSimulation('COURSE');
  state.combo = 2;
  const windowSteps = Math.ceil(0.6 / STEP);
  for (let i = 0; i < windowSteps - 5; i++) stepSimulation(state);
  assert.equal(state.combo, 2);
  for (let i = 0; i < 10; i++) stepSimulation(state);
  assert.equal(state.combo, 0);
});

test('a kicker launches the skater once, higher than a full jump', () => {
  const state = createSimulation('COURSE');
  state.items = [{ id: 'kick', kind: 'kicker', x: 10, y: -34, width: 60 }];
  stepSimulation(state);
  assert.equal(state.grounded, false);
  assert.ok(KICK_SPEED > JUMP_SPEED);
  assert.ok(state.velocity < -JUMP_SPEED);
  assert.equal(state.items[0].taken, true);
  assert.equal(state.hearts, 3);
});

test('rolling into a gap costs a heart and bounces the skater out', () => {
  const state = createSimulation('COURSE');
  state.items = [{ id: 'gap', kind: 'gap', x: 0, y: 0, width: 120 }];
  stepSimulation(state);
  assert.equal(state.hearts, 2);
  assert.ok(state.velocity < 0);
  assert.equal(state.grounded, false);
  for (let i = 0; i < 10; i++) stepSimulation(state);
  assert.equal(state.hearts, 2, 'invulnerability prevents a double hit');
});

test('stairs are a grindable handrail and a wall when rolled into', () => {
  const stairs = { id: 'stairs', kind: 'stairs', x: 0, y: -76, width: 300, rise: 28 };
  assert.equal(isGrindable(stairs), true);
  assert.equal(isGrindable(rail()), true);
  assert.equal(isGrindable({ kind: 'cone' }), false);
  const grind = falling(90, railHeight(stairs, 90) - 2);
  grind.items = [stairs];
  stepSimulation(grind);
  assert.equal(grind.rail, 'stairs');
  for (let i = 0; i < 20; i++) stepSimulation(grind);
  assert.equal(grind.foot, railHeight(stairs, grind.distance));
  const bonk = createSimulation('COURSE');
  bonk.items = [{ ...stairs }];
  stepSimulation(bonk);
  assert.equal(bonk.hearts, 2);
});

test('every section adds a new obstacle type', () => {
  const kinds = [0, 1, 2].map(i => new Set(pattern(i).map(item => item.kind)));
  assert.ok(kinds[0].has('stairs') && kinds[0].has('kicker'));
  assert.ok(kinds[1].has('gap'));
});
