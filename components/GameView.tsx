
import { useEffect, useRef, useState } from 'react';
import { Outfit, ItemType, CompanionType } from '../types';

interface GameViewProps {
  onEnd: (score: number) => void;
  outfit: Outfit;
}

// Physics — tuned for tight, responsive platformer feel
const GRAVITY_RISE = 0.32;         // Rising gravity (slightly lighter than base for a clean arc)
const GRAVITY_FALL = 0.52;         // Falling gravity (heavier — snappy descent, clear weight)
const GRAVITY_APEX = 0.14;         // Apex gravity (brief float at top of jump)
const APEX_VEL_ZONE = 2.8;         // Velocity range where apex float blends in
const JUMP_VELOCITY = -14.0;       // Initial jump impulse
const JUMP_CUT = 0.48;            // One-shot velocity multiplier on early release
const MAX_FALL_SPEED = 16;         // Terminal velocity cap
const COYOTE_MS = 80;             // Grace period after leaving ground/rail (ms)
const JUMP_BUFFER_MS = 100;        // Pre-land jump buffer window (ms)
const SQUASH_LERP = 0.2;          // Squash/stretch recovery speed (higher = snappier)
const BASE_SPAWN_RATE = 900;
const RAIL_SPAWN_CHANCE = 0.45;

// Milestone messages
const MILESTONE_MESSAGES: { score: number; message: string }[] = [
  { score: 5, message: 'You make every day an adventure!' },
  { score: 10, message: 'Remember our first date?' },
  { score: 25, message: 'My favorite person in the world' },
  { score: 50, message: 'Together is my favorite place' },
  { score: 75, message: 'You light up every room' },
  { score: 100, message: 'Forever and always, my love' },
];

interface GameItem {
  x: number;
  y: number;
  type: ItemType;
  speed: number;
  size: number;
  length?: number;
  slope?: number;
  phase?: number;        // Random offset for bobbing animation
  baseY?: number;        // Original Y for bobbing reference
}

interface Particle {
  x: number;
  y: number;
  vx: number;
  vy: number;
  life: number;
  type: 'heart' | 'sparkle' | 'combo';
  text?: string;
  scale?: number;
}

