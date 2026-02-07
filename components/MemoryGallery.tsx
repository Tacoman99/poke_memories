
import { useState, useEffect, useCallback, useRef } from 'react';
import { ArrowLeft, Heart, Lock, X, Play } from 'lucide-react';
import { Memory, MediaItem } from '../types';
import { MEMORY_POOL } from '../memories';

interface MemoryGalleryProps {
  unlockedIds: string[];
  onBack: () => void;
}

const TAPE_COLORS = ['#f59e0b', '#ec4899', '#3b82f6', '#34d399', '#a78bfa'];

function getRotation(index: number): number {
  return (index * 7 + 3) % 9 - 4;
}

function getSmallRotation(index: number): number {
  return ((index * 5 + 2) % 5 - 2);
}

/* ─── Video Thumbnail ─── */
const VideoPreview: React.FC<{
  src: string;
  thumbnail?: string;
  alt: string;
  className?: string;
}> = ({ src, thumbnail, alt, className = '' }) => {
  const [thumbFailed, setThumbFailed] = useState(false);
  const videoRef = useRef<HTMLVideoElement>(null);

  // If thumbnail image is provided and hasn't errored, try it first
  if (thumbnail && !thumbFailed) {
    return (
      <img
        src={thumbnail}
        alt={alt}
        className={className}
        onError={() => setThumbFailed(true)}
      />
    );
  }

  // Fall back to video element that seeks to 0.5s for a preview frame
  return (
    <video
      ref={videoRef}
      muted
      playsInline
      preload="auto"
      className={className}
      onLoadedData={() => {
        if (videoRef.current) {
          videoRef.current.currentTime = 0.5;
        }
      }}
    >
      <source src={src} />
    </video>
  );
};

/* ─── Pokeball Open Animation Overlay ─── */
const PokeballOpen: React.FC<{ onComplete: () => void }> = ({ onComplete }) => {
  useEffect(() => {
    const timer = setTimeout(onComplete, 900);
    return () => clearTimeout(timer);
  }, [onComplete]);

  return (
    <div className="fixed inset-0 z-[70] flex items-center justify-center pointer-events-none">
      <div
        className="absolute w-32 h-32 rounded-full border-4 border-rose-300"
        style={{ animation: 'pokeballSpark 0.8s ease-out forwards' }}
      />
      <div
        className="absolute"
        style={{ animation: 'pokeballTopOpen 0.7s ease-in forwards' }}
      >
        <svg width="120" height="60" viewBox="0 0 120 60">
          <path d="M10,60 A50,50 0 0,1 110,60" fill="#e11d48" stroke="#881337" strokeWidth="3" />
          <rect x="0" y="56" width="120" height="4" fill="#881337" />
          <circle cx="60" cy="60" r="14" fill="white" stroke="#881337" strokeWidth="3" />
          <circle cx="60" cy="60" r="6" fill="#e11d48" stroke="#881337" strokeWidth="2" />
        </svg>
      </div>
      <div
        className="absolute"
        style={{ animation: 'pokeballBottomOpen 0.7s ease-in forwards' }}
      >
        <svg width="120" height="60" viewBox="0 0 120 60">
          <rect x="0" y="0" width="120" height="4" fill="#881337" />
          <path d="M10,0 A50,50 0 0,0 110,0" fill="white" stroke="#881337" strokeWidth="3" />
          <circle cx="60" cy="0" r="14" fill="white" stroke="#881337" strokeWidth="3" />
          <circle cx="60" cy="0" r="6" fill="#fda4af" stroke="#881337" strokeWidth="2" />
        </svg>
      </div>
      <div
        className="absolute w-40 h-40 rounded-full"
        style={{
          background: 'radial-gradient(circle, rgba(251,113,133,0.5) 0%, rgba(251,113,133,0) 70%)',
          animation: 'pokeballGlow 0.8s ease-out forwards',
        }}
      />
    </div>
  );
};

