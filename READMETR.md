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

- **Modern Grafiksel Ayarlar Penceresi (WPF)**: Sistem tepsisinden tek tıkla açılan zengin ve modern ayarlar paneli; **Genel**, **Ses**, **Model**, **Kısayol**, **Sözlük**, **Yapay Zeka (LLM)** ve **Kapsül** ve **Yedekleme** sekmeleriyle tüm ayarları görsel olarak yönetme ve anında test etme.
- **Özel Sözlük & Jargon Desteği (Custom Dictionary)**: Kullanıcı tanımlı fonetik ve mesleki jargon eşlemeleri (`dictionary.json`). Sektörel terimler, özel isimler veya Whisper'ın karıştırabileceği teknik kelimeler kelime sınırı kurallarıyla ve Türkçe çekim ekleri kesme işaretiyle korunarak otomatik düzeltilir (ör. `pitonda` -> `Python'da`).
- **Gelişmiş Türkçe Metin Normalizasyonu**:
  - Konuşma dilindeki sayı, yüzde, para birimi, tarih, saat ve ölçü birimlerini kural tabanlı olarak iş yazışmasına uygun rakamsal biçime çevirir (ör. `yüzde yirmi` -> `%20`, `on beş eylül` -> `15 Eylül`, `üç buçuk kilo` -> `3,5 kg`, `iki buçukta` -> `02:30'da`, `yüz dolar` -> `100 USD`).
  - Sayılara ve sembollere gelen Türkçe çekim eklerinde kalınlık-incelik ve sertleşme gibi ses uyumu kurallarını eksiksiz uygular.
- **Bağlama Duyarlı (Context-Aware) LLM Modları & Uygulama Algılama**:
  - Yerleşik hazır modlar & kişilikler: **Temiz Metin** (✨ dolgu kelimeleri, kekelemeleri atar, yazım ve noktalama hatalarını düzeltir, anlama sadık kalır), **Kurumsal E-posta** (📧 taslağı saygılı ve akıcı bir iş e-postasına dönüştürür), **Maddeli Özet** (📝 ana fikirleri ve kararları `- ` maddeli listesine döker), **Kod & Teknik** (💻 teknik terimleri korur, komut ve kod parçalarını Markdown bloklarına alır) ve **İngilizce Çeviri** (🌐 Türkçe konuşmayı profesyonel İngilizceye çevirir).
  - Kullanıcı tanımlı **Özel Modlar (Custom Modes)** ekleme ve ham metin çıktısı alma desteği.
  - Aktif ön plan uygulamasını otomatik algılama (`ForegroundAppDetector`): VS Code, Visual Studio, Outlook, Thunderbird veya Terminal gibi pencereleri anında tespit edip uygun LLM moduna otomatik geçer ve kapsül üzerinde aktif uygulama rozetini gösterir.
  - **Donanım Seviyesinde Güvenli API Anahtarı Saklama**: Windows DPAPI (`Data Protection API`) ve uygulamaya özel entropy (`TRWhisper_DPAPI_Entropy_v2`) ile şifrelenmiş depolama; API anahtarlarınız asla düz metin olarak saklanmaz veya güvenli olmayan HTTP üzerinden iletilmez.
- **Esnek Kısayol Tuşları & 3 Farklı Dikte Modu**:
  - Klasik **Bas-Konuş (Push-to-Talk)** modu (varsayılan: Sağ Ctrl — basılı tutun, bırakınca çözümler).
  - **İki Basışla Aç/Kapa (Toggle)** modu (bir kez basıp konuşmaya başlayın, bitirmek için tekrar basın).
  - **Eller Serbest (Hands-Free)** modu (bir kez basıp konuşun; konuşmanız bitip sessizlik algılandığında kendiliğinden tamamlansın).
  - Özelleştirilebilir kısayollar (Sağ/Sol Ctrl, Shift, Alt, CapsLock, F8/F9, Mouse4/Mouse5 ve özel tuş kombinasyonları).
