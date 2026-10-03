# Skill availability adapter evidence

The independent `ISkillAvailabilitySnapshotReader` contract is intended for the
new skill-tree release mode. It must not change `QuickbarSnapshot`, the existing
quickbar binding decoder, or legacy-mode startup reads.

## Verified evidence (2026-10-03)

The supplied `aion202609091125.json` describes the client drawing and execution
paths below. Layout evidence must also match the connected module before use.

- `sub_1805C9DA0` (`UIItemSlot::Draw`) computes a local disabled value `v214`
  using `sub_1805CCAB0`; it computes cooldown separately. The disabled value is
  used to multiply a local drawing color before submitting the icon. The export
  does not show a persistent general-purpose usable flag on the slot.
- `sub_1805CCAB0` calls `sub_180620A20`, which delegates to `sub_18061F860` for
  current client-side skill conditions. `sub_1805CD020` recomputes that gate for
  execution and also tests cooldown; it does not read a cached usable flag.
- The embedded slot's `+192` float (`control+984`) is an animation timer. Draw
  updates it only for special counter, target/self condition, or chain metadata.
  It is cleared for disabled/cooling states and advanced by `sub_180620FF0`
  otherwise. Previous live observations validated it as an opportunity signal
  for selected chain and Parry slots. Ordinary skills without special metadata
  do not have this update contract. A zero timer on an ordinary slot cannot be
  interpreted as a disabled skill.
- `SkillItem+96` is the toggle/activation state already exposed by the existing
  learned-skill decoder. `sub_18003A190` writes `a4 != 0 ? 4 : 0` to this field.
  Draw checks it only after selecting the non-disabled drawing branch, to choose
  an alternate active texture. It is not a general-purpose usable flag.
- The slot's `+256` value records disabled texture-cache construction. Draw sets
  it when constructing the texture; `sub_1805CCEF0` clears it when textures are
  rebuilt. It does not clear simply because a skill becomes usable.
- `support_quickbar` insertion/removal (`sub_1802784E0`, `sub_180278C50`) uses
  `sub_18061EE50` special opportunities. It does not apply the complete resource,
  motion, equipment and cooldown gates used by Draw/execution.
- The main icon is submitted through `sub_18061A020` to engine virtual methods.
  The supplied export does not identify a persistent drawing queue with stable
  slot and frame identities. `sub_18061D100` has shared temporary vertex/color
  buffers for cooldown/animation drawing; those buffers are reused across slots
  and are not a validated last-icon-state source.
- Previous live capture verified local `actor+804/+808` as the most recently
  actually released skill ID and game release time. They are action evidence,
  distinct from an application key send and from cooldown changes shared by
  multiple skill ranks.
