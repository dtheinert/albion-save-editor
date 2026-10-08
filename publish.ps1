<#
  Builds the Windows desktop app as one self-contained exe and a zip for sharing:
    dist\AlbionSaveEditor.exe
    dist\AlbionSaveEditor-win-x64.zip   (exe + README.txt + LICENSE-AlbLib.txt)
  Usage:  powershell -ExecutionPolicy Bypass -File .\publish.ps1 [-Configuration Release] [-ArtifactsPath <dir>]
  -ArtifactsPath puts intermediate build output elsewhere (useful when a running editor locks bin\).
#>
param(
    [string]$Configuration = 'Release',
    [string]$ArtifactsPath = ''
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$publishDir = Join-Path $root 'obj\publish\win-x64'

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force $dist | Out-Null

$publishArgs = @(
    'publish', (Join-Path $root 'src\AlbionSaveEditor.Desktop\AlbionSaveEditor.Desktop.csproj'),
    '-c', $Configuration, '-r', 'win-x64', '--self-contained',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none',
    '-o', $publishDir
)
if ($ArtifactsPath) { $publishArgs += @('--artifacts-path', $ArtifactsPath) }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$exe = Join-Path $dist 'AlbionSaveEditor.exe'
Copy-Item (Join-Path $publishDir 'AlbionSaveEditor.Desktop.exe') $exe -Force

$staging = Join-Path $root 'obj\publish\zip'
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item $exe $staging
Copy-Item (Join-Path $root 'packaging\README.txt') $staging
Copy-Item (Join-Path $root 'src\AlbLib.Core\LICENSE') (Join-Path $staging 'LICENSE-AlbLib.txt')
$zip = Join-Path $dist 'AlbionSaveEditor-win-x64.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip

$extra = Get-ChildItem $publishDir | Where-Object { $_.Name -ne 'AlbionSaveEditor.Desktop.exe' }
if ($extra) { Write-Warning ("Publish produced extra files (not shipped): " + ($extra.Name -join ', ')) }
'{0}  {1:N1} MB' -f $exe, ((Get-Item $exe).Length / 1MB)
'{0}  {1:N1} MB' -f $zip, ((Get-Item $zip).Length / 1MB)
