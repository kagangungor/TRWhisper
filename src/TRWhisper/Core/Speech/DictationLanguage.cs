using System;
using System.Collections.Generic;
using System.Linq;
using TRWhisper.Core.Config;

namespace TRWhisper.Core.Speech
{
    /// <summary>
    /// Dikte dili için kısayol geçişi ve kullanıcıya gösterilen adlar.
    /// </summary>
    public static class DictationLanguage
    {
        /// <summary>
        /// Uygulamanın sunduğu dikte dilleri. Ayarlar'daki dil listesi, geçiş çipleri ve
        /// tepsi menüsü buradan beslenir. (Kısayolun geçiş sırasını kullanıcı belirler.)
        /// </summary>
        public static readonly IReadOnlyList<(string Code, string Name)> Supported = new[]
        {
            ("tr", "Türkçe"),
            ("en", "İngilizce"),
            ("de", "Almanca"),
            ("fr", "Fransızca"),
            ("auto", "Otomatik algıla"),
        };

        public static readonly IReadOnlyList<string> DefaultFastSwitchLanguages = new[] { "tr", "en" };

        /// <summary>
        /// Geçiş listesini desteklenen dillere süzer ve tekrarları atar; kullanıcının verdiği
        /// sıra (geçiş sırası) korunur, kodlar küçük harfe çekilir. İkiden az dil kalırsa geçiş
        /// anlamsızdır; varsayılana (TR, EN) döner.
        /// </summary>
        public static IReadOnlyList<string> NormalizeFastSwitchLanguages(IEnumerable<string>? codes)
        {
            var list = new List<string>();
            foreach (var raw in codes ?? Enumerable.Empty<string>())
            {
                var code = Supported.FirstOrDefault(l => string.Equals(l.Code, raw?.Trim(), StringComparison.OrdinalIgnoreCase)).Code;
                if (code != null && !list.Contains(code)) list.Add(code);
            }
            return list.Count >= 2 ? list : DefaultFastSwitchLanguages;
        }

        /// <summary>
        /// Geçiş sırasında bir sonraki dile geçer (sondan başa döner). Etkin dil listede
        /// yoksa listenin ilk diline gider. Liste verilmezse TR ↔ EN.
        /// </summary>
        public static string NextFastSwitch(string? current, IEnumerable<string>? languages = null)
        {
            var list = NormalizeFastSwitchLanguages(languages ?? DefaultFastSwitchLanguages);
            var index = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], current?.Trim(), StringComparison.OrdinalIgnoreCase)) { index = i; break; }
            }
            return index < 0 ? list[0] : list[(index + 1) % list.Count];
        }

        /// <summary>
        /// Hızlı geçiş ayarı açıksa dili bir sonrakine çevirir ve true döner; kapalıysa
        /// yapılandırmaya dokunmaz.
        /// </summary>
        public static bool TryFastSwitch(GeneralConfig general)
        {
            if (!general.EnableLanguageFastSwitch) return false;
            general.Language = NextFastSwitch(general.Language, general.FastSwitchLanguages);
            return true;
        }

        /// <summary>
        /// Dil kısayolu klavye hook'unda çalışabilmeli ve günlük yazımı bozmamalı: fare tuşu,
        /// tek başına değiştirici (Alt her Alt+X'te tetiklenirdi), değiştiricisiz harf/rakam
        /// ya da yalnız Shift'li tuş (yazarken her basışta dil değişirdi) ve bas-konuş tuşunun
        /// aynısı olamaz. Ctrl/Alt/Win'siz yalnızca F1-F24 kabul edilir.
        /// </summary>
        public static bool IsValidFastSwitchHotkey(string? hotkey, string? pushToTalkKey)
        {
            if (string.IsNullOrWhiteSpace(hotkey)) return false;
            if (string.Equals(hotkey.Trim(), pushToTalkKey?.Trim(), StringComparison.OrdinalIgnoreCase)) return false;

            var binding = Native.HotkeyBinding.Parse(hotkey, "None");
            if (binding.IsNone || binding.IsMouse) return false;

            if (binding.PrimaryVk is 0 or 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1
                or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C)
                return false;

            // Shift+harf büyük harf yazmaktır; yalnız Ctrl/Alt/Win kombinasyonu sayılır.
            return (binding.Modifiers & ~Native.HotkeyModifiers.Shift) != Native.HotkeyModifiers.None
                || binding.PrimaryVk is >= 0x70 and <= 0x87;
        }

        public static string DisplayName(string? code) => code?.Trim().ToLowerInvariant() switch
        {
            "tr" => "Türkçe (TR)",
            "en" => "İngilizce (EN)",
            "de" => "Almanca (DE)",
            "fr" => "Fransızca (FR)",
            "auto" => "Otomatik (Auto)",
            null or "" => "Türkçe (TR)",
            var other => other.ToUpperInvariant(),
        };
    }
}
