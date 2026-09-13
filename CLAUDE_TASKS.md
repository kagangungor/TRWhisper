# Claude Code Geliştirme Görevleri: TRWhisper Win32, Ses ve STT Çekirdeği

Bu dosya, Claude Code (Claude 3.7 Sonnet) terminalinde uygulanacak düşük seviyeli Win32 interop, ses kaydı ve CLI transkripsiyon sınıflarının detaylarını içerir.

Gemini tarafından proje mimarisi, `.csproj`, arayüzler (`IKeyboardHook`, `IClipboardPaster`, `IAudioRecorder`, `ITranscriptionEngine`), `DictationCoordinator`, `LlmCleanerService`, `ConfigManager`, `MarkdownLogger`, `TrayIconController`, `Program.cs` ve scriptler tamamlanmıştır.

---

## 🎯 Uygulanacak 4 Temel Sınıf

### 1. `src/TRWhisper/Core/Native/Win32KeyboardHook.cs`
- **Arayüz:** `IKeyboardHook` (`HotkeyDown`, `HotkeyUp`, `Start()`, `Stop()`, `IsHotkeyHeld`)
- **Gereksinimler:**
  - `SetWindowsHookEx(WH_KEYBOARD_LL = 13, ...)` ve `UnhookWindowsHookEx`.
  - GC'nin callback delegate'ini toplamaması için static pinned / referanslı `LowLevelKeyboardProc` delegate tanımı.
  - Tuş ayrımı:
    - Sağ Ctrl: `vkCode == 0xA3 (VK_RCONTROL)` ve `(flags & 1) != 0 (LLKHF_EXTENDED)`.
    - Sağ Shift: `vkCode == 0xA1 (VK_RSHIFT)` veya `GetAsyncKeyState(VK_RSHIFT)`.
  - **Key-Repeat Filtresi:** Windows, tuşa basılı tutulduğunda sürekli `WM_KEYDOWN` üretir. `IsHotkeyHeld` flag'i ile sadece İLK basışta `HotkeyDown` tetiklenmeli, tekrarlar yutulmalıdır.
  - Tuş bırakıldığında (`WM_KEYUP` veya `WM_SYSKEYUP`): `IsHotkeyHeld = false` yapılmalı ve `HotkeyUp` fırlatılmalıdır (`IsLlmModifierActive` parametresiyle).
  - Diğer tüm tuşlar için her zaman `CallNextHookEx` çağrılmalıdır.

---

### 2. `src/TRWhisper/Core/Native/ClipboardPaster.cs`
- **Arayüz:** `IClipboardPaster` (`Task PasteTextAsync(string text, int restoreDelayMs = 150)`)
- **Gereksinimler:**
  - **Pano Yedekleme:** `System.Windows.Forms.Clipboard.GetDataObject()` ile mevcut pano içeriğini saklayın. Pano o sırada kilitliyse (`CLIPBRD_E_CANT_OPEN`) 3 defaya kadar 30ms aralıklarla retry yapın.
  - **Yeni Metni Yazma:** `Clipboard.SetDataObject(text, true)`.
  - **Tuş Gönderme (`SendInput`):**
    - Kullanıcının fiziksel Sağ Ctrl tuşunu serbest bıraktığından emin olun (gerekirse sanal Right Ctrl KEYUP gönderin).
    - `VK_CONTROL` (down) -> `VK_V` (down) -> `VK_V` (up) -> `VK_CONTROL` (up) sıralamasını tek bir `SendInput` çağrısında (veya ardışık) gönderin.
  - **Gecikme & Geri Yükleme:** `await Task.Delay(restoreDelayMs)` beklemesi sonrası orijinal panoyu geri yükleyin.

---

### 3. `src/TRWhisper/Core/Audio/WasapiRecorder.cs`
- **Arayüz:** `IAudioRecorder` (`StartRecording(string outputWavPath)`, `Task<string> StopRecordingAsync()`, `IsRecording`)
- **Gereksinimler:**
  - `NAudio.Wave.WasapiCapture` kullanarak Windows varsayılan kayıt cihazından ses yakalayın.
  - Whisper'ın kabul ettiği standart format: **16.000 Hz, 16-bit, Mono PCM WAV**.
  - WasapiCapture genellikle 44.1kHz veya 48kHz IEEE Float (32-bit) üretir. Bu yüzden NAudio'nun `MediaFoundationResampler` veya `BufferedWaveProvider` + `WaveFormatConversionStream` (veya `WaveFileWriter` öncesi format dönüştürme) kullanarak 16kHz 16-bit mono'ya çevirin.
  - `StopRecordingAsync` çağrıldığında `WasapiCapture.StopRecording()` tetiklenmeli, `RecordingStopped` olayı beklenmeli, WAV dosya tanıtıcısı (`WaveFileWriter`) kapatılıp `Dispose` edilmeli ve dosya yolunu dönmelidir. Dosya tanıtıcısının açık kalmamasına dikkat edin.

---

### 4. `src/TRWhisper/Core/Speech/WhisperCliRunner.cs`
- **Arayüz:** `ITranscriptionEngine` (`Task<string> TranscribeAsync(string wavFilePath, string language = "tr", CancellationToken cancellationToken = default)`)
- **Gereksinimler:**
  - `config.json`'dan okunan `ResolvedCliPath` (`whisper-cli.exe`) ve `ResolvedModelPath` (`ggml-small.bin`) kullanın.
  - Argümanlar:
    `-m "{modelPath}" -f "{wavFilePath}" -l {language} -t {threads} -nt --output-txt`
  - `ProcessStartInfo`: `RedirectStandardOutput = true`, `RedirectStandardError = true`, `UseShellExecute = false`, `CreateNoWindow = true`.
  - Deadlock'ları önlemek için stdout/stderr okumasını `Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync())` şeklinde async yapın.
  - Çıktıdaki `[00:00:00.000 --> 00:00:02.000]` gibi zaman damgaları veya köşeli parantez temizliklerini regex ile ayıklayın. Temizlenmiş transkripti dönün.

---

## 🛠️ Doğrulama Komutu
Tüm dosyalar tamamlandığında projeyi derlemek için:
```powershell
$env:Path = "$env:LocalAppData\Microsoft\dotnet;$env:Path"
dotnet build src\TRWhisper\TRWhisper.csproj
```
Derleme başarılı olduktan sonra `scripts\build.ps1 -Run` ile test edebilirsiniz.
