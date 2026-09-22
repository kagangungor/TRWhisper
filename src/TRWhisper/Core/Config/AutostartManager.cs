using System;
using System.Diagnostics;
using Microsoft.Win32;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Config
{
    /// <summary>
    /// Windows oturum açılışında otomatik başlatmayı HKCU\...\CurrentVersion\Run anahtarıyla yönetir.
    /// HKCU kullanılır: yönetici hakkı gerektirmez ve yalnızca mevcut kullanıcıyı etkiler.
    /// </summary>
    public static class AutostartManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TRWhisper";

        /// <summary>
        /// Kayıt defterine yazılacak komut. Tek dosyalık yayında süreç yolu doğrudan exe'dir;
        /// geliştirme sırasında (dotnet ile çalışırken) .dll olabileceğinden o durumda
        /// otomatik başlatma anlamsızdır ve yazılmaz.
        /// </summary>
        public static string? GetExecutablePath()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                var path = process.MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) return null;
                if (path.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return null;
                return path;
            }
            catch (Exception ex)
            {
                FileLog.Write($"[Autostart] Çalıştırılabilir yolu alınamadı: {ex.Message}");
                return null;
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[Autostart] Durum okunamadı: {ex.Message}");
                return false;
            }
        }

        /// <summary>Otomatik başlatmayı açar/kapatır. Başarılıysa true döner.</summary>
        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (key == null)
                {
                    FileLog.Write("[Autostart] Run anahtarı açılamadı.");
                    return false;
                }

                if (!enabled)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    FileLog.Write("[Autostart] Kapatıldı.");
                    return true;
                }

                var exe = GetExecutablePath();
                if (string.IsNullOrEmpty(exe))
                {
                    FileLog.Write("[Autostart] Uygulama yolu belirlenemedi, açılamadı.");
                    return false;
                }

                // Yol boşluk içerebilir; tırnak zorunlu.
                key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
                FileLog.Write($"[Autostart] Açıldı: {exe}");
                return true;
            }
            catch (Exception ex)
            {
                FileLog.Write($"[Autostart] Ayarlanamadı: {ex.Message}");
                return false;
            }
        }
    }
}
