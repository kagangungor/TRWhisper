using System;
using System.IO;
using TRWhisper.Core.Config;
using TRWhisper.Core.Llm;
using Xunit;

namespace TRWhisper.Tests
{
    public class ApiUsageTrackerTests
    {
        private static string TempFile()
            => Path.Combine(Path.GetTempPath(), $"trwhisper_usage_{Guid.NewGuid():N}", ApiUsageTracker.FileName);

        [Fact]
        public void Evaluate_NoLimit_AlwaysAllowed()
        {
            Assert.Equal(QuotaDecision.Allowed, ApiUsageTracker.Evaluate(0, 0, QuotaActions.Block));
            Assert.Equal(QuotaDecision.Allowed, ApiUsageTracker.Evaluate(9999, 0, QuotaActions.Block));
        }

        [Fact]
        public void Evaluate_UnderWarningThreshold_IsAllowed()
        {
            Assert.Equal(QuotaDecision.Allowed, ApiUsageTracker.Evaluate(0, 100, QuotaActions.Block));
            Assert.Equal(QuotaDecision.Allowed, ApiUsageTracker.Evaluate(79, 100, QuotaActions.Block));
        }

        [Fact]
        public void Evaluate_AtEightyPercent_WarnsButAllows()
        {
            Assert.Equal(QuotaDecision.NearLimit, ApiUsageTracker.Evaluate(80, 100, QuotaActions.Block));
            Assert.Equal(QuotaDecision.NearLimit, ApiUsageTracker.Evaluate(99, 100, QuotaActions.Block));
        }

        [Fact]
        public void Evaluate_LimitReached_BlocksByDefault()
        {
            Assert.Equal(QuotaDecision.Blocked, ApiUsageTracker.Evaluate(100, 100, QuotaActions.Block));
            Assert.Equal(QuotaDecision.Blocked, ApiUsageTracker.Evaluate(250, 100, QuotaActions.Block));
        }

        [Fact]
        public void Evaluate_LimitReached_WarnOnlyLetsRequestThrough()
        {
            Assert.Equal(QuotaDecision.LimitReachedWarnOnly, ApiUsageTracker.Evaluate(100, 100, QuotaActions.WarnOnly));
        }

        [Fact]
        public void Evaluate_UnknownAction_FallsBackToBlocking()
        {
            // Elle düzenlenmiş config.json'daki yazım hatası sessizce "sınırsız"a dönüşmemeli.
            Assert.Equal(QuotaDecision.Blocked, ApiUsageTracker.Evaluate(100, 100, "sallabaşını"));
            Assert.Equal(QuotaDecision.Blocked, ApiUsageTracker.Evaluate(100, 100, null));
        }

        [Fact]
        public void Record_IncrementsAndPersistsTodayCount()
        {
            var file = TempFile();
            try
            {
                var tracker = new ApiUsageTracker(file);
                Assert.Equal(0, tracker.GetTodayCount());

                Assert.Equal(1, tracker.Record());
                Assert.Equal(2, tracker.Record());

                // Yeni örnek aynı dosyayı okur: Ayarlar penceresi ile dikte akışı aynı sayacı görür.
                Assert.Equal(2, new ApiUsageTracker(file).GetTodayCount());
            }
            finally
            {
                TryDeleteParent(file);
            }
        }

        [Fact]
        public void ResetToday_ClearsCounter()
        {
            var file = TempFile();
            try
            {
                var tracker = new ApiUsageTracker(file);
                tracker.Record();
                tracker.Record();

                tracker.ResetToday();

                Assert.Equal(0, tracker.GetTodayCount());
            }
            finally
            {
                TryDeleteParent(file);
            }
        }

        [Fact]
        public void GetTodayCount_StaleDate_StartsFromZero()
        {
            var file = TempFile();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "{\"Date\":\"2000-01-01\",\"Count\":500}");

                Assert.Equal(0, new ApiUsageTracker(file).GetTodayCount());
            }
            finally
            {
                TryDeleteParent(file);
            }
        }

        [Fact]
        public void GetTodayCount_CorruptFile_DoesNotThrow()
        {
            var file = TempFile();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "{ bu json değil");

                Assert.Equal(0, new ApiUsageTracker(file).GetTodayCount());
            }
            finally
            {
                TryDeleteParent(file);
            }
        }

        [Fact]
        public void EvaluateNext_UsesConfiguredLimitAndAction()
        {
            var file = TempFile();
            try
            {
                var tracker = new ApiUsageTracker(file);
                var config = new LlmCleaningConfig { DailyRequestLimit = 2, QuotaExceededAction = QuotaActions.Block };

                Assert.Equal(QuotaDecision.Allowed, tracker.EvaluateNext(config));   // 0/2
                tracker.Record();
                tracker.Record();

                Assert.Equal(QuotaDecision.Blocked, tracker.EvaluateNext(config));

                config.QuotaExceededAction = QuotaActions.WarnOnly;
                Assert.Equal(QuotaDecision.LimitReachedWarnOnly, tracker.EvaluateNext(config));
            }
            finally
            {
                TryDeleteParent(file);
            }
        }

        private static void TryDeleteParent(string file)
        {
            try { Directory.Delete(Path.GetDirectoryName(file)!, true); } catch { }
        }
    }
}
