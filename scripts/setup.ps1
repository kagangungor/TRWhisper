<#
.SYNOPSIS
    TRWhisper için gerekli yerel whisper.cpp binary'sini ve GGML modelini indirir.
#>

param (
    [string]$Model = "large-v3-turbo-q5_0", # small, large-v3-turbo-q5_0, medium
    [switch]$Cuda                           # NVIDIA GPU için CUDA 12.4 derlemesini kur (~640 MB)
)

# Test edilmiş whisper.cpp derlemesi (v1.9.3). "latest" kullanılmaz: en son sürümlerin
# bazılarında indirilebilir paket bulunmuyor (404).
$whisperBuild = "b4938"

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
$cudaDll = Join-Path $toolsDir "ggml-cuda.dll"

$hasBinary = (Test-Path $whisperCli) -or (Test-Path $whisperMain)
if (-not $hasBinary -or ($Cuda -and -not (Test-Path $cudaDll))) {
    $zipName = if ($Cuda) { "whisper-cublas-12.4.0-bin-x64.zip" } else { "whisper-bin-x64.zip" }
    $sizeInfo = if ($Cuda) { "~640 MB" } else { "~8 MB" }
    Write-Host "[*] whisper.cpp $whisperBuild Windows x64 binary indiriliyor ($zipName, $sizeInfo)..." -ForegroundColor Yellow
    $zipUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/$whisperBuild/$zipName"
    $tempZip = Join-Path $env:TEMP $zipName

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
        Write-Host "[-] Otomatik binary indirme başarısız: $($_.Exception.Message)" -ForegroundColor Red
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
        Write-Host "[-] Model indirme hatası: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "[!] Modeli manuel olarak şu adresten indirebilirsiniz: $modelUrl" -ForegroundColor Yellow
    }
} else {
    Write-Host "[+] Whisper modeli ($modelFileName) zaten mevcut." -ForegroundColor Green
}

# 4. Silero VAD modeli (sessiz kayıtta uydurma metni engeller)
$vadFileName = "ggml-silero-v6.2.0.bin"
$vadPath = Join-Path $toolsDir $vadFileName

if (-not (Test-Path $vadPath)) {
    Write-Host "[*] $vadFileName VAD modeli indiriliyor (~1 MB)..." -ForegroundColor Yellow
    $vadUrl = "https://huggingface.co/ggml-org/whisper-vad/resolve/main/$vadFileName"

    try {
        Invoke-WebRequest -Uri $vadUrl -OutFile $vadPath
        Write-Host "[+] VAD modeli başarıyla indirildi: $vadPath" -ForegroundColor Green
    }
    catch {
        Write-Host "[-] VAD modeli indirme hatası: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "[!] VAD modelini manuel olarak şu adresten indirebilirsiniz: $vadUrl" -ForegroundColor Yellow
    }
} else {
    Write-Host "[+] VAD modeli ($vadFileName) zaten mevcut." -ForegroundColor Green
}

# 5. CUDA 13 runtime DLL'leri (Whisper.net'in GPU yolu için)
# Whisper.net 1.9.1'in CUDA derlemesi CUDA 13'e bağlıdır ve NuGet paketi bu DLL'leri
# getirmez. Eksikse uygulama sessizce CPU'ya düşer (dikte başına ~14 sn). Sürümler
# whisper build'i gibi sabitlenmiştir; "latest" indeksleri değişebiliyor.
$cuda13Dir = Join-Path $rootPath "tools\cuda13"
$cuda13Files = @("cudart64_13.dll", "cublas64_13.dll", "cublasLt64_13.dll")

if ($Cuda) {
    $missing = $cuda13Files | Where-Object { -not (Test-Path (Join-Path $cuda13Dir $_)) }
    if ($missing.Count -gt 0) {
        if (-not (Test-Path $cuda13Dir)) { New-Item -ItemType Directory -Path $cuda13Dir -Force | Out-Null }
        Write-Host "[*] CUDA 13 runtime DLL'leri NVIDIA redist'ten indiriliyor (~385 MB)..." -ForegroundColor Yellow

        $redist = @(
            @{ Name = "cudart"; Url = "https://developer.download.nvidia.com/compute/cuda/redist/cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-13.0.96-archive.zip" },
            @{ Name = "cublas"; Url = "https://developer.download.nvidia.com/compute/cuda/redist/libcublas/windows-x86_64/libcublas-windows-x86_64-13.0.2.14-archive.zip" }
        )

        try {
            foreach ($pkg in $redist) {
                $tempZip = Join-Path $env:TEMP "trwhisper-cuda13-$($pkg.Name).zip"
                $tempDir = Join-Path $env:TEMP "trwhisper-cuda13-$($pkg.Name)"
                Invoke-WebRequest -Uri $pkg.Url -OutFile $tempZip -UseBasicParsing
                Expand-Archive -Path $tempZip -DestinationPath $tempDir -Force
                Get-ChildItem $tempDir -Recurse -File -Include $cuda13Files |
                    ForEach-Object { Copy-Item $_.FullName $cuda13Dir -Force }
                Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
                Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
            Write-Host "[+] CUDA 13 runtime DLL'leri hazır: $cuda13Dir" -ForegroundColor Green
        }
        catch {
            Write-Host "[-] CUDA 13 runtime indirme hatası: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "[!] Bu dosyalar olmadan uygulama CPU'da çalışır (çok yavaş)." -ForegroundColor Yellow
        }
    } else {
        Write-Host "[+] CUDA 13 runtime DLL'leri zaten mevcut." -ForegroundColor Green
    }
} else {
    Write-Host "[!] -Cuda verilmedi: Whisper.net CPU'da çalışacak (dikte başına ~14 sn)." -ForegroundColor Yellow
    Write-Host "    NVIDIA GPU'nuz varsa: scripts\setup.ps1 -Cuda" -ForegroundColor Yellow
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Kurulum Tamamlandı! " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
