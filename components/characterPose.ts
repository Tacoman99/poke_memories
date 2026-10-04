export interface Point { x: number; y: number }

export interface AnimationTiming {
  rollTime: number;
  airTime: number;
  grindTime: number;
}

const ease = (value: number) => {
  const t = Math.max(0, Math.min(1, value));
  return t * t * (3 - 2 * t);
};
const mix = (a: number, b: number, weight: number) => a + (b - a) * weight;
const ROLL_CYCLE = 1.2;
const PUSH_KEYS = [
  { phase: 0, ankleX: -13, ankleY: -8, hipX: 0, hipY: -65, lean: 9, arm: 0 },
  { phase: 0.18, ankleX: -15, ankleY: -8, hipX: -2, hipY: -61, lean: 10, arm: -3 },
  { phase: 0.44, ankleX: -31, ankleY: -8, hipX: 2, hipY: -65, lean: 14, arm: 5 },
  { phase: 0.62, ankleX: -20, ankleY: -22, hipX: 3, hipY: -65, lean: 12, arm: 2 },
  { phase: 0.8, ankleX: -10, ankleY: -8, hipX: 0, hipY: -65, lean: 9, arm: 0 },
  { phase: 1, ankleX: -13, ankleY: -8, hipX: 0, hipY: -65, lean: 9, arm: 0 },
];

function rollingPose(seconds: number) {
  const phase = ((seconds / ROLL_CYCLE) % 1 + 1) % 1;
  const next = PUSH_KEYS.findIndex(key => key.phase > phase);
  const a = PUSH_KEYS[next - 1], b = PUSH_KEYS[next];
  const weight = ease((phase - a.phase) / (b.phase - a.phase));
  return {
    ankleX: mix(a.ankleX, b.ankleX, weight), ankleY: mix(a.ankleY, b.ankleY, weight),
    hipX: mix(a.hipX, b.hipX, weight), hipY: mix(a.hipY, b.hipY, weight),
    lean: mix(a.lean, b.lean, weight), arm: mix(a.arm, b.arm, weight),
  };
}

function reachableWrist(shoulder: Point, wrist: Point): Point {
  const dx = wrist.x - shoulder.x, dy = wrist.y - shoulder.y;
  const scale = Math.min(1, 42 / Math.max(0.001, Math.hypot(dx, dy)));
  return { x: shoulder.x + dx * scale, y: shoulder.y + dy * scale };
}

export function bendJoint(start: Point, end: Point, upper: number, lower: number, direction = 1): Point {
  const dx = end.x - start.x, dy = end.y - start.y;
  const distance = Math.max(0.001, Math.hypot(dx, dy));
  const reach = Math.min(distance, upper + lower - 0.001);
  const along = (upper * upper - lower * lower + reach * reach) / (2 * reach);
  const bend = Math.sqrt(Math.max(0, upper * upper - along * along)) * direction;
  return {
    x: start.x + dx / distance * along + dy / distance * bend,
    y: start.y + dy / distance * along - dx / distance * bend,
  };
}

export type TrickPose = 'grab' | 'kick' | 'spin' | 'handstand' | 'flip' | null;

