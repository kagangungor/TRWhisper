using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TRWhisper.Core.Backup;
using TRWhisper.Core.Config;
using Xunit;

namespace TRWhisper.Tests
{
    public class BackupServiceTests : IDisposable
    {
        private readonly string _root;
        private readonly string _logDir;
        private readonly string _backupDir;
        private readonly ConfigManager _configManager;
        private readonly BackupService _service;

        public BackupServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trwhisper_backup_{Guid.NewGuid():N}");
            _logDir = Path.Combine(_root, "Dictation");
            _backupDir = Path.Combine(_root, "Yedekler");
            Directory.CreateDirectory(_logDir);

            _configManager = new ConfigManager(Path.Combine(_root, "config.json"));

            var cfg = _configManager.Current;
            cfg.General.LogDirectory = _logDir;
            cfg.Backup.BackupDirectory = _backupDir;
            _configManager.Save(cfg);

            _service = new BackupService(_configManager);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private void WriteDictionary(string content)
            => File.WriteAllText(_service.DictionaryPath, content, Encoding.UTF8);

        private string TranscriptPath => Path.Combine(_logDir, "2026-09.md");

        // ------------------------------------------------------------------ zamanlama

        [Fact]
        public void IsAutomaticBackupDue_Disabled_IsNeverDue()
        {
            var backup = new BackupConfig { EnableAutomaticBackup = false, LastBackupUtc = null };
            Assert.False(BackupService.IsAutomaticBackupDue(backup, DateTime.UtcNow));
        }

        [Fact]
        public void IsAutomaticBackupDue_NeverBackedUp_IsDue()
        {
            var backup = new BackupConfig { EnableAutomaticBackup = true, LastBackupUtc = null };
            Assert.True(BackupService.IsAutomaticBackupDue(backup, DateTime.UtcNow));
        }

        [Fact]
        public void IsAutomaticBackupDue_RespectsInterval()
        {
            var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
            var backup = new BackupConfig
            {
                EnableAutomaticBackup = true,
                AutomaticBackupIntervalDays = 3,
                LastBackupUtc = now.AddDays(-2)
            };

            Assert.False(BackupService.IsAutomaticBackupDue(backup, now));

            backup.LastBackupUtc = now.AddDays(-3);
            Assert.True(BackupService.IsAutomaticBackupDue(backup, now));
        }

        // ------------------------------------------------------------------ zip-slip

        [Theory]
        [InlineData("..\\..\\evil.md")]
        [InlineData("../../evil.md")]
        [InlineData("alt/klasor/2026-09.md")]
        [InlineData("..")]
        [InlineData("")]
        [InlineData("calistir.exe")]
        public void SafeTranscriptName_RejectsUnsafeEntries(string entry)
            => Assert.Null(BackupService.SafeTranscriptName(entry));

        [Fact]
        public void SafeTranscriptName_AcceptsPlainMarkdownName()
            => Assert.Equal("2026-09.md", BackupService.SafeTranscriptName("2026-09.md"));

        // ------------------------------------------------------------------ yedek içeriği

        [Fact]
        public void CreateBackup_ProducesZipWithConfigDictionaryAndTranscripts()
        {
            WriteDictionary("{\"rules\":[]}");
            File.WriteAllText(TranscriptPath, "# eylül", Encoding.UTF8);

            var entry = _service.CreateBackup();

            Assert.True(File.Exists(entry.FilePath));
            using var archive = ZipFile.OpenRead(entry.FilePath);
            var names = archive.Entries.Select(e => e.FullName).ToList();

            Assert.Contains("config.json", names);
            Assert.Contains("dictionary.json", names);
            Assert.Contains("transkriptler/2026-09.md", names);
            Assert.Contains("YEDEK-BILGI.TXT", names.Select(n => n.ToUpperInvariant()));
        }

        [Fact]
        public void CreateBackup_NeverWritesApiKeyIntoArchive()
        {
            var cfg = _configManager.Current;
            cfg.LlmCleaning.Provider = "Gemini";
            cfg.LlmCleaning.ApiKey = "AIzaTESTANAHTARI1234567890";
            _configManager.Save(cfg);

            var entry = _service.CreateBackup();

            using var archive = ZipFile.OpenRead(entry.FilePath);
            using var reader = new StreamReader(archive.GetEntry("config.json")!.Open(), Encoding.UTF8);
            var json = reader.ReadToEnd();

            Assert.DoesNotContain("AIzaTESTANAHTARI1234567890", json);
            Assert.Contains("\"ApiKey\": \"\"", json);
        }