/* ─── Media Thumbnail (in the memory detail grid) ─── */
const MediaThumb: React.FC<{
  item: MediaItem;
  index: number;
  caption: string;
  onClick: () => void;
}> = ({ item, index, caption, onClick }) => {
  const rotation = getSmallRotation(index);

  return (
    <button
      onClick={onClick}
      className="relative overflow-hidden bg-white p-1.5 shadow-md hover:shadow-xl transition-all duration-300 cursor-pointer group"
      style={{
        animation: `fadeInUp 0.4s ease-out ${index * 0.1}s both`,
        borderRadius: '4px',
        transform: `rotate(${rotation}deg)`,
      }}
      onMouseEnter={e => {
        e.currentTarget.style.transform = 'rotate(0deg) translateY(-4px)';
      }}
      onMouseLeave={e => {
        e.currentTarget.style.transform = `rotate(${rotation}deg)`;
      }}
    >
      <div className="relative aspect-square overflow-hidden rounded-sm">
        {item.type === 'video' ? (
          <>
            <VideoPreview
              src={item.url}
              thumbnail={item.thumbnail}
              alt={caption}
              className="w-full h-full object-cover"
            />
            <div className="absolute inset-0 flex items-center justify-center bg-black/15 group-hover:bg-black/25 transition-colors">
              <div className="w-11 h-11 rounded-full bg-white/90 flex items-center justify-center shadow-lg group-hover:scale-110 transition-transform">
                <Play size={18} className="text-rose-500 ml-0.5 fill-current" />
              </div>
            </div>
          </>
        ) : (
          <img
            src={item.url}
            alt={caption}
            className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
          />
        )}
      </div>
    </button>
  );
};

/* ─── Full-screen Media Viewer (single item, polaroid-style) ─── */
const MediaViewer: React.FC<{
  item: MediaItem;
  caption: string;
  onClose: () => void;
}> = ({ item, caption, onClose }) => {
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-[80] flex items-center justify-center p-4"
      style={{
        backgroundColor: 'rgba(0,0,0,0.6)',
        backdropFilter: 'blur(12px)',
        WebkitBackdropFilter: 'blur(12px)',
        animation: 'backdropFadeIn 0.25s ease-out',
      }}
      onClick={onClose}
    >
      <button
        onClick={onClose}
        className="absolute top-4 right-4 z-10 w-10 h-10 rounded-full bg-white/90 flex items-center justify-center shadow-lg hover:bg-white transition-colors"
      >
        <X size={20} className="text-rose-500" />
      </button>

      <div
        onClick={e => e.stopPropagation()}
        className="bg-white p-4 pb-8 rounded-sm shadow-2xl max-w-3xl w-full"
        style={{ animation: 'contentReveal 0.35s ease-out both' }}
      >
        <div className="relative overflow-hidden rounded-sm" style={{ backgroundColor: '#fdf8f3' }}>
          {item.type === 'video' ? (
            <video
              key={item.url}
              controls
              autoPlay
              playsInline
              className="w-full max-h-[70vh] object-contain"
            >
              <source src={item.url} />
            </video>
          ) : (
            <img
              src={item.url}
              alt={caption}
              className="w-full max-h-[70vh] object-contain"
            />
          )}
        </div>

        <p className="font-handwriting text-xl text-rose-600 text-center mt-4 tracking-wide">
          {caption}
        </p>
      </div>
    </div>
  );
};

