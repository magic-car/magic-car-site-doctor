[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $publishDirectory = Join-Path $projectRoot 'artifacts/win-x64'
    if (Test-Path $publishDirectory) { Remove-Item $publishDirectory -Recurse -Force }
    dotnet publish src/MagicCar.SiteDoctor.Windows/MagicCar.SiteDoctor.Windows.csproj `
        -c Release -r win-x64 --self-contained true -p:EnableRecovery=false -p:RestoreLockedMode=true `
        -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    Copy-Item README-FIRST-RUN.txt $publishDirectory
    Copy-Item THIRD-PARTY-NOTICES.md $publishDirectory
    $configuration = Get-Content (Join-Path $publishDirectory 'site-doctor.json') -Raw | ConvertFrom-Json
    if ($configuration.recovery.enabled -ne $false) { throw 'Refusing to package a build with recovery configured on.' }
    foreach ($required in @('MagicCar.SiteDoctor.exe', 'MagicCar.SiteDoctor.Core.dll', 'coreclr.dll', 'PresentationFramework.dll', 'WebView2Loader.dll', 'Microsoft.Web.WebView2.Core.dll', 'Ui/index.html', 'Ui/app.js', 'Ui/app.css')) {
        if (-not (Test-Path (Join-Path $publishDirectory $required))) { throw "Required artifact missing: $required" }
    }
    $zipPath = Join-Path $projectRoot 'artifacts/MagicCar.SiteDoctor-win-x64.zip'
    $pendingZip = Join-Path $projectRoot 'artifacts/MagicCar.SiteDoctor-win-x64.pending.zip'
    Compress-Archive -Path "$publishDirectory/*" -DestinationPath $pendingZip -Force
    $archive = [System.IO.Compression.ZipFile]::OpenRead($pendingZip)
    try {
        foreach ($entry in $archive.Entries) {
            $stream = $entry.Open()
            try { $stream.CopyTo([System.IO.Stream]::Null) } finally { $stream.Dispose() }
        }
        if ($null -eq $archive.GetEntry('MagicCar.SiteDoctor.exe')) { throw 'ZIP is missing its executable.' }
    } finally { $archive.Dispose() }
    Move-Item $pendingZip $zipPath -Force
    $hash = Get-FileHash $zipPath -Algorithm SHA256
    ($hash.Hash.ToLowerInvariant() + '  MagicCar.SiteDoctor-win-x64.zip') |
        Set-Content (Join-Path $projectRoot 'artifacts/MagicCar.SiteDoctor-win-x64.sha256') -Encoding ascii
    $hash
} finally { Pop-Location }
