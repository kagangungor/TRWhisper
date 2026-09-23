using System;
using System.IO;
using TRWhisper.Core.Config;
using TRWhisper.Core.Llm;
using Xunit;

namespace TRWhisper.Tests
{
    /// <summary>
    /// API anahtarı yaşam döngüsü: rotasyon damgası yalnızca anahtar gerçekten
    /// değiştiğinde tazelenmeli, aksi hâlde "kaç gündür aynı" sorusu anlamsızlaşır.
    /// </summary>
    public class ApiKeyLifecycleTests : IDisposable
    {
        private readonly string _root;
        private readonly ConfigManager _configManager;

        public ApiKeyLifecycleTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trwhisper_key_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
            _configManager = new ConfigManager(Path.Combine(_root, "config.json"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void NewConfig_HasNoRotationStamp()
            => Assert.Null(_configManager.Current.LlmCleaning.ApiKeyUpdatedUtc);

        [Fact]
        public void Save_NewApiKey_StampsRotationDate()
        {
            var cfg = _configManager.Current;
            cfg.LlmCleaning.ApiKey = "sk-yeni-anahtar-123456";

            _configManager.Save(cfg);

            var stamp = cfg.LlmCleaning.ApiKeyUpdatedUtc;
            Assert.NotNull(stamp);
            Assert.True((DateTime.UtcNow - stamp!.Value).TotalMinutes < 5);
        }

        [Fact]
        public void Save_UnchangedApiKey_KeepsOriginalStamp()
        {
            var cfg = _configManager.Current;
            cfg.LlmCleaning.ApiKey = "sk-yeni-anahtar-123456";
            _configManager.Save(cfg);
            var first = cfg.LlmCleaning.ApiKeyUpdatedUtc;

            // Başka bir ayar değişti: anahtar yaşı sıfırlanmamalı.
            cfg.General.Language = "en";
            _configManager.Save(cfg);

            Assert.Equal(first, cfg.LlmCleaning.ApiKeyUpdatedUtc);
        }

        [Fact]
        public void Save_ClearedApiKey_ClearsStamp()
        {
            var cfg = _configManager.Current;
            cfg.LlmCleaning.ApiKey = "sk-yeni-anahtar-123456";
            _configManager.Save(cfg);

            cfg.LlmCleaning.ApiKey = "";
            _configManager.Save(cfg);

            Assert.Null(cfg.LlmCleaning.ApiKeyUpdatedUtc);
        }

        [Fact]
        public void Save_ReplacedApiKey_RefreshesStamp()
        {
            var cfg = _configManager.Current;
            cfg.LlmCleaning.ApiKey = "sk-eski-anahtar-123456";
            _configManager.Save(cfg);
            var first = cfg.LlmCleaning.ApiKeyUpdatedUtc!.Value;

            cfg.LlmCleaning.ApiKeyUpdatedUtc = first.AddDays(-120);
            cfg.LlmCleaning.ApiKey = "sk-yeni-anahtar-654321";
            _configManager.Save(cfg);

            Assert.True(cfg.LlmCleaning.ApiKeyUpdatedUtc > first.AddDays(-1));
        }

        [Theory]
        [InlineData("Ollama", true)]
        [InlineData("ollama", true)]
        [InlineData("LlamaCpp", true)]
        [InlineData("Local", true)]
        [InlineData("Gemini", false)]
        [InlineData("OpenAI", false)]
        [InlineData(null, true)]
        public void IsLocalProvider_ClassifiesProviders(string? provider, bool expected)
            => Assert.Equal(expected, LlmCleanerService.IsLocalProvider(provider));
    }
}
