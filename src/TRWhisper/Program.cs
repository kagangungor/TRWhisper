using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using TRWhisper.Core;
using TRWhisper.Core.Audio;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;
using TRWhisper.Core.Dictionary;
using TRWhisper.Core.Normalization;
using TRWhisper.Core.History;
using TRWhisper.Core.Llm;
using TRWhisper.Core.Native;
using TRWhisper.Core.Speech;
using TRWhisper.Core.Tray;
using TRWhisper.UI;

namespace TRWhisper
{
    internal static class Program
    {
        private const string MutexName = "Global\\TRWhisper_SingleInstance_Mutex";

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(int directoryFlags);

        private const int LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
        private const int LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        [STAThread]
        private static void Main()
        {
            AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromMilliseconds(250));

            try
            {
                // DLL Hijacking / Binary Planting koruması: DLL aramalarını sadece uygulama dizini ve System32 ile sınırla
                SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
            }
            catch
            {
                // Eski veya kısıtlı Windows ortamlarında akışı bozma
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                FileLog.Write($"[Program] UnhandledException: {e.ExceptionObject}");
            };

            try
            {
                FileLog.Write("[Program] TRWhisper Main başlatılıyor...");

                // Tekil oturum kontrolü (Aynı anda birden fazla örnek çalışmasını engelle)
                using var mutex = new Mutex(true, MutexName, out bool isNewInstance);
                FileLog.Write($"[Program] Mutex kontrolü: isNewInstance={isNewInstance}");
                if (!isNewInstance)
                {
                    FileLog.Write("[Program] Zaten çalışan bir TRWhisper örneği tespit edildi, çıkılıyor.");
                    System.Windows.Forms.MessageBox.Show(
                        "TRWhisper zaten arka planda çalışıyor! Sistem tepsisindeki (sağ alt) simgeyi kontrol edin.",
                        "TRWhisper",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            // WPF Application başlat (Overlay penceresi ve Dispatcher için)
            var wpfApp = new System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            var configManager = new ConfigManager();
            EnsureModelAvailable(configManager);

            var overlayWindow = new PillOverlayWindow();
            overlayWindow.AttachConfig(configManager);
            var dictionaryService = new CustomDictionaryService(configManager);
            var textNormalizer = new TurkishTextNormalizer(configManager);
            var history = new TranscriptHistory();
            var logger = new MarkdownLogger(configManager);
            var llmCleaner = new LlmCleanerService(configManager);
            var trayController = new TrayIconController(configManager, history, dictionaryService);

            IKeyboardHook keyboardHook = new Win32KeyboardHook(configManager);
            IAudioRecorder audioRecorder = new WasapiRecorder(configManager);
            // Modeli süreç içinde sıcak tutar; whisper-cli yolu WhisperCliRunner olarak duruyor.
            using var whisperEngine = new WhisperNetEngine(configManager, dictionaryService);
            ITranscriptionEngine transcriptionEngine = whisperEngine;
            IClipboardPaster clipboardPaster = new ClipboardPaster(configManager);
            IForegroundAppDetector appDetector = new ForegroundAppDetector();

            using var coordinator = new DictationCoordinator(
                configManager,
                keyboardHook,
                audioRecorder,
                transcriptionEngine,
                llmCleaner,
                clipboardPaster,
                dictionaryService,
                textNormalizer,
                logger,
                history,
                trayController,
                overlayWindow,
                appDetector);

            // Tepsiden Ayarlar: pencere tekilddir, açıksa öne getirilir.
            UI.SettingsWindow? settingsWindow = null;
            trayController.SettingsRequested += () =>
            {
                wpfApp.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (settingsWindow != null)
                        {
                            if (settingsWindow.WindowState == WindowState.Minimized)
                                settingsWindow.WindowState = WindowState.Normal;
                            settingsWindow.Activate();
                            settingsWindow.Topmost = true;
                            settingsWindow.Topmost = false;
                            return;
                        }

                        // Model yolu kaydetme anında karşılaştırılır: her "Kaydet"te modeli
                        // boşaltıp yeniden yüklemek gereksiz ~2.5 sn + VRAM dalgalanması demek.
                        var modelPathBefore = configManager.Current.Whisper.ModelPath;

                        settingsWindow = new UI.SettingsWindow(configManager, dictionaryService, applied =>
                        {
                            // Canlı uygulama: yeniden başlatmaya gerek kalmadan servisleri güncelle.
                            trayController.SetLlmCleaningMode(applied.LlmCleaning.EnabledByDefault);

                            if (!string.Equals(applied.Whisper.ModelPath, modelPathBefore, StringComparison.OrdinalIgnoreCase))
                            {
                                modelPathBefore = applied.Whisper.ModelPath;
                                _ = Task.Run(() => whisperEngine.SwitchModelAsync(applied.Whisper.ModelPath));
                            }

                            overlayWindow.ApplyOverlaySettings();
                            keyboardHook.ReloadConfig();
                        }, llmCleaner, overlayWindow);
                        settingsWindow.Closed += (_, _) => settingsWindow = null;
                        settingsWindow.Show();
                        settingsWindow.Activate();
                    }
                    catch (Exception ex)
                    {
                        FileLog.Write($"[Program] Ayarlar penceresi açılamadı: {ex}");
                        System.Windows.Forms.MessageBox.Show(
                            "Ayarlar penceresi açılamadı: " + ex.Message, "TRWhisper",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Error);
                    }
                });
            };

