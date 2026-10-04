import type { GameResult } from './types';

export function memoryRewards(totalCollected: number, courseCompleted: boolean, result: GameResult) {
  const firstClear = result.mode === 'COURSE' && result.completed && !courseCompleted;
  const newTotal = totalCollected + result.collected;
  return {
    newTotal,
    firstClear,
    earned: Math.floor(newTotal / 5) - Math.floor(totalCollected / 5) + (firstClear ? 1 : 0),
  };
}

export const BOWL_TIER_OUTFIT_BONUS = 25;

export function bowlRewards(savedTier: number, tier: number) {
  return { newTier: Math.max(savedTier, tier), earned: Math.max(0, tier - savedTier) };
}

export function outfitProgress(totalCollected: number, bowlTier: number) {
  return totalCollected + bowlTier * BOWL_TIER_OUTFIT_BONUS;
}
