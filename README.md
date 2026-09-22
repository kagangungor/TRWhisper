# TRWhisper: Local Push-to-Talk Dictation App for Windows 11 🎙️

[🇬🇧 English](README.md) | [🇹🇷 Türkçe](READMETR.md)

[![Release](https://img.shields.io/github/v/release/kagangungor/TRWhisper?color=blue&logo=github)](https://github.com/kagangungor/TRWhisper/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/kagangungor/TRWhisper/total?color=green&logo=github)](https://github.com/kagangungor/TRWhisper/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20x64-0078D6?logo=windows11&logoColor=white)](https://github.com/kagangungor/TRWhisper)
[![License](https://img.shields.io/github/license/kagangungor/TRWhisper?color=orange)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/9.0)

**TRWhisper** is a completely local, zero-telemetry "push-to-talk" dictation application built for Windows 11 as an alternative to Superwhisper. It is tuned for Turkish dictation.

<p align="center">
  <img src="assets/Demo2.gif" alt="TRWhisper Demo" width="750">
</p>

---

## 🚀 Key Features

- **Modern Graphical Settings Window (WPF)**: A clean and accessible multi-tab settings panel (**General**, **Audio**, **Model**, **Hotkey**, **Dictionary**, **AI / LLM**, and **Overlay Pill**) to configure and test everything in real time.
- **Custom Dictionary & Jargon Support**: User-defined phonetic/jargon replacements (`dictionary.json`). Automatically corrects technical terms, acronyms, brand names, or words frequently misheard by Whisper with word-boundary awareness.
- **Advanced Turkish Text Normalization**:
  - Automatic sentence-start capitalization and punctuation validation.
  - Turkish suffix and conjunction rules (de/da, ki).
  - Speech stutter and repetitive word filter (e.g. `ve ve ve` -> `ve`).
- **Context-Aware LLM Modes & Active Window Detection**:
  - Multiple transcription modes: **Raw Text**, **Clean** (removes fillers like `ııı`, `eee`, `şey`), **Summarize**, **Formal Tone**, and **Bullet Points**.
  - Active foreground window detection (`ForegroundAppDetector`): Intelligently adapts LLM prompt persona based on whether you are working in code editors (VS Code), email clients (Outlook), messaging (Discord, Slack), or document editors.
  - **Hardware-Backed Secure API Key Storage**: Encrypted with Windows DPAPI (`Data Protection API`); keys are never stored in plain text.
- **Flexible Hotkeys & Hands-Free Dictation**:
  - Classic **Push-to-Talk** mode (default: Right Ctrl).
  - **Hands-Free Toggle** mode with intelligent silence detection to automatically finish dictation when you stop speaking.
  - Fully customizable hotkeys and modifier keys (Right/Left Ctrl, Shift, Alt, Function keys).
- **In-Process Whisper.net Engine & Model Management**:
  - High-performance in-process native Whisper.net C# engine alongside Whisper CLI.
  - Download models directly inside the Settings UI with progress tracking.
  - Automatic idle memory unloading (`IdleTimeoutMinutes`) to free VRAM/RAM when not dictating.
- **Auto-Paste & Smart Clipboard**:
  - Copies transcript to clipboard and simulates `Ctrl + V` via Win32 `SendInput`.
  - Optional clipboard restoration (`RestoreClipboard`) to preserve your previous clipboard contents after pasting.
- **100% Offline & Private (STT)**: Powered by local Whisper. Your voice data never leaves your computer.
- **NVIDIA GPU Acceleration**: CUDA build processes dictations in ~2–3 seconds with **Large-v3 Turbo** instead of ~23 seconds on CPU. Automatically falls back to CPU if no compatible GPU is detected.
- **Silence Detection & Hallucination Filter**:
  - Built-in **Silero VAD** skips non-speech segments to avoid phantom text on silent triggers.
  - Filters out known Whisper phantom subtitles (`Altyazı M.K.`, `İzlediğiniz için teşekkür ederim.`) and audio bracket tags (`[MÜZİK ÇALIYOR]`).
- **Floating Pill Overlay**: Real-time status display indicating listening volume, processing status, with cancel (✕) and complete (✓) buttons.
- **Windows Startup Integration**: Enable or disable autostart on Windows boot with a single switch in the Settings UI.
- **Automated Test Suite**: 87 automated unit tests and a UI smoke testing utility (`uismoke`) guaranteeing zero XAML template breakage.
- **Local Markdown Logging**: Transcripts are automatically logged with timestamps to `%USERPROFILE%\Dictation\YYYY-MM.md`. Logs are written to `%USERPROFILE%\Dictation\trwhisper.log`.

---

## 🛠️ Installation & Requirements

**Requirements**
- Windows 11 x64
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (to build from source)
- Optional: an NVIDIA GPU with an up-to-date driver (CUDA 12.4 support) for GPU acceleration

### ⚡ Easy Installation (Recommended): Single-File Setup Wizard

Download the latest **[release](https://github.com/kagangungor/TRWhisper/releases/latest)**
(`TRWhisper-Setup-x.y.z.exe`, ~51 MB) and run it. No administrator password is required;
the app is installed into `%LOCALAPPDATA%\Programs\TRWhisper`.

The wizard is available in Turkish and English and states exactly what will be installed
before it starts:

| Component | Status |
|---|---|
| TRWhisper application (.NET 9 embedded, no separate .NET needed) | Always (inside the setup file) |
| Silero VAD silence model | Always (inside the setup file) |
| Whisper **CPU** engine | Inside the setup file — works on every PC |
| Whisper **NVIDIA GPU (CUDA 12.4)** engine | Optional, ~640 MB download (auto-recommended if an NVIDIA GPU is found) |
| **Large-v3 Turbo** model (~547 MB) | Optional |
| **Small** model (~465 MB) | Optional |
| Microsoft Visual C++ 2015-2022 Runtime | Only downloaded and installed if missing (one UAC prompt) |
| Desktop / Start menu shortcut, start with Windows | Optional |

- At least one model must be selected; you can install both and switch between them any time
  from the system tray menu.
- Every download is verified with **SHA-256**, and files that are already installed are not
  downloaded again (re-run the setup to change your engine/model choice).
- Uninstall: **Settings > Apps > TRWhisper**. You are asked whether your dictation records
  (`%USERPROFILE%\Dictation`) should be deleted as well.
- Silent installation (for deployment):
  ```powershell
  TRWhisper-Setup-1.0.0.exe /SILENT /ENGINE=cuda /MODELS=turbo,small /TASKS=desktopicon
  ```

> The setup file is not code-signed, so Windows SmartScreen may warn about an "unknown
> publisher": **More info > Run anyway**. You can inspect the [VirusTotal Report](https://www.virustotal.com/gui/file/c97e23fe0cd720c570e44370f74d49fc74c961f106eecf11e48080d217d979c2/detection) (100% clean on Microsoft Defender, Kaspersky, Bitdefender, etc.).

---

### 🔧 Installing from Source (for developers)

The steps below are only needed if you want to build the project from source.

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

You can configure every setting easily through the graphical interface by right-clicking the tray icon and selecting **"⚙️ Ayarlar"** (Settings). Alternatively, you can edit `config.json` in the application directory:

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

- **`EnableCustomDictionary`**: Enables user-defined phonetic/jargon mappings in `dictionary.json`.
- **`EnableTextNormalization`**: Enables Turkish capitalization, punctuation validation, and speech stutter cleaning.
- **`IdleTimeoutMinutes`**: Automatically unloads the model from RAM/VRAM after inactivity (default: 10 minutes).
- **`RestoreClipboard`**: Automatically restores previous clipboard contents after dictation has been pasted.
- **`DictationMode`**: `PushToTalk` (press & hold) or `HandsFree` (voice toggle with silence cutoff).
- **`HandsFreeSilenceMs`**: Silence duration (ms) before hands-free dictation automatically finishes.
- **`Provider`**: `Gemini` or `OpenAI`. Keys are securely encrypted with Windows DPAPI.

> **Tip:** You can obtain a free Gemini API key from Google AI Studio and enter it via the Settings UI. If left empty, LLM cleanup is skipped and raw local transcripts are pasted directly.

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

## 🚀 Building & Running from Source

Using PowerShell:
```powershell
# Run in development mode
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Run

# Create a self-contained, single-file Release package (in publish\ folder)
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Publish
```

> **Note:** Always create release packages with `build.ps1 -Publish`. This bundles WPF native libraries inside the single-file executable (`IncludeNativeLibrariesForSelfExtract`). A plain `dotnet publish` without these flags produces an executable that crashes on launch. The application locates the `tools\whisper\` folder either beside the executable or in its parent directories.

---

## 🧪 Testing & Verification

TRWhisper maintains high reliability with automated test suites:

```powershell
# Run the 87 automated unit tests (AppMode, Hotkeys, LLM, ModelManager, DPAPI, etc.):
dotnet test tests\TRWhisper.Tests\TRWhisper.Tests.csproj

# Run the WPF Settings Window XAML template & UI smoke test suite:
dotnet run --project tools\uismoke
```
