# TRWhisper: Local Push-to-Talk Dictation App for Windows 11 🎙️

[🇬🇧 English](README.md) | [🇹🇷 Türkçe](READMETR.md)

**TRWhisper** is a completely local, zero-telemetry "push-to-talk" dictation application built for Windows 11 as an alternative to Superwhisper. It is tuned for Turkish dictation.

---

## 🚀 Key Features

- **Global Hotkey**: Hold down **Right Ctrl** to record; release it and the speech is transcribed locally.
- **Auto-Paste**:
  1. Copies the transcribed text to the clipboard.
  2. Uses the Win32 `SendInput` API to simulate `Ctrl + V`, pasting into the currently focused field.
  3. The text stays on the clipboard, so you can paste it again elsewhere with `Ctrl + V` (the previous clipboard content is replaced; with Windows clipboard history enabled it remains available via `Win + V`).
- **Completely Offline & Private (STT)**: Powered by a local `whisper.cpp` engine. Your voice is never sent to any server.
- **NVIDIA GPU Acceleration (optional)**: With the CUDA build of `whisper.cpp`, the **Large-v3 Turbo** model transcribes a short dictation in ~2–3 seconds instead of ~23 seconds on the CPU (see [Performance](#-performance)). Without a GPU it automatically falls back to the CPU.
- **Model Selection from the Tray**: Switch between **Small** (faster on CPU, less accurate) and **Large-v3 Turbo** (most accurate) at any time — no restart needed. Models whose file is missing are greyed out, and selecting Turbo on a machine without a usable NVIDIA GPU shows a "may be slow" warning.
- **Silence Detection & Hallucination Filter**:
  - The built-in **Silero VAD** of `whisper.cpp` skips parts without speech. If you press the hotkey without speaking, nothing is pasted (instead of phantom text like "Altyazı M.K.").
  - Known Whisper hallucinations (e.g. `Altyazı M.K.`, `İzlediğiniz için teşekkür ederim.`) and non-speech tags (`[MÜZİK ÇALIYOR]`, `(Müzik)`, `[BLANK_AUDIO]`) are removed — only when they make up a whole line, so real dictation is never cut.
- **Floating Pill Overlay**: Shows **Dinleniyor** (listening) while recording and **Çözümleniyor...** (transcribing) while processing, with cancel (✕) and finish (✓) buttons. When done, it shows the transcript with a **Kopyala** (copy) button.
- **Optional LLM Cleanup Mode**: Hold **Right Ctrl + Shift** while speaking (or turn on **✨ LLM Temizleme** in the tray menu) and the transcript is sent to the Gemini or OpenAI API to remove filler words (`ııı`, `eee`, `şey`, `yani`) and fix punctuation before pasting.
- **System Tray**:
  - 🔵 **Blue**: Idle (ready)
  - 🔴 **Red**: Recording (speak)
  - 🟡 **Amber**: Transcribing
  - Right-click menu: LLM cleanup toggle, **🧠 Whisper model**, **last 10 transcripts** (click to copy), open dictation folder, open settings, and exit.
- **Local Logging**: Every transcript is saved with a timestamp to `%USERPROFILE%\Dictation\YYYY-MM.md`. The temporary audio file is deleted immediately. Diagnostic messages go to `%USERPROFILE%\Dictation\trwhisper.log`.

---

## 🛠️ Installation & Requirements

**Requirements**
- Windows 11 x64
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (to build from source)
- Optional: an NVIDIA GPU with an up-to-date driver (CUDA 12.4 support) for GPU acceleration

### 1. Windows Microphone Permissions
Ensure microphone access is enabled in Windows 11:
1. **Settings (Win + I)** > **Privacy & security** > **Microphone**.
2. Toggle **"Microphone access"** to **On**.
3. Ensure **"Let desktop apps access your microphone"** is set to **On**.

### 2. Whisper Engine and Model Setup (Automatic)
Run the setup script with PowerShell from the project root directory:

```powershell
# CPU build + Large-v3 Turbo model + VAD model
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1

# NVIDIA GPU (CUDA 12.4) build instead — recommended if you have an NVIDIA GPU
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Cuda

# Additionally download the Small model (to switch between models from the tray)
powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Model small
```

The script:
- Creates the `tools\whisper\` and `%USERPROFILE%\Dictation\` folders.
- Downloads the tested `whisper.cpp` **v1.9.3** (build `b4938`) Windows x64 binaries — the CPU build (~8 MB) or, with `-Cuda`, the CUDA 12.4 build (~640 MB download, ~1.2 GB extracted). Running it again with `-Cuda` upgrades an existing CPU installation.
- Downloads the selected model (default `large-v3-turbo-q5_0`) and the Silero VAD model.
- Skips anything that already exists.

| File | Size | Purpose |
|---|---|---|
| `ggml-large-v3-turbo-q5_0.bin` | ~547 MB | Default model, most accurate |
| `ggml-small.bin` | ~465 MB | Faster on CPU, less accurate |
| `ggml-silero-v6.2.0.bin` | ~1 MB | Voice activity detection (silence filter) |

> **Manual Download Alternative:**
> - Whisper binaries ([release b4938](https://github.com/ggml-org/whisper.cpp/releases/tag/b4938)): download `whisper-bin-x64.zip` (CPU) or `whisper-cublas-12.4.0-bin-x64.zip` (NVIDIA GPU) and extract **all files** from its `Release` folder into `tools\whisper\`.
> - Models: [ggml-large-v3-turbo-q5_0.bin](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin), [ggml-small.bin](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin) and [ggml-silero-v6.2.0.bin](https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v6.2.0.bin) → save them into `tools\whisper\`.

### 3. Bypassing Windows SmartScreen and Defender Warnings
Because TRWhisper uses a low-level keyboard hook (`WH_KEYBOARD_LL`) and the `SendInput` API, Windows Defender or SmartScreen may display a warning on first launch:
1. **If the SmartScreen prompt appears:** Click **"More info"** and select **"Run anyway"**.
2. **Adding a Windows Defender Exclusion (Optional):**
   ```powershell
   # Open PowerShell as Administrator in the project directory and run:
   Add-MpPreference -ExclusionPath (Get-Location).Path
   # Or specify the full path: Add-MpPreference -ExclusionPath "C:\YourFolderPath\TRWHISPER"
   ```

---

## ⚙️ Configuration (`config.json`)

You can edit `config.json` in the application directory with Notepad, or right-click the tray icon and select **"⚙️ Ayarları Aç"** (Open Settings):

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
    "VadModelPath": "tools\\whisper\\ggml-silero-v6.2.0.bin",
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
  }
}
```

- **`ModelPath`**: the active model. Selecting a model from the tray menu updates this field.
- **`VadModelPath`**: the silence detection model. If the file is missing, TRWhisper keeps working without VAD and writes a note to the log.
- **`TimeoutSeconds`**: maximum time for a single transcription; `whisper-cli` is stopped if it takes longer.
- **`Provider`**: `Gemini` or `OpenAI`.

> **Tip:** You can obtain a free Gemini API key from Google AI Studio and paste it into the `ApiKey` field. If left blank, LLM cleanup is skipped and the raw local transcript is pasted.

---

## ⚡ Performance

Measured on an Intel Core i5-12450H + NVIDIA GeForce RTX 3050 Laptop GPU (4 GB) with Turkish speech samples. Times include model loading, which happens on every dictation.

| Engine | Model | 3.2 s speech | 10.7 s speech |
|---|---|---|---|
| CPU | Small | 6.5 s | 9.3 s |
| CPU | Large-v3 Turbo | 23.1 s | 24.4 s |
| CUDA (RTX 3050) | Small | 2.2 s | 2.8 s |
| CUDA (RTX 3050) | Large-v3 Turbo | 2.5 s | 3.1 s |

- On the GPU, Turbo is both faster and more accurate than Small on the CPU; it uses ~1.2 GB of VRAM.
- The first dictation after the GPU has been idle can take a few seconds longer.
- Without speech, VAD lets the engine skip the heavy work, so an empty recording finishes in ~1–2 seconds.

---

## 🩺 Troubleshooting

- **Something doesn't work as expected:** check `%USERPROFILE%\Dictation\trwhisper.log`.
- **Text is not pasted into an app running as Administrator:** Windows blocks simulated keystrokes from a normal app into elevated windows. The text is still on the clipboard — paste it with `Ctrl + V`.
- **Transcription takes 20+ seconds:** you are probably running Large-v3 Turbo on the CPU. Install the CUDA build with `setup.ps1 -Cuda` (NVIDIA GPU) or select **Small** from the tray menu.

---

## 🚀 Building and Running the Project

Using PowerShell:
```powershell
# Run in development mode
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Run

# Create a standalone single-file Release package (in publish\)
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Publish
```

> **Note:** Always create the package with `build.ps1 -Publish`. It embeds the native WPF libraries into the single-file executable (`IncludeNativeLibrariesForSelfExtract`); a plain `dotnet publish` without that option produces an executable that crashes on startup. The application finds the `tools\whisper\` folder next to the executable or in a parent folder.
