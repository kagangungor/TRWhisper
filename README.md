# TRWhisper: Windows 11 Yerel Bas-Konuş Dikte Uygulaması 🎙️

**TRWhisper**, Windows 11 için geliştirilmiş, Superwhisper alternatifi, tamamen yerel çalışan, sıfır telemetrili bir "bas-konuş" (push-to-talk) dikte uygulamasıdır.

---

## 🚀 Temel Özellikler

- **Global Kısayol Tuşu**: Varsayılan olarak **Sağ Ctrl (Right Ctrl)** tuşuna basılı tuttuğunuzda konuşmayı kaydeder, tuşu bıraktığınız anda yerel yapay zeka ile metne dönüştürür.
- **Akıllı Pano ve Otomatik Yapıştırma**:
  1. O anki pano (clipboard) içeriğini belleğe yedekler.
  2. Transkript metnini panoya kopyalar.
  3. Win32 `SendInput` API'si ile `Ctrl + V` tuşlayarak imlecin bulunduğu aktif alana anında yapıştırır.
  4. 150 ms sonra orijinal panonuzu geri yükler (panonuzdaki metin/görseller kaybolmaz).
- **Tamamen Çevrimdışı & Güvenli (STT)**: `whisper.cpp` yerel motoru (`ggml-small.bin`) kullanır. Sesiniz hiçbir sunucuya veya buluta gönderilmez.
- **Opsiyonel LLM Temizleme Modu**: **Sağ Ctrl + Sağ Shift** kombinasyonuyla konuşursanız, transkript Gemini veya OpenAI API'sine iletilerek dolgu kelimeleri (`ııı`, `eee`, `şey`, `yani`) temizlenir, noktalama düzeltilir ve öyle yapıştırılır.
- **Sistem Tepsisi (System Tray)**:
  - 🔵 **Mavi**: Boşta (Hazır)
  - 🔴 **Kırmızı**: Kaydediyor (Konuşun)
  - 🟡 **Sarı**: Çözümlüyor (Transkribe ediliyor)
  - Sağ tık menüsünde: **Son 10 transkript**, tek tıkla kopyalama, dikte klasörünü açma, ayarlar ve çıkış.
- **Yerel Günlük**: Her başarılı transkript zaman damgasıyla `%USERPROFILE%\Dictation\YYYY-MM.md` dosyasına kaydedilir. Geçici ses dosyası diskten derhal silinir.

---

## 🛠️ Kurulum ve Gereksinimler

### 1. Windows Mikrofon İzinleri
Windows 11'de mikrofon erişiminin açık olduğundan emin olun:
1. **Ayarlar (Win + I)** > **Gizlilik ve Güvenlik** > **Mikrofon**.
2. **"Mikrofon erişimi"** seçeneğini açık konuma getirin.
3. **"Masaüstü uygulamalarının mikrofonunuza erişmesine izin verin"** seçeneğinin **Açık** olduğundan emin olun.

### 2. Whisper Motoru ve Model Kurulumu (Otomatik)
Proje kök dizinindeyken PowerShell ile otomatik kurulum betiğini çalıştırın:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1
```
Bu betik:
- `tools\whisper\` klasörünü oluşturur.
- Son sürüm `whisper.cpp` Windows x64 binary'sini indirir.
- Hugging Face üzerinden Türkçe için optimize edilmiş `ggml-small.bin` (~465 MB) modelini indirir.
- `%USERPROFILE%\Dictation\` klasörünü hazırlar.

> **Manuel İndirmek İsterseniz:**
> - Whisper Binary: [whisper.cpp Releases](https://github.com/ggerganov/whisper.cpp/releases) -> `whisper-bin-x64.zip` indirip içindeki `whisper-cli.exe` dosyasını `tools\whisper\` içine atın.
> - GGML Modeli: [Hugging Face ggml-small.bin](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin) dosyasını indirip `tools\whisper\ggml-small.bin` olarak kaydedin.

### 3. Windows SmartScreen ve Defender Uyarılarını Geçme
TRWhisper yerel bir low-level klavye hook (`WH_KEYBOARD_LL`) ve `SendInput` API'si kullandığı için Windows Defender veya SmartScreen ilk çalıştırmada koruma uyarısı verebilir:
1. **SmartScreen penceresi çıkarsa:** **"Ek Bilgi" (More Info)** butonuna tıklayın ve **"Yine de çalıştır" (Run anyway)** seçeneğini seçin.
2. **Windows Defender İstisnası Eklemek (Opsiyonel):**
   ```powershell
   # PowerShell'i Yönetici olarak proje dizininde açıp istisnalara ekleyebilirsiniz:
   Add-MpPreference -ExclusionPath (Get-Location).Path
   # veya tam yol belirterek: Add-MpPreference -ExclusionPath "C:\KlasorYolunuz\TRWHISPER"
   ```

---

## ⚙️ Yapılandırma (`config.json`)

Uygulama dizinindeki `config.json` dosyasını Not Defteri ile açarak veya Sistem Tepsisi simgesine sağ tıklayıp **"⚙️ Ayarları Aç"** diyerek düzenleyebilirsiniz:

```json
{
  "General": {
    "Language": "tr",
    "LogDirectory": "%USERPROFILE%\\Dictation",
    "TempAudioPath": "%TEMP%\\trwhisper_temp.wav"
  },
  "Whisper": {
    "CliPath": "tools\\whisper\\whisper-cli.exe",
    "ModelPath": "tools\\whisper\\ggml-small.bin",
    "Threads": 4,
    "NoTimestamps": true
  },
  "LlmCleaning": {
    "EnabledByDefault": false,
    "Provider": "Gemini",
    "ApiKey": "AIzaSy...",
    "Model": "gemini-2.5-flash",
    "Endpoint": "https://generativelanguage.googleapis.com/v1beta/models",
    "SystemPrompt": "Aşağıdaki metin Türkçe sesli dikte çıktısıdır. Dolgu kelimelerini (ııı, eee, şey, yani) temizle, noktalama ve imlayı düzelt. YALNIZCA düzeltilmiş metni döndür."
  },
  "PasteSettings": {
    "RestoreClipboardDelayMs": 150
  }
}
```

> **İpucu:** Gemini API anahtarınızı Google AI Studio üzerinden ücretsiz alıp `ApiKey` alanına yapıştırabilirsiniz. Anahtar boş bırakılırsa LLM temizleme modu devre dışı kalır ve doğrudan yerel ham transkript yapıştırılır.

---

## 🚀 Projeyi Derleme ve Çalıştırma

PowerShell ile:
```powershell
# Geliştirme modunda çalıştırma
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Run

# Bağımsız Release paketi oluşturma
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Publish
```
