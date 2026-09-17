param([string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$cacheRoot = Join-Path $smartRoot 'artifacts\package-consumer-cache'
$testArguments = @()
$assetPaths = @(
    (Join-Path $smartRoot 'tests\Smart.Runtime.Tests\obj\project.assets.json'),
    (Join-Path $smartRoot 'tests\SampleProject.Tests\obj\project.assets.json')
)
if ($ArtifactsPath) {
    if (-not [IO.Path]::IsPathRooted($ArtifactsPath)) { $ArtifactsPath = Join-Path $smartRoot $ArtifactsPath }
    $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
    $frameworkArtifacts = Join-Path $ArtifactsPath 'framework'
    $consumerArtifacts = Join-Path $ArtifactsPath 'consumer'
    & (Join-Path $PSScriptRoot 'Pack.ps1') -ArtifactsPath $frameworkArtifacts
    $packageFeed = Join-Path $frameworkArtifacts 'packages'
    $cacheRoot = Join-Path $ArtifactsPath 'package-consumer-cache'
    # NuGet treats additional sources as a local property, so a command-line property alone does not override
    # Directory.Build.targets. Import its normal package conversion, then restrict this consumer's extra feed.
    $consumerTargets = Join-Path $ArtifactsPath 'package-consumer.targets'
    $escapedTargets = [Security.SecurityElement]::Escape((Join-Path $smartRoot 'Directory.Build.targets'))
    $escapedFeed = [Security.SecurityElement]::Escape($packageFeed)
    $targetsXml = '<Project><Import Project="{0}" /><PropertyGroup><RestoreAdditionalProjectSources>{1}</RestoreAdditionalProjectSources></PropertyGroup></Project>' -f $escapedTargets, $escapedFeed
    [IO.File]::WriteAllText($consumerTargets, $targetsXml)
    $testArguments = @('--artifacts-path', $consumerArtifacts, "-p:DirectoryBuildTargetsPath=$consumerTargets",
        '--logger', 'trx', '--results-directory', (Join-Path $ArtifactsPath 'test-results'))
    $assetPaths = @(
        (Join-Path $consumerArtifacts 'obj\Smart.Runtime.Tests\project.assets.json'),
        (Join-Path $consumerArtifacts 'obj\SampleProject.Tests\project.assets.json')
    )
}
else { & (Join-Path $PSScriptRoot 'Pack.ps1') }
$previous = if (Test-Path -LiteralPath $cacheRoot) { Get-ChildItem -LiteralPath $cacheRoot -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1 }
$cache = Join-Path $cacheRoot ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($cache) | Out-Null
if ($previous) {
    foreach ($dependency in Get-ChildItem -LiteralPath $previous.FullName -Directory | Where-Object { $_.Name -notlike 'smart.*' }) {
        Copy-Item -LiteralPath $dependency.FullName -Destination $cache -Recurse
    }
}
& $dotnetPath test (Join-Path $smartRoot 'Smart.slnx') -c Release --nologo -p:SmartUsePackages=true "-p:RestorePackagesPath=$cache" @testArguments
if ($LASTEXITCODE -ne 0) { throw 'Package consumer tests failed.' }
foreach ($assetPath in $assetPaths) {
    $assets = Get-Content -Raw -LiteralPath $assetPath | ConvertFrom-Json
    $framework = @($assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'Smart.*' })
    if ($framework.Count -eq 0 -or @($framework | Where-Object { $_.Value.type -ne 'package' }).Count -gt 0) {
        throw ('Expected actual framework package references: ' + $assetPath)
    }
}
Write-Output 'PACKAGE_CONSUMERS_VERIFIED'
