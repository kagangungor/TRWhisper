using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TRWhisper.Core.Config;
using Xunit;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Brushes = System.Windows.Media.Brushes;
using ComboBox = System.Windows.Controls.ComboBox;
using FlowDirection = System.Windows.FlowDirection;

namespace TRWhisper.Tests
{
    /// <summary>
    /// Açılır listedeki seçeneklerde g/y/j gibi aşağı uzanan harfler kesilmemeli. Pencere
    /// Display yazı modu + yerleşim yuvarlaması kullanıyor; %125 ölçekte 13 pt satır kutusu
    /// 16,8'e iniyor ve harf uzantısı (~17,3) seçili satırda kırpılıyordu. Test makinenin
    /// DPI'ıyla çalışır: metin kutusu, yazı tipinin tam satır yüksekliğinden kısa olmamalı.
    /// </summary>
    [Collection(WpfWindowCollection.Name)]
    public class ComboBoxItemTextClipTests
    {
        [Fact]
        public void DropDownItems_LeaveRoomForDescenders()
        {
            Exception? error = null;
            var t = new System.Threading.Thread(() =>
            {
                var root = Path.Combine(Path.GetTempPath(), $"trw_comboclip_{Guid.NewGuid():N}");
                Directory.CreateDirectory(root);
                try
                {
                    System.Threading.SynchronizationContext.SetSynchronizationContext(
                        new System.Windows.Threading.DispatcherSynchronizationContext());
                    var cm = new ConfigManager(Path.Combine(root, "config.json"));
                    var win = new TRWhisper.UI.SettingsWindow(cm, new TRWhisper.Core.Dictionary.CustomDictionaryService(cm))
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual, Left = -3000, Top = 0, ShowActivated = false
                    };
                    win.Show();

                    var combo = (ComboBox)win.FindName("LanguageCombo");
                    combo.IsDropDownOpen = true;
                    win.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    var pixelsPerDip = VisualTreeHelper.GetDpi(win).PixelsPerDip;

                    Assert.True(combo.Items.Count > 0);
                    for (int i = 0; i < combo.Items.Count; i++)
                    {
                        var item = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(i);
                        var tb = FirstVisual<TextBlock>(item);
                        Assert.NotNull(tb);

                        var line = new FormattedText(tb!.Text, System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight, new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                            tb.FontSize, Brushes.White, pixelsPerDip);
                        Assert.True(tb.ActualHeight + 0.01 >= line.Height,
                            $"'{tb.Text}': metin kutusu {tb.ActualHeight:0.##}, satır {line.Height:0.##} — alt uzantılar kesilir.");
                    }

                    combo.IsDropDownOpen = false;
                    ((Button)win.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
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

        private static T? FirstVisual<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var found = FirstVisual<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
