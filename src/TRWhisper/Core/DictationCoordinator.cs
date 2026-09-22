using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TRWhisper.Core.Audio;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;
using TRWhisper.Core.Dictionary;
using TRWhisper.Core.Normalization;
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
        private readonly CustomDictionaryService _dictionaryService;
        private readonly TurkishTextNormalizer _normalizer;
        private readonly MarkdownLogger _logger;
        private readonly TranscriptHistory _history;
        private readonly TrayIconController _trayController;
        private readonly UI.PillOverlayWindow? _overlayWindow;
        private readonly IForegroundAppDetector _appDetector;

        private bool _isProcessing;
        private DateTime _recordingStartTime;
        private readonly object _stateLock = new();
        private LlmMode? _sessionLlmMode;
        private string? _sessionFriendlyAppName;

        // --- Eller serbest modu ---
        private System.Threading.Timer? _handsFreeTimer;
        private bool _handsFreeSpeechSeen;
        private DateTime _handsFreeSilenceSince;
        private bool _handsFreeLlmActive;

        // --- Canlı akış (real-time live preview) ---
        private CancellationTokenSource? _livePreviewCts;
        private Task? _livePreviewTask;

        /// <summary>Dikte akışının çalışma biçimi.</summary>
        private enum DictationMode
        {
            /// <summary>Tuşu basılı tut, bırakınca çözümle.</summary>
            PushToTalk,
            /// <summary>Bir bas başlat, tekrar bas bitir.</summary>
            Toggle,
            /// <summary>Bir bas başlat, sessizlik sürünce kendiliğinden bit.</summary>
            HandsFree
        }

        private DictationMode CurrentMode()
            => _configManager.Current.Hotkey.DictationMode?.Trim().ToLowerInvariant() switch
            {
                "toggle" => DictationMode.Toggle,
                "handsfree" => DictationMode.HandsFree,
                _ => DictationMode.PushToTalk,
            };

        public DictationCoordinator(
            ConfigManager configManager,
            IKeyboardHook keyboardHook,
            IAudioRecorder audioRecorder,
            ITranscriptionEngine transcriptionEngine,
            ILlmCleaner llmCleaner,
            IClipboardPaster clipboardPaster,
            CustomDictionaryService dictionaryService,
            TurkishTextNormalizer normalizer,
            MarkdownLogger logger,
            TranscriptHistory history,
            TrayIconController trayController,
            UI.PillOverlayWindow? overlayWindow = null,
            IForegroundAppDetector? appDetector = null)
        {
            _configManager = configManager;
            _keyboardHook = keyboardHook;
            _audioRecorder = audioRecorder;
            _transcriptionEngine = transcriptionEngine;
            _llmCleaner = llmCleaner;
            _clipboardPaster = clipboardPaster;
            _dictionaryService = dictionaryService;
            _normalizer = normalizer;
            _logger = logger;
            _history = history;
            _trayController = trayController;
            _overlayWindow = overlayWindow;
            _appDetector = appDetector ?? new ForegroundAppDetector();

            if (_overlayWindow != null)
            {
                _overlayWindow.RequestCancel += () =>
                {
                    _overlayWindow.StopAudioReactiveWaveform();
                    StopHandsFreeMonitor();
                    StopLivePreview();
                    Task.Run(async () =>
                    {
                        lock (_stateLock)
                        {
                            if (!_audioRecorder.IsRecording) return;
                        }
                        var path = await _audioRecorder.StopRecordingAsync().ConfigureAwait(false);
                        if (File.Exists(path)) { try { File.Delete(path); } catch { } }
                        _trayController.SetState(AppState.Idle);
                    });
                };

                // Kapsüldeki "bitir" düğmesi moddan bağımsız olarak dikteyi tamamlar.
                _overlayWindow.RequestFinish += () => FinishDictation(false);
            }

            _keyboardHook.HotkeyDown += OnHotkeyDown;
            _keyboardHook.HotkeyUp += OnHotkeyUp;
        }

        private void OnHotkeyDown(object? sender, HotkeyEventArgs e)
        {
            var mode = CurrentMode();

            // Toggle/HandsFree: kayıt sürerken ikinci basış BİTİRİR.
            if (mode != DictationMode.PushToTalk)
            {
                bool recording;
                lock (_stateLock) { recording = _audioRecorder.IsRecording; }
                if (recording)
                {
                    FileLog.Write($"[DictationCoordinator] {mode}: ikinci basış, kayıt bitiriliyor.");
                    FinishDictation(e.IsLlmModifierActive);
                    return;
                }
            }

            lock (_stateLock)
            {
                if (_isProcessing || _audioRecorder.IsRecording) return;

                try
                {
                    var tempWav = _configManager.Current.General.ResolvedTempAudioPath;
                    _recordingStartTime = DateTime.Now;
                    _audioRecorder.StartRecording(tempWav);
                    _trayController.SetState(AppState.Recording);

                    bool llmActive = e.IsLlmModifierActive || _configManager.Current.LlmCleaning.EnabledByDefault;
                    string? foregroundProc = _appDetector.GetForegroundProcessName();
                    FileLog.Write($"[DictationCoordinator] Dikte başlatıldı. Ön plan süreci: '{foregroundProc}' (LLM etkin={llmActive})");

                    if (llmActive)
                    {
                        var resolved = LlmModeRegistry.ResolveActiveMode(_configManager.Current.LlmCleaning, foregroundProc, _appDetector);
                        _sessionLlmMode = resolved.Mode;
                        _sessionFriendlyAppName = resolved.DetectedAppName;
                        FileLog.Write($"[DictationCoordinator] LLM modu: {_sessionLlmMode.Name} ({_sessionLlmMode.Id}), Uygulama: '{_sessionFriendlyAppName}'");
                    }
                    else
                    {
                        _sessionLlmMode = null;
                        _sessionFriendlyAppName = null;
                    }

                    // Toggle'da kullanıcı tuşu bıraktıktan sonra kaydın SÜRDÜĞÜNÜ bilmeli.
                    _overlayWindow?.ShowListening(mode == DictationMode.Toggle
                        ? "Kayıt açık (bitirmek için tekrar basın)"
                        : null, _sessionLlmMode, _sessionFriendlyAppName);
                    _overlayWindow?.StartAudioReactiveWaveform(_audioRecorder);
                    StartLivePreview();
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[DictationCoordinator] Kayıt başlatma hatası: {ex.Message}");
                    StopLivePreview();
                    _trayController.SetState(AppState.Idle);
                    _overlayWindow?.StopAudioReactiveWaveform();
                    _overlayWindow?.HideWithFade();
                    return;
                }
            }

            if (mode == DictationMode.HandsFree) StartHandsFreeMonitor(e.IsLlmModifierActive);
        }

        /// <summary>
        /// Kısayolun bırakılması yalnızca bas-konuş modunda dikteyi bitirir; diğer modlarda
        /// bitirme ikinci basışla (veya eller serbestte sessizlikle) olur.
        /// </summary>
        private void OnHotkeyUp(object? sender, HotkeyEventArgs e)
        {
            if (CurrentMode() != DictationMode.PushToTalk) return;
            FinishDictation(e.IsLlmModifierActive);
        }

        // ------------------------------------------------------------------ eller serbest

        private void StartHandsFreeMonitor(bool llmActive)
        {
            StopHandsFreeMonitor();

            _handsFreeSpeechSeen = false;
            _handsFreeSilenceSince = DateTime.MinValue;
            _handsFreeLlmActive = llmActive;

            // Yoklama aralığı 100 ms: sessizlik eşiği saniyeler mertebesinde olduğundan
            // daha sık bakmanın faydası yok, CPU'ya da dokunmuyor.
            _handsFreeTimer = new System.Threading.Timer(HandsFreeTick, null, 150, 100);
            FileLog.Write("[DictationCoordinator] Eller serbest: sessizlik izleme başladı.");
        }

        private void StopHandsFreeMonitor()
        {
            var timer = Interlocked.Exchange(ref _handsFreeTimer, null);
            timer?.Dispose();
        }

        private void HandsFreeTick(object? state)
        {
            try
            {
                if (!_audioRecorder.IsRecording) { StopHandsFreeMonitor(); return; }

                var cfg = _configManager.Current.Hotkey;
                var threshold = (float)cfg.HandsFreeSilenceThreshold;
                var silenceMs = cfg.HandsFreeSilenceMs > 0 ? cfg.HandsFreeSilenceMs : 1800;

                if (_audioRecorder.CurrentLevel > threshold)
                {
                    _handsFreeSpeechSeen = true;
                    _handsFreeSilenceSince = DateTime.MinValue;
                    return;
                }

                // Konuşma hiç başlamadıysa bekle: kullanıcı tuşa basıp düşünüyor olabilir,
                // baştaki sessizlik kaydı kapatmamalı.
                if (!_handsFreeSpeechSeen) return;

                if (_handsFreeSilenceSince == DateTime.MinValue)
                {
                    _handsFreeSilenceSince = DateTime.UtcNow;
                    return;
                }

                if ((DateTime.UtcNow - _handsFreeSilenceSince).TotalMilliseconds < silenceMs) return;

                StopHandsFreeMonitor();
                FileLog.Write($"[DictationCoordinator] Eller serbest: {silenceMs} ms sessizlik, kayıt bitiriliyor.");
                FinishDictation(_handsFreeLlmActive);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[DictationCoordinator] Eller serbest izleme hatası: {ex.Message}");
                StopHandsFreeMonitor();
            }
        }

        // ------------------------------------------------------------------ canlı önizleme

        private void StartLivePreview()
        {
            StopLivePreview();
            if (!_configManager.Current.General.EnableStreamingPreview) return;

            _livePreviewCts = new CancellationTokenSource();
            var ct = _livePreviewCts.Token;

            _livePreviewTask = Task.Run(async () =>
            {
                try
                {
                    // Konuşmanın ilk hecelerinin oluşması için başlangıçta kısa bir bekleme
                    await Task.Delay(900, ct).ConfigureAwait(false);

                    while (!ct.IsCancellationRequested)
                    {
                        bool recording;
                        lock (_stateLock)
                        {
                            recording = _audioRecorder.IsRecording && !_isProcessing;
                        }
                        if (!recording) break;

                        // Son 10 saniyeyi (160000 örnek) alarak işlem yükünü ve hafızayı sınırla
                        var samples = _audioRecorder.GetRecordedSamplesSnapshot(160000);
                        if (samples.Length >= 8000) // En az 0.5 saniyelik ses varsa transkribe etmeyi dene
                        {
                            var language = _configManager.Current.General.Language;
                            var preview = await _transcriptionEngine.TranscribeLivePreviewAsync(samples, language, ct).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(preview) && !ct.IsCancellationRequested)
                            {
                                _overlayWindow?.UpdateLivePreview(preview);
                            }
                        }

                        await Task.Delay(900, ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    FileLog.Write($"[DictationCoordinator] Canlı önizleme döngüsü hatası: {ex.Message}");
                }
            }, ct);
        }

        private void StopLivePreview()
        {
            try
            {
                var cts = Interlocked.Exchange(ref _livePreviewCts, null);
                if (cts != null)
                {
                    cts.Cancel();
                    cts.Dispose();
                }
            }
            catch { }
        }

        /// <summary>
        /// Kaydı durdurup çözümleme hattını çalıştırır. Kısayol bırakma, ikinci basış,
        /// sessizlik algılama ve kapsüldeki "bitir" düğmesi hepsi buraya gelir.
        /// </summary>
        private void FinishDictation(bool llmModifierActive)
        {
            StopHandsFreeMonitor();
            StopLivePreview();
            _overlayWindow?.StopAudioReactiveWaveform();
            var e = new HotkeyEventArgs(llmModifierActive);
            var mode = CurrentMode();
            // Kayıt durdurma ve transkripsiyon arka plan task'ında çalışmalı.
            Task.Run(async () =>
            {
                string? wavPath = null;
                bool shouldCleanWithLlm = e.IsLlmModifierActive || _configManager.Current.LlmCleaning.EnabledByDefault;
                LlmMode? sessionMode;
                string? sessionAppName;

                lock (_stateLock)
                {
                    if (!_audioRecorder.IsRecording) return;
                    _isProcessing = true;

                    if (shouldCleanWithLlm && _sessionLlmMode == null)
                    {
                        var resolved = LlmModeRegistry.ResolveActiveMode(_configManager.Current.LlmCleaning, null, _appDetector);
                        _sessionLlmMode = resolved.Mode;
                        _sessionFriendlyAppName = resolved.DetectedAppName;
                    }

                    sessionMode = shouldCleanWithLlm ? _sessionLlmMode : null;
                    sessionAppName = shouldCleanWithLlm ? _sessionFriendlyAppName : null;
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
                    _overlayWindow?.ShowProcessing(overallTimeout + TimeSpan.FromSeconds(5), sessionMode, sessionAppName);
                    FileLog.Write($"[DictationCoordinator] kısayol bırakıldı (LLM={shouldCleanWithLlm}, Mod={sessionMode?.Name}, App={sessionAppName}), çözümleme başlıyor.");

                    // 1. Kaydı durdur (en fazla 2000 ms bekle)
                    var stopTask = _audioRecorder.StopRecordingAsync();
                    var stopFinished = await Task.WhenAny(stopTask, Task.Delay(2000, ct)).ConfigureAwait(false);
                    wavPath = stopFinished == stopTask ? await stopTask.ConfigureAwait(false) : null;

                    // Çok kısa basma kontrolü (< 350ms ise muhtemelen yanlışlıkla basılmıştır).
                    // Yalnızca bas-konuşta geçerli: Toggle/HandsFree'de kısa bir dokunuş
                    // kaydı bilerek başlatır, "yanlışlıkla" sayılamaz.
                    var duration = DateTime.Now - _recordingStartTime;
                    bool tooShort = mode == DictationMode.PushToTalk && duration.TotalMilliseconds < 350;

                    if (tooShort || string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
                    {
                        FileLog.Write($"[DictationCoordinator] Kayıt süresi çok kısa ({duration.TotalMilliseconds:0}ms) veya dosya geçersiz, iptal edildi.");
                        _overlayWindow?.HideWithFade();
                        _trayController.SetState(AppState.Idle);
                        if (tooShort)
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
                    var rawTranscript = await _transcriptionEngine.TranscribeAsync(wavPath, language, ct).ConfigureAwait(false);
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

                    // 2b. Özel sözlük: fonetik yazımları düzelt ("pitonda" -> "Python'da").
                    // rawTranscript bilerek DOKUNULMADAN bırakılır; günlük ve geçmiş ham metni saklar.
                    string finalTranscript = _dictionaryService.Apply(rawTranscript);
                    if (finalTranscript != rawTranscript)
                    {
                        FileLog.Write($"[DictationCoordinator] Sözlük uygulandı: '{finalTranscript}'");
                    }

                    // 2c. Sayı/tarih/saat/yüzde/birim normalizasyonu ("yüzde yirmi" -> "%20").
                    var normalized = _normalizer.Normalize(finalTranscript);
                    if (normalized != finalTranscript)
                    {
                        FileLog.Write($"[DictationCoordinator] Normalizasyon uygulandı: '{normalized}'");
                    }
                    finalTranscript = normalized;

                    // 3. LLM Temizleme Modu (aktifse). LLM'e sözlükten geçmiş metin verilir ki
                    // doğru terimleri görsün; dönen metne sözlük yeniden uygulanır (idempotent)
                    // çünkü LLM terimleri yeniden bozabilir.
                    bool llmSucceeded = false;
                    if (shouldCleanWithLlm)
                    {
                        try
                        {
                            var cleaned = await _llmCleaner.CleanTranscriptAsync(finalTranscript, sessionMode, ct).ConfigureAwait(false);
                            if (string.IsNullOrEmpty(_llmCleaner.LastError))
                            {
                                llmSucceeded = true;
                                finalTranscript = _normalizer.Normalize(_dictionaryService.Apply(cleaned));
                            }
                            else
                            {
                                var provider = _configManager.Current.LlmCleaning.Provider ?? "Ollama";
                                _trayController.ShowNotification(
                                    "LLM Bağlantı Uyarısı",
                                    $"{provider} servisine bağlanılamadı ({_llmCleaner.LastError}). Orijinal transkript yazıldı.",
                                    System.Windows.Forms.ToolTipIcon.Warning);
                            }
                        }
                        catch (Exception ex)
                        {
                            FileLog.Write($"[DictationCoordinator] LLM temizleme hatası, sözlükten geçmiş metin kullanılacak: {ex.Message}");
                        }
                    }

                    ct.ThrowIfCancellationRequested();

                    // 4. Metni hedef pencereye ulaştır (pano + Ctrl+V ya da doğrudan yazma)
                    FileLog.Write($"[DictationCoordinator] Yapıştırma başlatılıyor...");
                    var pasteResult = await _clipboardPaster.PasteTextAsync(finalTranscript).ConfigureAwait(false);
                    FileLog.Write($"[DictationCoordinator] Yapıştırma tamamlandı (sonuç={pasteResult}).");

                    // 5. Pop-up penceresinde sonucu göster (Kopyalama butonuyla birlikte).
                    // Yükseltilmiş hedefte kullanıcıya elle Ctrl+V yapması gerektiği bildirilir.
                    _overlayWindow?.ShowResult(
                        finalTranscript,
                        pasteResult,
                        (shouldCleanWithLlm && llmSucceeded) ? rawTranscript : null,
                        (shouldCleanWithLlm && llmSucceeded) ? sessionMode : null,
                        (shouldCleanWithLlm && llmSucceeded) ? sessionAppName : null);

                    if (pasteResult == PasteResult.ElevatedTargetCopiedOnly)
                    {
                        _trayController.ShowNotification(
                            "Yönetici Penceresi",
                            "Hedef uygulama yönetici olarak çalışıyor; Windows otomatik yapıştırmayı engelliyor. Metin panoya kopyalandı, Ctrl+V ile yapıştırın.",
                            System.Windows.Forms.ToolTipIcon.Warning);
                    }

                    // 6. Yerel Günlüğe (%USERPROFILE%\Dictation\YYYY-MM.md) yaz
                    await _logger.LogTranscriptAsync(finalTranscript, shouldCleanWithLlm, rawTranscript).ConfigureAwait(false);

                    // 7. Tepsi menüsündeki son 10 transkript listesine ekle
                    _history.Add(finalTranscript, shouldCleanWithLlm, rawTranscript);

                    FileLog.Write("[DictationCoordinator] Tüm çözümleme akışı başarıyla tamamlandı.");
                }

                try
                {
                    var pipeline = RunPipelineAsync(cts.Token);
                    var finished = await Task.WhenAny(pipeline, Task.Delay(overallTimeout)).ConfigureAwait(false);

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
                                try { await _audioRecorder.StopRecordingAsync().ConfigureAwait(false); } catch { }
                            });
                        }
                        return;
                    }

                    await pipeline.ConfigureAwait(false); // hattaki istisnaları yeniden fırlat
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
                        _sessionLlmMode = null;
                        _sessionFriendlyAppName = null;
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
            StopHandsFreeMonitor();
            StopLivePreview();
            _keyboardHook.Stop();
        }

        public void Dispose()
        {
            StopHandsFreeMonitor();
            StopLivePreview();
            _keyboardHook.HotkeyDown -= OnHotkeyDown;
            _keyboardHook.HotkeyUp -= OnHotkeyUp;
            _keyboardHook.Dispose();
            _audioRecorder.Dispose();
            _trayController.Dispose();
        }
    }
}