/* ─── Memory Detail (all media shown at once — scrapbook page) ─── */
const MemoryDetail: React.FC<{
  memory: Memory;
  onClose: () => void;
}> = ({ memory, onClose }) => {
  const [viewingItem, setViewingItem] = useState<MediaItem | null>(null);
  const tapeColor = TAPE_COLORS[parseInt(memory.id.replace(/\D/g, '') || '0') % TAPE_COLORS.length];

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !viewingItem) onClose();
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onClose, viewingItem]);

  return (
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center p-4 md:p-8"
      style={{
        backgroundColor: 'rgba(0,0,0,0.75)',
        animation: 'backdropFadeIn 0.3s ease-out',
      }}
      onClick={onClose}
    >
      <div
        className="relative max-w-3xl w-full max-h-[90vh] flex flex-col overflow-hidden"
        style={{
          animation: 'contentReveal 0.4s ease-out 0.45s both',
          borderRadius: '16px',
          background: '#fdf8f3',
          boxShadow: '0 0 0 2px #fecdd3, 0 0 30px rgba(244,63,94,0.12), 0 25px 60px rgba(0,0,0,0.25), inset 0 2px 8px rgba(0,0,0,0.04)',
        }}
        onClick={e => e.stopPropagation()}
      >
        {/* Washi tape across top */}
        <div
          className="absolute -top-2 left-1/2 -translate-x-1/2 h-7 z-20 rounded-sm"
          style={{
            width: '80px',
            backgroundColor: tapeColor,
            opacity: 0.55,
            transform: 'translateX(-50%) rotate(-1.5deg)',
          }}
        />

        {/* Close */}
        <button
          onClick={onClose}
          className="absolute top-3 right-3 z-20 w-9 h-9 rounded-full bg-white/80 flex items-center justify-center shadow-md hover:bg-white hover:shadow-lg transition-all"
        >
          <X size={18} className="text-rose-400" />
        </button>

        {/* Caption header */}
        <div className="px-7 pt-8 pb-5 text-center">
          <div className="flex items-center justify-center gap-2 mb-2">
            <Heart size={16} className="text-rose-300 fill-rose-300" style={{ animation: 'gentleSway 3s ease-in-out infinite', ['--rotation' as string]: '-5deg' }} />
            <p className="font-handwriting text-3xl md:text-4xl text-rose-700" style={{ letterSpacing: '0.5px' }}>
              {memory.caption}
            </p>
            <Heart size={12} className="text-rose-200 fill-rose-200 mt-2" />
          </div>
          {memory.date && (
            <span
              className="inline-block font-handwriting text-sm text-rose-500 px-4 py-1 rounded-full mt-1"
              style={{ backgroundColor: 'rgba(251,113,133,0.12)' }}
            >
              {memory.date}
            </span>
          )}
          {/* Gradient divider */}
          <div
            className="mt-5 h-px mx-auto"
            style={{
              maxWidth: '280px',
              background: 'linear-gradient(to right, transparent, #f9a8d4, transparent)',
            }}
          />
        </div>

        {/* Media grid */}
        <div className="flex-1 overflow-y-auto px-6 pb-2">
          <div className={`grid gap-4 ${
            memory.media.length === 1
              ? 'grid-cols-1 max-w-md mx-auto'
              : memory.media.length === 2
                ? 'grid-cols-2'
                : 'grid-cols-2 md:grid-cols-3'
          }`}>
            {memory.media.map((item, i) => (
              <MediaThumb
                key={i}
                item={item}
                index={i}
                caption={memory.caption}
                onClick={() => setViewingItem(item)}
              />
            ))}
          </div>
        </div>

        {/* Footer */}
        <div className="px-6 py-4 text-center">
          <div className="flex items-center justify-center gap-2">
            <span className="text-rose-200">&#183;</span>
            <Heart size={8} className="text-rose-200 fill-rose-200" />
            <span className="text-rose-200">&#183;</span>
          </div>
          <p className="font-handwriting text-sm text-rose-300 mt-1.5">
            Tap to view closer
          </p>
        </div>
      </div>

      {viewingItem && (
        <MediaViewer
          item={viewingItem}
          caption={memory.caption}
          onClose={() => setViewingItem(null)}
        />
      )}
    </div>
  );
};

