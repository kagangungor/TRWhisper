// Ayarlar penceresi icin gorsel duman testi.
//
// NEDEN VAR: "dotnet build" XAML'i BAML'e cevirir ama StaticResource cozumu,
// sablon PART_* eslemesi ve trigger'lar YALNIZCA calisma aninda patlar. Yani
// 0 hata ile derlenen bir XAML, acildiginda istisna firlatabilir. Bu arac
// gercek SettingsWindow'u ekran disinda acar, her sekmeyi PNG'ye basar ve
// sablon butunlugunu dogrular.
//
// Calistirma:
//   dotnet run --project tools\uismoke -- [cikti_klasoru]
// Cikis kodu 0 = temiz, 1 = sorun var (CI'da kullanilabilir).

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TRWhisper.Core.Config;
using TRWhisper.Core.Dictionary;
using TRWhisper.UI;

// Proje hem WPF hem WinForms kullaniyor; ortuk using'ler tip adlarini belirsiz birakiyor.
using Application = System.Windows.Application;
using RadioButton = System.Windows.Controls.RadioButton;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;
using ProgressBar = System.Windows.Controls.ProgressBar;
using ComboBox = System.Windows.Controls.ComboBox;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using FlowDirection = System.Windows.FlowDirection;
using Brushes = System.Windows.Media.Brushes;
using TextBox = System.Windows.Controls.TextBox;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace UiSmoke;

internal static class Program
{
    private static readonly List<string> Failures = new();
    private static readonly StringWriter BindingLog = new();
    private static string _outDir = "";

    [STAThread]
    private static int Main(string[] args)
    {
        _outDir = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "shots");
        Directory.CreateDirectory(_outDir);

