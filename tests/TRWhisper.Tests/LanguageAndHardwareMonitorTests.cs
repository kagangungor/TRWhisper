using System;
using System.IO;
using System.Text.Json;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;
using TRWhisper.Core.Speech;
using Xunit;

namespace TRWhisper.Tests
{
    /// <summary>
    /// Hızlı dil geçişi (TR ↔ EN), model bellek durumu ve canlı kaynak ölçümleri.
    /// </summary>
    public class LanguageAndHardwareMonitorTests
    {
        [Theory]
        [InlineData("tr", "en")]
        [InlineData("TR", "en")]
        [InlineData("en", "tr")]
        [InlineData("auto", "tr")]
        [InlineData("de", "tr")]
        [InlineData("", "tr")]
        [InlineData(null, "tr")]
        public void NextFastSwitch_TogglesTurkishAndEnglish(string? current, string expected)
            => Assert.Equal(expected, DictationLanguage.NextFastSwitch(current));

        [Theory]
        [InlineData("tr", "en")]
        [InlineData("en", "de")]
        [InlineData("de", "tr")]      // sondan başa döner
        [InlineData("fr", "tr")]      // listede yok → ilk dil
        [InlineData("auto", "tr")]
        public void NextFastSwitch_CyclesThroughSelectedLanguages(string current, string expected)
            => Assert.Equal(expected, DictationLanguage.NextFastSwitch(current, new[] { "tr", "en", "de" }));

        [Theory]
        [InlineData("de", "tr")]
        [InlineData("tr", "en")]
        [InlineData("en", "de")]      // kullanıcı sırası: DE → TR → EN → DE
        public void NextFastSwitch_FollowsUserOrder(string current, string expected)
            => Assert.Equal(expected, DictationLanguage.NextFastSwitch(current, new[] { "de", "tr", "en" }));

        [Fact]
        public void NextFastSwitch_AllSupportedLanguagesCycleInOrder()
        {
            var all = DictationLanguage.Supported.Select(l => l.Code).ToArray();
            var current = "tr";
            var visited = new System.Collections.Generic.List<string> { current };
            for (int i = 0; i < all.Length; i++)
            {
                current = DictationLanguage.NextFastSwitch(current, all);
                visited.Add(current);
            }
            Assert.Equal(new[] { "tr", "en", "de", "fr", "auto", "tr" }, visited);
        }

        [Fact]
        public void NormalizeFastSwitchLanguages_FiltersSortsAndFallsBack()
        {
            // Desteklenmeyen/tekrarlı kodlar atılır; kullanıcının sırası korunur, kod küçük harfe iner.
            Assert.Equal(new[] { "auto", "de", "tr" },
                DictationLanguage.NormalizeFastSwitchLanguages(new[] { "auto", "DE", "xx", "tr", "de" }));

            // İkiden az dil → varsayılan TR, EN.
            Assert.Equal(new[] { "tr", "en" }, DictationLanguage.NormalizeFastSwitchLanguages(new[] { "fr" }));
            Assert.Equal(new[] { "tr", "en" }, DictationLanguage.NormalizeFastSwitchLanguages(null));
        }

        [Fact]
        public void TryFastSwitch_UsesConfiguredLanguageList()
        {
            var general = new GeneralConfig
            {
                Language = "en",
                EnableLanguageFastSwitch = true,
                FastSwitchLanguages = new() { "tr", "en", "fr" },
            };

            Assert.True(DictationLanguage.TryFastSwitch(general));
            Assert.Equal("fr", general.Language);
            Assert.True(DictationLanguage.TryFastSwitch(general));
            Assert.Equal("tr", general.Language);
        }

        [Fact]
        public void FastSwitchLanguages_NullFromJsonFallsBackToDefault()
        {
            var general = System.Text.Json.JsonSerializer.Deserialize<GeneralConfig>("{\"FastSwitchLanguages\":null}")!;
            Assert.Equal(new[] { "tr", "en" }, general.FastSwitchLanguages);

            var custom = System.Text.Json.JsonSerializer.Deserialize<GeneralConfig>("{\"FastSwitchLanguages\":[\"de\",\"fr\"]}")!;
            Assert.Equal(new[] { "de", "fr" }, custom.FastSwitchLanguages);
        }

