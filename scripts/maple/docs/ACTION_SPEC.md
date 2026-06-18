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
- `Wait`
- `Idle`
- `Stop`

Add real MapleStory behavior by extending an Environment adapter first, then adding tests against a mock equivalent.

`ExecuteCombatDecision` receives a `proposal` produced by the combat proposal ports and neutral resolver. The proposal is plain data; Environment adapters decide how to execute it against the real or mock client.
