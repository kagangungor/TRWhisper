using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using TRWhisper.Core.Config;
using TRWhisper.Core.History;

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
        private readonly NotifyIcon _notifyIcon;
        private readonly ConfigManager _configManager;
        private readonly TranscriptHistory _history;

        private AppState _currentState = AppState.Idle;
        private bool _isLlmCleaningEnabled;

        public event Action<bool>? LlmCleaningToggled;
        public event Action? ExitRequested;

        public TrayIconController(ConfigManager configManager, TranscriptHistory history)
        {
            _configManager = configManager;
            _history = history;
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
            if (_currentState == state) return;
            _currentState = state;
            UpdateIcon(state);
            RebuildMenuSafe();
        }

        public void SetLlmCleaningMode(bool enabled)
        {
            _isLlmCleaningEnabled = enabled;
            RebuildMenuSafe();
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
                var icon = Icon.FromHandle(hIcon);

                _notifyIcon.Icon = icon;
                _notifyIcon.Text = statusText.Length > 63 ? statusText.Substring(0, 63) : statusText;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TrayIconController] İkon güncelleme hatası: {ex.Message}");
            }
        }

        private void OnHistoryChanged()
        {
            RebuildMenuSafe();
        }

        private void RebuildMenuSafe()
        {
            if (_notifyIcon.ContextMenuStrip != null && _notifyIcon.ContextMenuStrip.InvokeRequired)
            {
                _notifyIcon.ContextMenuStrip.BeginInvoke(new Action(BuildContextMenu));
            }
            else
            {
                BuildContextMenu();
            }
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            // Başlık / Durum
            string statusStr = _currentState switch
            {
                AppState.Recording => "🎙️ Kaydediyor... (Sağ Ctrl basılı)",
                AppState.Transcribing => "⚙️ Çözümlüyor...",
                _ => "🟢 Boşta (Sağ Ctrl basılı tutun)"
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
            var openLogsItem = new ToolStripMenuItem("📁 Dikte Klasörünü Aç (%USERPROFILE%\\Dictation)");
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

            // Ayarları Düzenle
            var openConfigItem = new ToolStripMenuItem("⚙️ Ayarları Aç (config.json)");
            openConfigItem.Click += (_, _) =>
            {
                try
                {
                    var path = _configManager.ConfigFilePath;
                    if (!File.Exists(path)) _configManager.Save(_configManager.Current);
                    Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ayar dosyası açılamadı: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            menu.Items.Add(openConfigItem);

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
            _notifyIcon.ShowBalloonTip(2000, title, message, icon);
        }

        public void Dispose()
        {
            _history.HistoryChanged -= OnHistoryChanged;
            _notifyIcon.Visible = false;
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
