<#
    Erzeugt eine eigenständige Windows-Fassung von Pixel Backup.
    Aufruf aus dem Projektstamm:

        powershell -ExecutionPolicy Bypass -File .\packaging\windows\publish.ps1
        powershell -ExecutionPolicy Bypass -File .\packaging\windows\publish.ps1 -Runtime win-arm64

    Ergebnis: .\publish\PixelBackup.exe – eine einzelne Datei ohne vorinstalliertes .NET.
    Rund 46 MiB: die eingebettete Laufzeit ist komprimiert (EnableCompressionInSingleFile
    steht im Projekt), sonst wären es 96 MiB. Der erste Start dauert dadurch
    etwa eine Zehntelsekunde länger, jeder weitere ist gleich schnell.
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Output  = "publish"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "Das .NET-SDK fehlt. Bezugsquelle: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    exit 1
}

Write-Host "Baue Pixel Backup für $Runtime ..." -ForegroundColor Cyan

dotnet publish (Join-Path $root "src\PixelBackup.App") `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
    -o (Join-Path $root $Output)

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Fertig: $(Join-Path $root $Output)\PixelBackup.exe" -ForegroundColor Green
Write-Host "adb wird beim ersten Start selbst nachgeladen, falls es fehlt."
