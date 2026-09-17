param(
    [ValidateSet('win-x64')][string]$Runtime = 'win-x64',
    [ValidateSet('Release')][string]$Configuration = 'Release',
    [switch]$SelfContained,
    [string]$ArtifactsPath,
    [string]$NativeWorkerProject
)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess) { throw 'Publish and smoke verification require Windows x64.' }
[xml]$props = Get-Content -LiteralPath (Join-Path $smartRoot 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.SmartFrameworkVersion
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Directory.Build.props must declare a semantic SmartFrameworkVersion.' }
if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $smartRoot 'artifacts\application-publish' }
if (-not [IO.Path]::IsPathRooted($ArtifactsPath)) { $ArtifactsPath = Join-Path $smartRoot $ArtifactsPath }
$run = Join-Path ([IO.Path]::GetFullPath($ArtifactsPath)) ($version + '\' + [Guid]::NewGuid().ToString('N'))
$build = Join-Path $run 'build'
$payload = Join-Path $run 'applications'
[IO.Directory]::CreateDirectory($payload) | Out-Null
if (-not $NativeWorkerProject) { $NativeWorkerProject = Join-Path $smartRoot 'src\Smart.Dma.Worker\Smart.Dma.Worker.csproj' }
if (-not [IO.Path]::IsPathRooted($NativeWorkerProject)) { $NativeWorkerProject = Join-Path $smartRoot $NativeWorkerProject }
$NativeWorkerProject = (Resolve-Path -LiteralPath $NativeWorkerProject).Path
function Get-AssemblyName([string]$Project) {
    [xml]$xml = Get-Content -LiteralPath $Project
    $declared = $xml.SelectSingleNode('//AssemblyName')
    if ($declared) { return $declared.InnerText }
    return [IO.Path]::GetFileNameWithoutExtension($Project)
}
function Publish-Project([string]$Project, [string]$Destination) {
    & $dotnetPath publish $Project -c $Configuration -r $Runtime --self-contained ($SelfContained.IsPresent.ToString().ToLowerInvariant()) `
        --artifacts-path $build --output $Destination --nologo -p:SmartUsePackages=false "-p:SmartFrameworkVersion=$version" `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:UseAppHost=true
    if ($LASTEXITCODE -ne 0) { throw "Application publish failed: $Project" }
}
$targets = @()
$workerName = Get-AssemblyName $NativeWorkerProject
foreach ($kind in @('Console', 'Desktop')) {
    # sourceName replacement keeps these paths correct in generated projects.
    $project = Join-Path $smartRoot ("template\SampleProject.$kind\SampleProject.$kind.csproj")
    $name = Get-AssemblyName $project
    $directory = $kind.ToLowerInvariant()
    $destination = Join-Path $payload $directory
    Publish-Project $project $destination
    # Publish explicitly even when the adapter already supplies development worker files.
    # The release worker must use the same RID and runtime deployment mode as its host.
    Publish-Project $NativeWorkerProject (Join-Path $destination 'native-worker')
    $targets += [ordered]@{
        Name = $kind; Directory = $directory; Assembly = "$name.dll"; EntryPoint = "$name.exe"
        NativeWorker = [ordered]@{ Directory = 'native-worker'; Assembly = "$workerName.dll"; EntryPoint = "$workerName.exe" }
    }
}
$files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path = $_.FullName.Substring($payload.Length + 1).Replace('\', '/'); Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest = Join-Path $payload 'publish-manifest.json'
[ordered]@{
    SchemaVersion = 1; FrameworkVersion = $version; Configuration = $Configuration; RuntimeIdentifier = $Runtime
    SelfContained = $SelfContained.IsPresent; CreatedUtc = [DateTime]::UtcNow.ToString('O'); Targets = $targets; Files = $files
    HardwareLibrariesIncluded = $false; ConfigurationIncluded = $false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifest -Encoding UTF8
& (Join-Path $PSScriptRoot 'Assert-ApplicationPublish.ps1') -ManifestPath $manifest
Write-Output ("APPLICATIONS_PUBLISHED " + $manifest)
