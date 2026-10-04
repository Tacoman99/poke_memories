import assert from 'node:assert/strict';
import test from 'node:test';
import { skaterPose } from '../components/characterPose.ts';

const length = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);

test('skating, airborne, grind and landing poses keep consistent limb lengths', () => {
  for (const [grind, air, velocity, landing] of [
    [false, false, 0, 0], [true, false, 0, 0], [false, true, -580, 0],
    [false, true, 500, 0], [false, false, 0, 1],
  ]) {
    for (let time = 0; time < 2000; time += 16) {
      const pose = skaterPose(time, grind, air, velocity, landing);
      for (const side of ['back', 'front']) {
        assert.ok(Math.abs(length(pose.hip, pose[`${side}Knee`]) - 33) < 0.001);
        assert.ok(Math.abs(length(pose[`${side}Knee`], pose[`${side}Ankle`]) - 35) < 0.001);
        assert.ok(Math.abs(length(pose.shoulder, pose[`${side}Elbow`]) - 22) < 0.001);
        assert.ok(Math.abs(length(pose[`${side}Elbow`], pose[`${side}Wrist`]) - 21) < 0.001);
      }
    }
  }
});

test('supporting skate wheels stay on the ground instead of floating', () => {
  for (let time = 0; time < 2000; time += 16) {
    const pose = skaterPose(time, false, false, 0, 0);
    assert.equal(pose.frontAnkle.y + 8, 0);
    assert.ok(pose.backAnkle.y + 8 <= 0);
  }
});

test('character motion is continuous and a landing bends the knees', () => {
  const normal = skaterPose(0, false, false, 0, 0);
  const landed = skaterPose(0, false, false, 0, 1);
  assert.ok(landed.hip.y > normal.hip.y);
  assert.equal(landed.frontAnkle.y, normal.frontAnkle.y);
  for (let time = 0; time < 2000; time += 16) {
    const a = skaterPose(time, false, false, 0, 0);
    const b = skaterPose(time + 16, false, false, 0, 0);
    assert.ok(length(a.backAnkle, b.backAnkle) < 3);
    assert.ok(length(a.frontKnee, b.frontKnee) < 3);
  }
});

test('rolling has distinct load, push, recovery and glide poses with arm counter motion', () => {
  const poseAt = phase => skaterPose(phase * 1200, false, false, 0, 0);
  const glide = poseAt(0), load = poseAt(0.18), push = poseAt(0.44), recovery = poseAt(0.62);
  assert.ok(load.hip.y - glide.hip.y >= 4, 'load bends the supporting leg');
  assert.ok(glide.backAnkle.x - push.backAnkle.x >= 18, 'push extends the trailing skate');
  assert.ok(push.backAnkle.y - recovery.backAnkle.y >= 14, 'recovery lifts the pushing skate');
  assert.ok(push.shoulder.x - load.shoulder.x >= 8, 'weight moves forward into the push');
  assert.ok(push.frontWrist.x - load.frontWrist.x >= 8, 'front arm swings with the push');
  assert.ok(push.backWrist.x < load.backWrist.x, 'rear arm counters the front arm');
  const beforeWrap = poseAt(0.99999), afterWrap = poseAt(1.00001);
  for (const part of ['hip', 'shoulder', 'backAnkle', 'frontWrist', 'backWrist']) {
    assert.ok(length(beforeWrap[part], afterWrap[part]) < 0.01, `${part} loops without a snap`);
  }
});

test('jump extends at takeoff, tucks at apex and opens again for landing', () => {
  const takeoff = skaterPose(0, false, true, -650, 0);
  const apex = skaterPose(360, false, true, 0, 0);
  const descent = skaterPose(700, false, true, 650, 0);
  assert.ok(apex.hip.y - takeoff.hip.y >= 11);
  assert.ok(takeoff.backAnkle.y - apex.backAnkle.y >= 16);
  assert.ok(apex.frontWrist.y - apex.shoulder.y < takeoff.frontWrist.y - takeoff.shoulder.y);
  assert.equal(descent.hip.y, takeoff.hip.y);
  assert.equal(descent.backAnkle.y, takeoff.backAnkle.y);
  for (const pose of [takeoff, apex, descent]) assert.equal(pose.frontAnkle.y + 8, 0);
});

