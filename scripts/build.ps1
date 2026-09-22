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

# dist\TRWhisper'da derleme çıktısı OLMAYAN, asla silinmemesi/ezilmemesi gereken dosyalar.
# dictionary.json publish çıktısında YOKTUR; kullanıcının makinesinde ilk çalıştırmada
# config.json'ın yanında oluşur. Korunmazsa .json uzantısı yüzünden artık sanılıp silinir.
$ProtectedFiles = @("config.json", "dictionary.json", "BENIOKU.txt", "Install.ps1", "Kurulum.bat")
# dist\TRWhisper'da tamamen korunan alt klasörler (yüzlerce MB model içerir).
$ProtectedDirs = @("tools")
# dist KÖKÜNDE artık-temizliğine dahil edilecek uzantılar. Kökte tanımadığımız bir
# uzantı varsa (kullanıcının koyduğu bir dosya olabilir) dokunulmaz.
$ManagedRootExtensions = @(".exe", ".dll", ".pdb", ".json", ".xml", ".config")

# Bir klasörün altındaki tüm dosyaları "göreli yol -> tam yol" tablosuna çevirir.
# PowerShell hashtable anahtarları büyük/küçük harf duyarsızdır; Windows yolları için doğru davranış.
function Get-RelativeFileMap {
    param([string]$BasePath)

    $map = @{}
    if (-not (Test-Path $BasePath)) { return $map }

    $basePrefix = (Resolve-Path $BasePath).Path.TrimEnd('\') + '\'
    Get-ChildItem -Path $BasePath -Recurse -File -Force | ForEach-Object {
        $map[$_.FullName.Substring($basePrefix.Length)] = $_.FullName
    }
    return $map
}

# publish\ ağacını dist\TRWhisper'a yönetilen bir ayna olarak uygular:
#   - publish'teki her dosya kopyalanır (config.json hariç: varsa korunur)
#   - publish'te ARTIK bulunmayan eski derleme çıktıları (kökteki DLL/EXE'ler ve
#     runtimes\ altındaki native'ler) silinir
#   - korunan dosya/klasörlere hiç dokunulmaz
function Sync-PublishToDist {
    param([string]$PublishDir, [string]$DistDir)

    $publishMap = Get-RelativeFileMap -BasePath $PublishDir
    if ($publishMap.Count -eq 0) {
        throw "publish klasörü boş: $PublishDir"
    }
    $distMap = Get-RelativeFileMap -BasePath $DistDir

    $copied = 0
    $removed = @()
    $configPreserved = $false

    # --- 1) Kopyalama ---
    foreach ($rel in $publishMap.Keys) {
        $source = $publishMap[$rel]
        $target = Join-Path $DistDir $rel

        if ($rel -eq "config.json") {
            # Yeni ayarların görülebilmesi için örnek dosya HER ZAMAN tazelenir.
            Copy-Item $source (Join-Path $DistDir "config.example.json") -Force
            # Kullanıcının mevcut ayarları asla ezilmez; yalnızca yoksa oluşturulur.
            if (Test-Path $target) {
                $configPreserved = $true
                continue
            }
        }

        $targetParent = Split-Path $target -Parent
        if (-not (Test-Path $targetParent)) {
            New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
        }
        Copy-Item $source $target -Force
        $copied++
    }

    # --- 2) Artık temizliği ---
    foreach ($rel in $distMap.Keys) {
        if ($publishMap.ContainsKey($rel)) { continue }
        if ($rel -eq "config.example.json") { continue }

        $segments = $rel -split '\\'
        $isRootLevel = $segments.Count -eq 1

        if ($isRootLevel) {
            if ($ProtectedFiles -contains $rel) { continue }
            # Kökte yalnızca tanıdığımız derleme-çıktısı uzantıları silinebilir.
            $ext = [System.IO.Path]::GetExtension($rel).ToLowerInvariant()
            if ($ManagedRootExtensions -notcontains $ext) { continue }
        }
        else {
            # Korunan klasörlerin (tools\ vb.) altına asla dokunma.
            if ($ProtectedDirs -contains $segments[0]) { continue }
        }

        Remove-Item $distMap[$rel] -Force
        $removed += $rel
    }

    # --- 3) Temizlik sonrası boşalan klasörleri kaldır (korunanlar hariç) ---
    Get-ChildItem -Path $DistDir -Recurse -Directory -Force |
        Sort-Object { $_.FullName.Length } -Descending |
        Where-Object {
            $top = $_.FullName.Substring($DistDir.TrimEnd('\').Length).TrimStart('\').Split('\')[0]
            ($ProtectedDirs -notcontains $top) -and
            (-not (Get-ChildItem -Path $_.FullName -Recurse -File -Force))
        } |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force }

    Write-Host "    kopyalanan dosya   : $copied" -ForegroundColor DarkGray
    if ($configPreserved) {
        Write-Host "    config.json        : KORUNDU (config.example.json güncellendi)" -ForegroundColor DarkGray
    } else {
        Write-Host "    config.json        : yoktu, oluşturuldu" -ForegroundColor DarkGray
    }
    if ($removed.Count -gt 0) {
        Write-Host "    temizlenen artık   : $($removed.Count)" -ForegroundColor DarkGray
        $removed | Sort-Object | ForEach-Object { Write-Host "      - $_" -ForegroundColor DarkGray }
    } else {
        Write-Host "    temizlenen artık   : yok" -ForegroundColor DarkGray
    }
}

if ($Publish) {
    Write-Host "[*] Proje Single-File / Self-Contained olarak paketleniyor..." -ForegroundColor Yellow
    $publishDir = Join-Path $root "publish"

    # publish\ ZORUNLU olarak sıfırdan üretilir. Artımlı publish, dosya publish\ içinden
    # elle silinmişse onu geri KOPYALAMAZ (MSBuild kopyaladığını sanır; config.json bu
    # şekilde kayboldu). Eksik bir publish\ ağacı aşağıdaki eşitlemede dist'teki sağlam
    # dosyaların "artık" sanılıp silinmesine yol açar.
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    & $dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız oldu (exit $LASTEXITCODE)." }

    $distDir = Join-Path $root "dist\TRWhisper"
    if (Test-Path $distDir) {
        Write-Host "[*] dist\TRWhisper eşitleniyor..." -ForegroundColor Yellow
        Sync-PublishToDist -PublishDir $publishDir -DistDir $distDir
    }
    Write-Host "[+] Paketleme tamamlandı: $distDir" -ForegroundColor Green
} elseif ($Run) {
    Write-Host "[*] TRWhisper başlatılıyor..." -ForegroundColor Cyan
    & $dotnet run --project $proj
} else {
    Write-Host "[*] TRWhisper derleniyor..." -ForegroundColor Cyan
    & $dotnet build $proj -c Release
    Write-Host "[+] Derleme tamamlandı." -ForegroundColor Green
}
