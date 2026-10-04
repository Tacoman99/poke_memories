import assert from 'node:assert/strict';
import test from 'node:test';
import {
  BOWL_DURATION, BOWL_TRICKS, LIP_HANDSTAND, advanceBowl, armLipHandstand, createBowl, goalTier, nextGoal,
  setPump, startBowlTrick, timeToCoping,
} from '../components/bowlplay.ts';

const smartPump = (s) => setPump(s, s.v * (s.s < 0 ? -1 : 1) < 0);

function play(hz, { pump = smartPump, trick = 'spin', seconds = BOWL_DURATION + 1, oncePerAir = false, minAirV = 400 } = {}) {
  const s = createBowl();
  let firstAir = null;
  let trickedAir = -1;
  for (let t = 0; t < seconds && !s.ended; t += 1 / hz) {
    pump(s);
    if (trick && s.airborne && s.airV > minAirV && (!oncePerAir || trickedAir !== s.airs)) {
      if (startBowlTrick(s, trick)) trickedAir = s.airs;
    }
    advanceBowl(s, 1 / hz);
    if (s.airborne && firstAir === null) firstAir = s.elapsed;
  }
  return { s, firstAir };
}

test('without pumping she never launches', () => {
  const { s } = play(60, { pump: (st) => setPump(st, false) });
  assert.equal(s.airs, 0);
  assert.equal(s.score, 0);
});

test('well timed pumping launches out of the bowl quickly', () => {
  const { s, firstAir } = play(60);
  assert.ok(firstAir !== null && firstAir < 3.5, `first air at ${firstAir}`);
  assert.ok(s.airs > 10);
  assert.ok(s.score > 0);
});

test('simply holding pump climbs back out of the bowl', () => {
  const { s, firstAir } = play(60, { pump: (st) => setPump(st, true) });
  assert.ok(firstAir !== null && firstAir < 3, `first air at ${firstAir}`);
  assert.ok(s.airs > 10);
});

test('holding pump from a dead stop at the bottom gets her moving again', () => {
  const s = createBowl();
  s.s = 0;
  s.v = 0;
  setPump(s, true);
  for (let i = 0; i < 300 && !s.airborne; i++) advanceBowl(s, 1 / 60);
  assert.ok(s.airs >= 1);
});

test('tricks only start in the air', () => {
  const s = createBowl();
  assert.equal(startBowlTrick(s, 'grab'), false);
  s.airborne = true;
  assert.equal(startBowlTrick(s, 'grab'), true);
  assert.equal(startBowlTrick(s, 'spin'), true);
  assert.equal(s.queuedTrick, 'spin');
  assert.equal(startBowlTrick(s, 'kick'), false);
});

test('landing mid trick bails and resets the combo', () => {
  const s = createBowl();
  for (let i = 0; i < 600 && !s.airborne; i++) { smartPump(s); advanceBowl(s, 1 / 60); }
  assert.ok(s.airborne);
  s.combo = 2;
  startBowlTrick(s, 'spin');
  s.trickTime = 0;
  while (s.airborne) { s.trickTime = 0; advanceBowl(s, 1 / 120); }
  assert.equal(s.bails, 1);
  assert.equal(s.combo, 0);
  assert.equal(s.bruises, 1);
  assert.ok(s.fallen > 0);
});

test('a finished trick banks points on landing', () => {
  const s = createBowl();
  let banked = false;
  for (let i = 0; i < 60 * 30 && !banked; i++) {
    smartPump(s);
    if (s.airborne && s.airV > 400) startBowlTrick(s, 'grab');
    advanceBowl(s, 1 / 60);
    banked = s.tricksLanded > 0;
  }
  assert.ok(banked);
  assert.ok(s.score > 0);
});

test('the run ends after the timer', () => {
  const { s } = play(60);
  assert.equal(s.ended, true);
  // She finishes any air in progress before the run ends, which is at most about one air.
  assert.ok(s.elapsed >= BOWL_DURATION && s.elapsed < BOWL_DURATION + 1.2, `elapsed ${s.elapsed}`);
  assert.equal(s.airborne, false);
});

test('results match across frame rates', () => {
  // Pump toggles on whole 0.6s steps so every frame rate samples the same input.
  const run = (hz) => {
    const s = createBowl();
    const frames = hz * 3;
    for (let i = 0; i < frames; i++) {
      setPump(s, Math.floor(i / (hz * 0.6)) % 2 === 0);
      advanceBowl(s, 1 / hz);
    }
    return s;
  };
  const [a, b, c] = [run(30), run(60), run(120)];
  for (const other of [b, c]) {
    assert.ok(Math.abs(a.s - other.s) < 25, `${a.s} vs ${other.s}`);
    assert.ok(Math.abs(a.v - other.v) < 40, `${a.v} vs ${other.v}`);
    assert.equal(a.airs, other.airs);
  }
});

