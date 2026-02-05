
import { useState, useEffect, useCallback } from 'react';
import { Heart, Play, BookHeart, RefreshCcw, Trophy, Sparkles, ChevronLeft, ChevronRight } from 'lucide-react';
import GameView from './components/GameView';
import MemoryGallery from './components/MemoryGallery';
import { GameState, SaveData, Outfit } from './types';
import { MEMORY_POOL } from './memories';

const OUTFITS: Outfit[] = [
  { name: 'Classic Pink', bodyColor: '#db2777', hairColor: '#ff69b4', hairHighlight: '#ff85c0', skateColor: '#db2777', trimColor: '#f9a8d4', unlockScore: 0, companion: 'pikachu' },
  { name: 'Gardevoir', bodyColor: '#4ade80', hairColor: '#22c55e', hairHighlight: '#86efac', skateColor: '#16a34a', trimColor: '#bbf7d0', unlockScore: 30, companion: 'gardevoir' },
  { name: 'Pikachu Yellow', bodyColor: '#facc15', hairColor: '#fbbf24', hairHighlight: '#fde68a', skateColor: '#eab308', trimColor: '#fef08a', unlockScore: 50, companion: 'pikachu' },
  { name: 'Team Rocket', bodyColor: '#1f2937', hairColor: '#6366f1', hairHighlight: '#818cf8', skateColor: '#111827', trimColor: '#4b5563', unlockScore: 100, companion: 'pikachu' },
  { name: 'Eevee Brown', bodyColor: '#92400e', hairColor: '#a16207', hairHighlight: '#ca8a04', skateColor: '#78350f', trimColor: '#d97706', unlockScore: 200, companion: 'pikachu' },
  { name: 'Shiny Holo', bodyColor: '#c084fc', hairColor: '#f0abfc', hairHighlight: '#67e8f9', skateColor: '#a855f7', trimColor: '#e879f9', unlockScore: 500, companion: 'gardevoir' },
];

const STORAGE_KEY = 'poke-memories-save';

function loadSave(): SaveData {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const data = JSON.parse(raw);
      return {
        highScore: data.highScore ?? 0,
        totalCollected: data.totalCollected ?? 0,
        unlockedMemoryIds: data.unlockedMemoryIds ?? [],
        unlockedOutfits: data.unlockedOutfits ?? ['Classic Pink'],
        selectedOutfit: data.selectedOutfit ?? 'Classic Pink',
      };
    }
  } catch { /* ignore corrupt data */ }
  return { highScore: 0, totalCollected: 0, unlockedMemoryIds: [], unlockedOutfits: ['Classic Pink'], selectedOutfit: 'Classic Pink' };
}

function writeSave(data: SaveData) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(data));
}

/** Randomly pick `count` new memory IDs from the pool that aren't already unlocked */
function pickNewMemories(alreadyUnlocked: string[], count: number): string[] {
  const available = MEMORY_POOL.filter(m => !alreadyUnlocked.includes(m.id));
  const picked: string[] = [];
  const pool = [...available];
  for (let i = 0; i < count && pool.length > 0; i++) {
    const idx = Math.floor(Math.random() * pool.length);
    picked.push(pool[idx].id);
    pool.splice(idx, 1);
  }
  return picked;
}

