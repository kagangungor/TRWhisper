# TRWhisper 2.0.0 — Büyük Sürüm / Major Release 🚀

Windows 11 için tamamen yerel çalışan Türkçe bas-konuş dikte uygulamasının 2.0.0 sürümü; modern grafiksel ayarlar arayüzü, özel sözlük desteği, gelişmiş Türkçe metin normalizasyonu, bağlama duyarlı LLM temizleme (ve yerel Ollama entegrasyonu), süreç içi Whisper.net motoru ve eller serbest dikte özellikleriyle karşınızda!

---

## 🇹🇷 Türkçe Sürüm Notları

### 🌟 2.0.0 ile Gelen Başlıca Yenilikler

1. **⚙️ Modern Grafiksel Ayarlar Penceresi (WPF)**:
   - Sistem tepsisinden tek tıkla açılan zengin ve modern ayarlar paneli.
   - **Genel**, **Ses**, **Model**, **Kısayol**, **Sözlük**, **Yapay Zeka (AI / LLM)** ve **Kapsül (Pill Overlay)** sekmeleriyle tüm yapılandırmayı görsel olarak yönetme.
   - Canlı bağlantı testi, API anahtarını maskeleme/gösterme ve arayüz üzerinden tek tıkla model indirme/silme.

2. **📖 Özel Sözlük & Jargon Desteği (`CustomDictionaryService`)**:
   - Kullanıcı tanımlı mesleki jargon, teknik terimler, özel isimler ve kısaltmalar (`dictionary.json`).
   - Whisper'ın yanlış algılayabileceği kelimeleri kelime sınırlarına duyarlı (word boundary) akıllı eşleme ile otomatik olarak doğru hallerine çevirme.

3. **✍️ Gelişmiş Türkçe Metin Normalizasyonu & Kekeleme Filtresi**:
   - Cümle başı büyük harf uyumu ve noktalama düzeltmeleri.
   - Türkçe de/da ve ki bağlaç kontrolleri.
   - Konuşma esnasında tekrarlanan dolgu/kekeleme kelimelerini temizleme (`ve ve ve` -> `ve`).

4. **🤖 Bağlama Duyarlı (Context-Aware) LLM Modları & Uygulama Algılama**:
   - Farklı kullanım modları: **Ham Metin**, **Temizle** (dolgu kelimeleri at), **Özetle**, **Resmi Dil** ve **Madde İmleri**.
   - Aktif pencere algılama (`ForegroundAppDetector`): Kod editörleri (VS Code), e-posta istemcileri, mesajlaşma (Discord, Slack) veya doküman editörlerine göre dil tonunu ve sistem prompt'unu otomatik uyarlama.

5. **🦙 %100 Yerel & Gizli LLM Desteği (Ollama Entegrasyonu)**:
   - Bulut API'leri (Gemini/OpenAI) yerine yerel **Ollama** motoru ile (`http://localhost:11434`, `qwen2.5:3b`, `llama3.2`) API anahtarı olmadan tamamen çevrimdışı ve ücretsiz LLM temizleme.

6. **🔒 Donanım Seviyesinde Güvenli API Anahtarı Saklama (Windows DPAPI)**:
   - API anahtarlarınız düz metin yerine Windows Data Protection API ile donanım ve kullanıcı hesabınıza özel şifrelenir.

7. **🚀 Whisper.net Yerleşik C# Motoru & Dinamik Bellek Yönetimi**:
   - CLI'a ek olarak yüksek performanslı süreç içi Whisper.net motoru.
   - Sistem boşta kaldığında RAM ve GPU VRAM tasarrufu sağlayan otomatik model boşaltma zamanlayıcısı (`IdleTimeoutMinutes`).
   - Tüm model kataloğu: **Large-v3 Turbo**, **Small**, **Base**, **Tiny**, **Medium**, **Large-v3**.

8. **🎙️ Esnek Kısayol Tuşları & Eller Serbest (Hands-Free) Dikte**:
   - Bas-konuş tuşunu ve LLM değiştiricisini dilediğiniz tuşlara atama (Sağ/Sol Ctrl, Shift, Alt, F tuşları).
   - Akıllı sessizlik algılamasıyla tuşa basılı tutmadan konuşmanızı tamamlayınca otomatik duran **Eller Serbest** modu.

