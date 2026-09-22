<#
.SYNOPSIS
    TRWhisper'ın tek dosyalık kurulum paketini (TRWhisper-Setup-x.y.z.exe) üretir.

.DESCRIPTION
    1. Uygulamayı self-contained tek dosya olarak paketler (scripts\build.ps1 -Publish).
    2. Kuruluma gömülecek whisper.cpp CPU derlemesini ve Silero VAD modelini indirir
       (installer\cache, SHA-256 doğrulamalı, bir kez indirilir).
    3. Inno Setup ile kurulum dosyasını derler ve SHA-256 özetini yazar.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
#>

param (
    [string]$Version,      # boş ise csproj'daki <Version> kullanılır
    [switch]$SkipPublish   # publish\TRWhisper.exe güncelse yeniden derleme
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$installerDir = Join-Path $root "installer"
$cacheDir = Join-Path $installerDir "cache"
$publishExe = Join-Path $root "publish\TRWhisper.exe"
$csproj = Join-Path $root "src\TRWhisper\TRWhisper.csproj"

# Kuruluma gömülecek kaynaklar (setup.ps1 ile aynı whisper.cpp sürümü: b4938 / v1.9.3)
$whisperBuild = "b4938"
$cpuZipUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/$whisperBuild/whisper-bin-x64.zip"
$cpuZipSha = "C2A4B60EDB11F7E11A9191FFB50929535527D4D91C9903DBE3E554583BBBC63D"
$vadUrl = "https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/ggml-silero-v6.2.0.bin"
$vadSha = "2AA269B785EEB53A82983A20501DDF7C1D9C48E33AB63A41391AC6C9F7FB6987"

function Write-Step($text) { Write-Host "[*] $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "[+] $text" -ForegroundColor Green }

function Get-FileOrDownload {
    param([string]$Path, [string]$Url, [string]$Sha256)

    if (Test-Path $Path) {
        if ((Get-FileHash $Path -Algorithm SHA256).Hash -eq $Sha256) {
            Write-Ok "Önbellekte mevcut: $(Split-Path $Path -Leaf)"
            return
        }
        Write-Host "[!] Önbellekteki dosya bozuk, yeniden indiriliyor: $(Split-Path $Path -Leaf)" -ForegroundColor Yellow
        Remove-Item $Path -Force
    }

    Write-Step "İndiriliyor: $(Split-Path $Path -Leaf)"
    Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing
    $actual = (Get-FileHash $Path -Algorithm SHA256).Hash
    if ($actual -ne $Sha256) {
        Remove-Item $Path -Force
        throw "SHA-256 uyuşmadı: $Url (beklenen $Sha256, gelen $actual)"
    }
    Write-Ok "İndirildi ve doğrulandı: $(Split-Path $Path -Leaf)"
}

# 1. Sürüm
if (-not $Version) {
    $Version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Version) { throw "Sürüm bulunamadı: $csproj içinde <Version> yok ve -Version verilmedi." }
Write-Ok "Sürüm: $Version"

# 2. Uygulamayı paketle
if (-not $SkipPublish) {
    Write-Step "Uygulama paketleniyor (self-contained tek dosya)..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build.ps1") -Publish
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 -Publish başarısız oldu." }
}

if (-not (Test-Path $publishExe)) { throw "Bulunamadı: $publishExe (önce scripts\build.ps1 -Publish çalıştırın)" }

# Native kütüphaneler gömülmediyse exe açılmıyor; sağlıklı derleme ~170,6 MB (162,7 MiB).
$exeBytes = (Get-Item $publishExe).Length
if ($exeBytes -lt 168000000) {
    throw ("publish\TRWhisper.exe yalnızca {0:N0} bayt. Native kütüphaneler gömülmemiş olabilir (-p:IncludeNativeLibrariesForSelfExtract=true). Kurulum üretilmedi." -f $exeBytes)
}
Write-Ok ("Uygulama hazır: {0:N1} MB" -f ($exeBytes / 1MB))

# 3. CUDA 13 paketini hazırla (trwhisper-cuda13-win-x64.zip)
$outputDir = Join-Path $installerDir "Output"
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$cudaZipOut = Join-Path $outputDir "trwhisper-cuda13-win-x64.zip"

$cudaFilesExist = (Test-Path (Join-Path $root "publish\cublas64_13.dll")) -and
                  (Test-Path (Join-Path $root "publish\cublasLt64_13.dll")) -and
                  (Test-Path (Join-Path $root "publish\cudart64_13.dll")) -and
                  (Test-Path (Join-Path $root "publish\runtimes\cuda\win-x64"))

$cudaSha = "2D397A7077760C74EFA2838F42D4E8B502D8D3CCAB9259844BBCFE06F5EB922F"
$cudaSize = 544457658

if ($cudaFilesExist) {
    Write-Step "CUDA 13 paketi hazırlanıyor (trwhisper-cuda13-win-x64.zip)..."
    $cudaStaging = Join-Path $env:TEMP "trwhisper_cuda13_stg_$([Guid]::NewGuid().ToString('N'))"
    if (Test-Path $cudaStaging) { Remove-Item $cudaStaging -Recurse -Force }
    New-Item -ItemType Directory -Path $cudaStaging -Force | Out-Null

    Copy-Item (Join-Path $root "publish\cublas*.dll") $cudaStaging -Force
    Copy-Item (Join-Path $root "publish\cudart*.dll") $cudaStaging -Force
    $stgRuntimes = Join-Path $cudaStaging "runtimes\cuda\win-x64"
    New-Item -ItemType Directory -Path $stgRuntimes -Force | Out-Null
    Copy-Item (Join-Path $root "publish\runtimes\cuda\win-x64\*") $stgRuntimes -Force

    if (Test-Path $cudaZipOut) { Remove-Item $cudaZipOut -Force }
    Compress-Archive -Path (Get-ChildItem $cudaStaging).FullName -DestinationPath $cudaZipOut -CompressionLevel Optimal
    Remove-Item $cudaStaging -Recurse -Force

    $cudaSize = (Get-Item $cudaZipOut).Length
    $cudaSha = (Get-FileHash $cudaZipOut -Algorithm SHA256).Hash
    Set-Content -Path "$cudaZipOut.sha256" -Value "$cudaSha *trwhisper-cuda13-win-x64.zip" -Encoding ASCII
    Write-Ok ("CUDA 13 paketi hazır: {0:N1} MB" -f ($cudaSize / 1MB))
} else {
    Write-Host "[!] CUDA runtime dosyaları publish klasöründe eksik, CUDA paketi üretilmedi." -ForegroundColor Yellow
}

# 4. Kuruluma gömülecek dosyalar
New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null

$cpuZip = Join-Path $cacheDir "whisper-bin-x64-$whisperBuild.zip"
Get-FileOrDownload -Path $cpuZip -Url $cpuZipUrl -Sha256 $cpuZipSha

$cpuDir = Join-Path $cacheDir "cpu"
if (-not (Test-Path (Join-Path $cpuDir "Release\whisper-cli.exe"))) {
    Write-Step "whisper.cpp CPU derlemesi açılıyor..."
    Expand-Archive -Path $cpuZip -DestinationPath $cpuDir -Force
}
Write-Ok "CPU motoru hazır: $cpuDir\Release"

Get-FileOrDownload -Path (Join-Path $cacheDir "ggml-silero-v6.2.0.bin") -Url $vadUrl -Sha256 $vadSha

# 5. Inno Setup ile derle
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 bulunamadı. Kurmak için: winget install JRSoftware.InnoSetup"
}

