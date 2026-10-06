#!/bin/bash
# usage: ev.sh '<c# code>'   runs C# in the bowl worktree's Editor
cd /c/Users/jorge/poke_memories-bowl/unity
unity command eval "$1" 2>&1 | tail -1 | cut -c1-400
