using System;
using System.IO;
using TRWhisper.Core.Config;
using TRWhisper.Core.Normalization;
using Xunit;

namespace TRWhisper.Tests
{
    public class SpokenPunctuationNormalizerTests : IDisposable
    {
        private readonly string _tempConfigFile;
        private readonly ConfigManager _configManager;
        private readonly SpokenPunctuationNormalizer _normalizer;

        public SpokenPunctuationNormalizerTests()
        {
            _tempConfigFile = Path.Combine(Path.GetTempPath(), $"trwhisper_test_cfg_{Guid.NewGuid():N}.json");
            _configManager = new ConfigManager(_tempConfigFile);
            _normalizer = new SpokenPunctuationNormalizer(_configManager);
        }

        public void Dispose()
        {
            try { File.Delete(_tempConfigFile); } catch { }
        }

        [Theory]
        [InlineData("merhaba nokta", "merhaba.")]
        [InlineData("merhaba virgül nasılsın", "merhaba, nasılsın")]
        [InlineData("nasılsın soru işareti", "nasılsın?")]
        [InlineData("harika ünlem", "harika!")]
        [InlineData("harika ünlem işareti", "harika!")]
        [InlineData("şunlar iki nokta elma", "şunlar: elma")]
        [InlineData("şunlar iki nokta üst üste elma", "şunlar: elma")]
        [InlineData("elma noktalı virgül armut", "elma; armut")]
        [InlineData("bekle üç nokta", "bekle...")]
        [InlineData("e tire posta", "e-posta")]
        [InlineData("e kısa çizgi posta", "e-posta")]
        [InlineData("ankara uzun çizgi istanbul", "ankara — istanbul")]
        public void Normalize_ConvertsBasicCommands(string input, string expected)
        {
            Assert.Equal(expected, _normalizer.Normalize(input));
        }

        [Theory]
        [InlineData("merhaba nokta nasılsın", "merhaba. Nasılsın")]
        [InlineData("tamam soru işareti iyiyim", "tamam? İyiyim")]
        [InlineData("tamam ünlem ılık bir gün", "tamam! Ilık bir gün")]
        [InlineData("dur üç nokta istanbul", "dur... İstanbul")]
        [InlineData("elma virgül ismail", "elma, ismail")]
        public void Normalize_CapitalizesAfterSentenceEndWithTurkishRules(string input, string expected)
        {
            Assert.Equal(expected, _normalizer.Normalize(input));
        }

        [Theory]
        [InlineData("merhaba nokta yeni satır nasılsın soru işareti", "merhaba.\nNasılsın?")]
        [InlineData("birinci satır başı ikinci", "birinci\nİkinci")]
        [InlineData("birinci alt satır ikinci", "birinci\nİkinci")]
        [InlineData("giriş yeni paragraf ikinci bölüm", "giriş\n\nİkinci bölüm")]
        public void Normalize_ConvertsLineBreaks(string input, string expected)
        {
            Assert.Equal(expected, _normalizer.Normalize(input));
        }

        [Theory]
        [InlineData("bir parantez aç örnek parantez kapa daha", "bir (örnek) daha")]
        [InlineData("bir parantez aç örnek parantezi kapat nokta", "bir (örnek).")]
        [InlineData("dedi ki tırnak aç merhaba tırnak kapa ve gitti", "dedi ki \"merhaba\" ve gitti")]
        [InlineData("tırnak işareti aç evet tırnak işareti kapat", "\"evet\"")]
        [InlineData("tamam nokta parantez aç ilk", "tamam. (İlk")]
        public void Normalize_HandlesParenthesesAndQuotesSpacing(string input, string expected)
        {
            Assert.Equal(expected, _normalizer.Normalize(input));
        }

        [Theory]
        [InlineData("ne soru işareti ünlem", "ne?!")]
        [InlineData("bitti nokta yeni paragraf başla", "bitti.\n\nBaşla")]
        [InlineData("peki virgül virgül", "peki,,")]
        public void Normalize_HandlesConsecutiveCommands(string input, string expected)
        {
            Assert.Equal(expected, _normalizer.Normalize(input));
        }

        [Theory]
        [InlineData("Merhaba dünya, nokta. Nasılsın", "Merhaba dünya. Nasılsın")]
        [InlineData("Merhaba NOKTA nasılsın", "Merhaba. Nasılsın")]
        [InlineData("İki Nokta", ":")]
        [InlineData("Merhaba dünya Nokta", "Merhaba dünya.")]
        public void Normalize_IsCaseInsensitiveAndAbsorbsWhisperPunctuation(string input, string expected)
        {
            Assert.Equal(expected, _normalizer.Normalize(input));
        }

        [Theory]
        [InlineData("noktalamak önemli")]
        [InlineData("virgülle ayır")]
        [InlineData("virgüllü sayı")]
        [InlineData("tireli kelime")]
        [InlineData("ünlemler ve noktalar")]
        [InlineData("satırlar yeni satırlar")]
        [InlineData("nokta'yı koy")]
        public void Normalize_DoesNotTouchWordsContainingCommands(string input)
        {
            Assert.Equal(input, _normalizer.Normalize(input));
        }

        [Theory]
        [InlineData("Merhaba, dünya. Nasılsın?")]
        [InlineData("Harika! Peki; sonra: ne olacak...")]
        [InlineData("Birinci satır.\nİkinci satır, devam.")]
        public void Normalize_WithoutCommands_PreservesWhisperPunctuation(string input)
        {
            Assert.Equal(input, _normalizer.Normalize(input));
        }

        [Fact]
        public void Normalize_OnlyAbsorbsPunctuationAdjacentToCommand()
        {
            Assert.Equal("Merhaba, dünya. Bugün.", _normalizer.Normalize("Merhaba, dünya. Bugün nokta"));
        }

        [Fact]
        public void Normalize_WhenDisabled_ReturnsTextUnchanged()
        {
            _configManager.Current.General.EnableSpokenPunctuation = false;
            const string input = "merhaba nokta yeni satır nasılsın soru işareti";

            Assert.Equal(input, _normalizer.Normalize(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Normalize_EmptyInput_ReturnsUnchanged(string input)
        {
            Assert.Equal(input, _normalizer.Normalize(input));
        }

        [Fact]
        public void Pipeline_NumberNormalizerLeavesCommandWordsIntact()
        {
            // Koordinatördeki sıra: önce sayı normalizasyonu, sonra sesli noktalama.
            var numbers = new TurkishTextNormalizer(_configManager);
            const string input = "şunlar iki nokta elma virgül armut üç nokta";

            Assert.Equal("şunlar: elma, armut...", _normalizer.Normalize(numbers.Normalize(input)));
        }
    }
}
