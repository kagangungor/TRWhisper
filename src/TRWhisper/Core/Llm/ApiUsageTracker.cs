using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Llm
{
    /// <summary>Günlük tavan karşısında bir isteğin akıbeti.</summary>
    public enum QuotaDecision
    {
        /// <summary>Tavan yok ya da kullanım henüz uyarı eşiğinin altında.</summary>
        Allowed,

        /// <summary>Tavanın %80'i aşıldı; istek gönderilir ama kullanıcı uyarılır.</summary>
        NearLimit,

        /// <summary>Tavan doldu, ancak davranış "WarnOnly": istek yine gönderilir.</summary>
        LimitReachedWarnOnly,

        /// <summary>Tavan doldu ve davranış "Block": istek hiç gönderilmez.</summary>
        Blocked
    }

    /// <summary>
    /// Bulut sağlayıcıya gönderilen istekleri gün bazında sayar ve yapılandırmadaki günlük
    /// tavanla karşılaştırır. Amaç fatura sürprizini önlemek: sağlayıcı panelindeki bütçe
    /// uyarısı ancak para harcandıktan sonra haber verir, bu sayaç ise harcamadan önce durdurur.
    ///
    /// Sayım denemeler üzerinden yapılır (yanıt başarılı olmasa da), çünkü sağlayıcı hız
    /// sınırları da denemeyi sayar ve tavanın amacı "kaç kez dışarı çıkıldığı"nı sınırlamaktır.
    /// Yerel sağlayıcılar (Ollama vb.) hiç sayılmaz; ücret doğurmazlar.
    /// </summary>
    public sealed class ApiUsageTracker
    {
        public const string FileName = "api-usage.json";

        /// <summary>Bu orana ulaşıldığında kullanıcı tavana yaklaştığı konusunda uyarılır.</summary>
        private const double WarningRatio = 0.8;

        private sealed class UsageState
        {
            /// <summary>Yerel takvim günü (yyyy-MM-dd). Gün değişince sayaç sıfırlanır.</summary>
            public string Date { get; set; } = "";
            public int Count { get; set; }
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _filePath;
        private readonly object _lock = new();

        public ApiUsageTracker(string filePath)
        {
            _filePath = filePath;
        }

        public string FilePath => _filePath;

        /// <summary>Sayaç dosyası, config.json ile aynı (yazılabilir) klasörde tutulur.</summary>
        public static string ResolveDefaultPath(ConfigManager configManager)
        {
            var dir = Path.GetDirectoryName(configManager.ConfigFilePath);
            if (string.IsNullOrEmpty(dir)) dir = AppPaths.BaseDirectory;
            return Path.Combine(dir, FileName);
        }

        private static string TodayKey() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Bugün yapılan bulut isteği sayısı. Dosya okunamazsa 0 döner.</summary>
        public int GetTodayCount()
        {
            lock (_lock)
            {
                return ReadState().Count;
            }
        }

        /// <summary>Bir isteği kaydeder ve bugünkü yeni toplamı döndürür.</summary>
        public int Record()
        {
            lock (_lock)
            {
                var state = ReadState();
                state.Count++;
                WriteState(state);
                return state.Count;
            }
        }

        /// <summary>Bugünkü sayacı sıfırlar (Ayarlar penceresinden elle).</summary>
        public void ResetToday()
        {
            lock (_lock)
            {
                WriteState(new UsageState { Date = TodayKey(), Count = 0 });
            }
        }

        /// <summary>Yapılandırmadaki tavana göre bir sonraki isteğin akıbetini hesaplar.</summary>
        public QuotaDecision EvaluateNext(LlmCleaningConfig config)
            => Evaluate(GetTodayCount(), config.DailyRequestLimit, config.QuotaExceededAction);

        /// <summary>
        /// Saf karar fonksiyonu (dosya sistemine dokunmaz, test edilebilir).
        /// <paramref name="used"/> bugün ŞİMDİYE KADAR yapılmış istek sayısıdır.
        /// </summary>
        public static QuotaDecision Evaluate(int used, int limit, string? action)
        {
            if (limit <= 0) return QuotaDecision.Allowed;

            if (used >= limit)
            {
                return QuotaActions.Normalize(action) == QuotaActions.WarnOnly
                    ? QuotaDecision.LimitReachedWarnOnly
                    : QuotaDecision.Blocked;
            }

            return used >= (int)Math.Ceiling(limit * WarningRatio)
                ? QuotaDecision.NearLimit
                : QuotaDecision.Allowed;
        }

        private UsageState ReadState()
        {
            var today = TodayKey();
            try
            {
                if (File.Exists(_filePath))
                {
                    var state = JsonSerializer.Deserialize<UsageState>(File.ReadAllText(_filePath));
                    if (state != null && string.Equals(state.Date, today, StringComparison.Ordinal))
                        return new UsageState { Date = today, Count = Math.Max(0, state.Count) };
                }
            }
            catch (Exception ex)
            {
                // Sayaç okunamazsa dikte akışı durmamalı; bozuk dosya sıfırdan yazılır.
                FileLog.Write($"[ApiUsageTracker] Kullanım sayacı okunamadı, sıfırdan başlanıyor: {ex.Message}");
            }

            return new UsageState { Date = today, Count = 0 };
        }

        private void WriteState(UsageState state)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(state, JsonOptions));
            }
            catch (Exception ex)
            {
                FileLog.Write($"[ApiUsageTracker] Kullanım sayacı yazılamadı: {ex.Message}");
            }
        }
    }
}