/* ─── Polaroid Card (gallery card) ─── */
const PolaroidCard: React.FC<{
  memory: Memory;
  index: number;
  onOpen: (memory: Memory) => void;
}> = ({ memory, index, onOpen }) => {
  const rotation = getRotation(index);
  const tapeColor = TAPE_COLORS[index % TAPE_COLORS.length];
  const firstItem = memory.media[0];

  return (
    <div
      className="break-inside-avoid mb-6 group"
      style={{
        animation: `fadeInUp 0.5s ease-out ${index * 0.07}s both`,
      }}
    >
      <div
        className="relative bg-white p-3 pb-14 shadow-md hover:shadow-xl transition-all duration-300 cursor-pointer"
        style={{
          transform: `rotate(${rotation}deg)`,
          ['--rotation' as string]: `${rotation}deg`,
          animation: `gentleSway 4s ease-in-out ${index * 0.3}s infinite`,
        }}
        onClick={() => onOpen(memory)}
        onMouseEnter={e => {
          e.currentTarget.style.transform = 'rotate(0deg) scale(1.05)';
          e.currentTarget.style.animation = 'none';
        }}
        onMouseLeave={e => {
          e.currentTarget.style.transform = `rotate(${rotation}deg)`;
          e.currentTarget.style.animation = `gentleSway 4s ease-in-out ${index * 0.3}s infinite`;
        }}
      >
        {/* Washi tape */}
        <div
          className="absolute -top-3 left-1/2 -translate-x-1/2 h-6 rounded-sm z-10"
          style={{
            width: '64px',
            backgroundColor: tapeColor,
            opacity: 0.6,
            transform: `translateX(-50%) rotate(${(index % 3 - 1) * 2}deg)`,
          }}
        />

        {/* Preview (first media item) */}
        <div className="relative aspect-[4/5] overflow-hidden bg-rose-50">
          {firstItem.type === 'video' ? (
            <div className="relative w-full h-full">
              <VideoPreview
                src={firstItem.url}
                thumbnail={firstItem.thumbnail}
                alt={memory.caption}
                className="w-full h-full object-cover"
              />
              <div className="absolute inset-0 flex items-center justify-center bg-black/20">
                <div className="w-14 h-14 rounded-full bg-white/90 flex items-center justify-center shadow-lg">
                  <Play size={24} className="text-rose-500 ml-1 fill-current" />
                </div>
              </div>
            </div>
          ) : (
            <img
              src={firstItem.url}
              alt={memory.caption}
              className="w-full h-full object-cover"
            />
          )}

          {/* Media count badge */}
          {memory.media.length > 1 && (
            <div className="absolute top-2 right-2 bg-black/50 text-white text-xs font-bold px-2 py-1 rounded-full">
              +{memory.media.length}
            </div>
          )}
        </div>

        {/* Caption area */}
        <div className="absolute bottom-0 left-0 right-0 px-4 pb-3 pt-1">
          <p className="font-handwriting text-lg text-rose-700 leading-tight truncate">
            {memory.caption}
          </p>
          {memory.date && (
            <p className="font-handwriting text-sm text-rose-400 mt-0.5">{memory.date}</p>
          )}
        </div>
      </div>
    </div>
  );
};

/* ─── Locked Slot ─── */
const LockedSlot: React.FC<{ index: number }> = ({ index }) => {
  const rotation = getRotation(index);

  return (
    <div
      className="break-inside-avoid mb-6"
      style={{
        animation: `fadeInUp 0.5s ease-out ${index * 0.07}s both`,
      }}
    >
      <div
        className="relative bg-white/40 p-3 pb-14 border-2 border-dashed border-rose-200 opacity-50"
        style={{ transform: `rotate(${rotation}deg)` }}
      >
        <div
          className="absolute -top-3 left-1/2 -translate-x-1/2 h-6 rounded-sm z-10"
          style={{
            width: '64px',
            backgroundColor: TAPE_COLORS[index % TAPE_COLORS.length],
            opacity: 0.25,
            transform: `translateX(-50%) rotate(${(index % 3 - 1) * 2}deg)`,
          }}
        />

        <div className="aspect-[4/5] bg-rose-100/50 flex flex-col items-center justify-center gap-3">
          <Lock size={32} className="text-rose-300" />
          <span className="font-handwriting text-rose-300 text-sm">Keep playing!</span>
        </div>

        <div className="absolute bottom-0 left-0 right-0 px-4 pb-3 pt-1">
          <p className="font-handwriting text-lg text-rose-300">???</p>
        </div>
      </div>
    </div>
  );
};

/* ─── Decorative Washi Strips ─── */
const WashiStrips: React.FC = () => (
  <>
    <div
      className="absolute top-16 -left-4 w-24 h-5 rounded-sm pointer-events-none"
      style={{ backgroundColor: '#f59e0b', opacity: 0.15, transform: 'rotate(-25deg)' }}
    />
    <div
      className="absolute top-32 -right-2 w-20 h-5 rounded-sm pointer-events-none"
      style={{ backgroundColor: '#a78bfa', opacity: 0.12, transform: 'rotate(15deg)' }}
    />
    <div
      className="absolute top-1/3 -left-6 w-28 h-5 rounded-sm pointer-events-none"
      style={{ backgroundColor: '#ec4899', opacity: 0.1, transform: 'rotate(-10deg)' }}
    />
    <div
      className="absolute top-1/2 -right-4 w-20 h-5 rounded-sm pointer-events-none"
      style={{ backgroundColor: '#34d399', opacity: 0.12, transform: 'rotate(20deg)' }}
    />
    <div
      className="absolute bottom-40 -left-3 w-16 h-5 rounded-sm pointer-events-none"
      style={{ backgroundColor: '#3b82f6', opacity: 0.1, transform: 'rotate(-18deg)' }}
    />
  </>
);