const App: React.FC = () => {
  const [save, setSave] = useState<SaveData>(loadSave);
  const [gameState, setGameState] = useState<GameState>('START');
  const [score, setScore] = useState(0);
  const [isNewHighScore, setIsNewHighScore] = useState(false);
  const [newlyUnlockedCount, setNewlyUnlockedCount] = useState(0);

  // Persist save whenever it changes
  useEffect(() => { writeSave(save); }, [save]);

  const currentOutfit = OUTFITS.find(o => o.name === save.selectedOutfit) ?? OUTFITS[0];
  const availableOutfits = OUTFITS.filter(o => save.totalCollected >= o.unlockScore);

  const cycleOutfit = (dir: number) => {
    const idx = availableOutfits.findIndex(o => o.name === save.selectedOutfit);
    const next = (idx + dir + availableOutfits.length) % availableOutfits.length;
    setSave(prev => ({ ...prev, selectedOutfit: availableOutfits[next].name }));
  };

  const startGame = () => {
    setScore(0);
    setIsNewHighScore(false);
    setNewlyUnlockedCount(0);
    setGameState('PLAYING');
  };

  const endGame = useCallback((finalScore: number) => {
    setScore(finalScore);
    setSave(prev => {
      const newTotal = prev.totalCollected + finalScore;
      const beaten = finalScore > prev.highScore;
      setIsNewHighScore(beaten);

      // How many total memories should be unlocked based on cumulative score
      const totalMemoriesEarned = Math.min(Math.ceil(newTotal / 5), MEMORY_POOL.length);
      const currentUnlocked = prev.unlockedMemoryIds.length;
      const toUnlock = Math.max(0, totalMemoriesEarned - currentUnlocked);
      const newIds = pickNewMemories(prev.unlockedMemoryIds, toUnlock);
      setNewlyUnlockedCount(newIds.length);

      const newUnlockedOutfits = OUTFITS.filter(o => newTotal >= o.unlockScore).map(o => o.name);
      return {
        ...prev,
        highScore: beaten ? finalScore : prev.highScore,
        totalCollected: newTotal,
        unlockedMemoryIds: [...prev.unlockedMemoryIds, ...newIds],
        unlockedOutfits: newUnlockedOutfits,
      };
    });
    setGameState('GAMEOVER');
  }, []);

  const openMemories = () => setGameState('MEMORIES');
  const backToMenu = () => setGameState('START');

  // Next outfit to unlock
  const nextLock = OUTFITS.find(o => save.totalCollected < o.unlockScore);

  return (
    <div className={`min-h-screen bg-rose-50 text-rose-900 overflow-hidden flex flex-col items-center justify-center ${gameState === 'PLAYING' ? '' : 'p-4'}`}>
      {/* Background Decor (Only on Menu) */}
      {gameState !== 'PLAYING' && (
        <div className="fixed inset-0 pointer-events-none opacity-20">
          {[...Array(20)].map((_, i) => (
            <Heart
              key={i}
              className="absolute animate-pulse text-rose-300"
              style={{
                top: `${Math.random() * 100}%`,
                left: `${Math.random() * 100}%`,
                transform: `scale(${Math.random() * 2}) rotate(${Math.random() * 360}deg)`,
              }}
            />
          ))}
        </div>
      )}

      {gameState === 'START' && (
        <div className="relative z-10 text-center max-w-md w-full bg-white/80 backdrop-blur-sm p-8 rounded-3xl shadow-xl border-4 border-rose-200 animate-in fade-in duration-500">
          <div className="flex justify-center mb-6">
            <div className="p-4 bg-rose-100 rounded-full float-animation">
              <Heart className="w-16 h-16 text-rose-500 fill-current" />
            </div>
          </div>
          <h1 className="text-4xl font-bold mb-2 text-rose-600">Poke-Memories</h1>
          <p className="text-rose-400 mb-4 italic">Skate through dreams, collect love.</p>

          {/* High Score */}
          {save.highScore > 0 && (
            <div className="mb-4 flex items-center justify-center gap-2 text-rose-500">
              <Trophy size={18} />
              <span className="font-bold">High Score: {save.highScore}</span>
              <span className="text-rose-300 text-sm ml-2">({save.totalCollected} total collected)</span>
            </div>
          )}

          {/* Outfit Selector */}
          {availableOutfits.length > 1 && (
            <div className="mb-4 p-3 bg-rose-50 rounded-2xl border border-rose-200">
              <div className="flex items-center justify-center gap-3">
                <button onClick={() => cycleOutfit(-1)} className="p-1 hover:bg-rose-100 rounded-full transition-colors">
                  <ChevronLeft size={20} className="text-rose-400" />
                </button>
                <div className="flex items-center gap-2">
                  <div className="w-6 h-6 rounded-full border-2 border-white shadow-sm" style={{ background: currentOutfit.bodyColor }} />
                  <span className="font-semibold text-rose-600 text-sm">{currentOutfit.name}</span>
                </div>
                <button onClick={() => cycleOutfit(1)} className="p-1 hover:bg-rose-100 rounded-full transition-colors">
                  <ChevronRight size={20} className="text-rose-400" />
                </button>
              </div>
              {nextLock && (
                <p className="text-xs text-rose-300 mt-1">Next: {nextLock.name} at {nextLock.unlockScore} total</p>
              )}
            </div>
          )}

          <div className="space-y-4">
            <button
              onClick={startGame}
              className="w-full flex items-center justify-center gap-2 bg-rose-500 hover:bg-rose-600 text-white py-4 rounded-2xl font-bold text-xl transition-all shadow-lg hover:shadow-rose-200 active:scale-95"
            >
              <Play className="fill-current" /> Play Game
            </button>
            <button
              onClick={openMemories}
              className="w-full flex items-center justify-center gap-2 bg-pink-100 hover:bg-pink-200 text-pink-600 py-3 rounded-2xl font-semibold transition-all"
            >
              <BookHeart /> My Memory Book ({save.unlockedMemoryIds.length} / {MEMORY_POOL.length})
            </button>
          </div>

        </div>
      )}

      {gameState === 'PLAYING' && (
        <GameView onEnd={endGame} outfit={currentOutfit} />
      )}

      {gameState === 'GAMEOVER' && (
        <div className="relative z-10 text-center max-w-md w-full bg-white/90 backdrop-blur-md p-8 rounded-3xl shadow-2xl border-4 border-rose-300 animate-in fade-in zoom-in duration-300">
          {isNewHighScore && (
            <div className="mb-4 flex items-center justify-center gap-2 text-amber-500 animate-bounce">
              <Sparkles size={24} />
              <span className="font-black text-xl">NEW HIGH SCORE!</span>
              <Sparkles size={24} />
            </div>
          )}
          <h2 className="text-3xl font-bold text-rose-600 mb-2">Wonderful Session!</h2>
          <div className="flex flex-col items-center gap-2 mb-4">
            <div className="text-6xl font-black text-rose-500">{score}</div>
            <div className="text-rose-400 font-medium text-lg">Pokeballs Collected</div>
          </div>

          {save.highScore > 0 && !isNewHighScore && (
            <p className="text-rose-300 text-sm mb-4">High Score: {save.highScore}</p>
          )}

          <div className="grid grid-cols-2 gap-4 mb-6">
            <button
              onClick={startGame}
              className="flex items-center justify-center gap-2 bg-rose-500 text-white p-4 rounded-2xl font-bold hover:bg-rose-600 transition-colors shadow-lg"
            >
              <RefreshCcw size={20} /> Try Again
            </button>
            <button
              onClick={backToMenu}
              className="flex items-center justify-center gap-2 bg-gray-100 text-gray-600 p-4 rounded-2xl font-bold hover:bg-gray-200 transition-colors"
            >
              Menu
            </button>
          </div>

          {newlyUnlockedCount > 0 && (
            <div className="mt-4 p-4 bg-rose-50 rounded-xl border-2 border-rose-200 text-rose-800">
              <p className="text-sm font-bold">You unlocked {newlyUnlockedCount} new {newlyUnlockedCount === 1 ? 'memory' : 'memories'}!</p>
              <button
                onClick={openMemories}
                className="mt-2 text-rose-600 underline font-semibold flex items-center justify-center gap-1 mx-auto"
              >
                <BookHeart size={16} /> View Memory Book
              </button>
            </div>
          )}

          {newlyUnlockedCount === 0 && score > 0 && save.unlockedMemoryIds.length < MEMORY_POOL.length && (
            <div className="mt-4 p-4 bg-rose-50 rounded-xl border-2 border-rose-200 text-rose-800">
              <p className="text-sm">Keep collecting to unlock more memories!</p>
              <p className="text-xs text-rose-400 mt-1">{save.unlockedMemoryIds.length} / {MEMORY_POOL.length} unlocked</p>
            </div>
          )}

        </div>
      )}

      {gameState === 'MEMORIES' && (
        <MemoryGallery
          unlockedIds={save.unlockedMemoryIds}
          onBack={backToMenu}
        />
      )}
    </div>
  );
};

export default App;