export function skaterPose(time: number, grinding: boolean, airborne: boolean, velocity: number, landing: number,
  airBlend = airborne ? 1 : 0, grindBlend = grinding ? 1 : 0, timing?: AnimationTiming,
  trick: TrickPose = null, trickProgress = 0) {
  const clocks = timing ?? { rollTime: time / 1000, airTime: time / 1000, grindTime: time / 1000 };
  const roll = rollingPose(clocks.rollTime);
  const air = Math.max(0, Math.min(1, airBlend));
  const grind = Math.max(0, Math.min(1, grindBlend)) * (1 - air);
  const rolling = 1 - air - grind;
  const tuck = 1 - ease(Math.abs(velocity) / 650);
  const balance = Math.sin(clocks.grindTime * 5);
  const counter = Math.sin(clocks.grindTime * 5 + 0.8);
  const blend = (rollValue: number, airValue: number, grindValue: number) =>
    rollValue * rolling + airValue * air + grindValue * grind;
  const progress = Math.max(0, Math.min(1, trickProgress));
  const trickWeight = trick ? ease(Math.min(1, Math.sin(Math.PI * progress) * 1.6)) * air : 0;
  const grab = trick === 'grab' ? trickWeight : 0;
  const kick = trick === 'kick' ? trickWeight : 0;
  const hand = trick === 'handstand' ? trickWeight : 0;
  const flipTuck = trick === 'flip' ? trickWeight : 0;
  const hip = {
    x: blend(roll.hipX, 1 - tuck * 3, -4 + balance * 2.5) - kick * 3,
    y: blend(roll.hipY, -64 + tuck * 11, -56 + Math.sin(clocks.grindTime * 10) * 1.5) + landing * 5 + grab * 6,
  };
  const shoulder = {
    x: mix(hip.x + blend(roll.lean, 12 - tuck * 3, 15 + counter * 3) + grab * 8 - kick * 4 + flipTuck * 6, hip.x, hand),
    y: hip.y - 29,
  };
  const head = { x: shoulder.x + 3 - counter * grind * 1.5, y: shoulder.y - 22 };
  const restBack = {
    x: blend(roll.ankleX, -17 - tuck * 7, -17),
    y: blend(roll.ankleY, -10 - tuck * 16, -8),
  };
  const backAnkle = {
    x: mix(mix(restBack.x, hip.x - 6, hand), hip.x - 4, flipTuck),
    y: mix(mix(restBack.y, hip.y + 64, hand), hip.y + 26, flipTuck),
  };
  const baseFront = { x: blend(16, 19 + tuck * 4, 20), y: -8 };
  const frontAnkle = {
    x: mix(mix(mix(mix(baseFront.x, 14, grab), 36, kick), hip.x + 6, hand), hip.x + 8, flipTuck),
    y: mix(mix(mix(mix(baseFront.y, -36, grab), -24, kick), hip.y + 64, hand), hip.y + 24, flipTuck),
  };
  const restBackWrist = {
    x: blend(-11 - roll.arm, -18 - tuck * 3, -24 + balance * 2),
    y: shoulder.y + blend(31 + roll.arm * 0.5, 27 - tuck * 9, 12 - counter * 6),
  };
  const backWrist = reachableWrist(shoulder, {
    x: mix(mix(restBackWrist.x, shoulder.x - 6, hand), backAnkle.x + 3, flipTuck),
    y: mix(mix(restBackWrist.y, shoulder.y - 40, hand), backAnkle.y - 14, flipTuck),
  });
  const baseWrist = {
    x: blend(20 + roll.arm, 30 + tuck * 5, 45 + counter * 3) - kick * 12,
    y: shoulder.y + blend(36 - roll.arm, 24 - tuck * 12, 12 + balance * 6) - kick * 14,
  };
  const frontWrist = reachableWrist(shoulder, {
    x: mix(mix(mix(baseWrist.x, frontAnkle.x + 4, grab), shoulder.x + 6, hand), frontAnkle.x + 2, flipTuck),
    y: mix(mix(mix(baseWrist.y, frontAnkle.y - 4, grab), shoulder.y - 40, hand), frontAnkle.y - 14, flipTuck),
  });
  const hairSway = blend(
    Math.sin(clocks.rollTime * 2 * Math.PI / ROLL_CYCLE - 0.6) * 3,
    -velocity / 650 * 6 + Math.sin(clocks.airTime * 8) * 2,
    Math.sin(clocks.grindTime * 5 - 0.8) * 3,
  );
  const clothSway = Math.sin(clocks.rollTime * 7 + 1) * rolling +
    Math.sin(clocks.airTime * 9) * air * 2 + balance * grind;
  return {
    hip, shoulder, head, backAnkle, frontAnkle, backWrist, frontWrist, hairSway, clothSway,
    wheelAngle: clocks.rollTime * 120,
    spin: trick === 'spin' ? ease(progress) * Math.PI * 2 * air : 0,
    // In plane body rotation round the hip: a handstand turns her over onto her raised hands, a flip goes fully backwards.
    flip: trick === 'flip' ? -ease(progress) * Math.PI * 2 * air : hand * Math.PI,
    backKnee: bendJoint(hip, backAnkle, 33, 35),
    frontKnee: bendJoint(hip, frontAnkle, 33, 35),
    backElbow: bendJoint(shoulder, backWrist, 22, 21, -1),
    frontElbow: bendJoint(shoulder, frontWrist, 22, 21),
  };
}
