using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.History
{
    public record HistoryEntry(DateTime Timestamp, string Text, bool CleanedWithLlm, string? RawText = null, int WordCount = 0);

    public record HistoryStats(int TotalTranscripts, int TotalWords, double EstimatedMinutesSaved);

    /// <summary>
    /// <see cref="MarkdownLogger"/>'ın aylık .md günlüklerini okuyup Ayarlar'daki Geçmiş
    /// sekmesine kayıt, arama/filtre ve verimlilik özeti sağlar. Yalnızca okur; günlüğe yazmaz.
    /// </summary>
    public class HistoryService
    {
        public const string FilterAll = "Tümü";
        public const string FilterToday = "Bugün";
        public const string FilterThisWeek = "Bu Hafta";
        public const string FilterThisMonth = "Bu Ay";

        /// <summary>Ortalama klavye yazma ve konuşma hızları (kelime/dakika).</summary>
        public const double TypingWpm = 40, SpeakingWpm = 150;

        private const string HeaderPrefix = "### 🕒 ";
        private const string LlmLabel = "[LLM Temizlendi]";
        private static readonly CultureInfo Turkish = new("tr-TR");

        private readonly ConfigManager _configManager;

        public HistoryService(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        public string LogDirectory => _configManager.Current.General.ResolvedLogDirectory;

        /// <summary>Günlük klasöründeki tüm .md dosyalarını arka planda okur; yeniden eskiye sıralı döner.</summary>
        public Task<List<HistoryEntry>> LoadHistoryAsync(CancellationToken ct = default)
        {
            var dir = LogDirectory;
            return Task.Run(() =>
            {
                var entries = new List<HistoryEntry>();
                if (!Directory.Exists(dir)) return entries;

                foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        entries.AddRange(Parse(File.ReadAllText(file, Encoding.UTF8)));
                    }
                    catch (IOException ex)
                    {
                        // Dosya o an yazılıyor/kilitliyse diğerleri yine gösterilir.
                        FileLog.Write($"[HistoryService] Günlük okunamadı ({Path.GetFileName(file)}): {ex.Message}");
                    }
                }

                entries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
                return entries;
            }, ct);
        }

        /// <summary>
        /// Tek bir günlük dosyasının içeriğini ayrıştırır. Kayıt biçimi (MarkdownLogger):
        /// <c>### 🕒 yyyy-MM-dd HH:mm:ss [LLM Temizlendi]</c>, boş satır, metin, isteğe bağlı
        /// <c>&lt;details&gt;</c> içinde "> ham metin", ardından <c>---</c> ayırıcısı.
        /// </summary>
        public static List<HistoryEntry> Parse(string markdown)
        {
            var entries = new List<HistoryEntry>();
            var lines = markdown.Replace("\r\n", "\n").Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith(HeaderPrefix, StringComparison.Ordinal)) continue;

                var header = lines[i][HeaderPrefix.Length..].Trim();
                bool cleaned = header.EndsWith(LlmLabel, StringComparison.Ordinal);
                if (cleaned) header = header[..^LlmLabel.Length].Trim();
                if (!DateTime.TryParseExact(header, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                                            DateTimeStyles.None, out var timestamp))
                    continue;

                int end = i + 1;
                while (end < lines.Length && !lines[end].StartsWith(HeaderPrefix, StringComparison.Ordinal)) end++;
                var body = lines[(i + 1)..end].ToList();
                i = end - 1;

                // Kaydı kapatan "---" ayırıcısı metne ait değildir (metnin içindeki "---" korunur).
                TrimBlankEdges(body);
                if (body.Count > 0 && body[^1].Trim() == "---") body.RemoveAt(body.Count - 1);

                string? raw = null;
                int details = body.FindIndex(l => l.Trim() == "<details>");
                if (details >= 0)
                {
                    var rawLines = body.Skip(details + 1)
                        .TakeWhile(l => l.Trim() != "</details>")
                        .Where(l => !l.TrimStart().StartsWith("<summary>", StringComparison.Ordinal))
                        .ToList();
                    TrimBlankEdges(rawLines);
                    // Logger yalnızca ilk satırın başına "> " koyar.
                    if (rawLines.Count > 0 && rawLines[0].StartsWith('>')) rawLines[0] = rawLines[0][1..].TrimStart();
                    raw = rawLines.Count > 0 ? string.Join("\n", rawLines) : null;
                    body = body.Take(details).ToList();
                    TrimBlankEdges(body);
                }

                var text = string.Join("\n", body).Trim();
                if (text.Length == 0) continue;

                entries.Add(new HistoryEntry(timestamp, text, cleaned, raw, CountWords(text)));
            }

            return entries;
        }

        public HistoryStats CalculateStats(IEnumerable<HistoryEntry> entries)
        {
            int count = 0, words = 0;
            foreach (var e in entries)
            {
                count++;
                words += e.WordCount;
            }

            // Her kelime yazmak yerine söylendiğinde kazanılan süre: 1/40 - 1/150 dakika.
            double minutesSaved = words / TypingWpm - words / SpeakingWpm;
            return new HistoryStats(count, words, minutesSaved);
        }

        /// <summary>
        /// Metin (temiz veya ham) içinde büyük/küçük harf duyarsız arama (Türkçe i/İ kurallı)
        /// ve tarih aralığı filtresi. Hafta Pazartesi başlar.
        /// </summary>
        public IEnumerable<HistoryEntry> Filter(IEnumerable<HistoryEntry> entries, string? searchKeyword,
                                                string? dateFilter, DateTime? now = null)
        {
            var today = (now ?? DateTime.Now).Date;
            DateTime? from = dateFilter switch
            {
                FilterToday => today,
                FilterThisWeek => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
                FilterThisMonth => new DateTime(today.Year, today.Month, 1),
                _ => null,
            };

            var keyword = searchKeyword?.Trim();
            foreach (var e in entries)
            {
                if (from.HasValue && e.Timestamp < from.Value) continue;
                if (!string.IsNullOrEmpty(keyword) && !Contains(e.Text, keyword) &&
                    !(e.RawText != null && Contains(e.RawText, keyword)))
                    continue;
                yield return e;
            }
        }

        /// <summary>"~35 dakika", "~4,2 saat" gibi kısa gösterim.</summary>
        public static string FormatDuration(double minutes)
        {
            if (minutes < 1) return "< 1 dakika";
            if (minutes < 60) return $"~{Math.Round(minutes):0} dakika";
            return string.Format(Turkish, "~{0:0.#} saat", minutes / 60);
        }

        private static bool Contains(string text, string keyword)
            => Turkish.CompareInfo.IndexOf(text, keyword, CompareOptions.IgnoreCase) >= 0;

        private static int CountWords(string text)
            => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        private static void TrimBlankEdges(List<string> lines)
        {
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        }
    }
}