test('goal tiers and next goals', () => {
  assert.equal(goalTier(0), 0);
  assert.equal(goalTier(2000), 1);
  assert.equal(goalTier(6000), 2);
  assert.equal(goalTier(99999), 3);
  assert.equal(nextGoal(0).tier, 1);
  assert.equal(nextGoal(7000).tier, 3);
  assert.equal(nextGoal(12000), null);
});

test('backflip fits in the air and banks points', () => {
  assert.ok(BOWL_TRICKS.flip.duration <= 0.5);
  const { s } = play(60, { trick: 'flip', seconds: 30, oncePerAir: true, minAirV: 600 });
  assert.ok(s.tricksLanded > 0);
  assert.ok(s.tricksLanded > s.bails);
  assert.ok(s.score > 0);
});

function rideUntil(s, done, max = 60 * 30) {
  for (let i = 0; i < max && !done(s); i++) { smartPump(s); advanceBowl(s, 1 / 120); }
}

test('a perfectly timed lip handstand stalls on the coping and scores', () => {
  const s = createBowl();
  rideUntil(s, st => st.airs >= 2 && !st.airborne && timeToCoping(st) <= LIP_HANDSTAND.perfectWindow * 0.8
    && Math.abs(st.v) >= LIP_HANDSTAND.minSpeed);
  const before = s.score;
  assert.equal(armLipHandstand(s), true);
  assert.equal(s.lipArmed, 'perfect');
  rideUntil(s, st => st.stall > 0, 240);
  assert.ok(s.stall > 0, 'stalls on the lip');
  assert.equal(s.airborne, false);
  assert.ok(s.score >= before + LIP_HANDSTAND.perfectPoints);
  assert.equal(s.perfects, 1);
  assert.equal(s.streak, 1);
  rideUntil(s, st => st.stall === 0, 240);
  assert.ok(Math.abs(s.v) > LIP_HANDSTAND.minSpeed * 0.8, 'drops back in with speed');
});

test('pressing the handstand too early misses', () => {
  const s = createBowl();
  rideUntil(s, st => st.airs >= 2 && !st.airborne && timeToCoping(st) > LIP_HANDSTAND.goodWindow + 0.02 && timeToCoping(st) < 1
    && Math.abs(st.v) >= LIP_HANDSTAND.minSpeed);
  s.streak = 3;
  assert.equal(armLipHandstand(s), false);
  assert.equal(s.lipArmed, 'miss');
  rideUntil(s, st => st.bruises > 0, 240);
  assert.equal(s.bruises, 1);
  assert.equal(s.streak, 0);
  assert.equal(s.stall, 0);
  assert.equal(s.airborne, false);
  assert.ok(s.fallen > 0);
  const pos = s.s;
  rideUntil(s, st => st.fallen === 0, 240);
  assert.ok(Math.abs(s.s) < Math.abs(pos), 'rides back into the bowl');
});

test('a press far from the coping is ignored', () => {
  const s = createBowl();
  rideUntil(s, st => st.airs >= 1 && !st.airborne && timeToCoping(st) > 1.2 && isFinite(timeToCoping(st)));
  assert.equal(armLipHandstand(s), false);
  assert.equal(s.lipArmed, null);
  assert.equal(s.bruises, 0);
});

test('chaining two tricks in one air banks a chain bonus', () => {
  const s = createBowl();
  // Kick plus grab needs 0.75s of air, so wait for a big launch.
  rideUntil(s, st => st.airborne && st.airV > 800);
  const start = s.score;
  assert.equal(startBowlTrick(s, 'kick'), true);
  assert.equal(startBowlTrick(s, 'grab'), true);
  while (s.airborne) advanceBowl(s, 1 / 120);
  assert.equal(s.bails, 0, 'lands clean');
  assert.equal(s.tricksLanded, 2);
  assert.ok(s.score - start >= BOWL_TRICKS.kick.points + BOWL_TRICKS.grab.points + 100);
});

test('a trick started right at takeoff scores more than a late one', () => {
  const run = (late) => {
    const s = createBowl();
    rideUntil(s, st => st.airborne && st.airV > 300);
    const startScore = s.score;
    if (late) while (s.airTime <= 0.2) advanceBowl(s, 1 / 120);
    assert.equal(startBowlTrick(s, 'grab'), true);
    assert.equal(s.trickGrade, late ? 'good' : 'perfect');
    while (s.airborne) advanceBowl(s, 1 / 120);
    return s.score - startScore;
  };
  assert.ok(run(false) > run(true));
});
