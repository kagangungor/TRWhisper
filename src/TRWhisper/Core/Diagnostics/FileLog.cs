using System;
using System.IO;
using System.Linq;
using System.Text;

namespace TRWhisper.Core.Diagnostics
{
    /// <summary>
    /// Basit, kilit korumalı dosya logu. Uygulama bir WinExe olduğu için
    /// <see cref="Console"/> çıktısı hiçbir yere gitmiyordu; bu sınıf
    /// <c>%USERPROFILE%\Dictation\trwhisper.log</c> dosyasına yazar ve dosya ~1 MB'ı
    /// aşınca bir kez <c>.old</c> olarak döndürür.
    /// </summary>
    public static class FileLog
    {
        private static readonly object Gate = new();
        private const long MaxBytes = 1_000_000;

        /// <summary>Eski sürümlerin düz metin transkript satırlarını tanıyan imza.</summary>
        private const string LegacyTranscriptMarker = "] Whisper tamamlandı: '";

        private static readonly string LogPath = Path.Combine(
            Environment.ExpandEnvironmentVariables("%USERPROFILE%\\Dictation"),
            "trwhisper.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    var dir = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                    {
                        var old = LogPath + ".old";
                        try { if (File.Exists(old)) File.Delete(old); } catch { }
                        try { File.Move(LogPath, old); } catch { }
                    }

                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}";
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Loglama hatası hiçbir zaman uygulama akışını bozmamalı.
            }
        }

        /// <summary>
        /// 2.0.1 öncesi derlemeler dikte metnini bu günlüğe düz metin olarak yazıyordu
        /// ("Whisper tamamlandı: '...'"). Güncel sürüm yalnızca karakter sayısını yazar;
        /// bu yordam eski kurulumlardan kalan satırları açılışta bir kez temizler.
        /// </summary>
        public static void PurgeLegacyTranscriptLines()
        {
            int removed = 0;

            lock (Gate)
            {
                foreach (var path in new[] { LogPath, LogPath + ".old" })
                {
                    try
                    {
                        if (!File.Exists(path)) continue;

                        var lines = File.ReadAllLines(path, Encoding.UTF8);
                        var kept = lines
                            .Where(line => !line.Contains(LegacyTranscriptMarker, StringComparison.Ordinal))
                            .ToArray();

                        if (kept.Length == lines.Length) continue;

                        File.WriteAllLines(path, kept, Encoding.UTF8);
                        removed += lines.Length - kept.Length;
                    }
                    catch
                    {
                        // Temizlik başarısız olursa uygulama açılışı engellenmemeli.
                    }
                }
            }

            if (removed > 0)
                Write($"[FileLog] Eski sürümlerden kalan {removed} düz metin transkript satırı günlükten silindi.");
        }
    }
}
