# Loot interaction adapter

This local read-only adapter publishes `LootInteractionSnapshot` through the sealed
`loot_interaction` Stable, FieldAware channel and `IRoadhogSnapshotReader`.
Game input remains in `IKeyboardInput`; this adapter performs no writes or packet calls.

Evidence: `aion202609091125.json`, `S_LOOT` dispatcher/handler `sub_18043D130`,
and the Script 1 capture `.tmp/corpse-probe-20261031/touch-20261031-213059.jsonl`.
On 2026-10-31 the selected corpse 2181382964 was acknowledged about 765 ms after
one NumPadDecimal press. The ID and visible window disappeared by the next sample,
48 ms later. LootableRaw was still 1 at acknowledgement and became 0 later.

The local adapter reads the accepted corpse ID, loot window visibility, and its
owned confirmation index. The acknowledgement ID is sufficient evidence of touch;
actor state 21, key acceptance, distance, disappearance, and LootableRaw becoming
zero are not success criteria. These fields can change independently; valid ID
updates survive incomplete UI reads. UI structure is verified before publication.
Failed reads retain the official snapshot; cold start waits below the business
boundary. Connection, process, module, local entity/session changes invalidate the
publication. No business code sees pointers or read quality.

`LootTouchObservation` runs only during the active loot action, with a 15 ms delay
between lightweight captures. It is armed before the first input, latches the
matching corpse ID, and is cancelled on completion, death, branch reset or stop.
The foreground workflow still yields between attempts. Input retries are spaced
600 ms apart, with an 8 s confirmation deadline. The old post-kill delay now limits
readiness waiting (default 1.2 s); ready corpses do not wait. The post-pick delay
and burst-count settings no longer govern pickup. There is no proximity success.

Only confirmed touches enter the per-account five-minute cache. An unconfirmed
timeout receives a separate five-second retry deferral. Observed loot UI is
dismissed with Escape only if it persists for 150 ms (300 ms key spacing, 1.5 s
bound); automatically closed UI proceeds immediately. No item confirmation is
accepted and residual item contents do not trigger another pickup. Existing
post-combat maintenance, cleanup, return and pre-aim handoff remain downstream.

The ordinary corpse signal was live-verified. Limited-item and confirmation-popup
behavior is covered by mocks; their real-client UI dismissal still needs an actual
matching scene. Polling cannot guarantee observing a signal shorter than a capture
gap; a missed acknowledgement causes a bounded retry, never fabricated success.
