#!/bin/bash
# usage: turn.sh <name> <time-fraction> [index]  -- freeze a page turn at a point and capture
cd /c/Users/jorge/poke_memories-wp4/shots
./ev.sh 'f.OpenBook(0); PokeMemories.Menu.MemoryBook.AnimationSpeed=0f;' >/dev/null
./ev.sh 'var b=f.GetComponent<PokeMemories.Menu.MemoryBook>(); var T=typeof(PokeMemories.Menu.MemoryBook); var fl=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance; T.GetField("index",fl).SetValue(b,'${3:-3}'); T.GetMethod("Next",fl).Invoke(b,null); T.GetField("turnTime",fl).SetValue(b,(float)'$2'*0.55f);' >/dev/null
sleep 1; ./shot.sh after $1
