# TRWhisper 1.0.0

Türkçe dikte için tamamen yerel çalışan bas-konuş uygulaması — artık **tek dosyalık kurulum** ile.
A fully local push-to-talk dictation app for Windows 11 — now with a **single-file installer**.

---

## 🇹🇷 Türkçe

### Kurulum
1. Aşağıdaki **TRWhisper-Setup-1.0.0.exe** dosyasını indirin (~51 MB).
2. Çalıştırın. Yönetici şifresi gerekmez.
3. Windows SmartScreen uyarısı çıkarsa: **Daha fazla bilgi → Yine de çalıştır**
   (kurulum dosyası henüz kod imzalı değil).

Kurulum sihirbazı Türkçe ve İngilizce'dir ve başlamadan önce ne kuracağını açıkça yazar.

### Kurulum sırasında sizin seçtikleriniz
| Bileşen | Açıklama |
|---|---|
| **Whisper CPU motoru** | Kurulum dosyasının içinde, her bilgisayarda çalışır |
| **Whisper NVIDIA GPU (CUDA 12.4) motoru** | ~640 MB indirme; NVIDIA kartınız varsa otomatik önerilir (Turbo modeli ~23 sn yerine ~3 sn) |
| **Large-v3 Turbo modeli** (~547 MB) | En doğru sonuç, GPU önerilir |
| **Small modeli** (~465 MB) | CPU'da hızlı, doğruluğu daha düşük |
| **Kısayollar** | Masaüstü, Başlat menüsü, Windows açılışında başlatma — hepsi isteğe bağlı |

En az bir model seçmeniz gerekir; ikisini birden kurup sistem tepsisindeki menüden anında
geçiş yapabilirsiniz.

### Her zaman kurulanlar
- TRWhisper uygulaması (.NET 9 gömülü — ayrıca .NET kurmanız gerekmez)
- Silero VAD sessizlik modeli (sessizlikte uydurma metin üretilmesini engeller)
- Microsoft Visual C++ 2015-2022 Runtime — **yalnızca bilgisayarınızda yoksa** (tek seferlik UAC)

### Gizlilik
Ses tanıma tamamen çevrimdışıdır; sesiniz hiçbir sunucuya gönderilmez. Kurulum yalnızca seçtiğiniz
bileşenleri indirir ve her dosyayı SHA-256 ile doğrular. İsteğe bağlı "LLM Temizleme" özelliğini
kendi API anahtarınızla açarsanız yalnızca metin (ses değil) gönderilir; varsayılan olarak kapalıdır.

### 🛡️ Güvenlik & Doğrulama (VirusTotal)
- **SHA-256:** `c97e23fe0cd720c570e44370f74d49fc74c961f106eecf11e48080d217d979c2`
- **VirusTotal Raporu:** [VirusTotal Sonucu (1/72)](https://www.virustotal.com/gui/file/c97e23fe0cd720c570e44370f74d49fc74c961f106eecf11e48080d217d979c2/detection)
  *(Microsoft Defender, Kaspersky, Bitdefender ve 70+ antivirüs motorunda %100 temizdir. DeepInstinct motorunun verdiği tek uyarı; bas-konuş için kullanılan global klavye kancası `WH_KEYBOARD_LL`, otomatik yapıştırma `SendInput` ve kurulum sihirbazının model indirme davranışından kaynaklanan tipik bir yanlış alarmdır / false positive).*

### Kaldırma
**Ayarlar → Uygulamalar → TRWhisper.** Dikte kayıtlarınızın (`%USERPROFILE%\Dictation`) silinip
silinmeyeceği size sorulur.

### Sessiz kurulum
```powershell
TRWhisper-Setup-1.0.0.exe /SILENT /ENGINE=cuda /MODELS=turbo,small /TASKS=desktopicon
```
`/ENGINE=cpu|cuda`, `/MODELS=turbo,small`, `/DIR=...`, `/TASKS=desktopicon,startmenuicon,autostart`

---

## 🇬🇧 English

### Installation
1. Download **TRWhisper-Setup-1.0.0.exe** below (~51 MB).
2. Run it — no administrator password required.
3. If Windows SmartScreen warns: **More info → Run anyway** (the installer is not code-signed yet).

The wizard is available in Turkish and English and states exactly what it will install.

### What you choose during setup
| Component | Notes |
|---|---|
| **Whisper CPU engine** | Included in the setup file, works on every PC |
| **Whisper NVIDIA GPU (CUDA 12.4) engine** | ~640 MB download; auto-recommended when an NVIDIA GPU is found (~3 s instead of ~23 s with Turbo) |
| **Large-v3 Turbo model** (~547 MB) | Most accurate, GPU recommended |
| **Small model** (~465 MB) | Fast on CPU, less accurate |
| **Shortcuts** | Desktop, Start menu, start with Windows — all optional |

At least one model must be selected; install both and switch instantly from the tray menu.

### Always installed
- TRWhisper application (.NET 9 embedded — no separate .NET installation needed)
- Silero VAD silence model (prevents hallucinated text on silence)
- Microsoft Visual C++ 2015-2022 Runtime — **only if missing** (one UAC prompt)

### Privacy
Speech recognition is fully offline; your voice never leaves your PC. Setup downloads only the
components you selected and verifies each with SHA-256. The optional "LLM cleanup" feature (your
own API key, disabled by default) sends text only — never audio.

### 🛡️ Security & Verification (VirusTotal)
- **SHA-256:** `c97e23fe0cd720c570e44370f74d49fc74c961f106eecf11e48080d217d979c2`
- **VirusTotal Report:** [VirusTotal Result (1/72)](https://www.virustotal.com/gui/file/c97e23fe0cd720c570e44370f74d49fc74c961f106eecf11e48080d217d979c2/detection)
  *(100% clean across Microsoft Defender, Kaspersky, Bitdefender, and 70+ other engines. The single flag by DeepInstinct is a known heuristic false positive triggered by the global push-to-talk keyboard hook `WH_KEYBOARD_LL`, auto-paste `SendInput`, and downloading models during installation).*

### Uninstall
**Settings → Apps → TRWhisper.** You are asked whether to delete your dictation records
(`%USERPROFILE%\Dictation`).

### Silent installation
```powershell
TRWhisper-Setup-1.0.0.exe /SILENT /ENGINE=cuda /MODELS=turbo,small /TASKS=desktopicon
```

---

### Requirements / Gereksinimler
- Windows 11 x64 (Windows 10 1809+ çalışır / works)
- Mikrofon izni / microphone access
- İsteğe bağlı / optional: NVIDIA GPU, driver ≥ 551.61 (CUDA 12.4)

### SHA-256
`TRWhisper-Setup-1.0.0.exe` → kurulum dosyasının yanındaki `.sha256` dosyasına bakın /
see the `.sha256` file published next to the installer.
