param([Parameter(Mandatory=$true)][string]$DotNet)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $smartRoot '.template.config\template.json'))) {
    Write-Output 'TEMPLATE_GENERATION_NOT_APPLICABLE: this is a generated consumer; normal build and architecture tests remain enabled.'
    exit 0
}
$destination = Join-Path $smartRoot ('artifacts\template-smoke\' + [Guid]::NewGuid().ToString('N'))
$hive = Join-Path $smartRoot '.tools\template-hive'
& $DotNet new install $smartRoot --force --debug:custom-hive $hive
if ($LASTEXITCODE -ne 0) { throw 'Template installation failed.' }
& $DotNet new smart-script -n SmokeProject -o $destination --debug:custom-hive $hive
if ($LASTEXITCODE -ne 0) { throw 'Template generation failed.' }
if (Test-Path -LiteralPath (Join-Path $destination '.template.config\template.json')) {
    throw 'A generated consumer must not retain the source-template marker.'
}
# This fixture lives only in the disposable generated consumer. Verify users can add ordinary
# domain concepts while retaining the complete dependency and raw-read architecture checks.
$consumerDomain = Join-Path $destination 'template\SmokeProject.Domain\ConsumerHealth.cs'
@'
namespace SmokeProject.Domain;
public sealed record Health(int Current, int Maximum);
'@ | Set-Content -LiteralPath $consumerDomain -Encoding UTF8
& (Join-Path $PSScriptRoot 'Add-ConsumerFixture.ps1') -Destination $destination
& $DotNet build (Join-Path $destination 'Smart.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Generated project build failed.' }
& $DotNet test (Join-Path $destination 'Smart.slnx') -c Release --no-build --nologo --logger trx --results-directory (Join-Path $destination 'artifacts\template-test-results')
if ($LASTEXITCODE -ne 0) { throw 'Generated project tests failed.' }
& $DotNet run --project (Join-Path $destination 'template\SmokeProject.Console') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Generated mock flow failed.' }
$consumerTemplateCheck = @(& (Join-Path $destination 'scripts\Test-Template.ps1') -DotNet $DotNet)
if ($LASTEXITCODE -ne 0 -or -not ($consumerTemplateCheck -match '^TEMPLATE_GENERATION_NOT_APPLICABLE:')) {
    throw 'Generated consumer must skip only the source-template generation check.'
}
Write-Output ("TEMPLATE_VERIFIED " + $destination)
