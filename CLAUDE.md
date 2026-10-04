# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Development Commands

- `npm run dev` — Start Vite dev server with hot reload
- `npm run build` — TypeScript compilation + Vite production build
- `npm run preview` — Preview production build locally

- `npm test` — Run gameplay, bowl, character pose, and progression tests using Node's built-in test runner (Node 22.6 or later)

Run the mechanics tests and build when changing gameplay. The mechanics tests cover actual descending rail landings, edge overlap, jumps off rails, buffered/coyote jumps, collection, hearts, course completion, and matching outcomes at 30/60/120Hz. They do not replace browser checks of artwork, responsive layout, or input events; only report browser playtesting when it was actually performed.

## Architecture

**Poké-Memories** is a React 19 SPA — a pink-themed casual web game where players roller-skate to collect Pokéballs, unlocking memories displayed in a gallery.

### Tech Stack

- React 19 + TypeScript (strict mode) with Vite 5
- Tailwind CSS (loaded via CDN in `index.html`) for UI styling
- HTML5 Canvas for game rendering (`GameView.tsx`, `BowlView.tsx`)

### Key Files

- **`App.tsx`** — Root component managing game state machine (`START` → `PLAYING` → `GAMEOVER` → `MEMORIES`). Orchestrates transitions between views.
- **`components/GameView.tsx`** — Retina canvas rendering, sunset park scenery, supported rails and grind sparks, keyboard/pointer controls, accessible HUD, and exit control. Accepts `mode`, `outfit`, `onEnd(GameResult)`, and `onExit`; resize changes rendering without resetting a run.
- **`components/gameplay.ts`** — Pure simulation with fixed 120Hz steps, bounded frame deltas, time-based timers, swept descending foot/top rail collisions, and deterministic authored patterns. COURSE runs 450 metres in 30 seconds at 360 logical pixels per second, with distinct Rose Walk, Boardwalk Rush, and Golden Hour sections containing cones, barriers, and four rails. ENDLESS cycles these sections with speed capped at 460 logical pixels per second.
- **`components/gameArt.ts`** — Illustrated girlfriend roller skater with shaded features, wavy hair, outfit colors, and Pikachu/Gardevoir companion artwork. The default look preserves vivid pink hair, a pink outfit with white collar/cuffs/hem, and pink skates.
- **`components/characterPose.ts`** — Joint-based animation with fixed limb lengths: a keyed load/push/recovery/glide cycle, jump extension/tuck driven by vertical velocity, and continuous grind balance corrections. Arms counter the body's movement; hair, skirt, and wheel spokes provide secondary motion. Separate simulation-driven animation clocks preserve the rolling phase during jumps/grinds, scale its cadence with course speed, and reset jump/grind timing on entry. Supporting wheels align with the simulation's foot position even in the air; time-based air/grind blending prevents sudden pose changes. Animation does not change jump physics or collisions.
- **`components/bowlplay.ts`** — Pure BOWL simulation at 120Hz: a flat bottom with curved transition walls, gravity, pump to build speed, air above the coping, and in-air tricks. Runs last 60 seconds; Bronze, Silver, and Gold goals sit at 2000, 6000, and 12000 points.
- **`components/BowlView.tsx`** — Canvas renderer and controls for BOWL. Accepts `outfit`, `onEnd(GameResult)`, and `onExit`; reports `collected` as the goal tier reached.
- **`components/gameplay.test.mjs`** — Runnable pure mechanics tests, including a complete course cleared using actual jumps and rail landings.
- **`progression.ts`** — Memory rewards from cumulative actual Pokéballs, plus a one-time first course clear reward tracked by `SaveData.courseCompleted`. `bowlRewards` grants memories for newly reached bowl tiers (saved in `SaveData.bowlTier`), and each tier adds 25 toward outfit unlocks via `outfitProgress`.
- **`tests/`** — Bowl, character pose, and progression tests.
- **`components/MemoryGallery.tsx`** — Displays unlocked memories in a grid with random placeholder images (picsum.photos). Will be replaced with user-uploaded pictures.
- **`types.ts`** — Shared types including `Memory`, `GameState`, `GameMode`, `GameResult`, `Outfit`, and `SaveData`. `GameResult.distance` is in metres; `collected` counts actual Pokéballs and `trickScore` contains grind points separately.

### Gameplay Controls

- Tap or click the play surface, or press Space, ArrowUp, or W to jump. A grounded press applies the jump immediately. Quick taps still have enough height to clear barriers; holding gives extra height. Releasing near the apex does not abruptly cut velocity.
- Descend onto a rail or stair handrail to grind for 60 points per second. Kickers launch extra air, and falling into a gap costs a heart.
- In the air, press J, K, or L (or the on screen buttons) for Pink Grab, Heart Kick, or 360 Twirl. In the bowl, I does a Backflip. Air tricks started right after takeoff grade Perfect and score more. Landing tricks quickly back to back builds a combo multiplier. In the bowl, pressing another trick mid trick queues it so one big air can chain several tricks for a chain bonus. Press the handstand key as she nears the coping for a timed lip handstand; a mistimed press, or landing mid trick, makes her fall and adds a bruise.
- In BOWL, hold Space, ArrowUp, W, or the play surface to pump and build speed until she airs out of the coping; use the trick buttons once airborne. Press U as she reaches the coping for a Lip Handstand; a press within 0.1s of the lip is Perfect, within 0.3s is Good, otherwise it misses. Consecutive Perfects build a streak that boosts the combo, and a miss resets it. Release and press jump again to leave the rail.
- Jump buffering and coyote time make near-landing presses and late rail departures forgiving.
- Each run starts with three hearts and grants 1.8 seconds of protection after a hit. Losing all hearts ends the run without a course clear.
- Blur or hiding the tab releases held inputs and pauses gameplay; tap or press jump to resume. Pointer cancellation also releases held input.
- Exit is separate from the jump target and returns to the menu. Completing COURSE produces clear feedback before reporting its result.

### Patterns

- Functional components with hooks; no external state management
- Game loop uses `requestAnimationFrame` with `useRef` for mutable game state that shouldn't trigger re-renders
- Each five cumulative collected Pokéballs unlocks a memory using floor thresholds; grind and trick points never count toward collection. BOWL runs do not change `totalCollected` or the high score. The first COURSE clear grants one additional memory, only once per save.
- Consistent rose/pink color palette throughout (background: `#fff1f2`)
- ES modules (`"type": "module"` in package.json)

### Sprite Artwork

The current character is a procedural illustration, not an artist-drawn sprite sheet. Exporting it as PNG does not by itself improve its quality. A future sprite replacement needs an approved character design, consistent rolling/push frames, ascent/descent frames, a landing animation, and a grind loop. Use transparent frames of the same size (for example 256 by 256), with a shared wheel-contact anchor and documented frame order and timing. Keep the simulation independent of animation: feet remain anchored to `state.foot`, and visual frames must not change jump height or rail collision.