- **Gerçek Zamanlı Canlı Akış Önizlemesi (Streaming Preview)**:
  - Canlı akış (`EnableStreamingPreview`): Siz mikrofona konuşurken kelimeler eşzamanlı olarak ekrandaki kayan kapsülde akar; konuşma tamamlandığında nihai yüksek kaliteli metin işlenip yapıştırılır.
- **Whisper.net Yerleşik C# Motoru & Dinamik Model Yönetimi**:
  - `whisper-cli` haricinde süreç içi yüksek performanslı Whisper.net yerel çalışma zamanı entegrasyonu.
  - Ayarlar arayüzünden doğrudan model indirme, silme ve **SHA-256 doğrulama** güvenliği.
  - Sistem boşta kaldığında bellek ve VRAM tasarrufu sağlayan otomatik model boşaltma zamanlayıcısı (`IdleTimeoutMinutes`).
- **Otomatik Yapıştırma & Çift Pano Modu**:
  - **Pano (Clipboard)** modu: Transkripti panoya kopyalar ve `Ctrl + V` simülasyonu ile yapıştırır; isteğe bağlı `RestoreClipboard` ile panonun eski içeriği korunur.
  - **Doğrudan Yazma (DirectType)** modu: Panoya hiç dokunmadan metni `KEYEVENTF_UNICODE` ile karakter karakter yazar.
- **%100 Çevrimdışı ve Gizli (STT) + Sıkı Gizlilik Denetimleri**:
  - Yerel Whisper motoruyla çalışır. Ses veriniz bilgisayarınızın dışına çıkmaz.
  - **Gizlilik Odaklı Günlükleme**: İsteğe bağlı `EnableHistoryLogging` seçeneği ile transkriptlerin diskte `.md` dosyası olarak kaydedilmesi tamamen kapatılabilir. Teşhis günlükleri (`trwhisper.log`) transkript içeriklerinden arındırılmıştır; geçici ses kayıtları oturum bazlı benzersiz GUID'lerle oluşturulur ve hemen silinir.
- **NVIDIA GPU Hızlandırma (CUDA 13)**: Modern NVIDIA ekran kartları için **CUDA 13** mimarisi (sürücü >= 580.00 gerektirir). Large-v3 Turbo modeli dikteyi işlemcideki ~23 saniye yerine ~2–3 saniyede çözer. Uyumlu GPU yoksa otomatik CPU moduna geçer.
- **Sessizlik Algılama ve Uydurma Metin Filtresi**:
  - Yerleşik **Silero VAD** özelliği konuşma olmayan kısımları atlar. Boş kayıtlarda uydurma metin yapıştırılmaz.
  - Whisper'ın bilinen hayalet altyazıları (`Altyazı M.K.`, `İzlediğiniz için teşekkür ederim.`) ve ses etiketleri (`[MÜZİK ÇALIYOR]`) filtrelenir.
- **Kayan Durum Kapsülü (Pill Overlay)**: Canlı ses dalgası animasyonu, gerçek zamanlı metin akışı, çözümleme durumu, aktif uygulama/mod rozeti, düşük mikrofon sesi uyarısı, ham/temiz metin geçişi ve iptal (✕) / bitir (✓) / kopyala düğmeleriyle serbest konumlandırma.
- **Windows Açılışında Başlatma (Autostart)**: Ayarlar menüsünden tek tıkla Windows başlangıcına eklenebilir (HKCU Run anahtarı, yönetici hakkı gerekmez).
- **Yedekleme ve Geri Yükleme**: Dikte günlükleri (`.md`), özel sözlük ve ayarlar tek bir ZIP dosyasına alınır; tepsi menüsünden veya Ayarlar'dan tek tıkla yedek alınır, seçilen yedekten geri yüklenir. Açılışta otomatik yedek, saklama sayısı sınırı ve geri yüklemeden hemen önce alınan emniyet yedeği içerir. API anahtarı yedeğe yazılmaz.
- **Günlük API Çağrı Tavanı (Maliyet Denetimi)**: Bulut sağlayıcı seçiliyken günlük istek tavanı uygulanır (varsayılan 200). Tavan dolunca ya LLM temizleme atlanır (**Engelle**) ya da yalnızca uyarı verilip çağrı sürdürülür (**Yalnızca uyar**) — seçim sizindir. Tavanın %80'inde önceden uyarı çıkar.
- **API Anahtarı Yaşam Döngüsü**: Anahtarın en son ne zaman değiştirildiği kaydedilir; eşik aşılınca (varsayılan 90 gün) Ayarlar penceresi rotasyon hatırlatır. Tek tıkla anahtar silme ve sağlayıcı anahtar paneline doğrudan geçiş.
- **Kapsamlı Test Paketi**: 152 adet otomatik birim testi ve XAML şablon duman testi (`uismoke`) ile yüksek kod kalitesi.
- **Yerel Günlük**: Transkriptler zaman damgasıyla `%USERPROFILE%\Dictation\YYYY-MM.md` dosyasına kaydedilebilir. Operasyonel günlükler `%USERPROFILE%\Dictation\trwhisper.log` dosyasına yazılır.

