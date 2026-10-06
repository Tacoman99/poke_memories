// Exports memories.ts to unity/Assets/Resources/memories.json (the web game's memory pool is the source).
// Run from the repo root: node tools/memories/export.mjs
import { readFileSync, writeFileSync } from 'node:fs';

const source = readFileSync('memories.ts', 'utf8')
  .replace(/^import .*$/m, '')
  .replace('export const MEMORY_POOL: Memory[] =', 'return');
const pool = new Function('B2src', `const B2 = B2src; ${source.replace(/^const B2 = .*$/m, '')}`)(
  /const B2 = '([^']+)'/.exec(readFileSync('memories.ts', 'utf8'))[1]);

const memories = pool.map(m => ({
  id: m.id,
  caption: m.caption,
  date: m.date ?? '',
  media: m.media.map(i => ({ type: i.type, url: i.url })),
}));
writeFileSync('unity/Assets/Resources/memories.json', JSON.stringify({ memories }, null, 2) + '\n');
console.log(`Wrote ${memories.length} memories`);
