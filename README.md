# TRWhisper: Local Push-to-Talk Dictation App for Windows 11 🎙️

[🇬🇧 English](README.md) | [🇹🇷 Türkçe](READMETR.md)

**TRWhisper** is a completely local, zero-telemetry "push-to-talk" dictation application built for Windows 11 as an alternative to Superwhisper.

---

## 🚀 Key Features

- **Global Hotkey**: By default, hold down **Right Ctrl** to record audio; releasing the key instantly transcribes the speech to text using local AI.
- **Smart Clipboard & Auto-Paste**:
  1. Backs up the current clipboard content in memory.
  2. Copies the transcribed text to the clipboard.
  3. Uses the Win32 `SendInput` API to simulate `Ctrl + V`, instantly pasting into the active focused field.
  4. Restores your original clipboard content after 150 ms (ensuring text or images in your clipboard are not lost).
- **Completely Offline & Private (STT)**: Powered by local `whisper.cpp` with the **Whisper Large-v3 Turbo** (`ggml-large-v3-turbo-q5_0.bin`) model. Your voice is never transmitted to any cloud or remote server.
- **Modern Floating Pill Overlay**: Real-time on-screen visual feedback during dictation (🔴 Dinliyor... / 🟡 Çözümleniyor... / 🟢 Tamamlandı).
- **Audio Artifact Filtering**: Automatically strips out non-speech hallucination tags (e.g. `[MÜZİK ÇALIYOR]`, `[SESSİZLİK]`, `[BLANK_AUDIO]`).
- **Optional LLM Cleanup Mode**: When speaking with the **Right Ctrl + Right Shift** combination, the transcript is sent to the Gemini or OpenAI API to clean up filler words (`um`, `uh`, `like`, `you know` / `ııı`, `eee`, `şey`, `yani`), correct punctuation, and paste the cleaned text.
- **System Tray**:
  - 🔵 **Blue**: Idle (Ready)
  - 🔴 **Red**: Recording (Speak)
  - 🟡 **Yellow**: Processing (Transcribing)
  - Context (right-click) menu: **Last 10 transcripts**, one-click copy, open dictation folder, settings, and exit.
- **Local Logging**: Every successful transcript is logged with a timestamp to `%USERPROFILE%\Dictation\YYYY-MM.md`. The temporary audio file is immediately deleted from disk.

---

## 🛠️ Installation & Requirements

### 1. Windows Microphone Permissions
Ensure microphone access is enabled in Windows 11:
1. **Settings (Win + I)** > **Privacy & security** > **Microphone**.
2. Toggle **"Microphone access"** to **On**.
3. Ensure **"Let desktop apps access your microphone"** is set to **On**.

### 2. Whisper Engine and Model Setup (Automatic)
Run the automated setup script with PowerShell from the project root directory:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1
```
This script:
- Creates the `tools\whisper\` directory.
- Downloads the latest Windows x64 binary of `whisper.cpp`.
- Downloads the `ggml-large-v3-turbo-q5_0.bin` (~547 MB) model via Hugging Face.
- Prepares the `%USERPROFILE%\Dictation\` folder.

> **Manual Download Alternative:**
> - Whisper Binary: [whisper.cpp Releases](https://github.com/ggerganov/whisper.cpp/releases) -> Download `whisper-bin-x64.zip` and extract `whisper-cli.exe` into `tools\whisper\`.
> - GGML Model: Download [Hugging Face ggml-large-v3-turbo-q5_0.bin](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin) and save it as `tools\whisper\ggml-large-v3-turbo-q5_0.bin`.

### 3. Bypassing Windows SmartScreen and Defender Warnings
Because TRWhisper uses a local low-level keyboard hook (`WH_KEYBOARD_LL`) and the `SendInput` API, Windows Defender or SmartScreen may display a warning on first launch:
1. **If the SmartScreen prompt appears:** Click **"More info"** and select **"Run anyway"**.
2. **Adding a Windows Defender Exclusion (Optional):**
   ```powershell
   # Open PowerShell as Administrator in the project directory and run:
   Add-MpPreference -ExclusionPath (Get-Location).Path
   # Or specify the full path: Add-MpPreference -ExclusionPath "C:\YourFolderPath\TRWHISPER"
   ```

---

## ⚙️ Configuration (`config.json`)

You can edit `config.json` in the application directory by opening it with Notepad, or by right-clicking the System Tray icon and selecting **"⚙️ Ayarları Aç"** (Open Settings):

```json
{
  "General": {
    "Language": "tr",
    "LogDirectory": "%USERPROFILE%\\Dictation",
    "TempAudioPath": "%TEMP%\\trwhisper_temp.wav"
  },
  "Whisper": {
    "CliPath": "tools\\whisper\\whisper-cli.exe",
    "ModelPath": "tools\\whisper\\ggml-large-v3-turbo-q5_0.bin",
    "Threads": 8,
    "NoTimestamps": true,
    "TimeoutSeconds": 120
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

> **Tip:** You can obtain a free Gemini API key from Google AI Studio and paste it into the `ApiKey` field. If left blank, the LLM cleaning mode will be disabled and the raw local transcript will be pasted directly.

---

## 🚀 Building and Running the Project

Using PowerShell:
```powershell
# Run in development mode
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Run

# Create a standalone Release package
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Publish
```
