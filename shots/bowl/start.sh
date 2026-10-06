#!/bin/bash
# starts Play mode on Course.unity and drops into the bowl (autoplay optional: start.sh auto)
cd /c/Users/jorge/poke_memories-bowl/unity
unity command recompile >/dev/null 2>&1
unity command eval 'UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Course.unity"); return 1;' >/dev/null 2>&1
unity command editor_play >/dev/null 2>&1
sleep 5
unity command eval 'var f = UnityEngine.Object.FindFirstObjectByType<PokeMemories.Menu.MenuFlow>(); f.StartBowl(); var b=UnityEngine.Object.FindFirstObjectByType<PokeMemories.Gameplay.BowlGame>(); b.autoPlay = '"$([ "$1" = auto ] && echo true || echo false)"'; return f.Screen.ToString();' 2>&1 | tail -1 | cut -c1-200
