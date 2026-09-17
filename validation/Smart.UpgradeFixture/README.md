# Frozen external upgrade consumer

This fixture is a small, game-free external consumer written against Smart **0.4.1**. It uses real
NuGet archives and fake reads/input only. It has no project reference to framework source, native
initialization, device selection, license service or live input.

The seven historical archives under `baseline-packages/0.4.1` are the original verified release
bytes (192,893 bytes total), with SHA256 in `package-hashes.json`. Do not rebuild or overwrite them
from newer source. Do not update this consumer to accommodate an incompatible new API and then
claim that the old consumer upgrades successfully. Introduce a separately versioned fixture if a
deliberately breaking release needs a new baseline.

From the repository root, after producing the new packages:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Upgrade.ps1 `
  -PackageFeed artifacts/packages -ArtifactsPath artifacts/upgrade
```

`CurrentVersion` defaults to the repository's `SmartFrameworkVersion`; `BaselineVersion` defaults
to `0.4.1`. Both can be supplied explicitly. A different `BaselinePackageFeed` must contain the
seven selected historical archives and a `package-hashes.json` with the same `Version` and
`Packages: [{ "File": "Smart.Contracts.<version>.nupkg", "Sha256": "..." }, ...]` structure.
Missing archives, conflicting bytes for one version, and baseline hash mismatches fail immediately.
The script never builds framework source, repacks an old release or downloads a missing baseline.

Each run creates a unique output directory. A copied consumer has its own MSBuild props/targets,
disables central package inheritance, and restores exact package versions from one isolated local
feed with no fallback folders. Separate empty baseline/current caches prevent stale package reuse.
The installed .NET 10 SDK, Windows desktop reference/runtime packs and Windows are prerequisites;
missing SDK packs fail against the local feed rather than requesting network packages.

The script verifies three paths:

1. Compile and run the fixed consumer with the old packages; save a configuration using the old
   `JsonConfigStore`. Capture source hashes, resolved assets, archive hashes and assembly hashes.
2. Change only `FrameworkVersion.props`, then compile and run the identical consumer with the new
   package version. Every other consumer file must keep its original SHA256.
3. Copy the old compiled output and replace the seven framework DLLs with the actual new
   package DLLs, including any `native-worker` payload introduced by the new DMA package. The worker's
   four deployment files must match the actual package-cache hashes. Run without recompiling any consumer assembly; all three consumer DLL hashes must
   still match the baseline. The old configuration document must remain byte-for-byte unchanged.

Every execution checks fixed/persisted JSON, Domain/Application assembly boundaries, cancellable
cold-start reads, a valid zero publication, failure retaining the identical published object and
stamp, successful recovery, module execution/stop, fake input release and reacquiring its lease.
Domain references no Smart assembly; Application references only Smart.Contracts. The runner is
the infrastructure/composition boundary and is the only project that handles raw read results.

The **old compiled consumer** also captures the real exported API of all seven framework assemblies.
The compatibility gate checks baseline types, base/interface relationships, visible constructors,
methods, properties, events, fields/constants, parameter names/defaults, generic constraints and
required modifiers. Missing/changed signatures and new abstract obligations on existing types fail.
A removed-signature negative control must fail, proving the comparison did real work. This is a
strict structural guard; it does not claim full CLR ABI, nullable-annotation or semantic compatibility.
The unchanged-old-binary run and behavior checks provide separate runtime evidence.

`latest-run.json` points to the unique `upgrade-summary.json`; build/run logs, package/cache proof,
consumer source/binary hashes, all three behavioral reports, API surfaces and the comparison stay
inside that run. Any failed step leaves its evidence and a failed summary. The historical package
set and consumer source must be reviewed as fixed test inputs, not automatically regenerated output.
