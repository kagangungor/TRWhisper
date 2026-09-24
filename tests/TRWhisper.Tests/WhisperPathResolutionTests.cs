using System;
using System.IO;
using TRWhisper.Core.Config;
using Xunit;

namespace TRWhisper.Tests
{
    /// <summary>
    /// Göreli model/araç yollarının yalnızca güvenilir klasörlerde arandığını doğrular.
    /// Gerçek dizin ağacı geçici klasörde kurulur: "profil" ve "Program Files" taklit edilir.
    /// </summary>
    public class WhisperPathResolutionTests : IDisposable
    {
        private const string Relative = "tools\\whisper\\ggml-small.bin";

        private readonly string _root;
        private readonly string _profile;
        private readonly string _localData;

        public WhisperPathResolutionTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trwhisper_resolve_{Guid.NewGuid():N}");
            _profile = Path.Combine(_root, "Users", "kagan");
            _localData = Path.Combine(_profile, "AppData", "Local", "TRWhisper");
            Directory.CreateDirectory(_localData);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private static string Plant(string directory)
        {
            var file = Path.Combine(directory, Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "model");
            return file;
        }

        private string Resolve(string baseDirectory)
            => WhisperConfig.ResolvePath(Relative, baseDirectory, _localData, _profile);

        [Fact]
        public void ProgramFilesInstall_IgnoresModelPlantedAtDriveRoot()
        {
            // C:\Program Files\TRWhisper kurulumu; C:\tools\whisper\... herkesin yazabildiği yer.
            var app = Path.Combine(_root, "Program Files", "TRWhisper");
            Directory.CreateDirectory(app);
            var planted = Plant(_root);

            var resolved = Resolve(app);

            Assert.NotEqual(planted, resolved);
            Assert.Equal(Path.Combine(app, Relative), resolved);
        }

        [Fact]
        public void ProgramFilesInstall_FindsModelInUserDataDirectory()
        {
            var app = Path.Combine(_root, "Program Files", "TRWhisper");
            Directory.CreateDirectory(app);
            var downloaded = Plant(_localData);

            Assert.Equal(downloaded, Resolve(app));
        }

        [Fact]
        public void AppDirectory_WinsOverEverythingElse()
        {
            var app = Path.Combine(_profile, "AppData", "Local", "Programs", "TRWhisper");
            var bundled = Plant(app);
            Plant(_localData);

            Assert.Equal(bundled, Resolve(app));
        }

        [Fact]
        public void DevelopmentBuild_InsideProfile_StillScansParentDirectories()
        {
            // <profil>\Desktop\Proje\bin\Release\net9.0-windows → Proje\tools\whisper\...
            var project = Path.Combine(_profile, "Desktop", "Proje");
            var bin = Path.Combine(project, "bin", "Release", "net9.0-windows");
            Directory.CreateDirectory(bin);
            var projectModel = Plant(project);

            Assert.Equal(projectModel, Resolve(bin));
        }

        [Fact]
        public void ParentScan_StopsAtProfileBoundary()
        {
            // Profilin hemen dışındaki (ör. C:\Users) bir klasöre bırakılan dosya bulunmaz.
            var bin = Path.Combine(_profile, "Desktop", "Proje", "bin");
            Directory.CreateDirectory(bin);
            var outside = Plant(Path.GetDirectoryName(_profile)!);

            Assert.NotEqual(outside, Resolve(bin));
        }

        [Fact]
        public void CurrentDirectory_IsNotSearched()
        {
            var app = Path.Combine(_root, "Program Files", "TRWhisper");
            Directory.CreateDirectory(app);
            var cwd = Path.Combine(_root, "Saldirgan");
            var planted = Plant(cwd);

            var previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(cwd);
                Assert.NotEqual(planted, Resolve(app));
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }
    }
}
