
import { useState } from 'react';
import { ArrowLeft, Heart, Lock, X } from 'lucide-react';
import { Memory } from '../types';
import { MEMORY_POOL } from '../memories';

interface MemoryGalleryProps {
  unlockedIds: string[];
  onBack: () => void;
}

const MemoryGallery: React.FC<MemoryGalleryProps> = ({
  unlockedIds,
  onBack
}) => {
  const [lightbox, setLightbox] = useState<Memory | null>(null);

  const unlockedSet = new Set(unlockedIds);
  const unlockedMemories = MEMORY_POOL.filter(m => unlockedSet.has(m.id));
  const lockedCount = MEMORY_POOL.length - unlockedMemories.length;

  return (
    <div className="fixed inset-0 z-50 bg-rose-50 overflow-y-auto p-4 md:p-8 flex flex-col items-center">
      <div className="max-w-4xl w-full">
        <div className="flex items-center justify-between mb-8">
          <button
            onClick={onBack}
            className="p-3 bg-white rounded-full text-rose-500 shadow-md hover:bg-rose-100 transition-colors"
          >
            <ArrowLeft />
          </button>
          <h2 className="text-3xl font-bold text-rose-600 flex items-center gap-2">
            <Heart className="fill-current" /> My Memories
          </h2>
          <div className="w-12" />
        </div>

        {unlockedMemories.length === 0 && lockedCount === 0 && (
          <div className="text-center py-20 bg-white/50 rounded-3xl border-2 border-dashed border-rose-200">
            <Heart className="w-16 h-16 mx-auto text-rose-200 mb-4" />
            <p className="text-xl text-rose-400">No memories yet. Go collect some Pokeballs!</p>
          </div>
        )}

        <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
          {/* Unlocked memories */}
          {unlockedMemories.map((memory) => (
            <button
              key={memory.id}
              onClick={() => setLightbox(memory)}
              className="bg-white rounded-2xl shadow-lg overflow-hidden border border-rose-100 hover:scale-[1.03] transition-transform duration-300 text-left"
            >
              <div className="relative aspect-square overflow-hidden">
                <img
                  src={memory.imageUrl}
                  alt={memory.caption}
                  className="w-full h-full object-cover"
                />
              </div>
              <div className="p-3">
                <p className="text-xs text-rose-600 font-medium italic">{memory.caption}</p>
              </div>
            </button>
          ))}

          {/* Locked slots */}
          {Array.from({ length: lockedCount }).map((_, idx) => (
            <div
              key={`locked-${idx}`}
              className="bg-white/50 rounded-2xl shadow-md overflow-hidden border-2 border-dashed border-rose-200 aspect-square flex flex-col items-center justify-center gap-2"
            >
              <Lock size={24} className="text-rose-200" />
              <p className="text-xs text-rose-300 font-medium">Keep playing!</p>
            </div>
          ))}
        </div>

        {unlockedMemories.length > 0 && (
          <p className="text-center text-rose-300 text-sm mt-8">
            {unlockedMemories.length} / {MEMORY_POOL.length} memories unlocked
          </p>
        )}
      </div>

      {/* Lightbox */}
      {lightbox && (
        <div
          className="fixed inset-0 z-[60] bg-black/80 flex items-center justify-center p-4"
          onClick={() => setLightbox(null)}
        >
          <div
            className="relative max-w-3xl w-full max-h-[90vh] bg-white rounded-3xl overflow-hidden shadow-2xl"
            onClick={(e) => e.stopPropagation()}
          >
            <button
              onClick={() => setLightbox(null)}
              className="absolute top-4 right-4 z-10 bg-white/90 text-rose-500 p-2 rounded-full shadow-lg hover:bg-white transition-colors"
            >
              <X size={20} />
            </button>
            <img
              src={lightbox.imageUrl}
              alt={lightbox.caption}
              className="w-full max-h-[75vh] object-contain bg-rose-50"
            />
            <div className="p-6 text-center">
              <p className="text-lg text-rose-600 font-semibold italic">{lightbox.caption}</p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default MemoryGallery;
