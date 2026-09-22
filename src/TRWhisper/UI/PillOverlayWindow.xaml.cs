using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Threading;
using TRWhisper.Core.Audio;
using TRWhisper.Core.Diagnostics;
using TRWhisper.Core.Config;
using TRWhisper.Core.Llm;
using TRWhisper.Core.Native;

namespace TRWhisper.UI
{
    public partial class PillOverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly TimeSpan DefaultHideDelay = TimeSpan.FromSeconds(10);
        // Yönetici pencerede kullanıcının elle Ctrl+V yapması gerekiyor; okuyup uygulaması için daha uzun.
        private static readonly TimeSpan ManualPasteHideDelay = TimeSpan.FromSeconds(20);

        private static readonly System.Windows.Media.Brush DefaultInfoBrush = FrozenBrush(0x71, 0x71, 0x7A);
        private static readonly System.Windows.Media.Brush ManualPasteBrush = FrozenBrush(0xFB, 0xBF, 0x24);
        private static readonly System.Windows.Media.Brush DefaultStatusBrush = FrozenBrush(0xCC, 0xCC, 0xCC);
        private static readonly System.Windows.Media.Brush LivePreviewBrush = FrozenBrush(0xF4, 0xF4, 0xF5);
        private static readonly System.Windows.Media.Brush WarningStatusBrush = FrozenBrush(0xFB, 0xBF, 0x24);

        private const double MinBarHeight = 6.0;
        private const double MaxBarHeight = 28.0;
        private static readonly double[] BarMultipliers = [0.45, 0.75, 1.0, 0.75, 0.45];
        private const float LowAudioThreshold = 0.015f;
        private static readonly TimeSpan LowAudioCheckDelay = TimeSpan.FromSeconds(2.5);

        private static System.Windows.Media.Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private ConfigManager? _configManager;

        private readonly DispatcherTimer _waveformTimer;
        private IAudioRecorder? _activeRecorder;
        private readonly double[] _currentBarHeights = [MinBarHeight, MinBarHeight, MinBarHeight, MinBarHeight, MinBarHeight];
        private Rectangle[]? _bars;
        private DateTime _recordingStartTime;
        private double _levelSum;
        private int _levelSampleCount;
        private bool _isLowAudioWarningActive;
        private bool _initialLowAudioCheckDone;
        private string _normalStatusText = "Dinleniyor";
        private uint _tickCount;

        private readonly DispatcherTimer _autoHideTimer;
        private readonly DispatcherTimer _processingFailSafeTimer;
        private string _currentTranscript = string.Empty;
        private string _rawTranscript = string.Empty;
        private string _cleanedTranscript = string.Empty;
        private bool _isShowingCleaned = true;

        private bool _isPositioningMode;
        private double _prePositioningLeft;
        private double _prePositioningTop;
        private Action<double, double>? _onPositionSaved;
        private Action? _onPositionReset;
        private Action? _onPositionCancelled;

        public event Action? RequestCancel;
        public event Action? RequestFinish;

        public bool IsPositioningMode => _isPositioningMode;

        public PillOverlayWindow()
        {
            InitializeComponent();

            _waveformTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _waveformTimer.Tick += WaveformTimer_Tick;

            _autoHideTimer = new DispatcherTimer
            {
                Interval = DefaultHideDelay
            };
            _autoHideTimer.Tick += (_, _) =>
            {
                _autoHideTimer.Stop();
                HideWithFade();
            };

            // Emniyet sübabı: "Çözümleniyor..." durumunda pencerenin ekranda asılı kalmaması için.
            // Normalde coordinator pill'i kendisi kapatır; süre ShowProcessing çağrısında
            // coordinator'ın watchdog süresinin biraz üstüne ayarlanır (yavaş CPU'da erken kapanmasın).
            _processingFailSafeTimer = new DispatcherTimer();
            _processingFailSafeTimer.Tick += (_, _) =>
            {
                _processingFailSafeTimer.Stop();
                HideWithFade();
            };

            Loaded += (_, _) =>
            {
                _bars ??= [Bar1, Bar2, Bar3, Bar4, Bar5];
                Reposition();
            };
            SizeChanged += (_, _) => Reposition();
        }