const GameView: React.FC<GameViewProps> = ({ onEnd, outfit }) => {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [, setScore] = useState(0);
  const scoreRef = useRef(0);
  const [dimensions, setDimensions] = useState({ width: window.innerWidth, height: window.innerHeight });

  // Physics & State Refs
  const playerY = useRef(window.innerHeight - 200);
  const playerVelocity = useRef(0);
  const isGrinding = useRef(false);
  const grindSlope = useRef(0);
  const items = useRef<GameItem[]>([]);
  const particles = useRef<Particle[]>([]);
  const frameId = useRef<number>(0);
  const lastSpawn = useRef(0);

  // Combo system
  const comboCount = useRef(0);
  const comboTimer = useRef(0);
  const comboDisplay = useRef({ text: '', alpha: 0, scale: 1 });

  // Power-up active effects
  const doubleScoreTimer = useRef(0);
  const magnetTimer = useRef(0);
  const shieldActive = useRef(false);

  // Milestone messages
  const milestoneDisplay = useRef({ text: '', alpha: 0 });
  const lastMilestone = useRef(0);

  // Parallax offsets
  const bgOffset = useRef(0);

  // Companion state
  const companionBob = useRef(0);
  const companionReaction = useRef<'idle' | 'happy' | 'grind' | 'worried'>('idle');
  const companionReactionTimer = useRef(0);

  // Track if ended to prevent double calls
  const hasEnded = useRef(false);

  // Smooth physics refs
  const lastFrameTime = useRef(0);
  const jumpHeld = useRef(false);
  const coyoteTimer = useRef(0);        // ms remaining for coyote time
  const jumpBufferTimer = useRef(0);     // ms remaining in jump buffer
  const wasGrounded = useRef(true);      // Was on ground/rail last frame
  const squash = useRef({ scaleX: 1, scaleY: 1 });
  const screenShake = useRef({ x: 0, y: 0, intensity: 0 });

  useEffect(() => {
    const handleResize = () => setDimensions({ width: window.innerWidth, height: window.innerHeight });
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    hasEnded.current = false;

    const gameLoop = (time: number) => {
      const { width, height } = dimensions;
      const playerX = width * 0.15;
      const playerHeight = 100;
      const groundY = height - 100;

      // --- DIFFICULTY SCALING ---
      const currentScore = scoreRef.current;
      const getSpeed = () => 6 + Math.min(currentScore * 0.08, 5);
      const getSpawnRate = () => Math.max(500, BASE_SPAWN_RATE - currentScore * 4);
      const speed = getSpeed();

      // --- PARALLAX UPDATE ---
      bgOffset.current += speed * 0.5;

      // --- COMBO TIMER ---
      if (comboCount.current > 0 && !isGrinding.current) {
        comboTimer.current -= 16; // ~1 frame at 60fps
        if (comboTimer.current <= 0) {
          comboCount.current = 0;
        }
      }

      // --- POWER-UP TIMERS ---
      if (doubleScoreTimer.current > 0) doubleScoreTimer.current -= 16;
      if (magnetTimer.current > 0) magnetTimer.current -= 16;

      // --- MILESTONE CHECK ---
      const floorScore = Math.floor(currentScore);
      for (const m of MILESTONE_MESSAGES) {
        if (floorScore >= m.score && lastMilestone.current < m.score) {
          milestoneDisplay.current = { text: m.message, alpha: 1 };
          lastMilestone.current = m.score;
        }
      }
      if (milestoneDisplay.current.alpha > 0) {
        milestoneDisplay.current.alpha -= 0.005;
      }

      // --- COMBO DISPLAY FADE ---
      if (comboDisplay.current.alpha > 0) {
        comboDisplay.current.alpha -= 0.015;
        comboDisplay.current.scale *= 0.995;
      }

      // --- COMPANION ---
      companionBob.current = Math.sin(time * 0.004) * 6;
      if (companionReactionTimer.current > 0) {
        companionReactionTimer.current -= 16;
        if (companionReactionTimer.current <= 0) companionReaction.current = 'idle';
      }
      if (isGrinding.current) companionReaction.current = 'grind';

      // --- DELTA TIME ---
      const dt = lastFrameTime.current === 0 ? 1 : Math.min((time - lastFrameTime.current) / 16.667, 3);
      lastFrameTime.current = time;

      // --- COYOTE & JUMP BUFFER TIMERS ---
      const isOnGround = playerY.current >= groundY - playerHeight - 1;
      const grounded = isOnGround || isGrinding.current;

      if (grounded) {
        coyoteTimer.current = COYOTE_MS;
      } else {
        coyoteTimer.current = Math.max(0, coyoteTimer.current - 16 * dt);
      }
      if (jumpBufferTimer.current > 0) {
        jumpBufferTimer.current = Math.max(0, jumpBufferTimer.current - 16 * dt);
      }

      // Execute buffered jump if we just landed and buffer is active
      if (grounded && jumpBufferTimer.current > 0 && !wasGrounded.current) {
        jumpBufferTimer.current = 0;
        coyoteTimer.current = 0;
        isGrinding.current = false;
        playerVelocity.current = JUMP_VELOCITY;
        squash.current = { scaleX: 0.82, scaleY: 1.25 };
        for (let i = 0; i < 5; i++) particles.current.push({
          x: playerX + 20, y: playerY.current + 95,
          vx: (Math.random() - 0.5) * 7, vy: 2 + Math.random() * 2,
          life: 0.8, type: 'sparkle'
        });
      }

      // Landing detection — trigger squash on landing
      if (grounded && !wasGrounded.current) {
        squash.current = { scaleX: 1.2, scaleY: 0.75 };
      }
      wasGrounded.current = grounded;

      // --- ASYMMETRIC GRAVITY (smoothly interpolated through apex) ---
      if (!isGrinding.current) {
        const vel = playerVelocity.current;
        const absVel = Math.abs(vel);
        let gravity: number;

        if (absVel < APEX_VEL_ZONE) {
          // Inside apex zone — smoothly blend between rise/fall and apex gravity
          // t goes from 0 (at exact apex) to 1 (at zone edge)
          const t = absVel / APEX_VEL_ZONE;
          // Ease-in (t²) so the float is most noticeable right at the peak
          const edgeGravity = vel <= 0 ? GRAVITY_RISE : GRAVITY_FALL;
          gravity = GRAVITY_APEX + (edgeGravity - GRAVITY_APEX) * (t * t);
        } else if (vel < 0) {
          gravity = GRAVITY_RISE;
        } else {
          gravity = GRAVITY_FALL;
        }

        playerVelocity.current += gravity * dt;

        // Terminal velocity
        if (playerVelocity.current > MAX_FALL_SPEED) {
          playerVelocity.current = MAX_FALL_SPEED;
        }

        playerY.current += playerVelocity.current * dt;
      }

      // Ground Boundary
      if (playerY.current > groundY - playerHeight) {
        playerY.current = groundY - playerHeight;
        playerVelocity.current = 0;
      }

      // --- SQUASH/STRETCH DECAY ---
      squash.current.scaleX += (1 - squash.current.scaleX) * SQUASH_LERP * dt;
      squash.current.scaleY += (1 - squash.current.scaleY) * SQUASH_LERP * dt;

      // --- SCREEN SHAKE DECAY ---
      if (screenShake.current.intensity > 0) {
        screenShake.current.x = (Math.random() - 0.5) * screenShake.current.intensity;
        screenShake.current.y = (Math.random() - 0.5) * screenShake.current.intensity;
        screenShake.current.intensity *= Math.pow(0.88, dt);
        if (screenShake.current.intensity < 0.3) {
          screenShake.current = { x: 0, y: 0, intensity: 0 };
        }
      }

      // --- SPAWNING ---
      if (time - lastSpawn.current > getSpawnRate()) {
        const rand = Math.random();
        if (rand < RAIL_SPAWN_CHANCE) {
          const tier = Math.random();
          const railY = tier < 0.4 ? groundY - 60 - Math.random() * 40
                      : tier < 0.75 ? groundY - 120 - Math.random() * 50
                      : groundY - 190 - Math.random() * 30;
          const isSlanted = Math.random() < 0.5;
          let slope = isSlanted
            ? (Math.random() < 0.7 ? 100 + Math.random() * 80 : -(80 + Math.random() * 60))
            : 0;
          const maxY = groundY - 15;
          if (railY + slope > maxY) slope = maxY - railY;
          items.current.push({
            x: width + 100, y: railY, type: 'rail', speed, size: 10,
            length: 250 + Math.random() * 250, slope
          });
        } else {
          // Determine item type
          const typeRand = Math.random();
          let itemType: ItemType;
          if (rand > 0.82) {
            itemType = 'obstacle';
          } else if (typeRand < 0.005) {
            itemType = 'masterball';
          } else if (typeRand < 0.025) {
            itemType = 'ultraball';
          } else if (typeRand < 0.075) {
            itemType = 'greatball';
          } else {
            itemType = 'ball';
          }

          const ballY = groundY - 30 - Math.random() * 200;
          const obstacleH = 40 + Math.random() * 25;
          const yPos = itemType === 'obstacle' ? groundY - obstacleH : ballY;
          items.current.push({
            x: width + 100,
            y: yPos,
            type: itemType,
            speed,
            size: itemType === 'obstacle' ? obstacleH : 40,
            phase: Math.random() * Math.PI * 2,
            baseY: yPos
          });
        }
        lastSpawn.current = time;
      }

      // --- PARTICLES UPDATE (with gravity on sparkles) ---
      particles.current = particles.current.filter(p => {
        p.x += p.vx * dt;
        p.y += p.vy * dt;
        if (p.type === 'sparkle') p.vy += 0.08 * dt; // Subtle gravity on sparkles
        p.vx *= Math.pow(0.98, dt); // Air resistance
        p.life -= 0.02 * dt;
        return p.life > 0;
      });

      // Trail particles during grind
      if (isGrinding.current && Math.random() > 0.5) {
        particles.current.push({
          x: playerX + 10, y: playerY.current + 95,
          vx: -1 - Math.random() * 2, vy: -Math.random() * 1.5,
          life: 0.8, type: Math.random() > 0.7 ? 'heart' : 'sparkle'
        });
      }

      // Helper: add score with combo multiplier
      const addScore = (base: number) => {
        const combo = Math.max(1, comboCount.current);
        const doubleActive = doubleScoreTimer.current > 0 ? 2 : 1;
        scoreRef.current += base * combo * doubleActive;
        setScore(Math.floor(scoreRef.current));
      };

      // Helper: trigger combo
      const triggerCombo = () => {
        comboCount.current++;
        comboTimer.current = 1500;
        if (comboCount.current >= 2) {
          comboDisplay.current = {
            text: `x${comboCount.current} COMBO!`,
            alpha: 1,
            scale: 1.5
          };
        }
        companionReaction.current = 'happy';
        companionReactionTimer.current = 500;
      };

      // --- MAGNET EFFECT (eased pull — stronger when closer) ---
      if (magnetTimer.current > 0) {
        items.current.forEach(item => {
          if (item.type === 'ball' || item.type === 'greatball' || item.type === 'ultraball' || item.type === 'masterball') {
            const dx = (playerX + 20) - (item.x + 20);
            const dy = (playerY.current + 50) - (item.y + 20);
            const dist = Math.sqrt(dx * dx + dy * dy);
            if (dist < 220 && dist > 8) {
              // Quadratic easing: pull strength increases as item gets closer
              const t = 1 - dist / 220;
              const pullStrength = (2 + t * t * 8) * dt;
              item.x += dx / dist * pullStrength;
              item.y += dy / dist * pullStrength;
            }
          }
        });
      }

      // --- PROCESS ITEMS & COLLISION ---
      let onRail = false;
      items.current = items.current.filter(item => {
        item.x -= item.speed;

        if (item.type === 'rail' && item.length) {
          const feetY = playerY.current + playerHeight;
          const slopeVal = item.slope || 0;
          const progress = Math.max(0, Math.min(1, (playerX + 20 - item.x) / item.length));
          const railYAtPlayer = item.y + progress * slopeVal;
          if (!isGrinding.current && playerVelocity.current >= 0 && feetY >= railYAtPlayer - 10 && feetY <= railYAtPlayer + 35 &&
            playerX + 25 > item.x && playerX + 15 < item.x + item.length) {
            isGrinding.current = true;
            grindSlope.current = slopeVal;
            playerY.current = railYAtPlayer - playerHeight;
            playerVelocity.current = 0;
            triggerCombo();
          }
          if (isGrinding.current && playerX + 20 >= item.x && playerX + 20 <= item.x + item.length) {
            onRail = true;
            playerY.current = railYAtPlayer - playerHeight;
            const combo = Math.max(1, comboCount.current);
            const doubleActive = doubleScoreTimer.current > 0 ? 2 : 1;
            scoreRef.current += 0.12 * combo * doubleActive;
            setScore(Math.floor(scoreRef.current));
          }
        } else if (item.type === 'ball' || item.type === 'greatball' || item.type === 'ultraball' || item.type === 'masterball') {
          // Apply bobbing to ball Y position
          if (item.phase !== undefined && item.baseY !== undefined) {
            item.y = item.baseY + Math.sin(time * 0.004 + item.phase) * 6;
          }

          const dx = (playerX + 20) - (item.x + 20);
          const dy = (playerY.current + 50) - (item.y + 20);
          const dist = Math.sqrt(dx * dx + dy * dy);

          // Slightly generous hitbox (44 instead of 40) — feels more responsive
          if (dist < 44) {
            triggerCombo();
            // Score based on type
            if (item.type === 'ball') {
              addScore(1);
              screenShake.current.intensity = Math.max(screenShake.current.intensity, 2);
            } else if (item.type === 'greatball') {
              addScore(2);
              doubleScoreTimer.current = 5000;
              screenShake.current.intensity = Math.max(screenShake.current.intensity, 3.5);
            } else if (item.type === 'ultraball') {
              addScore(3);
              magnetTimer.current = 5000;
              screenShake.current.intensity = Math.max(screenShake.current.intensity, 4);
            } else if (item.type === 'masterball') {
              addScore(5);
              shieldActive.current = true;
              screenShake.current.intensity = Math.max(screenShake.current.intensity, 6);
            }

            // Radial particle burst — more particles, directional spread
            const particleType = item.type === 'masterball' ? 'heart' : 'sparkle';
            const burstCount = item.type === 'masterball' ? 14 : item.type === 'ball' ? 8 : 11;
            for (let k = 0; k < burstCount; k++) {
              const angle = (k / burstCount) * Math.PI * 2 + Math.random() * 0.4;
              const spd = 3 + Math.random() * 5;
              particles.current.push({
                x: item.x + 20, y: item.y + 20,
                vx: Math.cos(angle) * spd,
                vy: Math.sin(angle) * spd - 2,
                life: 0.7 + Math.random() * 0.4,
                type: particleType as 'sparkle' | 'heart'
              });
            }

            // Brief squash on collect for feedback
            squash.current = { scaleX: 1.08, scaleY: 0.92 };
            return false;
          }
        } else if (item.type === 'obstacle' && !isGrinding.current) {
          const feetY = playerY.current + playerHeight;
          const overlapX = playerX + 35 > item.x && playerX + 5 < item.x + 30;
          const overlapY = feetY > item.y + 5;
          if (overlapX && overlapY) {
            if (shieldActive.current) {
              shieldActive.current = false;
              screenShake.current.intensity = 8;
              // Shield absorb — big radial burst
              for (let k = 0; k < 16; k++) {
                const angle = (k / 16) * Math.PI * 2;
                const spd = 4 + Math.random() * 6;
                particles.current.push({
                  x: playerX + 20, y: playerY.current + 50,
                  vx: Math.cos(angle) * spd,
                  vy: Math.sin(angle) * spd,
                  life: 1, type: 'heart'
                });
              }
              companionReaction.current = 'happy';
              companionReactionTimer.current = 800;
              return false;
            }
            // Death hit — strong shake
            screenShake.current.intensity = 12;
            if (!hasEnded.current) {
              hasEnded.current = true;
              onEnd(Math.floor(scoreRef.current));
            }
            return false;
          }
        }
        return item.x > -item.size - (item.length || 0);
      });

      if (isGrinding.current && !onRail) {
        isGrinding.current = false;
        grindSlope.current = 0;
      }

      // Check for nearby obstacles -> companion worried
      if (!isGrinding.current) {
        const nearObstacle = items.current.some(item =>
          item.type === 'obstacle' && item.x - playerX < 120 && item.x - playerX > 0
        );
        if (nearObstacle && companionReaction.current === 'idle') {
          companionReaction.current = 'worried';
        }
      }

      // ========================
      //  RENDER
      // ========================
      ctx.clearRect(0, 0, width, height);

      // --- SCREEN SHAKE OFFSET ---
      ctx.save();
      if (screenShake.current.intensity > 0) {
        ctx.translate(screenShake.current.x, screenShake.current.y);
      }

      // --- PARALLAX BACKGROUND ---
      // Sky gradient
      const sky = ctx.createLinearGradient(0, 0, 0, height);
      sky.addColorStop(0, '#fff5f7');
      sky.addColorStop(1, '#fbcfe8');
      ctx.fillStyle = sky;
      ctx.fillRect(0, 0, width, height);

      // Far layer - city skyline silhouettes (0.5x speed)
      const farOffset = bgOffset.current * 0.3;
      ctx.fillStyle = 'rgba(244, 183, 210, 0.3)';
      for (let i = -1; i < Math.ceil(width / 200) + 2; i++) {
        const bx = (i * 200) - (farOffset % 200);
        const bh = 60 + Math.sin(i * 2.3) * 30;
        ctx.fillRect(bx, groundY - bh - 60, 80, bh + 60);
        // Pointy top
        ctx.beginPath();
        ctx.moveTo(bx + 20, groundY - bh - 60 - 25);
        ctx.lineTo(bx + 40, groundY - bh - 60);
        ctx.lineTo(bx, groundY - bh - 60);
        ctx.fill();
        // Second building
        const bh2 = 40 + Math.cos(i * 1.7) * 20;
        ctx.fillRect(bx + 100, groundY - bh2 - 40, 60, bh2 + 40);
      }

      // Mid layer - trees (1.5x speed)
      const midOffset = bgOffset.current * 0.8;
      ctx.fillStyle = 'rgba(236, 155, 191, 0.35)';
      for (let i = -1; i < Math.ceil(width / 150) + 2; i++) {
        const tx = (i * 150) - (midOffset % 150);
        // Tree trunk
        ctx.fillStyle = 'rgba(180, 120, 140, 0.3)';
        ctx.fillRect(tx + 18, groundY - 50, 8, 50);
        // Tree crown
        ctx.fillStyle = 'rgba(236, 155, 191, 0.35)';
        ctx.beginPath();
        ctx.arc(tx + 22, groundY - 65, 28, 0, Math.PI * 2);
        ctx.fill();
      }

      // Near layer - flowers / lane markings (3x speed)
      const nearOffset = bgOffset.current * 2;
      ctx.fillStyle = 'rgba(251, 207, 232, 0.5)';
      for (let i = -1; i < Math.ceil(width / 80) + 2; i++) {
        const fx = (i * 80) - (nearOffset % 80);
        // Small flower
        ctx.beginPath();
        ctx.arc(fx + 10, groundY - 5, 4, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = 'rgba(244, 63, 94, 0.3)';
        ctx.beginPath();
        ctx.arc(fx + 10, groundY - 5, 2, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = 'rgba(251, 207, 232, 0.5)';
        // Lane dash
        ctx.fillRect(fx + 40, groundY + 20, 25, 3);
      }

      // Floor
      ctx.fillStyle = '#f9a8d4';
      ctx.fillRect(0, groundY, width, height - groundY);

      // --- PARTICLES ---
      particles.current.forEach(p => {
        ctx.globalAlpha = p.life;
        if (p.type === 'heart') {
          ctx.fillStyle = '#f43f5e';
          ctx.font = '18px Arial';
          ctx.fillText('\u2764', p.x, p.y);
        } else {
          ctx.fillStyle = '#fff';
          ctx.beginPath(); ctx.arc(p.x, p.y, 2.5, 0, 7); ctx.fill();
        }
        ctx.globalAlpha = 1;
      });

      // --- ITEMS ---
      items.current.forEach(item => {
        if (item.type === 'rail') {
          const slopeVal = item.slope || 0;
          const endX = item.x + (item.length || 0);
          const endY = item.y + slopeVal;
          ctx.shadowBlur = slopeVal !== 0 ? 18 : 12;
          ctx.shadowColor = slopeVal !== 0 ? '#a855f7' : '#ec4899';
          ctx.strokeStyle = slopeVal !== 0 ? '#c084fc' : '#f472b6';
          ctx.lineWidth = 12; ctx.lineCap = 'round';
          ctx.beginPath(); ctx.moveTo(item.x, item.y); ctx.lineTo(endX, endY); ctx.stroke();
          ctx.shadowBlur = 0;
          ctx.strokeStyle = '#fff'; ctx.lineWidth = 3;
          ctx.beginPath(); ctx.moveTo(item.x, item.y - 4); ctx.lineTo(endX, endY - 4); ctx.stroke();
        } else if (item.type === 'ball' || item.type === 'greatball' || item.type === 'ultraball' || item.type === 'masterball') {
          const bx = item.x + 20;
          const by = item.y + 20;
          // Subtle scale pulse
          const pulse = 1 + Math.sin(time * 0.006 + (item.phase || 0)) * 0.06;
          // Proximity glow — glows brighter as player approaches
          const pdx = (playerX + 20) - bx;
          const pdy = (playerY.current + 50) - by;
          const pDist = Math.sqrt(pdx * pdx + pdy * pdy);
          if (pDist < 100) {
            const glowAlpha = (1 - pDist / 100) * 0.35;
            const glowColor = item.type === 'masterball' ? '#c084fc'
                            : item.type === 'ultraball' ? '#facc15'
                            : item.type === 'greatball' ? '#3b82f6'
                            : '#f43f5e';
            ctx.save();
            ctx.globalAlpha = glowAlpha;
            ctx.fillStyle = glowColor;
            ctx.beginPath();
            ctx.arc(bx, by, 28 + (1 - pDist / 100) * 10, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
          }
          ctx.save();
          ctx.translate(bx, by);
          ctx.scale(pulse, pulse);
          ctx.translate(-bx, -by);
          if (item.type === 'ball') drawPokeball(ctx, bx, by, 18);
          else if (item.type === 'greatball') drawGreatBall(ctx, bx, by, 18);
          else if (item.type === 'ultraball') drawUltraBall(ctx, bx, by, 18);
          else drawMasterBall(ctx, bx, by, 18);
          ctx.restore();
        } else if (item.type === 'obstacle') {
          const cx = item.x + 15;
          const bottom = item.y + item.size;
          ctx.fillStyle = '#f97316';
          ctx.beginPath();
          ctx.moveTo(cx, item.y);
          ctx.lineTo(cx + 14, bottom);
          ctx.lineTo(cx - 14, bottom);
          ctx.closePath();
          ctx.fill();
          ctx.strokeStyle = '#fff'; ctx.lineWidth = 3;
          const h = item.size;
          ctx.beginPath(); ctx.moveTo(cx - 5, item.y + h * 0.35); ctx.lineTo(cx + 5, item.y + h * 0.35); ctx.stroke();
          ctx.beginPath(); ctx.moveTo(cx - 9, item.y + h * 0.65); ctx.lineTo(cx + 9, item.y + h * 0.65); ctx.stroke();
          ctx.fillStyle = '#ea580c';
          ctx.fillRect(cx - 16, bottom - 5, 32, 5);
        }
      });

      // --- CHARACTER ---
      const px = playerX;
      const py = playerY.current;
      const legCycle = Math.sin(time * 0.012);
      const bob = Math.sin(time * 0.01) * 4;
      const skin = '#e7c8b4';

      // Shield glow
      if (shieldActive.current) {
        ctx.save();
        ctx.globalAlpha = 0.25 + Math.sin(time * 0.008) * 0.1;
        ctx.fillStyle = '#c084fc';
        ctx.beginPath();
        ctx.arc(px + 20, py + 50, 55, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
      }

      ctx.save();
      ctx.translate(px + 20, py + 50);
      // Squash/stretch — applied at character center pivot
      ctx.scale(squash.current.scaleX, squash.current.scaleY);
      if (isGrinding.current) {
        ctx.rotate(Math.atan2(grindSlope.current, 375) * 0.6);
      }
      ctx.translate(-20, -50);

      // Hair (use outfit colors)
      ctx.fillStyle = outfit.hairColor;
      const hOffset = Math.sin(time * 0.008) * 8;
      ctx.beginPath();
      ctx.moveTo(15, 10 + bob);
      for (let i = 0; i < 5; i++) {
        const hx = -15 - i * 15;
        const hy = 20 + i * 18 + hOffset;
        ctx.quadraticCurveTo(hx - 10, hy - 5, hx, hy);
      }
      ctx.lineTo(20, 50);
      ctx.fill();

      ctx.fillStyle = outfit.hairHighlight;
      ctx.beginPath();
      ctx.moveTo(10, 15 + bob);
      for (let i = 0; i < 4; i++) {
        const hx = -10 - i * 12;
        const hy = 30 + i * 20 - hOffset;
        ctx.quadraticCurveTo(hx - 12, hy - 8, hx, hy);
      }
      ctx.lineTo(15, 60);
      ctx.fill();

      // Torso (outfit bodyColor)
      ctx.fillStyle = outfit.bodyColor;
      ctx.beginPath();
      ctx.roundRect(10, 32 + bob, 24, 24, 4);
      ctx.fill();

      // White Fur Trim
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.roundRect(8, 50 + bob, 28, 8, 4);
      ctx.fill();

      // Arms
      ctx.strokeStyle = skin; ctx.lineWidth = 6;
      ctx.beginPath();
      ctx.moveTo(12, 38 + bob);
      ctx.lineTo(isGrinding.current ? 40 : 5, 55 + bob);
      ctx.stroke();

      // Skirt
      ctx.fillStyle = outfit.bodyColor;
      ctx.beginPath();
      ctx.roundRect(8, 56 + bob, 28, 12, 4);
      ctx.fill();
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.roundRect(6, 64 + bob, 32, 6, 3);
      ctx.fill();

      // Legs
      const drawLeg = (isFront: boolean) => {
        let lx: number, ly: number;
        if (isGrinding.current) {
          lx = isFront ? 42 : 10;
          ly = 90;
        } else {
          lx = (isFront ? 28 : 12) + (isFront ? legCycle * 12 : -legCycle * 12);
          ly = 92 + Math.abs(legCycle) * 8;
        }

        ctx.strokeStyle = skin; ctx.lineWidth = 7;
        ctx.beginPath(); ctx.moveTo(22, 60 + bob); ctx.lineTo(lx, ly - 25); ctx.stroke();

        ctx.save(); ctx.strokeStyle = 'rgba(0,0,0,0.2)'; ctx.lineWidth = 1; ctx.setLineDash([2, 1]);
        ctx.beginPath(); ctx.moveTo(22, 62 + bob); ctx.lineTo(lx, ly - 25); ctx.stroke(); ctx.restore();

        // Leg warmers (outfit trimColor)
        ctx.fillStyle = outfit.trimColor;
        ctx.beginPath(); ctx.roundRect(lx - 6, ly - 25, 12, 18, 3); ctx.fill();

        // Skates (outfit skateColor)
        ctx.fillStyle = outfit.skateColor;
        ctx.beginPath(); ctx.roundRect(lx - 10, ly - 10, 20, 12, 4); ctx.fill();
        // Wheels
        ctx.fillStyle = '#333';
        ctx.beginPath(); ctx.arc(lx - 6, ly + 4, 4, 0, 7); ctx.fill();
        ctx.beginPath(); ctx.arc(lx + 6, ly + 4, 4, 0, 7); ctx.fill();
      };

      drawLeg(false); drawLeg(true);

      // Face
      ctx.fillStyle = skin;
      ctx.beginPath(); ctx.arc(22, 18 + bob, 18, 0, 7); ctx.fill();
      ctx.fillStyle = 'rgba(0,0,0,0.05)';
      ctx.beginPath(); ctx.arc(22, 18 + bob, 18, 0.5, Math.PI - 0.5); ctx.fill();

      // Bangs
      ctx.fillStyle = outfit.hairColor;
      ctx.beginPath();
      ctx.arc(22, 16 + bob, 19, Math.PI, 0);
      ctx.fill();
      ctx.beginPath();
      ctx.moveTo(4, 16 + bob);
      ctx.quadraticCurveTo(10, 28 + bob, 16, 18 + bob);
      ctx.fill();

      // Eyes
      ctx.fillStyle = '#332';
      ctx.beginPath(); ctx.arc(15, 20 + bob, 2.5, 0, 7); ctx.fill();
      ctx.beginPath(); ctx.arc(29, 20 + bob, 2.5, 0, 7); ctx.fill();

      // Lips
      ctx.fillStyle = '#f43f5e';
      ctx.beginPath(); ctx.arc(22, 28 + bob, 2.5, 0, 7); ctx.fill();

      ctx.restore();

      // --- COMPANION ---
      drawCompanion(ctx, px + 55, py - 30 + companionBob.current, time, companionReaction.current, outfit.companion);

      // --- HUD ---
      // Score
      ctx.fillStyle = outfit.bodyColor;
      ctx.font = 'bold 34px "Quicksand"';
      ctx.fillText(`Love: ${Math.floor(scoreRef.current)}`, 30, 55);

      // Grinding indicator
      if (isGrinding.current) {
        ctx.fillStyle = '#fff';
        ctx.font = 'bold 22px "Quicksand"';
        ctx.shadowBlur = 4; ctx.shadowColor = outfit.bodyColor;
        ctx.fillText('STAYING STEADY!', playerX + 60, py - 20);
        ctx.shadowBlur = 0;
      }

      // Combo display
      if (comboDisplay.current.alpha > 0 && comboCount.current >= 2) {
        ctx.save();
        ctx.globalAlpha = comboDisplay.current.alpha;
        ctx.font = `bold ${Math.floor(28 * comboDisplay.current.scale)}px "Quicksand"`;
        ctx.fillStyle = '#facc15';
        ctx.strokeStyle = '#92400e';
        ctx.lineWidth = 3;
        const comboText = comboDisplay.current.text;
        const comboX = playerX + 60;
        const comboY = py - 50;
        ctx.strokeText(comboText, comboX, comboY);
        ctx.fillText(comboText, comboX, comboY);
        ctx.restore();
      }

      // Power-up indicators
      const indicators: string[] = [];
      if (doubleScoreTimer.current > 0) indicators.push(`2x SCORE ${Math.ceil(doubleScoreTimer.current / 1000)}s`);
      if (magnetTimer.current > 0) indicators.push(`MAGNET ${Math.ceil(magnetTimer.current / 1000)}s`);
      if (shieldActive.current) indicators.push('SHIELD');
      if (indicators.length > 0) {
        ctx.font = 'bold 16px "Quicksand"';
        indicators.forEach((text, i) => {
          const iy = 80 + i * 24;
          ctx.fillStyle = 'rgba(168, 85, 247, 0.8)';
          const tw = ctx.measureText(text).width;
          ctx.fillRect(26, iy - 14, tw + 16, 22);
          ctx.fillStyle = '#fff';
          ctx.fillText(text, 34, iy);
        });
      }

      // Milestone message
      if (milestoneDisplay.current.alpha > 0) {
        ctx.save();
        ctx.globalAlpha = Math.min(1, milestoneDisplay.current.alpha);
        ctx.font = 'bold 28px "Quicksand"';
        ctx.fillStyle = '#fff';
        ctx.strokeStyle = '#db2777';
        ctx.lineWidth = 4;
        ctx.textAlign = 'center';
        const mText = milestoneDisplay.current.text;
        ctx.strokeText(mText, width / 2, height / 2 - 80);
        ctx.fillText(mText, width / 2, height / 2 - 80);
        ctx.textAlign = 'left';
        ctx.restore();
      }

      // --- END SCREEN SHAKE ---
      ctx.restore();

      frameId.current = requestAnimationFrame(gameLoop);
    };

    frameId.current = requestAnimationFrame(gameLoop);
    return () => {
      cancelAnimationFrame(frameId.current);
      lastFrameTime.current = 0; // Reset for next game session
    };
  }, [dimensions, onEnd, outfit]);

  const executeJump = () => {
    coyoteTimer.current = 0;
    jumpBufferTimer.current = 0;
    isGrinding.current = false;
    playerVelocity.current = JUMP_VELOCITY;
    squash.current = { scaleX: 0.82, scaleY: 1.25 };
    for (let i = 0; i < 6; i++) particles.current.push({
      x: dimensions.width * 0.15 + 10 + Math.random() * 20,
      y: playerY.current + 92,
      vx: (Math.random() - 0.5) * 7,
      vy: 1.5 + Math.random() * 3,
      life: 0.6 + Math.random() * 0.3,
      type: 'sparkle'
    });
  };

  // Native DOM listeners — bypass React synthetic event batching for zero-lag input
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const onPress = (e: Event) => {
      e.preventDefault();
      jumpHeld.current = true;
      const groundY = dimensions.height - 100;
      const onGround = playerY.current >= groundY - 101;
      const canJump = onGround || isGrinding.current || coyoteTimer.current > 0;
      if (canJump) {
        executeJump();
      } else {
        jumpBufferTimer.current = JUMP_BUFFER_MS;
      }
    };

    const onRelease = () => {
      jumpHeld.current = false;
      if (playerVelocity.current < -0.5) {
        playerVelocity.current *= JUMP_CUT;
      }
    };

    // { passive: false } lets us preventDefault to kill any touch delay
    canvas.addEventListener('mousedown', onPress, { passive: false });
    canvas.addEventListener('touchstart', onPress, { passive: false });
    canvas.addEventListener('mouseup', onRelease);
    canvas.addEventListener('touchend', onRelease);

    return () => {
      canvas.removeEventListener('mousedown', onPress);
      canvas.removeEventListener('touchstart', onPress);
      canvas.removeEventListener('mouseup', onRelease);
      canvas.removeEventListener('touchend', onRelease);
    };
  }, [dimensions]);

  return (
    <div className="fixed inset-0 touch-none overflow-hidden">
      <canvas ref={canvasRef} width={dimensions.width} height={dimensions.height} className="block cursor-none" />
      <div className="absolute top-8 right-8 flex flex-col items-end gap-3 pointer-events-none">
        <div className="bg-white/80 backdrop-blur-md px-6 py-3 rounded-2xl border-2 border-pink-200 shadow-xl animate-bounce">
          <p className="text-pink-600 font-black text-lg">TAP TO JUMP!</p>
        </div>
        <div className="bg-pink-600/90 text-white px-5 py-2 rounded-full text-sm font-bold shadow-lg">
           Mizu Trick on Rails for Bonus!
        </div>
      </div>
    </div>
  );
};

// --- BALL DRAWING HELPERS ---

function drawPokeball(ctx: CanvasRenderingContext2D, bx: number, by: number, r: number) {
  ctx.beginPath(); ctx.arc(bx, by, r, 0, Math.PI * 2);
  ctx.fillStyle = '#ef4444'; ctx.fill();
  ctx.fillStyle = '#fff'; ctx.beginPath(); ctx.arc(bx, by, r, 0, Math.PI, false); ctx.fill();
  ctx.strokeStyle = '#333'; ctx.lineWidth = 2.5; ctx.stroke();
  ctx.beginPath(); ctx.moveTo(bx - r, by); ctx.lineTo(bx + r, by); ctx.stroke();
  ctx.beginPath(); ctx.arc(bx, by, 5, 0, 7); ctx.fillStyle = '#fff'; ctx.fill(); ctx.stroke();
}

function drawGreatBall(ctx: CanvasRenderingContext2D, bx: number, by: number, r: number) {
  // Blue top, red stripe
  ctx.beginPath(); ctx.arc(bx, by, r, 0, Math.PI * 2);
  ctx.fillStyle = '#3b82f6'; ctx.fill();
  ctx.fillStyle = '#fff'; ctx.beginPath(); ctx.arc(bx, by, r, 0, Math.PI, false); ctx.fill();
  ctx.strokeStyle = '#333'; ctx.lineWidth = 2.5; ctx.stroke();
  // Red stripe accents on top half
  ctx.strokeStyle = '#ef4444'; ctx.lineWidth = 2;
  ctx.beginPath(); ctx.arc(bx, by, r - 5, Math.PI + 0.4, Math.PI * 2 - 0.4); ctx.stroke();
  // Center line and button
  ctx.strokeStyle = '#333'; ctx.lineWidth = 2.5;
  ctx.beginPath(); ctx.moveTo(bx - r, by); ctx.lineTo(bx + r, by); ctx.stroke();
  ctx.beginPath(); ctx.arc(bx, by, 5, 0, 7); ctx.fillStyle = '#fff'; ctx.fill(); ctx.stroke();
  // Glow
  ctx.shadowBlur = 8; ctx.shadowColor = '#3b82f6';
  ctx.beginPath(); ctx.arc(bx, by, 2, 0, 7); ctx.fill();
  ctx.shadowBlur = 0;
}

function drawUltraBall(ctx: CanvasRenderingContext2D, bx: number, by: number, r: number) {
  // Black top, yellow stripes
  ctx.beginPath(); ctx.arc(bx, by, r, 0, Math.PI * 2);
  ctx.fillStyle = '#1f2937'; ctx.fill();
  ctx.fillStyle = '#fff'; ctx.beginPath(); ctx.arc(bx, by, r, 0, Math.PI, false); ctx.fill();
  // Yellow H-stripes
  ctx.strokeStyle = '#facc15'; ctx.lineWidth = 3;
  ctx.beginPath(); ctx.moveTo(bx - 6, by - r + 3); ctx.lineTo(bx - 6, by - 3); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(bx + 6, by - r + 3); ctx.lineTo(bx + 6, by - 3); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(bx - 6, by - r / 2); ctx.lineTo(bx + 6, by - r / 2); ctx.stroke();
  // Border and center
  ctx.strokeStyle = '#333'; ctx.lineWidth = 2.5;
  ctx.beginPath(); ctx.arc(bx, by, r, 0, Math.PI * 2); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(bx - r, by); ctx.lineTo(bx + r, by); ctx.stroke();
  ctx.beginPath(); ctx.arc(bx, by, 5, 0, 7); ctx.fillStyle = '#facc15'; ctx.fill(); ctx.stroke();
  // Glow
  ctx.shadowBlur = 10; ctx.shadowColor = '#facc15';
  ctx.beginPath(); ctx.arc(bx, by, 3, 0, 7); ctx.fill();
  ctx.shadowBlur = 0;
}

function drawMasterBall(ctx: CanvasRenderingContext2D, bx: number, by: number, r: number) {
  // Purple top
  ctx.beginPath(); ctx.arc(bx, by, r, 0, Math.PI * 2);
  ctx.fillStyle = '#7c3aed'; ctx.fill();
  ctx.fillStyle = '#fff'; ctx.beginPath(); ctx.arc(bx, by, r, 0, Math.PI, false); ctx.fill();
  // M pattern
  ctx.strokeStyle = '#ec4899'; ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(bx - 8, by - 4);
  ctx.lineTo(bx - 4, by - 12);
  ctx.lineTo(bx, by - 6);
  ctx.lineTo(bx + 4, by - 12);
  ctx.lineTo(bx + 8, by - 4);
  ctx.stroke();
  // Pink circles
  ctx.fillStyle = '#ec4899';
  ctx.beginPath(); ctx.arc(bx - 8, by - r + 6, 2, 0, 7); ctx.fill();
  ctx.beginPath(); ctx.arc(bx + 8, by - r + 6, 2, 0, 7); ctx.fill();
  // Border and center
  ctx.strokeStyle = '#333'; ctx.lineWidth = 2.5;
  ctx.beginPath(); ctx.arc(bx, by, r, 0, Math.PI * 2); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(bx - r, by); ctx.lineTo(bx + r, by); ctx.stroke();
  ctx.beginPath(); ctx.arc(bx, by, 5, 0, 7); ctx.fillStyle = '#fff'; ctx.fill(); ctx.stroke();
  // Glow
  ctx.shadowBlur = 14; ctx.shadowColor = '#c084fc';
  ctx.beginPath(); ctx.arc(bx, by, 3, 0, 7); ctx.fillStyle = '#e9d5ff'; ctx.fill();
  ctx.shadowBlur = 0;
}

// --- COMPANION DISPATCHER ---

function drawCompanion(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  time: number,
  reaction: 'idle' | 'happy' | 'grind' | 'worried',
  type: CompanionType
) {
  if (type === 'gardevoir') {
    drawGardevoir(ctx, x, y, time, reaction);
  } else {
    drawPikachu(ctx, x, y, time, reaction);
  }
}

// --- PIKACHU ---

function drawPikachu(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  time: number,
  reaction: 'idle' | 'happy' | 'grind' | 'worried'
) {
  ctx.save();
  const bounce = reaction === 'happy' ? Math.sin(time * 0.02) * 4 : 0;
  const ty = y + bounce;

  // Body - yellow circle
  ctx.fillStyle = '#facc15';
  ctx.beginPath(); ctx.arc(x, ty, 14, 0, Math.PI * 2); ctx.fill();

  // Ears - pointy
  ctx.fillStyle = '#facc15';
  // Left ear
  ctx.beginPath();
  ctx.moveTo(x - 8, ty - 12);
  ctx.lineTo(x - 14, ty - 28);
  ctx.lineTo(x - 2, ty - 16);
  ctx.closePath();
  ctx.fill();
  // Left ear tip (black)
  ctx.fillStyle = '#1f2937';
  ctx.beginPath();
  ctx.moveTo(x - 11, ty - 23);
  ctx.lineTo(x - 14, ty - 28);
  ctx.lineTo(x - 8, ty - 24);
  ctx.closePath();
  ctx.fill();

  // Right ear
  ctx.fillStyle = '#facc15';
  ctx.beginPath();
  ctx.moveTo(x + 8, ty - 12);
  ctx.lineTo(x + 14, ty - 28);
  ctx.lineTo(x + 2, ty - 16);
  ctx.closePath();
  ctx.fill();
  // Right ear tip (black)
  ctx.fillStyle = '#1f2937';
  ctx.beginPath();
  ctx.moveTo(x + 11, ty - 23);
  ctx.lineTo(x + 14, ty - 28);
  ctx.lineTo(x + 8, ty - 24);
  ctx.closePath();
  ctx.fill();

  // Red cheeks
  ctx.fillStyle = '#ef4444';
  ctx.beginPath(); ctx.arc(x - 10, ty + 2, 4, 0, Math.PI * 2); ctx.fill();
  ctx.beginPath(); ctx.arc(x + 10, ty + 2, 4, 0, Math.PI * 2); ctx.fill();

  // Eyes
  if (reaction === 'happy') {
    ctx.strokeStyle = '#1f2937'; ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.moveTo(x - 7, ty - 2); ctx.lineTo(x - 5, ty - 5); ctx.lineTo(x - 3, ty - 2); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(x + 3, ty - 2); ctx.lineTo(x + 5, ty - 5); ctx.lineTo(x + 7, ty - 2); ctx.stroke();
  } else if (reaction === 'worried') {
    ctx.fillStyle = '#1f2937';
    ctx.beginPath(); ctx.arc(x - 5, ty - 3, 3, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.arc(x + 5, ty - 3, 3, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#fff';
    ctx.beginPath(); ctx.arc(x - 4, ty - 4, 1, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.arc(x + 6, ty - 4, 1, 0, Math.PI * 2); ctx.fill();
  } else {
    ctx.fillStyle = '#1f2937';
    ctx.beginPath(); ctx.arc(x - 5, ty - 3, 2, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.arc(x + 5, ty - 3, 2, 0, Math.PI * 2); ctx.fill();
  }

  // Mouth
  if (reaction === 'happy') {
    ctx.strokeStyle = '#1f2937'; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.arc(x, ty + 3, 3, 0, Math.PI); ctx.stroke();
  } else if (reaction === 'worried') {
    ctx.strokeStyle = '#1f2937'; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.arc(x, ty + 6, 2, Math.PI, 0); ctx.stroke();
  } else {
    ctx.fillStyle = '#1f2937';
    ctx.beginPath(); ctx.arc(x, ty + 4, 1.5, 0, Math.PI * 2); ctx.fill();
  }

  // Lightning tail
  ctx.fillStyle = '#facc15';
  ctx.strokeStyle = '#92400e';
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(x + 12, ty);
  ctx.lineTo(x + 20, ty - 8);
  ctx.lineTo(x + 16, ty - 4);
  ctx.lineTo(x + 24, ty - 14);
  ctx.lineTo(x + 18, ty - 4);
  ctx.lineTo(x + 22, ty - 6);
  ctx.lineTo(x + 14, ty + 4);
  ctx.closePath();
  ctx.fill();
  ctx.stroke();

  // Sparkles during grind
  if (reaction === 'grind') {
    ctx.fillStyle = '#fbbf24';
    for (let i = 0; i < 3; i++) {
      const sx = x + Math.sin(time * 0.01 + i * 2) * 20;
      const sy = ty - 5 + Math.cos(time * 0.012 + i * 2) * 10;
      ctx.beginPath(); ctx.arc(sx, sy, 1.5, 0, Math.PI * 2); ctx.fill();
    }
  }

  ctx.restore();
}

// --- GARDEVOIR ---

function drawGardevoir(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  time: number,
  reaction: 'idle' | 'happy' | 'grind' | 'worried'
) {
  ctx.save();
  const bounce = reaction === 'happy' ? Math.sin(time * 0.02) * 4 : 0;
  const sway = Math.sin(time * 0.003) * 3;
  const ty = y + bounce;

  // Flowing gown (white, flows behind)
  ctx.fillStyle = '#fff';
  ctx.beginPath();
  ctx.moveTo(x - 8, ty + 4);
  ctx.quadraticCurveTo(x - 16 + sway, ty + 22, x - 10 + sway * 0.5, ty + 28);
  ctx.lineTo(x + 10 - sway * 0.5, ty + 28);
  ctx.quadraticCurveTo(x + 16 - sway, ty + 22, x + 8, ty + 4);
  ctx.closePath();
  ctx.fill();

  // Body - white slender torso
  ctx.fillStyle = '#fff';
  ctx.beginPath(); ctx.ellipse(x, ty, 8, 12, 0, 0, Math.PI * 2); ctx.fill();

  // Green chest spike/plate
  ctx.fillStyle = '#22c55e';
  ctx.beginPath();
  ctx.moveTo(x, ty - 6);
  ctx.lineTo(x - 5, ty + 2);
  ctx.lineTo(x + 5, ty + 2);
  ctx.closePath();
  ctx.fill();

  // Head - white, slightly taller
  ctx.fillStyle = '#fff';
  ctx.beginPath(); ctx.ellipse(x, ty - 16, 9, 10, 0, 0, Math.PI * 2); ctx.fill();

  // Green hair/helmet - swooping to the side
  ctx.fillStyle = '#22c55e';
  ctx.beginPath();
  ctx.moveTo(x + 6, ty - 24);
  ctx.quadraticCurveTo(x + 2, ty - 30, x - 4, ty - 26);
  ctx.quadraticCurveTo(x - 10, ty - 22, x - 8, ty - 16);
  ctx.lineTo(x - 5, ty - 16);
  ctx.quadraticCurveTo(x - 6, ty - 22, x - 2, ty - 24);
  ctx.quadraticCurveTo(x + 2, ty - 26, x + 6, ty - 24);
  ctx.closePath();
  ctx.fill();

  // Hair curl flowing behind
  ctx.beginPath();
  ctx.moveTo(x - 8, ty - 16);
  ctx.quadraticCurveTo(x - 18 + sway, ty - 10, x - 22 + sway * 1.5, ty - 18);
  ctx.quadraticCurveTo(x - 20 + sway, ty - 24, x - 12, ty - 20);
  ctx.closePath();
  ctx.fill();

  // Red horn/crest on chest
  ctx.fillStyle = '#ef4444';
  ctx.beginPath();
  ctx.moveTo(x, ty - 4);
  ctx.lineTo(x - 2, ty + 1);
  ctx.lineTo(x + 2, ty + 1);
  ctx.closePath();
  ctx.fill();

  // Eyes
  if (reaction === 'happy') {
    // Gentle closed eyes
    ctx.strokeStyle = '#ef4444'; ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.arc(x - 4, ty - 16, 2, Math.PI, 0); ctx.stroke();
    ctx.beginPath(); ctx.arc(x + 4, ty - 16, 2, Math.PI, 0); ctx.stroke();
  } else if (reaction === 'worried') {
    // Wide red eyes
    ctx.fillStyle = '#ef4444';
    ctx.beginPath(); ctx.ellipse(x - 4, ty - 16, 2.5, 3, 0, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.ellipse(x + 4, ty - 16, 2.5, 3, 0, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#fff';
    ctx.beginPath(); ctx.arc(x - 3, ty - 17, 1, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.arc(x + 5, ty - 17, 1, 0, Math.PI * 2); ctx.fill();
  } else {
    // Calm red eyes
    ctx.fillStyle = '#ef4444';
    ctx.beginPath(); ctx.ellipse(x - 4, ty - 16, 2, 2.5, 0, 0, Math.PI * 2); ctx.fill();
    ctx.beginPath(); ctx.ellipse(x + 4, ty - 16, 2, 2.5, 0, 0, Math.PI * 2); ctx.fill();
  }

  // Mouth
  if (reaction === 'happy') {
    ctx.strokeStyle = '#ccc'; ctx.lineWidth = 0.8;
    ctx.beginPath(); ctx.arc(x, ty - 12, 2, 0.1, Math.PI - 0.1); ctx.stroke();
  }

  // Arms - thin, elegant
  ctx.strokeStyle = '#fff'; ctx.lineWidth = 3;
  ctx.beginPath();
  ctx.moveTo(x - 7, ty - 2);
  ctx.quadraticCurveTo(x - 14, ty + 6 + sway, x - 12, ty + 14);
  ctx.stroke();
  ctx.beginPath();
  ctx.moveTo(x + 7, ty - 2);
  ctx.quadraticCurveTo(x + 14, ty + 6 - sway, x + 12, ty + 14);
  ctx.stroke();

  // Green fingertips
  ctx.fillStyle = '#22c55e';
  ctx.beginPath(); ctx.arc(x - 12, ty + 14, 2.5, 0, Math.PI * 2); ctx.fill();
  ctx.beginPath(); ctx.arc(x + 12, ty + 14, 2.5, 0, Math.PI * 2); ctx.fill();

  // Psychic aura during grind
  if (reaction === 'grind') {
    ctx.globalAlpha = 0.3;
    ctx.strokeStyle = '#c084fc';
    ctx.lineWidth = 2;
    for (let i = 0; i < 3; i++) {
      const r = 18 + Math.sin(time * 0.008 + i * 2) * 6;
      ctx.beginPath(); ctx.arc(x, ty - 4, r, 0, Math.PI * 2); ctx.stroke();
    }
    ctx.globalAlpha = 1;
  }

  // Psychic sparkles on happy
  if (reaction === 'happy' || reaction === 'grind') {
    ctx.fillStyle = '#e9d5ff';
    for (let i = 0; i < 4; i++) {
      const sx = x + Math.sin(time * 0.008 + i * 1.5) * 22;
      const sy = ty - 8 + Math.cos(time * 0.01 + i * 1.5) * 14;
      const sparkleSize = 1 + Math.sin(time * 0.012 + i) * 0.5;
      ctx.beginPath(); ctx.arc(sx, sy, sparkleSize, 0, Math.PI * 2); ctx.fill();
    }
  }

  ctx.restore();
}

export default GameView;
