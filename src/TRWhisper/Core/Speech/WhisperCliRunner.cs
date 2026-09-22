using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Speech
{
    public class WhisperCliRunner : ITranscriptionEngine
    {
        private readonly ConfigManager _configManager;

        public WhisperCliRunner(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        public async Task<string> TranscribeAsync(string wavFilePath, string language = "tr", CancellationToken cancellationToken = default)
        {
            if (!File.Exists(wavFilePath))
            {
                FileLog.Write($"[WhisperCliRunner] Ses dosyası bulunamadı: {wavFilePath}");
                return string.Empty;
            }

            var cfg = _configManager.Current.Whisper;
            var cliPath = cfg.ResolvedCliPath;
            var modelPath = cfg.ResolvedModelPath;

            // Eğer whisper-cli.exe yoksa main.exe'yi kontrol et
            if (!File.Exists(cliPath))
            {
                var dir = Path.GetDirectoryName(cliPath) ?? string.Empty;
                var mainFallback = Path.Combine(dir, "main.exe");
                if (File.Exists(mainFallback))
                {
                    cliPath = mainFallback;
                }
                else
                {
                    FileLog.Write($"[WhisperCliRunner] HATA: whisper.cpp binary dosyası bulunamadı: {cliPath}");
                    FileLog.Write("[WhisperCliRunner] Lütfen 'scripts/setup.ps1' betiğini çalıştırarak binary ve modeli indirin.");
                    return string.Empty;
                }
            }

            if (!File.Exists(modelPath))
            {
                FileLog.Write($"[WhisperCliRunner] HATA: Whisper model dosyası bulunamadı: {modelPath}");
                FileLog.Write("[WhisperCliRunner] Lütfen 'scripts/setup.ps1' betiğini çalıştırarak modeli indirin.");
                return string.Empty;
            }

            var timeoutSeconds = cfg.TimeoutSeconds > 0 ? cfg.TimeoutSeconds : 120;

            // Transkripti "{wav}.txt" yan dosyasından okuyoruz (whisper bunu çıkmadan ÖNCE
            // yazıp kapatır). stdout/stderr'i BİLEREK redirect ETMİYORUZ: self-contained
            // tek-dosya WPF host'unda redirect edilen pipe'ın yazma ucu whisper-cli çıktıktan
            // sonra kapanmıyordu ve Process.WaitForExitAsync _output.EOF/_error.EOF'u
            // (token'sız) sonsuza kadar bekleyip "Çözümleniyor..." takılmasına yol açıyordu.
            var startInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                WorkingDirectory = Path.GetDirectoryName(cliPath) ?? string.Empty,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add(modelPath);
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(wavFilePath);
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add(language);
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add(cfg.Threads.ToString());
            if (cfg.NoTimestamps) startInfo.ArgumentList.Add("-nt");
            startInfo.ArgumentList.Add("-otxt");

            // Silero VAD: konuşma yoksa whisper boş çıktı verir (uydurma metin üretmez).
            // Model dosyası yoksa dikte bozulmasın diye VAD'siz devam edilir.
            var vadModelPath = cfg.ResolvedVadModelPath;
            if (File.Exists(vadModelPath))
            {
                startInfo.ArgumentList.Add("--vad");
                startInfo.ArgumentList.Add("-vm");
                startInfo.ArgumentList.Add(vadModelPath);
            }
            else
            {
                FileLog.Write($"[WhisperCliRunner] VAD modeli bulunamadı, VAD'siz çalışılıyor: {vadModelPath}");
            }

            var sidecarTxt = wavFilePath + ".txt";
            try { if (File.Exists(sidecarTxt)) File.Delete(sidecarTxt); } catch { }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            Process? process = null;
            try
            {
                process = new Process { StartInfo = startInfo };

                var sw = Stopwatch.StartNew();
                process.Start();
                FileLog.Write($"[WhisperCliRunner] başladı: {Path.GetFileName(cliPath)} (model={Path.GetFileName(modelPath)}, threads={cfg.Threads})");

                // Süreç bitişini HasExited yoklamasıyla bekle. HasExited (ve WaitForExit(int)),
                // WaitForExitAsync'in aksine asenkron okuyucu EOF'unu BEKLEMEZ.
                try
                {
                    while (!process.HasExited)
                    {
                        if (linkedCts.IsCancellationRequested) break;
                        await Task.Delay(150, linkedCts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // aşağıda HasExited kontrolüyle ele alınıyor
                }

                if (!process.HasExited)
                {
                    var reason = timeoutCts.IsCancellationRequested
                        ? $"zaman aşımı ({timeoutSeconds}s)"
                        : "iptal edildi";
                    FileLog.Write($"[WhisperCliRunner] {reason}, süreç ağacı sonlandırılıyor.");
                    TryKill(process);
                    try { process.WaitForExit(2000); } catch { }
                    return string.Empty;
                }

                sw.Stop();
                var exitCode = SafeExitCode(process);

                string sidecarText = string.Empty;
                if (File.Exists(sidecarTxt))
                {
                    try { sidecarText = File.ReadAllText(sidecarTxt, Encoding.UTF8); }
                    catch (Exception ex) { FileLog.Write($"[WhisperCliRunner] yan dosya okunamadı: {ex.Message}"); }
                }

                var cleaned = CleanTranscript(sidecarText);
                FileLog.Write($"[WhisperCliRunner] bitti: exit={exitCode}, {sw.ElapsedMilliseconds}ms, sidecar={sidecarText.Length} char, sonuç={cleaned.Length} char");

                return cleaned;
            }
            catch (Exception ex)
            {
                FileLog.Write($"[WhisperCliRunner] whisper-cli çalıştırma hatası: {ex.Message}");
                if (process != null) TryKill(process);
                return string.Empty;
            }
            finally
            {
                try { if (File.Exists(sidecarTxt)) File.Delete(sidecarTxt); } catch { }
                if (process != null) TryKill(process);
                process?.Dispose();
            }
        }

        private static int SafeExitCode(Process process)
        {
            try { return process.HasExited ? process.ExitCode : -1; }
            catch { return -1; }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[WhisperCliRunner] Kill hatası: {ex.Message}");
            }
        }

        private string CleanTranscript(string raw) => TranscriptCleaner.Clean(raw);
    }
}
