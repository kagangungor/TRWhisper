using System;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using TRWhisper.Core;
using TRWhisper.Core.Audio;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;
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

        [STAThread]
        private static void Main()
        {
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

            var overlayWindow = new PillOverlayWindow();

            var configManager = new ConfigManager();
            var history = new TranscriptHistory();
            var logger = new MarkdownLogger(configManager);
            var llmCleaner = new LlmCleanerService(configManager);
            var trayController = new TrayIconController(configManager, history);

            IKeyboardHook keyboardHook = new Win32KeyboardHook();
            IAudioRecorder audioRecorder = new WasapiRecorder();
            ITranscriptionEngine transcriptionEngine = new WhisperCliRunner(configManager);
            IClipboardPaster clipboardPaster = new ClipboardPaster();

            using var coordinator = new DictationCoordinator(
                configManager,
                keyboardHook,
                audioRecorder,
                transcriptionEngine,
                llmCleaner,
                clipboardPaster,
                logger,
                history,
                trayController,
                overlayWindow);

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
            };

            coordinator.Start();

            // WPF Message Loop çalıştır
            wpfApp.Run();
            }
            catch (Exception ex)
            {
                FileLog.Write($"[Program] Fatal error in Main: {ex}");
                System.Windows.Forms.MessageBox.Show($"TRWhisper başlatılamadı: {ex.Message}", "TRWhisper Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
