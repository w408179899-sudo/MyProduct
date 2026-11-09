# Advanced skills

`ScriptSettings.AdvancedSkills` is opt-in and defaults to disabled. Existing JSON
without this property uses the original opening/quickbar flow. No account or
profile is automatically populated or enabled.

Elite, Ranged and Melee are **manual list labels**, not inferred attributes.
Monster names are matched in full after trimming, case-insensitively, using the
official locked-target snapshot. Names survive changes in spawned object IDs.
The editor supports both world-object enumeration and typed names (button or
Enter). Both paths reject duplicates across all groups, including disabled
groups. Imported ambiguous membership fails back to the ordinary flow. Special
monsters remain a UI placeholder.

The global switch, group switch, exact membership and a nonempty configured
skill list must all match. Only then is the target's opening skill list bypassed,
including deferred opener retries. Cooling or unbound configured skills do not
re-enable openers: the combined plan falls back to the first usable normal root.
The separate opening attack key/pet command and existing maintenance remain in
their original flows.

`AdvancedSkillCombatPlans` caches account-local immutable plans and returns the
original plan instance for unmatched targets. Matched plans prepend the group's
skills to the normal roots. A selection opportunity always checks this combined
order from the beginning, without a remembered normal-list cursor. Pending
actions, current continuations and finite handoff waits keep their input ownership.
Advanced selection never inserts a root into a running chain, even if the legacy
trigger-preemption option is enabled. The unmodified release executor handles
keys, cooldowns, confirmation, retries, target guards, maintenance and cancellation.
Equivalent physical bindings share repeat suppression across the two lists, so a
duplicate ordinary entry cannot replay an accepted chain or bypass a failed
advanced attempt's hold.

The editor stores complete skill trees; selecting a learned chain starter keeps
its descendants. Refresh-all-configured-skills includes the stored advanced
trees. Dialog edits are drafts until Save and participate in the existing account
and profile persistence path. Saved changes take effect on the next script start.

Focused coverage: `AdvancedSkillTests`, `AdvancedSkillUiTests`, plus the existing
quickbar, opening, unified skill, Spiritmaster, configured-skill-refresh and
responsive-layout tests. Use isolated builds (`RoadhogDesktopScriptRoot`) and
mock accounts; implementing this feature does not require restarting live workers.

## Shared advanced profiles

The advanced dialog can create, select, save-as and delete independent named
profiles. The library is `config/advanced-skill-profiles.json` beside accounts.json;
it has no path or profession binding. Existing inline advanced settings remain
available as the local, unshared choice. Nothing is automatically migrated.

`AccountConfig.AdvancedSkillProfileName` and `AdvancedSkillProfileEnabled` belong
to the account, outside the full script profile. Only group switches, manual
monster lists and complete ordered skill trees are shared. Normal settings saves
do not write the advanced library. Saving its editor or manually refreshing all
configured skills writes the selected advanced profile. Skill rank refresh still
uses the current bar; startup only resolves each account's own key bindings.

The normal worker entry loads the selected profile once, before combat setup.
The running worker retains its snapshot until its next script start. Missing or
invalid profiles disable the advanced module for that start and emit one warning,
so a stale inline copy cannot suppress the normal opening flow. No reference
retains the old inline behavior. Account shutdown/cancellation remains unchanged.

Atomic replacement plus a cross-process file gate and per-profile revisions
prevent partial reads and stale-window overwrites. Duplicate names are rejected;
save-as creates an independent profile. The UI refuses deletion while a saved
account references the profile. Failed account persistence compensates its own
library revision without overwriting a newer editor. `AdvancedSkillProfileTests`
covers these behaviors, including actual worker entry and per-account key binding.
