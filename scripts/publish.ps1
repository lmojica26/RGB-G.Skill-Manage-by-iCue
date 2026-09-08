#requires -Version 5.1
<#
  Builds a self-contained x64 portable release into ./dist.
  Usage:  ./scripts/publish.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

$common = @(
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=false', '-p:_IsPublishing=true'
)

Write-Host 'Publishing CLI...' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src/GSkillCue.Cli/GSkillCue.Cli.csproj')  @common -o (Join-Path $dist 'cli')

Write-Host 'Publishing tray app...' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src/GSkillCue.Tray/GSkillCue.Tray.csproj') @common -o (Join-Path $dist 'tray')

Copy-Item (Join-Path $root 'vendor/pawnio/installer/PawnIO_setup.exe') $dist
Copy-Item (Join-Path $root 'README.md') $dist
Copy-Item (Join-Path $root 'docs/phase0-findings.md') $dist -ErrorAction SilentlyContinue

Write-Host "`nDone. Portable build in $dist" -ForegroundColor Green
Write-Host 'Install PawnIO_setup.exe once, then run cli\gskillcue.exe or tray\GSkillCueTray.exe as Administrator.'
