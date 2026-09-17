param(
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')][string]$BaselineVersion = '0.4.1',
    [string]$CurrentVersion,
    [string]$PackageFeed,
    [string]$BaselinePackageFeed,
    [string]$ArtifactsPath = 'artifacts/upgrade'
)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $smartRoot 'validation/Smart.UpgradeFixture'
function FullPath([string]$path) {
    if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $smartRoot $path }
    return [IO.Path]::GetFullPath($path)
}
if (-not $CurrentVersion) {
    [xml]$properties = Get-Content -Raw -LiteralPath (Join-Path $smartRoot 'Directory.Build.props')
    $CurrentVersion = [string]$properties.Project.PropertyGroup.SmartFrameworkVersion
}
if ($CurrentVersion -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw 'CurrentVersion must be an explicit package version.' }
if (-not $PackageFeed) { $PackageFeed = Join-Path $smartRoot 'artifacts/packages' }
if (-not $BaselinePackageFeed) { $BaselinePackageFeed = Join-Path $fixtureRoot ('baseline-packages/' + $BaselineVersion) }
$PackageFeed = FullPath $PackageFeed
$BaselinePackageFeed = FullPath $BaselinePackageFeed
$ArtifactsPath = FullPath $ArtifactsPath
$runRoot = Join-Path $ArtifactsPath ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$consumer = Join-Path $runRoot 'consumer'
$localFeed = Join-Path $runRoot 'feed'
[IO.Directory]::CreateDirectory($consumer) | Out-Null
[IO.Directory]::CreateDirectory($localFeed) | Out-Null
$dotnetPath = Join-Path $smartRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$packageIds = @('Smart.Contracts', 'Smart.Data', 'Smart.Runtime', 'Smart.Hosting', 'Smart.Hosting.Windows', 'Smart.Adapters.Dma', 'Smart.Adapters.KmBox')
$steps = [Collections.Generic.List[object]]::new()
$packageEvidence = [Collections.Generic.List[object]]::new()
$workerEvidence = [Collections.Generic.List[object]]::new()
$summary = [ordered]@{ Passed = $false; BaselineVersion = $BaselineVersion; CurrentVersion = $CurrentVersion; RunRoot = $runRoot;
    Offline = $true; FixedConsumerSource = $false; OldConsumerBinaryUnchanged = $false; PublicApiCompatible = $false;
    StartedUtc = [DateTimeOffset]::UtcNow.ToString('O'); Steps = $steps; PackageEvidence = $packageEvidence; WorkerPayloadEvidence = $workerEvidence }
