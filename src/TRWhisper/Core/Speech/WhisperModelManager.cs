using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Speech
{
    public record ModelDownloadProgress(
        string ModelId,
        long BytesDownloaded,
        long TotalBytes,
        double Percentage,
        double SpeedBytesPerSecond);

    public class WhisperModelInfo
    {
        public string Id { get; init; } = "";
        public string FileName { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Description { get; init; } = "";
        public long ApproximateSizeBytes { get; init; }
        public string DownloadUrl { get; init; } = "";
        public string? Sha256 { get; init; }
        public bool NeedsGpu { get; init; }
        public bool IsVad { get; init; }

        public string RelativePath => Path.Combine("tools", "whisper", FileName);

        public bool IsInstalled => File.Exists(ResolvedPath);
        public long InstalledSizeBytes
        {
            get
            {
                try
                {
                    var path = ResolvedPath;
                    return File.Exists(path) ? new FileInfo(path).Length : 0;
                }
                catch
                {
                    return 0;
                }
            }
        }

        public string ResolvedPath => Path.Combine(WhisperModelManager.GetToolsDirectory(), FileName);

        public string FormattedSize => WhisperModelManager.FormatBytes(InstalledSizeBytes > 0 ? InstalledSizeBytes : ApproximateSizeBytes);
    }

    public static class WhisperModelManager
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        public static readonly IReadOnlyList<WhisperModelInfo> Catalog = new List<WhisperModelInfo>
        {
            new()
            {
                Id = "large-v3-turbo-q5_0",
                FileName = "ggml-large-v3-turbo-q5_0.bin",
                DisplayName = "Large-v3 Turbo (Q5_0) — Önerilen",
                Description = "En yüksek Türkçe doğruluğu ve optimize bellek kullanımı. (GPU önerilir)",
                ApproximateSizeBytes = 574_041_195,
                DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin",
                Sha256 = "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2",
                NeedsGpu = true
            },
            new()
            {
                Id = "small",
                FileName = "ggml-small.bin",
                DisplayName = "Small (CPU Dostu)",
                Description = "Hızlı ve dengeli. Ekran kartı olmayan sistemler için ideal.",
                ApproximateSizeBytes = 487_601_967,
                DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
                Sha256 = "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b",
                NeedsGpu = false
            },
            new()
            {
                Id = "base",
                FileName = "ggml-base.bin",
                DisplayName = "Base (Çok Hızlı)",
                Description = "Çok hafif ve düşük kaynak kullanımı. Hızlı notlar için.",
                ApproximateSizeBytes = 147_951_465,
                DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
                Sha256 = "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe",
                NeedsGpu = false
            },
            new()
            {
                Id = "tiny",
                FileName = "ggml-tiny.bin",
                DisplayName = "Tiny (Ultra Hafif)",
                Description = "En az bellek tüketen Whisper modeli (~75 MB). Minimum doğruluk.",
                ApproximateSizeBytes = 77_691_741,
                DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin",
                Sha256 = "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21",
                NeedsGpu = false
            },
            new()
            {
                Id = "medium",
                FileName = "ggml-medium.bin",
                DisplayName = "Medium",
                Description = "Yüksek doğruluklu standart model (~1.5 GB).",
                ApproximateSizeBytes = 1_533_774_272,
                DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin",
                Sha256 = "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208",
                NeedsGpu = true
            },
            new()
            {
                Id = "large-v3",
                FileName = "ggml-large-v3.bin",
                DisplayName = "Large-v3 (Tam Boyut)",
                Description = "En kapsamlı model (~2.9 GB). Güçlü ekran kartı gerektirir.",
                ApproximateSizeBytes = 3_095_033_408,
                DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin",
                Sha256 = "64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2",
                NeedsGpu = true
            },
            new()
            {
                Id = "silero-vad",
                FileName = "ggml-silero-v6.2.0.bin",
                DisplayName = "Silero VAD (Ses Algılama)",
                Description = "Sessizlik anlarını filtreleyerek uydurma metin üretimini engeller.",
                ApproximateSizeBytes = 885_098,
                DownloadUrl = "https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v6.2.0.bin",
                Sha256 = "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987",
                NeedsGpu = false,
                IsVad = true
            }
        };

        public static string GetToolsDirectory()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var direct = Path.Combine(baseDir, "tools", "whisper");
            if (Directory.Exists(direct)) return direct;

            var currentDir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 5 && currentDir != null; i++)
            {
                var candidate = Path.Combine(currentDir.FullName, "tools", "whisper");
                if (Directory.Exists(candidate)) return candidate;
                currentDir = currentDir.Parent;
            }

            var currentWorking = Path.Combine(Directory.GetCurrentDirectory(), "tools", "whisper");
            if (Directory.Exists(currentWorking)) return currentWorking;

            try
            {
                Directory.CreateDirectory(direct);
                return direct;
            }
            catch
            {
                return baseDir;
            }
        }

        public static WhisperModelInfo? GetModelById(string id)
        {
            return Catalog.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static WhisperModelInfo? FindByPath(string path)
        {
            var fileName = Path.GetFileName(path);
            return Catalog.FirstOrDefault(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        }

        public static List<WhisperModelInfo> GetInstalledModels()
        {
            return Catalog.Where(m => !m.IsVad && m.IsInstalled).ToList();
        }

        public static async Task<(bool Success, string Message)> DownloadModelAsync(
            string modelId,
            IProgress<ModelDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var model = GetModelById(modelId);
            if (model == null)
            {
                return (false, $"Bilinmeyen model kimliği: {modelId}");
            }

            var toolsDir = GetToolsDirectory();
            if (!Directory.Exists(toolsDir))
            {
                Directory.CreateDirectory(toolsDir);
            }

            var finalPath = Path.Combine(toolsDir, model.FileName);
            var tempPath = finalPath + ".downloading";

            try
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }

                FileLog.Write($"[WhisperModelManager] Model indirme başlatılıyor: {model.DisplayName} -> {model.DownloadUrl}");

                using var response = await HttpClient.GetAsync(
                    model.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? model.ApproximateSizeBytes;
                long totalDownloaded = 0;

                var stopwatch = Stopwatch.StartNew();
                var lastReportTime = DateTime.UtcNow;
                long lastDownloadedBytes = 0;

                await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                        totalDownloaded += bytesRead;

                        var now = DateTime.UtcNow;
                        if ((now - lastReportTime).TotalMilliseconds >= 250 || totalDownloaded == totalBytes)
                        {
                            var timeDelta = (now - lastReportTime).TotalSeconds;
                            double speed = timeDelta > 0 ? (totalDownloaded - lastDownloadedBytes) / timeDelta : 0;
                            double percentage = totalBytes > 0 ? Math.Min(100.0, (double)totalDownloaded / totalBytes * 100.0) : 0;

                            progress?.Report(new ModelDownloadProgress(
                                model.Id,
                                totalDownloaded,
                                totalBytes,
                                percentage,
                                speed));

                            lastReportTime = now;
                            lastDownloadedBytes = totalDownloaded;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(model.Sha256))
                {
                    FileLog.Write($"[WhisperModelManager] Model SHA-256 bütünlüğü doğrulanıyor: {model.DisplayName}");
                    await using (var checkStream = File.OpenRead(tempPath))
                    {
                        var hashBytes = await SHA256.HashDataAsync(checkStream, cancellationToken).ConfigureAwait(false);
                        var computedHash = Convert.ToHexString(hashBytes);

                        if (!string.Equals(computedHash, model.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(tempPath); } catch { }
                            FileLog.Write($"[WhisperModelManager] Model SHA-256 doğrulaması BAŞARISIZ! Beklenen: {model.Sha256}, Hesaplanan: {computedHash}");
                            return (false, "Model doğrulama hatası: İndirilen model dosyasının SHA-256 özeti eşleşmiyor. Dosya güvenlik gerekçesiyle silindi.");
                        }
                    }

                    FileLog.Write($"[WhisperModelManager] Model SHA-256 doğrulaması başarılı: {model.DisplayName}");
                }

                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                File.Move(tempPath, finalPath);
                FileLog.Write($"[WhisperModelManager] Model başarıyla indirildi: {finalPath}");

                return (true, $"{model.DisplayName} başarıyla indirildi.");
            }
            catch (OperationCanceledException)
            {
                FileLog.Write($"[WhisperModelManager] Model indirme iptal edildi: {model.DisplayName}");
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return (false, "İndirme iptal edildi.");
            }
            catch (Exception ex)
            {
                FileLog.Write($"[WhisperModelManager] Model indirme hatası: {ex.Message}");
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return (false, $"İndirme hatası: {ex.Message}");
            }
        }

        public static (bool Success, string Message) DeleteModel(string modelId)
        {
            var model = GetModelById(modelId);
            if (model == null) return (false, "Model bulunamadı.");

            try
            {
                var path = model.ResolvedPath;
                if (!File.Exists(path))
                {
                    return (false, "Model dosyası zaten mevcut değil.");
                }

                File.Delete(path);
                FileLog.Write($"[WhisperModelManager] Model dosyası silindi: {path}");
                return (true, $"{model.DisplayName} modeli diskten silindi.");
            }
            catch (Exception ex)
            {
                FileLog.Write($"[WhisperModelManager] Model silinemedi: {ex.Message}");
                return (false, $"Dosya silinemedi: {ex.Message}");
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(double)bytes / 1024:0.#} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{(double)bytes / (1024 * 1024):0.#} MB";
            return $"{(double)bytes / (1024 * 1024 * 1024):0.##} GB";
        }
    }
}
