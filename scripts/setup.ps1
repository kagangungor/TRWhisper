<#
.SYNOPSIS
    TRWhisper için gerekli yerel whisper.cpp binary'sini ve GGML modelini indirir.
#>

param (
    [string]$Model = "small" # tiny, base, small, medium
)

$ErrorActionPreference = "Stop"

$rootPath = Resolve-Path (Join-Path $PSScriptRoot "..")
$toolsDir = Join-Path $rootPath "tools\whisper"
$dictationDir = [Environment]::ExpandEnvironmentVariables("%USERPROFILE%\Dictation")

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " TRWhisper Otomatik Kurulum ve İndirme " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Dizinleri oluştur
if (-not (Test-Path $toolsDir)) {
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
    Write-Host "[+] Klasör oluşturuldu: $toolsDir" -ForegroundColor Green
}

if (-not (Test-Path $dictationDir)) {
    New-Item -ItemType Directory -Path $dictationDir -Force | Out-Null
    Write-Host "[+] Dikte günlüğü klasörü oluşturuldu: $dictationDir" -ForegroundColor Green
}

# 2. whisper.cpp binary kontrolü ve indirme
$whisperCli = Join-Path $toolsDir "whisper-cli.exe"
$whisperMain = Join-Path $toolsDir "main.exe"

if (-not (Test-Path $whisperCli) -and -not (Test-Path $whisperMain)) {
    Write-Host "[*] whisper.cpp Windows x64 binary indiriliyor..." -ForegroundColor Yellow
    $zipUrl = "https://github.com/ggerganov/whisper.cpp/releases/latest/download/whisper-bin-x64.zip"
    $tempZip = Join-Path $env:TEMP "whisper-bin-x64.zip"

    try {
        Invoke-WebRequest -Uri $zipUrl -OutFile $tempZip -UseBasicParsing
        Expand-Archive -Path $tempZip -DestinationPath $toolsDir -Force
        Remove-Item $tempZip -Force
        $nestedRelease = Join-Path $toolsDir "Release"
        if (Test-Path $nestedRelease) {
            Move-Item -Path "$nestedRelease\*" -Destination $toolsDir -Force
            Remove-Item $nestedRelease -Force -Recurse
        }
        Write-Host "[+] whisper.cpp binary başarıyla indirildi ve açıldı." -ForegroundColor Green
    }
    catch {
        Write-Host "[-] Otomatik binary indirme başarısız: $($_.Message)" -ForegroundColor Red
        Write-Host "[!] Lütfen 'whisper-cli.exe' dosyasını manuel olarak '$toolsDir' içerisine kopyalayın." -ForegroundColor Yellow
    }
} else {
    Write-Host "[+] whisper binary zaten mevcut." -ForegroundColor Green
}

# 3. Whisper Modeli (ggml-small.bin) kontrolü ve indirme
$modelFileName = "ggml-$Model.bin"
$modelPath = Join-Path $toolsDir $modelFileName

if (-not (Test-Path $modelPath)) {
    Write-Host "[*] $modelFileName modeli Hugging Face üzerinden indiriliyor (~465 MB)..." -ForegroundColor Yellow
    $modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$modelFileName"

    try {
        # İlerleme çubuğu ile indirme
        Invoke-WebRequest -Uri $modelUrl -OutFile $modelPath
        Write-Host "[+] Model başarıyla indirildi: $modelPath" -ForegroundColor Green
    }
    catch {
        Write-Host "[-] Model indirme hatası: $($_.Message)" -ForegroundColor Red
        Write-Host "[!] Modeli manuel olarak şu adresten indirebilirsiniz: $modelUrl" -ForegroundColor Yellow
    }
} else {
    Write-Host "[+] Whisper modeli ($modelFileName) zaten mevcut." -ForegroundColor Green
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Kurulum Tamamlandı! " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