        /// <summary>
        /// Yapılandırmayı bağlar. Pencere ctor'da parametre almadığı için (XAML) ayrı verilir;
        /// bağlanmazsa varsayılanlarla (alt konum, 10 sn) çalışır.
        /// </summary>
        public void AttachConfig(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        /// <summary>
        /// Ayarlar değiştikten sonra kapsülü yeniden konumlandırır. Süre ayarı zaten her
        /// gösterimde okunduğu için burada ek iş gerekmez.
        /// </summary>
        public void ApplyOverlaySettings()
        {
            Dispatcher.Invoke(Reposition);
        }

        private TimeSpan ResultHideDelay
        {
            get
            {
                var seconds = _configManager?.Current.Overlay.ResultDurationSeconds ?? 0;
                return seconds > 0 ? TimeSpan.FromSeconds(seconds) : DefaultHideDelay;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Pencerenin aktif uygulamadan odağı (focus) ÇALMASINI engelle (WS_EX_NOACTIVATE)
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        public void StartAudioReactiveWaveform(IAudioRecorder audioRecorder)
        {
            Dispatcher.Invoke(() =>
            {
                _activeRecorder = audioRecorder;
                _recordingStartTime = DateTime.UtcNow;
                _levelSum = 0;
                _levelSampleCount = 0;
                _isLowAudioWarningActive = false;
                _initialLowAudioCheckDone = false;
                _normalStatusText = StatusTextBlock.Text;
                StatusTextBlock.Foreground = DefaultStatusBrush;
                _tickCount = 0;

                _bars ??= [Bar1, Bar2, Bar3, Bar4, Bar5];
                for (int i = 0; i < 5; i++)
                {
                    _currentBarHeights[i] = MinBarHeight;
                    _bars[i].Height = MinBarHeight;
                }

                _waveformTimer.Start();
            });
        }

        public void StopAudioReactiveWaveform()
        {
            Dispatcher.Invoke(() =>
            {
                _waveformTimer.Stop();
                _activeRecorder = null;
                _isLowAudioWarningActive = false;
                _initialLowAudioCheckDone = false;

                StatusTextBlock.Foreground = DefaultStatusBrush;

                _bars ??= [Bar1, Bar2, Bar3, Bar4, Bar5];
                for (int i = 0; i < 5; i++)
                {
                    _currentBarHeights[i] = MinBarHeight;
                    _bars[i].Height = MinBarHeight;
                }
            });
        }

        private void WaveformTimer_Tick(object? sender, EventArgs e)
        {
            if (_activeRecorder == null || !_activeRecorder.IsRecording)
            {
                StopAudioReactiveWaveform();
                return;
            }

            _tickCount++;
            float level = _activeRecorder.CurrentLevel;

            // 1. Zarf Takipçisi (Envelope Follower) ve Doğal Dalga Hareketi
            // Konuşma heceleri ve ses vurgularını belirgin kılmak için dinamik kontrast ve hassasiyet artırıldı:
            // 0.008 altı sessizlik/dip gürültüsü; konuşma enerjisi heceler arası farkı açacak şekilde ölçeklenir:
            double speechEnergy = level > 0.008f ? Math.Clamp(Math.Pow(level * 4.2, 0.75), 0.0, 1.0) : 0.0;

            // Çubukların tek bir blok gibi değil, organik ve akıcı bir ses dalgası gibi dalgalanmasını sağla:
            double wavePhase = _tickCount * 0.22;

            _bars ??= [Bar1, Bar2, Bar3, Bar4, Bar5];
            for (int i = 0; i < 5; i++)
            {
                // Heceler arası canlı dalgalanma (sinüzoidal faz farkı):
                double ripple = 0.72 + 0.28 * Math.Sin(wavePhase + i * 1.3);
                double effectiveMultiplier = BarMultipliers[i] * ripple;

                double target = MinBarHeight + (MaxBarHeight - MinBarHeight) * speechEnergy * effectiveMultiplier;
                double current = _currentBarHeights[i];

                // Hızlı yükseliş (Attack): heceler geldiğinde çubuklar anında sıçrasın
                // Çevik düşüş (Decay): kelimeler ve heceler arasındaki nefes/duraklamalarda çubuklar hızla insin
                // Böylece konuşma boyunca çubuklar yüksek bir platoda asılı kalmaz, net şekilde inip çıkar:
                double nextHeight = target > current
                    ? (target * 0.70 + current * 0.30)
                    : (target * 0.28 + current * 0.72);

                _currentBarHeights[i] = nextHeight;
                _bars[i].Height = nextHeight;
            }

            // 2. Kısık Ses / Yanlış Mikrofon Tespiti
            var elapsed = DateTime.UtcNow - _recordingStartTime;
            if (elapsed < LowAudioCheckDelay)
            {
                _levelSum += level;
                _levelSampleCount++;
            }
            else if (!_initialLowAudioCheckDone)
            {
                double avgLevel = _levelSampleCount > 0 ? _levelSum / _levelSampleCount : 0.0;
                if (avgLevel < LowAudioThreshold && level < LowAudioThreshold)
                {
                    _isLowAudioWarningActive = true;
                    StatusTextBlock.Text = "Ses çok kısık...";
                    StatusTextBlock.Foreground = WarningStatusBrush;
                }
                _initialLowAudioCheckDone = true;
            }
            else if (_isLowAudioWarningActive && level >= 0.02f)
            {
                // Ses normale döndüğünde uyarıyı kaldır ve önceki metne geri dön
                _isLowAudioWarningActive = false;
                StatusTextBlock.Text = _normalStatusText;
                StatusTextBlock.Foreground = DefaultStatusBrush;
            }
        }

        /// <param name="statusOverride">
        /// null ise "Dinleniyor" yazar. Toggle modunda kullanıcı tuşu bıraktıktan sonra da
        /// kaydın sürdüğünü bilmeli, bu yüzden farklı bir metin verilebilir.
        /// </param>
        private static string FormatModeBadgeText(LlmMode mode, string? detectedAppName)
        {
            return string.IsNullOrWhiteSpace(detectedAppName)
                ? mode.Name
                : $"{mode.Name} · {detectedAppName}";
        }

        public void ShowListening(string? statusOverride = null, LlmMode? activeMode = null, string? detectedAppName = null)
        {
            Dispatcher.Invoke(() =>
            {
                _autoHideTimer.Stop();
                _processingFailSafeTimer.Stop();

                ListeningPanel.Visibility = Visibility.Visible;
                ResultPanel.Visibility = Visibility.Collapsed;

                if (activeMode != null)
                {
                    ListeningModeBadge.Visibility = Visibility.Visible;
                    ListeningModeIcon.Text = activeMode.Icon;
                    ListeningModeName.Text = FormatModeBadgeText(activeMode, detectedAppName);
                }
                else
                {
                    ListeningModeBadge.Visibility = Visibility.Collapsed;
                }

                _normalStatusText = statusOverride ?? "Dinleniyor";
                StatusTextBlock.Text = _normalStatusText;
                StatusTextBlock.Foreground = DefaultStatusBrush;

                BeginAnimation(OpacityProperty, null);
                Opacity = 1.0;
                Show();
                Reposition();

                // Yumuşak açılış opaklık animasyonu
                var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180));
                BeginAnimation(OpacityProperty, fadeIn);
            });
        }

        public void ShowProcessing(TimeSpan failSafeTimeout, LlmMode? activeMode = null, string? detectedAppName = null)
        {
            Dispatcher.Invoke(() =>
            {
                StopAudioReactiveWaveform();

                if (!IsVisible) Show();

                ListeningPanel.Visibility = Visibility.Visible;
                ResultPanel.Visibility = Visibility.Collapsed;

                if (activeMode != null)
                {
                    ListeningModeBadge.Visibility = Visibility.Visible;
                    ListeningModeIcon.Text = activeMode.Icon;
                    ListeningModeName.Text = FormatModeBadgeText(activeMode, detectedAppName);
                }
                else
                {
                    ListeningModeBadge.Visibility = Visibility.Collapsed;
                }

                StatusTextBlock.Text = "Çözümleniyor...";
                Reposition();

                _processingFailSafeTimer.Stop();
                _processingFailSafeTimer.Interval = failSafeTimeout;
                _processingFailSafeTimer.Start();
            });
        }

        /// <summary>
        /// Canlı akış (real-time live preview) transkripsiyon önizlemesini günceller.
        /// Dinleme panelinde StatusTextBlock metnini canlı konuşmayla yeniler.
        /// </summary>
        public void UpdateLivePreview(string previewText)
        {
            if (string.IsNullOrWhiteSpace(previewText)) return;

            Dispatcher.Invoke(() =>
            {
                // Yalnızca dinleme panelindeyken güncelle; processing veya result ekranına geçildiyse dokunma
                if (ListeningPanel.Visibility != Visibility.Visible || _activeRecorder == null) return;

                // Kısık ses uyarısı aktifse kullanıcıyı uyarmaya devam etsin
                if (_isLowAudioWarningActive) return;

                StatusTextBlock.Text = previewText.Trim();
                StatusTextBlock.Foreground = LivePreviewBrush;
                Reposition();
            });
        }

        public void ShowResult(string transcriptText, PasteResult pasteResult, string? rawTranscript = null, LlmMode? activeMode = null, string? detectedAppName = null)
        {
            Dispatcher.Invoke(() =>
            {
                StopAudioReactiveWaveform();
                _processingFailSafeTimer.Stop();
                _currentTranscript = transcriptText;
                _cleanedTranscript = transcriptText;
                _rawTranscript = rawTranscript ?? string.Empty;
                _isShowingCleaned = true;

                ListeningPanel.Visibility = Visibility.Collapsed;
                ResultPanel.Visibility = Visibility.Visible;

                TranscriptTextBlock.Text = transcriptText;

                if (activeMode != null && !string.IsNullOrWhiteSpace(_rawTranscript))
                {
                    ResultModeBadge.Visibility = Visibility.Visible;
                    ResultModeIcon.Text = activeMode.Icon;
                    ResultModeName.Text = FormatModeBadgeText(activeMode, detectedAppName);
                }
                else
                {
                    ResultModeBadge.Visibility = Visibility.Collapsed;
                }

                // Eğer LLM temizliği yapılmışsa ve orijinal metin farklıysa geçiş butonunu göster
                bool hasAlternative = !string.IsNullOrWhiteSpace(_rawTranscript) &&
                                      !string.Equals(_rawTranscript.Trim(), transcriptText.Trim(), StringComparison.Ordinal);

                if (hasAlternative)
                {
                    ToggleTranscriptVersionButton.Visibility = Visibility.Visible;
                    UpdateToggleVersionButtonText("Orijinali Gör");
                }
                else
                {
                    ToggleTranscriptVersionButton.Visibility = Visibility.Collapsed;
                }

                bool needsManualPaste = pasteResult == PasteResult.ElevatedTargetCopiedOnly;
                ResultInfoText.Text = pasteResult switch
                {
                    PasteResult.Pasted => "Panoya kopyalandı ✓",
                    PasteResult.PastedClipboardRestored => "Yapıştırıldı ✓",
                    PasteResult.DirectTyped => "Doğrudan yazıldı ✓",
                    PasteResult.ElevatedTargetCopiedOnly => "Yönetici pencere: Panoya kopyalandı, Ctrl+V yapın",
                    _ => ""
                };

                // Elle yapıştırma gerekiyorsa uyarı rengiyle öne çıkar ve okumaya zaman bırak.
                ResultInfoText.Foreground = needsManualPaste ? ManualPasteBrush : DefaultInfoBrush;
                ResultInfoText.FontWeight = needsManualPaste ? FontWeights.SemiBold : FontWeights.Normal;

                // Kopyala butonunu varsayılan haline getir
                var copyText = CopyButton.Template.FindName("CopyText", CopyButton) as System.Windows.Controls.TextBlock;
                if (copyText != null) copyText.Text = "Kopyala";

                if (!IsVisible) Show();
                Reposition();

                // Süre dolunca kendiliğinden kapanması için zamanlayıcı başlat
                _autoHideTimer.Stop();
                _autoHideTimer.Interval = needsManualPaste ? ManualPasteHideDelay : ResultHideDelay;
                _autoHideTimer.Start();
            });
        }

        private async void ToggleTranscriptVersion_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_rawTranscript) || string.IsNullOrEmpty(_cleanedTranscript))
                return;

            _isShowingCleaned = !_isShowingCleaned;
            _currentTranscript = _isShowingCleaned ? _cleanedTranscript : _rawTranscript;
            TranscriptTextBlock.Text = _currentTranscript;

            UpdateToggleVersionButtonText(_isShowingCleaned ? "Orijinali Gör" : "Cilalıyı Gör");

            // Kullanıcının seçtiği versiyonu anında panoya kopyala
            if (await ClipboardPaster.SetTextAsync(_currentTranscript))
            {
                ResultInfoText.Text = _isShowingCleaned ? "Cilalı metin kopyalandı ✓" : "Orijinal metin kopyalandı ✓";
                ResultInfoText.Foreground = DefaultInfoBrush;
                ResultInfoText.FontWeight = FontWeights.Normal;
            }
        }

        private void UpdateToggleVersionButtonText(string text)
        {
            if (ToggleTranscriptVersionButton.Template.FindName("ToggleText", ToggleTranscriptVersionButton) is System.Windows.Controls.TextBlock tb)
            {
                tb.Text = text;
            }
        }

        public void HideWithFade()
        {
            Dispatcher.Invoke(() =>
            {
                _autoHideTimer.Stop();
                _processingFailSafeTimer.Stop();
                StopAudioReactiveWaveform();

                var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (_, _) =>
                {
                    BeginAnimation(OpacityProperty, null);
                    Opacity = 1.0;
                    Hide();
                };
                BeginAnimation(OpacityProperty, fadeOut);
            });
        }

        private void Reposition()
        {
            if (_isPositioningMode) return;

            var workArea = SystemParameters.WorkArea;
            double targetWidth = ActualWidth > 0 ? ActualWidth : 460;
            double targetHeight = ActualHeight > 0 ? ActualHeight : 56;

            var overlay = _configManager?.Current.Overlay;
            var position = overlay?.Position;

            if (string.Equals(position, "Custom", StringComparison.OrdinalIgnoreCase) &&
                overlay?.CustomX.HasValue == true && overlay?.CustomY.HasValue == true)
            {
                Left = Math.Clamp(overlay.CustomX.Value, workArea.Left, Math.Max(workArea.Left, workArea.Right - targetWidth));
                Top = Math.Clamp(overlay.CustomY.Value, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - targetHeight));
            }
            else if (string.Equals(position, "Top", StringComparison.OrdinalIgnoreCase))
            {
                Left = workArea.Left + (workArea.Width - targetWidth) / 2;
                Top = workArea.Top + 40;
            }
            else
            {
                // Varsayılan: Ekranın altı (Bottom)
                Left = workArea.Left + (workArea.Width - targetWidth) / 2;
                Top = workArea.Bottom - targetHeight - 40;
            }
        }

        public void ShowPositioningMode(Action<double, double> onSave, Action onResetDefault, Action? onCancel = null)
        {
            Dispatcher.Invoke(() =>
            {
                _autoHideTimer.Stop();
                _processingFailSafeTimer.Stop();
                StopAudioReactiveWaveform();

                _isPositioningMode = true;
                _onPositionSaved = onSave;
                _onPositionReset = onResetDefault;
                _onPositionCancelled = onCancel;

                _prePositioningLeft = Left;
                _prePositioningTop = Top;

                ListeningPanel.Visibility = Visibility.Collapsed;
                ResultPanel.Visibility = Visibility.Collapsed;
                PositioningPanel.Visibility = Visibility.Visible;

                PillBorder.Cursor = System.Windows.Input.Cursors.SizeAll;

                UpdateCoordsDisplay();

                BeginAnimation(OpacityProperty, null);
                Opacity = 1.0;
                Topmost = true;
                Show();
                Activate();
            });
        }

        public void ExitPositioningMode()
        {
            _isPositioningMode = false;
            _onPositionSaved = null;
            _onPositionReset = null;
            _onPositionCancelled = null;

            PillBorder.Cursor = System.Windows.Input.Cursors.Arrow;
            PositioningPanel.Visibility = Visibility.Collapsed;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1.0;
            Hide();
        }

        private void UpdateCoordsDisplay()
        {
            if (PositionCoordsText != null)
            {
                PositionCoordsText.Text = $"X: {Math.Round(Left)}, Y: {Math.Round(Top)}";
            }
        }

        private void PillBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isPositioningMode && e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
                ClampToScreen();
                UpdateCoordsDisplay();
            }
        }

        private void ClampToScreen()
        {
            var workArea = SystemParameters.WorkArea;
            double targetWidth = ActualWidth > 0 ? ActualWidth : 460;
            double targetHeight = ActualHeight > 0 ? ActualHeight : 56;

            Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - targetWidth));
            Top = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - targetHeight));
        }

        private void ResetPositionButton_Click(object sender, RoutedEventArgs e)
        {
            var workArea = SystemParameters.WorkArea;
            double targetWidth = ActualWidth > 0 ? ActualWidth : 460;
            double targetHeight = ActualHeight > 0 ? ActualHeight : 56;

            Left = workArea.Left + (workArea.Width - targetWidth) / 2;
            Top = workArea.Bottom - targetHeight - 40;
            UpdateCoordsDisplay();

            _onPositionReset?.Invoke();
        }

        private void SavePositionButton_Click(object sender, RoutedEventArgs e)
        {
            ClampToScreen();
            var savedX = Left;
            var savedTop = Top;
            var callback = _onPositionSaved;
            ExitPositioningMode();
            callback?.Invoke(savedX, savedTop);
        }

        private void CancelPositionButton_Click(object sender, RoutedEventArgs e)
        {
            Left = _prePositioningLeft;
            Top = _prePositioningTop;
            var callback = _onPositionCancelled;
            ExitPositioningMode();
            callback?.Invoke();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            HideWithFade();
            RequestCancel?.Invoke();
        }

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            RequestFinish?.Invoke();
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentTranscript))
            {
                // WPF Clipboard.SetText UI thread'inde panoyu iki kez açar (yazma + flush); başka bir
                // uygulama panoyu kısa süre kilitlediğinde yeniden dener ya da takılır ve pill donar.
                // Otomatik yapıştırmanın STA + doğrulama yolunu arka planda kullan.
                if (await ClipboardPaster.SetTextAsync(_currentTranscript))
                {
                    // Kullanıcının bilerek kopyaladığı metin Win+V geçmişine girsin; otomatik
                    // yapıştırmadaki geçici metinden farklı olarak burada dışlama uygulanmaz.
                    ResultInfoText.Text = "Panoya kopyalandı ✓";
                    ResultInfoText.Foreground = DefaultInfoBrush;
                    ResultInfoText.FontWeight = FontWeights.Normal;

                    var copyText = CopyButton.Template.FindName("CopyText", CopyButton) as System.Windows.Controls.TextBlock;
                    if (copyText != null) copyText.Text = "Kopyalandı! ✓";

                    // 2 saniye sonra otomatik kapat
                    var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    closeTimer.Tick += (_, _) =>
                    {
                        closeTimer.Stop();
                        HideWithFade();
                    };
                    closeTimer.Start();
                }
                else
                {
                    FileLog.Write("[PillOverlay] Kopyala: pano güncellenemedi (başka bir uygulama panoyu kilitliyor olabilir).");
                    ResultInfoText.Text = "Kopyalanamadı, tekrar deneyin";
                }
            }
        }

        private void CloseResultButton_Click(object sender, RoutedEventArgs e)
        {
            HideWithFade();
        }
    }
}
