#!/bin/bash
cd /c/Users/jorge/poke_memories-wp4/unity
unity command editor_stop >/dev/null 2>&1
unity command recompile >/dev/null 2>&1
sleep 6
for i in $(seq 1 30); do r=$(unity command recompile_status 2>&1 | tail -1 | cut -f3); [ "$r" = "completed" -o "$r" = "up_to_date" ] && break; sleep 2; done
unity command clear_console >/dev/null 2>&1
unity command editor_play >/dev/null 2>&1
sleep 4
timeout 20 unity command console 2>&1 | tr '{' '\n' | grep -i 'error CS\|"level":"error"' | cut -c1-280 | tail -5
echo cycled