        [Fact]
        public void CreateBackup_WithoutTranscripts_OnlyPacksSettings()
        {
            var cfg = _configManager.Current;
            cfg.Backup.IncludeTranscripts = false;
            _configManager.Save(cfg);

            File.WriteAllText(TranscriptPath, "# eylül", Encoding.UTF8);

            var entry = _service.CreateBackup();

            using var archive = ZipFile.OpenRead(entry.FilePath);
            Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("transkriptler/", StringComparison.Ordinal));
        }

        [Fact]
        public void CreateBackup_UpdatesLastBackupTimestamp()
        {
            Assert.Null(_configManager.Current.Backup.LastBackupUtc);

            _service.CreateBackup();

            Assert.NotNull(_configManager.Current.Backup.LastBackupUtc);
        }

        [Fact]
        public void PruneOldBackups_KeepsOnlyNewest()
        {
            Directory.CreateDirectory(_backupDir);
            for (int i = 0; i < 5; i++)
            {
                var path = Path.Combine(_backupDir, $"{BackupService.FilePrefix}2026090{i}-120000{BackupService.FileExtension}");
                File.WriteAllText(path, "x");
                File.SetLastWriteTimeUtc(path, new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(i));
            }

            var deleted = _service.PruneOldBackups(2);

            Assert.Equal(3, deleted);
            Assert.Equal(2, _service.ListBackups().Count);
        }

        [Fact]
        public void PruneOldBackups_ZeroKeepsEverything()
        {
            Directory.CreateDirectory(_backupDir);
            File.WriteAllText(Path.Combine(_backupDir, $"{BackupService.FilePrefix}20260901-120000{BackupService.FileExtension}"), "x");

            Assert.Equal(0, _service.PruneOldBackups(0));
            Assert.Single(_service.ListBackups());
        }

        // ------------------------------------------------------------------ geri yükleme

        [Fact]
        public void Restore_BringsBackDictionaryTranscriptsAndSettings()
        {
            WriteDictionary("ESKI-SOZLUK");
            File.WriteAllText(TranscriptPath, "ESKI-TRANSKRIPT", Encoding.UTF8);

            var cfg = _configManager.Current;
            cfg.General.Language = "tr";
            _configManager.Save(cfg);

            var backup = _service.CreateBackup();

            // Yedekten sonra her şey değişir.
            WriteDictionary("YENI-SOZLUK");
            File.WriteAllText(TranscriptPath, "YENI-TRANSKRIPT", Encoding.UTF8);
            cfg = _configManager.Current;
            cfg.General.Language = "de";
            _configManager.Save(cfg);

            var result = _service.Restore(backup.FilePath);

            Assert.True(result.ConfigRestored);
            Assert.True(result.DictionaryRestored);
            Assert.Equal(1, result.TranscriptsRestored);
            Assert.Equal("ESKI-SOZLUK", File.ReadAllText(_service.DictionaryPath, Encoding.UTF8));
            Assert.Equal("ESKI-TRANSKRIPT", File.ReadAllText(TranscriptPath, Encoding.UTF8));
            Assert.Equal("tr", _configManager.Current.General.Language);
        }

        [Fact]
        public void Restore_TakesSafetyBackupBeforeOverwriting()
        {
            WriteDictionary("BIRINCI");
            var backup = _service.CreateBackup();

            WriteDictionary("IKINCI");
            var result = _service.Restore(backup.FilePath);

            Assert.True(File.Exists(result.SafetyBackupPath));
            using var archive = ZipFile.OpenRead(result.SafetyBackupPath);
            using var reader = new StreamReader(archive.GetEntry("dictionary.json")!.Open(), Encoding.UTF8);
            Assert.Equal("IKINCI", reader.ReadToEnd());
        }

        [Fact]
        public void Restore_KeepsCurrentApiKey()
        {
            var cfg = _configManager.Current;
            cfg.LlmCleaning.ApiKey = "";
            _configManager.Save(cfg);

            var backup = _service.CreateBackup();

            cfg = _configManager.Current;
            cfg.LlmCleaning.ApiKey = "sk-guncel-anahtar-123456";
            _configManager.Save(cfg);

            _service.Restore(backup.FilePath);

            Assert.Equal("sk-guncel-anahtar-123456", _configManager.Current.LlmCleaning.ApiKey);
        }

