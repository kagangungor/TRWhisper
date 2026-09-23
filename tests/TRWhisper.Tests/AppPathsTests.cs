using System;
using System.IO;
using TRWhisper.Core.Config;
using TRWhisper.Core.Speech;
using Xunit;

namespace TRWhisper.Tests
{
    public class AppPathsTests
    {
        [Fact]
        public void IsDirectoryWritable_TempDirectory_IsWritable()
        {
            Assert.True(AppPaths.IsDirectoryWritable(Path.GetTempPath()));
        }

        [Fact]
        public void IsDirectoryWritable_MissingDirectory_IsFalse()
        {
            var missing = Path.Combine(Path.GetTempPath(), $"trwhisper_yok_{Guid.NewGuid():N}");
            Assert.False(AppPaths.IsDirectoryWritable(missing));
        }

        [Fact]
        public void IsDirectoryWritable_ProbeFileIsNotLeftBehind()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"trwhisper_probe_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);

            try
            {
                Assert.True(AppPaths.IsDirectoryWritable(dir));
                Assert.Empty(Directory.GetFiles(dir));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void DataDirectories_AreUnderUserProfile()
        {
            Assert.EndsWith(Path.Combine("TRWhisper"), AppPaths.RoamingDataDirectory);
            Assert.EndsWith(Path.Combine("TRWhisper"), AppPaths.LocalDataDirectory);
            Assert.NotEqual(AppPaths.RoamingDataDirectory, AppPaths.LocalDataDirectory);
        }

        [Fact]
        public void ResolveDefaultConfigPath_WritableAppDirectory_StaysNextToExecutable()
        {
            // Test ana bilgisayarı yazılabilir bir çıktı klasöründen çalışır: davranış değişmemeli.
            var expected = Path.Combine(AppPaths.BaseDirectory, "config.json");
            Assert.Equal(expected, ConfigManager.ResolveDefaultConfigPath());
        }

        [Fact]
        public void GetWritableToolsDirectory_ReturnsExistingWritableDirectory()
        {
            var dir = WhisperModelManager.GetWritableToolsDirectory();

            Assert.False(string.IsNullOrWhiteSpace(dir));
            Assert.True(Directory.Exists(dir));
            Assert.EndsWith(Path.Combine("tools", "whisper"), dir);
        }
    }
}
