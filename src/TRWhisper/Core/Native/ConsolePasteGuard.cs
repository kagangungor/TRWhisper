using System;
using System.Text;

namespace TRWhisper.Core.Native
{
    /// <summary>
    /// Konsol pencerelerine (cmd, PowerShell, Windows Terminal, Git Bash...) giden metni
    /// tek satıra indirir. Konsolda satır sonu Enter demektir: "yeni satır" komutu ya da
    /// AI Asistanı'nın çok satırlı yanıtı yapıştırıldığı anda kullanıcı görmeden komut
    /// olarak çalışırdı. Tek satıra inen metin istem satırında bekler; kullanıcı okuyup
    /// Enter'a kendisi basar.
    /// </summary>
    public static class ConsolePasteGuard
    {
        /// <summary>
        /// Konsol pencere sınıfları: conhost, Windows Terminal, mintty (Git Bash/Cygwin),
        /// ConEmu/Cmder, PuTTY. VS Code gibi uygulamaların gömülü terminalleri sınıf adıyla
        /// ayırt edilemez; onlar kendi çok satırlı yapıştırma uyarılarına güvenir.
        /// </summary>
        private static readonly string[] ConsoleClasses =
        {
            "ConsoleWindowClass",
            "CASCADIA_HOSTING_WINDOW_CLASS",
            "mintty",
            "VirtualConsoleClass",
            "PuTTY",
        };

        public static bool IsConsoleWindowClass(string? className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            foreach (var cls in ConsoleClasses)
            {
                if (string.Equals(cls, className, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Satır sonlarını (CRLF, LF, CR, U+0085, U+2028, U+2029) ve sekmeyi tek boşluğa
        /// çevirir, diğer denetim karakterlerini (ESC, Ctrl+C karşılığı \x03 vb.) atar,
        /// sondaki boşlukları kırpar.
        /// </summary>
        public static string ToSingleLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') continue; // CRLF → tek ayırıcı

                if (c is '\r' or '\n' or '\t' or '\u0085' or '\u2028' or '\u2029')
                {
                    if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                }
                else if (!char.IsControl(c))
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().TrimEnd();
        }
    }
}
