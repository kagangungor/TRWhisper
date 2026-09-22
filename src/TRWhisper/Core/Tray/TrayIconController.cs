using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;
using TRWhisper.Core.Dictionary;
using TRWhisper.Core.History;
using TRWhisper.Core.Llm;
using TRWhisper.Core.Speech;

namespace TRWhisper.Core.Tray
{
    public enum AppState
    {
        Idle,
        Recording,
        Transcribing
    }

    public class TrayIconController : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly NotifyIcon _notifyIcon;
        private readonly ConfigManager _configManager;
        private readonly TranscriptHistory _history;
        private readonly CustomDictionaryService? _dictionaryService;

        // Denetleyicinin oluşturulduğu UI thread'inin dispatcher'ı. Genel metotlar arka plan
        // thread'lerinden çağrılır; WinForms kontrolü (ContextMenuStrip) arka plan thread'inde
        // oluşturulursa o thread'e WindowsFormsSynchronizationContext kurulur ve çağıranın
        // sonraki await'i mesaj pompalanmayan thread'e post edilip asla devam etmez.
        private readonly Dispatcher _uiDispatcher;

        private AppState _currentState = AppState.Idle;
        private bool _isLlmCleaningEnabled;

        // Tepsiden seçilebilen whisper modelleri (WhisperModelManager kataloğundan dinamik).
        public static (string Label, string Path, bool NeedsGpu)[] WhisperModels =>
            WhisperModelManager.Catalog
                .Where(m => !m.IsVad)
                .Select(m => (m.DisplayName, m.RelativePath, m.NeedsGpu))
                .ToArray();

        public event Action<bool>? LlmCleaningToggled;
        public event Action<string>? LlmModeChanged;
        public event Action<string>? WhisperModelChanged;
        public event Action? ExitRequested;

        /// <summary>Tepsiden Ayarlar penceresi istendi (Program.cs açar).</summary>
        public event Action? SettingsRequested;

        public TrayIconController(ConfigManager configManager, TranscriptHistory history,
                                  CustomDictionaryService? dictionaryService = null)
        {
            _configManager = configManager;
            _history = history;
            _dictionaryService = dictionaryService;
            _uiDispatcher = Dispatcher.CurrentDispatcher;
            _isLlmCleaningEnabled = configManager.Current.LlmCleaning.EnabledByDefault;

            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "TRWhisper - Başlatılıyor..."
            };

            UpdateIcon(AppState.Idle);
            BuildContextMenu();

