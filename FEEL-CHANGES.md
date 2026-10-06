# Unity-only simulation rule changes (branch unity-feel)

All in `unity/Assets/Scripts/Gameplay/CourseSimulation.cs`. Mirror in `components/gameplay.ts` before the web branch is frozen.
Heights, jump speeds, collision boxes and timers are unchanged except where listed.

1. **Pace (momentum)**: `Pace` in [0.82, 1.28], `Speed = Baseline * Pace`.
   - Ground: push stroke adds `0.2 * max(0, sin(2π·phase))` pace/s (phase = RollTime / 0.6s); drag `0.9 * (Pace-1)` per second.
   - Rail grind: +0.22 pace/s. Air: pace drifts to 1 at 0.25/s.
   - Hurt: pace -0.2.
2. **Touchdown** (`Touchdown`): `weight = clamp((impact-300)/550, 0, 1)`. Sets `Crouch`. On a clean landing: boost `-0.14*weight`, plus `0.07 + 0.015*min(5,Combo)` after banked tricks, else `0.05` on a rail, else `0.03*(1-weight)`.
3. **Gravity**: falling x1.25, within 150 of the apex x0.8, terminal speed 950 (was 850 cap).
4. **Rail magnet**: a rail catches the foot up to 10 px past its top (`before > RailMagnet` instead of `> 0.001`).
5. **Animation-only state** (no physics effect): `Crouch`, `BoostPulse`, `SnapOffset`, `PumpPhase`, `Bails`.

Non-sim: autopilot lead distances scale with Pace; `VisualPolish.cs` (URP 2D lights, bloom/vignette, trail), AudioListener added in `GameFeel.Bind`, sprite atlases via `Assets/Editor/SpriteAtlasPrebuildGenerator.cs`.
