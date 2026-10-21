[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    dotnet build UsageMonitor.sln -c Release -p:MSBuildEnableWorkloadResolver=false
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/UsageMonitor.Core.Tests/UsageMonitor.Core.Tests.csproj -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Core verification failed.' }
    dotnet run --project tests/UsageMonitor.Services.Tests/UsageMonitor.Services.Tests.csproj -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Settings verification failed.' }
    dotnet run --project tests/UsageMonitor.Presentation.Tests/UsageMonitor.Presentation.Tests.csproj -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Presentation verification failed.' }
}
finally {
    Pop-Location
}