- The XML parser `sub_18053C3F0` derives the Draw self-condition value
  (`metadata+264`, Draw's `v210`) from `effectN_type == 164`, `reserved1..7`.
  The supplied DLL's parser tables establish effect 164 as `Evade` and
  `sub_18053C360`'s 32 recognized status names. Only recognized status strings
  in those Evade fields are exposed as `XmlSelfConditionStatuses`. This is
  metadata classification, not a calculation of the current actor's statuses.
  In the supplied 7,063-skill XML, these are `1937 Test_Evade` and
  `1968 ALL_ShockReflect_G1`. For `first_target=Me`, the client clears its
  target-status mask; the latter's self opportunity comes from its Evade
  effect. `first_target=Me` alone never makes an ordinary skill special.
- Draw also tests `metadata+1908`, populated by `ultra_transfer` in the XML
  parser. The only enabled entry in the supplied XML is `1822 DP_Transfer`.
  `XmlUltraTransfer` is exposed only for parsed value `1`. Both self-condition
  and transfer metadata select the same demonstrated special timer adapter;
  neither is allowed to enter ordinary cooldown fallback. The ordinary
  `self_flying_restriction` field is not a Draw opportunity category.

## Publication requirements

The experimental decoder declares icon opportunities only for slots whose
currently displayed skill has supported counter, chain, target-condition,
Evade self-condition or enabled ultra-transfer
metadata. Ordinary skills are capability-unsupported in this adapter; their
exact displayed IDs are published in `UnsupportedSkillIds`, and `BindingSlots`
contains all 24 actual bindings. The controller may use the separately agreed
existing official cooldown logic for ordinary skill-tree nodes. That path is
not presented as a newly discovered universal icon flag.

The provider owns raw validity. Register a separate sealed `Stable` channel and
use the existing capture/merge/publication pipeline. Successful valid `false`
values are published immediately. Partial reads merge only valid observations
with identical session/page/bar/slot/binding identities. Failed reads preserve
the last official value; a cold-start read retries below the business boundary
and supports cancellation. A slot rebound to another skill must not inherit the
previous skill's usable value. Validate character/process/module/session
generation before committing a capture, including its release fields. The
release ID/time pair is captured twice and merged as a pair. A positive special
animation timer is accepted on its first valid capture, and zero publishes
`CanUse=false` immediately; there is no repeated-positive confirmation or TTL.

The static binding signature includes page, bar, slot, content type and exact
base skill ID for all 24 slots. It excludes dynamic displayed IDs, so a normal
root changing to its chain child does not simulate a page/rebinding change.
Displayed IDs and opportunity values are one coupled observation when partially
read. The adapter never maps a lower bound rank to a higher learned rank: the
verified slot `1227` remains `1227`, even when `1271` shares its cooldown family.

The panel and supported control visibility bits (`+0x28`, bit 0) and their
identities are captured and rechecked. Explicitly hidden presentation clears
only the new opportunity channel's session; it cannot retain a previously
highlighted timer as a fresh visible opportunity. This is a lifecycle change,
not a read-failure timeout. Visibility does not prove the client is rendering
frames while minimized or suspended. The experiment therefore requires visible,
actively rendered current main/Alt bars. The export does not provide a trusted
per-slot rendered-frame identity; a static positive timer alone cannot prove
render progress. At the initial usable frame the timer may still be zero until
the client advances its next Draw animation update.

Keep new-mode reads demand-driven and cancellable by the mode session. Legacy
mode must not call, initialize, prewarm, or wait for this new channel. Shared VMM
connection and hardware failures remain shared infrastructure limitations; an
availability decoder failure must not itself reset the entire connection.

The implemented adapter supports these experimental special opportunities and
release confirmation. It does **not** establish a universal icon availability
field, a rendered-frame heartbeat, or ordinary resource/action eligibility.
The legacy Quickbar decoder and its reads are unchanged. The new reader is
invoked only by the selected new skill-tree release mode.

## Batched capture and fast-loop guard

The UI capture uses six dependent NOCACHE scatter stages: shared anchors;
current page table and panel pointers; panel properties and both control arrays;
all 24 slot bindings; supported slot visibility/timers and first release pair;
then every structural guard and the second release pair. A batch does not make
external memory atomic. Original byte-count, identity, page, binding, visibility
and release-pair checks remain. A short individual timer is Partial, while
independently valid fields publish immediately. Structural read failure holds
the existing official object; observed lifecycle changes still invalidate it.

Actor resolution for this channel reads existing pointer regions as blocks,
then batches candidate identity and name reads. It retains the existing
candidate score/order and fallback regions without unused actor resource reads.
The selected locator path and three known actor identity fields (entity back
pointer, object type and server ID) are rechecked in one final batch. A locator
belongs to this capture only; no actor cache or extra initialization was added
to legacy channels. An incomplete actor check is read failure; a successfully
observed changed identity is a lifecycle change. The connection handle's
generation is still checked at publication.

The optional CombatState extension carries local entity/server IDs, target
entity/server IDs, current/max HP and MP, and current DP. Only existing resource
RVAs and actor target/server fields are used. Resource zeroes are meaningful
business values. HP/MP pairs and target identities are validated and merged
independently inside the provider; cold incomplete reads cannot create a
default combat state. All resources fit in one 20-byte request, so MP/DP add
no separate transport batch. CombatState null means an adapter or legacy mock
does not supply this optional capability, not a business read-quality fallback.

These fields establish local life/resources and selected-target identity. They
do not establish target HP, monster classification, distance or a universal
icon flag. Initial target acquisition and shared maintenance retain existing
formal snapshots. Six transport batches are a static operation budget, not an
80ms wall-clock guarantee on every device or shared account.

## Exact filtered cooldown lookup

The supplied JSON's `sub_1803B2100` is the game's exact SkillItem lookup used
by the slot drawing path. It starts at `*(skillManager+0x830)`, reads the root
from the header's `+8`, compares the requested uint ID with node `+0x20`, and
follows left `+0` or right `+0x10` until the `+0x19` sentinel. It rejects a
non-exact lower bound and an outer entry with zero level count at `+0x30`.
This establishes the ordered outer-map lookup; it does not establish any
name-family or rank alias. The existing highest-inner-level/item decoder is
reused after finding that exact outer key.

Previously even filtered reads advanced through all N learned outer nodes,
while filtering only saved detail reads. Explicit-ID requests now visit only
their ordered search paths. A balanced mock with 1,023 outer entries needs
10 node-block reads for one extreme key, compared with visiting all 1,023
entries before. This is a static read-count test, not a device latency claim.
Capture-local node reuse ends with this read; there is no cross-frame locator
cache. Missing exact IDs remain missing, and requested ranks bypass display
name grouping. The unfiltered enumeration and its display grouping are unchanged.

Filtered lookup reads tree anchors and nodes without the provider's memory
cache. A final NOCACHE scatter checks the manager/header, sentinel, visited
key/link fields and selected level-tree binding, then reads each exact item's
ID, cooldown duration and end time. All three item fields require full reads;
an ID mismatch or any short mandatory read fails the raw capture and enters
the existing Skills trusted-publication hold path. Fully read zero cooldown
values remain valid. This removes the old scalar-read cache delay from the
new loop's filtered cooldown observations without changing full skill reads.
The lookup tests cover absent keys, empty levels, ordered bounds, cycles,
foreign/damaged sentinels, short reads, changed final guards, exact-rank identity
and valid zeroes versus missing cooldown fields.

## 80ms combat scheduling

The new mode targets one serial poll cycle every 80ms. Read and finite key-hold
time are deducted from that cycle, and retry timing starts at the poll boundary
so variable read durations do not accidentally skip another full cycle. Key
holds in this mode are capped at 30ms. A local fast segment lasts at most a
320ms scheduling budget before returning to the existing worker; a pending
external read or input call may overrun a budget. Shared maintenance, opening,
pet, target acquisition and worker lifecycle work keep their existing rules.

Every poll and press checks the official bar's life and selected-target identity.
New HP/MP/DP maintenance candidates interrupt the segment. Candidates already
checked but currently unexecutable cannot starve attacks until their own CD ends.
Only configured exact displayed continuations have chain priority. The current
eligible action is retried until CD, display/opportunity closure, acceptance or
its bounded failure limit. After a configured predecessor is accepted or its
own cooldown advances, a finite chain handoff waits for the next displayed
opportunity instead of immediately pressing another attack root. The handoff
continues normal 80ms polls and all worker/life/maintenance guards. It lasts
at most 1500ms, capped by a shorter positive child XML opportunity window.
Startup bindings expose only this immutable XML duration; the legacy node's
configured delay and startup cooldown values do not drive the new handoff.
The deadline belongs to the original action and is not extended by retries,
repeated cooldown observations or a later precise release confirmation.
All immediate children known to be cooling end the wait early. An unopened
probability branch ends at the fixed deadline; it cannot hold combat forever.
Only the actual displayed, usable configured child is pressed. A cooldown-only
handoff does not fabricate a precise release confirmation. Scope/stop changes
and real maintenance takeover cancel it. Without a pending chain handoff,
entering CD can select another action in the same tick. Confirmed zero-CD roots
yield to the next eligible root.

The supplied JSON proves a common time domain: `sub_1800570F0` writes the return
of `sub_181244AE3` to actor+808, while `sub_1803AC470` and `sub_1803B2500` add the
same clock's return to SkillItem+80 duration and write SkillItem+84 end time.
An observed actual release time is therefore a safe initial lower bound for
checking old cooldown ends; it is not automatically the current time. Before
complete calibration, the action state advances that lower bound with monotonic
elapsed time. This uses the same running-client one-ms-per-ms clock assumption
as the existing calibrated clock, and late observation adds conservative waiting.
It is not valid proof for a paused or slower remote clock. New actual releases
re-anchor it; zero release time never invents a clock, and scope/stop reset it.
A real observed cooldown advance supplies the existing full calibration.
No current clock RVA, KUSER address or host OS offset is guessed.

Before initial calibration, the new mode prefers an explicitly configured,
exactly bound ordinary hostile attack with a positive cooldown. XML active,
enemy and attack/damage metadata must identify that candidate; passive, toggle,
DP, support, special conditions and chain continuations cannot be trial skills.
A known-ready candidate keeps ordinary release handling. If the candidate's
historical cooldown cannot yet be evaluated against the game clock, a separate
finite startup action may trial its key. This does not publish CanUse=true,
change its cooldown readiness to Ready, or treat a key send as calibration.
Only the existing observed cooldown advance supplies full clock calibration.

Unknown-clock trials use the same serial 80ms polls and action-boundary guards.
Each actually started candidate gets at most 1200ms, with no more than three
different candidates and a 4000ms total monotonic budget. Candidate selection
alone spends no budget. Explicitly cooling skills and rebound slots are never
trialed. Completion is latched for that scope; retries, maintenance and failed
inputs cannot reopen or extend it. Maintenance may interrupt input while elapsed
budget remains consumed. Death, stop, target/page/binding changes clear the
action scope. Actual configured chain opportunities and existing handoffs keep
priority throughout startup. The legacy mode does not invoke this trial policy.

Ordinary readiness uses the official exact cooldown, plus this clock domain;
the special adapter retains its actual CanUse signal and excludes known cooling
skills. The callback receives the current official availability value and is
recomputed at the press boundary. Hardware transport, client animation, worker
handoffs and actual game cast timing may exceed the 80ms target. Poll cadence,
input attempts and confirmed actual releases are separate measurements.