---

## 🛠️ Kurulum ve Gereksinimler

**Gereksinimler**
- Windows 11 x64
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (kaynak koddan derlemek için)
- İsteğe bağlı: GPU hızlandırma için uyumlu sürücülü (CUDA 13 desteği için NVIDIA sürücüsü >= 580.00) bir NVIDIA ekran kartı

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
| Whisper **NVIDIA GPU (CUDA 13)** motoru | Seçime bağlı, ~519 MB indirilir (açılmış hali ~680 MB; NVIDIA kartınız ve sürücü >= 580.00 varsa otomatik önerilir) |
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
  TRWhisper-Setup-2.1.0.exe /SILENT /ENGINE=cuda /MODELS=turbo,small /TASKS=desktopicon
  ```

> [!NOTE]
> **Güvenlik & VirusTotal Sonuçları (68/69 Temiz):**  
> Kurulum dosyası açık kaynaklı ve bağımsız bir proje olduğundan ticari kod imzalama sertifikası (EV Code Signing) içermez. Windows SmartScreen ilk çalıştırmada "bilinmeyen yayımcı" uyarısı verebilir: **Daha fazla bilgi > Yine de çalıştır**.  
>  
> Güncel [VirusTotal Tarama Raporu (v2.1.0)](https://www.virustotal.com/gui/file/bc7f92be96bae06c652c572e3da75953c105eba5755d22505e5ad2532d98d3ff?nocache=1)'nu inceleyebilirsiniz. Microsoft Defender, Kaspersky, Bitdefender, ESET, Sophos ve Malwarebytes dahil **68 antivirüs motoru dosyayı %100 temiz** olarak onaylamaktadır.  
>  
> **1 Sezgisel/ML Uyarısı (Trapmine) Hakkında:**  
> Bu motor, imzasız ve yeni yayınlanan dosyalara otomatik düşük olasılıklı makine öğrenimi puanı (`Suspicious.low.ml.score`) vermektedir. TRWhisper'ın global bas-konuş kısayolunu dinlemek için düşük seviyeli Windows klavye kancası (`SetWindowsHookEx` / `WH_KEYBOARD_LL`), dikte edilen metni aktif imleç konumuna yapıştırmak için klavye simülasyonu (`SendInput`) ve API anahtarlarını şifrelemek için Windows DPAPI kullanması, bu tür yapay zeka motorlarında yanlış pozitif (false positive) uyarı oluşturabilmektedir. Yazılım tamamen açık kaynaklıdır ve hiçbir zararlı bileşen barındırmaz.

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

# Bunun yerine NVIDIA GPU (CUDA 13) derlemesi — NVIDIA ekran kartınız varsa önerilir
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Cuda

# İsteğe bağlı olarak diğer modelleri (small, base, tiny, medium, large-v3) indirmek için:
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Model small
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Model base
```

