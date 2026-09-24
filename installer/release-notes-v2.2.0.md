# TRWhisper 2.2.0 — Akıllı Dikte ve Güvenlik / Smarter Dictation & Security 🛡️

TRWhisper 2.2.0; sesli noktalama, diller arası hızlı geçiş, yeni LLM sağlayıcıları, AI Asistanı modu, dikte geçmişi ve sesli geri bildirim getiriyor. Kapsamlı bir güvenlik denetiminde bulunan dokuz sorunun hepsi bu sürümde düzeltildi. Tüm kullanıcıların güncellemesi tavsiye edilir; mevcut `config.json` ve sözlüğünüz korunur, yeni özelliklerin çoğu kapalı başlar.

---

## 🇹🇷 Türkçe Sürüm Notları

### ✨ Yenilikler

1. **🗣️ Sesli Noktalama**
   - `nokta`, `virgül`, `soru işareti`, `ünlem`, `iki nokta`, `noktalı virgül`, `üç nokta`, `tire`, `uzun çizgi`, `yeni satır`, `yeni paragraf`, `parantez aç/kapa`, `tırnak aç/kapa` dediğinizde işaretin kendisi yazılır.
   - Komutun yanındaki Whisper noktalaması yutulur, işaretler iki kez yazılmaz. Ayarlar → Genel'den kapatılabilir (`EnableSpokenPunctuation`).

2. **🌐 Hızlı Dil Geçişi**
   - Bir kısayolla (varsayılan **Alt+L**) dikte dili, seçtiğiniz diller arasında kendi belirlediğiniz sırayla değişir: Türkçe, İngilizce, Almanca, Fransızca, Otomatik algılama.
   - Kapsül yeni dili kısa süre gösterir; dil tepsi menüsünden de değiştirilebilir. Varsayılan kapalıdır.
   - Sayı/tarih normalizasyonu artık yalnızca Türkçe çıktıya uygulanır; İngilizce metin Türkçe kurallarla bozulmaz.

3. **🤖 AI Asistanı Modu**
   - Metni temizlemek yerine dikte ettiğiniz talimatı ya da soruyu uygular ve yanıtı yapıştırır.
   - Yanıt uzunluk sınırında kesilirse sessizce yarım metin yapıştırılmaz; uyarı gösterilir.

4. **🔌 Yeni LLM Sağlayıcıları**
   - **Groq**, **DeepSeek**, **Anthropic Claude** (varsayılan `claude-haiku-4-5`) ve **OpenAI uyumlu herhangi bir uç nokta** (LM Studio, OpenRouter vb.).
   - Sağlayıcı seçilince varsayılan uç nokta ve model kendiliğinden dolar; "Anahtar Panelini Aç" her sağlayıcının kendi sayfasına gider.

5. **📜 Dikte Geçmişi**
   - **Ayarlar → Yedekleme ve Geçmiş → Geçmiş**: Türkçe harflere duyarlı arama, Bugün / Bu Hafta / Bu Ay süzgeçleri, kelime sayıları ve tahmini "kazanılan süre" özeti.
   - Ayrıntı görünümü temizlenmiş metni ve ham metni birlikte gösterir.

6. **🔔 Sesli Geri Bildirim**
   - Başlama, başarı ve iptal/hata için kısa, yumuşak tınılar; ses düzeyi ayarı ve **Sesi Test Et** düğmesi. Varsayılan kapalıdır.

7. **🔄 Güncelleme Denetimi**
   - Açıksa, açılıştan 10 saniye sonra GitHub'daki son sürüm denetlenir ve yeni sürüm varsa tepsi menüsünde bir öğe belirir. Ayarlar'da elle **Şimdi Denetle** düğmesi de vardır.
   - Hiçbir şey kendiliğinden indirilmez veya kurulmaz. Varsayılan kapalıdır.
   - Not: Bu özellik 2.2.0 ile geldiği için uygulama içi bildirim bir sonraki sürümden itibaren işe yarar.

8. **🎨 Arayüz**
   - Sekmeler sadeleşti: **Model ve Yapay Zeka** ile **Yedekleme ve Geçmiş** tek sekmede, üstte bölüm düğmeleriyle.
   - Model sekmesinde canlı **bellek kartı**: modelin yüklü olup olmadığı, etkin model ve dil, RAM kullanımı, GPU/CPU modu ve modeli anında boşaltan düğme.
   - İsteğe bağlı Windows 11 **Mica** arka planı ve kapsülün ekran kenarlarına **kenetlenmesi** (ikisi de varsayılan kapalı).