        [Fact]
        public void TryFastSwitch_WhenEnabled_SwitchesLanguage()
        {
            var general = new GeneralConfig { Language = "tr", EnableLanguageFastSwitch = true };

            Assert.True(DictationLanguage.TryFastSwitch(general));
            Assert.Equal("en", general.Language);

            Assert.True(DictationLanguage.TryFastSwitch(general));
            Assert.Equal("tr", general.Language);
        }

        [Fact]
        public void TryFastSwitch_WhenDisabled_LeavesLanguageUntouched()
        {
            var general = new GeneralConfig { Language = "tr", EnableLanguageFastSwitch = false };

            Assert.False(DictationLanguage.TryFastSwitch(general));
            Assert.Equal("tr", general.Language);
        }

        [Fact]
        public void GeneralConfig_FastSwitchDefaultsToOffWithAltL()
        {
            var general = new GeneralConfig();
            Assert.False(general.EnableLanguageFastSwitch);
            Assert.Equal("Alt+L", general.FastSwitchHotkey);
        }

        [Fact]
        public void ConfigDefaultJson_FastSwitchIsOff()
        {
            // Şablon çıktı klasörüne config.json adıyla kopyalanıyor; kaynağı doğrudan oku.
            // Konum bu kaynak dosyadan çözülür: derleme çıktısı depo dışında da olabilir.
            var template = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "..", "src", "TRWhisper", "config.default.json"));
            Assert.True(File.Exists(template), template);

