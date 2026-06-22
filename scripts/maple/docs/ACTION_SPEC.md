# Action Spec

Action definitions live in:

```text
scripts/maple/data/action_specs.lua
```

Every action declares:

- timeout group
- max retries
- required params

Executor rejects unknown actions and missing params before calling Environment.

Current actions:

- `BindClient`
- `Login`
- `NavigateTo`
- `InteractNpc`
- `ProcessInventoryRules`
- `EvaluateEquipmentCandidates`
- `LearnSkill`
- `ExecuteCombatDecision`
- `BasicAttack`
- `UseQuickslot`
- `PressKey`
- `SetWalkDirection`
- `StopMove`
- `PickAllDrops`
- `UseItem`
- `EquipItem`
- `Wait`
- `Idle`
- `Stop`

Add real MapleStory behavior by extending an Environment adapter first, then adding tests against a mock equivalent.

`ExecuteCombatDecision` receives a `proposal` produced by the combat proposal ports and neutral resolver. The proposal is plain data; Environment adapters decide how to execute it against the real or mock client.

`PressKey` is the official keyboard execution brick. Business modules must queue `PressKey` or `ExecuteCombatDecision`; they must not call `keybd`, `wnd`, or `proc` directly.

The atomic Maple actions are reusable bricks for future combat, loot, movement, item, and equipment flows. Higher-level branches should queue these actions or a proposal that the Environment can safely map to these actions; business modules should not call real client APIs directly.
