# Aion 20610 Mission Notes

## Start NPC

F11 sample on 2026-06-10 while quest `20610` was active:

- character: `HiBroHi`, level `5`
- map: `DF1_SZ_Alder_Q1`, `big_map_id=220010000`
- quest: `20610`, `status_code=3`, `req_count=0`
- target: `아스크`, `kind=NPC`, `id=65520`
- `interact_id=2147514375`
- position: NPC `560.99,2786.03,299.06`, character `562.99,2783.84,299.37`, distance `2.98`
- dialog was closed before interaction

Use `2147514375` for the quest `20610` opening NPC. The older `2147492916` value was observed in the previous 20590 reward context and did not open the 20610 dialog in the current log.

## Task teleport

After the initial NPC dialog chain for quest `20610`, use the documented mission teleport API instead of clicking the quest UI by screen coordinates:

- `GetQuestTeleportId(20610)`
- `QuestTeleport(20610, teleport_id)`

The old UI path below is kept as historical F11 evidence only. Do not use the `v3_quest_dialog` coordinate as an automation selector because resolution and UI scale can vary.

## UI controls recorded (historical)

These controls were observed after the initial NPC dialog chain for quest `20610`.

1. Quest tracker teleport entry
   - parent: `quest_indicator_dialog`
   - name: `teleport`
   - observed: `depth=3 obj=885856256 name=teleport visible=true x=1221 y=263 parent=quest_indicator_dialog`
   - purpose: open the quest detail / NPC finding flow from the tracked quest.

2. Quest detail NPC link
   - parent: `v3_quest_dialog`
   - name: `(no-name)`
   - observed: `depth=3 obj=846082560 name=(no-name) visible=true x=424 y=254 parent=v3_quest_dialog`
   - purpose: click the blue NPC link in the quest detail page.

3. Dictionary teleport button
   - parent: `dictionary_dialog`
   - name: `teleport_to_npc`
   - observed: `depth=1 obj=1043476992 name=teleport_to_npc visible=true x=569 y=495 parent=dictionary_dialog`
   - purpose: start the actual teleport/read-bar to the NPC.

After clicking `dictionary_dialog.teleport_to_npc`, wait for the read bar/position change. The character is teleported to another position when the read completes.

## Reward NPC after teleport

After the task teleport/read-bar completes, the character lands near the reward NPC:

- interact_id: `2147524326`
- observed NPC position: `223.97,2679.86,295.25`
- observed character position: `223.17,2680.63,295.25`
- dialog chain:
  1. `select_success/content_id=10002/quest_id=20610` -> click x=25 child
  2. `select_quest_reward1/content_id=5/quest_id=20610` -> click `ok`
- OK button F3:
  - parent: `dlg_dialog`
  - name: `ok`
  - observed: `depth=1 obj=1034967552 name=ok visible=true x=129 y=419 parent=dlg_dialog`

After clicking OK, do not interact with the NPC again. Mark quest `20610` reward complete in runtime.

## Next blue grind task

The next tracked blue task after quest `20610` is handled by the 20611 grind runner.

- completion source: `GetQuestList()`
- quest matching: use the normal blue task id returned by `GetQuestList()` (`24340`/`24341` observed). Do not start the grind from `20611-20615` level-blocked main quests alone.
- active status: `status_code=3`
- complete status: `status_code=4`
- grind point: `194.491,2689.982,300.625`
- behavior: after quest `20610` reward is confirmed, wait for `GetQuestList()` to show the follow-up blue grind task before moving to the grind point. On script restart or character switch, do not infer progress from level alone.
- restart recovery: rely on direct task evidence from `GetQuestList()`. The follow-up normal blue grind task can be `24340` or `24341`; `status_code=3` means grind is active, `status_code=4` means stop combat and submit reward.
- arrival/start guard: use a 10m range for the grind point. Before stationary combat starts, only continue this phase when `GetQuestList()` shows a supported blue task (`24340`/`24341`) with `status_code=3`. When the task becomes `status_code=4`, stop grinding and submit.
- combat gate: keep the normal custom stationary combat gate limited to primary mode `combat`. The 20611 grind step runs while primary mode is `leveling`, so it uses a dedicated `combat_tick_quest_grind()` adapter that only runs when `active_20611_grind=true`.

## 20611 grind reward handoff

F11 after the grind showed the actual completed blue task as normal task `24340`, not a main quest id:

- `quest_id=24340`
- `tab=1`
- `status_code=4`
- `req_count=5`
- `exp_reward=792`
- remote reward dialog: `type=select_quest_reward_remote`, `content_id=56`, `quest_id=24340`
- dialog OK child: `name=ok`, `x=128.67`, `y=418.67`

For that older `24340` run, `OpenQuestSubmit(24340)` opened the completed task submit panel, then the dialog OK button completed the reward. Only after OK succeeds should the 20611 grind stage be marked complete.

F11 on 2026-06-10 after quest `20610` showed the next normal blue task as `24341`:

- `quest_id=24341`
- `tab=1`
- `status_code=3`
- `req_count=0`
- `lv_num=7`
- `exp_reward=1055`
- map `220010000`, `알데르 언덕`
- character position `191.28,2693.58,300.62`
- Korean title in the quest panel: `알데르 언덕의 골칫거리 II`

Use the actual blue task id returned by `GetQuestList()` for `OpenQuestSubmit(...)` and remote reward OK handling. Do not hardcode only `24340`.

## 20611 yellow level-gated mission

F11 after completing the blue normal task showed only yellow mission entries:

- character level `6`
- map `220010000`, `알데르 언덕`
- `20611 tab=0 status_code=6 seq=1 lv_num=8 exp_reward=228`
- `20612 tab=0 status_code=6 seq=2 lv_num=11`
- `20613 tab=0 status_code=6 seq=3 lv_num=14`
- `20614 tab=0 status_code=6 seq=4 lv_num=17`
- `20615 tab=0 status_code=6 seq=5 lv_num=20`

Although the F11 `current_id` line reported `20615`, the task panel screenshot had the 8-level yellow mission selected. For level-gated yellow missions choose the earliest `seq` level-blocked mission, which is `20611` here.

The visible Korean button is `즉시 이동` (immediate move). Before using the mission teleport API, open the mission/quest panel (`J`) and confirm `v3_quest_dialog` is visible. Calling `QuestTeleport` while the panel is closed has crashed the client in testing.

```lua
QuestTeleport(20611)
```

Do not click the panel by screen coordinate. If the character is already in the recommended area, the call may not produce an obvious position change, so this step should be a one-shot action and should not wait forever for movement.

## Startup resume rule

When the script starts in leveling mode, read a current character snapshot before running quest ticks:

- character name/level
- current map id/name
- `GetQuestList()`
- current open NPC/task dialog, if any

Then apply stage flags from `aion.main_quest_resume` so switching characters or restarting the script does not reuse stale runtime state from the previous character.

Hard gates:

- character level `<= 1`: always start from quest `20590`; ignore later-task evidence
- quest `20590 status_code=3`: run quest `20590` first; block `20610`/`20611` ticks
