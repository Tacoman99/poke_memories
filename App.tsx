
import { useState, useEffect, useCallback } from 'react';
import { Heart, Play, BookHeart, RefreshCcw, Trophy, Sparkles, ChevronLeft, ChevronRight, Flag, Infinity as InfinityIcon, Waves, Medal } from 'lucide-react';
import GameView from './components/GameView';
import BowlView from './components/BowlView';
import { BOWL_GOALS } from './components/bowlplay';
import MemoryGallery from './components/MemoryGallery';
import { GameState, SaveData, Outfit, GameMode, GameResult } from './types';
import { MEMORY_POOL } from './memories';
import { bowlRewards, memoryRewards, outfitProgress } from './progression';

const OUTFITS: Outfit[] = [
  { name: 'Classic Pink', bodyColor: '#db2777', hairColor: '#ff289c', hairHighlight: '#ff91d2', skateColor: '#db2777', trimColor: '#f9a8d4', unlockScore: 0, companion: 'pikachu' },
  { name: 'Gardevoir', bodyColor: '#4ade80', hairColor: '#22c55e', hairHighlight: '#86efac', skateColor: '#16a34a', trimColor: '#bbf7d0', unlockScore: 30, companion: 'gardevoir' },
  { name: 'Pikachu Yellow', bodyColor: '#facc15', hairColor: '#fbbf24', hairHighlight: '#fde68a', skateColor: '#eab308', trimColor: '#fef08a', unlockScore: 50, companion: 'pikachu' },
  { name: 'Team Rocket', bodyColor: '#1f2937', hairColor: '#6366f1', hairHighlight: '#818cf8', skateColor: '#111827', trimColor: '#4b5563', unlockScore: 100, companion: 'pikachu' },
  { name: 'Eevee Brown', bodyColor: '#92400e', hairColor: '#a16207', hairHighlight: '#ca8a04', skateColor: '#78350f', trimColor: '#d97706', unlockScore: 200, companion: 'pikachu' },
  { name: 'Shiny Holo', bodyColor: '#c084fc', hairColor: '#f0abfc', hairHighlight: '#67e8f9', skateColor: '#a855f7', trimColor: '#e879f9', unlockScore: 500, companion: 'gardevoir' },
];

const STORAGE_KEY = 'poke-memories-save';

