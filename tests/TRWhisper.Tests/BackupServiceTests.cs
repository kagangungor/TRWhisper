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
    }
}
