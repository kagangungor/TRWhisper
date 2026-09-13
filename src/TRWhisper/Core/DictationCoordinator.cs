using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TRWhisper.Core.Audio;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;
using TRWhisper.Core.History;
using TRWhisper.Core.Llm;
using TRWhisper.Core.Native;
using TRWhisper.Core.Speech;
using TRWhisper.Core.Tray;

namespace TRWhisper.Core
{
    public class DictationCoordinator : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly IKeyboardHook _keyboardHook;
        private readonly IAudioRecorder _audioRecorder;
        private readonly ITranscriptionEngine _transcriptionEngine;
        private readonly ILlmCleaner _llmCleaner;
        private readonly IClipboardPaster _clipboardPaster;
        private readonly MarkdownLogger _logger;
        private readonly TranscriptHistory _history;
        private readonly TrayIconController _trayController;
        private readonly UI.PillOverlayWindow? _overlayWindow;

        private bool _isProcessing;
        private DateTime _recordingStartTime;
        private readonly object _stateLock = new();

        public DictationCoordinator(
            ConfigManager configManager,
            IKeyboardHook keyboardHook,
            IAudioRecorder audioRecorder,
            ITranscriptionEngine transcriptionEngine,
            ILlmCleaner llmCleaner,
            IClipboardPaster clipboardPaster,
            MarkdownLogger logger,
            TranscriptHistory history,
            TrayIconController trayController,
            UI.PillOverlayWindow? overlayWindow = null)
        {
            _configManager = configManager;
            _keyboardHook = keyboardHook;
            _audioRecorder = audioRecorder;
            _transcriptionEngine = transcriptionEngine;
            _llmCleaner = llmCleaner;
            _clipboardPaster = clipboardPaster;
            _logger = logger;
            _history = history;
            _trayController = trayController;
            _overlayWindow = overlayWindow;

            if (_overlayWindow != null)
            {
                _overlayWindow.RequestCancel += () =>
                {
                    Task.Run(async () =>
                    {
                        lock (_stateLock)
                        {
                            if (!_audioRecorder.IsRecording) return;
                        }
                        var path = await _audioRecorder.StopRecordingAsync();
                        if (File.Exists(path)) { try { File.Delete(path); } catch { } }
                        _trayController.SetState(AppState.Idle);
                    });
                };

                _overlayWindow.RequestFinish += () =>
                {
                    OnHotkeyUp(this, new HotkeyEventArgs(false));
                };
            }

            _keyboardHook.HotkeyDown += OnHotkeyDown;
            _keyboardHook.HotkeyUp += OnHotkeyUp;
        }

        private void OnHotkeyDown(object? sender, HotkeyEventArgs e)
        {
            lock (_stateLock)
            {
                if (_isProcessing || _audioRecorder.IsRecording) return;

                try
                {
                    var tempWav = _configManager.Current.General.ResolvedTempAudioPath;
                    _recordingStartTime = DateTime.Now;
                    _audioRecorder.StartRecording(tempWav);
                    _trayController.SetState(AppState.Recording);
                    _overlayWindow?.ShowListening();
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[DictationCoordinator] Kayıt başlatma hatası: {ex.Message}");
                    _trayController.SetState(AppState.Idle);
                    _overlayWindow?.HideWithFade();
                }
            }
        }

