# File diagnostics

`latest.log` and `roadhog-*.log` remain UTF-8 JSON Lines. Existing sampling,
immediate writes, rotation, directory limits and retention remain unchanged.
R1 replaces repeated names with fixed symbols; it does not discard more events
or field values. In-memory diagnostics retain their original representation.

Each R1 line is independently decodable:

```text
["R1", localDateTimeTicks, utcOffsetMinutes, level, event, [field, value, ...], error?]
```

Ticks retain .NET's 100 ns precision. Levels are `0=info, 1=warn, 2=error`.
Integer event/field symbols index the corresponding arrays in
`FileLogSymbols.v1.json`, embedded in the executable. Strings are literal names
not present in the dictionary. Values retain their original JSON types. The
optional error defaults to null. Unknown names are never dropped.

**R1 symbol IDs are permanent. Only append to the symbol arrays. Never reorder,
remove or reuse an index.** A breaking change requires a new version and retention
of the old decoder. No per-process or per-file dictionary state is required, so
rotation, pruning, concurrent writers and copying individual lines cannot remove
information needed for decoding. Legacy JSON objects may coexist with R1 rows.

Restore the original field names without starting the UI or workers:

```powershell
Start-Process .\Roadhog.exe -ArgumentList '--decode-log "input.log" "output.jsonl"' -Wait -WindowStyle Hidden
```

The output must not exist. Decoding never overwrites source evidence. Malformed
or unfinished rows fail with exit code 2; previously decoded rows remain in the
output. Capture complete lines when copying an actively written log. Do not parse
ticks as a JavaScript double; use .NET Int64 or another exact integer type.

Focused regression/audit commands (after an isolated Release build):

```powershell
dotnet Roadhog.Tests/bin/Release/net8.0-windows/Roadhog.Tests.dll --test-filter="file log"
dotnet Roadhog.Tests/bin/Release/net8.0-windows/Roadhog.Tests.dll --file-log-replay <captured-jsonl-directory> <output-directory>
```

Replay compares every restored JSON value and requires at least 50% fewer UTF-8
bytes for each supplied file. Captured customer logs stay local and are not test
fixtures in source control; the regression fixture contains sanitized examples.