test('grinding shifts hips and balance arms without lifting either skate from the rail', () => {
  const poses = Array.from({ length: 100 }, (_, i) => skaterPose(i * 16, true, false, 0, 0));
  const range = values => Math.max(...values) - Math.min(...values);
  assert.ok(range(poses.map(pose => pose.hip.x)) > 4.9);
  assert.ok(range(poses.map(pose => pose.frontWrist.y - pose.shoulder.y)) > 11);
  assert.ok(range(poses.map(pose => pose.backWrist.y - pose.shoulder.y)) > 9);
  for (const pose of poses) {
    assert.equal(pose.frontAnkle.y + 8, 0);
    assert.equal(pose.backAnkle.y + 8, 0);
  }
});

test('blended transitions preserve reach, contact and continuous motion', () => {
  for (let weight = 0; weight < 1; weight += 0.01) {
    const a = skaterPose(528, false, true, -350, 0, weight, 1 - weight);
    const b = skaterPose(528, false, true, -350, 0, weight + 0.01, 0.99 - weight);
    assert.equal(a.frontAnkle.y + 8, 0);
    assert.ok(a.backAnkle.y + 8 <= 0);
    for (const part of ['hip', 'shoulder', 'backAnkle', 'frontWrist', 'backWrist']) {
      assert.ok(length(a[part], b[part]) < 1, `${part} blends gradually`);
    }
    for (const side of ['back', 'front']) {
      assert.ok(Math.abs(length(a.hip, a[`${side}Knee`]) - 33) < 0.001);
      assert.ok(Math.abs(length(a[`${side}Knee`], a[`${side}Ankle`]) - 35) < 0.001);
      assert.ok(Math.abs(length(a.shoulder, a[`${side}Elbow`]) - 22) < 0.001);
      assert.ok(Math.abs(length(a[`${side}Elbow`], a[`${side}Wrist`]) - 21) < 0.001);
    }
  }
});

test('air tricks change the pose while keeping limbs intact, and no trick leaves the pose alone', () => {
  const base = skaterPose(300, false, true, 0, 0);
  const same = skaterPose(300, false, true, 0, 0, 1, 0, undefined, null, 0.5);
  assert.deepEqual(same, base);
  const grab = skaterPose(300, false, true, 0, 0, 1, 0, undefined, 'grab', 0.5);
  assert.ok(length(grab.frontWrist, grab.frontAnkle) < 14, 'grab reaches the skate');
  assert.ok(grab.frontAnkle.y < base.frontAnkle.y - 20, 'grab pulls the knee up');
  const kick = skaterPose(300, false, true, 0, 0, 1, 0, undefined, 'kick', 0.5);
  assert.ok(kick.frontAnkle.x > base.frontAnkle.x + 10, 'kick extends the front leg');
  const spin = skaterPose(300, false, true, 0, 0, 1, 0, undefined, 'spin', 0.5);
  assert.ok(spin.spin > 2 && spin.spin < 4.5);
  const hand = skaterPose(300, false, true, 0, 0, 1, 0, undefined, 'handstand', 0.5);
  assert.ok(Math.abs(hand.flip - Math.PI) < 0.3, 'handstand turns her upside down');
  assert.ok(hand.frontWrist.y < hand.shoulder.y - 30 && hand.frontAnkle.y > hand.hip.y + 60, 'arms reach and legs extend for the invert');
  const flip = skaterPose(300, false, true, 0, 0, 1, 0, undefined, 'flip', 0.5);
  assert.ok(Math.abs(Math.abs(flip.flip) - Math.PI) < 0.8, 'backflip is halfway round');
  assert.ok(Math.abs(skaterPose(300, false, true, 0, 0, 1, 0, undefined, 'flip', 1).flip + Math.PI * 2) < 0.01, 'backflip completes a full turn');
  assert.ok(flip.frontAnkle.y < base.frontAnkle.y - 15, 'backflip tucks the knees');
  for (const trick of ['grab', 'kick', 'spin', 'handstand', 'flip']) {
    for (let p = 0; p <= 1; p += 0.05) {
      const pose = skaterPose(300, false, true, -200, 0, 1, 0, undefined, trick, p);
      for (const side of ['back', 'front']) {
        assert.ok(Math.abs(length(pose.hip, pose[`${side}Knee`]) - 33) < 0.001);
        assert.ok(Math.abs(length(pose[`${side}Knee`], pose[`${side}Ankle`]) - 35) < 0.001);
      }
    }
  }
});
