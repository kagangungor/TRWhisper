using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Audio
{
    /// <summary>
    /// Modern Windows Core Audio (WASAPI) tabanlı mikrofon kaydedici.
    /// Eski WinMM/MME WaveIn yerine doğrudan Windows native WASAPI kullanır.
    /// waveInReset kaynaklı sürücü kilitlenmelerini (deadlock) tamamen önler.
    /// </summary>
    public class WasapiRecorder : IAudioRecorder
    {
        private readonly ConfigManager? _configManager;
        private WasapiCapture? _capture;
        private WaveFileWriter? _writer;
        private string? _currentOutputWavPath;
        private readonly object _lock = new();
        private bool _isRecording;
        private volatile float _currentLevel;
        private TaskCompletionSource<string>? _stopCompletionSource;

        // Canlı akış (real-time live preview) 16 kHz mono float tamponu
        private BufferedWaveProvider? _liveBufferedWaveProvider;
        private ISampleProvider? _liveSampleProvider;
        private readonly List<float> _live16kSamples = new(16000 * 30);
        private readonly object _liveSamplesLock = new();
        private readonly float[] _liveReadChunk = new float[4096];

        public WasapiRecorder(ConfigManager? configManager = null)
        {
            _configManager = configManager;
        }

        /// <summary>Son tamponun tepe genliği (0..1); kayıt yokken 0.</summary>
        public float CurrentLevel => _isRecording ? _currentLevel : 0f;

        public bool IsRecording
        {
            get
            {
                lock (_lock)
                {
                    return _isRecording;
                }
            }
        }

        /// <summary>
        /// Tampondaki tepe genliği 0..1 aralığında döndürür. WASAPI paylaşımlı modda format
        /// genelde 32-bit IEEE float'tır; 16-bit PCM de desteklenir, tanınmayan formatta 0 döner
        /// (bu durumda eller serbest mod sessizlikte kesmez, kullanıcı tuşla bitirir).
        /// </summary>
        private static float ComputePeak(byte[] buffer, int bytesRecorded, WaveFormat? format)
        {
            if (format == null || bytesRecorded <= 0) return 0f;

            float peak = 0f;
            try
            {
                if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
                {
                    for (int i = 0; i + 3 < bytesRecorded; i += 4)
                    {
                        var sample = Math.Abs(BitConverter.ToSingle(buffer, i));
                        if (sample > peak) peak = sample;
                    }
                }
                else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
                {
                    for (int i = 0; i + 1 < bytesRecorded; i += 2)
                    {
                        var sample = Math.Abs(BitConverter.ToInt16(buffer, i) / 32768f);
                        if (sample > peak) peak = sample;
                    }
                }
            }
            catch
            {
                return 0f;
            }

            return peak > 1f ? 1f : peak;
        }

        /// <summary>
        /// Ayarlarda seçilen mikrofonu açar. Cihaz çıkarılmış veya devre dışıysa sessizce
        /// sistem varsayılanına düşülür: yanlış bir cihaz kimliği yüzünden dikte hiç
        /// başlamamasındansa varsayılan mikrofonla çalışmak yeğdir.
        /// </summary>
        private WasapiCapture CreateCapture()
        {
            var deviceId = _configManager?.Current.Audio.InputDeviceId;
            if (string.IsNullOrWhiteSpace(deviceId)) return new WasapiCapture();

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    if (!string.Equals(device.ID, deviceId, StringComparison.Ordinal)) { device.Dispose(); continue; }
                    FileLog.Write($"[WasapiRecorder] Seçili mikrofon: {device.FriendlyName}");
                    return new WasapiCapture(device);
                }
                FileLog.Write($"[WasapiRecorder] Seçili mikrofon bulunamadı ({deviceId}), varsayılana dönülüyor.");
            }
            catch (Exception ex)
            {
                FileLog.Write($"[WasapiRecorder] Mikrofon seçilemedi ({ex.Message}), varsayılana dönülüyor.");
            }
            return new WasapiCapture();
        }

        public void StartRecording(string outputWavPath)
        {
            lock (_lock)
            {
                if (_isRecording) return;

                _currentOutputWavPath = outputWavPath;

                // Eski geçici dosya varsa temizle
                if (File.Exists(outputWavPath))
                {
                    try { File.Delete(outputWavPath); } catch { }
                }

                var dir = Path.GetDirectoryName(outputWavPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                _stopCompletionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                try
                {
                    // Ayarlardaki mikrofon; seçili değilse/bulunamazsa sistem varsayılanı
                    var capture = CreateCapture();
                    var writer = new WaveFileWriter(outputWavPath, capture.WaveFormat);

                    capture.DataAvailable += OnDataAvailable;
                    capture.RecordingStopped += OnRecordingStopped;

                    // Canlı akış (streaming live preview) için 16 kHz mono float tamponu
                    lock (_liveSamplesLock)
                    {
                        _live16kSamples.Clear();
                    }

                    try
                    {
                        var buffered = new BufferedWaveProvider(capture.WaveFormat)
                        {
                            DiscardOnBufferOverflow = true,
                            ReadFully = false
                        };
                        ISampleProvider sampleProvider = buffered.ToSampleProvider();
                        if (sampleProvider.WaveFormat.Channels > 1)
                        {
                            sampleProvider = new MonoMixSampleProvider(sampleProvider);
                        }
                        if (sampleProvider.WaveFormat.SampleRate != 16000)
                        {
                            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 16000);
                        }

                        _liveBufferedWaveProvider = buffered;
                        _liveSampleProvider = sampleProvider;
                    }
                    catch (Exception liveEx)
                    {
                        FileLog.Write($"[WasapiRecorder] Canlı akış tamponu başlatılamadı: {liveEx.Message}");
                        _liveBufferedWaveProvider = null;
                        _liveSampleProvider = null;
                    }

                    _writer = writer;
                    _capture = capture;
                    _capture.StartRecording();
                    _isRecording = true;
                    _currentLevel = 0f;

                    FileLog.Write($"[WasapiRecorder] WASAPI ses kaydı başladı: {outputWavPath} ({capture.WaveFormat})");
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[WasapiRecorder] WASAPI kayıt başlatılamadı: {ex.Message}");
                    CleanupUnsafe();
                    throw;
                }
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            // Seviye ölçümü kilidin DIŞINDA: eller serbest modun yoklaması kayıt yazımını
            // beklememeli ve bu hesap tamponu yalnızca okur.
            _currentLevel = ComputePeak(e.Buffer, e.BytesRecorded, _capture?.WaveFormat);

            // Canlı akış (real-time live preview) tamponlama
            if (_liveBufferedWaveProvider != null && _liveSampleProvider != null && e.BytesRecorded > 0)
            {
                try
                {
                    _liveBufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
                    int read;
                    while ((read = _liveSampleProvider.Read(_liveReadChunk, 0, _liveReadChunk.Length)) > 0)
                    {
                        lock (_liveSamplesLock)
                        {
                            for (int i = 0; i < read; i++)
                            {
                                _live16kSamples.Add(_liveReadChunk[i]);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[WasapiRecorder] Canlı akış tamponlama hatası: {ex.Message}");
                }
            }

            lock (_lock)
            {
                if (_writer != null && e.BytesRecorded > 0)
                {
                    try
                    {
                        _writer.Write(e.Buffer, 0, e.BytesRecorded);
                    }
                    catch (Exception ex)
                    {
                        FileLog.Write($"[WasapiRecorder] Ses verisi yazma hatası: {ex.Message}");
                    }
                }
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            TaskCompletionSource<string>? tcs;
            string path;

            lock (_lock)
            {
                try
                {
                    if (_writer != null)
                    {
                        _writer.Flush();
                        _writer.Dispose();
                        _writer = null;
                    }
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[WasapiRecorder] WAV kapatma hatası (RecordingStopped): {ex.Message}");
                }
                finally
                {
                    _writer = null;
                    _isRecording = false;
                }

                path = _currentOutputWavPath ?? string.Empty;
                tcs = _stopCompletionSource;
                ClearLiveBuffer();
            }

            if (tcs != null && !tcs.Task.IsCompleted)
            {
                if (e.Exception != null)
                {
                    FileLog.Write($"[WasapiRecorder] Kayıt hatası: {e.Exception.Message}");
                }
                tcs.TrySetResult(path);
            }
        }

        public async Task<string> StopRecordingAsync()
        {
            WasapiCapture? captureToStop;
            TaskCompletionSource<string>? tcs;
            string outputPath;

            lock (_lock)
            {
                if (!_isRecording || _capture == null)
                {
                    return _currentOutputWavPath ?? string.Empty;
                }

                captureToStop = _capture;
                tcs = _stopCompletionSource;
                outputPath = _currentOutputWavPath ?? string.Empty;
            }

            var sw = Stopwatch.StartNew();
            if (captureToStop != null)
            {
                try
                {
                    // WASAPI StopRecording() thread'e sinyal gönderir; bloklamaz.
                    captureToStop.StopRecording();
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[WasapiRecorder] StopRecording hatası: {ex.Message}");
                }
            }

            if (tcs != null)
            {
                // RecordingStopped'ın tetiklenmesi için en fazla 800 ms bekle (WASAPI ile 10-60 ms sürer)
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(800)).ConfigureAwait(false);
                if (completed != tcs.Task)
                {
                    FileLog.Write("[WasapiRecorder] RecordingStopped zaman aşımı (800ms); dosya zorla kapatılıyor.");
                }
            }
            sw.Stop();

            lock (_lock)
            {
                try
                {
                    if (_writer != null)
                    {
                        _writer.Flush();
                        _writer.Dispose();
                        _writer = null;
                    }
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[WasapiRecorder] WAV kapatma hatası (stop): {ex.Message}");
                }
                finally
                {
                    _writer = null;
                    _isRecording = false;
                }

                if (_capture != null)
                {
                    try { _capture.DataAvailable -= OnDataAvailable; } catch { }
                    try { _capture.RecordingStopped -= OnRecordingStopped; } catch { }
                    try { _capture.Dispose(); } catch { }
                    _capture = null;
                }

                ClearLiveBuffer();
            }

            FileLog.Write($"[WasapiRecorder] Ses kaydı durduruldu ({sw.ElapsedMilliseconds}ms): {outputPath}");
            return outputPath;
        }

        /// <summary>
        /// Kayıt devam ederken o ana kadar biriken 16 kHz mono float ses örneklerini döndürür.
        /// maxLastSamples belirtilmişse yalnızca sondaki dilimi alır (örn. son 10 saniye için 160000).
        /// </summary>
        public float[] GetRecordedSamplesSnapshot(int? maxLastSamples = null)
        {
            lock (_liveSamplesLock)
            {
                if (_live16kSamples.Count == 0) return Array.Empty<float>();

                if (maxLastSamples.HasValue && maxLastSamples.Value > 0 && _live16kSamples.Count > maxLastSamples.Value)
                {
                    var count = maxLastSamples.Value;
                    var startIndex = _live16kSamples.Count - count;
                    var result = new float[count];
                    _live16kSamples.CopyTo(startIndex, result, 0, count);
                    return result;
                }

                return _live16kSamples.ToArray();
            }
        }

        private void ClearLiveBuffer()
        {
            lock (_liveSamplesLock)
            {
                _live16kSamples.Clear();
            }
            _liveBufferedWaveProvider = null;
            _liveSampleProvider = null;
        }

        private void CleanupUnsafe()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.RecordingStopped -= OnRecordingStopped;
                    _capture.Dispose();
                }
            }
            catch { }
            finally { _capture = null; }

            try { _writer?.Dispose(); } catch { } finally { _writer = null; }
            _isRecording = false;
            ClearLiveBuffer();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                CleanupUnsafe();
            }
            GC.SuppressFinalize(this);
        }
    }
}
