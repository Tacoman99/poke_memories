import assert from 'node:assert/strict';
import test from 'node:test';
import { bowlRewards, memoryRewards, outfitProgress } from '../progression.ts';

const run = (overrides = {}) => ({
  mode: 'ENDLESS', collected: 0, trickScore: 0, distance: 100, completed: false, ...overrides,
});

test('memories require five actual collected balls, not rail points', () => {
  assert.equal(memoryRewards(0, false, run({ collected: 1, trickScore: 1000 })).earned, 0);
  assert.equal(memoryRewards(0, false, run({ collected: 5 })).earned, 1);
  assert.equal(memoryRewards(0, false, run({ collected: 11 })).earned, 2);
});

test('collection milestones accumulate across both modes', () => {
  assert.equal(memoryRewards(4, false, run({ collected: 1, mode: 'COURSE' })).earned, 1);
  assert.equal(memoryRewards(6, false, run({ collected: 3 })).earned, 0);
});

test('a first course finish adds one memory, including with no balls', () => {
  assert.deepEqual(memoryRewards(0, false, run({ mode: 'COURSE', completed: true })), {
    newTotal: 0, firstClear: true, earned: 1,
  });
  assert.equal(memoryRewards(4, false, run({ mode: 'COURSE', completed: true, collected: 1 })).earned, 2);
});

test('replays and failed courses do not grant another finish reward', () => {
  assert.equal(memoryRewards(0, true, run({ mode: 'COURSE', completed: true })).earned, 0);
  assert.equal(memoryRewards(0, false, run({ mode: 'COURSE' })).earned, 0);
  assert.equal(memoryRewards(0, false, run({ completed: true })).earned, 0);
});

test('each newly beaten bowl goal grants one memory', () => {
  assert.deepEqual(bowlRewards(0, 2), { newTier: 2, earned: 2 });
  assert.deepEqual(bowlRewards(2, 3), { newTier: 3, earned: 1 });
  assert.deepEqual(bowlRewards(3, 1), { newTier: 3, earned: 0 });
});

test('bowl medals push outfit progress forward', () => {
  assert.equal(outfitProgress(10, 0), 10);
  assert.equal(outfitProgress(10, 3), 85);
});
