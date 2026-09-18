param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z_][A-Za-z0-9_.]*$')][string]$Name,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [string]$DotNet
)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $smartRoot '.template.config\template.json'))) {
    throw 'Generate from the maintained Smart template source, not from a generated project.'
}
if (-not $DotNet) {
    $DotNet = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $DotNet)) { $DotNet = (Get-Command dotnet -ErrorAction Stop).Source }
}
$destination = [IO.Path]::GetFullPath($OutputPath)
if ((Test-Path -LiteralPath $destination) -and @(Get-ChildItem -LiteralPath $destination -Force).Count -gt 0) {
    throw 'Output directory must be new or empty.'
}
# Installing the workspace directly recursively discovers templates inside old validation
# checkouts. Stage only deliverable source into a unique root and use a fresh private hive.
$run = Join-Path $smartRoot ('artifacts\template-install\' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $run 'source'
$hive = Join-Path $run 'hive'
function Copy-TemplateSource([string]$From, [string]$To) {
    [IO.Directory]::CreateDirectory($To) | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $From -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
        if ($item.PSIsContainer) {
            if ($item.Name -in @('bin','obj','.git','.vs','.tools','artifacts','logs','data','TestResults','external') -or $item.Name -like 'smoke-*') { continue }
            Copy-TemplateSource $item.FullName (Join-Path $To $item.Name)
        }
        else {
            if ($item.Name -eq 'config.local.json' -or $item.Extension -in @('.user','.suo')) { continue }
            Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $To $item.Name)
        }
    }
}
Copy-TemplateSource $smartRoot $source
& $DotNet new install $source --force --debug:custom-hive $hive
if ($LASTEXITCODE -ne 0) { throw 'Template installation failed.' }
& $DotNet new smart-script -n $Name -o $destination --debug:custom-hive $hive
if ($LASTEXITCODE -ne 0) { throw 'Project generation failed.' }
$desktop = Join-Path $destination ("template\$Name.Desktop\$Name.Desktop.csproj")
[xml]$project = Get-Content -LiteralPath $desktop
if ($project.Project.PropertyGroup.UseWPF -ne 'true' -or -not (Test-Path -LiteralPath (Join-Path (Split-Path $desktop) 'MainWindow.xaml'))) {
    throw 'Generated project did not contain the WPF desktop shell.'
}
Write-Output ('PROJECT_GENERATED ' + $destination)
