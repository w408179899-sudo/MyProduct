param([Parameter(Mandatory=$true)][string]$ManifestPath)
$ErrorActionPreference = 'Stop'
$ManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path
$payload = Split-Path -Parent $ManifestPath
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.SchemaVersion -ne 1 -or $manifest.Configuration -ne 'Release' -or $manifest.RuntimeIdentifier -ne 'win-x64' -or
    $manifest.HardwareLibrariesIncluded -ne $false -or $manifest.ConfigurationIncluded -ne $false) { throw 'Unexpected application manifest contract.' }
function Resolve-PayloadPath([string]$RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) { throw 'Manifest paths must be relative.' }
    $full = [IO.Path]::GetFullPath((Join-Path $payload $RelativePath))
    if (-not $full.StartsWith($payload + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escaped its payload.' }
    return $full
}
$expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $manifest.Files) {
    $path = Resolve-PayloadPath $file.Path
    if (-not $expected.Add($path)) { throw "Duplicate manifest file: $($file.Path)" }
    $actual = Get-Item -LiteralPath $path
    if ($actual.Length -ne $file.Length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Sha256) {
        throw "Published file does not match its manifest: $($file.Path)"
    }
}
foreach ($item in Get-ChildItem -LiteralPath $payload -Recurse -Force) {
    $relative = $item.FullName.Substring($payload.Length + 1).Replace('\', '/')
    if ($relative -match '(^|/)(data|logs|config|configuration|settings|\.tools|external|\.git)(/|$)' -or
        $item.Name -match '^(accounts?|config(\.local)?|appsettings(\.[^.]+)?|dma|kmbox(-net)?|device-leases)\.json$' -or
        $item.Name -match '^(vmm(sharp)?|leechcore[^.]*|FTD[^.]*|kmNet[^.]*|dbghelp|symsrv|tinylz4)\.dll$' -or
        $item.Extension -in @('.log', '.jsonl', '.nupkg')) { throw "Deployment-only data or hardware native files leaked into publication: $relative" }
    if (-not $item.PSIsContainer -and $item.FullName -ne $ManifestPath -and -not $expected.Contains($item.FullName)) {
        throw "Unlisted published file: $relative"
    }
}
if (@($manifest.Targets).Count -ne 2 -or @($manifest.Targets | Where-Object { $_.Name -eq 'Console' }).Count -ne 1 -or
    @($manifest.Targets | Where-Object { $_.Name -eq 'Desktop' }).Count -ne 1) { throw 'Publication requires exactly one Console and one Desktop host.' }
foreach ($target in $manifest.Targets) {
    $hostDirectory = Resolve-PayloadPath $target.Directory
    foreach ($component in @(
        [pscustomobject]@{ Directory = $hostDirectory; Assembly = $target.Assembly; EntryPoint = $target.EntryPoint },
        [pscustomobject]@{ Directory = (Join-Path $hostDirectory $target.NativeWorker.Directory); Assembly = $target.NativeWorker.Assembly; EntryPoint = $target.NativeWorker.EntryPoint }
    )) {
        $name = [IO.Path]::GetFileNameWithoutExtension($component.Assembly)
        foreach ($required in @($component.Assembly, $component.EntryPoint, "$name.deps.json", "$name.runtimeconfig.json")) {
            $path = [IO.Path]::GetFullPath((Join-Path $component.Directory $required))
            if (-not $expected.Contains($path)) { throw "Missing application/worker publication file: $path" }
        }
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $component.Directory $component.Assembly)).ProductVersion
        if ($version.Split('+')[0] -ne $manifest.FrameworkVersion) { throw "Published component version mismatch: $name ($version)" }
        $runtime = Get-Content -LiteralPath (Join-Path $component.Directory "$name.runtimeconfig.json") -Raw | ConvertFrom-Json
        $isSelfContained = @($runtime.runtimeOptions.includedFrameworks).Count -gt 0 -and $null -ne $runtime.runtimeOptions.includedFrameworks
        if ($isSelfContained -ne $manifest.SelfContained) { throw "Runtime deployment mode mismatch: $name" }
    }
}
Write-Output ("APPLICATION_MANIFEST_VERIFIED files=$($expected.Count) " + $ManifestPath)
