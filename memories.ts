import { Memory } from './types';

// =====================================================
// YOUR MEMORY POOL - Add your photos and captions here!
// =====================================================
// Drop photos into public/memories/ and reference them below.
// Example: a file at public/memories/beach.jpg becomes "/memories/beach.jpg"
//
// Each memory needs:
//   id:       unique string (keep it short)
//   imageUrl: path to the image in public/memories/
//   caption:  the text shown below the photo
//
// The game randomly unlocks these as she collects Pokeballs.
// She gets 1 new memory per 5 total Pokeballs collected (across all sessions).

export const MEMORY_POOL: Memory[] = [
  // --- Replace these with your real photos and captions ---
  { id: 'mem-1', imageUrl: '/memories/1.jpg', caption: 'Our first adventure together' },
  { id: 'mem-2', imageUrl: '/memories/2.jpg', caption: 'That time we got lost' },
  { id: 'mem-3', imageUrl: '/memories/3.jpg', caption: 'Your beautiful smile' },
  { id: 'mem-4', imageUrl: '/memories/4.jpg', caption: 'Best day ever' },
  { id: 'mem-5', imageUrl: '/memories/5.jpg', caption: 'You and me against the world' },
  { id: 'mem-6', imageUrl: '/memories/6.jpg', caption: 'Remember this?' },
  { id: 'mem-7', imageUrl: '/memories/7.jpg', caption: 'My favorite person' },
  { id: 'mem-8', imageUrl: '/memories/8.jpg', caption: 'Wouldn\'t trade this for anything' },
  { id: 'mem-9', imageUrl: '/memories/9.jpg', caption: 'Making memories with you' },
  { id: 'mem-10', imageUrl: '/memories/10.jpg', caption: 'Forever grateful for you' },
  // Add as many as you want!
];
