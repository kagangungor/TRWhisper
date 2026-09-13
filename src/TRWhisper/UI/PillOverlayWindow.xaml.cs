using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

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

        private readonly Storyboard? _waveAnim;
        private readonly DispatcherTimer _autoHideTimer;
        private readonly DispatcherTimer _processingFailSafeTimer;
        private string _currentTranscript = string.Empty;

        public event Action? RequestCancel;
        public event Action? RequestFinish;

        public PillOverlayWindow()
        {
            InitializeComponent();

            _waveAnim = TryFindResource("WaveAnim") as Storyboard;

            _autoHideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _autoHideTimer.Tick += (_, _) =>
            {
                _autoHideTimer.Stop();
                HideWithFade();
            };

            // Emniyet sübabı: "Çözümleniyor..." durumunda beklenmedik bir durumda
            // pencerenin ekranda asılı kalmaması için 25 saniye sonra otomatik kapatılır.
            _processingFailSafeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(25)
            };
            _processingFailSafeTimer.Tick += (_, _) =>
            {
                _processingFailSafeTimer.Stop();
                HideWithFade();
            };

            Loaded += (_, _) => Reposition();
            SizeChanged += (_, _) => Reposition();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Pencerenin aktif uygulamadan odağı (focus) ÇALMASINI engelle (WS_EX_NOACTIVATE)
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        public void ShowListening()
        {
            Dispatcher.Invoke(() =>
            {
                _autoHideTimer.Stop();
                _processingFailSafeTimer.Stop();

                ListeningPanel.Visibility = Visibility.Visible;
                ResultPanel.Visibility = Visibility.Collapsed;

                StatusTextBlock.Text = "Dinleniyor";
                _waveAnim?.Begin(this, true);

                Show();
                Reposition();

                // Yumuşak açılış opaklık animasyonu
                var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180));
                BeginAnimation(OpacityProperty, fadeIn);
            });
        }

        public void ShowProcessing()
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsVisible) Show();

                ListeningPanel.Visibility = Visibility.Visible;
                ResultPanel.Visibility = Visibility.Collapsed;

                StatusTextBlock.Text = "Çözümleniyor...";
                Reposition();

                _processingFailSafeTimer.Stop();
                _processingFailSafeTimer.Start();
            });
        }

        public void ShowResult(string transcriptText, bool autoCopiedToClipboard = true)
        {
            Dispatcher.Invoke(() =>
            {
                _waveAnim?.Stop(this);
                _processingFailSafeTimer.Stop();
                _currentTranscript = transcriptText;

                ListeningPanel.Visibility = Visibility.Collapsed;
                ResultPanel.Visibility = Visibility.Visible;

                TranscriptTextBlock.Text = transcriptText;
                ResultInfoText.Text = autoCopiedToClipboard ? "Panoya kopyalandı ✓" : "";

                // Kopyala butonunu varsayılan haline getir
                var copyText = CopyButton.Template.FindName("CopyText", CopyButton) as System.Windows.Controls.TextBlock;
                if (copyText != null) copyText.Text = "Kopyala";

                if (!IsVisible) Show();
                Reposition();

                // 10 saniye sonra kendiliğinden kapanması için zamanlayıcı başlat
                _autoHideTimer.Stop();
                _autoHideTimer.Start();
            });
        }

        public void HideWithFade()
        {
            Dispatcher.Invoke(() =>
            {
                _autoHideTimer.Stop();
                _processingFailSafeTimer.Stop();
                _waveAnim?.Stop(this);

                var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (_, _) =>
                {
                    Hide();
                    Opacity = 1.0;
                };
                BeginAnimation(OpacityProperty, fadeOut);
            });
        }

        private void Reposition()
        {
            var workArea = SystemParameters.WorkArea;
            double targetWidth = ActualWidth > 0 ? ActualWidth : 340;
            double targetHeight = ActualHeight > 0 ? ActualHeight : 56;

            Left = workArea.Left + (workArea.Width - targetWidth) / 2;
            Top = workArea.Bottom - targetHeight - 40; // Ekranın altından 40px yukarıda yüzer
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

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentTranscript))
            {
                try
                {
                    System.Windows.Clipboard.SetText(_currentTranscript);
                    ResultInfoText.Text = "Panoya kopyalandı ✓";

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
                catch (Exception ex)
                {
                    Console.WriteLine($"[PillOverlay] Pano kopyalama hatası: {ex.Message}");
                }
            }
        }

        private void CloseResultButton_Click(object sender, RoutedEventArgs e)
        {
            HideWithFade();
        }
    }
}