### 🛡️ Güvenlik Düzeltmeleri

1. **Yedekten geri yükleme artık bağlantı ayarlarını değiştiremez.** API anahtarı, yapay zeka sağlayıcısı/uç noktası/modeli, "her dikteyi LLM ile temizle" seçeneği, Whisper dosya yolları ve klasörler yedekten alınmaz. Önceden, hazırlanmış bir yedek korunan anahtarınızı ve dikteleri başka bir sunucuya yönlendirebilir ya da modeli bir ağ paylaşımından yükletebilirdi. Onay penceresi yalnızca güvendiğiniz yedekleri geri yüklemenizi hatırlatır.
2. **Kurulum dosyasının yanına konan CUDA paketi** artık indirilen paketle aynı SHA-256 doğrulamasından geçer; tutmazsa yok sayılır ve paket internetten indirilir.
3. **Terminallere yapıştırma:** Hedef bir konsolsa (cmd, PowerShell, Windows Terminal, Git Bash, ConEmu, PuTTY) satır sonları boşluğa çevrilir; çok satırlı bir yanıt ya da "yeni satır" komutu, siz okumadan komut çalıştıramaz.
4. **Uzak Ollama yerel sayılmaz:** Yalnızca `localhost`'taki sağlayıcılar yerel kabul edilir; başka bir bilgisayardaki sunucuda bulut uyarısı ve günlük tavan uygulanır.
5. **Model ve araç dosyaları** yalnızca güvenilir klasörlerde aranır. Program Files kurulumunda başka bir kullanıcının `C:\tools\` gibi bir yere bıraktığı model artık yüklenmez.
6. **Visual C++ Runtime** kurulum dosyası çalıştırılmadan önce Microsoft imzası doğrulanır.
7. **Zip bombası koruması:** Geri yüklemede boyut sınırları var; sınırı aşan bir yedek hiçbir şey değiştirilmeden reddedilir.
8. **Gizlilik:** Geçmiş kaydı kapalıysa dikte günlükleri yedeğe alınmaz; yedek klasörü OneDrive'daysa Ayarlar uyarır.
9. **Güncelleme bağlantıları** yalnızca `github.com/kagangungor/TRWhisper` adreslerini açar.

### 🐞 Hata Düzeltmeleri
- Sessiz kurulumda (`/VERYSILENT`) bazı sorular kurulumu sonsuza dek bekletiyordu; artık komut satırı seçimi esas alınır ve sorunlar günlüğe yazılır.
- Açılır listelerde seçili satırdaki g, y, j harflerinin alt kısmı kesiliyordu.
- LLM yanıtı uzunluk sınırında kesildiğinde temizleme modları yarım metin yerine orijinal transkripti yazar.

### ✅ Kalite
- 420 otomatik birim testi (önceki sürümde 152) ve güncellenmiş `uismoke` arayüz duman testi.

### 📦 Kurulum & Doğrulama
- **Dosya:** `TRWhisper-Setup-2.2.0.exe`
- **Boyut:** ~51.8 MB (54,338,304 bayt)
- **SHA-256:** `7393AE8FE65B9E946AB5979562695281124433CE6D43F296D594EB42CDC31621`
- **İsteğe bağlı CUDA 13 paketi:** Değişmedi. Kurulum, GPU seçildiğinde [v2.0.0 sürümündeki](https://github.com/kagangungor/TRWhisper/releases/tag/v2.0.0) `trwhisper-cuda13-win-x64.zip` dosyasını SHA-256 doğrulamasıyla indirir.

```powershell
Get-FileHash .\TRWhisper-Setup-2.2.0.exe -Algorithm SHA256
```

> Kurulum dosyası Authenticode ile imzalı değildir. SmartScreen uyarısı bu yüzden çıkar. İndirdiğiniz dosyayı yukarıdaki özetle doğrulayın.

---

## 🇬🇧 English Release Notes

### ✨ What's New

1. **🗣️ Spoken Punctuation**
   - Say `nokta`, `virgül`, `soru işareti`, `ünlem`, `iki nokta`, `noktalı virgül`, `üç nokta`, `tire`, `uzun çizgi`, `yeni satır`, `yeni paragraf`, `parantez aç/kapa`, `tırnak aç/kapa` and the mark itself is typed.
   - Whisper's own punctuation next to a command is absorbed, so marks are never doubled. It can be turned off in Settings → General (`EnableSpokenPunctuation`).

2. **🌐 Fast Language Switch**
   - A hotkey (default **Alt+L**) cycles the dictation language through the languages you choose, in your own order: Turkish, English, German, French, Auto-detect.
   - The pill briefly shows the new language, and it can also be changed from the tray menu. Off by default.
   - Number/date normalization now only applies to Turkish output, so English text is no longer altered by Turkish rules.

3. **🤖 AI Assistant Mode**
   - Instead of cleaning the text, it carries out the instruction or question you dictate and pastes the answer.
   - If an answer is cut off at the length limit, a warning is shown instead of silently pasting half a reply.

4. **🔌 New LLM Providers**
   - **Groq**, **DeepSeek**, **Anthropic Claude** (default `claude-haiku-4-5`) and **any OpenAI-compatible endpoint** (LM Studio, OpenRouter, …).
   - Choosing a provider fills in its default endpoint and model; "Open key console" goes to each provider's own page.

5. **📜 Dictation History**
   - **Settings → Backup & History → History**: Turkish-aware search, Today / This Week / This Month filters, word counts and an estimated "time saved" summary.
   - The detail view shows the cleaned and the raw text side by side.

6. **🔔 Sound Feedback**
   - Short, soft tones for start, success and cancel/error, with a volume setting and a **Test sound** button. Off by default.

7. **🔄 Update Check**
   - When enabled, the latest GitHub release is checked 10 seconds after startup and a tray item appears if a newer version exists. There is also a manual **Check now** button in Settings.
   - Nothing is downloaded or installed automatically. Off by default.
   - Note: since this feature arrives with 2.2.0, in-app notifications start working from the next release.

8. **🎨 Interface**
   - Simplified tabs: **Model & AI** and **Backup & History** are now single tabs with section buttons at the top.
   - A live **memory card** in the Model tab: whether the model is loaded, the active model and language, RAM usage, GPU/CPU mode, and a button to unload the model immediately.
   - Optional Windows 11 **Mica** background and **edge snapping** for the pill (both off by default).

### 🛡️ Security Fixes

1. **Restoring a backup can no longer change connection settings.** The API key, the AI provider/endpoint/model, "clean every dictation with the LLM", Whisper file paths and folders are never taken from a backup. Previously, a crafted backup could redirect your preserved key and your dictations to another server, or load a model from a network share. The confirmation dialog reminds you to restore only backups you trust.
2. **A CUDA package placed next to the setup file** now goes through the same SHA-256 check as a downloaded one; if it does not match, it is ignored and the package is downloaded instead.
3. **Pasting into terminals:** when the target is a console (cmd, PowerShell, Windows Terminal, Git Bash, ConEmu, PuTTY), line breaks become spaces, so a multi-line answer or a "yeni satır" command can't run a command before you have read it.
4. **A remote Ollama is no longer treated as local:** only providers on `localhost` count as local; a server on another computer gets the cloud warning and the daily cap.
5. **Model and tool files** are only looked up in trusted folders. In a Program Files install, a model another user placed somewhere like `C:\tools\` is no longer loaded.
6. **The Visual C++ Runtime** installer's Microsoft signature is verified before it is run.
7. **Zip-bomb protection:** restores have size limits; an oversized backup is rejected before anything is changed.
8. **Privacy:** dictation logs are left out of backups when history logging is off, and Settings warns you if the backup folder is synced by OneDrive.
9. **Update links** only open `github.com/kagangungor/TRWhisper` addresses.

### 🐞 Bug Fixes
- Some prompts could leave a silent install (`/VERYSILENT`) waiting forever; the command-line choice is now used and problems are written to the log.
- The descenders of g, y and j were clipped on the selected row of drop-down lists.
- When an LLM reply is cut off at the length limit, cleanup modes paste the original transcript instead of half a sentence.

### ✅ Quality
- 420 automated unit tests (up from 152) and an updated `uismoke` UI smoke test.

### 📦 Installation & Verification
- **File:** `TRWhisper-Setup-2.2.0.exe`
- **Size:** ~51.8 MB (54,338,304 bytes)
- **SHA-256:** `7393AE8FE65B9E946AB5979562695281124433CE6D43F296D594EB42CDC31621`
- **Optional CUDA 13 package:** unchanged. When you choose GPU, the installer downloads `trwhisper-cuda13-win-x64.zip` from the [v2.0.0 release](https://github.com/kagangungor/TRWhisper/releases/tag/v2.0.0) and verifies it with SHA-256.

```powershell
Get-FileHash .\TRWhisper-Setup-2.2.0.exe -Algorithm SHA256
```

> The installer is not Authenticode-signed, which is why SmartScreen shows a warning. Verify your download against the digest above.
