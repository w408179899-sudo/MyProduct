param([string]$PackageFeed, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
[xml]$props = Get-Content -LiteralPath (Join-Path $smartRoot 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.SmartFrameworkVersion
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Invalid framework version.' }
if (-not $PackageFeed) { $PackageFeed = Join-Path $smartRoot 'artifacts\packages' }
if (-not [IO.Path]::IsPathRooted($PackageFeed)) { $PackageFeed = Join-Path $smartRoot $PackageFeed }
$PackageFeed = (Resolve-Path -LiteralPath $PackageFeed).Path
if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $smartRoot 'artifacts\package-worker-verification' }
if (-not [IO.Path]::IsPathRooted($ArtifactsPath)) { $ArtifactsPath = Join-Path $smartRoot $ArtifactsPath }
$run = Join-Path ([IO.Path]::GetFullPath($ArtifactsPath)) ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$workerFiles = @('Smart.Dma.Worker.exe', 'Smart.Dma.Worker.dll', 'Smart.Dma.Worker.deps.json', 'Smart.Dma.Worker.runtimeconfig.json')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = Join-Path $PackageFeed ("Smart.Adapters.Dma.$version.nupkg")
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    foreach ($name in $workerFiles) {
        if ($entries -notcontains "tools/native-worker/$name") { throw "Missing packaged worker file: $name" }
    }
    if ($entries -notcontains 'buildTransitive/Smart.Adapters.Dma.targets') { throw 'Missing transitive worker deployment rules.' }
}
finally { $archive.Dispose() }
# This consumer cannot inherit the repository's source references or central package defaults.
foreach ($file in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
    [IO.File]::WriteAllText((Join-Path $run $file), '<Project />')
}
$project = Join-Path $run 'WorkerPackageConsumer.csproj'
[IO.File]::WriteAllText($project, ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><PackageReference Include="Smart.Adapters.Dma" Version="' + $version + '" /></ItemGroup></Project>'))
[IO.File]::WriteAllText((Join-Path $run 'Program.cs'), 'System.Console.WriteLine(typeof(Smart.Adapters.Dma.VmmTransport).FullName);')
$config = Join-Path $run 'NuGet.Config'
[IO.File]::WriteAllText($config, ('<configuration><packageSources><clear/><add key="local" value="' + [Security.SecurityElement]::Escape($PackageFeed) + '" /></packageSources></configuration>'))
$build = Join-Path $run 'build'
$cache = Join-Path $run 'cache'
$publish = Join-Path $run 'publish'
& $dotnetPath build $project -c Release --artifacts-path $build --configfile $config "-p:RestorePackagesPath=$cache" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Offline package worker consumer build failed.' }
& $dotnetPath publish $project -c Release --artifacts-path $build --no-build --no-restore --output $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Offline package worker consumer publication failed.' }
$restoredWorker = Join-Path $cache ("smart.adapters.dma\$version\tools\native-worker")
$originalRoot = $env:DOTNET_ROOT; $originalRootX64 = $env:DOTNET_ROOT_X64
$env:DOTNET_ROOT = Split-Path -Parent $dotnetPath; $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$records = @()
try {
    foreach ($stage in @(
        @{ Name = 'Build'; Directory = (Join-Path $build 'bin\WorkerPackageConsumer\release\native-worker') },
        @{ Name = 'Publish'; Directory = (Join-Path $publish 'native-worker') }
    )) {
        $files = @()
        foreach ($name in $workerFiles) {
            $hash = (Get-FileHash -LiteralPath (Join-Path $stage.Directory $name) -Algorithm SHA256).Hash
            if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $restoredWorker $name) -Algorithm SHA256).Hash) { throw "Package worker copy mismatch: $($stage.Name)/$name" }
            $files += [ordered]@{ Name = $name; Sha256 = $hash }
        }
        $stdout = Join-Path $run ($stage.Name + '.worker.stdout.log')
        $stderr = Join-Path $run ($stage.Name + '.worker.stderr.log')
        $process = Start-Process -FilePath (Join-Path $stage.Directory 'Smart.Dma.Worker.exe') -ArgumentList '--help' `
            -WorkingDirectory $stage.Directory -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        try {
            $childHandle = $process.Handle
            if (-not $process.WaitForExit(10000)) { $process.Kill(); throw 'Packaged worker help timed out.' }
            if ($process.ExitCode -ne 0 -or (Get-Content -LiteralPath $stdout -Raw) -notmatch 'no device is opened without its private pipe handshake') {
                throw "Packaged worker offline help failed: $($stage.Name). See $stderr"
            }
            $records += [ordered]@{ Stage = $stage.Name; Files = $files; ExitCode = $process.ExitCode; Mode = 'Help' }
        }
        finally { $process.Dispose() }
    }
}
finally { $env:DOTNET_ROOT = $originalRoot; $env:DOTNET_ROOT_X64 = $originalRootX64 }
[ordered]@{ FrameworkVersion = $version; PackageSha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash; Stages = $records } |
    ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $run 'package-worker-result.json') -Encoding UTF8
Write-Output ("PACKAGED_WORKER_VERIFIED " + $run)
