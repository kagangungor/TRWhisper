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

        // Geri yükleme üst sınırları. Yedek dışarıdan gelmiş olabilir; aşırı sıkıştırılmış
        // bir ZIP (zip bombası) diski ya da belleği doldurmasın. Gerçek bir yedekte ayarlar
        // birkaç KB, sözlük birkaç yüz KB, aylık günlük birkaç MB'tır.
        public const long MaxConfigBytes = 1L * 1024 * 1024;
        public const long MaxDictionaryBytes = 5L * 1024 * 1024;
        public const long MaxTranscriptBytes = 64L * 1024 * 1024;
        public const long MaxTotalRestoreBytes = 1024L * 1024 * 1024;
        public const int MaxTranscriptEntries = 5000;

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
        /// Bu bilgisayara bağlı ayarlar (API anahtarı, yapay zeka bağlantısı, dosya yolları)
        /// yedekten alınmaz, mevcut hâliyle korunur; bkz. <see cref="KeepMachineBoundSettings"/>.
        /// Boyut sınırlarını aşan bir yedek hiçbir şeye dokunulmadan reddedilir.
        /// </summary>
        public RestoreResult Restore(string zipPath)
        {
            lock (_lock)
            {
                if (!File.Exists(zipPath))
                    throw new FileNotFoundException("Yedek dosyası bulunamadı.", zipPath);

                using var archive = ZipFile.OpenRead(zipPath);

                // Önce yalnızca okunur denetim: sınır aşılıyorsa emniyet yedeği bile alınmaz.
                ValidateArchiveSize(archive);

                var configEntry = archive.GetEntry(ConfigEntry);
                var dictionaryEntry = archive.GetEntry(DictionaryEntry);
                var configJson = configEntry != null ? ReadEntry(configEntry, MaxConfigBytes) : null;
                var dictionaryJson = dictionaryEntry != null ? ReadEntry(dictionaryEntry, MaxDictionaryBytes) : null;

                var safety = CreateBackup(prune: false);

                bool configRestored = false;
                bool dictionaryRestored = false;
                var transcriptsRestored = 0;

                if (configJson != null)
                {
                    RestoreConfig(configJson);
                    configRestored = true;
                }

                if (dictionaryJson != null)
                {
                    File.WriteAllText(DictionaryPath, dictionaryJson, Encoding.UTF8);
                    dictionaryRestored = true;
                }

                var logDir = _configManager.Current.General.ResolvedLogDirectory;
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith(TranscriptFolder, StringComparison.Ordinal)) continue;

                    var name = SafeTranscriptName(entry.Name);
                    if (name == null) continue;

                    Directory.CreateDirectory(logDir);
                    ExtractEntry(entry, Path.Combine(logDir, name), MaxTranscriptBytes);
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

        /// <summary>Yedekteki ayarları uygular; bu bilgisayara bağlı ayarlar korunur.</summary>
        private void RestoreConfig(string json)
        {
            var restored = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
                           ?? throw new InvalidDataException("Yedekteki config.json okunamadı.");

            KeepMachineBoundSettings(restored, _configManager.Current);
            _configManager.Save(restored);
        }

        /// <summary>
        /// Yedekten gelmemesi gereken ayarları <paramref name="current"/>'tan geri kopyalar.
        ///
        /// Yedek başkasından gelmiş olabilir. Mevcut API anahtarı korunurken sağlayıcı ve uç
        /// nokta yedekten alınsaydı, hazırlanmış bir yedek anahtarı ve her dikteyi saldırganın
        /// sunucusuna yönlendirebilirdi. Dosya yolları da yedekten alınırsa model bir ağ
        /// paylaşımından (\\sunucu\...) yüklenip Windows kimlik bilgisi sızdırılabilirdi.
        /// Ayrıca bu ayarlar zaten makineye özgüdür: başka bilgisayarın yolları burada geçersizdir.
        /// </summary>
        public static void KeepMachineBoundSettings(AppConfig restored, AppConfig current)
        {
            // Verinin nereye gittiği: bağlantı, anahtar ve "her dikteyi LLM'e gönder" anahtarı.
            restored.LlmCleaning.ApiKey = current.LlmCleaning.ApiKey;
            restored.LlmCleaning.ApiKeyUpdatedUtc = current.LlmCleaning.ApiKeyUpdatedUtc;
            restored.LlmCleaning.Provider = current.LlmCleaning.Provider;
            restored.LlmCleaning.Endpoint = current.LlmCleaning.Endpoint;
            restored.LlmCleaning.Model = current.LlmCleaning.Model;
            restored.LlmCleaning.EnabledByDefault = current.LlmCleaning.EnabledByDefault;

            // Dosyaların nereden okunup nereye yazıldığı.
            restored.Whisper.CliPath = current.Whisper.CliPath;
            restored.Whisper.ModelPath = current.Whisper.ModelPath;
            restored.Whisper.VadModelPath = current.Whisper.VadModelPath;
            restored.General.LogDirectory = current.General.LogDirectory;
            restored.General.TempAudioPath = current.General.TempAudioPath;
            restored.Backup.BackupDirectory = current.Backup.BackupDirectory;
            restored.Backup.LastBackupUtc = current.Backup.LastBackupUtc;
        }

        /// <summary>
        /// Yedek klasörü bulutla eşitlenen bir klasörde mi (OneDrive). Yedekler şifresiz
        /// dikte metni taşıdığı için Ayarlar penceresi bu durumda uyarır.
        /// </summary>
        public static bool IsCloudSyncedPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            string full;
            try { full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim())); }
            catch (Exception) { return false; }

            foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                var root = Environment.GetEnvironmentVariable(variable);
                if (string.IsNullOrWhiteSpace(root)) continue;

                root = Path.TrimEndingDirectorySeparator(root);
                if (full.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// ZIP başlıklarındaki boyutlara göre yedeği hiçbir şey yazmadan önce denetler. Başlık
        /// yalan söyleyebileceği için okuma/çıkarma sırasında da sınır ayrıca uygulanır.
        /// </summary>
        private static void ValidateArchiveSize(ZipArchive archive)
        {
            long total = 0;
            var transcripts = 0;

            foreach (var entry in archive.Entries)
            {
                long limit;
                if (entry.FullName == ConfigEntry) limit = MaxConfigBytes;
                else if (entry.FullName == DictionaryEntry) limit = MaxDictionaryBytes;
                else if (entry.FullName.StartsWith(TranscriptFolder, StringComparison.Ordinal) &&
                         SafeTranscriptName(entry.Name) != null)
                {
                    limit = MaxTranscriptBytes;
                    if (++transcripts > MaxTranscriptEntries)
                        throw new InvalidDataException($"Yedek çok fazla günlük dosyası içeriyor (en fazla {MaxTranscriptEntries}).");
                }
                else continue; // geri yüklenmeyen girdiler okunmaz bile

                if (entry.Length > limit)
                    throw new InvalidDataException($"Yedekteki \"{entry.FullName}\" boyut sınırını aşıyor ({limit / (1024 * 1024)} MB).");

                total += entry.Length;
                if (total > MaxTotalRestoreBytes)
                    throw new InvalidDataException($"Yedeğin açılmış boyutu sınırı aşıyor ({MaxTotalRestoreBytes / (1024 * 1024)} MB).");
            }
        }

        private List<string> CollectTranscripts(AppConfig cfg)
        {
            // Geçmiş kaydı kapatıldıysa kullanıcı dikte metinlerinin saklanmasını istemiyor;
            // diskte kalmış eski günlükler her yedekte yeniden çoğaltılmaz.
            if (!cfg.Backup.IncludeTranscripts || !cfg.General.EnableHistoryLogging) return new List<string>();

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

        private static string ReadEntry(ZipArchiveEntry entry, long maxBytes)
        {
            using var buffer = new MemoryStream();
            using (var source = entry.Open())
            {
                CopyLimited(source, buffer, maxBytes, entry.FullName);
            }
            buffer.Position = 0;
            using var reader = new StreamReader(buffer, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>Girdiyi sınırlı olarak dosyaya çıkarır; sınır aşılırsa yarım dosya silinir.</summary>
        private static void ExtractEntry(ZipArchiveEntry entry, string destinationPath, long maxBytes)
        {
            try
            {
                using var source = entry.Open();
                using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                CopyLimited(source, target, maxBytes, entry.FullName);
            }
            catch (InvalidDataException)
            {
                try { File.Delete(destinationPath); } catch { }
                throw;
            }
        }

        private static void CopyLimited(Stream source, Stream target, long maxBytes, string entryName)
        {
            var chunk = new byte[81920];
            long copied = 0;
            int read;
            while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
            {
                copied += read;
                if (copied > maxBytes)
                    throw new InvalidDataException($"Yedekteki \"{entryName}\" boyut sınırını aşıyor ({maxBytes / (1024 * 1024)} MB).");
                target.Write(chunk, 0, read);
            }
        }
    }
}
