# Fixed channel gate

The fixed-channel setting is account/profile scoped and disabled by default (`FixedChannelNumber = 0`).
The home-page selector exposes `not fixed` plus channel numbers 1 through 10.

When enabled, `FixedChannelController` owns the per-account gate:

1. Confirm two consecutive valid channel mismatches.
2. Suspend ordinary account work.
3. Return to within 20 meters of revive-path point zero.
4. Wait 5 minutes 10 seconds once for the correction session.
5. Execute exactly six mouse clicks in order: menu, service, switch channel, channel move, select channel, move.
   The switch-channel step moves directly from the service coordinate by their coordinate delta, without rebasing the cursor to the top-left; the other five steps keep the ordinary top-left rebase.
6. Start the 30-second verification window after the sixth click finishes.
7. On timeout or an unavailable channel snapshot at the deadline, execute all six clicks again without repeating the initial wait.
8. Resume custom combat from revive-path point zero only after the target channel is verified.

The home page stores six independent screen coordinates. Each row has a move-only test button; tests never click. All six coordinates are validated before a live attempt sends any mouse input, so an incomplete configuration cannot perform a partial channel operation.

## Client adapter ports

- `IRoadhogScopedChannelGameApi` supplies an account-scoped `ChannelSnapshot` containing map id, zero-based channel index, channel count, and capture time.
- `IFixedChannelSwitchExecutor` owns the complete client UI operation for one switch attempt.

`AionVmmGameApi` implements the scoped channel port with the 2026-09-09 client layout: channel index/count are the adjacent `uint32` values at `Game.dll + 0xD71CB0` and `+ 0xD71CB4`. The map context is embedded at `Game.dll + 0xD647C0`, so map id is read directly from `Game.dll + 0xD647C0 + 0x20DC` (`Game.dll + 0xD6689C`). Do not dereference the context base: its first qword is a vtable. Account 4's live probe read map id `120010000`; the old extra dereference read the UTF-16 fragment `op` as `7340143`. Verification reads use the account context's cache-bypass flag.

The state machine receives configured coordinates only through `FixedChannelSwitchRequest`. `FixedChannelMouseSwitchExecutor` is the production UI adapter and performs exactly six left clicks in the required order. The channel snapshot still comes through the documented API port; the state machine contains no client offsets. Tests use mock snapshots, a manual clock, and recording input; they do not require a live client.
