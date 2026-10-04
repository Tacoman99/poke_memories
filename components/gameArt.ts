import type { CompanionType, Outfit } from '../types';
import { skaterPose } from './characterPose';
import type { AnimationTiming, Point, TrickPose } from './characterPose';

export function drawBall(ctx: CanvasRenderingContext2D, x: number, y: number, radius = 14) {
  ctx.save();
  ctx.shadowColor = '#fb7185'; ctx.shadowBlur = 14;
  ctx.fillStyle = '#f43f5e';
  ctx.beginPath(); ctx.arc(x, y, radius, 0, Math.PI * 2); ctx.fill();
  ctx.shadowBlur = 0;
  ctx.fillStyle = '#fff';
  ctx.beginPath(); ctx.arc(x, y, radius, 0, Math.PI); ctx.fill();
  ctx.strokeStyle = '#51394b'; ctx.lineWidth = 2.5;
  ctx.beginPath(); ctx.arc(x, y, radius, 0, Math.PI * 2); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(x - radius, y); ctx.lineTo(x + radius, y); ctx.stroke();
  ctx.fillStyle = '#fff';
  ctx.beginPath(); ctx.arc(x, y, 4, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
  ctx.fillStyle = '#fecdd3'; ctx.beginPath(); ctx.arc(x - 5, y - 7, 3, 0, Math.PI * 2); ctx.fill();
  ctx.restore();
}

export function drawSkater(
  ctx: CanvasRenderingContext2D, x: number, foot: number, time: number,
  outfit: Outfit, grinding: boolean, airborne: boolean, slope: number, velocity = 0, landing = 0,
  airBlend = airborne ? 1 : 0, grindBlend = grinding ? 1 : 0,
  timing?: AnimationTiming, trick: TrickPose = null, trickProgress = 0,
) {
  const pose = skaterPose(time, grinding, airborne, velocity, landing, airBlend, grindBlend, timing, trick, trickProgress);
  const skin = '#e5b49f';
  const shade = '#c58d82';
  const outline = '#50323c';
  const { hip, shoulder, head } = pose;
  ctx.save();
  ctx.translate(x, foot);
  if (grinding) ctx.rotate(Math.atan(slope));
  if (pose.flip) {
    ctx.translate(hip.x, hip.y); ctx.rotate(pose.flip); ctx.translate(-hip.x, -hip.y);
  }
  if (pose.spin) {
    ctx.translate(hip.x, hip.y); ctx.rotate(-pose.spin); ctx.translate(-hip.x, -hip.y);
  }
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';
  const segment = (a: Point, b: Point, width: number, color: string) => {
    ctx.strokeStyle = outline; ctx.lineWidth = width + 1.8;
    ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
    ctx.strokeStyle = color; ctx.lineWidth = width;
    ctx.stroke();
    ctx.strokeStyle = '#f7d1b9'; ctx.lineWidth = 1.2;
    ctx.beginPath(); ctx.moveTo(a.x + 1, a.y); ctx.lineTo(b.x + 1, b.y); ctx.stroke();
  };
  const skate = (ankle: Point) => {
    const boot = ctx.createLinearGradient(ankle.x, ankle.y - 11, ankle.x, ankle.y + 4);
    boot.addColorStop(0, outfit.trimColor); boot.addColorStop(0.5, outfit.skateColor); boot.addColorStop(1, '#752947');
    ctx.fillStyle = boot; ctx.strokeStyle = outline; ctx.lineWidth = 1.2;
    ctx.beginPath(); ctx.moveTo(ankle.x - 6, ankle.y - 9); ctx.lineTo(ankle.x + 4, ankle.y - 9);
    ctx.lineTo(ankle.x + 6, ankle.y - 1); ctx.quadraticCurveTo(ankle.x + 17, ankle.y, ankle.x + 15, ankle.y + 4);
    ctx.lineTo(ankle.x - 7, ankle.y + 4); ctx.closePath(); ctx.fill(); ctx.stroke();
    ctx.strokeStyle = '#fff4f1'; ctx.lineWidth = 1;
    for (let i = 0; i < 3; i++) {
      ctx.beginPath(); ctx.moveTo(ankle.x - 2, ankle.y - 6 + i * 2); ctx.lineTo(ankle.x + 4, ankle.y - 5 + i * 2); ctx.stroke();
    }
    ctx.fillStyle = '#513646'; ctx.fillRect(ankle.x - 7, ankle.y + 4, 22, 1.5);
    for (const wheel of [ankle.x - 3, ankle.x + 11]) {
      ctx.strokeStyle = '#71394d'; ctx.lineWidth = 1;
      ctx.fillStyle = '#dc709e'; ctx.beginPath(); ctx.arc(wheel, ankle.y + 5, 3, 0, Math.PI * 2); ctx.fill();
      ctx.stroke();
      ctx.fillStyle = '#ffe2ed'; ctx.beginPath(); ctx.arc(wheel, ankle.y + 5, 1, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = '#ffe2ed'; ctx.lineWidth = 0.7;
      for (let spoke = 0; spoke < 3; spoke++) {
        const angle = pose.wheelAngle + spoke * Math.PI * 2 / 3;
        ctx.beginPath(); ctx.moveTo(wheel, ankle.y + 5);
        ctx.lineTo(wheel + Math.cos(angle) * 2.5, ankle.y + 5 + Math.sin(angle) * 2.5); ctx.stroke();
      }
    }
  };
  const leg = (knee: Point, ankle: Point, color: string) => {
    segment(hip, knee, 8, color);
    segment(knee, ankle, 5.5, color);
    segment({ x: ankle.x, y: ankle.y - 13 }, ankle, 6.5, '#f4e9ec');
    skate(ankle);
  };
  const arm = (elbow: Point, wrist: Point, color: string) => {
    segment(shoulder, elbow, 5.2, color);
    const cuff = { x: shoulder.x + (elbow.x - shoulder.x) * 0.68, y: shoulder.y + (elbow.y - shoulder.y) * 0.68 };
    segment(shoulder, cuff, 7.5, outfit.bodyColor);
    ctx.fillStyle = '#fff7fb'; ctx.beginPath(); ctx.arc(cuff.x, cuff.y, 4.5, 0, Math.PI * 2); ctx.fill();
    segment(elbow, wrist, 4, color);
    drawHand(ctx, wrist, Math.atan2(wrist.y - elbow.y, wrist.x - elbow.x), color, outline);
  };
  const wave = pose.hairSway;
  const hair = ctx.createLinearGradient(head.x, head.y - 12, head.x - 30, shoulder.y + 29);
  hair.addColorStop(0, outfit.hairColor); hair.addColorStop(0.28, outfit.hairColor);
  hair.addColorStop(0.7, outfit.hairHighlight); hair.addColorStop(1, outfit.hairColor);
  ctx.fillStyle = hair; ctx.strokeStyle = '#502637'; ctx.lineWidth = 1.2;
  ctx.beginPath(); ctx.moveTo(head.x + 5, head.y - 12);
  ctx.bezierCurveTo(head.x - 22, head.y - 21, head.x - 24, head.y + 8, head.x - 29, head.y + 19);
  ctx.bezierCurveTo(head.x - 38, head.y + 24 + wave, head.x - 23, head.y + 33, head.x - 41, head.y + 41);
  ctx.bezierCurveTo(head.x - 48 - wave, head.y + 46, head.x - 31 - wave, head.y + 52, head.x - 39 - wave * 1.5, head.y + 58);
  ctx.bezierCurveTo(head.x - 12, head.y + 55, head.x - 17, head.y + 26, head.x - 5, head.y + 14);
  ctx.closePath(); ctx.fill(); ctx.stroke();
  ctx.strokeStyle = outfit.hairHighlight; ctx.lineWidth = 1.4;
  for (let i = 0; i < 3; i++) {
    ctx.beginPath(); ctx.moveTo(head.x - 9 - i * 4, head.y - 5);
    ctx.bezierCurveTo(head.x - 29 - i * 2, head.y + 12, head.x - 13 - i * 4, head.y + 28 + wave, head.x - 30 - i * 3, head.y + 46); ctx.stroke();
  }
  arm(pose.backElbow, pose.backWrist, shade);
  leg(pose.backKnee, pose.backAnkle, shade);
  segment({ x: head.x - 1, y: head.y + 7 }, { x: shoulder.x + 2, y: shoulder.y + 2 }, 7, skin);
  const shirt = ctx.createLinearGradient(hip.x - 8, shoulder.y, hip.x + 14, hip.y);
  shirt.addColorStop(0, outfit.trimColor); shirt.addColorStop(0.3, outfit.bodyColor); shirt.addColorStop(1, outfit.bodyColor);
  ctx.fillStyle = shirt; ctx.strokeStyle = outline; ctx.lineWidth = 1.3;
  ctx.beginPath(); ctx.moveTo(shoulder.x - 8, shoulder.y - 1);
  ctx.quadraticCurveTo(shoulder.x + 1, shoulder.y + 8, shoulder.x + 9, shoulder.y + 1);
  ctx.quadraticCurveTo(shoulder.x + 16, shoulder.y + 17, hip.x + 9, hip.y - 4);
  ctx.lineTo(hip.x - 9, hip.y - 4); ctx.quadraticCurveTo(hip.x - 3, shoulder.y + 16, shoulder.x - 8, shoulder.y - 1);
  ctx.closePath(); ctx.fill(); ctx.stroke();
  ctx.strokeStyle = '#fff7fb'; ctx.lineWidth = 4;
  ctx.beginPath(); ctx.moveTo(shoulder.x - 6, shoulder.y + 1);
  ctx.quadraticCurveTo(shoulder.x + 1, shoulder.y + 7, shoulder.x + 8, shoulder.y + 2); ctx.stroke();
  leg(pose.frontKnee, pose.frontAnkle, skin);
  ctx.fillStyle = shirt; ctx.strokeStyle = outline; ctx.lineWidth = 1;
  ctx.beginPath(); ctx.moveTo(hip.x - 9, hip.y - 7); ctx.lineTo(hip.x + 10, hip.y - 7);
  ctx.lineTo(hip.x + 14, hip.y + 8 + pose.clothSway);
  ctx.quadraticCurveTo(hip.x, hip.y + 11, hip.x - 12, hip.y + 8 - pose.clothSway);
  ctx.closePath(); ctx.fill(); ctx.stroke();
  ctx.strokeStyle = '#fff7fb'; ctx.lineWidth = 4;
  ctx.beginPath(); ctx.moveTo(hip.x - 11, hip.y + 8 - pose.clothSway);
  ctx.quadraticCurveTo(hip.x, hip.y + 11, hip.x + 13, hip.y + 8 + pose.clothSway); ctx.stroke();
  arm(pose.frontElbow, pose.frontWrist, skin);
  const tattooX = pose.frontElbow.x + (pose.frontWrist.x - pose.frontElbow.x) * 0.42;
  const tattooY = pose.frontElbow.y + (pose.frontWrist.y - pose.frontElbow.y) * 0.42;
  ctx.fillStyle = '#5b3a4a'; ctx.strokeStyle = '#3b2430'; ctx.lineWidth = 0.9;
  ctx.beginPath(); ctx.moveTo(tattooX, tattooY + 2.4);
  ctx.bezierCurveTo(tattooX - 3.4, tattooY - 0.4, tattooX - 1.8, tattooY - 3.2, tattooX, tattooY - 1.2);
  ctx.bezierCurveTo(tattooX + 1.8, tattooY - 3.2, tattooX + 3.4, tattooY - 0.4, tattooX, tattooY + 2.4);
  ctx.fill(); ctx.stroke();
  const face = ctx.createLinearGradient(head.x - 6, head.y, head.x + 11, head.y);
  face.addColorStop(0, shade); face.addColorStop(0.5, skin); face.addColorStop(1, '#f2c8ae');
  ctx.fillStyle = face; ctx.strokeStyle = outline; ctx.lineWidth = 1;
  ctx.beginPath(); ctx.moveTo(head.x - 8, head.y - 6);
  ctx.bezierCurveTo(head.x - 4, head.y - 16, head.x + 11, head.y - 12, head.x + 11, head.y - 3);
  ctx.lineTo(head.x + 14, head.y + 1); ctx.lineTo(head.x + 11, head.y + 3);
  ctx.quadraticCurveTo(head.x + 10, head.y + 13, head.x + 3, head.y + 11);
  ctx.quadraticCurveTo(head.x - 10, head.y + 8, head.x - 8, head.y - 6); ctx.fill(); ctx.stroke();
  ctx.fillStyle = '#49303a'; ctx.beginPath(); ctx.ellipse(head.x + 6, head.y - 1, 2.2, 1.6, 0.1, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = '#fff'; ctx.beginPath(); ctx.arc(head.x + 6.6, head.y - 1.6, 0.65, 0, Math.PI * 2); ctx.fill();
  ctx.strokeStyle = '#422934'; ctx.lineWidth = 1.1;
  ctx.beginPath(); ctx.moveTo(head.x + 2, head.y - 5); ctx.quadraticCurveTo(head.x + 5, head.y - 7, head.x + 9, head.y - 5); ctx.stroke();
  ctx.strokeStyle = '#a75666'; ctx.lineWidth = 1.4;
  ctx.beginPath(); ctx.moveTo(head.x + 5, head.y + 6); ctx.quadraticCurveTo(head.x + 8, head.y + 8, head.x + 10, head.y + 5); ctx.stroke();
  ctx.fillStyle = hair;
  ctx.beginPath(); ctx.moveTo(head.x - 10, head.y + 2);
  ctx.bezierCurveTo(head.x - 16, head.y - 16, head.x + 2, head.y - 19, head.x + 10, head.y - 10);
  ctx.quadraticCurveTo(head.x + 2, head.y - 13, head.x - 3, head.y - 1); ctx.lineTo(head.x - 6, head.y + 6); ctx.fill();
  ctx.restore();
}

function drawHand(ctx: CanvasRenderingContext2D, wrist: Point, angle: number, color: string, outline: string) {
  ctx.save();
  ctx.translate(wrist.x, wrist.y);
  ctx.rotate(angle);
  ctx.fillStyle = color; ctx.strokeStyle = outline; ctx.lineWidth = 0.8;
  const fingers = [
    { y: -1.8, length: 4.6 },
    { y: -0.6, length: 5.2 },
    { y: 0.6, length: 5 },
    { y: 1.7, length: 4.1 },
  ];
  for (const finger of fingers) {
    ctx.beginPath(); ctx.moveTo(3, finger.y);
    ctx.lineTo(3 + finger.length * 0.55, finger.y + 0.3);
    ctx.lineTo(3 + finger.length, finger.y + 1.1);
    ctx.lineWidth = 2.6; ctx.strokeStyle = outline; ctx.stroke();
    ctx.lineWidth = 1.5; ctx.strokeStyle = color; ctx.stroke();
  }
  ctx.lineWidth = 0.8; ctx.strokeStyle = outline;
  ctx.beginPath(); ctx.ellipse(1.8, 0, 3, 2.6, 0, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(1, -2.2); ctx.quadraticCurveTo(3, -4.2, 5, -3.6);
  ctx.lineWidth = 2.8; ctx.stroke();
  ctx.lineWidth = 1.7; ctx.strokeStyle = color; ctx.stroke();
  ctx.fillStyle = color; ctx.beginPath(); ctx.ellipse(1.8, 0, 2.4, 2, 0, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = 'rgba(255,255,255,0.28)'; ctx.beginPath(); ctx.ellipse(2.2, -0.8, 1.2, 0.6, 0, 0, Math.PI * 2); ctx.fill();
  ctx.restore();
}

export function drawCompanion(
  ctx: CanvasRenderingContext2D, x: number, y: number, time: number,
  reaction: 'idle' | 'happy' | 'grind' | 'worried', type: CompanionType,
) {
  ctx.save();
  const ty = y + Math.sin(time * 0.004) * 5 + (reaction === 'happy' ? Math.sin(time * 0.02) * 4 : 0);
  if (type === 'gardevoir') {
    const sway = Math.sin(time * 0.003) * 3;
    ctx.fillStyle = '#fff';
    ctx.beginPath(); ctx.moveTo(x - 8, ty + 4);
    ctx.quadraticCurveTo(x - 16 + sway, ty + 22, x - 10 + sway * 0.5, ty + 28);
    ctx.lineTo(x + 10 - sway * 0.5, ty + 28);
    ctx.quadraticCurveTo(x + 16 - sway, ty + 22, x + 8, ty + 4); ctx.closePath(); ctx.fill();
    ctx.beginPath(); ctx.ellipse(x, ty, 8, 12, 0, 0, 7); ctx.fill();
    ctx.beginPath(); ctx.ellipse(x, ty - 16, 9, 10, 0, 0, 7); ctx.fill();
    ctx.fillStyle = '#22c55e';
    ctx.beginPath(); ctx.moveTo(x, ty - 6); ctx.lineTo(x - 5, ty + 2); ctx.lineTo(x + 5, ty + 2); ctx.fill();
    ctx.beginPath(); ctx.moveTo(x + 6, ty - 24);
    ctx.quadraticCurveTo(x + 2, ty - 30, x - 4, ty - 26);
    ctx.quadraticCurveTo(x - 10, ty - 22, x - 8, ty - 16);
    ctx.lineTo(x - 5, ty - 16); ctx.quadraticCurveTo(x - 6, ty - 22, x - 2, ty - 24);
    ctx.quadraticCurveTo(x + 2, ty - 26, x + 6, ty - 24); ctx.fill();
    ctx.beginPath(); ctx.moveTo(x - 8, ty - 16);
    ctx.quadraticCurveTo(x - 18 + sway, ty - 10, x - 22 + sway * 1.5, ty - 18);
    ctx.quadraticCurveTo(x - 20 + sway, ty - 24, x - 12, ty - 20); ctx.fill();
    ctx.fillStyle = '#ef4444';
    ctx.beginPath(); ctx.moveTo(x, ty - 4); ctx.lineTo(x - 2, ty + 1); ctx.lineTo(x + 2, ty + 1); ctx.fill();
    for (const ex of [x - 4, x + 4]) {
      if (reaction === 'happy') {
        ctx.strokeStyle = '#ef4444'; ctx.lineWidth = 1.5;
        ctx.beginPath(); ctx.arc(ex, ty - 16, 2, Math.PI, 0); ctx.stroke();
      } else {
        ctx.beginPath(); ctx.ellipse(ex, ty - 16, reaction === 'worried' ? 2.5 : 2, 2.5, 0, 0, 7); ctx.fill();
      }
    }
    ctx.strokeStyle = '#fff'; ctx.lineWidth = 3;
    for (const side of [-1, 1]) {
      ctx.beginPath(); ctx.moveTo(x + side * 7, ty - 2);
      ctx.quadraticCurveTo(x + side * 14, ty + 6 - side * sway, x + side * 12, ty + 14); ctx.stroke();
      ctx.fillStyle = '#22c55e'; ctx.beginPath(); ctx.arc(x + side * 12, ty + 14, 2.5, 0, 7); ctx.fill();
    }
    if (reaction === 'grind') {
      ctx.globalAlpha = 0.3; ctx.strokeStyle = '#c084fc'; ctx.lineWidth = 2;
      for (let i = 0; i < 3; i++) {
        ctx.beginPath(); ctx.arc(x, ty - 4, 18 + Math.sin(time * 0.008 + i * 2) * 6, 0, 7); ctx.stroke();
      }
      ctx.globalAlpha = 1;
    }
  } else {
    ctx.fillStyle = '#facc15';
    ctx.beginPath(); ctx.arc(x, ty, 14, 0, 7); ctx.fill();
    for (const side of [-1, 1]) {
      ctx.fillStyle = '#facc15';
      ctx.beginPath(); ctx.moveTo(x + side * 8, ty - 12);
      ctx.lineTo(x + side * 14, ty - 28); ctx.lineTo(x + side * 2, ty - 16); ctx.fill();
      ctx.fillStyle = '#1f2937';
      ctx.beginPath(); ctx.moveTo(x + side * 11, ty - 23);
      ctx.lineTo(x + side * 14, ty - 28); ctx.lineTo(x + side * 8, ty - 24); ctx.fill();
      ctx.fillStyle = '#ef4444'; ctx.beginPath(); ctx.arc(x + side * 10, ty + 2, 4, 0, 7); ctx.fill();
      ctx.fillStyle = '#1f2937';
      if (reaction === 'happy') {
        ctx.strokeStyle = '#1f2937'; ctx.lineWidth = 1.5;
        ctx.beginPath(); ctx.moveTo(x + side * 3, ty - 2);
        ctx.lineTo(x + side * 5, ty - 5); ctx.lineTo(x + side * 7, ty - 2); ctx.stroke();
      } else {
        ctx.beginPath(); ctx.arc(x + side * 5, ty - 3, reaction === 'worried' ? 3 : 2, 0, 7); ctx.fill();
      }
    }
    ctx.strokeStyle = '#1f2937'; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.arc(x, ty + 3, 3, 0, Math.PI); ctx.stroke();
    ctx.fillStyle = '#facc15'; ctx.strokeStyle = '#92400e';
    ctx.beginPath(); ctx.moveTo(x + 12, ty); ctx.lineTo(x + 20, ty - 8);
    ctx.lineTo(x + 16, ty - 4); ctx.lineTo(x + 24, ty - 14);
    ctx.lineTo(x + 18, ty - 4); ctx.lineTo(x + 22, ty - 6);
    ctx.lineTo(x + 14, ty + 4); ctx.closePath(); ctx.fill(); ctx.stroke();
  }
  if (reaction === 'grind' || reaction === 'happy') {
    ctx.fillStyle = type === 'gardevoir' ? '#e9d5ff' : '#fbbf24';
    for (let i = 0; i < 4; i++) {
      ctx.beginPath(); ctx.arc(x + Math.sin(time * 0.008 + i * 1.5) * 22,
        ty - 8 + Math.cos(time * 0.01 + i * 1.5) * 14, 1.5, 0, 7); ctx.fill();
    }
  }
  ctx.restore();
}
