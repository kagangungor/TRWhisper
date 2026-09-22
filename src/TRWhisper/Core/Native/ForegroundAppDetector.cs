using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Native
{
    public interface IForegroundAppDetector
    {
        /// <summary>
        /// Ön plandaki pencerenin süreç adını (örn: "OUTLOOK", "Code", "devenv") döndürür.
        /// Başarısızlık durumunda null döner, asla hata fırlatmaz.
        /// </summary>
        string? GetForegroundProcessName();

        /// <summary>
        /// Süreç adını kullanıcıya gösterilecek estetik/dostça ada dönüştürür (örn: "OUTLOOK" -> "Outlook", "Code" -> "VS Code").
        /// </summary>
        string GetFriendlyAppName(string? processName);
    }

    public class ForegroundAppDetector : IForegroundAppDetector
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public string? GetForegroundProcessName()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;

                if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return null;

                using var process = Process.GetProcessById((int)pid);
                return process.ProcessName;
            }
            catch (Exception ex)
            {
                FileLog.Write($"[ForegroundAppDetector] Ön plan süreç adı okunamadı: {ex.Message}");
                return null;
            }
        }

        public string GetFriendlyAppName(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return string.Empty;

            return processName.ToLowerInvariant() switch
            {
                "outlook" => "Outlook",
                "thunderbird" => "Thunderbird",
                "code" => "VS Code",
                "devenv" => "Visual Studio",
                "windowsterminal" => "Terminal",
                "cmd" => "Komut Satırı",
                "powershell" or "pwsh" => "PowerShell",
                "slack" => "Slack",
                "discord" => "Discord",
                "telegram" => "Telegram",
                "notepad" => "Notepad",
                "winword" => "Word",
                "excel" => "Excel",
                "powerpnt" => "PowerPoint",
                "chrome" => "Chrome",
                "msedge" => "Edge",
                "firefox" => "Firefox",
                _ => char.ToUpper(processName[0]) + (processName.Length > 1 ? processName.Substring(1) : "")
            };
        }
    }
}
