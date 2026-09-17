<#
.SYNOPSIS
    TRWhisper projesini derler veya çalıştırır.
#>

param (
    [switch]$Run,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$proj = Join-Path $root "src\TRWhisper\TRWhisper.csproj"

$dotnet = "$env:LocalAppData\Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

if ($Publish) {
    Write-Host "[*] Proje Single-File / Self-Contained olarak paketleniyor..." -ForegroundColor Yellow
    & $dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root "publish")
    if (Test-Path (Join-Path $root "dist\TRWhisper")) {
        Copy-Item (Join-Path $root "publish\*") (Join-Path $root "dist\TRWhisper\") -Recurse -Force
    }
    Write-Host "[+] Paketleme tamamlandı: $(Join-Path $root 'dist\TRWhisper')" -ForegroundColor Green
} elseif ($Run) {
    Write-Host "[*] TRWhisper başlatılıyor..." -ForegroundColor Cyan
    & $dotnet run --project $proj
} else {
    Write-Host "[*] TRWhisper derleniyor..." -ForegroundColor Cyan
    & $dotnet build $proj -c Release
    Write-Host "[+] Derleme tamamlandı." -ForegroundColor Green
}
