[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    dotnet restore MagicCar.SiteDoctor.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet build MagicCar.SiteDoctor.slnx -c Release --no-restore -p:EnableRecovery=false
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    dotnet test tests/MagicCar.SiteDoctor.Core.Tests/MagicCar.SiteDoctor.Core.Tests.csproj `
        -c Release --no-build --logger 'trx;LogFileName=core-tests.trx' --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
} finally { Pop-Location }
