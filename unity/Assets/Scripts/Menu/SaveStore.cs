using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeMemories.Gameplay;
using PlayMode = PokeMemories.Gameplay.PlayMode;

namespace PokeMemories.Menu
{
    [Serializable]
    public class SaveData
    {
        public int highScore;
        public int totalCollected;
        public bool courseCompleted;
        public List<string> unlockedMemoryIds = new();
    }

    public readonly struct RunReward
    {
        public readonly int Earned;
        public readonly bool NewHighScore;
        public RunReward(int earned, bool newHighScore) { Earned = earned; NewHighScore = newHighScore; }
    }

    /// <summary>
    /// Progress saved in PlayerPrefs. Reward rules match progression.ts: one memory per
    /// five cumulative Pokeballs, plus one for the first course clear.
    /// </summary>
    public static class SaveStore
    {
        const string Key = "pokeMemories.save";
        public const int BallsPerMemory = 5;
        static SaveData data;

        public static SaveData Data => data ??= Load();
        public static int BallsToNextMemory => BallsPerMemory - Data.totalCollected % BallsPerMemory;
        public static bool AllUnlocked => Data.unlockedMemoryIds.Count >= MemoryPool.All.Count;

        /// <summary>Memories earned by a run, given the progress before it. Mirrors memoryRewards().</summary>
        public static int Earned(int totalCollected, bool courseCompleted, PlayMode mode, int collected, bool completed)
        {
            var firstClear = mode == PlayMode.Course && completed && !courseCompleted;
            var newTotal = totalCollected + collected;
            return newTotal / BallsPerMemory - totalCollected / BallsPerMemory + (firstClear ? 1 : 0);
        }

        public static RunReward RecordRun(PlayMode mode, int collected, bool completed)
        {
            var save = Data;
            var earned = Earned(save.totalCollected, save.courseCompleted, mode, collected, completed);
            var newBest = collected > save.highScore;

            save.highScore = Mathf.Max(save.highScore, collected);
            save.totalCollected += collected;
            save.courseCompleted |= mode == PlayMode.Course && completed;

            var locked = MemoryPool.All.Where(m => !save.unlockedMemoryIds.Contains(m.id)).ToList();
            var unlocked = 0;
            for (; unlocked < earned && locked.Count > 0; unlocked++)
            {
                var pick = UnityEngine.Random.Range(0, locked.Count);
                save.unlockedMemoryIds.Add(locked[pick].id);
                locked.RemoveAt(pick);
            }
            Save();
            return new RunReward(unlocked, newBest);
        }

        static SaveData Load()
        {
            try
            {
                var json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonUtility.FromJson<SaveData>(json);
                    if (loaded != null)
                    {
                        loaded.highScore = Mathf.Max(0, loaded.highScore);
                        loaded.totalCollected = Mathf.Max(0, loaded.totalCollected);
                        loaded.unlockedMemoryIds ??= new List<string>();
                        // Drop ids that left the pool and any duplicates.
                        var seen = new HashSet<string>();
                        loaded.unlockedMemoryIds.RemoveAll(id => MemoryPool.Find(id) == null || !seen.Add(id));
                        return loaded;
                    }
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Could not read saved progress: {error.Message}");
            }
            return new SaveData();
        }

        static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }
    }
}