            using var doc = JsonDocument.Parse(File.ReadAllText(template));
            var general = doc.RootElement.GetProperty("General");
            Assert.False(general.GetProperty("EnableLanguageFastSwitch").GetBoolean());
            Assert.Equal("Alt+L", general.GetProperty("FastSwitchHotkey").GetString());
            Assert.Equal(new[] { "tr", "en" },
                general.GetProperty("FastSwitchLanguages").EnumerateArray().Select(e => e.GetString()).ToArray());
        }

        [Theory]
        [InlineData("Alt+L", true)]
        [InlineData("Ctrl+Alt+L", true)]
        [InlineData("Ctrl+Shift+L", true)]
        [InlineData("Win+L", true)]
        [InlineData("F7", true)]
        [InlineData("L", false)]            // yazarken her L'de dil değişirdi
        [InlineData("Shift+L", false)]      // büyük L yazmak
        [InlineData("Alt", false)]          // tek değiştirici
        [InlineData("LeftAlt", false)]
        [InlineData("Shift", false)]
        [InlineData("Mouse4", false)]       // dil kısayolu yalnız klavye hook'unda
        [InlineData("None", false)]
        [InlineData("", false)]
        [InlineData("RightCtrl", false)]    // hem değiştirici hem bas-konuş
        [InlineData("Ctrl+Space", false)]   // bas-konuş ile çakışma
        public void IsValidFastSwitchHotkey(string hotkey, bool expected)
            => Assert.Equal(expected, DictationLanguage.IsValidFastSwitchHotkey(hotkey, hotkey == "Ctrl+Space" ? "Ctrl+Space" : "RightCtrl"));

        private static string ThisDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "")
            => Path.GetDirectoryName(path)!;

        [Theory]
        [InlineData("tr", "Türkçe (TR)")]
        [InlineData("en", "İngilizce (EN)")]
        [InlineData("de", "Almanca (DE)")]
        [InlineData("fr", "Fransızca (FR)")]
        [InlineData("auto", "Otomatik (Auto)")]
        public void DisplayName_ReturnsFriendlyLabel(string code, string expected)
            => Assert.Equal(expected, DictationLanguage.DisplayName(code));

        [Theory]
        [InlineData("en", "turn on the light", "turn on the light")]
        [InlineData("de", "on", "on")]
        [InlineData("tr", "yüzde yirmi", "%20")]
        [InlineData("auto", "yüzde yirmi", "%20")]
        public void TurkishNormalizer_RunsOnlyForTurkishOrAuto(string language, string input, string expected)
        {
            var root = Path.Combine(Path.GetTempPath(), $"trw_norm_{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                var cm = new ConfigManager(Path.Combine(root, "config.json"));
                cm.Current.General.Language = language;
                var normalizer = new TRWhisper.Core.Normalization.TurkishTextNormalizer(cm);

                Assert.Equal(expected, normalizer.Normalize(input));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Theory]
        [InlineData("auto", "en", "turn on the light", "turn on the light")]   // auto + İngilizce algılandı
        [InlineData("auto", "tr", "yüzde yirmi", "%20")]                      // auto + Türkçe algılandı
        [InlineData("en", "tr", "yüzde yirmi", "%20")]                        // algılanan dil ayarı ezer
        [InlineData("auto", null, "yüzde yirmi", "%20")]                      // motor bildirmedi → ayar
        public void TurkishNormalizer_DetectedLanguageOverridesSetting(string setting, string? detected, string input, string expected)
        {
            var root = Path.Combine(Path.GetTempPath(), $"trw_norm_{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                var cm = new ConfigManager(Path.Combine(root, "config.json"));
                cm.Current.General.Language = setting;
                var normalizer = new TRWhisper.Core.Normalization.TurkishTextNormalizer(cm);

                Assert.Equal(expected, normalizer.Normalize(input, detected));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void PostProcessLlmOutput_SkipsTurkishNormalizationForEnglishOutput()
        {
            var root = Path.Combine(Path.GetTempPath(), $"trw_norm_{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                var cm = new ConfigManager(Path.Combine(root, "config.json"));
                cm.Current.General.Language = "auto";
                var dictionary = new TRWhisper.Core.Dictionary.CustomDictionaryService(cm);
                var normalizer = new TRWhisper.Core.Normalization.TurkishTextNormalizer(cm);
                var cleanMode = TRWhisper.Core.Llm.LlmModeRegistry.GetDefaultModes()
                    .First(m => !TRWhisper.Core.Llm.LlmModeRegistry.IsActionMode(m.Id));

                Assert.Equal("Turn on the light.",
                    TRWhisper.Core.DictationCoordinator.PostProcessLlmOutput("Turn on the light.", cleanMode, dictionary, normalizer, "en"));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void WhisperNetEngine_FreshEngine_ReportsNoDetectedLanguage()
        {
            var root = Path.Combine(Path.GetTempPath(), $"trw_lang_{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                using var engine = new WhisperNetEngine(new ConfigManager(Path.Combine(root, "config.json")));
                Assert.Null(((ITranscriptionEngine)engine).LastDetectedLanguage);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void WhisperNetEngine_FreshEngine_ReportsNotLoadedAndUnloadIsSafe()
        {
            var root = Path.Combine(Path.GetTempPath(), $"trw_lang_{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                var cm = new ConfigManager(Path.Combine(root, "config.json"));
                using var engine = new WhisperNetEngine(cm);

                Assert.False(engine.IsModelLoaded);
                Assert.Null(engine.LoadedModelPath);
                Assert.Null(engine.LoadedLanguage);

                // Yüklü model yokken boşaltma hata vermez, durum değişmez.
                Assert.True(engine.UnloadModel());
                Assert.False(engine.IsModelLoaded);

                engine.Dispose();
                Assert.False(engine.UnloadModel());   // kapatılmış motor dokunulmaz
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void ResourceMonitor_WorkingSetIsPositive()
        {
            Assert.True(ResourceMonitor.ProcessWorkingSetBytes() > 0);
        }

        [Theory]
        [InlineData(0, "0 MB")]
        [InlineData(-5, "0 MB")]
        [InlineData(512L * 1024 * 1024, "512 MB")]
        public void ResourceMonitor_FormatBytes(long bytes, string expected)
            => Assert.Equal(expected, ResourceMonitor.FormatBytes(bytes));

        [Fact]
        public void ResourceMonitor_FormatBytes_UsesGigabytesAboveOneGb()
            => Assert.EndsWith(" GB", ResourceMonitor.FormatBytes(3L * 1024 * 1024 * 1024));

        [Fact]
        public void ResourceMonitor_AccelerationLabel()
        {
            Assert.Contains("CUDA 13", ResourceMonitor.AccelerationLabel(true));
            Assert.Contains("CPU", ResourceMonitor.AccelerationLabel(false));
        }
    }
}
