using System;
using System.IO;
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
    }
}