function WriteJson([string]$path, $value) {
    [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
}
function Hash([string]$path) { return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function InvokeDotnet([string]$name, [string[]]$arguments, [int]$expectedExitCode = 0) {
    $log = Join-Path $runRoot ($name + '.log')
    & $dotnetPath @arguments *> $log
    $code = $LASTEXITCODE
    $steps.Add([pscustomobject]@{ Name = $name; ExitCode = $code; ExpectedExitCode = $expectedExitCode; Log = $log })
    if ($code -ne $expectedExitCode) {
        Get-Content -LiteralPath $log -Tail 30 | Write-Host
        throw ('Upgrade step failed: ' + $name + ', exit ' + $code + '. See ' + $log)
    }
}
function SourceHashes {
    return @(Get-ChildItem -LiteralPath $consumer -Recurse -File | Where-Object { $_.Name -ne 'FrameworkVersion.props' } |
        Sort-Object FullName | ForEach-Object {
            [pscustomobject]@{ Path = $_.FullName.Substring($consumer.Length + 1).Replace('\', '/'); Sha256 = Hash $_.FullName }
        })
}
function WriteVersion([string]$version) {
    [IO.File]::WriteAllText((Join-Path $consumer 'FrameworkVersion.props'),
        ('<Project><PropertyGroup><FrameworkVersion>{0}</FrameworkVersion></PropertyGroup></Project>' -f $version), [Text.UTF8Encoding]::new($false))
}
function AssertAssets([string]$stage, [string]$version, [string]$buildRoot, [string]$cache, [string]$output) {
    foreach ($projectName in @('UpgradeFixture.Domain', 'UpgradeFixture.Application', 'UpgradeFixture.Runner')) {
        $path = Join-Path $buildRoot ('obj/' + $projectName + '/project.assets.json')
        $assets = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        if ([IO.Path]::GetFullPath($assets.project.restore.packagesPath).TrimEnd('\', '/') -ne $cache.TrimEnd('\', '/')) {
            throw ('Consumer used another package cache: ' + $path)
        }
        $framework = @($assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'Smart.*' })
        $expectedCount = if ($projectName -eq 'UpgradeFixture.Domain') { 0 } elseif ($projectName -eq 'UpgradeFixture.Application') { 1 } else { 7 }
        if ($framework.Count -ne $expectedCount) { throw ('Unexpected framework dependencies in ' + $projectName) }
        foreach ($package in $framework) {
            $id = $package.Name.Split('/')[0]
            if ($package.Value.type -ne 'package' -or $package.Name -cne ($id + '/' + $version)) { throw ('Not an actual exact-version package: ' + $package.Name) }
            if ($projectName -eq 'UpgradeFixture.Application' -and $id -ne 'Smart.Contracts') { throw 'Application acquired a raw/runtime/hardware dependency.' }
            $archive = Join-Path (Join-Path $cache $package.Value.path) ($id.ToLowerInvariant() + '.' + $version.ToLowerInvariant() + '.nupkg')
            $feedArchive = Join-Path $localFeed ($id + '.' + $version + '.nupkg')
            $cachedHash = Hash $archive
            if ($cachedHash -ne (Hash $feedArchive)) { throw ('Cached package differs from the staged feed: ' + $id) }
            if ($projectName -eq 'UpgradeFixture.Runner') {
                $target = @($assets.targets.PSObject.Properties)[0].Value
                $runtimeFiles = @($target.PSObject.Properties[$package.Name].Value.runtime.PSObject.Properties | Where-Object { $_.Name -like '*.dll' })
                if ($runtimeFiles.Count -ne 1) { throw ('Expected one managed runtime assembly in ' + $id) }
                $runtimeAssembly = Join-Path (Join-Path $cache $package.Value.path) $runtimeFiles[0].Name
                $outputAssembly = Join-Path $output ([IO.Path]::GetFileName($runtimeFiles[0].Name))
                if ((Hash $runtimeAssembly) -ne (Hash $outputAssembly)) { throw ('Built output differs from the restored package assembly: ' + $id) }
                $packageEvidence.Add([pscustomobject]@{ Stage = $stage; Package = $id; Version = $version; FeedPath = $feedArchive;
                    CachePath = $archive; NupkgSha256 = $cachedHash; RuntimeAssemblySha256 = Hash $outputAssembly; Assets = $path })
                if ($id -eq 'Smart.Adapters.Dma') {
                    $packagedWorker = Join-Path (Join-Path $cache $package.Value.path) 'tools/native-worker'
                    if (Test-Path -LiteralPath $packagedWorker -PathType Container) {
                        foreach ($workerFile in @('Smart.Dma.Worker.exe', 'Smart.Dma.Worker.dll', 'Smart.Dma.Worker.deps.json', 'Smart.Dma.Worker.runtimeconfig.json')) {
                            $packagedFile = Join-Path $packagedWorker $workerFile
                            $deployedFile = Join-Path $output ('native-worker/' + $workerFile)
                            $workerHash = Hash $packagedFile
                            if ($workerHash -ne (Hash $deployedFile)) { throw ('Worker content differs from the actual restored DMA package: ' + $workerFile) }
                            $workerEvidence.Add([pscustomobject]@{ Stage = $stage; File = $workerFile; CachePath = $packagedFile; OutputPath = $deployedFile; Sha256 = $workerHash })
                        }
                    }
                }
            }
        }
    }
}
function BuildConsumer([string]$stage, [string]$version) {
    WriteVersion $version
    $buildRoot = Join-Path $runRoot ($stage + '-build')
    $cache = Join-Path $runRoot ($stage + '-cache')
    $project = Join-Path $consumer 'UpgradeFixture.Runner/UpgradeFixture.Runner.csproj'
    InvokeDotnet ($stage + '-restore') @('restore', $project, '--nologo', '--configfile', (Join-Path $runRoot 'NuGet.Config'),
        '--packages', $cache, '--artifacts-path', $buildRoot, '-p:Configuration=Release', '-p:NuGetAudit=false', '-p:RestoreFallbackFolders=', '--no-http-cache')
    InvokeDotnet ($stage + '-build') @('build', $project, '-c', 'Release', '--nologo', '--no-restore', '--artifacts-path', $buildRoot,
        "-p:RestorePackagesPath=$cache")
    $output = Join-Path $buildRoot 'bin/UpgradeFixture.Runner/release'
    AssertAssets $stage $version $buildRoot $cache $output
    return $output
}
function RunConsumer([string]$stage, [string]$mode, [string]$output) {
    $evidence = Join-Path $runRoot ($stage + '-evidence')
    InvokeDotnet ($stage + '-run') @((Join-Path $output 'UpgradeFixture.Runner.dll'), $mode, $evidence, (Join-Path $runRoot 'baseline-persisted-settings.json'))
    $result = Get-Content -Raw -LiteralPath (Join-Path $evidence 'result.json') | ConvertFrom-Json
    if (-not $result.Passed -or @($result.Checks).Count -ne 6 -or @($result.LoadedFramework).Count -ne 7) { throw ('Incomplete consumer evidence: ' + $stage) }
    foreach ($assembly in $result.LoadedFramework) {
        if ([IO.Path]::GetFullPath($assembly.Location) -ne (Join-Path $output ($assembly.Name + '.dll'))) { throw ('Unexpected runtime assembly location: ' + $assembly.Location) }
    }
}
try {
    foreach ($baseline in @($true, $false)) {
        $sourceFeed = if ($baseline) { $BaselinePackageFeed } else { $PackageFeed }
        $version = if ($baseline) { $BaselineVersion } else { $CurrentVersion }
        $manifestPath = Join-Path $sourceFeed 'package-hashes.json'
        $manifest = if (Test-Path -LiteralPath $manifestPath) { Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json } else { $null }
        if ($baseline -and -not $manifest) { throw ('A baseline SHA256 manifest is required: ' + $manifestPath) }
        if ($baseline -and ($manifest.Version -ne $version -or @($manifest.Packages).Count -ne 7)) { throw 'Baseline hash manifest does not describe the selected seven-package release.' }
        foreach ($id in $packageIds) {
            $name = $id + '.' + $version + '.nupkg'
            $source = Join-Path $sourceFeed $name
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw ('Required historical/current package is missing; it will not be rebuilt: ' + $source) }
            $digest = Hash $source
            if ($baseline -and $manifest) {
                $entry = @($manifest.Packages | Where-Object { $_.File -eq $name })
                if ($entry.Count -ne 1 -or $entry[0].Sha256 -ne $digest) { throw ('Frozen baseline package hash mismatch: ' + $name) }
            }
            $destination = Join-Path $localFeed $name
            if ((Test-Path -LiteralPath $destination) -and (Hash $destination) -ne $digest) { throw ('Conflicting bytes for the same package version: ' + $name) }
            Copy-Item -LiteralPath $source -Destination $destination
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath $fixtureRoot -Recurse -File) {
        $relative = $file.FullName.Substring($fixtureRoot.Length + 1)
        if ($relative -match '(^|[\\/])(baseline-packages|bin|obj)([\\/]|$)') { continue }
        $destination = Join-Path $consumer $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    $escapedFeed = [Security.SecurityElement]::Escape($localFeed)
    [IO.File]::WriteAllText((Join-Path $runRoot 'NuGet.Config'),
        ('<configuration><packageSources><clear/><add key="isolated-local-feed" value="{0}"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>' -f $escapedFeed))
    $sourceHashes = SourceHashes
    WriteJson (Join-Path $runRoot 'fixed-consumer-source-hashes.json') $sourceHashes
    $baselineOutput = BuildConsumer 'baseline' $BaselineVersion
    RunConsumer 'baseline' 'baseline' $baselineOutput
    $configurationHash = Hash (Join-Path $runRoot 'baseline-persisted-settings.json')
    $summary['BaselinePassed'] = $true

    $currentOutput = BuildConsumer 'upgraded-source' $CurrentVersion
    RunConsumer 'upgraded-source' 'upgrade' $currentOutput
    $beforeSourceJson = ConvertTo-Json -InputObject $sourceHashes -Compress -Depth 5
    $afterSourceJson = ConvertTo-Json -InputObject (SourceHashes) -Compress -Depth 5
    if ($beforeSourceJson -cne $afterSourceJson) {
        throw 'Consumer source changed between baseline and upgraded compilation.'
    }
    $summary.FixedConsumerSource = $true
    $summary['RecompiledConsumerPassed'] = $true

    $binaryOutput = Join-Path $runRoot 'old-binary-with-current-framework'
    [IO.Directory]::CreateDirectory($binaryOutput) | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $baselineOutput) { Copy-Item -LiteralPath $item.FullName -Destination $binaryOutput -Recurse }
    foreach ($id in $packageIds) { Copy-Item -LiteralPath (Join-Path $currentOutput ($id + '.dll')) -Destination (Join-Path $binaryOutput ($id + '.dll')) -Force }
    $currentWorker = Join-Path $currentOutput 'native-worker'
    if (Test-Path -LiteralPath $currentWorker -PathType Container) {
        # Worker files are part of the new framework package deployment, never a rebuild of the old consumer.
        $binaryWorker = Join-Path $binaryOutput 'native-worker'
        [IO.Directory]::CreateDirectory($binaryWorker) | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $currentWorker -File) {
            $deployed = Join-Path $binaryWorker $file.Name
            Copy-Item -LiteralPath $file.FullName -Destination $deployed -Force
            if ((Hash $deployed) -ne (Hash $file.FullName)) { throw ('Old-binary worker deployment changed package content: ' + $file.Name) }
        }
    }
    $consumerBinaryEvidence = @('UpgradeFixture.Domain', 'UpgradeFixture.Application', 'UpgradeFixture.Runner') | ForEach-Object {
        $name = $_ + '.dll'; $before = Hash (Join-Path $baselineOutput $name); $after = Hash (Join-Path $binaryOutput $name)
        if ($before -ne $after) { throw ('The old consumer binary was modified: ' + $name) }
        [pscustomobject]@{ File = $name; BaselineSha256 = $before; UpgradedDirectorySha256 = $after }
    }
    WriteJson (Join-Path $runRoot 'old-consumer-binary-hashes.json') $consumerBinaryEvidence
    RunConsumer 'upgraded-binary' 'upgrade' $binaryOutput
    $summary.OldConsumerBinaryUnchanged = $true
    $summary['ExistingBinaryPassed'] = $true
    if ((Hash (Join-Path $runRoot 'baseline-persisted-settings.json')) -ne $configurationHash) { throw 'Upgrade modified the baseline configuration document.' }
    $summary['PersistedConfigurationSha256'] = $configurationHash

    $beforeApiPath = Join-Path $runRoot 'baseline-evidence/public-api.json'
    $afterApiPath = Join-Path $runRoot 'upgraded-binary-evidence/public-api.json'
    $comparisonPath = Join-Path $runRoot 'public-api-comparison.json'
    $oldConsumer = Join-Path $baselineOutput 'UpgradeFixture.Runner.dll'
    InvokeDotnet 'public-api-compare' @($oldConsumer, 'api-compare', $beforeApiPath, $afterApiPath, $comparisonPath)
    # A deliberately removed signature must fail, so an empty/no-op API guard cannot produce green evidence.
    $beforeApi = Get-Content -Raw -LiteralPath $beforeApiPath | ConvertFrom-Json
    $negativePath = Join-Path $runRoot 'api-negative-control.json'
    WriteJson $negativePath @($beforeApi | Select-Object -Skip 1)
    $negativeReport = Join-Path $runRoot 'api-negative-control-result.json'
    InvokeDotnet 'public-api-negative-control' @($oldConsumer, 'api-compare', $beforeApiPath, $negativePath, $negativeReport) 2
    $negative = Get-Content -Raw -LiteralPath $negativeReport | ConvertFrom-Json
    if ($negative.Passed -or @($negative.MissingOrChanged).Count -ne 1) { throw 'The public API guard failed its removed-member negative control.' }
    $summary['PublicApiNegativeControlPassed'] = $true
    $summary.PublicApiCompatible = $true
    $summary.Passed = $true
}
catch { $summary['Error'] = $_.Exception.Message; throw }
finally {
    $summary['FinishedUtc'] = [DateTimeOffset]::UtcNow.ToString('O')
    WriteJson (Join-Path $runRoot 'upgrade-summary.json') $summary
    WriteJson (Join-Path $ArtifactsPath 'latest-run.json') ([ordered]@{ Passed = $summary.Passed; Summary = (Join-Path $runRoot 'upgrade-summary.json') })
}
Write-Output ('UPGRADE_VERIFIED: ' + $BaselineVersion + ' -> ' + $CurrentVersion + '; evidence=' + $runRoot)
