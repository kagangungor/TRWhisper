using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TRWhisper.Core.Audio;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;
using TRWhisper.Core.Dictionary;
using Whisper.net;

namespace TRWhisper.Core.Speech
{
    /// <summary>
    /// Modeli süreç içinde sıcak tutan transkripsiyon motoru. whisper-cli'nin aksine her
    /// diktede süreç başlatıp modeli diskten okumaz; ölçümde dikte başına ~940 ms'lik
    /// sabit maliyet (model yükleme + süreç başlatma) bu sayede ortadan kalkıyor.
    ///
    /// Model 10 dakika kullanılmazsa bellekten (ve CUDA kullanılıyorsa VRAM'den) atılır;
    /// sonraki dikte onu otomatik geri yükler.
    /// </summary>
    public class WhisperNetEngine : ITranscriptionEngine, IDisposable
    {
        /// <summary>whisper modelleri yalnızca 16 kHz mono ile çalışır.</summary>
        private const int WhisperSampleRate = 16000;

        /// <summary>
        /// Ayarlardan okunan boşta kalma süresi. 0/negatif = modeli hiç boşaltma
        /// (VRAM dolu kalır ama her dikte sıcak başlar).
        /// </summary>
        private TimeSpan IdleTimeout
        {
            get
            {
                var minutes = _configManager.Current.Whisper.IdleTimeoutMinutes;
                if (minutes <= 0) return Timeout.InfiniteTimeSpan;
                return TimeSpan.FromMinutes(minutes);
            }
        }

        private readonly ConfigManager _configManager;
        private readonly CustomDictionaryService? _dictionaryService;

        // Model yükleme/boşaltma ve transkripsiyonu tek sıraya alır. whisper bağlamı
        // eşzamanlı çağrılara güvenli değildir; ayrıca boşta-kalma zamanlayıcısının
        // çalışan bir dikte sırasında modeli boşaltmasını engeller.
        private readonly SemaphoreSlim _gate = new(1, 1);

        private readonly System.Threading.Timer _idleTimer;

        private WhisperFactory? _factory;
        private WhisperProcessor? _processor;
        private WhisperVadFactory? _vadFactory;

        private string? _loadedModelPath;
        private string? _loadedLanguage;
        private int _loadedDictionaryRevision = -1;
        private bool _disposed;

