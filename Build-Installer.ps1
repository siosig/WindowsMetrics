#requires -Version 7
<#
.SYNOPSIS
  Publishes WindowsMetrics framework-dependent and packages it into a per-user MSI.
.OUTPUTS
  artifacts/installer/WindowsMetrics-<version>-x64.msi
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root 'artifacts/publish-fdd'
$buildDir = Join-Path $root 'artifacts/build-fdd'
$outDir = Join-Path $root 'artifacts/installer'
$csproj = Join-Path $root 'WindowsMetrics/WindowsMetrics.csproj'

$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

# Command-line properties override the self-contained single-file defaults in the csproj.
dotnet publish $csproj -c $Configuration -o $publishDir `
    -p:PublishSelfContained=false -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false -r win-x64 --self-contained false `
    "-p:OutputPath=$buildDir\\"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

dotnet build (Join-Path $root 'WindowsInstaller/WindowsInstaller.wixproj') -c $Configuration `
    -p:ProductVersion=$version "-p:PublishDir=$publishDir\" -o $outDir
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }

Get-ChildItem $outDir -Filter *.msi