Bu betik:
- `tools\whisper\` ve `%USERPROFILE%\Dictation\` klasörlerini oluşturur.
- Test edilmiş `whisper.cpp` **v1.9.3** (derleme `b4938`) Windows x64 dosyalarını indirir: işlemci derlemesi (~8 MB) ya da `-Cuda` ile Whisper.net için gerekli CUDA 13 çalışma zamanı kitaplıklarını (`cudart64_13.dll`, `cublas64_13.dll`, `cublasLt64_13.dll`, ~385 MB). Mevcut bir işlemci kurulumunda `-Cuda` ile tekrar çalıştırmak onu GPU derlemesine yükseltir.
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
> - Whisper dosyaları ([b4938 sürümü](https://github.com/ggml-org/whisper.cpp/releases/tag/b4938)): Windows binary dosyalarını `tools\whisper\` içine çıkarın. CUDA GPU hızlandırması için CUDA 13 DLL'lerinin (`cudart64_13.dll`, `cublas64_13.dll`, `cublasLt64_13.dll`) `tools\cuda13\` (veya uygulamanın çalıştığı dizinde) bulunduğundan emin olun.
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

## 🔐 Güvenlik ve Gizlilik

### API anahtarınız nasıl korunuyor? (Tehdit modeli)
- Bulut LLM temizleme için girdiğiniz API anahtarı `config.json` içine **düz metin yazılmaz**: Windows DPAPI (`DataProtectionScope.CurrentUser`) ile şifrelenip `enc:` ön ekiyle saklanır. DPAPI şifreleme başarısız olursa anahtar diske **hiç yazılmaz**.
- Bu koruma **Windows hesabınıza bağlıdır**: dosya başka bir kullanıcıya, başka bir makineye ya da diskten alınan bir yedeğe taşınırsa çözülemez.
- **Koruma sağlamadığı durum:** Aynı Windows kullanıcısı olarak çalışan kötü amaçlı bir yazılım aynı DPAPI çağrısını yapabileceği için anahtarı çözebilir. Bu, yerel anahtar saklamanın (Windows Kimlik Bilgisi Yöneticisi dâhil) doğasında olan bir sınırdır. Azami gizlilik için LLM temizlemeyi **Ollama** ile tamamen yerel çalıştırın; bu durumda API anahtarı hiç gerekmez.
- Uygulama, loopback (`localhost`/`127.0.0.1`) dışındaki `http://` uç noktalarına API anahtarı göndermeyi **reddeder** — sağlayıcı Gemini, OpenAI veya Ollama fark etmez; harici uç noktalar için HTTPS zorunludur.

### Anahtar kapsamı, süresi ve rotasyonu
TRWhisper'ın kendi oturum/token yaşam döngüsü yoktur: kimlik doğrulaması tek bir statik API anahtarıyla yapılır ve o anahtarın **kapsamını, süresini ve iptalini yalnızca sağlayıcı paneli belirleyebilir**. Uygulama bu gerçeği gizlemek yerine görünür kılar:

