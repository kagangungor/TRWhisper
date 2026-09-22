using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;
using TRWhisper.Core.Dictionary;
using TRWhisper.Core.Llm;
using TRWhisper.Core.Native;
using TRWhisper.Core.Speech;
using TRWhisper.Core.Tray;

// Proje hem WPF hem WinForms kullanıyor; örtük using'ler yüzünden bu tip adları belirsiz
// kalıyor. Ayarlar penceresi tamamen WPF olduğundan WPF karşılıkları sabitleniyor.
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;
using RadioButton = System.Windows.Controls.RadioButton;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TRWhisper.UI
{
    /// <summary>
    /// config.json'ı elle düzenleme ihtiyacını ortadan kaldıran sekmeli ayarlar penceresi.
    /// Kaydetme, ayarları ilgili servislere yeniden başlatma gerektirmeden uygular.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private sealed record ComboEntry(string Value, string Label)
        {
            public override string ToString() => Label;
        }

        private readonly ConfigManager _configManager;
        private readonly CustomDictionaryService _dictionaryService;
        private readonly ILlmCleaner _llmCleaner;
        private readonly PillOverlayWindow? _overlayWindow;

        /// <summary>Kaydedilen ayarların canlı uygulanması için geri çağrı (Program.cs bağlar).</summary>
        private readonly Action<AppConfig>? _onApplied;

        private readonly ObservableCollection<DictionaryRule> _rules = new();
        private readonly ObservableCollection<AppModeMappingItem> _appMappings = new();
        private readonly ObservableCollection<WhisperModelItem> _modelItems = new();
        private readonly StackPanel[] _pages;

        private List<LlmMode> _currentModes = new();
        private string? _selectedModeId;
        private readonly Dictionary<string, string> _promptDrafts = new(StringComparer.OrdinalIgnoreCase);

        // Kısayollar (Hotkey) durumları
        private string _currentPttKey = "RightCtrl";
        private string _currentLlmKey = "Shift";
        private bool _recordingPtt;
        private bool _recordingLlm;

        // Model indirme durumu
        private CancellationTokenSource? _modelDownloadCts;

        // Kapsül (Overlay) konum durumu
        private string _overlayPosition = "Bottom";
        private double? _customX;
        private double? _customY;

        private bool _loading = true;

        /// <summary>"Vazgeç" ile kapatılıyorsa kaydetme adımı tamamen atlanır.</summary>
        private bool _closingWithoutSave;

        /// <summary>API anahtarı açık metin olarak mı gösteriliyor?</summary>
        private bool _apiKeyRevealed;

        /// <summary>Yükleme bittiğindeki alan imzası; kaydedilmemiş değişiklik tespiti için.</summary>
        private string _savedSignature = "";

        public SettingsWindow(ConfigManager configManager,
                              CustomDictionaryService dictionaryService,
                              Action<AppConfig>? onApplied = null,
                              ILlmCleaner? llmCleaner = null,
                              PillOverlayWindow? overlayWindow = null)
        {
            InitializeComponent();

            _configManager = configManager;
            _dictionaryService = dictionaryService;
            _onApplied = onApplied;
            _llmCleaner = llmCleaner ?? new LlmCleanerService(configManager);
            _overlayWindow = overlayWindow;

            _pages = new[] { PageGeneral, PageAudio, PageModel, PageHotkey, PageDictionary, PageAi, PageOverlay };

            RulesList.ItemsSource = _rules;
            AppMappingsList.ItemsSource = _appMappings;
            ModelsListControl.ItemsSource = _modelItems;
            PreviewKeyDown += SettingsWindow_PreviewKeyDown;
            PreviewKeyUp += SettingsWindow_PreviewKeyUp;
            PreviewMouseDown += SettingsWindow_PreviewMouseDown;

            LoadSettings();
            _loading = false;
            _savedSignature = UiSignature();
        }

        /// <summary>
        /// API anahtarının tek doğru kaynağı. Maskeli ve açık metin kutuları aynı
        /// değeri taşır; okuma her zaman o an görünür olandan yapılır.
        /// </summary>
        private string ApiKeyText
        {
            get => _apiKeyRevealed ? LlmApiKeyBox.Text : LlmApiKeyPasswordBox.Password;
            set
            {
                LlmApiKeyBox.Text = value;
                LlmApiKeyPasswordBox.Password = value;
            }
        }

        private void ApiKeyReveal_Click(object sender, RoutedEventArgs e)
        {
            // Görünür kutudaki güncel değeri diğerine taşı, sonra yer değiştir.
            if (_apiKeyRevealed) LlmApiKeyPasswordBox.Password = LlmApiKeyBox.Text;
            else LlmApiKeyBox.Text = LlmApiKeyPasswordBox.Password;

            _apiKeyRevealed = !_apiKeyRevealed;

            LlmApiKeyBox.Visibility = _apiKeyRevealed ? Visibility.Visible : Visibility.Collapsed;
            LlmApiKeyPasswordBox.Visibility = _apiKeyRevealed ? Visibility.Collapsed : Visibility.Visible;

            var label = _apiKeyRevealed ? "API anahtarını gizle" : "API anahtarını göster";
            ApiKeyRevealButton.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(ApiKeyRevealButton, label);
            ApiKeyRevealIcon.Data = (Geometry)FindResource(_apiKeyRevealed ? "IconEyeOff" : "IconEye");

            (_apiKeyRevealed ? (System.Windows.Controls.Control)LlmApiKeyBox : LlmApiKeyPasswordBox).Focus();
        }

        // ------------------------------------------------------------------ yükleme

        private void LoadSettings()
        {
            // Bu pencere config.json bozuk olsa bile AÇILMALI: ConfigManager zaten hatalı
            // dosyada varsayılanlara düşüyor; burada da her alan tek tek korunuyor ki
            // tek bir bozuk değer pencerenin tamamını engellemesin.
            var problems = new List<string>();
            var configBroken = _configManager.LastLoadFailed;

            AppConfig cfg;
            try
            {
                cfg = _configManager.Current;
            }
            catch (Exception ex)
            {
                FileLog.Write($"[Settings] Yapılandırma okunamadı: {ex.Message}");
                problems.Add("yapılandırma okunamadı, varsayılanlar gösteriliyor");
                cfg = new AppConfig();
            }

            TryLoad(problems, "genel", () =>
            {
                FillCombo(LanguageCombo, new[]
                {
                    new ComboEntry("tr", "Türkçe"),
                    new ComboEntry("en", "İngilizce"),
                    new ComboEntry("de", "Almanca"),
                    new ComboEntry("fr", "Fransızca"),
                    new ComboEntry("auto", "Otomatik algıla"),
                }, cfg.General.Language);

                AutostartToggle.IsChecked = AutostartManager.IsEnabled();
                NormalizationToggle.IsChecked = cfg.General.EnableTextNormalization;
                DictionaryToggle.IsChecked = cfg.General.EnableCustomDictionary;
                StreamingPreviewToggle.IsChecked = cfg.General.EnableStreamingPreview;
            });

            TryLoad(problems, "ses", () => LoadMicrophones(cfg.Audio.InputDeviceId));

            TryLoad(problems, "model", () =>
            {
                IdleMinutesBox.Text = cfg.Whisper.IdleTimeoutMinutes.ToString(CultureInfo.InvariantCulture);
                UpdateGpuBadge();
                RefreshModelList();
            });

            TryLoad(problems, "kısayol", () =>
            {
                FillCombo(DictationModeCombo, new[]
                {
                    new ComboEntry("PushToTalk", "Bas-konuş (Basılı tut)"),
                    new ComboEntry("Toggle", "Aç/Kapa (Toggle)"),
                    new ComboEntry("HandsFree", "Eller Serbest (Sessizlikte otomatik bitir)"),
                }, cfg.Hotkey.DictationMode);
                DictationModeCombo.SelectionChanged += (_, _) => UpdateModeHint();

                SilenceMsBox.Text = cfg.Hotkey.HandsFreeSilenceMs.ToString(CultureInfo.InvariantCulture);
                SilenceThresholdBox.Text = cfg.Hotkey.HandsFreeSilenceThreshold.ToString("0.###", CultureInfo.InvariantCulture);
                UpdateModeHint();

                _currentPttKey = string.IsNullOrWhiteSpace(cfg.Hotkey.PushToTalkKey) ? "RightCtrl" : cfg.Hotkey.PushToTalkKey;
                _currentLlmKey = string.IsNullOrWhiteSpace(cfg.Hotkey.LlmModifierKey) ? "Shift" : cfg.Hotkey.LlmModifierKey;
                UpdateHotkeyDisplays();
            });

            TryLoad(problems, "sözlük", LoadRules);

            TryLoad(problems, "yapay zeka", () =>
            {
                LlmEnabledToggle.IsChecked = cfg.LlmCleaning.EnabledByDefault;
                FillCombo(LlmProviderCombo, new[]
                {
                    new ComboEntry("Ollama", "Ollama (Yerel)"),
                    new ComboEntry("Gemini", "Google Gemini (Bulut)"),
                    new ComboEntry("OpenAI", "OpenAI (Bulut)"),
                }, cfg.LlmCleaning.Provider);

                LlmEndpointBox.Text = cfg.LlmCleaning.Endpoint ?? "";
                LlmModelBox.Text = cfg.LlmCleaning.Model ?? "";
                ApiKeyText = cfg.LlmCleaning.ApiKey ?? "";

                UpdateLlmProviderUI();

                // LLM Modları yükleme
                _currentModes = LlmModeRegistry.GetAllModes(cfg.LlmCleaning);
                _promptDrafts.Clear();
                foreach (var mode in _currentModes)
                {
                    _promptDrafts[mode.Id] = mode.SystemPrompt;
                }
                RefreshLlmModesCombo(cfg.LlmCleaning.ActiveModeId);

                // Otomatik Uygulama Modu yükleme
                AutoAppModeToggle.IsChecked = cfg.LlmCleaning.EnableAutoAppMode;
                UpdateAutoAppModeUI();
                LoadAppMappings(cfg.LlmCleaning);
            });

            TryLoad(problems, "kapsül", () =>
            {
                _overlayPosition = string.IsNullOrWhiteSpace(cfg.Overlay.Position) ? "Bottom" : cfg.Overlay.Position;
                _customX = cfg.Overlay.CustomX;
                _customY = cfg.Overlay.CustomY;

                FillCombo(OverlayPositionCombo, new[]
                {
                    new ComboEntry("Bottom", "Ekranın altı (Varsayılan)"),
                    new ComboEntry("Top", "Ekranın üstü"),
                    new ComboEntry("Custom", "Özel konum (Ekranda sürükle-bırak)"),
                }, _overlayPosition);

                UpdateOverlayCoordsUI();
                OverlayDurationBox.Text = cfg.Overlay.ResultDurationSeconds.ToString(CultureInfo.InvariantCulture);
            });

            if (configBroken)
            {
                StatusText.Text = "config.json okunamadı (bozuk olabilir). Varsayılanlar gösteriliyor; " +
                                  "Kaydet'e basarsanız dosya bu değerlerle yeniden yazılır.";
            }
            else if (problems.Count > 0)
            {
                StatusText.Text = "Bazı ayarlar okunamadı, varsayılanlar kullanıldı: " + string.Join(", ", problems);
            }
            else
            {
                StatusText.Text = $"Ayarlar yüklendi — {_configManager.ConfigFilePath}";
            }

            if (configBroken || problems.Count > 0)
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
        }

        /// <summary>
        /// Tek bir sekmenin yüklenmesi hata verse bile pencere açılmaya devam etsin:
        /// bozuk bir alan yüzünden ayarlara hiç erişememek en kötü sonuç olurdu.
        /// </summary>
        private static void TryLoad(List<string> problems, string section, Action load)
        {
            try { load(); }
            catch (Exception ex)
            {
                FileLog.Write($"[Settings] '{section}' yüklenemedi: {ex.Message}");
                problems.Add(section);
            }
        }

        private static void FillCombo(ComboBox combo, ComboEntry[] entries, string? selectedValue)
        {
            combo.ItemsSource = entries;
            combo.SelectedItem = entries.FirstOrDefault(
                                     e => string.Equals(e.Value, selectedValue, StringComparison.OrdinalIgnoreCase))
                                 ?? entries.FirstOrDefault();
        }

        private static string SelectedValue(ComboBox combo, string fallback)
            => combo.SelectedItem is ComboEntry entry ? entry.Value : fallback;

        private void LoadMicrophones(string? selectedId)
        {
            var entries = new List<ComboEntry> { new("", "Sistem varsayılanı") };
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    entries.Add(new ComboEntry(device.ID, device.FriendlyName));
                    device.Dispose();
                }
            }
            catch (Exception ex)
            {
                FileLog.Write($"[Settings] Mikrofonlar listelenemedi: {ex.Message}");
            }

            // Seçili cihaz artık takılı değilse listede görünür kalsın, sessizce kaybolmasın.
            if (!string.IsNullOrEmpty(selectedId) && entries.All(e => e.Value != selectedId))
                entries.Add(new ComboEntry(selectedId, "(bağlı değil) " + selectedId));

            FillCombo(MicrophoneCombo, entries.ToArray(), selectedId ?? "");
        }

        /// <summary>
        /// Seçilen çalışma biçimini anlatır ve eller serbest ayarlarını yalnızca o modda gösterir.
        /// </summary>
        private void UpdateModeHint()
        {
            var mode = SelectedValue(DictationModeCombo, "PushToTalk");

            DictationModeHint.Text = mode switch
            {
                "Toggle" => "Tuşa bir kez basın, konuşun, bitirmek için tekrar basın. Tuşu basılı tutmanız gerekmez.",
                "HandsFree" => "Tuşa bir kez basın ve konuşun; konuşmanız bitip sessizlik sürünce kayıt kendiliğinden biter. Tekrar basarsanız hemen biter.",
                _ => "Tuşu basılı tutun, konuşun, bırakın. 350 ms'den kısa basışlar yanlışlıkla sayılıp iptal edilir.",
            };

            HandsFreeCard.Visibility = mode == "HandsFree" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateGpuBadge()
        {
            // CudaEligible yalnızca EnsureConfigured çalıştıktan sonra anlamlıdır; ayarlar
            // penceresi ilk dikteden önce açılırsa "CPU" yazıp kullanıcıyı yanıltabilirdi.
            try { WhisperNetRuntime.EnsureConfigured(); }
            catch (Exception ex) { FileLog.Write($"[Settings] CUDA durumu belirlenemedi: {ex.Message}"); }

            var enabled = WhisperNetRuntime.CudaEligible;

            GpuBadgeText.Text = enabled ? "CUDA etkin" : "CPU modu";
            var color = enabled ? Color.FromRgb(0x22, 0xC5, 0x5E) : Color.FromRgb(0x71, 0x71, 0x7A);
            GpuDot.Fill = new SolidColorBrush(color);
            GpuBadge.BorderBrush = new SolidColorBrush(color);
            GpuBadge.Background = new SolidColorBrush(enabled
                ? Color.FromRgb(0x16, 0x30, 0x1F)
                : Color.FromRgb(0x1D, 0x1D, 0x20));

            GpuHint.Text = enabled
                ? "GPU kullanılıyor; Large-v3 Turbo modeli rahatlıkla çalışır."
                : "CUDA çalışma zamanı bulunamadı. Large-v3 Turbo CPU'da çok yavaştır (~20+ sn); Small modelini tercih edin.";
        }

        private void LlmProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            UpdateLlmProviderUI(autoFillDefaults: true);
        }

        private void UpdateLlmProviderUI(bool autoFillDefaults = false)
        {
            var provider = SelectedValue(LlmProviderCombo, "Ollama");
            bool isOllama = provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);

            if (OllamaOfflineBadge != null)
                OllamaOfflineBadge.Visibility = isOllama ? Visibility.Visible : Visibility.Collapsed;

            if (CloudWarningBadge != null)
                CloudWarningBadge.Visibility = isOllama ? Visibility.Collapsed : Visibility.Visible;

            if (LlmApiKeyPanel != null)
                LlmApiKeyPanel.Visibility = isOllama ? Visibility.Collapsed : Visibility.Visible;

            if (isOllama)
            {
                if (LlmEndpointHint != null)
                    LlmEndpointHint.Text = "Ollama için varsayılan: http://localhost:11434/v1/chat/completions";
                if (LlmModelHint != null)
                    LlmModelHint.Text = "Önerilen yerel modeller: qwen2.5:3b, llama3.2:3b, gemma2:2b";
                if (autoFillDefaults)
                {
                    if (string.IsNullOrWhiteSpace(LlmEndpointBox.Text) || LlmEndpointBox.Text.Contains("googleapis") || LlmEndpointBox.Text.Contains("openai"))
                        LlmEndpointBox.Text = "http://localhost:11434/v1/chat/completions";
                    if (string.IsNullOrWhiteSpace(LlmModelBox.Text) || LlmModelBox.Text.StartsWith("gemini") || LlmModelBox.Text.StartsWith("gpt"))
                        LlmModelBox.Text = "qwen2.5:3b";
                }
            }
            else if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                if (LlmEndpointHint != null)
                    LlmEndpointHint.Text = "Gemini API varsayılan adresi kullanılır.";
                if (LlmModelHint != null)
                    LlmModelHint.Text = "Önerilen bulut model: gemini-2.0-flash";
                if (autoFillDefaults)
                {
                    LlmEndpointBox.Text = "https://generativelanguage.googleapis.com/v1beta/models";
                    if (string.IsNullOrWhiteSpace(LlmModelBox.Text) || LlmModelBox.Text.Contains(":") || LlmModelBox.Text.StartsWith("gpt"))
                        LlmModelBox.Text = "gemini-2.0-flash";
                }
            }
            else if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                if (LlmEndpointHint != null)
                    LlmEndpointHint.Text = "OpenAI varsayılan: https://api.openai.com/v1/chat/completions";
                if (LlmModelHint != null)
                    LlmModelHint.Text = "Önerilen bulut model: gpt-4o-mini";
                if (autoFillDefaults)
                {
                    LlmEndpointBox.Text = "https://api.openai.com/v1/chat/completions";
                    if (string.IsNullOrWhiteSpace(LlmModelBox.Text) || LlmModelBox.Text.Contains(":") || LlmModelBox.Text.StartsWith("gemini"))
                        LlmModelBox.Text = "gpt-4o-mini";
                }
            }
        }

        private void RefreshLlmModesCombo(string? selectModeId = null)
        {
            var entries = _currentModes.Select(m => new ComboEntry(m.Id, m.DisplayName)).ToArray();
            FillCombo(LlmModeCombo, entries, selectModeId ?? _selectedModeId ?? "Clean");
            UpdateSelectedModeUI(SelectedValue(LlmModeCombo, "Clean"));
            RefreshNewAppModeCombo();
        }

        private void AutoAppModeToggle_Changed(object sender, RoutedEventArgs e)
        {
            UpdateAutoAppModeUI();
        }

        private void UpdateAutoAppModeUI()
        {
            if (AutoAppModePanel != null)
            {
                AutoAppModePanel.Visibility = AutoAppModeToggle.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void LoadAppMappings(LlmCleaningConfig config)
        {
            _appMappings.Clear();
            var mappings = config.AppModeMappings ?? LlmCleaningConfig.GetDefaultAppModeMappings();

            foreach (var kvp in mappings)
            {
                var mode = _currentModes.FirstOrDefault(m => string.Equals(m.Id, kvp.Value, StringComparison.OrdinalIgnoreCase));
                string display = mode != null ? mode.DisplayName : kvp.Value;
                _appMappings.Add(new AppModeMappingItem
                {
                    ProcessName = kvp.Key,
                    ModeId = kvp.Value,
                    ModeDisplayName = display
                });
            }

            RefreshNewAppModeCombo();
        }

        private void RefreshNewAppModeCombo()
        {
            if (NewAppModeCombo == null) return;
            var entries = _currentModes.Select(m => new ComboEntry(m.Id, m.DisplayName)).ToArray();
            FillCombo(NewAppModeCombo, entries, "Clean");
        }

        private void AddAppMapping_Click(object sender, RoutedEventArgs e)
        {
            var processName = NewAppProcessBox.Text.Trim();
            if (string.IsNullOrEmpty(processName))
            {
                StatusText.Text = "Eşleme eklemek için süreç adını girin.";
                NewAppProcessBox.Focus();
                return;
            }

            // Eğer kullanıcı '.exe' uzantısı girdiyse temizle ("outlook.exe" -> "outlook")
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                processName = processName.Substring(0, processName.Length - 4);
            }

            var modeId = SelectedValue(NewAppModeCombo, "Clean");
            var mode = _currentModes.FirstOrDefault(m => string.Equals(m.Id, modeId, StringComparison.OrdinalIgnoreCase));
            string modeDisplay = mode?.DisplayName ?? modeId;

            // Varsa eskisini kaldır
            var existing = _appMappings.FirstOrDefault(m => string.Equals(m.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _appMappings.Remove(existing);
            }

            _appMappings.Insert(0, new AppModeMappingItem
            {
                ProcessName = processName,
                ModeId = modeId,
                ModeDisplayName = modeDisplay
            });

            NewAppProcessBox.Clear();
            StatusText.Text = $"'{processName}' → {modeDisplay} eşlendi.";
            NewAppProcessBox.Focus();
        }

        private void DeleteAppMapping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: AppModeMappingItem item }) return;
            _appMappings.Remove(item);
            StatusText.Text = $"'{item.ProcessName}' eşlemesi kaldırıldı.";
        }

        private void UpdateSelectedModeUI(string modeId)
        {
            _selectedModeId = modeId;
            var mode = _currentModes.FirstOrDefault(m => string.Equals(m.Id, modeId, StringComparison.OrdinalIgnoreCase))
                       ?? _currentModes.FirstOrDefault();

            if (mode == null) return;

            LlmModeDescriptionText.Text = string.IsNullOrWhiteSpace(mode.Description)
                ? "(Açıklama belirtilmemiş)"
                : mode.Description;

            if (_promptDrafts.TryGetValue(mode.Id, out var draft))
            {
                LlmModePromptBox.Text = draft;
            }
            else
            {
                LlmModePromptBox.Text = mode.SystemPrompt;
            }

            DeleteCustomModeButton.Visibility = mode.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;
            ResetModePromptButton.Visibility = mode.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LlmModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            // Önceki modun taslak istemini sakla
            if (!string.IsNullOrEmpty(_selectedModeId))
            {
                _promptDrafts[_selectedModeId] = LlmModePromptBox.Text;
            }

            var newModeId = SelectedValue(LlmModeCombo, "Clean");
            UpdateSelectedModeUI(newModeId);
        }

        private void ResetModePrompt_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedModeId)) return;

            var defaultMode = LlmModeRegistry.GetDefaultModes()
                .FirstOrDefault(m => string.Equals(m.Id, _selectedModeId, StringComparison.OrdinalIgnoreCase));

            if (defaultMode != null)
            {
                LlmModePromptBox.Text = defaultMode.SystemPrompt;
                _promptDrafts[_selectedModeId] = defaultMode.SystemPrompt;
                StatusText.Text = $"'{defaultMode.Name}' modu istemi varsayılana sıfırlandı.";
            }
        }

        private void AddNewMode_Click(object sender, RoutedEventArgs e)
        {
            var name = NewModeNameBox.Text.Trim();
            var icon = NewModeIconBox.Text.Trim();
            var desc = NewModeDescBox.Text.Trim();
            var prompt = NewModePromptBox.Text.Trim();

            if (string.IsNullOrEmpty(name))
            {
                StatusText.Text = "Yeni mod eklemek için mod adını doldurun.";
                NewModeNameBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(icon)) icon = "🎯";
            if (string.IsNullOrEmpty(prompt))
            {
                prompt = $"Sen bir {name} asistanısın. Görevin, verilen dikte metnini düzenlemektir.\n" +
                         LlmModeRegistry.SandboxSuffix;
            }
            else
            {
                prompt = LlmModeRegistry.EnsureSandboxedPrompt(prompt);
            }

            var id = "custom_" + Guid.NewGuid().ToString("N")[..8];
            var newMode = new LlmMode(id, name, icon, desc, prompt, isBuiltIn: false);

            _currentModes.Add(newMode);
            _promptDrafts[id] = prompt;

            RefreshLlmModesCombo(id);

            NewModeNameBox.Clear();
            NewModeDescBox.Clear();
            NewModePromptBox.Clear();
            NewModeIconBox.Text = "🎯";

            StatusText.Text = $"'{name}' özel modu eklendi.";
        }

        private void DeleteCustomMode_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedModeId)) return;

            var mode = _currentModes.FirstOrDefault(m => string.Equals(m.Id, _selectedModeId, StringComparison.OrdinalIgnoreCase));
            if (mode == null || mode.IsBuiltIn) return;

            _currentModes.Remove(mode);
            _promptDrafts.Remove(mode.Id);

            StatusText.Text = $"'{mode.Name}' modu silindi.";
            RefreshLlmModesCombo("Clean");
        }

        private async void TestLlmConnection_Click(object sender, RoutedEventArgs e)
        {
            if (TestLlmConnectionButton == null || LlmConnectionStatusText == null) return;

            TestLlmConnectionButton.IsEnabled = false;
            TestLlmConnectionButton.Content = "⏳ Test ediliyor...";
            LlmConnectionStatusText.Visibility = Visibility.Visible;
            LlmConnectionStatusText.Foreground = (Brush)FindResource("TextMuted");
            LlmConnectionStatusText.Text = "Bağlantı kuruluyor ve test ediliyor... (Yerel model ilk kez yükleniyorsa 10-15 sn sürebilir)";

            try
            {
                var provider = SelectedValue(LlmProviderCombo, "Ollama");
                var tempConfig = new LlmCleaningConfig
                {
                    Provider = provider,
                    Endpoint = LlmEndpointBox.Text?.Trim() ?? "",
                    Model = LlmModelBox.Text?.Trim() ?? "",
                    ApiKey = ApiKeyText.Trim()
                };

                var (success, message) = await _llmCleaner.TestConnectionAsync(tempConfig);

                if (success)
                {
                    LlmConnectionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)); // Yeşil
                    LlmConnectionStatusText.Text = message;
                }
                else
                {
                    LlmConnectionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)); // Kırmızı / Mercan
                    LlmConnectionStatusText.Text = message;
                }
            }
            catch (Exception ex)
            {
                LlmConnectionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                LlmConnectionStatusText.Text = "Beklenmeyen hata: " + ex.Message;
            }
            finally
            {
                TestLlmConnectionButton.IsEnabled = true;
                TestLlmConnectionButton.Content = "⚡ Bağlantıyı Test Et";
            }
        }

        private void LoadRules()
        {
            _rules.Clear();
            foreach (var rule in _dictionaryService.GetRules())
                _rules.Add(rule);
            UpdateRuleCount();
        }

        private void UpdateRuleCount()
            => RuleCountText.Text = $"{_rules.Count} kural — {_dictionaryService.DictionaryPath}";

        // ------------------------------------------------------------------ gezinme

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (_pages == null) return;   // ctor içindeki ilk IsChecked=True için
            if (sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out var index)) return;

            for (int i = 0; i < _pages.Length; i++)
                _pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

            // Sekmeler tek bir ScrollViewer'i paylasiyor; onceki sayfanin kaydirma
            // konumu kalirsa yeni sayfa bos bir alanla aciliyor.
            ContentScroll?.ScrollToTop();
        }

        // ------------------------------------------------------------------ sözlük

        /// <summary>Yeni kural alanlarinda Enter, "Ekle" dugmesiyle ayni isi yapar.</summary>
        private void NewRuleBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            AddRule_Click(sender, e);
            e.Handled = true;
        }

        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            var match = NewMatchBox.Text.Trim();
            var replace = NewReplaceBox.Text.Trim();

            if (match.Length == 0 || replace.Length == 0)
            {
                StatusText.Text = "Kural eklemek için her iki alanı da doldurun.";
                return;
            }

            var existing = _rules.FirstOrDefault(
                r => string.Equals(r.Match, match, StringComparison.OrdinalIgnoreCase));
            if (existing != null) _rules.Remove(existing);

            _rules.Insert(0, new DictionaryRule { Match = match, Replace = replace });

            if (SaveRules())
            {
                NewMatchBox.Clear();
                NewReplaceBox.Clear();
                StatusText.Text = existing != null
                    ? $"'{match}' kuralı güncellendi."
                    : $"'{match}' → '{replace}' eklendi.";
                NewMatchBox.Focus();
            }
        }

        private void DeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: DictionaryRule rule }) return;

            _rules.Remove(rule);
            if (SaveRules()) StatusText.Text = $"'{rule.Match}' kuralı silindi.";
        }

        /// <summary>Sözlük değişiklikleri anında diske yazılır; "Kaydet"i beklemez.</summary>
        private bool SaveRules()
        {
            UpdateRuleCount();
            if (_dictionaryService.SaveRules(_rules.ToList())) return true;

            StatusText.Text = "Sözlük kaydedilemedi, dictionary.json yazılabilir durumda mı?";
            return false;
        }

        // ------------------------------------------------------------------ pencere kontrolleri (Discord stili)

        private void SettingsWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                if (WindowBorder != null)
                {
                    WindowBorder.BorderThickness = new Thickness(0);
                    WindowBorder.Padding = new Thickness(7);
                }
                if (MaximizeIcon != null)
                {
                    MaximizeIcon.Data = (Geometry)FindResource("IconRestore");
                }
                if (MaximizeButton != null)
                {
                    MaximizeButton.ToolTip = "Geri Yükle";
                }
            }
            else
            {
                if (WindowBorder != null)
                {
                    WindowBorder.BorderThickness = new Thickness(1);
                    WindowBorder.Padding = new Thickness(0);
                }
                if (MaximizeIcon != null)
                {
                    MaximizeIcon.Data = (Geometry)FindResource("IconMaximize");
                }
                if (MaximizeButton != null)
                {
                    MaximizeButton.ToolTip = "Ekranı Kapla";
                }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ApplyAndSave()) _savedSignature = UiSignature();
        }

        /// <summary>Değişiklikleri yazmadan kapatır (Esc de buraya düşer).</summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _closingWithoutSave = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Karar verilmeden önce indirme iptal EDİLMEZ: kullanıcı "İptal" derse
            // pencere açık kalır ve sürmekte olan indirme boşuna kesilmiş olurdu.
            if (!_loading && !_closingWithoutSave && UiSignature() != _savedSignature)
            {
                var answer = MessageBox.Show(
                    "Kaydedilmemiş değişiklikler var. Kaydedilsin mi?",
                    "TRWhisper", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (answer == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (answer == MessageBoxResult.Yes && !ApplyAndSave(silent: true))
                {
                    e.Cancel = true;   // kaydedilemedi: kapatıp veriyi kaybetme
                    return;
                }
            }

            if (_modelDownloadCts != null)
            {
                try
                {
                    _modelDownloadCts.Cancel();
                    _modelDownloadCts.Dispose();
                }
                catch { }
                _modelDownloadCts = null;
            }

            base.OnClosing(e);
        }

        /// <summary>
        /// Kaydedilecek tüm alanların anlık imzası. "Vazgeç" dışında kapatılırken
        /// kaydedilmemiş değişiklik olup olmadığını anlamak için karşılaştırılır.
        /// Sözlük kuralları ve model indirme/silme buraya girmez: onlar zaten anında diske yazılır.
        /// </summary>
        private string UiSignature()
        {
            var sb = new StringBuilder();
            void Add(object? v) => sb.Append(v).Append('');

            Add(SelectedValue(LanguageCombo, ""));
            Add(NormalizationToggle.IsChecked);
            Add(DictionaryToggle.IsChecked);
            Add(StreamingPreviewToggle.IsChecked);
            Add(AutostartToggle.IsChecked);
            Add(SelectedValue(MicrophoneCombo, ""));
            Add(SelectedValue(ModelCombo, ""));
            Add(IdleMinutesBox.Text);
            Add(_currentPttKey);
            Add(_currentLlmKey);
            Add(SelectedValue(DictationModeCombo, ""));
            Add(SilenceMsBox.Text);
            Add(SilenceThresholdBox.Text);
            Add(LlmEnabledToggle.IsChecked);
            Add(SelectedValue(LlmProviderCombo, ""));
            Add(LlmEndpointBox.Text);
            Add(LlmModelBox.Text);
            Add(ApiKeyText);
            Add(SelectedValue(LlmModeCombo, ""));
            Add(LlmModePromptBox.Text);
            Add(AutoAppModeToggle.IsChecked);
            Add(_overlayPosition);
            Add(_customX);
            Add(_customY);
            Add(OverlayDurationBox.Text);

            foreach (var kv in _promptDrafts.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                Add(kv.Key);
                Add(kv.Value);
            }
            foreach (var m in _currentModes.Where(m => !m.IsBuiltIn))
            {
                Add(m.Id);
                Add(m.Name);
                Add(m.SystemPrompt);
            }
            foreach (var m in _appMappings)
            {
                Add(m.ProcessName);
                Add(m.ModeId);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Alanları yapılandırmaya yazar, diske kaydeder ve canlı uygulama geri çağrısını tetikler.
        /// </summary>
        public bool ApplyAndSave(bool silent = false)
        {
            try
            {
                var cfg = _configManager.Current;

                cfg.General.Language = SelectedValue(LanguageCombo, cfg.General.Language);
                cfg.General.EnableTextNormalization = NormalizationToggle.IsChecked == true;
                cfg.General.EnableCustomDictionary = DictionaryToggle.IsChecked == true;
                cfg.General.EnableStreamingPreview = StreamingPreviewToggle.IsChecked == true;

                cfg.Audio.InputDeviceId = SelectedValue(MicrophoneCombo, "");

                cfg.Whisper.ModelPath = SelectedValue(ModelCombo, cfg.Whisper.ModelPath);
                cfg.Whisper.IdleTimeoutMinutes = ParseInt(IdleMinutesBox.Text, cfg.Whisper.IdleTimeoutMinutes, 0, 600);

                cfg.Hotkey.PushToTalkKey = _currentPttKey;
                cfg.Hotkey.LlmModifierKey = _currentLlmKey;
                cfg.Hotkey.DictationMode = SelectedValue(DictationModeCombo, cfg.Hotkey.DictationMode);
                cfg.Hotkey.HandsFreeSilenceMs = ParseInt(SilenceMsBox.Text, cfg.Hotkey.HandsFreeSilenceMs, 300, 10000);
                cfg.Hotkey.HandsFreeSilenceThreshold =
                    ParseDouble(SilenceThresholdBox.Text, cfg.Hotkey.HandsFreeSilenceThreshold, 0.001, 0.5);

                cfg.LlmCleaning.EnabledByDefault = LlmEnabledToggle.IsChecked == true;
                cfg.LlmCleaning.Provider = SelectedValue(LlmProviderCombo, cfg.LlmCleaning.Provider);
                cfg.LlmCleaning.Endpoint = LlmEndpointBox.Text.Trim();
                cfg.LlmCleaning.Model = LlmModelBox.Text.Trim();
                cfg.LlmCleaning.ApiKey = ApiKeyText.Trim();

                // LLM Modları kaydetme
                if (!string.IsNullOrEmpty(_selectedModeId))
                {
                    _promptDrafts[_selectedModeId] = LlmModePromptBox.Text;
                }
                cfg.LlmCleaning.ActiveModeId = SelectedValue(LlmModeCombo, cfg.LlmCleaning.ActiveModeId ?? "Clean");
                cfg.LlmCleaning.CustomModes = _currentModes.Where(m => !m.IsBuiltIn).ToList();

                var defaultModes = LlmModeRegistry.GetDefaultModes();
                cfg.LlmCleaning.ModePromptOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var def in defaultModes)
                {
                    if (_promptDrafts.TryGetValue(def.Id, out var draftPrompt))
                    {
                        var trimmedDraft = draftPrompt.Trim();
                        var trimmedDef = def.SystemPrompt.Trim();
                        if (!string.Equals(trimmedDraft, trimmedDef, StringComparison.Ordinal))
                        {
                            cfg.LlmCleaning.ModePromptOverrides[def.Id] = draftPrompt;
                        }
                        else
                        {
                            cfg.LlmCleaning.ModePromptOverrides.Remove(def.Id);
                        }
                    }
                }

                // Otomatik Uygulama Modu kaydetme
                cfg.LlmCleaning.EnableAutoAppMode = AutoAppModeToggle.IsChecked == true;
                cfg.LlmCleaning.AppModeMappings = _appMappings.ToDictionary(
                    m => m.ProcessName,
                    m => m.ModeId,
                    StringComparer.OrdinalIgnoreCase);

                cfg.Overlay.Position = _overlayPosition;
                cfg.Overlay.CustomX = _customX;
                cfg.Overlay.CustomY = _customY;
                cfg.Overlay.ResultDurationSeconds = ParseInt(OverlayDurationBox.Text, cfg.Overlay.ResultDurationSeconds, 1, 120);

                // Otomatik başlatma config.json'da değil kayıt defterinde tutulur.
                var wantAutostart = AutostartToggle.IsChecked == true;
                if (wantAutostart != AutostartManager.IsEnabled() && !AutostartManager.SetEnabled(wantAutostart))
                {
                    if (!silent) StatusText.Text = "Otomatik başlatma ayarlanamadı (kayıt defterine yazılamadı).";
                }

                _configManager.Save(cfg);
                _onApplied?.Invoke(cfg);

                if (!silent)
                    StatusText.Text = $"Kaydedildi — {DateTime.Now:HH:mm:ss}";
                FileLog.Write("[Settings] Ayarlar kaydedildi ve uygulandı.");
                return true;
            }
            catch (Exception ex)
            {
                FileLog.Write($"[Settings] Kaydetme hatası: {ex.Message}");
                if (!silent)
                {
                    StatusText.Text = "Kaydedilemedi: " + ex.Message;
                    MessageBox.Show($"Ayarlar kaydedilemedi:\n{ex.Message}", "TRWhisper",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }
        }

        // ------------------------------------------------------------------ Model Kütüphanesi
        private void RefreshModelList()
        {
            _modelItems.Clear();
            var activePath = _configManager.Current.Whisper.ModelPath;
            var activeModel = WhisperModelManager.FindByPath(activePath);

            foreach (var model in WhisperModelManager.Catalog)
            {
                bool isActive = activeModel != null
                    ? string.Equals(model.Id, activeModel.Id, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(model.FileName, Path.GetFileName(activePath), StringComparison.OrdinalIgnoreCase);

                _modelItems.Add(new WhisperModelItem(model, isActive));
            }

            var installed = WhisperModelManager.GetInstalledModels();
            var entries = installed.Select(m => new ComboEntry(m.RelativePath, m.DisplayName)).ToList();
            if (entries.Count == 0)
            {
                entries = WhisperModelManager.Catalog.Where(m => !m.IsVad).Select(m => new ComboEntry(m.RelativePath, m.DisplayName)).ToList();
            }
            FillCombo(ModelCombo, entries.ToArray(), _configManager.Current.Whisper.ModelPath);
        }

        private async void DownloadModel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WhisperModelItem item }) return;

            if (_modelDownloadCts != null)
            {
                MessageBox.Show("Zaten devam eden bir indirme var.", "TRWhisper", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _modelDownloadCts = new CancellationTokenSource();
            DownloadProgressCard.Visibility = Visibility.Visible;
            DownloadingModelTitle.Text = $"{item.DisplayName} İndiriliyor...";
            ModelDownloadProgressBar.Value = 0;
            DownloadDetailText.Text = "Bağlantı kuruluyor...";
            DownloadSpeedText.Text = "";

            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                ModelDownloadProgressBar.Value = p.Percentage;
                DownloadDetailText.Text = $"{WhisperModelManager.FormatBytes(p.BytesDownloaded)} / {WhisperModelManager.FormatBytes(p.TotalBytes)} (%{Math.Round(p.Percentage, 1)})";
                DownloadSpeedText.Text = $"{WhisperModelManager.FormatBytes((long)p.SpeedBytesPerSecond)}/s";
            });

            try
            {
                var result = await WhisperModelManager.DownloadModelAsync(item.Id, progress, _modelDownloadCts.Token);
                DownloadProgressCard.Visibility = Visibility.Collapsed;

                StatusText.Text = result.Message;
                if (result.Success)
                {
                    RefreshModelList();
                }
            }
            catch (Exception ex)
            {
                DownloadProgressCard.Visibility = Visibility.Collapsed;
                StatusText.Text = $"İndirme hatası: {ex.Message}";
            }
            finally
            {
                _modelDownloadCts.Dispose();
                _modelDownloadCts = null;
            }
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_modelDownloadCts != null)
            {
                _modelDownloadCts.Cancel();
                StatusText.Text = "İndirme iptal ediliyor...";
            }
        }

        private void DeleteModel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WhisperModelItem item }) return;

            var confirm = MessageBox.Show(
                $"{item.DisplayName} modelini diskten silmek istediğinize emin misiniz?\n(Gerektiğinde tekrar indirebilirsiniz)",
                "Modeli Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            var result = WhisperModelManager.DeleteModel(item.Id);
            StatusText.Text = result.Message;
            RefreshModelList();
        }

        private void SelectModel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WhisperModelItem item }) return;

            _configManager.Current.Whisper.ModelPath = item.Info.RelativePath;
            SelectComboValue(ModelCombo, item.Info.RelativePath);
            RefreshModelList();
            StatusText.Text = $"{item.DisplayName} aktif model olarak seçildi. Kaydet'e basarak uygulayın.";
        }

        // ------------------------------------------------------------------ Kısayol Kaydı
        private void UpdateHotkeyDisplays()
        {
            var pttBinding = HotkeyBinding.Parse(_currentPttKey, "RightCtrl");
            PttKeyDisplayText.Text = pttBinding.DisplayName;

            var llmBinding = HotkeyBinding.Parse(_currentLlmKey, "Shift");
            LlmKeyDisplayText.Text = llmBinding.DisplayName;
        }

        private void RecordPttButton_Click(object sender, RoutedEventArgs e)
        {
            _recordingLlm = false;
            LlmRecordingBanner.Visibility = Visibility.Collapsed;

            _recordingPtt = !_recordingPtt;
            PttRecordingBanner.Visibility = _recordingPtt ? Visibility.Visible : Visibility.Collapsed;
            RecordPttButton.Content = _recordingPtt ? "Tuşa Basın..." : "Kısayol Ata";
            _lastModifierDown = Key.None;
        }

        private void RecordLlmButton_Click(object sender, RoutedEventArgs e)
        {
            _recordingPtt = false;
            PttRecordingBanner.Visibility = Visibility.Collapsed;

            _recordingLlm = !_recordingLlm;
            LlmRecordingBanner.Visibility = _recordingLlm ? Visibility.Visible : Visibility.Collapsed;
            RecordLlmButton.Content = _recordingLlm ? "Tuşa Basın..." : "Kısayol Ata";
            _lastModifierDown = Key.None;
        }

        private void DisableLlmButton_Click(object sender, RoutedEventArgs e)
        {
            CancelRecording();
            _currentLlmKey = "None";
            UpdateHotkeyDisplays();
            StatusText.Text = "LLM temizleme kısayolu devre dışı bırakıldı (Kullanma).";
        }

        private void PresetPtt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string keyTag)
            {
                CancelRecording();
                _currentPttKey = keyTag;
                UpdateHotkeyDisplays();
                StatusText.Text = $"Bas-konuş kısayolu '{PttKeyDisplayText.Text}' olarak ayarlandı.";
            }
        }

        private void PresetLlm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string keyTag)
            {
                CancelRecording();
                _currentLlmKey = keyTag;
                UpdateHotkeyDisplays();
                StatusText.Text = $"LLM temizleme kısayolu '{LlmKeyDisplayText.Text}' olarak ayarlandı.";
            }
        }

        private Key _lastModifierDown = Key.None;

        private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_recordingPtt && !_recordingLlm) return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                CancelRecording();
                StatusText.Text = "Kısayol kaydı iptal edildi.";
                e.Handled = true;
                return;
            }

            if (IsModifierKey(key))
            {
                _lastModifierDown = key;
                e.Handled = true;
                return;
            }

            _lastModifierDown = Key.None;
            var hotkeyStr = ConvertKeyToHotkeyString(key, Keyboard.Modifiers);
            if (string.IsNullOrEmpty(hotkeyStr)) return;

            CommitRecordedHotkey(hotkeyStr);
            e.Handled = true;
        }

        private void SettingsWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (!_recordingPtt && !_recordingLlm) return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (_lastModifierDown != Key.None && _lastModifierDown == key)
            {
                var hotkeyStr = ConvertKeyToHotkeyString(key, ModifierKeys.None);
                _lastModifierDown = Key.None;
                CommitRecordedHotkey(hotkeyStr);
                e.Handled = true;
            }
        }

        private void SettingsWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_recordingPtt && !_recordingLlm) return;

            if (e.XButton1 == MouseButtonState.Pressed)
            {
                CommitRecordedHotkey("Mouse4");
                e.Handled = true;
            }
            else if (e.XButton2 == MouseButtonState.Pressed)
            {
                CommitRecordedHotkey("Mouse5");
                e.Handled = true;
            }
        }

        private void CommitRecordedHotkey(string hotkeyStr)
        {
            if (_recordingPtt)
            {
                _currentPttKey = hotkeyStr;
                UpdateHotkeyDisplays();
                StatusText.Text = $"Yeni Bas-konuş kısayolu atandı: {PttKeyDisplayText.Text}";
            }
            else if (_recordingLlm)
            {
                _currentLlmKey = hotkeyStr;
                UpdateHotkeyDisplays();
                StatusText.Text = $"Yeni LLM temizleme kısayolu atandı: {LlmKeyDisplayText.Text}";
            }

            CancelRecording();
        }

        private void CancelRecording()
        {
            _recordingPtt = false;
            _recordingLlm = false;
            _lastModifierDown = Key.None;
            if (PttRecordingBanner != null) PttRecordingBanner.Visibility = Visibility.Collapsed;
            if (LlmRecordingBanner != null) LlmRecordingBanner.Visibility = Visibility.Collapsed;
            if (RecordPttButton != null) RecordPttButton.Content = "Kısayol Ata";
            if (RecordLlmButton != null) RecordLlmButton.Content = "Kısayol Ata";
        }

        private static bool IsModifierKey(Key key)
        {
            return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                       or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
        }

        private static string ConvertKeyToHotkeyString(Key key, ModifierKeys modifiers)
        {
            if (key is Key.LeftCtrl or Key.RightCtrl)
                return key == Key.RightCtrl ? "RightCtrl" : "LeftCtrl";
            if (key is Key.LeftAlt or Key.RightAlt)
                return key == Key.RightAlt ? "RightAlt" : "LeftAlt";
            if (key is Key.LeftShift or Key.RightShift)
                return key == Key.RightShift ? "RightShift" : "LeftShift";
            if (key is Key.Capital)
                return "CapsLock";

            var parts = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            string keyName = key switch
            {
                Key.Space => "Space",
                >= Key.F1 and <= Key.F24 => key.ToString(),
                >= Key.A and <= Key.Z => key.ToString(),
                >= Key.D0 and <= Key.D9 => key.ToString()[1..],
                >= Key.NumPad0 and <= Key.NumPad9 => key.ToString()["NumPad".Length..],
                Key.Tab => "Tab",
                Key.Return => "Enter",
                Key.Back => "Backspace",
                Key.Insert => "Insert",
                Key.Delete => "Delete",
                Key.Home => "Home",
                Key.End => "End",
                Key.PageUp => "PageUp",
                Key.PageDown => "PageDown",
                Key.Pause => "Pause",
                Key.Scroll => "ScrollLock",
                Key.PrintScreen => "PrintScreen",
                _ => key.ToString()
            };

            if (parts.Count > 0)
            {
                if (!IsModifierKey(key))
                {
                    parts.Add(keyName);
                }
                return string.Join("+", parts);
            }

            return keyName;
        }

        // ------------------------------------------------------------------ Kapsül Konumu
        private void UpdateOverlayCoordsUI()
        {
            if (OverlayCustomCoordsText == null) return;

            if (string.Equals(_overlayPosition, "Custom", StringComparison.OrdinalIgnoreCase) && _customX.HasValue && _customY.HasValue)
            {
                OverlayCustomCoordsText.Text = $"X: {Math.Round(_customX.Value)}, Y: {Math.Round(_customY.Value)} (Özel Konum)";
            }
            else if (string.Equals(_overlayPosition, "Top", StringComparison.OrdinalIgnoreCase))
            {
                OverlayCustomCoordsText.Text = "Ekranın Üstü";
            }
            else
            {
                OverlayCustomCoordsText.Text = "Varsayılan (Ekranın Altı / Orta)";
            }
        }

        private void OverlayPositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var val = SelectedValue(OverlayPositionCombo, "Bottom");
            _overlayPosition = val;
            UpdateOverlayCoordsUI();
        }

        private void LaunchPositioning_Click(object sender, RoutedEventArgs e)
        {
            if (_overlayWindow == null)
            {
                MessageBox.Show("Kapsül penceresi şu anda aktif değil.", "TRWhisper", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _overlayWindow.ShowPositioningMode(
                onSave: (x, y) =>
                {
                    _customX = x;
                    _customY = y;
                    _overlayPosition = "Custom";
                    SelectComboValue(OverlayPositionCombo, "Custom");
                    UpdateOverlayCoordsUI();
                    ApplyAndSave(silent: true);
                    StatusText.Text = $"Kapsül konumu kaydedildi: X: {Math.Round(x)}, Y: {Math.Round(y)}";
                },
                onResetDefault: () =>
                {
                    _customX = null;
                    _customY = null;
                    _overlayPosition = "Bottom";
                    SelectComboValue(OverlayPositionCombo, "Bottom");
                    UpdateOverlayCoordsUI();
                    ApplyAndSave(silent: true);
                    StatusText.Text = "Kapsül konumu varsayılana döndürüldü.";
                },
                onCancel: () =>
                {
                    StatusText.Text = "Konumlandırma iptal edildi.";
                });
        }

        private void ResetPosition_Click(object sender, RoutedEventArgs e)
        {
            _customX = null;
            _customY = null;
            _overlayPosition = "Bottom";
            SelectComboValue(OverlayPositionCombo, "Bottom");
            UpdateOverlayCoordsUI();
            if (_overlayWindow?.IsPositioningMode == true)
            {
                _overlayWindow.ExitPositioningMode();
            }
            StatusText.Text = "Kapsül konumu varsayılana döndürüldü. Kaydet'e basarak onaylayabilirsiniz.";
        }

        private static void SelectComboValue(ComboBox combo, string value)
        {
            if (combo.ItemsSource is IEnumerable<ComboEntry> entries)
            {
                combo.SelectedItem = entries.FirstOrDefault(e => string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase))
                                     ?? entries.FirstOrDefault();
            }
        }

        // ------------------------------------------------------------------ küçük yardımcılar

        private static double ParseDouble(string text, double fallback, double min, double max)
        {
            var normalized = (text ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return fallback;
            return Math.Clamp(value, min, max);
        }

        private static int ParseInt(string text, int fallback, int min, int max)
        {
            if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return fallback;
            return Math.Clamp(value, min, max);
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
            => e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$");

        private void RefreshMics_Click(object sender, RoutedEventArgs e)
        {
            LoadMicrophones(SelectedValue(MicrophoneCombo, ""));
            StatusText.Text = "Ses cihazları yenilendi.";
        }
    }

    public class AppModeMappingItem
    {
        public string ProcessName { get; set; } = string.Empty;
        public string ModeId { get; set; } = string.Empty;
        public string ModeDisplayName { get; set; } = string.Empty;
    }

    public class WhisperModelItem
    {
        public WhisperModelInfo Info { get; }
        public string Id => Info.Id;
        public string DisplayName => Info.DisplayName;
        public string Description => $"{Info.Description} ({Info.FormattedSize})";
        public bool IsInstalled => Info.IsInstalled;
        public bool IsActive { get; set; }

        public string StatusText => IsActive ? "Aktif Model" : (IsInstalled ? "Yüklü" : "İndirilmedi");

        public Brush StatusForeground => IsActive
            ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))
            : (IsInstalled
                ? new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA))
                : new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)));

        public Brush StatusBackground => IsActive
            ? new SolidColorBrush(Color.FromArgb(0x33, 0x16, 0x65, 0x34))
            : (IsInstalled
                ? new SolidColorBrush(Color.FromArgb(0x33, 0x1E, 0x40, 0xAF))
                : new SolidColorBrush(Color.FromArgb(0x33, 0x37, 0x41, 0x51)));

        public Brush StatusBorder => IsActive
            ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
            : (IsInstalled
                ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
                : new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63)));

        public Visibility SelectButtonVisibility => (IsInstalled && !IsActive && !Info.IsVad) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DownloadButtonVisibility => (!IsInstalled) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DeleteButtonVisibility => (IsInstalled && !IsActive) ? Visibility.Visible : Visibility.Collapsed;

        public WhisperModelItem(WhisperModelInfo info, bool isActive)
        {
            Info = info;
            IsActive = isActive;
        }
    }
}
