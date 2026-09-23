using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;
using TRWhisper.Core.Dictionary;

namespace TRWhisper.Core.Backup
{
    /// <summary>Yedek klasöründeki tek bir ZIP dosyası.</summary>
    public sealed record BackupEntry(string FilePath, DateTime CreatedUtc, long SizeBytes)
    {
        public string FileName => Path.GetFileName(FilePath);
    }

    /// <summary>Geri yükleme sonucu; hangi parçaların geldiğini kullanıcıya söylemek için.</summary>
    public sealed record RestoreResult(bool ConfigRestored, bool DictionaryRestored, int TranscriptsRestored, string SafetyBackupPath);

    /// <summary>
    /// Kullanıcının ürettiği tek verileri — dikte günlükleri (.md), özel sözlük ve ayarlar —
    /// tek bir ZIP'e alır ve geri yükler.
    ///
    /// API anahtarı yedeğe YAZILMAZ. İki gerekçe: (1) yedek dosyası e-postayla ya da buluta
    /// taşınabilen sıradan bir dosyadır, sır taşımamalıdır; (2) anahtar diskte DPAPI ile
    /// kullanıcı hesabına bağlı şifrelenir, başka makinede zaten çözülemezdi — taşımak
    /// yalnızca yanlış bir güven duygusu verirdi.
    /// </summary>
    public class BackupService
    {
        public const string FilePrefix = "TRWhisper-Yedek-";
        public const string FileExtension = ".zip";

        private const string ConfigEntry = "config.json";
        private const string DictionaryEntry = "dictionary.json";
        private const string TranscriptFolder = "transkriptler/";
        private const string ManifestEntry = "YEDEK-BILGI.txt";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly ConfigManager _configManager;
        private readonly object _lock = new();

        public BackupService(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        /// <summary>Yedeklerin yazıldığı klasör (yapılandırmadan, ortam değişkenleri çözülmüş).</summary>
        public string BackupDirectory
        {
            get
            {
                var dir = _configManager.Current.Backup.ResolvedBackupDirectory;
                return string.IsNullOrWhiteSpace(dir)
                    ? Path.Combine(_configManager.Current.General.ResolvedLogDirectory, "Yedekler")
                    : dir;
            }
        }

        /// <summary>dictionary.json her zaman config.json ile aynı klasörde durur.</summary>
        public string DictionaryPath
        {
            get
            {
                var dir = Path.GetDirectoryName(_configManager.ConfigFilePath);
                if (string.IsNullOrEmpty(dir)) dir = AppPaths.BaseDirectory;
                return Path.Combine(dir, CustomDictionaryService.FileName);
            }
        }

        // ------------------------------------------------------------------ yedek alma

        /// <summary>
        /// Yeni bir yedek ZIP'i üretir, eskileri saklama sayısına göre budar ve
        /// yapılandırmadaki son yedek damgasını günceller. Hata durumunda istisna fırlatır.
        /// </summary>
        public BackupEntry CreateBackup() => CreateBackup(prune: true);

        /// <param name="prune">
        /// Saklama sayısına göre eski yedekleri sil. Geri yükleme öncesi alınan emniyet
        /// yedeğinde KAPATILIR: saklama sayısı düşükse budama, kullanıcının geri yüklemek
        /// üzere seçtiği ZIP'i açılmadan silebilirdi.
        /// </param>
        private BackupEntry CreateBackup(bool prune)
        {
            lock (_lock)
            {
                var cfg = _configManager.Current;
                var dir = BackupDirectory;
                Directory.CreateDirectory(dir);

                var createdUtc = DateTime.UtcNow;
                var path = Path.Combine(dir, FilePrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + FileExtension);

                // Aynı saniyede ikinci yedek istenirse üzerine yazma.
                int suffix = 2;
                while (File.Exists(path))
                {
                    path = Path.Combine(dir, FilePrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + suffix++ + FileExtension);
                }

                var transcripts = CollectTranscripts(cfg);

                using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    WriteEntry(archive, ConfigEntry, BuildSanitizedConfigJson());

                    if (File.Exists(DictionaryPath))
                    {
                        WriteEntry(archive, DictionaryEntry, File.ReadAllText(DictionaryPath, Encoding.UTF8));
                    }

                    foreach (var file in transcripts)
                    {
                        archive.CreateEntryFromFile(file, TranscriptFolder + Path.GetFileName(file));
                    }

                    WriteEntry(archive, ManifestEntry, BuildManifest(createdUtc, transcripts.Count));
                }

                cfg.Backup.LastBackupUtc = createdUtc;
                _configManager.Save(cfg);

                if (prune) PruneOldBackups(cfg.Backup.RetentionCount);

                var info = new FileInfo(path);
                FileLog.Write($"[BackupService] Yedek alındı: {info.Name} ({info.Length / 1024} KB, {transcripts.Count} transkript dosyası).");
                return new BackupEntry(path, createdUtc, info.Length);
            }
        }

