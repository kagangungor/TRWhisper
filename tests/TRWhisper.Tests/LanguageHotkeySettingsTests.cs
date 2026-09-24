using System;
using System.IO;
using TRWhisper.Core.Config;
using Xunit;

namespace TRWhisper.Tests
{
    /// <summary>
    /// Ayarlar > Kısayol sekmesindeki hızlı dil geçişi kısayolu: yükleme, hızlı seçim ve kaydetme.
    /// </summary>
    [Collection(WpfWindowCollection.Name)]
    public class LanguageHotkeySettingsTests
    {
        [Fact]
        public void SettingsWindow_LanguageHotkey_LoadsPresetAndSaves()
        {
            Exception? error = null;
            var t = new System.Threading.Thread(() =>
            {
                var root = Path.Combine(Path.GetTempPath(), $"trw_langkey_{Guid.NewGuid():N}");
                Directory.CreateDirectory(root);
                try
                {
                    System.Threading.SynchronizationContext.SetSynchronizationContext(
                        new System.Windows.Threading.DispatcherSynchronizationContext());
                    var cm = new ConfigManager(Path.Combine(root, "config.json"));
                    cm.Current.General.FastSwitchHotkey = "Ctrl+Alt+L";
                    cm.Save(cm.Current);

                    var win = new TRWhisper.UI.SettingsWindow(cm, new TRWhisper.Core.Dictionary.CustomDictionaryService(cm))
                    {
                        WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                        Left = -3000, Top = 0, ShowActivated = false
                    };
                    win.Show();

                    var display = (System.Windows.Controls.TextBlock)win.FindName("LangKeyDisplayText");
                    Assert.Equal("Ctrl + Alt + L", display.Text);
                    var hint = (System.Windows.Controls.TextBlock)win.FindName("LanguageFastSwitchHint");
                    Assert.Contains("Ctrl + Alt + L", hint.Text);

                    // Hızlı seçim çipi: F7.
                    var page = (System.Windows.DependencyObject)win.FindName("PageHotkey");
                    var f7 = FindButtonByTag(page, "F7");
                    Assert.NotNull(f7);
                    f7!.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Assert.Equal("F7", display.Text);

                    // Kullanıcı yolu: Kaydet düğmesi (kaydedilmiş imzayı da tazeler; ApplyAndSave tek başına
                    // tazelemez ve kapanışta "kaydedilmemiş değişiklik" modalı testi sonsuza bekletir).
                    ((System.Windows.Controls.Button)win.FindName("SaveButton"))
                        .RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Assert.Equal("F7", new ConfigManager(Path.Combine(root, "config.json")).Current.General.FastSwitchHotkey);

                    win.AllowClose = true;
                    win.Close();
                }
                catch (Exception ex) { error = ex; }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
        }

        [Fact]
        public void SettingsWindow_FollowsLanguageChangedElsewhere_AndSaveKeepsIt()
        {
            Exception? error = null;
            var t = new System.Threading.Thread(() =>
            {
                var root = Path.Combine(Path.GetTempPath(), $"trw_langsync_{Guid.NewGuid():N}");
                Directory.CreateDirectory(root);
                try
                {
                    System.Threading.SynchronizationContext.SetSynchronizationContext(
                        new System.Windows.Threading.DispatcherSynchronizationContext());
                    var cm = new ConfigManager(Path.Combine(root, "config.json"));
                    cm.Current.General.Language = "tr";
                    cm.Save(cm.Current);

                    var win = new TRWhisper.UI.SettingsWindow(cm, new TRWhisper.Core.Dictionary.CustomDictionaryService(cm))
                    {
                        WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                        Left = -3000, Top = 0, ShowActivated = false
                    };
                    win.Show();
                    var combo = (System.Windows.Controls.ComboBox)win.FindName("LanguageCombo");
                    Assert.Equal("Türkçe", combo.SelectedItem?.ToString());

                    // Alt+L / tepsi yolu: koordinatör yapılandırmayı değiştirip kaydeder.
                    cm.Current.General.Language = "en";
                    cm.Save(cm.Current);
                    win.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    Assert.Equal("İngilizce", combo.SelectedItem?.ToString());

                    // Pencereden Kaydet eski dili geri yazmamalı.
                    ((System.Windows.Controls.Button)win.FindName("SaveButton"))
                        .RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Assert.Equal("en", new ConfigManager(Path.Combine(root, "config.json")).Current.General.Language);

                    win.AllowClose = true;
                    win.Close();
                }
                catch (Exception ex) { error = ex; }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
        }

        [Fact]
        public void SettingsWindow_FastSwitchLanguages_ExpandSelectKeepMinimumTwoAndSave()
        {
            Exception? error = null;
            var t = new System.Threading.Thread(() =>
            {
                var root = Path.Combine(Path.GetTempPath(), $"trw_langlist_{Guid.NewGuid():N}");
                Directory.CreateDirectory(root);
                try
                {
                    System.Threading.SynchronizationContext.SetSynchronizationContext(
                        new System.Windows.Threading.DispatcherSynchronizationContext());
                    var cm = new ConfigManager(Path.Combine(root, "config.json"));
                    cm.Current.General.EnableLanguageFastSwitch = true;
                    cm.Save(cm.Current);

                    var win = new TRWhisper.UI.SettingsWindow(cm, new TRWhisper.Core.Dictionary.CustomDictionaryService(cm))
                    {
                        WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                        Left = -3000, Top = 0, ShowActivated = false
                    };
                    win.Show();

                    var panel = (System.Windows.Controls.WrapPanel)win.FindName("FastSwitchLanguagesPanel");
                    var chips = panel.Children.OfType<System.Windows.Controls.CheckBox>()
                        .ToDictionary(c => (string)c.Tag);
                    Assert.Equal(new[] { "tr", "en", "de", "fr", "auto" }, chips.Keys.ToArray());

                    // Başlangıçta kapalı; ok düğmesiyle açılır.
                    Assert.False(panel.IsVisible);
                    ((System.Windows.Controls.Primitives.ToggleButton)win.FindName("FastSwitchLanguagesExpander")).IsChecked = true;
                    win.UpdateLayout();
                    Assert.True(panel.IsVisible);

                    // Varsayılan TR + EN; son iki dilden biri bırakılamaz.
                    Assert.True(chips["tr"].IsChecked == true && chips["en"].IsChecked == true);
                    chips["en"].IsChecked = false;
                    Assert.True(chips["en"].IsChecked == true);

                    chips["de"].IsChecked = true;
                    chips["auto"].IsChecked = true;
                    chips["tr"].IsChecked = false;   // artık üç dil kaldığı için bırakılabilir
                    Assert.False(chips["tr"].IsChecked == true);

                    // Yeni seçilen diller sona eklenir: TR, EN + DE + Auto − TR.
                    var summary = (System.Windows.Controls.TextBlock)win.FindName("FastSwitchLanguagesSummary");
                    Assert.Equal("Geçiş yapılacak diller: İngilizce → Almanca → Otomatik algıla", summary.Text);

                    // Oklarla sıralama: Auto'yu öne al → EN, Auto, DE; EN'i sona doğru al → Auto, EN, DE.
                    ClickOrderArrow(win, "auto", "Öne al");
                    Assert.Equal("Geçiş yapılacak diller: İngilizce → Otomatik algıla → Almanca", summary.Text);
                    ClickOrderArrow(win, "en", "Sona doğru al");
                    Assert.Equal("Geçiş yapılacak diller: Otomatik algıla → İngilizce → Almanca", summary.Text);

                    // Uçlardaki oklar pasif.
                    Assert.False(FindOrderArrow(win, "auto", "Öne al").IsEnabled);
                    Assert.False(FindOrderArrow(win, "de", "Sona doğru al").IsEnabled);

                    ((System.Windows.Controls.Button)win.FindName("SaveButton"))
                        .RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Assert.Equal(new[] { "auto", "en", "de" },
                        new ConfigManager(Path.Combine(root, "config.json")).Current.General.FastSwitchLanguages);

                    win.AllowClose = true;
                    win.Close();
                }
                catch (Exception ex) { error = ex; }
                finally
                {
                    try { Directory.Delete(root, true); } catch { }
                }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
        }

        private static System.Windows.Controls.Button FindOrderArrow(System.Windows.Window win, string code, string toolTip)
        {
            win.UpdateLayout();
            var list = (System.Windows.DependencyObject)win.FindName("FastSwitchOrderList");
            return FindVisual<System.Windows.Controls.Button>(list)
                .Single(b => b.Tag as string == code && b.ToolTip as string == toolTip);
        }

        private static void ClickOrderArrow(System.Windows.Window win, string code, string toolTip)
            => FindOrderArrow(win, code, toolTip)
                .RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        private static System.Collections.Generic.IEnumerable<T> FindVisual<T>(System.Windows.DependencyObject root)
            where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T t) yield return t;
                foreach (var sub in FindVisual<T>(child)) yield return sub;
            }
        }

        private static System.Windows.Controls.Button? FindButtonByTag(System.Windows.DependencyObject root, string tag)
        {
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root))
            {
                if (child is not System.Windows.DependencyObject d) continue;
                if (d is System.Windows.Controls.Button b && b.Tag as string == tag) return b;
                var found = FindButtonByTag(d, tag);
                if (found != null) return found;
            }
            return null;
        }
    }
}