Write-Step "Kurulum dosyası derleniyor (Inno Setup)..."
$isccArgs = @("/DAppVersion=$Version")
if ($cudaSha) {
    $isccArgs += "/DCudaZipSha=$cudaSha"
    $isccArgs += "/DCudaZipSize=$cudaSize"
}
$isccArgs += (Join-Path $installerDir "TRWhisper.iss")

$maxRetries = 3
$compiled = $false
for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {
    & $iscc $isccArgs
    if ($LASTEXITCODE -eq 0) {
        $compiled = $true
        break
    }
    if ($attempt -lt $maxRetries) {
        Write-Host "[!] Inno Setup derlemesi başarısız oldu (muhtemel antivirüs tarama kilidi). 3 saniye sonra tekrar deneniyor ($attempt/$maxRetries)..." -ForegroundColor Yellow
        Start-Sleep -Seconds 3
    }
}
if (-not $compiled) { throw "Inno Setup derlemesi başarısız oldu (ISCC çıkış kodu $LASTEXITCODE)." }

# 6. Özet + SHA-256
$setupExe = Join-Path $installerDir "Output\TRWhisper-Setup-$Version.exe"
if (-not (Test-Path $setupExe)) { throw "Kurulum dosyası oluşmadı: $setupExe" }

$hash = (Get-FileHash $setupExe -Algorithm SHA256).Hash
Set-Content -Path "$setupExe.sha256" -Value "$hash *TRWhisper-Setup-$Version.exe" -Encoding ASCII

Write-Host ""
Write-Ok "Kurulum dosyası hazır:"
Write-Host "    $setupExe" -ForegroundColor White
Write-Host "    Boyut : $([math]::Round((Get-Item $setupExe).Length / 1MB, 1)) MB" -ForegroundColor White
Write-Host "    SHA256: $hash" -ForegroundColor White
if (Test-Path $cudaZipOut) {
    Write-Host "    CUDA  : $cudaZipOut" -ForegroundColor White
    Write-Host "    Boyut : $([math]::Round((Get-Item $cudaZipOut).Length / 1MB, 1)) MB" -ForegroundColor White
    Write-Host "    SHA256: $cudaSha" -ForegroundColor White
}
