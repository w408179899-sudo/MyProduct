param(
    [Parameter(Mandatory = $true)]
    [string]$ReactorConsole,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [switch]$Evaluation
)

$ErrorActionPreference = 'Stop'
$cli = (Resolve-Path -LiteralPath $ReactorConsole).Path
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'Roadhog.csproj'
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    throw 'Choose a new output directory for the protected build.'
}

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$build = Join-Path $repo ('.tmp\roadhog-protection-' + [Guid]::NewGuid().ToString('N'))
$inputFiles = Join-Path $build 'publish'
$protectedFiles = Join-Path $build 'protected'
New-Item -ItemType Directory -Path $protectedFiles -Force | Out-Null

dotnet publish $project -c Release -r win-x64 --self-contained false -o $inputFiles `
    -p:RoadhogProtectionBuild=true -p:RoadhogSkipDesktopCopy=true
if ($LASTEXITCODE -ne 0) { throw 'Roadhog publish failed.' }

$main = 'Roadhog.dll'
$libraries = @('SkillScheduling.dll', 'Hardware.KmBox.dll')
$owned = @($main) + $libraries
$log = Join-Path $build 'reactor.log'
$reactorArgs = @(
    '-file', (Join-Path $inputFiles $main),
    '-files', (($libraries | ForEach-Object { Join-Path $inputFiles $_ }) -join ';'),
    '-targetfile', (Join-Path $protectedFiles '<AssemblyFileName>'),
    '-target_runtime', 'dotnet', '-obfuscation', '1', '-obfuscate_public_types', '0',
    '-exclude_fields', '1', '-exclude_properties', '1', '-exclude_events', '1',
    '-exclude_enums', '1', '-exclude_serializable_types', '1', '-mapping_file', '1',
    '-stringencryption', '1', '-virtualization', '1', '-virtualization_protect_other_features', '0',
    '-declarative_protection', '1', '-suppressildasm', '0', '-verbose', '-nodialog', '-logfile', $log
)
if (-not $Evaluation) { $reactorArgs += '-licensed' }
& $cli @reactorArgs
if ($LASTEXITCODE -ne 0) { throw "Reactor protection failed (exit $LASTEXITCODE). See $log" }

foreach ($name in $owned) {
    $original = Get-FileHash -LiteralPath (Join-Path $inputFiles $name)
    $protected = Get-FileHash -LiteralPath (Join-Path $protectedFiles $name)
    if ($original.Hash -eq $protected.Hash) { throw "Assembly was not protected: $name" }
}

New-Item -ItemType Directory -Path $output | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $inputFiles -Recurse -File) {
    $relative = $file.FullName.Substring($inputFiles.Length + 1)
    if ($relative -in $owned -or $file.Extension -in '.pdb', '.lib' -or $file.Name -eq '.gitattributes') {
        continue
    }
    $target = Join-Path $output $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
foreach ($name in $owned) {
    Copy-Item -LiteralPath (Join-Path $protectedFiles $name) -Destination $output
}

$config = Join-Path $output 'config'
foreach ($directory in @($config, (Join-Path $config 'paths'), (Join-Path $config 'profiles'), (Join-Path $output 'logs'))) {
    New-Item -ItemType Directory -Path $directory | Out-Null
}
Set-Content -Encoding ASCII -LiteralPath (Join-Path $config 'accounts.json') -Value '{"version":1,"accounts":[]}'
Set-Content -Encoding ASCII -LiteralPath (Join-Path $config 'kmbox-net.json') -Value '{"ip":"127.0.0.1","port":49412,"mac":"00000000"}'
if ($Evaluation) { Write-Warning 'Evaluation build for local testing only. Do not distribute.' }
Write-Output "Protected runtime: $output"
Write-Output "Private protection log and mapping files: $build"
