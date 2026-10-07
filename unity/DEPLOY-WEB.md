# Web build (iPhone Safari / Vercel)

1. Build: Unity menu `PokeMemories > Build WebGL` (or batchmode `-executeMethod PokeMemories.EditorTools.WebBuilder.Build`). Output: `Builds/Web` (gitignored).
2. `cp vercel.json Builds/Web/` (sets Brotli Content-Encoding headers and the `/media/` proxy to the photo bucket, which sends no CORS headers).
3. From `Builds/Web`: `npx vercel link --yes --project poke-memories-unity` then `npx vercel deploy --prod`.

Course and Endless are framed for landscape: rotate the phone. Menu, Memory Book and Rose Bowl work in portrait.
