
export type MediaType = 'image' | 'video';

export interface MediaItem {
    type: MediaType;
    url: string;
    thumbnail?: string;
  }

export interface Memory {
    id: string;
    media: MediaItem[];
    caption: string;
    date?: string;
  }

  export type GameState = 'START' | 'PLAYING' | 'GAMEOVER' | 'MEMORIES';

  export type GameMode = 'COURSE' | 'ENDLESS' | 'BOWL';

  export interface GameResult {
    mode: GameMode;
    collected: number;
    trickScore: number;
    distance: number;
    completed: boolean;
  }

  export type ItemType = 'ball' | 'obstacle' | 'rail' | 'greatball' | 'ultraball' | 'masterball';

  export interface GameObject {
    x: number;
    y: number;
    width: number;
    height: number;
    type: 'ball' | 'obstacle' | 'player';
  }

  export type CompanionType = 'pikachu' | 'gardevoir';

  export interface Outfit {
    name: string;
    bodyColor: string;
    hairColor: string;
    hairHighlight: string;
    skateColor: string;
    trimColor: string;
    unlockScore: number;
    companion: CompanionType;
  }

  export interface SaveData {
    highScore: number;
    totalCollected: number;
    unlockedMemoryIds: string[];
    unlockedOutfits: string[];
    selectedOutfit: string;
    courseCompleted: boolean;
    bowlBest: number;
    bowlTier: number;
  }