- Anahtarın en son ne zaman değiştirildiği (`LlmCleaning.ApiKeyUpdatedUtc`) kaydedilir. **Ayarlar → Yapay Zeka** sekmesi anahtarın yaşını gösterir; `ApiKeyRotationReminderDays` eşiği (varsayılan **90 gün**) aşılırsa rotasyon uyarısı çıkar. Damga yalnızca anahtar gerçekten değiştiğinde tazelenir, her kaydetmede değil.
- **"Anahtarı Sil"** düğmesi anahtarı bu bilgisayardan anında kaldırır ve ayarları kaydeder. Silmek anahtarı geçersiz kılmaz; sağlayıcı panelinden de iptal edin.
- **"Anahtar Panelini Aç"** düğmesi doğru sayfaya götürür: [Google AI Studio](https://aistudio.google.com/app/apikey) veya [OpenAI API anahtarları](https://platform.openai.com/api-keys).
- Sağlayıcı panelinde önerilenler: anahtarı yalnızca kullandığınız modele/API'ye kısıtlayın, mümkünse IP kısıtı tanımlayın, bütçe ve kota uyarısı kurun, kullanılmayan anahtarları silin.
- En güçlü çözüm anahtarı hiç kullanmamaktır: LLM temizlemeyi **Ollama** ile yerel çalıştırın.

### Maliyet ve kota denetimi
Bulut sağlayıcı seçiliyken LLM temizlemeli her dikte bir API çağrısı doğurur. Sağlayıcı panelindeki bütçe uyarısı ancak para harcandıktan **sonra** haber verir; bu yüzden tavan uygulamanın içindedir:

- `LlmCleaning.DailyRequestLimit` (varsayılan **200**, `0` = sınırsız) bir takvim gününde yapılabilecek bulut çağrısını sınırlar. Sayaç `api-usage.json` içinde `config.json` ile aynı klasörde tutulur ve yerel gece yarısında sıfırlanır.
- `LlmCleaning.QuotaExceededAction` tavan dolunca ne olacağını belirler:
  - `Block` (varsayılan) — istek **hiç gönderilmez**, ham transkript yapıştırılır, tepside uyarı çıkar. Ek ücret doğmaz.
  - `WarnOnly` — istek gönderilir, yalnızca uyarı gösterilir. Fatura büyümeye devam edebilir.
- Tavanın **%80'ine** ulaşıldığında önceden uyarı verilir. Geçersiz/bilinmeyen bir `QuotaExceededAction` değeri sessizce `Block`'a düşer.
- Yerel sağlayıcılar (Ollama, LlamaCpp) hiç sayılmaz; ücret doğurmazlar.
- Güncel sayaç **Ayarlar → Yapay Zeka → Kullanım ve Maliyet Tavanı** altında görünür ve oradan sıfırlanabilir (sağlayıcıdaki gerçek kullanımı değiştirmez).

### Günlükler ve dikte içeriği
- Teşhis günlüğü (`%USERPROFILE%\Dictation\trwhisper.log`) transkript metni **içermez**, yalnızca karakter sayısı yazar. Sağlayıcı hata gövdeleri 200 karakterle sınırlanır ve anahtar benzeri dizgiler maskelenir.
- 2.0.1 öncesi sürümlerden kalan düz metin transkript satırları, uygulama açılışında bir kez otomatik olarak silinir.
- Transkript geçmişi (`%USERPROFILE%\Dictation\YYYY-MM.md`) `EnableHistoryLogging: false` ile tamamen kapatılabilir.
- Geçici ses kayıtları `%TEMP%` altında benzersiz GUID adlarıyla oluşturulur, çözümleme biter bitmez silinir; açılışta yetim kalanlar temizlenir.

### Yedekleme ve geri yükleme
Dikte geçmişiniz ve özel sözlüğünüz yeniden üretilemez veriler. TRWhisper bunları tek bir ZIP dosyasında toplar:

| Yedeğin içeriği | Kaynak |
| :--- | :--- |
| `config.json` | Ayarlar — **API anahtarı çıkarılmış olarak** |
| `dictionary.json` | Özel sözlük kuralları |
| `transkriptler/*.md` | `%USERPROFILE%\Dictation` altındaki aylık dikte günlükleri |
| `YEDEK-BILGI.TXT` | Tarih, bilgisayar adı ve geri yükleme yönergesi |

- **Yedek alma:** Tepsi menüsünden **"💾 Şimdi Yedek Al"** ya da **Ayarlar → Yedekleme → "Şimdi Yedek Al"**.
- **Otomatik yedek:** `Backup.EnableAutomaticBackup` açıkken, son yedeğin üzerinden `AutomaticBackupIntervalDays` (varsayılan 1) gün geçtiyse TRWhisper açılışta arka planda sessizce yedek alır. `RetentionCount` (varsayılan 10) en yeni kaç yedeğin saklanacağını belirler; fazlası silinir.
- **Geri yükleme:** **Ayarlar → Yedekleme → "Yedekten Geri Yükle"**. Ayarların, sözlüğün ve aynı adlı günlük dosyalarının **üzerine yazılır**; bu yüzden işlemden hemen önce mevcut durumun **emniyet yedeği** otomatik alınır ve adı sonuç satırında bildirilir.
- **API anahtarı yedeğe hiç yazılmaz.** İki nedenle: yedek dosyası taşınabilir sıradan bir dosyadır ve sır taşımamalıdır; ayrıca anahtar DPAPI ile bu Windows hesabına bağlı şifrelendiği için başka bir makinede zaten çözülemezdi. Geri yükleme sonrası mevcut anahtarınız korunur, yedekten gelen boş değer onu ezmez.
- Arşivden dosya açarken yalnızca düz `.md` adları kabul edilir; yol ayracı, `..` veya başka uzantı içeren girdiler yok sayılır (zip-slip koruması).
- Yedekler yerelde kalır. Farklı bir diske ya da buluta kopyalamak isterseniz `Backup.BackupDirectory` değerini o konuma çevirebilirsiniz (ör. `D:\Yedekler\TRWhisper`).

### İndirme bütünlüğü
- `scripts\setup.ps1`, kurulum sihirbazı ve uygulama içi model indirici; whisper.cpp ikilileri, GGML modelleri, Silero VAD ve CUDA runtime paketleri dâhil **indirdiği her dosyayı sabit SHA-256 özetiyle doğrular**. Özet tutmazsa dosya silinir ve işlem durdurulur (fail-closed).
- Hugging Face adresleri belirli bir commit'e sabitlenmiştir; `main` dalı değişse bile indirilen dosya değişmez.

### Kurulum, imzalama ve doğrulama
- Yayınlanan kurulum dosyaları şu an **Authenticode ile imzalı değildir** (SmartScreen uyarısının temel sebebi budur). İndirdiğiniz dosyayı sürüm notlarındaki özetle doğrulayın:
  ```powershell
  Get-FileHash .\TRWhisper-Setup-2.1.0.exe -Algorithm SHA256
  ```
  Derleme betiği imzalamayı destekler: `scripts\build-installer.ps1 -CertThumbprint <sertifika_parmak_izi>` (veya `TRWHISPER_SIGN_THUMBPRINT` ortam değişkeni).
- Varsayılan kurulum yönetici hakkı istemez ve `%LOCALAPPDATA%\Programs\TRWhisper` altına yapılır. Bu dizin aynı kullanıcı tarafından yazılabilir olduğundan, sertleştirilmiş ortamlarda kurulumu **Program Files** altına alabilirsiniz:
  ```powershell
  .\TRWhisper-Setup-2.1.0.exe /ALLUSERS
  ```
  Bu modda uygulama dizini yalnızca okunur kalır; ayarlar `%APPDATA%\TRWhisper`, sonradan indirilen modeller `%LOCALAPPDATA%\TRWhisper` altına yazılır.
- Uygulama açılışta DLL aramasını yalnızca kendi dizini ve `System32` ile sınırlar (`SetDefaultDllDirectories`), böylece DLL hijacking/binary planting yüzeyini daraltır.

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
    "EnableTextNormalization": true,
    "EnableStreamingPreview": true,
    "EnableHistoryLogging": true
  },
  "Whisper": {
    "CliPath": "tools\\whisper\\whisper-cli.exe",
    "ModelPath": "tools\\whisper\\ggml-large-v3-turbo-q5_0.bin",
    "VadModelPath": "tools\\whisper\\ggml-silero-v6.2.0.bin",
    "Threads": 4,
    "NoTimestamps": true,
    "TimeoutSeconds": 120,
    "IdleTimeoutMinutes": 10
  },
  "LlmCleaning": {
    "EnabledByDefault": false,
    "Provider": "Ollama",
    "ApiKey": "",
    "Model": "qwen2.5:3b",
    "Endpoint": "http://localhost:11434/v1/chat/completions",
    "ActiveModeId": "Clean",
    "EnableAutoAppMode": true,
    "ApiKeyRotationReminderDays": 90,
    "DailyRequestLimit": 200,
    "QuotaExceededAction": "Block",
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
  },
  "Backup": {
    "EnableAutomaticBackup": true,
    "AutomaticBackupIntervalDays": 1,
    "RetentionCount": 10,
    "IncludeTranscripts": true,
    "BackupDirectory": "%USERPROFILE%\\Dictation\\Yedekler"
  }
}
```

- **`EnableCustomDictionary`**: `dictionary.json` dosyasındaki özel mesleki/teknik kelime eşlemelerini etkinleştirir.
- **`EnableTextNormalization`**: Konuşma dilindeki sayı, tarih, saat, yüzde, para ve birimlerin kural tabanlı rakamsal dönüşümünü sağlar.
- **`EnableStreamingPreview`**: Konuşma sırasında kelimelerin kayan durum kapsülünde eşzamanlı akmasını sağlar.
- **`EnableHistoryLogging`**: Transkriptlerin `%USERPROFILE%\Dictation\YYYY-MM.md` dosyasına kaydedilmesini açar/kapatır (gizlilik için devre dışı bırakılabilir).
- **`IdleTimeoutMinutes`**: Whisper modelinin boşta kaldığında RAM/VRAM'i serbest bırakması için dakika cinsinden süre (varsayılan: 10 dk; 0 modeli sürekli bellekte tutar).
- **`PasteMode`**: `Clipboard` (panoya kopyalayıp `Ctrl + V` simüle eder) veya `DirectType` (panoya dokunmadan `KEYEVENTF_UNICODE` ile karakter karakter yazar).
- **`RestoreClipboard`**: Dikte yapıştırıldıktan sonra panonuzda önceden bulunan içeriği otomatik geri yükler (yalnızca Clipboard modunda).
- **`DictationMode`**: `PushToTalk` (bas-konuş), `Toggle` (iki basışla aç/kapa) veya `HandsFree` (otomatik sessizlik algılamalı eller serbest).
- **`HandsFreeSilenceMs`**: Eller serbest modunda konuşmanın bittiğini algılayan sessizlik süresi (milisaniye).
- **`Provider`**: `Ollama`, `Gemini` veya `OpenAI`. Bulut API anahtarları Windows DPAPI (`TRWhisper_DPAPI_Entropy_v2`) ile şifrelenerek korunur. Gemini için `x-goog-api-key` başlığı kullanılır ve harici uç noktalarda HTTPS zorunludur.
- **`ActiveModeId`**: Aktif LLM kişiliği (`Clean`, `Email`, `Summary`, `Technical`, `TranslateEn` veya özel mod kimlikleri).
- **`EnableAutoAppMode`**: Ön plandaki uygulamaya (`ForegroundAppDetector`) göre LLM modunu otomatik uyarlar.
- **`ApiKeyRotationReminderDays`**: API anahtarı bu kadar gündür değişmediyse Ayarlar penceresi rotasyon hatırlatır (varsayılan 90; `0` hatırlatmayı kapatır).
- **`DailyRequestLimit`**: Bulut sağlayıcıya bir günde yapılabilecek en fazla çağrı (varsayılan 200; `0` = sınırsız). Yerel sağlayıcılar sayılmaz.
- **`QuotaExceededAction`**: Tavan dolunca `Block` (LLM temizlemeyi atla, ek ücret doğmasın) veya `WarnOnly` (yalnızca uyar, çağrıyı yine de yap).
- **`Backup.EnableAutomaticBackup` / `AutomaticBackupIntervalDays` / `RetentionCount`**: Açılışta otomatik yedek, yedekler arası en az gün sayısı ve saklanacak yedek adedi (`0` = hepsini sakla).
- **`Backup.IncludeTranscripts`**: Dikte günlüklerinin (`.md`) yedeğe dahil edilip edilmeyeceği. Kapatılırsa yedek yalnızca ayarları ve sözlüğü içerir.
- **`Backup.BackupDirectory`**: Yedek ZIP dosyalarının yazılacağı klasör; `%USERPROFILE%` gibi ortam değişkenleri kullanılabilir.

> **İpucu (Bulut API):** Gemini API anahtarınızı Google AI Studio üzerinden ücretsiz alıp Ayarlar arayüzünden kaydedebilirsiniz. Anahtar boş bırakılırsa LLM temizleme atlanır ve yerel transkript doğrudan yapıştırılır.

### 🦙 %100 Yerel ve Gizli LLM Temizleme (Ollama)

Transkript temizleme işlemini de tıpkı ses tanıma gibi **tamamen çevrimdışı, sıfır veri transferiyle ve ücretsiz** çalıştırmak isterseniz **Ollama** entegrasyonunu kullanabilirsiniz:

1. [ollama.com](https://ollama.com) adresinden Ollama'yı indirin veya PowerShell'den kurun:
   ```powershell
   winget install Ollama.Ollama
   ```
2. Türkçe dil kabiliyeti ve metin düzeltme performansı yüksek hafif bir model indirin:
   ```powershell
   ollama run qwen2.5:3b
   # veya
   ollama run llama3.2:3b
   ```
3. TRWhisper tepsi simgesine sağ tıklayıp **⚙️ Ayarlar > Yapay Zeka (AI)** sekmesini açın:
   - **Sağlayıcı:** `Ollama`
   - **Uç Nokta:** `http://localhost:11434`
   - **Model:** `qwen2.5:3b` (veya indirdiğiniz model adı)
   - **API Anahtarı:** Boş bırakabilirsiniz (yerel çalıştığı için anahtar gerekmez).