9. **📋 Akıllı Pano & Ses Giriş Cihazı Seçimi**:
   - İstenilen mikrofonu seçebilme ve otomatik stereo-mono miksaj.
   - Dikte yapıştırıldıktan sonra panonun eski içeriğini otomatik geri yükleme (`RestoreClipboard`).

10. **🧪 Kapsamlı Test Paketi**:
    - 87 adet otomatik birim testi ve XAML şablon duman testi (`uismoke`) ile sıfır gerileme garantisi.

---

### Kurulum

1. Aşağıdaki **TRWhisper-Setup-2.0.0.exe** dosyasını indirin (~51 MB).
2. Çalıştırın. Yönetici şifresi gerekmez (kullanıcı profiline kurulur).
3. Windows SmartScreen uyarısı çıkarsa: **Daha fazla bilgi → Yine de çalıştır**.

### 🛡️ Güvenlik & Doğrulama
- **Dosya:** `TRWhisper-Setup-2.0.0.exe`
- **Boyut:** ~51.3 MB (53,816,507 bayt)
- **SHA-256:** `3CAAB66A8A72207E35374F07294B887AFF7EB725D9134CD124C2B451BDB7784E`

---

## 🇬🇧 English Release Notes

### 🌟 What's New in 2.0.0

1. **⚙️ Modern Graphical Settings Window (WPF)**:
   - Full graphical settings panel accessible via system tray.
   - Tabs for **General**, **Audio**, **Model**, **Hotkey**, **Dictionary**, **AI / LLM**, and **Overlay Pill**.
   - Real-time connection testing, password mask toggling, and in-app model download/delete management.

2. **📖 Custom Jargon & Phonetic Dictionary (`CustomDictionaryService`)**:
   - User-defined phonetic and jargon replacements in `dictionary.json`.
   - Word-boundary aware automatic replacement of domain-specific terminology, acronyms, and company names.

3. **✍️ Advanced Turkish Text Normalization & Stutter Cleaning**:
   - Sentence-start capitalization and punctuation validation.
   - Automatic correction of Turkish suffixes and conjunctions (de/da, ki).
   - Speech stutter and repetitive word filter (`ve ve ve` -> `ve`).

4. **🤖 Context-Aware LLM Modes & Window Detection**:
   - Transcription styles: **Raw Text**, **Clean** (filler removal), **Summarize**, **Formal Tone**, and **Bullet Points**.
   - Foreground application detection: Intelligently adapts LLM personas for VS Code, email, Discord, Slack, and Word.

5. **🦙 100% Local & Private LLM Cleanup (Ollama Integration)**:
   - Air-gapped local LLM cleaning using **Ollama** (`http://localhost:11434`, `qwen2.5:3b`, `llama3.2`) with zero telemetry and no API keys required.

6. **🔒 Hardware-Backed Secret Storage (Windows DPAPI)**:
   - API keys are encrypted at rest with Windows Data Protection API instead of plain text storage.

7. **🚀 In-Process Whisper.net Engine & Idle Memory Management**:
   - High performance native in-process Whisper.net C# engine.
   - Automatic idle memory / VRAM unloading timer (`IdleTimeoutMinutes`).
   - Full model catalog: **Large-v3 Turbo**, **Small**, **Base**, **Tiny**, **Medium**, and **Large-v3**.

8. **🎙️ Flexible Hotkeys & Hands-Free Dictation**:
   - Customizable Push-to-Talk keys and modifier keys.
   - Hands-Free toggle mode with intelligent silence threshold detection.

9. **📋 Smart Clipboard & Audio Device Picker**:
   - Select any input microphone with stereo-to-mono downmixing.
   - Optional clipboard restoration after pasting (`RestoreClipboard`).

10. **🧪 Comprehensive Test Suite**:
    - 87 automated unit tests and XAML UI smoke tests guaranteeing reliability.

---

### Installation
1. Download **TRWhisper-Setup-2.0.0.exe** (~51 MB).
2. Run the installer (no admin rights required).
3. If Windows SmartScreen appears: **More info → Run anyway**.

### 🛡️ Checksums & Integrity
- **File:** `TRWhisper-Setup-2.0.0.exe`
- **Size:** ~51.3 MB (53,816,507 bytes)
- **SHA-256:** `3CAAB66A8A72207E35374F07294B887AFF7EB725D9134CD124C2B451BDB7784E`