        // Sessizce yutulan binding kirilmalari en sinsi XAML hatasi; dinleyiciyle yakala.
        PresentationTraceSources.Refresh();
        var src = PresentationTraceSources.DataBindingSource;
        src.Listeners.Add(new TextWriterTraceListener(BindingLog));
        src.Switch.Level = SourceLevels.Error | SourceLevels.Warning;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int rc = 0;
        app.Startup += (_, _) =>
        {
            try { Run(); }
            catch (Exception ex) { Failures.Add("ISTISNA: " + ex); }

            foreach (var b in BindingLog.ToString().Split('\n')
                         .Where(l => l.Contains("BindingExpression") &&
                                     l.Contains("error", StringComparison.OrdinalIgnoreCase))
                         .Take(10))
                Failures.Add("BINDING: " + b.Trim());

            if (Failures.Count == 0)
            {
                Console.WriteLine($"TUM KONTROLLER GECTI  (ekran goruntuleri: {_outDir})");
            }
            else
            {
                Console.WriteLine($"{Failures.Count} SORUN:");
                foreach (var f in Failures) Console.WriteLine("  - " + f);
                rc = 1;
            }
            app.Shutdown();
        };
        app.Run();
        return rc;
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) Failures.Add(what);
    }

    private static void Run()
    {
        // Gercek config'e dokunma: gecici bir klasorde calis.
        var tmp = Path.Combine(Path.GetTempPath(), "trwhisper_uismoke");
        Directory.CreateDirectory(tmp);
        var cfg = new ConfigManager(Path.Combine(tmp, "config.json"));
        var dict = new CustomDictionaryService(cfg);

        // Rotasyon uyarisinin gercekten ciktigini gorebilmek icin eskimis bir anahtar kur.
        // Once anahtar yazilir (Save damgayi "simdi" yapar), sonra damga geriye alinir.
        var seeded = cfg.Current;
        seeded.LlmCleaning.ApiKey = "sk-uismoke-sahte-anahtar";
        seeded.LlmCleaning.ApiKeyRotationReminderDays = 90;
        cfg.Save(seeded);
        seeded.LlmCleaning.ApiKeyUpdatedUtc = DateTime.UtcNow.AddDays(-200);
        cfg.Save(seeded);

        var w = new SettingsWindow(cfg, dict)
        {
            Left = -20000,
            Top = -20000,
            ShowInTaskbar = false,
            Width = 980,
            Height = 700
        };
        w.Show();
        Pump();

        var tabs = new[]
        {
            ("NavGeneral", "genel"), ("NavAudio", "ses"), ("NavModel", "model"),
            ("NavHotkey", "kisayol"), ("NavDictionary", "sozluk"), ("NavAi", "yapayzeka"),
            ("NavOverlay", "kapsul"), ("NavBackup", "yedekleme")
        };

        for (int i = 0; i < tabs.Length; i++)
        {
            var nav = (RadioButton)w.FindName(tabs[i].Item1);
            Check(nav != null, $"{tabs[i].Item1} bulunamadi");
            if (nav == null) continue;
            nav.IsChecked = true;
            Pump();
            Shoot(w, $"{i}-{tabs[i].Item2}.png");
        }

        CheckTemplates(w);
        CheckApiKeyReveal(w);
        CheckButtonWidths(w);
        CheckBackupPage(w);

        // API anahtari satiri yalnizca bulut saglayicida gorunur; ayrica goruntule.
        var provider = (ComboBox)w.FindName("LlmProviderCombo");
        if (provider != null && provider.Items.Count > 1)
        {
            ((RadioButton)w.FindName("NavAi")).IsChecked = true;
            provider.SelectedIndex = 1;
            Pump();
            Shoot(w, "5b-yapayzeka-apikey.png");

            // Anahtar yasam dongusu ve gunluk kota karti sayfanin altinda kaliyor.
            var scroll = (ScrollViewer)w.FindName("ContentScroll");
            var quotaCard = w.FindName("QuotaCard") as FrameworkElement;
            if (scroll != null && quotaCard != null)
            {
                quotaCard.BringIntoView();
                Pump();
                Shoot(w, "5c-yapayzeka-kota.png");
                CheckApiKeyRotation(w);
                scroll.ScrollToTop();
                Pump();
            }
        }

        // DIKKAT: duz Close() KULLANILAMAZ. Test alanlari degistirdigi icin pencere
        // "kaydedilmemis degisiklik var" MODAL'ini acar ve bassiz calismada sonsuza
        // kadar bekler. "Vazgec" yolu hem dogru kapanis hem de gecici config'i kirletmez.
        var cancelBtn = (Button)w.FindName("CancelButton");
        if (cancelBtn != null) cancelBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        else w.Close();
        Pump();
        Check(!w.IsVisible, "Vazgec pencereyi kapatmadi");
    }

    /// <summary>Eskimis anahtarda rotasyon rozetinin ciktigini ve metnin dolu oldugunu dogrular.</summary>
    private static void CheckApiKeyRotation(Window w)
    {
        var badge = w.FindName("ApiKeyRotationBadge") as Border;
        var text = w.FindName("ApiKeyRotationText") as TextBlock;
        var age = w.FindName("ApiKeyAgeText") as TextBlock;

        Check(badge != null, "ApiKeyRotationBadge bulunamadi");
        Check(badge?.Visibility == Visibility.Visible, "200 gunluk anahtarda rotasyon uyarisi cikmadi");
        Check(!string.IsNullOrWhiteSpace(text?.Text), "Rotasyon uyari metni bos");
        Check(age?.Text?.Contains("gun", StringComparison.OrdinalIgnoreCase) == true ||
              age?.Text?.Contains("gün", StringComparison.OrdinalIgnoreCase) == true,
              $"Anahtar yasi beklenmedik: '{age?.Text}'");

        Check(w.FindName("ClearApiKeyButton") is Button, "Anahtari Sil dugmesi yok");
        Check(w.FindName("OpenProviderConsoleButton") is Button, "Anahtar Panelini Ac dugmesi yok");
        Check(w.FindName("DailyLimitBox") is TextBox, "Gunluk tavan kutusu yok");
        Check(w.FindName("QuotaActionCombo") is ComboBox combo && combo.Items.Count == 2,
              "Tavan davranisi listesi eksik (Engelle / Yalnizca uyar)");
    }

    /// <summary>Yedekleme sekmesindeki denetimlerin yerinde oldugunu dogrular.</summary>
    private static void CheckBackupPage(Window w)
    {
        Check(w.FindName("AutoBackupToggle") is CheckBox, "Otomatik yedek anahtari yok");
        Check(w.FindName("BackupIntervalBox") is TextBox, "Yedekleme araligi kutusu yok");
        Check(w.FindName("BackupRetentionBox") is TextBox, "Saklanacak yedek kutusu yok");
        Check(w.FindName("IncludeTranscriptsToggle") is CheckBox, "Transkript dahil etme anahtari yok");
        Check(w.FindName("BackupDirBox") is TextBox, "Yedek klasoru kutusu yok");
        Check(w.FindName("CreateBackupButton") is Button, "Simdi Yedek Al dugmesi yok");
        Check(w.FindName("RestoreBackupButton") is Button, "Geri Yukle dugmesi yok");
        Check(w.FindName("BackupsListControl") is ItemsControl, "Yedek listesi yok");
    }

    /// <summary>Ozel ControlTemplate'lerin gercekten uygulandigini ve parcalarinin yerinde oldugunu dogrular.</summary>
    private static void CheckTemplates(Window w)
    {
        var toggle = (CheckBox)w.FindName("AutostartToggle");
        Check(toggle?.Template?.FindName("knob", toggle) is System.Windows.Shapes.Ellipse,
              "Toggle sablonunda 'knob' yok");
        Check(toggle?.Template?.FindName("focus", toggle) is Border,
              "Toggle sablonunda odak halkasi yok");

        var save = (Button)w.FindName("SaveButton");
        Check(save?.Template?.FindName("focus", save) is Border, "PrimaryButton odak halkasi yok");
        Check(save?.IsDefault == true, "Kaydet varsayilan dugme degil (Enter calismaz)");

        var cancel = (Button)w.FindName("CancelButton");
        Check(cancel != null, "Vazgec dugmesi bulunamadi");
        Check(cancel?.IsCancel == true, "Vazgec IsCancel degil (Esc calismaz)");
        Check((cancel?.Content as string) == "Vazgeç", $"Vazgec etiketi beklenmedik: '{cancel?.Content}'");

        var pb = (ProgressBar)w.FindName("ModelDownloadProgressBar");
        pb?.ApplyTemplate();
        Check(pb?.Template?.FindName("PART_Indicator", pb) != null, "ProgressBar PART_Indicator yok");
        Check(pb?.Template?.FindName("PART_Track", pb) != null, "ProgressBar PART_Track yok");

        var combo = (ComboBox)w.FindName("LlmProviderCombo");
        Check(combo?.Template?.FindName("arrow", combo) != null, "ComboBox ok isareti yok");
        if (combo != null)
        {
            combo.IsDropDownOpen = true;
            Pump();
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            var popupBorder = popup?.Child as Border;
            // TemplateBinding ActualWidth tuzagi: acilir liste kutudan dar kalmamali.
            Check(popupBorder != null && popupBorder.ActualWidth >= combo.ActualWidth - 1,
                  $"Acilir liste kutudan dar: {popupBorder?.ActualWidth} < {combo.ActualWidth}");
            combo.IsDropDownOpen = false;
            Pump();
        }

        var scroll = (ScrollViewer)w.FindName("ContentScroll");
        Check(scroll != null, "ContentScroll bulunamadi (sekme degisiminde basa sarma calismaz)");
        var sb = FindVisual<ScrollBar>(scroll);
        Check(sb != null, "Dikey kaydirma cubugu bulunamadi");
        if (sb != null)
        {
            sb.ApplyTemplate();
            var track = sb.Template.FindName("PART_Track", sb) as Track;
            Check(track?.Thumb != null, "ScrollBar thumb yok");
            Check(track?.DecreaseRepeatButton != null && track?.IncreaseRepeatButton != null,
                  "ScrollBar oluk dugmeleri yok (oluga tiklama calismaz)");
        }

        Check(CountVisual<System.Windows.Shapes.Path>(w) >= 3, "Vektor ikonlar cizilmemis");
    }

    /// <summary>API anahtari varsayilan olarak maskeli olmali; goster/gizle degeri kaybetmemeli.</summary>
    private static void CheckApiKeyReveal(Window w)
    {
        var pwd = (PasswordBox)w.FindName("LlmApiKeyPasswordBox");
        var txt = (TextBox)w.FindName("LlmApiKeyBox");
        var btn = (Button)w.FindName("ApiKeyRevealButton");
        if (pwd == null || txt == null || btn == null)
        {
            Failures.Add("API anahtari denetimleri bulunamadi");
            return;
        }

        Check(pwd.Visibility == Visibility.Visible, "API anahtari acilista maskeli degil");
        Check(txt.Visibility == Visibility.Collapsed, "API anahtari acik metin kutusu acilista gorunur");

        const string secret = "sk-test-1234567890";
        pwd.Password = secret;

        btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Pump();
        Check(txt.Visibility == Visibility.Visible && pwd.Visibility == Visibility.Collapsed,
              "Goster'e basilinca acik metin kutusu gorunmedi");
        Check(txt.Text == secret, $"Goster'de deger kayboldu: '{txt.Text}'");

        txt.Text = secret + "-duzenlendi";
        btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Pump();
        Check(pwd.Visibility == Visibility.Visible && txt.Visibility == Visibility.Collapsed,
              "Gizle'ye basilinca maskeli kutuya donulmedi");
        Check(pwd.Password == secret + "-duzenlendi",
              $"Gizle'de duzenleme kayboldu: '{pwd.Password}'");

        pwd.Password = "";
    }

    /// <summary>
    /// Sabit genislikli butonlarda metin tasmasi. DesiredSize KULLANILAMAZ:
    /// Margin'i icerir ve Width verilince olcum zaten kirpildigi icin tasma gorunmez.
    /// Metin dogrudan FormattedText ile olculur.
    /// </summary>
    private static void CheckButtonWidths(Window w)
    {
        foreach (var b in EnumerateVisual<Button>(w))
            CheckFits(b, b.Content as string);

        // Calisma aninda degisen etiketler de sigmali.
        CheckFits((Button)w.FindName("RecordPttButton"), "Tuşa Basın...");
        CheckFits((Button)w.FindName("RecordLlmButton"), "Tuşa Basın...");
    }

    private static void CheckFits(Button b, string text)
    {
        if (b == null || string.IsNullOrEmpty(text) || double.IsNaN(b.Width)) return;
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
                                   FlowDirection.LeftToRight,
                                   new Typeface(b.FontFamily, b.FontStyle, b.FontWeight, b.FontStretch),
                                   b.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(b).PixelsPerDip);
        double needed = ft.WidthIncludingTrailingWhitespace + b.Padding.Left + b.Padding.Right + 2;
        if (needed > b.Width + 0.5)
            Failures.Add($"Buton metni tasiyor: '{text}' {needed:F0}px yer istiyor, tanimli genislik {b.Width:F0}px");
    }

    private static void Shoot(Window w, string file)
    {
        var root = (FrameworkElement)w.Content;
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),
                                         (int)Math.Ceiling(root.ActualHeight),
                                         96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(_outDir, file));
        png.Save(fs);
    }

    private static void Pump()
    {
        for (int i = 0; i < 3; i++)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static T FindVisual<T>(DependencyObject root) where T : DependencyObject
        => EnumerateVisual<T>(root).FirstOrDefault();

    private static int CountVisual<T>(DependencyObject root) where T : DependencyObject
        => EnumerateVisual<T>(root).Count();

    private static IEnumerable<T> EnumerateVisual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null) yield break;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var sub in EnumerateVisual<T>(child)) yield return sub;
        }
    }
}
