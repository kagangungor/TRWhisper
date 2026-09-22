using System;
using System.IO;
using System.Linq;
using TRWhisper.Core.Speech;
using Xunit;

namespace TRWhisper.Tests
{
    public class WhisperModelManagerTests
    {
        [Fact]
        public void Catalog_ContainsRequiredWhisperModels()
        {
            var catalog = WhisperModelManager.Catalog;
            Assert.NotNull(catalog);
            Assert.True(catalog.Count >= 6);

            Assert.Contains(catalog, m => m.Id == "large-v3-turbo-q5_0");
            Assert.Contains(catalog, m => m.Id == "small");
            Assert.Contains(catalog, m => m.Id == "base");
            Assert.Contains(catalog, m => m.Id == "tiny");
            Assert.Contains(catalog, m => m.Id == "medium");
            Assert.Contains(catalog, m => m.Id == "large-v3");
            Assert.Contains(catalog, m => m.Id == "silero-vad");
        }

        [Theory]
        [InlineData("large-v3-turbo-q5_0", "ggml-large-v3-turbo-q5_0.bin")]
        [InlineData("small", "ggml-small.bin")]
        [InlineData("silero-vad", "ggml-silero-v6.2.0.bin")]
        public void GetModelById_ReturnsCorrectModel(string id, string expectedFileName)
        {
            var model = WhisperModelManager.GetModelById(id);
            Assert.NotNull(model);
            Assert.Equal(expectedFileName, model.FileName);
            Assert.False(string.IsNullOrWhiteSpace(model.DownloadUrl));
        }

        [Fact]
        public void FormatBytes_FormatsReadableStrings()
        {
            Assert.Equal("500 B", WhisperModelManager.FormatBytes(500));
            Assert.Equal("1,5 KB", WhisperModelManager.FormatBytes(1536).Replace('.', ',')); // Handles culture variations
            Assert.Contains("MB", WhisperModelManager.FormatBytes(500 * 1024 * 1024));
            Assert.Contains("GB", WhisperModelManager.FormatBytes(2L * 1024 * 1024 * 1024));
        }
    }
}