        /// <summary>
        /// Otomatik yedek zamanı geldiyse yedek alır. Açılışta çağrılır; kullanıcının
        /// önüne hiçbir pencere getirmez, başarısızlık yalnızca günlüğe yazılır.
        /// </summary>
        public BackupEntry? RunAutomaticBackupIfDue()
        {
            if (!IsAutomaticBackupDue(_configManager.Current.Backup, DateTime.UtcNow)) return null;

            try
            {
                return CreateBackup();
            }
            catch (Exception ex)
            {
                FileLog.Write($"[BackupService] Otomatik yedek alınamadı: {ex.Message}");
                return null;
            }
        }

        /// <summary>Saf zamanlama kararı (test edilebilir): otomatik yedeğin vakti geldi mi.</summary>
        public static bool IsAutomaticBackupDue(BackupConfig backup, DateTime nowUtc)
        {
            if (!backup.EnableAutomaticBackup) return false;
            if (backup.LastBackupUtc is not { } last) return true;   // hiç yedek alınmamış

            var intervalDays = Math.Max(1, backup.AutomaticBackupIntervalDays);
            return nowUtc - last >= TimeSpan.FromDays(intervalDays);
        }

        /// <summary>Yedek klasöründeki ZIP'ler, en yenisi başta.</summary>
        public IReadOnlyList<BackupEntry> ListBackups()
        {
            try
            {
                var dir = BackupDirectory;
                if (!Directory.Exists(dir)) return Array.Empty<BackupEntry>();

                return Directory.GetFiles(dir, FilePrefix + "*" + FileExtension)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => new BackupEntry(f.FullName, f.LastWriteTimeUtc, f.Length))
                    .ToList();
            }
            catch (Exception ex)
            {
                FileLog.Write($"[BackupService] Yedekler listelenemedi: {ex.Message}");
                return Array.Empty<BackupEntry>();
            }
        }

        /// <summary>En yeni <paramref name="keep"/> yedeği bırakır, kalanını siler. 0 = hepsini sakla.</summary>
        public int PruneOldBackups(int keep)
        {
            if (keep <= 0) return 0;

            var deleted = 0;
            foreach (var old in ListBackups().Skip(keep))
            {
                try
                {
                    File.Delete(old.FilePath);
                    deleted++;
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[BackupService] Eski yedek silinemedi ({old.FileName}): {ex.Message}");
                }
            }
            return deleted;
        }

        // ------------------------------------------------------------------ geri yükleme

        /// <summary>
        /// Verilen ZIP'ten ayarları, sözlüğü ve transkriptleri geri yükler. Üzerine yazmadan
        /// önce mevcut durumun yedeğini alır: yanlış dosya seçilmesi geri alınamaz olmasın.
        /// Mevcut API anahtarı korunur (yedekte zaten yoktur).
        /// </summary>
        public RestoreResult Restore(string zipPath)
        {
            lock (_lock)
            {
                if (!File.Exists(zipPath))
                    throw new FileNotFoundException("Yedek dosyası bulunamadı.", zipPath);

                var safety = CreateBackup(prune: false);

                using var archive = ZipFile.OpenRead(zipPath);

                bool configRestored = false;
                bool dictionaryRestored = false;
                var transcriptsRestored = 0;

                var configEntry = archive.GetEntry(ConfigEntry);
                if (configEntry != null)
                {
                    RestoreConfig(ReadEntry(configEntry));
                    configRestored = true;
                }

                var dictionaryEntry = archive.GetEntry(DictionaryEntry);
                if (dictionaryEntry != null)
                {
                    File.WriteAllText(DictionaryPath, ReadEntry(dictionaryEntry), Encoding.UTF8);
                    dictionaryRestored = true;
                }

                var logDir = _configManager.Current.General.ResolvedLogDirectory;
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith(TranscriptFolder, StringComparison.Ordinal)) continue;

                    var name = SafeTranscriptName(entry.Name);
                    if (name == null) continue;

                    Directory.CreateDirectory(logDir);
                    entry.ExtractToFile(Path.Combine(logDir, name), overwrite: true);
                    transcriptsRestored++;
                }

