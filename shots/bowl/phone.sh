#!/bin/bash
# usage: phone.sh <w> <h>   sets the Game view to a fixed resolution (e.g. 390 844 portrait phone)
cd /c/Users/jorge/poke_memories-bowl/unity
unity command eval '
var asm = typeof(UnityEditor.Editor).Assembly;
var gvsT = asm.GetType("UnityEditor.GameViewSizes");
var single = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(gvsT);
var inst = single.GetProperty("instance").GetValue(null);
var group = gvsT.GetMethod("GetGroup").Invoke(inst, new object[]{ (int)UnityEditor.GameViewSizeGroupType.Standalone });
var sizeT = asm.GetType("UnityEditor.GameViewSize"); var enumT = asm.GetType("UnityEditor.GameViewSizeType");
var size = System.Activator.CreateInstance(sizeT, new object[]{ System.Enum.ToObject(enumT, 1), '$1', '$2', "Test"+'$1'+"x"+'$2' });
group.GetType().GetMethod("AddCustomSize").Invoke(group, new[]{ size });
int idx = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null) - 1;
var gv = UnityEditor.EditorWindow.GetWindow(asm.GetType("UnityEditor.GameView"));
gv.GetType().GetMethod("SizeSelectionCallback").Invoke(gv, new object[]{ idx, null });
return idx;' 2>&1 | tail -1 | cut -c1-300