function loadSave(): SaveData {
  const empty: SaveData = { highScore: 0, totalCollected: 0, unlockedMemoryIds: [], unlockedOutfits: ['Classic Pink'], selectedOutfit: 'Classic Pink', courseCompleted: false, bowlBest: 0, bowlTier: 0 };
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const data: unknown = JSON.parse(raw);
      if (!data || typeof data !== 'object') throw new Error('Invalid saved progress');
      const fields = data as Record<string, unknown>;
      const count = (value: unknown) => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : 0;
      const totalCollected = count(fields.totalCollected);
      const bowlTier = Math.min(BOWL_GOALS.length, count(fields.bowlTier));
      const progress = outfitProgress(totalCollected, bowlTier);
      const unlockedOutfits = OUTFITS.filter(o => progress >= o.unlockScore).map(o => o.name);
      return {
        highScore: count(fields.highScore),
        totalCollected,
        unlockedMemoryIds: Array.isArray(fields.unlockedMemoryIds)
          ? [...new Set(fields.unlockedMemoryIds.filter((id): id is string => typeof id === 'string' && MEMORY_POOL.some(m => m.id === id)))]
          : [],
        unlockedOutfits,
        selectedOutfit: typeof fields.selectedOutfit === 'string' && unlockedOutfits.includes(fields.selectedOutfit) ? fields.selectedOutfit : 'Classic Pink',
        courseCompleted: fields.courseCompleted === true,
        bowlBest: count(fields.bowlBest),
        bowlTier,
      };
    }
  } catch (error) {
    console.error('Unable to load saved skating progress:', error);
  }
  return empty;
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
  const [mode, setMode] = useState<GameMode>('COURSE');
  const [result, setResult] = useState<GameResult | null>(null);
  const [isNewHighScore, setIsNewHighScore] = useState(false);
  const [newlyUnlockedCount, setNewlyUnlockedCount] = useState(0);
  const [saveError, setSaveError] = useState(false);

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(save));
      setSaveError(false);
    } catch (error) {
      console.error('Unable to save skating progress:', error);
      setSaveError(true);
    }
  }, [save]);

  const currentOutfit = OUTFITS.find(o => o.name === save.selectedOutfit) ?? OUTFITS[0];
  const availableOutfits = OUTFITS.filter(o => save.unlockedOutfits.includes(o.name));

  const cycleOutfit = (dir: number) => {
    const idx = availableOutfits.findIndex(o => o.name === save.selectedOutfit);
    const next = (idx + dir + availableOutfits.length) % availableOutfits.length;
    setSave(prev => ({ ...prev, selectedOutfit: availableOutfits[next].name }));
  };

  const startGame = () => {
    setResult(null);
    setIsNewHighScore(false);
    setNewlyUnlockedCount(0);
    setGameState('PLAYING');
  };

  const endGame = useCallback((finished: GameResult) => {
    if (finished.mode === 'BOWL') {
      const { newTier, earned } = bowlRewards(save.bowlTier, finished.collected);
      const newIds = pickNewMemories(save.unlockedMemoryIds, earned);
      const progress = outfitProgress(save.totalCollected, newTier);
      setResult(finished);
      setIsNewHighScore(finished.trickScore > save.bowlBest);
      setNewlyUnlockedCount(newIds.length);
      setSave({
        ...save,
        bowlBest: Math.max(save.bowlBest, finished.trickScore),
        bowlTier: newTier,
        unlockedMemoryIds: [...save.unlockedMemoryIds, ...newIds],
        unlockedOutfits: OUTFITS.filter(o => progress >= o.unlockScore).map(o => o.name),
      });
      setGameState('GAMEOVER');
      return;
    }
    const { newTotal, firstClear, earned } = memoryRewards(save.totalCollected, save.courseCompleted, finished);
    const newIds = pickNewMemories(save.unlockedMemoryIds, earned);
    setResult(finished);
    setIsNewHighScore(finished.collected > save.highScore);
    setNewlyUnlockedCount(newIds.length);
    setSave({
      ...save,
      highScore: Math.max(save.highScore, finished.collected),
      totalCollected: newTotal,
      unlockedMemoryIds: [...save.unlockedMemoryIds, ...newIds],
      unlockedOutfits: OUTFITS.filter(o => outfitProgress(newTotal, save.bowlTier) >= o.unlockScore).map(o => o.name),
      courseCompleted: save.courseCompleted || firstClear,
    });
    setGameState('GAMEOVER');
  }, [save]);

  const openMemories = () => setGameState('MEMORIES');
  const backToMenu = () => setGameState('START');

  // Next outfit to unlock
  const outfitPoints = outfitProgress(save.totalCollected, save.bowlTier);
  const nextLock = OUTFITS.find(o => outfitPoints < o.unlockScore);
  const nextBowlGoal = BOWL_GOALS.find(g => g.tier > save.bowlTier);
  const medalName = (tier: number) => BOWL_GOALS.find(g => g.tier === tier)?.name ?? 'No medal';
  const isBowlResult = result?.mode === 'BOWL';

  return (
    <div className={`min-h-screen text-rose-900 overflow-hidden flex flex-col items-center justify-center ${gameState === 'PLAYING' ? '' : 'p-4'}`} style={{ background: 'radial-gradient(ellipse at top left, #fde68a66, transparent 55%), linear-gradient(135deg, #fff1f2, #fce7f3 55%, #ede9fe)' }}>
      {saveError && (
        <div role="alert" className="fixed bottom-3 left-3 right-3 z-[100] bg-white border-2 border-rose-400 rounded-xl p-3 text-sm text-center">
          Progress is only kept for this visit. Your browser could not save it.
        </div>
      )}
      {/* Background Decor (Only on Menu) */}
      {gameState !== 'PLAYING' && (
        <div className="fixed inset-0 pointer-events-none opacity-20">
          {[...Array(20)].map((_, i) => (
            <Heart
              key={i}
              className="absolute animate-pulse text-rose-300"
              style={{
                top: `${(i * 37 + 7) % 100}%`,
                left: `${(i * 23 + 11) % 100}%`,
                transform: `scale(${0.6 + (i % 4) * 0.3}) rotate(${i * 29}deg)`,
              }}
            />
          ))}
        </div>
      )}

      {gameState === 'START' && (
        <div className="relative z-10 text-center max-w-lg w-full bg-white/85 backdrop-blur-sm p-6 sm:p-8 rounded-3xl shadow-xl border-2 border-white">
          <div className="flex justify-center mb-4">
            <div className="p-3 bg-rose-100 rounded-full">
              <Heart className="w-10 h-10 text-rose-500 fill-current" />
            </div>
          </div>
          <p className="text-xs font-bold tracking-[0.25em] uppercase text-rose-400 mb-2">A little skate date</p>
          <h1 className="text-4xl font-bold mb-2 text-rose-600">Poke-Memories</h1>
          <p className="text-rose-500 mb-5">Jump the obstacles. Land on rails. Bring home memories.</p>

          <div className="grid grid-cols-3 gap-2 sm:gap-3 mb-4">
            {(['COURSE', 'ENDLESS', 'BOWL'] as const).map(choice => (
              <button key={choice} onClick={() => setMode(choice)} aria-pressed={mode === choice}
                className={`text-left p-3 sm:p-4 rounded-2xl border-2 transition-colors ${mode === choice ? 'border-rose-500 bg-rose-50 shadow-sm' : 'border-rose-100 bg-white hover:border-rose-300'}`}>
                {choice === 'COURSE' ? <Flag size={24} className="text-rose-500 mb-2" /> : choice === 'ENDLESS' ? <InfinityIcon size={24} className="text-violet-500 mb-2" /> : <Waves size={24} className="text-sky-500 mb-2" />}
                <span className="block font-bold">{choice === 'COURSE' ? 'Sunset course' : choice === 'ENDLESS' ? 'Endless skate' : 'Skate bowl'}</span>
                <span className="block text-xs text-rose-500 mt-1">{choice === 'COURSE' ? '30 seconds. Three different sections.' : choice === 'ENDLESS' ? 'Keep rolling. Chase your best.' : '60 second session. Pump, fly, trick.'}</span>
              </button>
            ))}
          </div>
          <p className="text-sm text-rose-500 mb-4">
            {mode === 'COURSE'
              ? save.courseCompleted ? 'Course cleared! Replay for more Pokeballs and rail tricks.' : 'Your first finish unlocks a memory, plus any you earn collecting.'
              : mode === 'ENDLESS'
                ? 'Every 5 Pokeballs collected across your runs unlocks a memory.'
                : nextBowlGoal ? `Beat ${nextBowlGoal.score.toLocaleString()} points for ${nextBowlGoal.name}. Each new medal unlocks a memory and outfit progress.` : 'All bowl medals earned! Chase your best score.'}
          </p>
          <div className="text-left bg-violet-50 border border-violet-100 rounded-2xl p-4 mb-5 text-sm">
            {mode === 'BOWL' ? (<>
              <p className="font-bold text-violet-800 mb-1">Hold Space or the pump button going down, let go going up</p>
              <p className="text-violet-700">Pump to build speed and launch above the coping. In the air, hit a trick button. Land straight or you bail.</p>
            </>) : (<>
              <p className="font-bold text-violet-800 mb-1">Tap or press Space to jump</p>
              <p className="text-violet-700">Hold for a higher jump. Land on a rail from above to grind, then jump off. Avoid cones and barriers.</p>
            </>)}
          </div>

          {/* High Score */}
          {mode === 'BOWL' && save.bowlBest > 0 && (
            <div className="mb-4 flex items-center justify-center gap-2 text-sky-600">
              <Medal size={18} />
              <span className="font-bold">Bowl best: {save.bowlBest.toLocaleString()}</span>
              <span className="text-sky-400 text-sm ml-2">({medalName(save.bowlTier)})</span>
            </div>
          )}
          {mode !== 'BOWL' && save.highScore > 0 && (
            <div className="mb-4 flex items-center justify-center gap-2 text-rose-500">
              <Trophy size={18} />
              <span className="font-bold">Best haul: {save.highScore}</span>
              <span className="text-rose-300 text-sm ml-2">({save.totalCollected} total collected)</span>
            </div>
          )}

          {/* Outfit Selector */}
          {availableOutfits.length > 1 && (
            <div className="mb-4 p-3 bg-rose-50 rounded-2xl border border-rose-200">
              <div className="flex items-center justify-center gap-3">
                <button onClick={() => cycleOutfit(-1)} aria-label="Previous outfit" className="p-1 hover:bg-rose-100 rounded-full transition-colors">
                  <ChevronLeft size={20} className="text-rose-400" />
                </button>
                <div className="flex items-center gap-2">
                  <div className="w-6 h-6 rounded-full border-2 border-white shadow-sm" style={{ background: currentOutfit.bodyColor }} />
                  <span className="font-semibold text-rose-600 text-sm">{currentOutfit.name}</span>
                </div>
                <button onClick={() => cycleOutfit(1)} aria-label="Next outfit" className="p-1 hover:bg-rose-100 rounded-full transition-colors">
                  <ChevronRight size={20} className="text-rose-400" />
                </button>
              </div>
              {nextLock && (
                <p className="text-xs text-rose-300 mt-1">Next: {nextLock.name} at {nextLock.unlockScore} points ({outfitPoints} now)</p>
              )}
            </div>
          )}

          <div className="space-y-4">
            <button
              onClick={startGame}
              className="w-full flex items-center justify-center gap-2 bg-rose-500 hover:bg-rose-600 text-white py-4 rounded-2xl font-bold text-xl transition-all shadow-lg hover:shadow-rose-200 active:scale-95"
            >
              <Play className="fill-current" /> {mode === 'COURSE' ? 'Skate the course' : mode === 'ENDLESS' ? 'Go endless' : 'Drop into the bowl'}
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

      {gameState === 'PLAYING' && mode === 'BOWL' && (
        <BowlView onEnd={endGame} onExit={backToMenu} outfit={currentOutfit} />
      )}
      {gameState === 'PLAYING' && mode !== 'BOWL' && (
        <GameView mode={mode} onEnd={endGame} onExit={backToMenu} outfit={currentOutfit} />
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
          {isBowlResult ? (<>
            <h2 className="text-3xl font-bold text-rose-600 mb-2">{result?.completed ? `${medalName(result.collected)} session!` : 'Session over'}</h2>
            <p className="text-rose-500 mb-4">{nextBowlGoal ? `${nextBowlGoal.score.toLocaleString()} points earns ${nextBowlGoal.name}.` : 'Every medal is yours. Go beat your best.'}</p>
            <div className="flex flex-col items-center gap-2 mb-4">
              <div className="text-6xl font-black text-sky-500">{(result?.trickScore ?? 0).toLocaleString()}</div>
              <div className="text-rose-400 font-medium text-lg">Bowl Points</div>
            </div>
            {!isNewHighScore && save.bowlBest > 0 && (
              <p className="text-rose-400 text-sm mb-4">Bowl best: {save.bowlBest.toLocaleString()}</p>
            )}
          </>) : (<>
            <h2 className="text-3xl font-bold text-rose-600 mb-2">{result?.completed ? 'Finish line, sunshine!' : 'A little tumble'}</h2>
            <p className="text-rose-500 mb-4">{result?.completed ? 'You made it through the sunset course.' : 'Your collected Pokeballs still count. Take another lap.'}</p>
            <div className="flex flex-col items-center gap-2 mb-4">
              <div className="text-6xl font-black text-rose-500">{result?.collected ?? 0}</div>
              <div className="text-rose-400 font-medium text-lg">Pokeballs Collected</div>
            </div>
            {save.highScore > 0 && !isNewHighScore && (
              <p className="text-rose-400 text-sm mb-4">Best haul: {save.highScore}</p>
            )}
            <p className="text-sm text-violet-600 mb-4">{result?.trickScore ?? 0} trick points · {Math.round(result?.distance ?? 0)} m skated</p>
          </>)}
          <p className="text-xs text-rose-400 mb-4">{saveError ? 'Progress is kept for this visit only.' : 'Your memories and outfit progress are saved.'}</p>

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

          {newlyUnlockedCount === 0 && !isBowlResult && save.unlockedMemoryIds.length < MEMORY_POOL.length && (
            <div className="mt-4 p-4 bg-rose-50 rounded-xl border-2 border-rose-200 text-rose-800">
              <p className="text-sm">Keep collecting to unlock more memories!</p>
              <p className="text-xs text-rose-400 mt-1">{5 - save.totalCollected % 5} more Pokeballs to your next memory</p>
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