        private void OnHotkeyUp(object? sender, HotkeyEventArgs e)
        {
            // Kayıt durdurma ve transkripsiyon arka plan task'ında çalışmalı.
            Task.Run(async () =>
            {
                string? wavPath = null;
                bool shouldCleanWithLlm = e.IsLlmModifierActive || _configManager.Current.LlmCleaning.EnabledByDefault;

                lock (_stateLock)
                {
                    if (!_audioRecorder.IsRecording) return;
                    _isProcessing = true;
                }

                var whisperTimeout = _configManager.Current.Whisper.TimeoutSeconds;
                if (whisperTimeout <= 0) whisperTimeout = 120;
                // Watchdog: WhisperCliRunner kendi sıkı zaman aşımını uygular; bu yalnızca
                // LLM temizleme + yapıştırma payı bırakan bir backstop.
                var overallTimeout = TimeSpan.FromSeconds(whisperTimeout + 30);

                using var cts = new CancellationTokenSource();

                // Hattı yerel fonksiyon olarak çalıştır; wavPath'i kapanış üzerinden
                // yazar, böylece zaman aşımında bile finally onu silebilir.
                async Task RunPipelineAsync(CancellationToken ct)
                {
                    // İlk satırda yield: çağrı (var pipeline = RunPipelineAsync(...)) anında
                    // tamamlanmamış bir Task döndürsün ki alttaki Task.WhenAny watchdog'u,
                    // StopRecordingAsync ilerideki bir noktada senkron bloklasa bile başlasın.
                    await Task.Yield();

                    _trayController.SetState(AppState.Transcribing);
                    _overlayWindow?.ShowProcessing();
                    FileLog.Write($"[DictationCoordinator] kısayol bırakıldı (LLM={shouldCleanWithLlm}), çözümleme başlıyor.");

                    // 1. Kaydı durdur (en fazla 2000 ms bekle)
                    var stopTask = _audioRecorder.StopRecordingAsync();
                    var stopFinished = await Task.WhenAny(stopTask, Task.Delay(2000, ct));
                    wavPath = stopFinished == stopTask ? await stopTask : null;

                    // Çok kısa basma kontrolü (< 350ms ise muhtemelen yanlışlıkla basılmıştır)
                    var duration = DateTime.Now - _recordingStartTime;
                    if (duration.TotalMilliseconds < 350 || string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
                    {
                        FileLog.Write($"[DictationCoordinator] Kayıt süresi çok kısa ({duration.TotalMilliseconds:0}ms) veya dosya geçersiz, iptal edildi.");
                        _overlayWindow?.HideWithFade();
                        _trayController.SetState(AppState.Idle);
                        if (duration.TotalMilliseconds < 350)
                        {
                            _trayController.ShowNotification(
                                "Kayıt Çok Kısa",
                                "Konuşurken Sağ Ctrl tuşunu basılı tutun, konuşmanız bitince tuşu bırakın.",
                                System.Windows.Forms.ToolTipIcon.Warning);
                        }
                        return;
                    }

                    // 2. Yerel Whisper ile transkribe et
                    FileLog.Write($"[DictationCoordinator] Whisper transkripsiyonu başlatılıyor ({wavPath})...");
                    var language = _configManager.Current.General.Language;
                    var rawTranscript = await _transcriptionEngine.TranscribeAsync(wavPath, language, ct);
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(rawTranscript))
                    {
                        FileLog.Write("[DictationCoordinator] Boş transkript üretildi.");
                        _overlayWindow?.HideWithFade();
                        _trayController.ShowNotification(
                            "Ses Algılanamadı",
                            "Mikrofondan ses alınamadı veya ses çok kısık. Lütfen mikrofonunuzu kontrol edin.",
                            System.Windows.Forms.ToolTipIcon.Info);
                        return;
                    }

                    rawTranscript = rawTranscript.Trim();
                    FileLog.Write($"[DictationCoordinator] Whisper tamamlandı: '{rawTranscript}'");
                    string finalTranscript = rawTranscript;

                    // 3. LLM Temizleme Modu (aktifse)
                    if (shouldCleanWithLlm)
                    {
                        try
                        {
                            finalTranscript = await _llmCleaner.CleanTranscriptAsync(rawTranscript, ct);
                        }
                        catch (Exception ex)
                        {
                            FileLog.Write($"[DictationCoordinator] LLM temizleme hatası, ham metin kullanılacak: {ex.Message}");
                            finalTranscript = rawTranscript;
                        }
                    }

                    ct.ThrowIfCancellationRequested();

                    // 4. Pop-up penceresinde sonucu göster (Kopyalama butonuyla birlikte)
                    _overlayWindow?.ShowResult(finalTranscript, autoCopiedToClipboard: true);

                    // 5. Panoyu yedekle, metni yapıştır, panoyu geri yükle
                    FileLog.Write($"[DictationCoordinator] Pano yapıştırma başlatılıyor...");
                    var delay = _configManager.Current.PasteSettings.RestoreClipboardDelayMs;
                    await _clipboardPaster.PasteTextAsync(finalTranscript, delay);
                    FileLog.Write($"[DictationCoordinator] Pano yapıştırma tamamlandı.");

                    // 6. Yerel Günlüğe (%USERPROFILE%\Dictation\YYYY-MM.md) yaz
                    await _logger.LogTranscriptAsync(finalTranscript, shouldCleanWithLlm, rawTranscript);

                    // 7. Tepsi menüsündeki son 10 transkript listesine ekle
                    _history.Add(finalTranscript, shouldCleanWithLlm, rawTranscript);

                    FileLog.Write("[DictationCoordinator] Tüm çözümleme akışı başarıyla tamamlandı.");
                }

                try
                {
                    var pipeline = RunPipelineAsync(cts.Token);
                    var finished = await Task.WhenAny(pipeline, Task.Delay(overallTimeout));

                    if (finished != pipeline)
                    {
                        // Watchdog devreye girdi: hat zaman aşımına uğradı.
                        cts.Cancel();
                        FileLog.Write($"[DictationCoordinator] çözümleme zaman aşımı ({overallTimeout.TotalSeconds:0}s), akış iptal edildi.");
                        _overlayWindow?.HideWithFade();
                        _trayController.ShowNotification(
                            "Çözümleme Zaman Aşımı",
                            "Ses yazıya dönüştürülemedi. Lütfen tekrar deneyin.",
                            System.Windows.Forms.ToolTipIcon.Warning);

                        // Kayıt hâlâ açıksa en iyi çabayla kapat ki sonraki kayıt mikrofonu açabilsin.
                        if (_audioRecorder.IsRecording)
                        {
                            _ = Task.Run(async () =>
                            {
                                try { await _audioRecorder.StopRecordingAsync(); } catch { }
                            });
                        }
                        return;
                    }

                    await pipeline; // hattaki istisnaları yeniden fırlat
                }
                catch (OperationCanceledException)
                {
                    // Watchdog iptali; zaten yukarıda bildirildi.
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[DictationCoordinator] Transkripsiyon akışı hatası: {ex.Message}");
                    _overlayWindow?.HideWithFade();
                }
                finally
                {
                    // Geçici ses dosyasını diskten derhal sil
                    if (!string.IsNullOrEmpty(wavPath) && File.Exists(wavPath))
                    {
                        try
                        {
                            File.Delete(wavPath);
                        }
                        catch (Exception ex)
                        {
                            FileLog.Write($"[DictationCoordinator] Geçici dosya silinemedi: {ex.Message}");
                        }
                    }

                    lock (_stateLock)
                    {
                        _isProcessing = false;
                    }

                    _trayController.SetState(AppState.Idle);
                }
            });
        }

        public void Start()
        {
            _keyboardHook.Start();
            _trayController.SetState(AppState.Idle);
        }

        public void Stop()
        {
            _keyboardHook.Stop();
        }

        public void Dispose()
        {
            _keyboardHook.HotkeyDown -= OnHotkeyDown;
            _keyboardHook.HotkeyUp -= OnHotkeyUp;
            _keyboardHook.Dispose();
            _audioRecorder.Dispose();
            _trayController.Dispose();
        }
    }
}