            trayController.ExitRequested += () =>
            {
                coordinator.Stop();
                wpfApp.Dispatcher.Invoke(() =>
                {
                    overlayWindow.Close();
                    wpfApp.Shutdown();
                });
                System.Windows.Forms.Application.Exit();
            };

            trayController.LlmCleaningToggled += (enabled) =>
            {
                var cfg = configManager.Current;
                cfg.LlmCleaning.EnabledByDefault = enabled;
                configManager.Save(cfg);
            };

            trayController.WhisperModelChanged += (modelPath) =>
            {
                var cfg = configManager.Current;
                cfg.Whisper.ModelPath = modelPath;
                configManager.Save(cfg);
                // Eski modeli bellekten at, yenisini hemen yükle ki ilk dikte de hızlı olsun.
                _ = Task.Run(() => whisperEngine.SwitchModelAsync(modelPath));
            };

            FileLog.Write("[Program] Servisler başlatıldı, coordinator.Start() çağrılıyor...");
            coordinator.Start();

            // Modeli arka planda ısıt: açılıştan sonraki ilk dikte de model yükleme
            // bedelini (~1-3 sn) ödemesin. Başarısız olursa dikte yine çalışır,
            // yalnızca ilk çağrıda model yüklenir.
            _ = Task.Run(() => whisperEngine.WarmUpAsync());

            // WPF Message Loop çalıştır
            FileLog.Write("[Program] wpfApp.Run() başlatılıyor...");
            wpfApp.Run();
            FileLog.Write("[Program] wpfApp.Run() sonlandı, uygulama kapanıyor.");
            }
            catch (Exception ex)
            {
                FileLog.Write($"[Program] Fatal error in Main: {ex}");
                System.Windows.Forms.MessageBox.Show($"TRWhisper başlatılamadı: {ex.Message}", "TRWhisper Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Yapılandırmadaki model dosyası yoksa (kurulumda seçilmemiş ya da kullanıcı silmiş)
        /// kurulu olan ilk modele geçer; aksi hâlde dikte sessizce boş sonuç döndürür.
        /// </summary>
        private static void EnsureModelAvailable(ConfigManager configManager)
        {
            var cfg = configManager.Current;
            if (System.IO.File.Exists(cfg.Whisper.ResolvedModelPath))
                return;

            foreach (var model in TrayIconController.WhisperModels)
            {
                if (!System.IO.File.Exists(WhisperConfig.ResolvePath(model.Path)))
                    continue;

                FileLog.Write($"[Program] Model bulunamadı ({cfg.Whisper.ModelPath}), kurulu modele geçiliyor: {model.Path}");
                cfg.Whisper.ModelPath = model.Path;
                configManager.Save(cfg);
                return;
            }

            FileLog.Write($"[Program] UYARI: Hiçbir whisper modeli bulunamadı ({cfg.Whisper.ModelPath}).");
        }
    }
}
