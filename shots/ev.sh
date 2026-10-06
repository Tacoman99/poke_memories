#!/bin/bash
cd /c/Users/jorge/poke_memories-wp4/unity
unity command eval "var f=UnityEngine.Object.FindAnyObjectByType<PokeMemories.Menu.MenuFlow>(); $1" 2>&1 | tail -3
