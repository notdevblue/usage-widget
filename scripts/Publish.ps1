[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$packageName = if ($FrameworkDependent) { "UsageMonitor-$Runtime-runtime-required" } else { "UsageMonitor-$Runtime" }
$outputDirectory = Join-Path $projectRoot "artifacts\$packageName"
$archivePath = Join-Path $projectRoot "artifacts\$packageName.zip"
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$compression = if ($FrameworkDependent) { 'false' } else { 'true' }
Push-Location $projectRoot
try {
    dotnet publish src/UsageMonitor/UsageMonitor.csproj -c Release -r $Runtime --self-contained $selfContained `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=$compression -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -p:MSBuildEnableWorkloadResolver=false -o $outputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputDirectory
    if (-not $FrameworkDependent) {
        $packageCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages' }
        $licenseDirectory = Join-Path $outputDirectory 'licenses'
        [IO.Directory]::CreateDirectory($licenseDirectory) | Out-Null
        $runtimePackage = Join-Path $packageCache "microsoft.netcore.app.runtime.$Runtime\8.0.32"
        $desktopPackage = Join-Path $packageCache "microsoft.windowsdesktop.app.runtime.$Runtime\8.0.32"
        Copy-Item -LiteralPath (Join-Path $runtimePackage 'LICENSE.TXT') -Destination (Join-Path $licenseDirectory 'dotnet-runtime-LICENSE.txt')
        Copy-Item -LiteralPath (Join-Path $runtimePackage 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $licenseDirectory 'dotnet-runtime-THIRD-PARTY-NOTICES.txt')
        Copy-Item -LiteralPath (Join-Path $desktopPackage 'LICENSE') -Destination (Join-Path $licenseDirectory 'windows-desktop-LICENSE.txt')
    }
    Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $archivePath -Force
    $packageHash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
    "$($packageHash.Hash.ToLowerInvariant())  $packageName.zip" | Set-Content -LiteralPath "$archivePath.sha256" -Encoding ascii
    Get-Item -LiteralPath $archivePath | Select-Object FullName, Length
}
finally {
    Pop-Location
}
