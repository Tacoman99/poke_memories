#!/bin/bash
# usage: touch.sh <id> <phase: Began|Moved|Ended> <guiX> <guiY>   injects a touch (GUI coords, origin top-left)
cd /c/Users/jorge/poke_memories-bowl/unity
unity command eval '
var ts = UnityEngine.InputSystem.Touchscreen.current ?? UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Touchscreen>();
var st = new UnityEngine.InputSystem.LowLevel.TouchState { touchId = '$1', phase = UnityEngine.InputSystem.TouchPhase.'$2', position = new UnityEngine.Vector2('$3', UnityEngine.Screen.height - '$4'), pressure = 1f };
UnityEngine.InputSystem.InputSystem.QueueStateEvent(ts, st);
return "ok";' 2>&1 | tail -1 | cut -c1-120