        public WhisperNetEngine(ConfigManager configManager, CustomDictionaryService? dictionaryService = null)
        {
            _configManager = configManager;
            _dictionaryService = dictionaryService;
            _idleTimer = new System.Threading.Timer(OnIdleTimeout, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        // Ayarlar penceresinin canlı durum kartı için salt okunur anlık görüntü. Kilit
        // alınmaz: değerler yalnızca gösterim içindir, bir sonraki yoklamada tazelenir.
        public bool IsModelLoaded => _processor != null || _factory != null;
        public string? LoadedModelPath => _loadedModelPath;
        public string? LoadedLanguage => _loadedLanguage;

        // Yalnızca TranscribeAsync yazar (kilit altında); canlı önizleme dokunmaz.
        private volatile string? _lastDetectedLanguage;
        public string? LastDetectedLanguage => _lastDetectedLanguage;

        /// <summary>
        /// Modeli kullanıcı isteğiyle bellekten (ve VRAM'den) atar. Dikte sürüyorsa native
        /// bağlamı altından çekmemek için hiçbir şey yapmaz ve false döner.
        /// </summary>
        public bool UnloadModel(string reason = "Kullanıcı isteği")
        {
            if (_disposed || !_gate.Wait(0)) return false;
            try
            {
                Unload(reason);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Modeli önceden belleğe alır. Uygulama açılışında arka planda çağrılır ki ilk
        /// dikte de model yükleme bedelini ödemesin.
        /// </summary>
        public async Task WarmUpAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var language = _configManager.Current.General.Language;
                await EnsureLoadedAsync(_configManager.Current.Whisper.ResolvedModelPath, language).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[WhisperNetEngine] Isıtma başarısız: {ex.Message}");
            }
            finally
            {
                _gate.Release();
                RestartIdleTimer();
            }
        }

        /// <summary>
        /// Kullanılan modeli değiştirir: mevcut modeli bellekten atar ve yenisini hemen
        /// yükler; böylece değişimden sonraki ilk dikte de hızlı olur.
        /// </summary>
        public async Task SwitchModelAsync(string modelPath, CancellationToken cancellationToken = default)
        {
            if (_disposed) return;

            var resolved = WhisperConfig.ResolvePath(modelPath);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Unload($"model değişimi -> {Path.GetFileName(resolved)}");
                await EnsureLoadedAsync(resolved, _configManager.Current.General.Language).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[WhisperNetEngine] Model değiştirme hatası: {ex.Message}");
            }
            finally
            {
                _gate.Release();
                RestartIdleTimer();
            }
        }

        public async Task<string> TranscribeAsync(string wavFilePath, string language = "tr", CancellationToken cancellationToken = default)
        {
            if (_disposed) return string.Empty;

            if (!File.Exists(wavFilePath))
            {
                FileLog.Write($"[WhisperNetEngine] Ses dosyası bulunamadı: {wavFilePath}");
                return string.Empty;
            }

            var modelPath = _configManager.Current.Whisper.ResolvedModelPath;
            if (!File.Exists(modelPath))
            {
                var msg = $"Whisper model dosyası bulunamadı: {Path.GetFileName(modelPath)}";
                FileLog.Write($"[WhisperNetEngine] HATA: {msg} (Tam yol: {modelPath})");
                throw new FileNotFoundException(msg, modelPath);
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _lastDetectedLanguage = null;
                var sw = Stopwatch.StartNew();

                var processor = await EnsureLoadedAsync(modelPath, language).ConfigureAwait(false);
                var loadMs = sw.ElapsedMilliseconds;

                var samples = ReadSamples16kMono(wavFilePath);
                if (samples.Length == 0)
                {
                    FileLog.Write("[WhisperNetEngine] Ses dosyasından örnek okunamadı.");
                    return string.Empty;
                }

                var speech = ApplyVad(samples);
                if (speech.Length == 0)
                {
                    FileLog.Write($"[WhisperNetEngine] VAD konuşma bulamadı ({samples.Length / (double)WhisperSampleRate:0.0} sn), boş sonuç.");
                    return string.Empty;
                }

                cancellationToken.ThrowIfCancellationRequested();

                var builder = new StringBuilder();
                string? detectedLanguage = null;
                await foreach (var segment in processor.ProcessAsync(speech, cancellationToken).ConfigureAwait(false))
                {
                    builder.Append(segment.Text);
                    if (detectedLanguage == null && !string.IsNullOrWhiteSpace(segment.Language))
                        detectedLanguage = segment.Language;
                }
                _lastDetectedLanguage = detectedLanguage;

                sw.Stop();
                var cleaned = TranscriptCleaner.Clean(builder.ToString());
                FileLog.Write(
                    $"[WhisperNetEngine] bitti: {sw.ElapsedMilliseconds}ms (yükleme {loadMs}ms), dil={language}->{detectedLanguage ?? "?"}, " +
                    $"ses={samples.Length / (double)WhisperSampleRate:0.0}sn -> konuşma={speech.Length / (double)WhisperSampleRate:0.0}sn, " +
                    $"sonuç={cleaned.Length} char");

                return cleaned;
            }
            catch (OperationCanceledException)
            {
                FileLog.Write("[WhisperNetEngine] transkripsiyon iptal edildi.");
                return string.Empty;
            }
            catch (FileNotFoundException)
            {
                throw;
            }
            catch (Exception ex)
            {
                FileLog.Write($"[WhisperNetEngine] transkripsiyon hatası: {ex.Message}");
                // Bağlam bozulmuş olabilir; bir sonraki çağrı temiz yüklesin.
                Unload("hata sonrası");
                throw;
            }
            finally
            {
                _gate.Release();
                RestartIdleTimer();
            }
        }

        /// <summary>
        /// Canlı akış (real-time live preview) transkripsiyonu.
        /// Non-blocking kilit (_gate.WaitAsync(0)) kullanarak motor meşgulse beklemeden
        /// o anki döngü vuruşunu atlar; CPU/GPU üzerinde asla kuyruk birikmesine izin vermez.
        /// </summary>
        public async Task<string> TranscribeLivePreviewAsync(float[] samples, string language = "tr", CancellationToken cancellationToken = default)
        {
            if (_disposed || samples == null || samples.Length == 0) return string.Empty;

            var modelPath = _configManager.Current.Whisper.ResolvedModelPath;
            if (!File.Exists(modelPath)) return string.Empty;

            // Non-blocking kilit: Eğer motor nihai transkripsiyon veya başka bir işlemle meşgulse
            // ASLA kuyruk oluşturma ve bekleme; bu döngü vuruşunu (tick) hemen atla.
            bool acquired = false;
            try
            {
                acquired = await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return string.Empty;
            }

            if (!acquired) return string.Empty;

            try
            {
                var processor = await EnsureLoadedAsync(modelPath, language).ConfigureAwait(false);

                // Silero VAD: sessizlikte uydurma metin veya boş tahminleri önle
                var speech = ApplyVad(samples);
                if (speech.Length == 0) return string.Empty;

                cancellationToken.ThrowIfCancellationRequested();

                var builder = new StringBuilder();
                await foreach (var segment in processor.ProcessAsync(speech, cancellationToken).ConfigureAwait(false))
                {
                    builder.Append(segment.Text);
                }

                return TranscriptCleaner.Clean(builder.ToString());
            }
            catch (OperationCanceledException)
            {
                return string.Empty;
            }
            catch (Exception ex)
            {
                FileLog.Write($"[WhisperNetEngine] Canlı önizleme transkripsiyon hatası: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                _gate.Release();
                RestartIdleTimer();
            }
        }

        // --- model yaşam döngüsü (çağıranlar _gate'i tutuyor olmalı) ---

        private async Task<WhisperProcessor> EnsureLoadedAsync(string modelPath, string language)
        {
            // Başlangıç istemi processor kurulurken gömülür; sözlük diskte değiştiyse
            // processor'ı yeniden kurmadan yeni prompt devreye girmez.
            var dictionaryRevision = _dictionaryService?.Revision ?? 0;
            var sameModel = string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase);

            if (_processor != null && sameModel &&
                string.Equals(_loadedLanguage, language, StringComparison.OrdinalIgnoreCase) &&
                dictionaryRevision == _loadedDictionaryRevision)
            {
                return _processor;
            }

            // Model aynı ama dil ya da sözlük değiştiyse yalnızca processor'ı yeniden kur;
            // ağır olan factory (ve GPU'daki ağırlıklar) yerinde kalsın.
            if (_factory != null && sameModel)
            {
                var reason = dictionaryRevision != _loadedDictionaryRevision
                    ? $"sözlük güncellendi (r{dictionaryRevision})"
                    : $"dil değişti -> {language}";

                _processor?.Dispose();
                _processor = BuildProcessor(_factory, language);
                _loadedLanguage = language;
                _loadedDictionaryRevision = _dictionaryService?.Revision ?? 0;
                FileLog.Write($"[WhisperNetEngine] {reason} (model bellekte kaldı).");
                return _processor;
            }

            Unload("yeni model yüklenecek");

            WhisperNetRuntime.EnsureConfigured();

            var cfg = _configManager.Current.Whisper;
            var sw = Stopwatch.StartNew();

            // FromPath senkron ve ~1-3 sn sürebiliyor; çağıran thread'i bloklamasın.
            var factory = await Task.Run(() =>
            {
                var options = WhisperFactoryOptions.Default;
                options.UseGpu = WhisperNetRuntime.CudaEligible;
                options.UseFlashAttention = WhisperNetRuntime.CudaEligible;
                return WhisperFactory.FromPath(modelPath, options);
            }).ConfigureAwait(false);

            _factory = factory;
            _processor = BuildProcessor(factory, language);
            _loadedModelPath = modelPath;
            _loadedLanguage = language;
            _loadedDictionaryRevision = _dictionaryService?.Revision ?? 0;

            // Silero VAD: konuşma yoksa whisper'a hiç ses verilmez, böylece sessizlikte
            // uydurma metin üretmez. Model dosyası yoksa dikte bozulmasın diye VAD'siz devam edilir.
            var vadModelPath = cfg.ResolvedVadModelPath;
            if (File.Exists(vadModelPath))
            {
                try
                {
                    _vadFactory = WhisperVadFactory.FromPath(vadModelPath);
                }
                catch (Exception ex)
                {
                    _vadFactory = null;
                    FileLog.Write($"[WhisperNetEngine] VAD yüklenemedi, VAD'siz çalışılıyor: {ex.Message}");
                }
            }
            else
            {
                _vadFactory = null;
                FileLog.Write($"[WhisperNetEngine] VAD modeli bulunamadı, VAD'siz çalışılıyor: {vadModelPath}");
            }

            sw.Stop();
            FileLog.Write(
                $"[WhisperNetEngine] model yüklendi: {Path.GetFileName(modelPath)}, " +
                $"{sw.ElapsedMilliseconds}ms, gpu={WhisperNetRuntime.CudaEligible}, vad={_vadFactory != null}");

            return _processor;
        }

        private WhisperProcessor BuildProcessor(WhisperFactory factory, string language)
        {
            var threads = _configManager.Current.Whisper.Threads;
            var builder = factory.CreateBuilder().WithLanguage(language);
            if (threads > 0) builder = builder.WithThreads(threads);

            // Sözlükteki doğru yazımlar modele başlangıç istemi olarak verilir; "Python"/"GitHub"
            // gibi terimleri Türkçe fonetikle yazma eğilimi baştan azalır. Prompt bir garanti
            // değil yalnızca eğilimdir, bu yüzden sözlük düzeltmesi çıktıya ayrıca uygulanır.
            var prompt = _dictionaryService?.GetInitialPrompt();
            if (!string.IsNullOrWhiteSpace(prompt)) builder = builder.WithPrompt(prompt);

            return builder.Build();
        }

        private void Unload(string reason)
        {
            if (_processor == null && _factory == null && _vadFactory == null) return;

            try { _processor?.Dispose(); } catch (Exception ex) { FileLog.Write($"[WhisperNetEngine] processor dispose: {ex.Message}"); }
            try { _vadFactory?.Dispose(); } catch (Exception ex) { FileLog.Write($"[WhisperNetEngine] vad dispose: {ex.Message}"); }
            try { _factory?.Dispose(); } catch (Exception ex) { FileLog.Write($"[WhisperNetEngine] factory dispose: {ex.Message}"); }

            _processor = null;
            _vadFactory = null;
            _factory = null;
            _loadedModelPath = null;
            _loadedLanguage = null;

            FileLog.Write($"[WhisperNetEngine] model bellekten atıldı ({reason}).");
        }

        private void RestartIdleTimer()
        {
            if (_disposed) return;
            try { _idleTimer.Change(IdleTimeout, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
        }

        private void OnIdleTimeout(object? state)
        {
            if (_disposed) return;

            // Dikte sürüyorsa modeli çekme; sonraki turda tekrar denenir.
            if (!_gate.Wait(0)) { RestartIdleTimer(); return; }
            try
            {
                Unload($"{_configManager.Current.Whisper.IdleTimeoutMinutes:0} dk boşta kaldı");
            }
            finally
            {
                _gate.Release();
            }
        }

        // --- ses hazırlama ---

        /// <summary>
        /// Kaydı whisper'ın beklediği 16 kHz mono float formatına çevirir. WasapiRecorder
        /// mikrofonun kendi formatında (genelde 48 kHz stereo float) yazdığı için bu şart;
        /// whisper-cli bu dönüşümü kendi içinde (miniaudio) yapıyordu, Whisper.net yapmaz.
        /// </summary>
        private static float[] ReadSamples16kMono(string wavFilePath)
        {
            using var reader = new AudioFileReader(wavFilePath);

            ISampleProvider provider = reader;
            if (provider.WaveFormat.Channels > 1)
                provider = new MonoMixSampleProvider(provider);
            if (provider.WaveFormat.SampleRate != WhisperSampleRate)
                provider = new WdlResamplingSampleProvider(provider, WhisperSampleRate);

            var samples = new List<float>(WhisperSampleRate * 16);
            var chunk = new float[WhisperSampleRate];
            int read;
            while ((read = provider.Read(chunk, 0, chunk.Length)) > 0)
                samples.AddRange(new ReadOnlySpan<float>(chunk, 0, read));

            return samples.ToArray();
        }

        /// <summary>
        /// Silero VAD ile konuşma içeren aralıkları seçip birleştirir. Konuşma yoksa boş
        /// dizi döner (çağıran bunu "ses algılanamadı" olarak ele alır).
        /// </summary>
        private float[] ApplyVad(float[] samples)
        {
            if (_vadFactory == null) return samples;

            try
            {
                var threads = _configManager.Current.Whisper.Threads;
                var vadBuilder = _vadFactory.CreateBuilder();
                if (threads > 0) vadBuilder = vadBuilder.WithThreads(threads);
                using var vad = vadBuilder.Build();

                var segments = vad.DetectSpeech(samples);
                if (segments.Count == 0) return Array.Empty<float>();

                var speech = new List<float>(samples.Length);
                foreach (var segment in segments)
                {
                    var start = Math.Clamp((int)(segment.Start.TotalSeconds * WhisperSampleRate), 0, samples.Length);
                    var end = Math.Clamp((int)(segment.End.TotalSeconds * WhisperSampleRate), start, samples.Length);
                    if (end > start)
                        speech.AddRange(new ReadOnlySpan<float>(samples, start, end - start));
                }

                return speech.ToArray();
            }
            catch (Exception ex)
            {
                // VAD bir dikteyi kaybetmekten iyidir: hata hâlinde ham sesle devam et.
                FileLog.Write($"[WhisperNetEngine] VAD hatası, ham ses kullanılıyor: {ex.Message}");
                return samples;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _idleTimer.Dispose();

            // Çalışan bir dikte varsa bitmesini kısa süre bekle. Kilit alınamazsa native
            // bağlam hâlâ kullanımda demektir; altından çekmek çökme üretir. Süreç zaten
            // kapanıyor, bellek işletim sistemine bırakılır.
            if (_gate.Wait(TimeSpan.FromSeconds(5)))
            {
                try { Unload("uygulama kapanıyor"); }
                finally { _gate.Release(); }
            }
            else
            {
                FileLog.Write("[WhisperNetEngine] kapanışta dikte sürüyordu, model boşaltılmadı.");
            }

            _gate.Dispose();
        }
    }
}