                FileLog.Write($"[BackupService] Geri yükleme tamamlandı: ayarlar={configRestored}, " +
                              $"sözlük={dictionaryRestored}, transkript={transcriptsRestored}.");

                return new RestoreResult(configRestored, dictionaryRestored, transcriptsRestored, safety.FilePath);
            }
        }

        /// <summary>
        /// Zip-slip koruması: arşivdeki ad yalnızca düz bir .md dosya adı olabilir. Yol ayracı,
        /// ".." ya da geçersiz karakter içeren girdiler sessizce atlanır.
        /// </summary>
        public static string? SafeTranscriptName(string? entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName)) return null;
            if (entryName.Contains('/') || entryName.Contains('\\')) return null;
            if (entryName is "." or "..") return null;
            if (entryName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            if (!entryName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;

            return entryName;
        }

        // ------------------------------------------------------------------ yardımcılar

        /// <summary>
        /// Yedeğe girecek config.json. Bellekteki yapılandırma seri hâle getirilir ve
        /// API anahtarı ile rotasyon damgası boşaltılır.
        /// </summary>
        internal string BuildSanitizedConfigJson()
        {
            var clone = JsonSerializer.Deserialize<AppConfig>(
                            JsonSerializer.Serialize(_configManager.Current, JsonOptions), JsonOptions)
                        ?? new AppConfig();

            clone.LlmCleaning.ApiKey = "";
            clone.LlmCleaning.ApiKeyUpdatedUtc = null;

            return JsonSerializer.Serialize(clone, JsonOptions);
        }

        /// <summary>Yedekteki ayarları uygular; mevcut API anahtarı ve damgası korunur.</summary>
        private void RestoreConfig(string json)
        {
            var restored = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
                           ?? throw new InvalidDataException("Yedekteki config.json okunamadı.");

            var current = _configManager.Current;
            restored.LlmCleaning.ApiKey = current.LlmCleaning.ApiKey;
            restored.LlmCleaning.ApiKeyUpdatedUtc = current.LlmCleaning.ApiKeyUpdatedUtc;

            _configManager.Save(restored);
        }

        private List<string> CollectTranscripts(AppConfig cfg)
        {
            if (!cfg.Backup.IncludeTranscripts) return new List<string>();

            try
            {
                var logDir = cfg.General.ResolvedLogDirectory;
                if (!Directory.Exists(logDir)) return new List<string>();

                // Yalnızca kök dizindeki .md dosyaları: yedek klasörünün kendisi de burada olabilir.
                return Directory.GetFiles(logDir, "*.md", SearchOption.TopDirectoryOnly).ToList();
            }
            catch (Exception ex)
            {
                FileLog.Write($"[BackupService] Transkriptler listelenemedi: {ex.Message}");
                return new List<string>();
            }
        }

        private static string BuildManifest(DateTime createdUtc, int transcriptCount)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TRWhisper yedeği");
            sb.AppendLine($"Oluşturulma (yerel) : {createdUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Oluşturulma (UTC)   : {createdUtc:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Bilgisayar          : {Environment.MachineName}");
            sb.AppendLine($"Transkript dosyası  : {transcriptCount.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine();
            sb.AppendLine("İçerik:");
            sb.AppendLine("  config.json        — ayarlar (API anahtarı ÇIKARILMIŞTIR)");
            sb.AppendLine("  dictionary.json    — özel sözlük kuralları");
            sb.AppendLine("  transkriptler/*.md — aylık dikte günlükleri");
            sb.AppendLine();
            sb.AppendLine("Geri yükleme: TRWhisper → Ayarlar → Yedekleme → Yedekten Geri Yükle.");
            sb.AppendLine("API anahtarı yedekte yoktur; geri yükleme sonrası sağlayıcı anahtarınızı yeniden girin.");
            return sb.ToString();
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        private static string ReadEntry(ZipArchiveEntry entry)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
