using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TRWhisper.Core.Speech
{
    /// <summary>
    /// Whisper çıktısındaki zaman damgalarını, konuşma-dışı etiketleri ve sessizlikte
    /// üretilen bilinen uydurma cümleleri temizler. WhisperCliRunner ve WhisperNetEngine
    /// aynı kuralları kullansın diye ortak yere alındı.
    /// </summary>
    public static class TranscriptCleaner
    {
        private static readonly Regex TimestampRegex = new(@"\[\d{2}:\d{2}:\d{2}\.\d{3}\s*-->\s*\d{2}:\d{2}:\d{2}\.\d{3}\]", RegexOptions.Compiled);
        private static readonly Regex MetaRegex = new(@"\[(BLANK_AUDIO|MUSIC|LAUGHTER|APPLAUSE)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Tüm satır (veya tüm çıktı) tek bir köşeli- ya da normal-parantez ifadesinden ibaretse bu
        // whisper'ın konuşma-dışı etiketidir ([MÜZİK ÇALIYOR], [SESSİZLİK], (Müzik), (Alkış) ...).
        // Türkçe derlemede bu etiketler çıkabiliyor ve olduğu gibi yapıştırılıyordu.
        private static readonly Regex LineBracketRegex = new(@"^(\[[^\]]{0,40}\]|\([^\)]{0,40}\))$", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

        // Whisper'ın sessizlik/gürültüde uydurduğu, altyazı verisinden gelen bilinen Türkçe cümleler.
        // Yalnızca satırın TAMAMI eşleşirse atılır (küçük harfe çevrilip sondaki noktalama
        // yok sayılarak); böylece gerçek diktedeki "... teşekkür ederim" korunur. VAD asıl
        // korumadır, bu liste VAD'in kaçırdığı durumlar için ek güvenliktir.
        private static readonly HashSet<string> KnownHallucinations = new(System.StringComparer.Ordinal)
        {
            "altyazı m.k",
            "altyazı: m.k",
            "izlediğiniz için teşekkür ederim",
            "izlediğiniz için teşekkürler",
            "abone olmayı unutmayın",
        };

        private static readonly System.Globalization.CultureInfo TurkishCulture = new("tr-TR");

        public static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            // Zaman damgalarını temizle: [00:00:00.000 --> 00:00:02.000]
            var cleaned = TimestampRegex.Replace(raw, "");

            // [BLANK_AUDIO] gibi İngilizce etiketleri temizle
            cleaned = MetaRegex.Replace(cleaned, "");

            // Satır sonlarını ayır; tamamı tek köşeli-parantez ifadesi olan satırları
            // (konuşma-dışı etiketler) at, kalanları boşlukla birleştir.
            var lines = cleaned.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>();
            foreach (var line in lines)
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                if (LineBracketRegex.IsMatch(t)) continue;
                if (KnownHallucinations.Contains(t.TrimEnd('.', '!', ' ').ToLower(TurkishCulture))) continue;
                kept.Add(t);
            }

            var result = string.Join(" ", kept).Trim();

            // Çift boşlukları teke indir
            return WhitespaceRegex.Replace(result, " ");
        }
    }
}
