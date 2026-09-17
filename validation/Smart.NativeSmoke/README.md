# Native PE-header smoke

This standalone diagnostic reads only the selected process module's generic PE headers. It has no
game offsets, memory writes, KMBox input, process-list output, or implicit device selection. No arguments
or `--help` returns usage without loading a DLL or acquiring a device.

```powershell
dotnet Smart.NativeSmoke.dll --library C:\Native\vmm.dll --device fpga://devindex=0 --pid 1234 --module target.exe --duration-ms 5000
```

`--diagnostic` adds only `-printf -v` to VMM initialization. `--zero-control` additionally reads 64 bytes
at virtual address zero as an expected invalid-read control; an unexpectedly complete result fails the
acceptance result. Neither option permits writing memory.

The explicit capture sequence validates a 64-byte MZ header and bounded `e_lfanew` (64..65536), then the
PE signature/COFF/optional prefix, then batches the complete bounded optional header and fixed
4096-byte module header together. When requested, the same batch also contains the address-zero
control: both valid blocks must remain complete while only the invalid block fails. Section count is
limited to 1..96 and optional-header size to 4096 bytes. A 100ms async interval
bounds polling. Existing `VmmTransport.GetProcess` also reads the fixed 4096-byte header while binding
and checking module identity; this tool does not change that shared implementation.

The report contains aggregate success/failure/control counts, selected identity, header SHA256, timing
and cleanup state. A successful native smoke is not proof of live game-data activity or KMBox behavior:
PE headers are normally static. Use the controlled target process for changing-value validation.

Duration is the 100..60000ms observation window after initialization. Native synchronous calls may
outlast that window; shutdown awaits the queue and native close before releasing the shared Smart
device lease. Close failure keeps ownership for a subsequent disposal retry. Exceptions are handled
and return exit code 2, allowing ordinary native stdout flushing; success returns 0.

Tests use a fake transport and do not initialize physical hardware. The parent workspace can build
this project and its tests with `--artifacts-path` for isolated outputs.
