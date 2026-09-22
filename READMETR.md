# TRWhisper: Windows 11 Yerel Bas-Konuş Dikte Uygulaması 🎙️

[🇹🇷 Türkçe](READMETR.md) | [🇬🇧 English](README.md)

[![Sürüm](https://img.shields.io/github/v/release/kagangungor/TRWhisper?color=blue&logo=github)](https://github.com/kagangungor/TRWhisper/releases/latest)
[![İndirmeler](https://img.shields.io/github/downloads/kagangungor/TRWhisper/total?color=green&logo=github)](https://github.com/kagangungor/TRWhisper/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20x64-0078D6?logo=windows11&logoColor=white)](https://github.com/kagangungor/TRWhisper)
[![Lisans](https://img.shields.io/github/license/kagangungor/TRWhisper?color=orange)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/9.0)

**TRWhisper**, Windows 11 için geliştirilmiş, Superwhisper alternatifi, tamamen yerel çalışan, sıfır telemetrili bir "bas-konuş" (push-to-talk) dikte uygulamasıdır. Türkçe dikte için ayarlanmıştır.

<p align="center">
  <img src="assets/Demo2.gif" alt="TRWhisper Demo" width="750">
</p>

---

## 🚀 Temel Özellikler

- **Modern Grafiksel Ayarlar Penceresi (WPF)**: Sistem tepsisinden tek tıkla açılan zengin ve modern ayarlar paneli; **Genel**, **Ses**, **Model**, **Kısayol**, **Sözlük**, **Yapay Zeka (LLM)** ve **Kapsül** sekmeleriyle tüm ayarları görsel olarak yönetme ve anında test etme.
- **Özel Sözlük & Jargon Desteği (Custom Dictionary)**: Kullanıcı tanımlı fonetik ve mesleki jargon eşlemeleri (`dictionary.json`). Sektörel terimler, özel isimler veya Whisper'ın karıştırabileceği teknik kelimeler kelime sınırı kurallarıyla otomatik olarak düzeltilir.
- **Gelişmiş Türkçe Metin Normalizasyonu**:
  - Cümle başı büyük harf uyumu ve noktalama kontrolleri.
  - de/da ve ki bağlaç düzeltmeleri.
  - Konuşma sırasındaki kekeleme ve tekrarlı kelimeleri ayıklama (ör. `ve ve ve` -> `ve`).
- **Bağlama Duyarlı (Context-Aware) LLM Modları & Uygulama Algılama**:
  - Farklı transkript modları: **Ham Metin**, **Temizle** (dolgu kelimeleri at), **Özetle**, **Resmi Dil** ve **Madde İmleri**.
  - Aktif ön plan uygulamasını otomatik algılama (`ForegroundAppDetector`): Kod editörleri (VS Code), e-posta , mesajlaşma (Discord, Slack) veya doküman editörlerine göre dil tonunu otomatik uyarlar.
  - **Donanım Seviyesinde Güvenli API Anahtarı Saklama**: Windows DPAPI (`Data Protection API`) ile şifrelenmiş güvenli depolama; API anahtarlarınız düz metin olarak saklanmaz.
- **Esnek Kısayol Tuşları & Eller Serbest (Hands-Free) Dikte**:
  - Klasik **Bas-Konuş (Push-to-Talk)** modu (varsayılan: Sağ Ctrl).
  - Tuşa basılı tutmadan konuşmanızı tamamlayınca sessizlik algılayıp otomatik bitiren **Eller Serbest (Hands-free toggle)** modu.
  - Özelleştirilebilir tetikleme ve LLM değiştirici tuşları (Sağ/Sol Ctrl, Shift, Alt, F tuşları vb.).
- **Whisper.net Yerleşik C# Motoru & Dinamik Model Yönetimi**:
  - `whisper-cli` haricinde süreç içi yüksek performanslı Whisper.net yerel çalışma zamanı entegrasyonu.
  - Ayarlar arayüzünden doğrudan model indirme ilerleme takibi.
  - Sistem boşta kaldığında bellek ve VRAM tasarrufu sağlayan otomatik model boşaltma zamanlayıcısı (`IdleTimeoutMinutes`).
- **Otomatik Yapıştırma & Akıllı Pano**:
  - Transkript metnini panoya kopyalar ve `Ctrl + V` simülasyonu ile imlecin bulunduğu alana yapıştırır.
  - İsteğe bağlı olarak yapıştırma sonrası panonun eski içeriğini otomatik geri yükleme seçeneği (`RestoreClipboard`).
- **Tamamen Çevrimdışı ve Gizli (STT)**: Yerel Whisper motoruyla çalışır. Sesiniz hiçbir sunucuya gönderilmez.
- **NVIDIA GPU Hızlandırma**: CUDA derlemesiyle **Large-v3 Turbo** modeli dikteyi işlemcideki ~23 saniye yerine ~2–3 saniyede çözer. GPU yoksa otomatik CPU moduna geçer.
- **Sessizlik Algılama ve Uydurma Metin Filtresi**:
  - Yerleşik **Silero VAD** özelliği konuşma olmayan kısımları atlar. Boş kayıtlarda uydurma metin yapıştırılmaz.
  - Whisper'ın bilinen hayalet altyazıları (`Altyazı M.K.`, `İzlediğiniz için teşekkür ederim.`) ve ses etiketleri (`[MÜZİK ÇALIYOR]`) filtrelenir.
- **Kayan Durum Kapsülü (Pill Overlay)**: Kayıt sırasında ses seviyesini ve durumunu, işlem sırasında çözümlenmeyi gösterir; iptal (✕) ve bitir (✓) düğmeleri içerir.
- **Windows Açılışında Başlatma (Autostart)**: Ayarlar menüsünden tek tıkla Windows başlangıcına eklenebilir.
- **Kapsamlı Test Paketi**: 87 adet otomatik birim testi ve XAML şablon duman testi (`uismoke`) ile yüksek kod kalitesi.
- **Yerel Günlük**: Her transkript zaman damgasıyla `%USERPROFILE%\Dictation\YYYY-MM.md` dosyasına kaydedilir. Günlük loglar `%USERPROFILE%\Dictation\trwhisper.log` dosyasına yazılır.

---

## 🛠️ Kurulum ve Gereksinimler

**Gereksinimler**
- Windows 11 x64
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (kaynak koddan derlemek için)
- İsteğe bağlı: GPU hızlandırma için güncel sürücülü (CUDA 12.4 destekli) bir NVIDIA ekran kartı

### ⚡ Kolay Kurulum (Önerilen): Tek Dosyalık Kurulum Sihirbazı

En son sürümü **[Releases](https://github.com/kagangungor/TRWhisper/releases/latest)** sayfasından
indirin (`TRWhisper-Setup-x.y.z.exe`, ~51 MB) ve çalıştırın. Yönetici şifresi gerekmez;
uygulama `%LOCALAPPDATA%\Programs\TRWhisper` klasörüne kurulur.

Sihirbaz Türkçe ve İngilizce'dir ve kuruluma başlamadan önce ne kurulacağını açıkça yazar:

| Bileşen | Durum |
|---|---|
| TRWhisper uygulaması (.NET 9 gömülü, ayrıca .NET gerekmez) | Her zaman (kurulum dosyasının içinde) |
| Silero VAD sessizlik modeli | Her zaman (kurulum dosyasının içinde) |
| Whisper **CPU** motoru | Kurulum dosyasının içinde — her bilgisayarda çalışır |
| Whisper **NVIDIA GPU (CUDA 12.4)** motoru | Seçime bağlı, ~640 MB indirilir (NVIDIA kartınız varsa otomatik önerilir) |
| **Large-v3 Turbo** modeli (~547 MB) | Seçime bağlı (Önerilen: en yüksek Türkçe doğruluğu, GPU'da çok hızlı) |
| **Small** modeli (~465 MB) | Seçime bağlı (Dengeli ve hızlı: yalnızca CPU kullanan sistemler için ideal) |
| **Base** modeli (~148 MB) | Seçime bağlı (Çok hızlı, hafif: pratik ve anlık notlar için) |
| **Tiny** modeli (~75 MB) | Seçime bağlı (Ultra hafif: en düşük bellek ve kaynak kullanımı) |
| **Medium** modeli (~1.5 GB) | Seçime bağlı (Yüksek doğruluk: standart tam model) |
| **Large-v3** modeli (~3.1 GB) | Seçime bağlı (En kapsamlı tam model: güçlü bir GPU gerektirir) |
| Microsoft Visual C++ 2015-2022 Runtime | Yalnızca bilgisayarınızda yoksa indirilip kurulur (tek seferlik UAC) |
| Masaüstü / Başlat menüsü kısayolu, Windows açılışında başlatma | Seçime bağlı |

- Uygulama içerisindeki **⚙️ Ayarlar > Model** sekmesinden bu modellerin tamamını dilediğiniz an tek tıkla indirebilir, silebilir ve sistem tepsisinden aralarında yeniden başlatma gerekmeksizin geçiş yapabilirsiniz.
- İndirilen her dosya **SHA-256** ile doğrulanır; zaten kurulu olan dosyalar yeniden indirilmez
  (kurulumu tekrar çalıştırıp motor/model seçiminizi değiştirebilirsiniz).
- Kaldırma: **Ayarlar > Uygulamalar > TRWhisper**. Dikte kayıtlarınızın (`%USERPROFILE%\Dictation`)
  silinip silinmeyeceği size sorulur.
- Sessiz kurulum (kurumsal dağıtım):
  ```powershell
  TRWhisper-Setup-1.0.0.exe /SILENT /ENGINE=cuda /MODELS=turbo,small /TASKS=desktopicon
  ```

> Kurulum dosyası imzasız olduğu için Windows SmartScreen "bilinmeyen yayımcı" uyarısı gösterebilir:
> **Daha fazla bilgi > Yine de çalıştır**. Dilerseniz [VirusTotal Tarama Raporu](https://www.virustotal.com/gui/file/c97e23fe0cd720c570e44370f74d49fc74c961f106eecf11e48080d217d979c2/detection)'nu inceleyebilirsiniz (Microsoft Defender, Kaspersky, Bitdefender dahil 70+ antivirüste tamamen temizdir).

---

### 🔧 Kaynaktan Kurulum (geliştiriciler için)

Aşağıdaki adımlar yalnızca projeyi kaynak koddan derleyecekseniz gereklidir.

### 1. Windows Mikrofon İzinleri
Windows 11'de mikrofon erişiminin açık olduğundan emin olun:
1. **Ayarlar (Win + I)** > **Gizlilik ve Güvenlik** > **Mikrofon**.
2. **"Mikrofon erişimi"** seçeneğini açık konuma getirin.
3. **"Masaüstü uygulamalarının mikrofonunuza erişmesine izin verin"** seçeneğinin **Açık** olduğundan emin olun.

### 2. Whisper Motoru ve Model Kurulumu (Otomatik)
Proje kök dizinindeyken PowerShell ile kurulum betiğini çalıştırın:

```powershell
# İşlemci (CPU) derlemesi + Large-v3 Turbo modeli + VAD modeli
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1

# Bunun yerine NVIDIA GPU (CUDA 12.4) derlemesi — NVIDIA ekran kartınız varsa önerilir
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Cuda

# İsteğe bağlı olarak diğer modelleri (small, base, tiny, medium, large-v3) indirmek için:
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Model small
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Model base
```

Bu betik:
- `tools\whisper\` ve `%USERPROFILE%\Dictation\` klasörlerini oluşturur.
- Test edilmiş `whisper.cpp` **v1.9.3** (derleme `b4938`) Windows x64 dosyalarını indirir: işlemci derlemesi (~8 MB) ya da `-Cuda` ile CUDA 12.4 derlemesi (~640 MB indirme, açılmış hali ~1,2 GB). Mevcut bir işlemci kurulumunda `-Cuda` ile tekrar çalıştırmak onu GPU derlemesine yükseltir.
- Seçilen modeli (varsayılan `large-v3-turbo-q5_0`) ve Silero VAD modelini indirir.
- Zaten mevcut olan dosyaları atlar.

| Dosya | Boyut | Amaç |
|---|---|---|
| `ggml-large-v3-turbo-q5_0.bin` | ~547 MB | Varsayılan model, en yüksek Türkçe doğruluğu (GPU önerilir) |
| `ggml-small.bin` | ~465 MB | İşlemcide (CPU) hızlı ve dengeli |
| `ggml-base.bin` | ~148 MB | Çok hızlı, hafif, düşük bellek kullanımı |
| `ggml-tiny.bin` | ~75 MB | Ultra hafif, minimum sistem kaynağı kullanımı |
| `ggml-medium.bin` | ~1.5 GB | Yüksek doğruluklu standart model |
| `ggml-large-v3.bin` | ~3.1 GB | En kapsamlı tam model (güçlü GPU gerektirir) |
| `ggml-silero-v6.2.0.bin` | ~1 MB | Ses etkinliği algılama (sessizlik filtresi) |

> **Manuel İndirmek İsterseniz:**
> - Whisper dosyaları ([b4938 sürümü](https://github.com/ggml-org/whisper.cpp/releases/tag/b4938)): `whisper-bin-x64.zip` (işlemci) veya `whisper-cublas-12.4.0-bin-x64.zip` (NVIDIA GPU) paketini indirip içindeki `Release` klasörünün **tüm dosyalarını** `tools\whisper\` içine çıkarın.
> - Modeller: [ggml-large-v3-turbo-q5_0.bin](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin), [ggml-small.bin](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin), [ggml-base.bin](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin), [ggml-tiny.bin](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin), [ggml-medium.bin](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin), [ggml-large-v3.bin](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin) ve [ggml-silero-v6.2.0.bin](https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v6.2.0.bin) dosyalarını `tools\whisper\` içine kaydedin.

### 3. Windows SmartScreen ve Defender Uyarılarını Geçme
TRWhisper düşük seviyeli bir klavye kancası (`WH_KEYBOARD_LL`) ve `SendInput` API'si kullandığı için Windows Defender veya SmartScreen ilk çalıştırmada uyarı verebilir:
1. **SmartScreen penceresi çıkarsa:** **"Ek Bilgi" (More Info)** düğmesine tıklayın ve **"Yine de çalıştır" (Run anyway)** seçeneğini seçin.
2. **Windows Defender İstisnası Eklemek (İsteğe bağlı):**
   ```powershell
   # PowerShell'i Yönetici olarak proje dizininde açıp istisnalara ekleyebilirsiniz:
   Add-MpPreference -ExclusionPath (Get-Location).Path
   # veya tam yol belirterek: Add-MpPreference -ExclusionPath "C:\KlasorYolunuz\TRWHISPER"
   ```

---

## ⚙️ Yapılandırma (`config.json`)

TRWhisper'ı tepsi simgesine sağ tıklayıp **"⚙️ Ayarlar"** diyerek açılan grafiksel arayüz üzerinden tüm detaylarıyla yapılandırabilirsiniz. İsterseniz uygulama dizinindeki `config.json` dosyasını doğrudan Not Defteri ile de düzenleyebilirsiniz:

```json
{
  "General": {
    "Language": "tr",
    "LogDirectory": "%USERPROFILE%\\Dictation",
    "TempAudioPath": "%TEMP%\\trwhisper_temp.wav",
    "EnableCustomDictionary": true,
    "EnableTextNormalization": true
  },
  "Whisper": {
    "CliPath": "tools\\whisper\\whisper-cli.exe",
    "ModelPath": "tools\\whisper\\ggml-large-v3-turbo-q5_0.bin",
    "VadModelPath": "tools\\whisper\\ggml-silero-v6.2.0.bin",
    "Threads": 8,
    "NoTimestamps": true,
    "TimeoutSeconds": 120,
    "IdleTimeoutMinutes": 10
  },
  "LlmCleaning": {
    "EnabledByDefault": false,
    "Provider": "Gemini",
    "ApiKey": "",
    "Model": "gemini-2.0-flash",
    "Endpoint": "https://generativelanguage.googleapis.com/v1beta/models",
    "SystemPrompt": "Aşağıdaki metin Türkçe sesli dikte (speech-to-text) çıktısıdır. Lütfen bu metni konuşma dilinden temiz yazı diline dönüştür:\n1. 'ııı', 'eee', 'şey', 'yani', 'falan', 'hımm' gibi duraksama ve dolgu kelimelerini temizle.\n2. Noktalama işaretlerini (nokta, virgül, soru işareti vb.) ve büyük/küçük harf kullanımını eksiksiz düzelt.\n3. Anlatılmak istenen ana fikri ve kelime anlamlarını kesinlikle değiştirme.\n4. Çıktı olarak YALNIZCA düzeltilmiş metni ver. Başına ya da sonuna açıklama, tırnak işareti, selamlama veya markdown ekleme."
  },
  "Paste": {
    "PasteMode": "Clipboard",
    "RestoreClipboard": true,
    "RestoreDelayMs": 200
  },
  "Audio": {
    "InputDeviceId": ""
  },
  "Hotkey": {
    "PushToTalkKey": "RightCtrl",
    "LlmModifierKey": "Shift",
    "DictationMode": "PushToTalk",
    "HandsFreeSilenceMs": 1800,
    "HandsFreeSilenceThreshold": 0.012
  },
  "Overlay": {
    "Position": "Bottom",
    "ResultDurationSeconds": 10
  }
}
```

- **`EnableCustomDictionary`**: `dictionary.json` dosyasındaki özel mesleki/teknik kelime eşlemelerini etkinleştirir.
- **`EnableTextNormalization`**: Türkçe büyük harf, noktalama, bağlaç ve kekeleme temizliğini açar.
- **`IdleTimeoutMinutes`**: Whisper modelinin boşta kaldığında RAM/VRAM'i serbest bırakması için dakika cinsinden süre (varsayılan: 10 dk).
- **`RestoreClipboard`**: Dikte yapıştırıldıktan sonra panonuzda daha önceden kopyalanmış olan veriyi otomatik olarak geri yükler.
- **`DictationMode`**: `PushToTalk` (bas-konuş) veya `HandsFree` (eller serbest).
- **`HandsFreeSilenceMs`**: Eller serbest modunda konuşmanın bittiğini algılayan sessizlik süresi (milisaniye).
- **`Provider`**: `Gemini` veya `OpenAI`. API anahtarları Windows DPAPI ile şifrelenerek güvenle korunur.

> **İpucu:** Gemini API anahtarınızı Google AI Studio üzerinden ücretsiz alıp Ayarlar arayüzünden kaydedebilirsiniz. Anahtar boş bırakılırsa LLM temizleme atlanır ve yerel transkript doğrudan yapıştırılır.

---

## ⚡ Performans

Intel Core i5-12450H + NVIDIA GeForce RTX 3050 Laptop GPU (4 GB) üzerinde, Türkçe konuşma örnekleriyle ölçülmüştür. Süreler, her diktede yeniden yapılan model yüklemesini de içerir.

| Motor | Model | 3,2 sn konuşma | 10,7 sn konuşma |
|---|---|---|---|
| İşlemci (CPU) | Small | 6,5 sn | 9,3 sn |
| İşlemci (CPU) | Large-v3 Turbo | 23,1 sn | 24,4 sn |
| CUDA (RTX 3050) | Small | 2,2 sn | 2,8 sn |
| CUDA (RTX 3050) | Large-v3 Turbo | 2,5 sn | 3,1 sn |

- GPU'da Turbo, işlemcideki Small'dan hem daha hızlı hem daha doğrudur; ~1,2 GB VRAM kullanır.
- GPU bir süre boşta kaldıktan sonraki ilk dikte birkaç saniye daha uzun sürebilir.
- Konuşma yoksa VAD sayesinde ağır işlem atlanır; boş bir kayıt ~1–2 saniyede biter.

---

## 🩺 Sorun Giderme

- **Bir şey beklendiği gibi çalışmıyorsa:** `%USERPROFILE%\Dictation\trwhisper.log` dosyasına bakın.
- **Yönetici olarak çalışan bir uygulamaya metin yapıştırılmıyor:** Windows, normal bir uygulamanın yönetici yetkili pencerelere tuş göndermesini engeller. Metin yine panodadır; `Ctrl + V` ile yapıştırabilirsiniz.
- **Çözümleme 20 saniyeden uzun sürüyor:** büyük ihtimalle Large-v3 Turbo işlemcide çalışıyor. NVIDIA ekran kartınız varsa `setup.ps1 -Cuda` ile CUDA derlemesini kurun ya da tepsi menüsünden **Small** modelini seçin.

---

## 🚀 Projeyi Derleme ve Çalıştırma

PowerShell ile:
```powershell
# Geliştirme modunda çalıştırma
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Run

# Bağımsız, tek dosyalık Release paketi oluşturma (publish\ klasörüne)
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Publish
```

> **Not:** Paketi her zaman `build.ps1 -Publish` ile oluşturun. Bu komut WPF'in native kütüphanelerini tek dosyalık exe'nin içine gömer (`IncludeNativeLibrariesForSelfExtract`); bu seçenek olmadan yapılan düz bir `dotnet publish`, açılışta çöken bir exe üretir. Uygulama `tools\whisper\` klasörünü exe'nin yanında ya da üst klasörlerinde bulur.

---

## 🧪 Testler ve Doğrulama

TRWhisper'ın kararlılığı ve mimarisi otomatik test paketleriyle korunmaktadır:

```powershell
# 87 adet birim testini (AppMode, Hotkey, LLM, ModelManager, DPAPI vb.) çalıştırır:
dotnet test tests\TRWhisper.Tests\TRWhisper.Tests.csproj

# WPF Ayarlar Penceresi XAML şablon ve duman testini çalıştırır:
dotnet run --project tools\uismoke
```
