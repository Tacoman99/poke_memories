import { Memory } from './types';

// =====================================================
// YOUR MEMORY POOL - Add your photos and captions here!
// =====================================================
// Each memory maps to a folder in public/memories/.
// The game randomly unlocks these as she collects Pokeballs.
// She gets 1 new memory per 5 total Pokeballs collected (across all sessions).

export const MEMORY_POOL: Memory[] = [

  {
    id: 'ily-night',
    media: [
      { type: 'image', url: '/memories/ILY_night/F49E23FB-A91D-48BD-9C26-CF3674E4FEB6.jpg' },
      { type: 'image', url: '/memories/ILY_night/IMG_3974.jpg' },
      { type: 'video', url: '/memories/ILY_night/IMG_4001.MOV' },
      { type: 'video', url: '/memories/ILY_night/IMG_4002.MOV' },
    ],
    caption: 'The night you said you loved me',
  },

  {
    id: 'bday-surprise',
    media: [
      { type: 'image', url: '/memories/bday_suprise/IMG_6430.jpg' },
      { type: 'video', url: '/memories/bday_suprise/IMG_5131.MOV' },
      { type: 'video', url: '/memories/bday_suprise/IMG_6251.MOV' },
      { type: 'video', url: '/memories/bday_suprise/IMG_6254.MOV' },
    ],
    caption: 'Birthday Surprise',
  },

  {
    id: 'blue-monday',
    media: [
      { type: 'image', url: '/memories/blue_monday/IMG_4664.jpg' },
      { type: 'image', url: '/memories/blue_monday/IMG_4669.jpg' },
      { type: 'image', url: '/memories/blue_monday/IMG_4781.jpg' },
      { type: 'video', url: '/memories/blue_monday/IMG_5857.MOV' },
    ],
    caption: 'Blue Monday',
  },

  {
    id: 'bowling',
    media: [
      { type: 'image', url: '/memories/bowling/IMG_1023.jpg' },
      { type: 'video', url: '/memories/bowling/IMG_1018.MOV' },
    ],
    caption: 'When I got fried at bowling',
  },

  {
    id: 'drunk-beach',
    media: [
      { type: 'image', url: '/memories/drunk_beach/IMG_9905.jpg' },
      { type: 'video', url: '/memories/drunk_beach/IMG_9911.MOV' },
    ],
    caption: 'When we got drunk at the beach',
  },

  {
    id: 'emo-nite',
    media: [
      { type: 'image', url: '/memories/emo_nite/BA9302AF-BBF1-4295-94A0-C6FC55AF7B0C.jpg' },
      { type: 'image', url: '/memories/emo_nite/D02067F0-FF96-4811-AEB5-6B606E6810AD.jpg' },
      { type: 'video', url: '/memories/emo_nite/IMG_0390.MOV' },
      { type: 'video', url: '/memories/emo_nite/IMG_0394.MOV' },
    ],
    caption: 'Emo Nite',
  },

  {
    id: 'linkedin',
    media: [
      { type: 'image', url: '/memories/linkedin/IMG_5566.jpg' },
      { type: 'image', url: '/memories/linkedin/IMG_5607.jpg' },
      { type: 'image', url: '/memories/linkedin/IMG_5621.jpg' },
      { type: 'image', url: '/memories/linkedin/IMG_6116.jpg' },
      { type: 'video', url: '/memories/linkedin/IMG_5629.MOV' },
      { type: 'video', url: '/memories/linkedin/IMG_6117.MOV' },
    ],
    caption: "Celebrating my new big boy job",
  },

  {
    id: 'ls-dunes',
    media: [
      { type: 'image', url: '/memories/ls_dunes/0041A42A-63E2-4F30-A599-6B43E3DF3A29.jpg' },
      { type: 'image', url: '/memories/ls_dunes/1652854318035668305.jpg' },
      { type: 'image', url: '/memories/ls_dunes/6809057599094683274.jpg' },
      { type: 'image', url: '/memories/ls_dunes/B5400078-5B7F-4C5D-B34D-5FD987B86F45.jpg' },
      { type: 'image', url: '/memories/ls_dunes/CE9D4564-3A9B-4EF6-A3EC-5CC25028246D.jpg' },
      { type: 'video', url: '/memories/ls_dunes/IMG_2030.MOV' },
    ],
    caption: 'L.S. Dunes',
  },

  {
    id: 'official-gf',
    media: [
      { type: 'image', url: '/memories/official_gf/73D33035-8B3B-4284-8149-4ACBA18CCE94.jpg' },
      { type: 'video', url: '/memories/official_gf/IMG_3330.MOV' },
    ],
    caption: 'When you became my official GF',
  },

  {
    id: 'pokemon-rips',
    media: [
      { type: 'video', url: '/memories/pokemon_rips/37F425C1-3820-4838-8561-DC29628733DF.mov' },
      { type: 'video', url: '/memories/pokemon_rips/7914871B-C17B-4B5A-B6D5-A0A05503C0B4.mov' },
      { type: 'video', url: '/memories/pokemon_rips/9C591BE9-3325-4AA4-9A7A-466B7F106494.mov' },
      { type: 'video', url: '/memories/pokemon_rips/IMG_5445.MOV' },
      { type: 'video', url: '/memories/pokemon_rips/IMG_8442.MOV' },
    ],
    caption: 'Pokemon Rips',
  },

  {
    id: 'ptv',
    media: [
      { type: 'image', url: '/memories/ptv/IMG_3731.jpg' },
      { type: 'video', url: '/memories/ptv/7083324670808014885.mp4' },
      { type: 'video', url: '/memories/ptv/IMG_3752.mov' },
      { type: 'video', url: '/memories/ptv/IMG_3777.mov' },
    ],
    caption: 'PTV',
  },

  {
    id: 'random-drunk-night',
    media: [
      { type: 'image', url: '/memories/random_drunk_night/07C39C6A-4509-4038-8079-46DD21217810.jpg' },
      { type: 'image', url: '/memories/random_drunk_night/774580D2-ED98-47FD-8CD6-F4953F226F0B.jpg' },
      { type: 'image', url: '/memories/random_drunk_night/B8B4D453-F977-413B-8992-C32AB14FFEE2.jpg' },
    ],
    caption: 'Random Drunk Night lol',
  },

  {
    id: 'surprise-petco-park',
    media: [
      { type: 'image', url: '/memories/surpise_petco_park/7680052C-5974-48A3-829A-D7F426FDBD27.jpg' },
      { type: 'image', url: '/memories/surpise_petco_park/IMG_0741.jpg' },
      { type: 'video', url: '/memories/surpise_petco_park/3a90298ab61b4c26988037d3307cdc81.mov' },
      { type: 'video', url: '/memories/surpise_petco_park/88186259c241497a873a63e9dc4c548f.mov' },
    ],
    caption: 'Surprise Petco Park',
  },


];
