using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TRWhisper.Core.Config;
using TRWhisper.Core.Dictionary;
using Xunit;
using Xunit.Abstractions;
using RadioButton = System.Windows.Controls.RadioButton;

namespace TRWhisper.Tests
{
    /// <summary>
    /// Ayarlar'ın hiçbir sayfasında bir öğe kabından taşıp kırpılmamalı. Özellikle %125/%150
    /// ölçekte tam piksele düşmeyen dolgular (ör. 14,11) kartın son satırını ~1 px kırpıyordu;
    /// sabit genişlikli satırlar da dar kartlardan taşabiliyordu. Test makinenin DPI'ıyla çalışır.
    /// </summary>
    [Collection(WpfWindowCollection.Name)]
    public class SettingsLayoutClipTests
    {
        private readonly ITestOutputHelper _out;
        public SettingsLayoutClipTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void NoPageClipsItsContent()
        {
            var report = new System.Text.StringBuilder();
            int clipped = 0;
            Exception? error = null;
            var t = new Thread(() =>
            {
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                    var root = Path.Combine(Path.GetTempPath(), $"trw_clip_{Guid.NewGuid():N}");
                    var logDir = Path.Combine(root, "Dictation");
                    Directory.CreateDirectory(logDir);
                    File.WriteAllText(Path.Combine(logDir, $"{DateTime.Now:yyyy-MM}.md"),
                        $"### 🕒 {DateTime.Now.AddMinutes(-5):yyyy-MM-dd HH:mm:ss}\n\nÖrnek.\n\n---\n\n");
                    var cm = new ConfigManager(Path.Combine(root, "config.json"));
                    cm.Current.General.LogDirectory = logDir;
                    var win = new TRWhisper.UI.SettingsWindow(cm, new CustomDictionaryService(cm))
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual, Left = -3000, Top = 0, ShowActivated = false
                    };
                    win.Show();
                    report.AppendLine($"DPI ölçeği: {VisualTreeHelper.GetDpi(win).DpiScaleY:0.##}");

                    void Pump(int ms)
                    {
                        var frame = new DispatcherFrame();
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                        timer.Start();
                        Dispatcher.PushFrame(frame);
                    }
                    RadioButton R(string n) => (RadioButton)win.FindName(n);

                    void Scan(string pageName)
                    {
                        Pump(300);
                        void Walk(DependencyObject d)
                        {
                            // Kaydırılabilir görünüm alanları bilerek kırpar.
                            if (d is System.Windows.Controls.ScrollContentPresenter) return;
                            if (d is FrameworkElement fe && fe.IsVisible)
                            {
                                var clip = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(fe);
                                if (clip != null && (clip.Bounds.Height + 0.05 < fe.ActualHeight || clip.Bounds.Width + 0.05 < fe.ActualWidth))
                                {
                                    clipped++;
                                    report.AppendLine($"  [{pageName}] {fe.GetType().Name} '{fe.Name}' boyut={fe.ActualWidth:0.##}x{fe.ActualHeight:0.##} görünen={clip.Bounds.Width:0.##}x{clip.Bounds.Height:0.##}");
                                }
                            }
                            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
                        }
                        Walk((DependencyObject)win.FindName(pageName));
                    }

                    R("NavAudio").IsChecked = true; Scan("PageAudio");
                    R("NavGeneral").IsChecked = true; Scan("PageGeneral");
                    R("NavModel").IsChecked = true; Scan("PageModel");
                    R("SubNavSecond").IsChecked = true; Scan("PageAi");
                    R("NavHotkey").IsChecked = true; Scan("PageHotkey");
                    R("NavDictionary").IsChecked = true; Scan("PageDictionary");
                    R("NavOverlay").IsChecked = true; Scan("PageOverlay");
                    R("NavBackup").IsChecked = true; Scan("PageBackup");
                    R("SubNavSecond").IsChecked = true; Pump(800); Scan("PageHistory");

                    var outDir = Environment.GetEnvironmentVariable("RENDER_OUT");
                    if (!string.IsNullOrEmpty(outDir))
                    {
                        // İsteğe bağlı görsel kontrol: RENDER_OUT verilirse Ses ve Kapsül sayfaları çizilir.
                        Directory.CreateDirectory(outDir);
                        foreach (var (nav, page) in new[] { ("NavAudio", "PageAudio"), ("NavOverlay", "PageOverlay") })
                        {
                            R(nav).IsChecked = true; Pump(300);
                            var p = (FrameworkElement)win.FindName(page);
                            var bmp = new RenderTargetBitmap((int)(p.ActualWidth * 1.5), (int)(p.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
                            var dv = new DrawingVisual();
                            using (var dc = dv.RenderOpen())
                            {
                                dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x0F, 0x11)), null, new Rect(0, 0, p.ActualWidth, p.ActualHeight));
                                dc.DrawRectangle(new VisualBrush(p) { Viewbox = new Rect(0, 0, p.ActualWidth, p.ActualHeight), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, p.ActualWidth, p.ActualHeight));
                            }
                            bmp.Render(dv);
                            var enc = new PngBitmapEncoder();
                            enc.Frames.Add(BitmapFrame.Create(bmp));
                            using var fs = File.Create(Path.Combine(outDir, page + ".png"));
                            enc.Save(fs);
                        }
                    }

                    win.AllowClose = true;
                    win.Close();
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(60)), "yerleşim testi takıldı");
            _out.WriteLine(report.ToString());
            Assert.Null(error);
            Assert.True(clipped == 0, "Kırpılan öğeler:\n" + report);
        }
    }
}
