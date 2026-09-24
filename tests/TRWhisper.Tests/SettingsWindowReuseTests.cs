using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using System.Windows.Threading;
using TRWhisper.Core.Config;
using TRWhisper.Core.Dictionary;
using TRWhisper.UI;
using Xunit;
using Xunit.Abstractions;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace TRWhisper.Tests
{
    /// <summary>
    /// Ayarlar penceresi kapatılınca yok edilmez, gizlenir; yeniden açılış anında olur ve
    /// kaydedilmemiş düzenlemeleri geri getirmez. Uygulama kapanırken gerçekten kapanır.
    /// </summary>
    [Collection(WpfWindowCollection.Name)]
    public class SettingsWindowReuseTests
    {
        private readonly ITestOutputHelper _out;
        public SettingsWindowReuseTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void Close_Hides_ShowAgainDiscardsUnsavedEdits_AllowCloseReallyCloses()
        {
            Exception? error = null;
            var t = new Thread(() =>
            {
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                    var root = Path.Combine(Path.GetTempPath(), $"trw_reuse_{Guid.NewGuid():N}");
                    Directory.CreateDirectory(root);
                    var cm = new ConfigManager(Path.Combine(root, "config.json"));
                    cm.Current.General.EnableSpokenPunctuation = true;

                    var sw = Stopwatch.StartNew();
                    var win = new SettingsWindow(cm, new CustomDictionaryService(cm))
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual, Left = -3000, Top = 0, ShowActivated = false
                    };
                    win.Show();
                    var firstOpenMs = sw.ElapsedMilliseconds;
                    bool closed = false;
                    win.Closed += (_, _) => closed = true;

                    // Kaydetmeden bir ayarı değiştir, "Vazgeç" ile kapat → gizlenir.
                    var toggle = (CheckBox)win.FindName("chkSpokenPunctuation");
                    toggle.IsChecked = false;
                    ((Button)win.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Assert.False(win.IsVisible);
                    Assert.False(closed);

                    // Yeniden açılış: aynı nesne, ayar diskteki değere döner.
                    sw.Restart();
                    win.ShowAgain();
                    var reopenMs = sw.ElapsedMilliseconds;
                    Assert.True(win.IsVisible);
                    Assert.True(toggle.IsChecked);
                    _out.WriteLine($"ilk açılış {firstOpenMs} ms, yeniden açılış {reopenMs} ms");
                    Assert.True(reopenMs < firstOpenMs, $"yeniden açılış {reopenMs} ms, ilk {firstOpenMs} ms");

                    // Uygulama kapanışı: gizli ya da açık, pencere gerçekten kapanır ve soru sormaz.
                    toggle.IsChecked = false; // kaydedilmemiş değişiklik olsa bile gizliyken sorulmamalı
                    ((Button)win.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    win.AllowClose = true;
                    win.Close();
                    Assert.True(closed);
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(30)), "pencere testi takıldı (beklenmeyen bir iletişim kutusu olabilir)");
            Assert.Null(error);
        }
    }
}
