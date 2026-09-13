using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
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
        private WasapiCapture? _capture;
        private WaveFileWriter? _writer;
        private string? _currentOutputWavPath;
        private readonly object _lock = new();
        private bool _isRecording;
        private TaskCompletionSource<string>? _stopCompletionSource;

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
                    // Varsayılan mikrofonu native WASAPI ile başlat
                    var capture = new WasapiCapture();
                    var writer = new WaveFileWriter(outputWavPath, capture.WaveFormat);

                    capture.DataAvailable += OnDataAvailable;
                    capture.RecordingStopped += OnRecordingStopped;

                    _writer = writer;
                    _capture = capture;
                    _capture.StartRecording();
                    _isRecording = true;

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
            }

            FileLog.Write($"[WasapiRecorder] Ses kaydı durduruldu ({sw.ElapsedMilliseconds}ms): {outputPath}");
            return outputPath;
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
