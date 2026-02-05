# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Development Commands

- `npm run dev` — Start Vite dev server with hot reload
- `npm run build` — TypeScript compilation + Vite production build
- `npm run preview` — Preview production build locally

No test runner is configured.

## Architecture

**Poké-Memories** is a React 19 SPA — a pink-themed casual web game where players roller-skate to collect Pokéballs, unlocking memories displayed in a gallery.

### Tech Stack

- React 19 + TypeScript (strict mode) with Vite 5
- Tailwind CSS (loaded via CDN in `index.html`) for UI styling
- HTML5 Canvas for game rendering (`GameView.tsx`)

### Key Files

- **`App.tsx`** — Root component managing game state machine (`START` → `PLAYING` → `GAMEOVER` → `MEMORIES`). Orchestrates transitions between views.
- **`components/GameView.tsx`** — Canvas-based game engine. Contains physics (gravity, jumping), collision detection, spawning logic, particle effects, and hand-drawn character rendering. All game constants (GRAVITY, JUMP, SPAWN_RATE, etc.) are defined at the top.
- **`components/MemoryGallery.tsx`** — Displays unlocked memories in a grid with random placeholder images (picsum.photos). Will be replaced with user-uploaded pictures.
- **`types.ts`** — Shared TypeScript interfaces: `Memory`, `GameState`, `GameObject`.

### Patterns

- Functional components with hooks; no external state management
- Game loop uses `requestAnimationFrame` with `useRef` for mutable game state that shouldn't trigger re-renders
- Score unlocks memories at a rate of `Math.ceil(score / 5)`
- Consistent rose/pink color palette throughout (background: `#fff1f2`)
- ES modules (`"type": "module"` in package.json)
