#!/bin/bash
cd /c/Users/jorge/poke_memories-bowl/unity
unity command eval '
var b=UnityEngine.Object.FindFirstObjectByType<PokeMemories.Gameplay.BowlGame>(); var fl=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var T=typeof(PokeMemories.Gameplay.BowlGame); var s=(PokeMemories.Gameplay.BowlSimulation)T.GetField("sim",fl).GetValue(b);
var started=(bool)T.GetField("started",fl).GetValue(b);
var tb=(UnityEngine.Rect[])T.GetField("trickButtons",fl).GetValue(b); var lip=(UnityEngine.Rect)T.GetField("lipButton",fl).GetValue(b); var menu=(UnityEngine.Rect)T.GetField("menuButton",fl).GetValue(b);
return $"started={started} pump={s.Pumping} air={s.Airborne} trick={s.Trick} lip={s.LipArmed} s={s.S:F0} v={s.V:F0} t={s.TimeLeft:F1} g0={tb[0].center} lip={lip.center} menu={menu.center}";' 2>&1 | tail -1 | grep -o '"result":"[^"]*"'