            _history.HistoryChanged += OnHistoryChanged;
        }

        public void SetState(AppState state)
        {
            RunOnUiThread(() =>
            {
                if (_currentState == state) return;
                _currentState = state;
                UpdateIcon(state);
                BuildContextMenu();
            });
        }

        public void SetLlmCleaningMode(bool enabled)
        {
            RunOnUiThread(() =>
            {
                _isLlmCleaningEnabled = enabled;
                BuildContextMenu();
            });
        }

        // UI thread'indeysek hemen çalıştır; değilsse sıraya koy (FIFO, çağıranı bloklamaz).
        private void RunOnUiThread(Action action)
        {
            if (_uiDispatcher.CheckAccess())
                action();
            else
                _uiDispatcher.BeginInvoke(action);
        }

        private void UpdateIcon(AppState state)
        {
            try
            {
                using var bmp = new Bitmap(32, 32);
                using var g = Graphics.FromImage(bmp);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                Color badgeColor;
                string statusText;

                switch (state)
                {
                    case AppState.Recording:
                        badgeColor = Color.FromArgb(239, 68, 68); // Kırmızı
                        statusText = "TRWhisper: Kaydediyor... (Sağ Ctrl'yi bırakın)";
                        break;
                    case AppState.Transcribing:
                        badgeColor = Color.FromArgb(245, 158, 11); // Amber / Sarı
                        statusText = "TRWhisper: Çözümlüyor... (Lütfen bekleyin)";
                        break;
                    case AppState.Idle:
                    default:
                        badgeColor = Color.FromArgb(59, 130, 246); // Mavi / Nötr
                        statusText = "TRWhisper: Boşta (Konuşmak için Sağ Ctrl basılı tutun)";
                        break;
                }

                // Dış dairesel arka plan
                using (var brush = new SolidBrush(badgeColor))
                {
                    g.FillEllipse(brush, 2, 2, 28, 28);
                }

                // Mikrofon simgesi çizimi (Basit beyaz piktogram)
                using (var pen = new Pen(Color.White, 2.5f))
                using (var brush = new SolidBrush(Color.White))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;

                    // Mikrofon kapsülü
                    g.FillRoundedRectangle(brush, 12, 7, 8, 11, 4);

                    // Mikrofon ayağı / U yay
                    g.DrawArc(pen, 9, 11, 14, 9, 0, 180);

                    // Alt dikey çizgi ve taban
                    g.DrawLine(pen, 16, 20, 16, 24);
                    g.DrawLine(pen, 12, 24, 20, 24);
                }

                var hIcon = bmp.GetHicon();
                Icon? icon = null;
                try
                {
                    icon = (Icon)Icon.FromHandle(hIcon).Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }

                var oldIcon = _notifyIcon.Icon;
                _notifyIcon.Icon = icon;
                oldIcon?.Dispose();

                _notifyIcon.Text = statusText.Length > 63 ? statusText.Substring(0, 63) : statusText;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TrayIconController] İkon güncelleme hatası: {ex.Message}");
            }
        }

        private void OnHistoryChanged()
        {
            RunOnUiThread(BuildContextMenu);
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            // Başlık / Durum
            var pttName = TRWhisper.Core.Native.HotkeyBinding.Parse(_configManager.Current.Hotkey.PushToTalkKey).DisplayName;
            string statusStr = _currentState switch
            {
                AppState.Recording => $"🎙️ Kaydediyor... ({pttName} basılı)",
                AppState.Transcribing => "⚙️ Çözümlüyor...",
                _ => $"🟢 Boşta ({pttName} basılı tutun)"
            };

            var statusItem = new ToolStripMenuItem(statusStr)
            {
                Enabled = false,
                Font = new Font(Control.DefaultFont, FontStyle.Bold)
            };
            menu.Items.Add(statusItem);

            // LLM Temizleme Modu Toggle
            var llmItem = new ToolStripMenuItem($"✨ LLM Temizleme: {(_isLlmCleaningEnabled ? "AÇIK" : "KAPALI")}")
            {
                Checked = _isLlmCleaningEnabled
            };
            llmItem.Click += (_, _) =>
            {
                _isLlmCleaningEnabled = !_isLlmCleaningEnabled;
                LlmCleaningToggled?.Invoke(_isLlmCleaningEnabled);
                BuildContextMenu();
            };
            menu.Items.Add(llmItem);

            // 🎯 LLM Modu Alt Menüsü
            var llmModeMenu = new ToolStripMenuItem("🎯 LLM Modu");

            var autoAppModeItem = new ToolStripMenuItem("⚡ Uygulamaya Göre Otomatik Mod")
            {
                Checked = _configManager.Current.LlmCleaning.EnableAutoAppMode
            };
            autoAppModeItem.Click += (_, _) =>
            {
                _configManager.Current.LlmCleaning.EnableAutoAppMode = !_configManager.Current.LlmCleaning.EnableAutoAppMode;
                _configManager.Save(_configManager.Current);
                FileLog.Write($"[TrayIconController] Otomatik Mod değiştirildi: {_configManager.Current.LlmCleaning.EnableAutoAppMode}");
                BuildContextMenu();
            };
            llmModeMenu.DropDownItems.Add(autoAppModeItem);
            llmModeMenu.DropDownItems.Add(new ToolStripSeparator());

            var activeLlmMode = LlmModeRegistry.GetActiveMode(_configManager.Current.LlmCleaning);
            var allLlmModes = LlmModeRegistry.GetAllModes(_configManager.Current.LlmCleaning);
            foreach (var mode in allLlmModes)
            {
                var modeItem = new ToolStripMenuItem($"{mode.Icon} {mode.Name}")
                {
                    Checked = string.Equals(mode.Id, activeLlmMode.Id, StringComparison.OrdinalIgnoreCase)
                };
                modeItem.Click += (_, _) =>
                {
                    if (modeItem.Checked) return;
                    _configManager.Current.LlmCleaning.ActiveModeId = mode.Id;
                    _configManager.Save(_configManager.Current);
                    LlmModeChanged?.Invoke(mode.Id);
                    FileLog.Write($"[TrayIconController] LLM Modu değişti: {mode.Name} ({mode.Id})");
                    BuildContextMenu();
                };
                llmModeMenu.DropDownItems.Add(modeItem);
            }
            menu.Items.Add(llmModeMenu);

            // Whisper Modeli Alt Menüsü (dosyası olmayan model soluk/devre dışı gösterilir)
            var modelMenu = new ToolStripMenuItem("🧠 Whisper Modeli");
            var currentModelFile = Path.GetFileName(_configManager.Current.Whisper.ModelPath);
            foreach (var model in WhisperModels)
            {
                var exists = File.Exists(WhisperConfig.ResolvePath(model.Path));
                var modelItem = new ToolStripMenuItem(exists ? model.Label : $"{model.Label} — dosya yok")
                {
                    Checked = string.Equals(Path.GetFileName(model.Path), currentModelFile, StringComparison.OrdinalIgnoreCase),
                    Enabled = exists
                };
                modelItem.Click += (_, _) =>
                {
                    if (modelItem.Checked) return;
                    WhisperModelChanged?.Invoke(model.Path);
                    BuildContextMenu();

                    var cliPath = _configManager.Current.Whisper.ResolvedCliPath;
                    Task.Run(() =>
                    {
                        // cuInit birkaç yüz ms sürebilir; UI thread'ini bekletmemek için arka planda.
                        var gpu = CudaAvailability.IsAvailable(cliPath);
                        FileLog.Write($"[TrayIconController] Whisper modeli değişti: {model.Path} (GPU={gpu})");
                        if (model.NeedsGpu && !gpu)
                        {
                            ShowNotification(
                                "Model Yavaş Olabilir",
                                "Kullanılabilir bir NVIDIA GPU bulunamadı. Large-v3 Turbo işlemcide çalışacak ve her dikte 20 saniyeden uzun sürebilir. Daha hızlı sonuç için Small modelini seçin.",
                                ToolTipIcon.Warning);
                        }
                    });
                };
                modelMenu.DropDownItems.Add(modelItem);
            }
            menu.Items.Add(modelMenu);

            menu.Items.Add(new ToolStripSeparator());

            // Son 10 Transkript Alt Menüsü
            var historyMenu = new ToolStripMenuItem("📋 Son 10 Transkript");
            var entries = _history.GetRecentEntries();

            if (entries.Count == 0)
            {
                var emptyItem = new ToolStripMenuItem("(Henüz bir transkript yok)") { Enabled = false };
                historyMenu.DropDownItems.Add(emptyItem);
            }
            else
            {
                foreach (var entry in entries)
                {
                    var prefix = entry.CleanedWithLlm ? "✨ " : "🎙️ ";
                    var itemTitle = $"{prefix}[{entry.Timestamp:HH:mm:ss}] {entry.ShortPreview}";
                    var subItem = new ToolStripMenuItem(itemTitle);
                    subItem.ToolTipText = "Panoya kopyalamak için tıklayın:\n" + entry.Text;
                    subItem.Click += (_, _) =>
                    {
                        try
                        {
                            Clipboard.SetText(entry.Text);
                            _notifyIcon.ShowBalloonTip(1500, "Panoya Kopyalandı", entry.ShortPreview, ToolTipIcon.Info);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TrayMenu] Pano kopyalama hatası: {ex.Message}");
                        }
                    };
                    historyMenu.DropDownItems.Add(subItem);
                }

                historyMenu.DropDownItems.Add(new ToolStripSeparator());
                var clearItem = new ToolStripMenuItem("Geçmişi Temizle");
                clearItem.Click += (_, _) => _history.Clear();
                historyMenu.DropDownItems.Add(clearItem);
            }
            menu.Items.Add(historyMenu);

            menu.Items.Add(new ToolStripSeparator());

            // Dikte Klasörünü Aç
            var openLogsItem = new ToolStripMenuItem("📁 Dikte Klasörünü Aç");
            openLogsItem.Click += (_, _) =>
            {
                try
                {
                    var dir = _configManager.Current.General.ResolvedLogDirectory;
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{dir}\"", UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Klasör açılamadı: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            menu.Items.Add(openLogsItem);

            // Ayarlar penceresi
            var openSettingsItem = new ToolStripMenuItem("⚙️ Ayarları Aç");
            openSettingsItem.Click += (_, _) => SettingsRequested?.Invoke();
            menu.Items.Add(openSettingsItem);

            // Özel Sözlüğü Düzenle. Kaydedilen değişiklikler bir sonraki diktede
            // kendiliğinden devreye girer; yeniden başlatmaya gerek yok.
            if (_dictionaryService != null)
            {
                var openDictionaryItem = new ToolStripMenuItem("📖 Özel Sözlüğü Aç");
                openDictionaryItem.Click += (_, _) =>
                {
                    try
                    {
                        // Dosya silinmişse varsayılanlarla yeniden oluşturulur; boş bir
                        // Not Defteri penceresi açılmasın.
                        _dictionaryService.Refresh();
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "notepad.exe",
                            Arguments = $"\"{_dictionaryService.DictionaryPath}\"",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Sözlük dosyası açılamadı: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };
                menu.Items.Add(openDictionaryItem);
            }

            menu.Items.Add(new ToolStripSeparator());

            // Çıkış
            var exitItem = new ToolStripMenuItem("❌ Çıkış");
            exitItem.Click += (_, _) =>
            {
                ExitRequested?.Invoke();
            };
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
        }

        public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            RunOnUiThread(() => _notifyIcon.ShowBalloonTip(2000, title, message, icon));
        }

        public void Dispose()
        {
            _history.HistoryChanged -= OnHistoryChanged;
            _notifyIcon.Visible = false;
            var currentIcon = _notifyIcon.Icon;
            _notifyIcon.Icon = null;
            currentIcon?.Dispose();
            _notifyIcon.Dispose();
        }
    }

    public static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float width, float height, float radius)
        {
            using var path = new GraphicsPath();
            path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
            path.AddArc(x + width - 2 * radius, y, radius * 2, radius * 2, 270, 90);
            path.AddArc(x + width - 2 * radius, y + height - 2 * radius, radius * 2, radius * 2, 0, 90);
            path.AddArc(x, y + height - 2 * radius, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }
}
