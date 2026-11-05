<#
.SYNOPSIS
    Builds artifacts\PromptLibrary-Setup.msi.

.DESCRIPTION
    Publishes the single-file Release exe (src\PromptLibrary.App -> publish\PromptLibrary.exe), builds the WiX v5
    installer project (installer\PromptLibrary.Installer.wixproj; WiX comes from NuGet, nothing is installed
    machine-wide) and copies the MSI to artifacts\PromptLibrary-Setup.msi.

    The installer's version is the exe's file version: set <Version> in src\PromptLibrary.App\PromptLibrary.App.csproj,
    or pass -Version to override it for one build.

.EXAMPLE
    .\build\build-installer.ps1
.EXAMPLE
    .\build\build-installer.ps1 -Version 1.0.1 -OutputMsi C:\Temp\PromptLibrary-Setup-1.0.1.msi
#>
param(
    [string] $Version,
    [string] $OutputMsi
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputMsi) { $OutputMsi = Join-Path $root 'artifacts\PromptLibrary-Setup.msi' }

# A one-off version builds into its own folders so the normal publish\ output is left alone.
$publishDir = Join-Path $root 'publish'
$versionArgs = @()
if ($Version) {
    $publishDir = Join-Path $root "artifacts\publish-$Version"
    $versionArgs = @("-p:Version=$Version")
}

Write-Host "Publishing PromptLibrary.exe to $publishDir"
dotnet publish (Join-Path $root 'src\PromptLibrary.App') -c Release -o $publishDir @versionArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

Write-Host 'Building the installer'
$wixproj = Join-Path $root 'installer\PromptLibrary.Installer.wixproj'
$installerOut = Join-Path $root 'installer\bin\Release'
dotnet build $wixproj -c Release --no-incremental -o $installerOut "-p:AppPublishDir=$publishDir"
if ($LASTEXITCODE -ne 0) { throw "Installer build failed ($LASTEXITCODE)." }

New-Item -ItemType Directory -Force (Split-Path -Parent $OutputMsi) | Out-Null
Copy-Item (Join-Path $installerOut 'PromptLibrary-Setup.msi') $OutputMsi -Force
Write-Host "Installer: $OutputMsi"
