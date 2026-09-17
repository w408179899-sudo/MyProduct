# Snapshot provider native smoke

This separate diagnostic verifies the public typed `SnapshotCatalog` / `SnapshotProvider` contract
using only a selected process module's static PE header and deliberately invalid virtual address zero.
It has no game fields, writes, keyboard or mouse input, process enumeration, or implicit device choice.
No arguments or `--help` loads no native library and acquires no lease.

```powershell
dotnet Smart.SnapshotSmoke.dll --library C:\Native\vmm.dll --device fpga://devindex=0 --pid 1234 --module target.exe --duration-ms 5000
```

`--diagnostic` enables VMM `-printf -v`. Shared argument validation and PE decoding are reused from
the standalone NativeSmoke project without altering that tool or the public framework.

The bounded scenario checks these steps through the actual provider, never a second snapshot cache:

1. Read address zero during cold start. Require completed failed captures, no publication, and a
   waiting official read that can be cancelled after 150ms.
2. Switch the internal raw reader to the selected module's 4096-byte header. Require a valid MZ,
   bounded PE/COFF/optional header, stable SHA256, and first official publication at version one.
3. Switch back to address zero. Require another completed failed capture and exactly unchanged
   official value, generation/version, capture timestamp and publication count.
4. Recover the valid header. Require another successful capture and a new official version.
5. Explicitly reset the same session. While reading address zero, require no old snapshot to escape
   and a cancellable wait. Then require a valid publication at version one in a different generation.

Every native capture checks selected process/module identity before and after reading, on the DMA
dispatcher thread. A real identity change ends the run as a failure. PE metadata must fit wholly in
the fixed 4096-byte header; unsupported layouts fail rather than causing additional arbitrary reads.
All address-zero reads must be incomplete; an unexpectedly complete result cannot pass the control.

`--duration-ms` is the total managed deadline (500..60000ms), including initialization. Cancellation
uses awaited `CancelAsync`. Native synchronous initialization/read/close cannot be forcibly aborted;
this is a soft native deadline. Cleanup drains provider captures, then dispatcher/native transport,
then releases the Smart device lease. Native close failures retain the lease until cleanup retry.

The JSON report contains contract flags, actual capture/publication counts, identity, SHA256, stamps,
timings and cleanup state. Success returns 0; unmet assertions, timeouts and handled errors return 2.
This proves real bad-address handling and static-header publication behavior, **not** dynamic half-written
game structures, physical disconnect recovery, gameplay correctness, or KMBox behavior.

Focused tests use only fake transports. Build with `--artifacts-path artifacts/snapshot-smoke-verification`
to keep outputs separate from running framework binaries.
