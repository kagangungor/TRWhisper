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

# İndirilen her dosya sabit bir SHA-256 özetiyle doğrulanır. HTTPS yalnızca aktarımı
# korur; yayın varlığı kaynağında değiştirilirse (hesap ele geçirme, bozuk ayna) tek
# koruma bu pindir. Özetler: GitHub Releases API (asset digest), NVIDIA CUDA redist
# manifesti (redistrib_13.0.x.json) ve WhisperModelManager.cs kataloğu.
$WhisperZipHashes = @{
    "whisper-bin-x64.zip"               = "C2A4B60EDB11F7E11A9191FFB50929535527D4D91C9903DBE3E554583BBBC63D"
    "whisper-cublas-12.4.0-bin-x64.zip" = "C1B17166E1E31A91CC8E9C1F910D3785E3CE757BB2958BF9DCE13FDB4880005F"
}

# Hugging Face adresleri commit'e sabitlenmiştir: "main" değişirse özet tutmazdı.
$WhisperHfCommit = "5359861c739e955e79d9a303bcbc70fb988958b1"
$VadHfCommit = "9ffd54a1e1ee413ddf265af9913beaf518d1639b"

$ModelHashes = @{
    "large-v3-turbo-q5_0" = "394221709CD5AD1F40C46E6031CA61BCE88931E6E088C188294C6D5A55FFA7E2"
    "small"               = "1BE3A9B2063867B937E64E2EC7483364A79917E157FA98C5D94B5C1FFFEA987B"
    "base"                = "60ED5BC3DD14EEA856493D334349B405782DDCAF0028D4B5DF4088345FBA2EFE"
    "tiny"                = "BE07E048E1E599AD46341C8D2A135645097A538221678B7ACDD1B1919C6E1B21"
    "medium"              = "6C14D5ADEE5F86394037B4E4E8B59F1673B6CEE10E3CF0B11BBDBEE79C156208"
    "large-v3"            = "64D182B440B98D5203C4F9BD541544D84C605196C4F7B845DFA11FB23594D1E2"
}

$VadSha = "2AA269B785EEB53A82983A20501DDF7C1D9C48E33AB63A41391AC6C9F7FB6987"

# İndirir ve SHA-256 tutmazsa dosyayı silip durur (fail-closed).
function Invoke-VerifiedDownload {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$OutFile,
        [Parameter(Mandatory = $true)][string]$Sha256
    )

    Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing

    $actual = (Get-FileHash $OutFile -Algorithm SHA256).Hash
    if ($actual -ne $Sha256.ToUpperInvariant()) {
        Remove-Item $OutFile -Force -ErrorAction SilentlyContinue
        throw "SHA-256 dogrulamasi BASARISIZ: $Url (beklenen $($Sha256.ToUpperInvariant()), gelen $actual). Dosya guvenlik gerekcesiyle silindi."
    }

    Write-Host "[+] SHA-256 doğrulandı: $(Split-Path $OutFile -Leaf)" -ForegroundColor Green
}

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
        if (-not $WhisperZipHashes.ContainsKey($zipName)) {
            throw "$zipName icin SHA-256 ozeti tanimli degil; dogrulanmamis ikili indirilmeyecek."
        }
        Invoke-VerifiedDownload -Url $zipUrl -OutFile $tempZip -Sha256 $WhisperZipHashes[$zipName]
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

if (-not $ModelHashes.ContainsKey($Model)) {
    throw "Bilinmeyen model: '$Model'. SHA-256 ozeti tanimli olmayan model indirilmez. Desteklenenler: $($ModelHashes.Keys -join ', ')"
}

if (-not (Test-Path $modelPath)) {
    Write-Host "[*] $modelFileName modeli Hugging Face üzerinden indiriliyor (~465 MB)..." -ForegroundColor Yellow
    $modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/$WhisperHfCommit/$modelFileName"

    try {
        Invoke-VerifiedDownload -Url $modelUrl -OutFile $modelPath -Sha256 $ModelHashes[$Model]
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
    $vadUrl = "https://huggingface.co/ggml-org/whisper-vad/resolve/$VadHfCommit/$vadFileName"

    try {
        Invoke-VerifiedDownload -Url $vadUrl -OutFile $vadPath -Sha256 $VadSha
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

        # Özetler NVIDIA redist manifestlerinden alınmıştır
        # (redistrib_13.0.2.json -> cuda_cudart 13.0.96, redistrib_13.0.1.json -> libcublas 13.0.2.14).
        $redist = @(
            @{ Name = "cudart"; Url = "https://developer.download.nvidia.com/compute/cuda/redist/cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-13.0.96-archive.zip"; Sha256 = "A2ED875F9997AA24904FB70CC9DB3ACD9308433CDE99BC8E63EC1271C9DA31B4" },
            @{ Name = "cublas"; Url = "https://developer.download.nvidia.com/compute/cuda/redist/libcublas/windows-x86_64/libcublas-windows-x86_64-13.0.2.14-archive.zip"; Sha256 = "B03FD06FB14FA33E27FC441433FE74CCEE9738B62C62A08D39960CB0CE9F1D14" }
        )

        try {
            foreach ($pkg in $redist) {
                $tempZip = Join-Path $env:TEMP "trwhisper-cuda13-$($pkg.Name).zip"
                $tempDir = Join-Path $env:TEMP "trwhisper-cuda13-$($pkg.Name)"
                Invoke-VerifiedDownload -Url $pkg.Url -OutFile $tempZip -Sha256 $pkg.Sha256
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