        [Fact]
        public void Restore_LowRetention_DoesNotDeleteTheArchiveBeingRestored()
        {
            // Saklama sayısı 1 iken emniyet yedeği budama tetiklerse, geri yüklenecek
            // ZIP açılmadan silinirdi.
            var cfg = _configManager.Current;
            cfg.Backup.RetentionCount = 1;
            _configManager.Save(cfg);

            WriteDictionary("ILK");
            var backup = _service.CreateBackup();

            WriteDictionary("SONRAKI");
            var result = _service.Restore(backup.FilePath);

            Assert.True(result.DictionaryRestored);
            Assert.Equal("ILK", File.ReadAllText(_service.DictionaryPath, Encoding.UTF8));
        }

        [Fact]
        public void Restore_MissingFile_Throws()
            => Assert.Throws<FileNotFoundException>(() => _service.Restore(Path.Combine(_root, "yok.zip")));

        [Fact]
        public void Restore_TraversalEntryStaysInsideLogDirectory()
        {
            var crafted = Path.Combine(_root, "kotu.zip");
            using (var archive = ZipFile.Open(crafted, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("transkriptler/../../kacis.md");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write("kotu");
            }

            _service.Restore(crafted);

            // Girdi, adı ne olursa olsun günlük klasörünün dışına yazılamaz.
            Assert.False(File.Exists(Path.Combine(_root, "kacis.md")));
            Assert.All(Directory.GetFiles(_logDir), f => Assert.Equal(_logDir, Path.GetDirectoryName(f)));
        }

        [Fact]
        public void Restore_IgnoresNonMarkdownTranscriptEntries()
        {
            var crafted = Path.Combine(_root, "exe.zip");
            using (var archive = ZipFile.Open(crafted, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("transkriptler/calistir.exe");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write("MZ");
            }

            var result = _service.Restore(crafted);

            Assert.Equal(0, result.TranscriptsRestored);
            Assert.False(File.Exists(Path.Combine(_logDir, "calistir.exe")));
        }

        // ------------------------------------------------------------------ dışarıdan gelen yedek

        private string CraftBackup(string name, Action<ZipArchive> fill)
        {
            var path = Path.Combine(_root, name);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            fill(archive);
            return path;
        }

        private static void AddEntry(ZipArchive archive, string name, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
            writer.Write(content);
        }

        /// <summary>Sıfırlarla dolu, çok iyi sıkışan (ZIP'te birkaç KB tutan) büyük bir girdi.</summary>
        private static void AddZeros(ZipArchive archive, string name, long bytes)
        {
            using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
            var chunk = new byte[1024 * 1024];
            for (long written = 0; written < bytes; written += chunk.Length)
                stream.Write(chunk, 0, (int)Math.Min(chunk.Length, bytes - written));
        }

        [Fact]
        public void Restore_MaliciousConfig_CannotRedirectApiKeyOrPaths()
        {
            var cfg = _configManager.Current;
            cfg.LlmCleaning.Provider = "OpenAI";
            cfg.LlmCleaning.Endpoint = "https://api.openai.com/v1/chat/completions";
            cfg.LlmCleaning.Model = "gpt-4o-mini";
            cfg.LlmCleaning.ApiKey = "sk-gercek-anahtar-123456";
            cfg.LlmCleaning.EnabledByDefault = false;
            cfg.Whisper.ModelPath = "tools\\whisper\\ggml-small.bin";
            cfg.General.Language = "tr";
            _configManager.Save(cfg);

            // Saldırganın hazırladığı yedek: anahtarı ve dikteleri kendi sunucusuna yönlendirir,
            // modeli ağ paylaşımından yükletir.
            var evil = new AppConfig();
            evil.LlmCleaning.Provider = "CustomOpenAI";
            evil.LlmCleaning.Endpoint = "https://saldirgan.example/v1/chat/completions";
            evil.LlmCleaning.Model = "casus-model";
            evil.LlmCleaning.EnabledByDefault = true;
            evil.Whisper.ModelPath = "\\\\saldirgan.example\\pay\\model.bin";
            evil.Whisper.CliPath = "\\\\saldirgan.example\\pay\\whisper-cli.exe";
            evil.General.LogDirectory = "C:\\Windows\\Temp\\baska";
            evil.Backup.BackupDirectory = "\\\\saldirgan.example\\yedek";
            evil.General.Language = "en";

            var zip = CraftBackup("kotu-ayar.zip", a => AddEntry(a, "config.json",
                System.Text.Json.JsonSerializer.Serialize(evil)));

            var result = _service.Restore(zip);

            var now = _configManager.Current;
            Assert.True(result.ConfigRestored);
            Assert.Equal("en", now.General.Language); // sıradan ayarlar yine gelir
            Assert.Equal("OpenAI", now.LlmCleaning.Provider);
            Assert.Equal("https://api.openai.com/v1/chat/completions", now.LlmCleaning.Endpoint);
            Assert.Equal("gpt-4o-mini", now.LlmCleaning.Model);
            Assert.Equal("sk-gercek-anahtar-123456", now.LlmCleaning.ApiKey);
            Assert.False(now.LlmCleaning.EnabledByDefault);
            Assert.Equal("tools\\whisper\\ggml-small.bin", now.Whisper.ModelPath);
            Assert.DoesNotContain("saldirgan", now.Whisper.CliPath);
            Assert.Equal(_logDir, now.General.LogDirectory);
            Assert.Equal(_backupDir, now.Backup.BackupDirectory);
        }

        [Fact]
        public void Restore_OversizedDictionary_IsRejectedBeforeAnythingChanges()
        {
            WriteDictionary("MEVCUT");
            var zip = CraftBackup("bomba.zip", a => AddZeros(a, "dictionary.json", BackupService.MaxDictionaryBytes + 1));

            Assert.Throws<InvalidDataException>(() => _service.Restore(zip));

            Assert.Equal("MEVCUT", File.ReadAllText(_service.DictionaryPath, Encoding.UTF8));
            Assert.Empty(_service.ListBackups()); // emniyet yedeği bile alınmadı
        }

        [Fact]
        public void Restore_OversizedTranscript_IsRejected()
        {
            var zip = CraftBackup("bomba-md.zip", a => AddZeros(a, "transkriptler/2026-09.md", BackupService.MaxTranscriptBytes + 1));

            Assert.Throws<InvalidDataException>(() => _service.Restore(zip));
            Assert.False(File.Exists(TranscriptPath));
        }

        [Fact]
        public void Restore_TooManyTranscriptEntries_IsRejected()
        {
            var zip = CraftBackup("cok-girdi.zip", a =>
            {
                for (int i = 0; i <= BackupService.MaxTranscriptEntries; i++)
                    a.CreateEntry($"transkriptler/{i}.md");
            });

            Assert.Throws<InvalidDataException>(() => _service.Restore(zip));
            Assert.Empty(Directory.GetFiles(_logDir));
        }

        [Fact]
        public void CreateBackup_HistoryLoggingDisabled_LeavesTranscriptsOut()
        {
            File.WriteAllText(TranscriptPath, "GIZLI-DIKTE", Encoding.UTF8);
            var cfg = _configManager.Current;
            cfg.General.EnableHistoryLogging = false;
            _configManager.Save(cfg);

            var backup = _service.CreateBackup();

            using var archive = ZipFile.OpenRead(backup.FilePath);
            Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("transkriptler/", StringComparison.Ordinal));
        }

        [Fact]
        public void IsCloudSyncedPath_DetectsOneDriveFolders()
        {
            var original = Environment.GetEnvironmentVariable("OneDrive");
            var oneDrive = Path.Combine(_root, "OneDrive");
            try
            {
                Environment.SetEnvironmentVariable("OneDrive", oneDrive);

                Assert.True(BackupService.IsCloudSyncedPath(Path.Combine(oneDrive, "Yedekler")));
                Assert.True(BackupService.IsCloudSyncedPath("%OneDrive%\\Yedekler"));
                Assert.False(BackupService.IsCloudSyncedPath(_backupDir));
                Assert.False(BackupService.IsCloudSyncedPath(oneDrive + "Degil\\Yedekler")); // önek benzerliği yetmez
                Assert.False(BackupService.IsCloudSyncedPath(""));
            }
            finally
            {
                Environment.SetEnvironmentVariable("OneDrive", original);
            }
        }
    }
}
