using System;
using System.Collections.Generic;
using UnityEngine;

namespace PokeMemories.Menu
{
    [Serializable]
    public class MediaItem
    {
        public string type;
        public string url;
        /// <summary>Extra clockwise rotation in degrees for photos stored sideways.</summary>
        public int rotate;
        public bool IsVideo => type == "video";
    }

    [Serializable]
    public class Memory
    {
        public string id;
        public string caption;
        public string date;
        public MediaItem[] media;
    }

    [Serializable]
    class MemoryList { public Memory[] memories; }

    /// <summary>
    /// The memory pool, ported from the web game's memories.ts. Loaded from
    /// Resources/memories.json, which tools/memories/export.mjs regenerates from memories.ts.
    /// </summary>
    public static class MemoryPool
    {
        static List<Memory> all;

        public static IReadOnlyList<Memory> All => all ??= Load();

        public static Memory Find(string id) => ((List<Memory>)All).Find(m => m.id == id);

        static List<Memory> Load()
        {
            var asset = Resources.Load<TextAsset>("memories");
            if (asset == null)
            {
                Debug.LogError("Resources/memories.json is missing; run node tools/memories/export.mjs.");
                return new List<Memory>();
            }
            var list = JsonUtility.FromJson<MemoryList>(asset.text);
            return new List<Memory>(list?.memories ?? Array.Empty<Memory>());
        }
    }
}