Böylece hem ses tanıma (Whisper) hem de yapay zeka ile metin düzenleme (Ollama) bilgisayarınızın dışına tek bir bayt dahi göndermeden tamamen yerel ve gizli çalışır.

---

## ⚡ Performans

Intel Core i5-12450H + NVIDIA GeForce RTX 3050 Laptop GPU (4 GB) üzerinde, Türkçe konuşma örnekleriyle ölçülmüştür. Süreler, her diktede yeniden yapılan model yüklemesini de içerir.

| Motor | Model | 3,2 sn konuşma | 10,7 sn konuşma |
|---|---|---|---|
| İşlemci (CPU) | Small | 6,5 sn | 9,3 sn |
| İşlemci (CPU) | Large-v3 Turbo | 23,1 sn | 24,4 sn |
| CUDA 13 (RTX 3050) | Small | 2,2 sn | 2,8 sn |
| CUDA 13 (RTX 3050) | Large-v3 Turbo | 2,5 sn | 3,1 sn |

- GPU'da Turbo, işlemcideki Small'dan hem daha hızlı hem daha doğrudur; ~1,2 GB VRAM kullanır.
- GPU bir süre boşta kaldıktan sonraki ilk dikte birkaç saniye daha uzun sürebilir.
- Konuşma yoksa VAD sayesinde ağır işlem atlanır; boş bir kayıt ~1–2 saniyede biter.

---

## 🩺 Sorun Giderme

- **Bir şey beklendiği gibi çalışmıyorsa:** `%USERPROFILE%\Dictation\trwhisper.log` dosyasına bakın.
- **Yönetici olarak çalışan bir uygulamaya metin yapıştırılmıyor:** Windows, normal bir uygulamanın yönetici yetkili pencerelere tuş göndermesini engeller. Metin yine panodadır; `Ctrl + V` ile yapıştırabilirsiniz.
- **Çözümleme 20 saniyeden uzun sürüyor:** büyük ihtimalle Large-v3 Turbo işlemcide çalışıyor. NVIDIA ekran kartınız varsa `setup.ps1 -Cuda` ile CUDA derlemesini kurun (sürücü sürümü >= 580.00 gereklidir) ya da tepsi menüsünden **Small** modelini seçin.

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
# 152 adet birim testini (AppMode, Hotkey, LLM, ModelManager, DPAPI, veri yolları vb.) çalıştırır:
dotnet test tests\TRWhisper.Tests\TRWhisper.Tests.csproj

# WPF Ayarlar Penceresi XAML şablon ve duman testini çalıştırır:
dotnet run --project tools\uismoke
```