/* ─── Doodle decorations ─── */
const Doodles: React.FC = () => (
  <>
    <Heart
      size={14}
      className="absolute text-rose-200 pointer-events-none"
      style={{ top: '22%', left: '48%', opacity: 0.25, transform: 'rotate(12deg)' }}
    />
    <Heart
      size={10}
      className="absolute text-rose-200 pointer-events-none"
      style={{ top: '45%', right: '8%', opacity: 0.2, transform: 'rotate(-20deg)' }}
    />
    <span
      className="absolute text-rose-200 pointer-events-none text-xs"
      style={{ top: '60%', left: '5%', opacity: 0.2, transform: 'rotate(8deg)' }}
    >
      &#9733;
    </span>
    <Heart
      size={12}
      className="absolute text-rose-200 pointer-events-none"
      style={{ top: '75%', left: '52%', opacity: 0.2, transform: 'rotate(25deg)' }}
    />
    <span
      className="absolute text-rose-200 pointer-events-none text-xs"
      style={{ top: '35%', left: '3%', opacity: 0.18, transform: 'rotate(-15deg)' }}
    >
      &#9733;
    </span>
  </>
);

/* ═══════════════════════════════════════════
   Main Component
   ═══════════════════════════════════════════ */
const MemoryGallery: React.FC<MemoryGalleryProps> = ({ unlockedIds, onBack }) => {
  const [openMemory, setOpenMemory] = useState<Memory | null>(null);
  const [showPokeballAnim, setShowPokeballAnim] = useState(false);
  const pendingMemory = useRef<Memory | null>(null);

  const unlockedSet = new Set(unlockedIds);
  const unlockedMemories = MEMORY_POOL.filter(m => unlockedSet.has(m.id));
  const lockedCount = MEMORY_POOL.length - unlockedMemories.length;

  const handleOpenMemory = useCallback((memory: Memory) => {
    pendingMemory.current = memory;
    setShowPokeballAnim(true);
  }, []);

  const handlePokeballDone = useCallback(() => {
    setShowPokeballAnim(false);
    setOpenMemory(pendingMemory.current);
    pendingMemory.current = null;
  }, []);

  return (
    <div className="fixed inset-0 z-50 scrapbook-bg overflow-y-auto">
      <div className="absolute left-0 top-0 bottom-0 w-1 bg-rose-300/60" />
      <div className="absolute left-1 top-0 bottom-0 w-px bg-rose-200/40" />

      <div className="relative max-w-4xl mx-auto px-6 py-8 md:px-10">
        <WashiStrips />
        <Doodles />

        <div className="flex items-center justify-between mb-10">
          <button
            onClick={onBack}
            className="p-3 bg-white rounded-full text-rose-500 shadow-md hover:bg-rose-50 hover:shadow-lg transition-all"
          >
            <ArrowLeft size={22} />
          </button>
          <h2 className="font-handwriting text-4xl md:text-5xl text-rose-600 flex items-center gap-3">
            <Heart className="fill-rose-400 text-rose-400" size={28} />
            My Memory Book
          </h2>
          <div className="w-12" />
        </div>

        {unlockedMemories.length === 0 && lockedCount === 0 && (
          <div className="text-center py-20 bg-white/40 rounded-xl border-2 border-dashed border-rose-200">
            <Heart className="w-16 h-16 mx-auto text-rose-200 mb-4" />
            <p className="font-handwriting text-2xl text-rose-400">
              No memories yet. Go collect some Pokeballs!
            </p>
          </div>
        )}

        <div className="columns-2 md:columns-3 gap-5">
          {unlockedMemories.map((memory, i) => (
            <PolaroidCard
              key={memory.id}
              memory={memory}
              index={i}
              onOpen={handleOpenMemory}
            />
          ))}
          {Array.from({ length: lockedCount }).map((_, idx) => (
            <LockedSlot key={`locked-${idx}`} index={unlockedMemories.length + idx} />
          ))}
        </div>

        {(unlockedMemories.length > 0 || lockedCount > 0) && (
          <p className="font-handwriting text-center text-rose-400 text-xl mt-10 mb-4">
            {unlockedMemories.length} of {MEMORY_POOL.length} memories unlocked
          </p>
        )}
      </div>

      {showPokeballAnim && <PokeballOpen onComplete={handlePokeballDone} />}

      {openMemory && (
        <MemoryDetail
          memory={openMemory}
          onClose={() => setOpenMemory(null)}
        />
      )}
    </div>
  );
};

export default MemoryGallery;
