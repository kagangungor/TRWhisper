using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TRWhisper.Core.Config;
using TRWhisper.Core.History;
using Xunit;

namespace TRWhisper.Tests
{
    public class HistoryServiceTests : IDisposable
    {
        private readonly string _root;
        private readonly string _logDir;
        private readonly ConfigManager _configManager;
        private readonly HistoryService _service;

        public HistoryServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trwhisper_history_{Guid.NewGuid():N}");
            _logDir = Path.Combine(_root, "Dictation");
            Directory.CreateDirectory(_root);
            _configManager = new ConfigManager(Path.Combine(_root, "config.json"));
            _configManager.Current.General.LogDirectory = _logDir;
            _service = new HistoryService(_configManager);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private const string FileHeader =
            "# TRWhisper Dikte Günlüğü - Eylül 2026\n\n> Bu dosya TRWhisper tarafından yerel olarak tutulmaktadır.\n\n---\n\n";

        private static string Entry(string timestamp, string text, string? raw = null)
        {
            var sb = new StringBuilder();
            sb.Append($"### 🕒 {timestamp}{(raw != null ? " [LLM Temizlendi]" : "")}\n\n{text}\n\n");
            if (raw != null)
                sb.Append($"<details>\n<summary>Ham Transkript</summary>\n\n> {raw}\n</details>\n\n");
            sb.Append("---\n\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ ayrıştırma

        [Fact]
        public void Parse_SingleEntry()
        {
            var entries = HistoryService.Parse(FileHeader + Entry("2026-09-24 14:15:03", "Merhaba dünya."));

            var e = Assert.Single(entries);
            Assert.Equal(new DateTime(2026, 9, 24, 14, 15, 3), e.Timestamp);
            Assert.Equal("Merhaba dünya.", e.Text);
            Assert.False(e.CleanedWithLlm);
            Assert.Null(e.RawText);
            Assert.Equal(2, e.WordCount);
        }

        [Fact]
        public void Parse_MultipleEntries_WithAndWithoutRaw()
        {
            var md = FileHeader
                     + Entry("2026-09-24 09:00:00", "Birinci kayıt.")
                     + Entry("2026-09-24 10:00:00", "İkinci kayıt temizlendi.", raw: "ikinci kayıt ııı temizlendi")
                     + Entry("2026-09-24 11:00:00", "Üçüncü kayıt.");

            var entries = HistoryService.Parse(md);

            Assert.Equal(3, entries.Count);
            Assert.True(entries[1].CleanedWithLlm);
            Assert.Equal("İkinci kayıt temizlendi.", entries[1].Text);
            Assert.Equal("ikinci kayıt ııı temizlendi", entries[1].RawText);
            Assert.Null(entries[2].RawText);
        }

        [Fact]
        public void Parse_MultiLineTextAndRaw_KeepsInnerSeparatorsAndLines()
        {
            var md = Entry("2026-09-24 12:00:00", "Birinci paragraf.\n\n---\n\nİkinci paragraf.", raw: "ham satır bir\nham satır iki")
                .Replace("\n", "\r\n");

            var e = Assert.Single(HistoryService.Parse(md));

            Assert.Equal("Birinci paragraf.\n\n---\n\nİkinci paragraf.", e.Text);
            Assert.Equal("ham satır bir\nham satır iki", e.RawText);
        }

        [Fact]
        public void Parse_LlmEntryWithoutRawBlock_IsCleanedWithNullRaw()
        {
            var e = Assert.Single(HistoryService.Parse("### 🕒 2026-09-24 12:00:00 [LLM Temizlendi]\n\nAynı metin.\n\n---\n"));

            Assert.True(e.CleanedWithLlm);
            Assert.Null(e.RawText);
        }

        [Fact]
        public void Parse_IgnoresMalformedHeadersAndEmptyBodies()
        {
            var md = "### 🕒 bozuk-tarih\n\nmetin\n\n---\n\n### 🕒 2026-09-24 12:00:00\n\n---\n\n"
                     + Entry("2026-09-24 13:00:00", "Geçerli.");

            var e = Assert.Single(HistoryService.Parse(md));
            Assert.Equal("Geçerli.", e.Text);
        }

        [Fact]
        public async Task LoadHistoryAsync_RoundTripsMarkdownLoggerOutput()
        {
            var logger = new MarkdownLogger(_configManager);
            await logger.LogTranscriptAsync("Düz dikte metni.", cleanedWithLlm: false);
            await logger.LogTranscriptAsync("Temizlenmiş metin.", cleanedWithLlm: true, rawTranscript: "temizlenmemiş ııı metin");

            var entries = await _service.LoadHistoryAsync();

            Assert.Equal(2, entries.Count);
            var cleaned = entries.Single(e => e.CleanedWithLlm);
            Assert.Equal("Temizlenmiş metin.", cleaned.Text);
            Assert.Equal("temizlenmemiş ııı metin", cleaned.RawText);
            Assert.Equal("Düz dikte metni.", entries.Single(e => !e.CleanedWithLlm).Text);
        }

        [Fact]
        public async Task LoadHistoryAsync_ReadsAllMonthFiles_NewestFirst()
        {
            Directory.CreateDirectory(_logDir);
            File.WriteAllText(Path.Combine(_logDir, "2026-08.md"), FileHeader + Entry("2026-08-10 10:00:00", "Ağustos."));
            File.WriteAllText(Path.Combine(_logDir, "2026-09.md"),
                FileHeader + Entry("2026-09-01 10:00:00", "Eylül bir.") + Entry("2026-09-20 10:00:00", "Eylül yirmi."));
            File.WriteAllText(Path.Combine(_logDir, "trwhisper.log"), "### 🕒 2026-09-30 10:00:00\n\nlog satırı\n");

            var entries = await _service.LoadHistoryAsync();

            Assert.Equal(new[] { "Eylül yirmi.", "Eylül bir.", "Ağustos." }, entries.Select(e => e.Text));
        }

        [Fact]
        public async Task LoadHistoryAsync_MissingDirectory_ReturnsEmpty()
        {
            Assert.False(Directory.Exists(_logDir));

            Assert.Empty(await _service.LoadHistoryAsync());
        }

        [Fact]
        public async Task LoadHistoryAsync_EmptyOrForeignMarkdown_ReturnsEmpty()
        {
            Directory.CreateDirectory(_logDir);
            File.WriteAllText(Path.Combine(_logDir, "2026-09.md"), "");
            File.WriteAllText(Path.Combine(_logDir, "notlar.md"), "# Notlarım\n\nbaşka bir şey\n");

            Assert.Empty(await _service.LoadHistoryAsync());
        }

        // ------------------------------------------------------------------ istatistik

        [Fact]
        public void CalculateStats_SumsWordsAndEstimatesTimeSaved()
        {
            var entries = new[]
            {
                new HistoryEntry(DateTime.Now, "x", false, WordCount: 400),
                new HistoryEntry(DateTime.Now, "y", false, WordCount: 200),
            };

            var stats = _service.CalculateStats(entries);

            Assert.Equal(2, stats.TotalTranscripts);
            Assert.Equal(600, stats.TotalWords);
            // 600 kelime: yazma 15 dk, konuşma 4 dk → 11 dk kazanç.
            Assert.Equal(11.0, stats.EstimatedMinutesSaved, 6);
        }

        [Fact]
        public void CalculateStats_Empty_IsZero()
        {
            var stats = _service.CalculateStats(Array.Empty<HistoryEntry>());

            Assert.Equal(new HistoryStats(0, 0, 0), stats);
        }

        [Theory]
        [InlineData(0.4, "< 1 dakika")]
        [InlineData(35.2, "~35 dakika")]
        [InlineData(252, "~4,2 saat")]
        public void FormatDuration_UsesMinutesThenHours(double minutes, string expected)
            => Assert.Equal(expected, HistoryService.FormatDuration(minutes));

        // ------------------------------------------------------------------ filtre

        // 2026-09-24 bir Perşembe; hafta Pazartesi 2026-09-21'de başlar.
        private static readonly DateTime Now = new(2026, 9, 24, 15, 0, 0);

        private static readonly List<HistoryEntry> Sample = new()
        {
            new(new DateTime(2026, 9, 24, 9, 0, 0), "Bugünkü İstanbul toplantısı", true, "bugünkü istanbul toplantısı ııı"),
            new(new DateTime(2026, 9, 21, 8, 0, 0), "Pazartesi raporu", false),
            new(new DateTime(2026, 9, 20, 23, 59, 0), "Pazar notu", false),
            new(new DateTime(2026, 9, 1, 10, 0, 0), "Ay başı planı", false),
            new(new DateTime(2026, 8, 31, 10, 0, 0), "Geçen ay", false),
        };

        [Theory]
        [InlineData(HistoryService.FilterAll, 5)]
        [InlineData(null, 5)]
        [InlineData(HistoryService.FilterToday, 1)]
        [InlineData(HistoryService.FilterThisWeek, 2)]
        [InlineData(HistoryService.FilterThisMonth, 4)]
        public void Filter_ByDateRange(string? dateFilter, int expected)
            => Assert.Equal(expected, _service.Filter(Sample, null, dateFilter, Now).Count());

        [Theory]
        [InlineData("istanbul")]   // Türkçe: i ↔ İ
        [InlineData("İSTANBUL")]
        [InlineData("ııı")]        // yalnızca ham metinde geçiyor
        public void Filter_SearchIsCaseInsensitiveTurkishAndIncludesRawText(string keyword)
        {
            var match = Assert.Single(_service.Filter(Sample, keyword, HistoryService.FilterAll, Now));
            Assert.StartsWith("Bugünkü", match.Text);
        }

        [Fact]
        public void Filter_CombinesSearchAndDate()
        {
            Assert.Empty(_service.Filter(Sample, "geçen ay", HistoryService.FilterThisMonth, Now));
            Assert.Single(_service.Filter(Sample, "geçen ay", HistoryService.FilterAll, Now));
        }
    }
}
