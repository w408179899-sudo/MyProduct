param(
    [Parameter(Mandatory=$true)][string]$Solution,
    [Parameter(Mandatory=$true)][string]$ArtifactsPath
)
$ErrorActionPreference = 'Stop'
$solutionPath = (Resolve-Path -LiteralPath $Solution).Path
$artifactsRoot = (Resolve-Path -LiteralPath $ArtifactsPath).Path
[xml]$solutionXml = Get-Content -LiteralPath $solutionPath
$pending = [Collections.Generic.Queue[string]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $solutionXml.SelectNodes('//Project[@Path]')) {
    $pending.Enqueue([IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $solutionPath) $entry.Path)))
}
$records = @()
while ($pending.Count -gt 0) {
    $projectPath = $pending.Dequeue()
    if (-not $seen.Add($projectPath)) { continue }
    [xml]$project = Get-Content -LiteralPath $projectPath
    foreach ($reference in $project.SelectNodes('//ProjectReference[@Include]')) {
        if ($reference.Include.Contains('$(')) { throw "Release verification requires a resolved project reference: $($reference.Include)" }
        $pending.Enqueue([IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $projectPath) $reference.Include)))
    }
    $projectName = [IO.Path]::GetFileNameWithoutExtension($projectPath)
    $assemblyName = $project.SelectSingleNode('//AssemblyName')
    if ($assemblyName) { $assemblyName = $assemblyName.InnerText } else { $assemblyName = $projectName }
    $projectBin = Join-Path $artifactsRoot ('bin\' + $projectName)
    if (-not (Test-Path -LiteralPath $projectBin)) { throw "Missing build output: $projectName" }
    $debug = @(Get-ChildItem -LiteralPath $projectBin -Directory | Where-Object { $_.Name -match '^debug($|_)' })
    if ($debug.Count -ne 0) { throw "Debug dependency output is forbidden in this fresh verification run: $projectName" }
    $assembly = Join-Path $projectBin ('release\' + $assemblyName + '.dll')
    if (-not (Test-Path -LiteralPath $assembly)) { throw "Missing Release assembly: $projectName" }
    $assemblyInfo = Join-Path $artifactsRoot ('obj\' + $projectName + '\release\' + $projectName + '.AssemblyInfo.cs')
    if (-not (Test-Path -LiteralPath $assemblyInfo) -or
        (Get-Content -LiteralPath $assemblyInfo -Raw) -notmatch 'AssemblyConfigurationAttribute\("Release"\)') {
        throw "Compiler-generated assembly configuration is not Release: $projectName"
    }
    $records += [ordered]@{ Project = $projectName; Configuration = 'Release'; Assembly = $assembly; Sha256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash }
}
if ($records.Count -eq 0) { throw 'No projects were verified.' }
$manifest = Join-Path $artifactsRoot 'release-build.json'
[ordered]@{ SchemaVersion = 1; Solution = [IO.Path]::GetFileName($solutionPath); Projects = $records } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifest -Encoding UTF8
Write-Output ("RELEASE_DEPENDENCIES_VERIFIED projects=$($records.Count) $manifest")
