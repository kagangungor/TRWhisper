using System;
using System.IO;
using System.Linq;
using TRWhisper.Core;
using TRWhisper.Core.Config;
using TRWhisper.Core.Dictionary;
using TRWhisper.Core.Llm;
using TRWhisper.Core.Normalization;
using Xunit;

namespace TRWhisper.Tests
{
    public class PostLlmNormalizationTests : IDisposable
    {
        private readonly string _root;
        private readonly CustomDictionaryService _dictionary;
        private readonly TurkishTextNormalizer _normalizer;

        public PostLlmNormalizationTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trwhisper_postllm_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
            var configManager = new ConfigManager(Path.Combine(_root, "config.json"));
            _dictionary = new CustomDictionaryService(configManager);
            _normalizer = new TurkishTextNormalizer(configManager);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private static LlmMode Mode(string id) => LlmModeRegistry.GetDefaultModes().Single(m => m.Id == id);

        [Fact]
        public void AiActionMode_LeavesLlmAnswerUntouched()
        {
            const string answer = "Bütçenin yüzde yirmi kadarı ayrılmalı.";

            Assert.Equal(answer, DictationCoordinator.PostProcessLlmOutput(answer, Mode("AiAction"), _dictionary, _normalizer));
        }

        [Fact]
        public void CleanMode_StillNormalizesLlmOutput()
        {
            var result = DictationCoordinator.PostProcessLlmOutput(
                "Bütçenin yüzde yirmi kadarı ayrılmalı.", Mode("Clean"), _dictionary, _normalizer);

            Assert.Contains("%20", result);
        }
    }
}
