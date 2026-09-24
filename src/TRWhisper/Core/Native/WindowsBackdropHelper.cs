using System;
using System.Runtime.InteropServices;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Native
{
    /// <summary>
    /// Windows 11 DWM arka planı (Mica). Yalnızca DWM özniteliklerini ayarlar; pencerenin
    /// istemci alanını saydam yapmak (WindowChrome cam çerçevesi, arka plan fırçası) çağıranın işidir.
    /// Windows 10'da hiçbir şey yapmadan false döner.
    /// </summary>
    public static class WindowsBackdropHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;   // 22621+ (22H2)
        private const int DWMWA_MICA_EFFECT = 1029;         // 22000 (21H2), belgelenmemiş
        private const int DWMSBT_NONE = 1;
        private const int DWMSBT_MAINWINDOW = 2;             // Mica

        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x00080000;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

        private static bool HasSystemBackdropType => Environment.OSVersion.Version.Build >= 22621;

        public static bool ApplyMica(IntPtr hwnd, bool darkTheme = true)
        {
            if (hwnd == IntPtr.Zero || !IsWindows11) return false;
            try
            {
                // Koyu mod Mica'nın koyu tonunu seçer.
                SetAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, darkTheme ? 1 : 0);
                return HasSystemBackdropType
                    ? SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_MAINWINDOW)
                    : SetAttribute(hwnd, DWMWA_MICA_EFFECT, 1);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[Backdrop] Mica uygulanamadı: {ex.Message}");
                return false;
            }
        }

        public static bool RemoveMica(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindows11) return false;
            try
            {
                return HasSystemBackdropType
                    ? SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE)
                    : SetAttribute(hwnd, DWMWA_MICA_EFFECT, 0);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[Backdrop] Mica kaldırılamadı: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cam çerçeve istemci alanına yayıldığında DWM, özel başlık çubuğunun üstüne kendi
        /// simge durumu/ekranı kapla/kapat düğmelerini çizer. WS_SYSMENU kaldırılınca çizmez;
        /// pencerenin kendi düğmeleri WindowState ile çalıştığı için etkilenmez.
        /// </summary>
        public static void HideSystemCaptionButtons(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
        }

        private static bool SetAttribute(IntPtr hwnd, int attribute, int value) =>
            DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;
    }
}
