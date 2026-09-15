using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TRWhisper.Core.Config
{
    public class GeneralConfig
    {
        public string Language { get; set; } = "tr";
        public string LogDirectory { get; set; } = "%USERPROFILE%\\Dictation";
        public string TempAudioPath { get; set; } = "%TEMP%\\trwhisper_temp.wav";

        [JsonIgnore]
        public string ResolvedLogDirectory => Environment.ExpandEnvironmentVariables(LogDirectory);

        [JsonIgnore]
        public string ResolvedTempAudioPath => Environment.ExpandEnvironmentVariables(TempAudioPath);
    }

    public class WhisperConfig
    {
        public string CliPath { get; set; } = "tools\\whisper\\whisper-cli.exe";
        public string ModelPath { get; set; } = "tools\\whisper\\ggml-large-v3-turbo-q5_0.bin";
        public int Threads { get; set; } = 4;
        public bool NoTimestamps { get; set; } = true;

        /// <summary>
        /// whisper-cli sürecinin en fazla ne kadar çalışmasına izin verileceği. Süre
        /// aşılırsa süreç ağacı sonlandırılır; böylece dikte akışı "Çözümleniyor..."
        /// durumunda sonsuza kadar takılamaz.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 120;

        [JsonIgnore]
        public string ResolvedCliPath
        {
            get => ResolvePath(CliPath);
        }

        [JsonIgnore]
        public string ResolvedModelPath
        {
            get => ResolvePath(ModelPath);
        }

        private static string ResolvePath(string path)
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            if (Path.IsPathRooted(expanded) && File.Exists(expanded))
                return expanded;

            // 1. BaseDirectory kontrolü
            var direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, expanded);
            if (File.Exists(direct)) return direct;

            // 2. CurrentDirectory kontrolü
            var current = Path.Combine(Directory.GetCurrentDirectory(), expanded);
            if (File.Exists(current)) return current;

            // 3. Üst dizinleri tara (bin/Release/net9.0-windows -> proje kökü)
            var currentDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 5 && currentDir != null; i++)
            {
                var candidate = Path.Combine(currentDir.FullName, expanded);
                if (File.Exists(candidate)) return candidate;
                currentDir = currentDir.Parent;
            }

            return direct;
        }
    }

    public class LlmCleaningConfig
    {
        public bool EnabledByDefault { get; set; } = false;
        public string Provider { get; set; } = "Gemini";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gemini-2.5-flash";
        public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models";
        public string SystemPrompt { get; set; } = "Aşağıdaki metin Türkçe sesli dikte çıktısıdır. Dolgu kelimelerini (ııı, eee, şey, yani) temizle, noktalama ve imlayı düzelt. YALNIZCA düzeltilmiş metni döndür.";
    }

    public class PasteSettingsConfig
    {
        public int RestoreClipboardDelayMs { get; set; } = 150;
    }

    public class AppConfig
    {
        public GeneralConfig General { get; set; } = new();
        public WhisperConfig Whisper { get; set; } = new();
        public LlmCleaningConfig LlmCleaning { get; set; } = new();
        public PasteSettingsConfig PasteSettings { get; set; } = new();
    }

    public class ConfigManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _configFilePath;
        private AppConfig _currentConfig;
        private readonly object _lock = new();

        public event Action<AppConfig>? ConfigChanged;

        public AppConfig Current
        {
            get
            {
                lock (_lock)
                {
                    return _currentConfig;
                }
            }
        }

        public string ConfigFilePath => _configFilePath;

        public ConfigManager(string? configFilePath = null)
        {
            _configFilePath = configFilePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            _currentConfig = LoadOrCreate();
            WatchConfigFile();
        }

        public AppConfig LoadOrCreate()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_configFilePath))
                    {
                        var json = File.ReadAllText(_configFilePath);
                        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                        if (config != null)
                        {
                            _currentConfig = config;
                            return _currentConfig;
                        }
                    }

                    _currentConfig = new AppConfig();
                    Save(_currentConfig);
                    return _currentConfig;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConfigManager] Konfigürasyon okuma hatası, varsayılanlar yükleniyor: {ex.Message}");
                    _currentConfig = new AppConfig();
                    return _currentConfig;
                }
            }
        }

        public void Save(AppConfig config)
        {
            lock (_lock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_configFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var json = JsonSerializer.Serialize(config, JsonOptions);
                    File.WriteAllText(_configFilePath, json);
                    _currentConfig = config;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConfigManager] Konfigürasyon kaydetme hatası: {ex.Message}");
                }
            }
        }

        private void WatchConfigFile()
        {
            try
            {
                var dir = Path.GetDirectoryName(_configFilePath);
                var filename = Path.GetFileName(_configFilePath);

                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                var watcher = new FileSystemWatcher(dir, filename)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                watcher.Changed += (_, _) =>
                {
                    // Debounce file write events
                    System.Threading.Thread.Sleep(100);
                    var reloaded = LoadOrCreate();
                    ConfigChanged?.Invoke(reloaded);
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigManager] FileSystemWatcher başlatılamadı: {ex.Message}");
            }
        }
    }
}
