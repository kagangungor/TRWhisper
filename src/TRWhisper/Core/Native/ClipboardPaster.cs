using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Native
{
    public class ClipboardPaster : IClipboardPaster
    {
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_V = 0x56;
        private const ushort VK_RCONTROL = 0xA3;

        // Ctrl+V öncesi serbest bırakılacak modifier'lar: (sanal tuş kodu, extended mı)
        private static readonly (ushort Vk, bool Extended)[] ModifierKeys =
        {
            (0xA0, false), // VK_LSHIFT
            (0xA1, false), // VK_RSHIFT
            (0xA2, false), // VK_LCONTROL
            (0xA3, true),  // VK_RCONTROL
            (0xA4, false), // VK_LMENU (Alt)
            (0xA5, true),  // VK_RMENU (AltGr)
            (0x5B, true),  // VK_LWIN
            (0x5C, true),  // VK_RWIN
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public async Task PasteTextAsync(string text, int restoreDelayMs = 150)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 1. O anki panoyu yedekle (Retry mekanizması ile)
            IDataObject? originalData = null;
            await RunOnStaThreadAsync(() =>
            {
                originalData = SafeGetClipboardData();
            });

            try
            {
                // Dikte edilen hedef pencereyi not al (yapıştırma öncesi tekrar kontrol edilir)
                var targetWindow = GetForegroundWindow();

                // 2. Metni panoya kopyala ve panonun gerçekten güncellendiğini doğrula
                uint seqBefore = GetClipboardSequenceNumber();
                await RunOnStaThreadAsync(() =>
                {
                    SafeSetClipboardText(text);
                });

                // Chromium/UWP tabanlı hedefler (VS Code, tarayıcı, Slack) panoyu asenkron
                // okur; Ctrl+V'den önce yazımın tamamlandığından emin ol
                // (normalde 0-10 ms, en fazla ~200 ms).
                bool clipboardUpdated = false;
                for (int i = 0; i < 20; i++)
                {
                    if (GetClipboardSequenceNumber() != seqBefore) { clipboardUpdated = true; break; }
                    await Task.Delay(10);
                }

                if (!clipboardUpdated)
                {
                    // Metnimiz panoya yazılamadı (başka uygulama panoyu kilitliyor).
                    // Ctrl+V gönderirsek hedefe yanlış/eski içerik gider — iptal et.
                    FileLog.Write("[ClipboardPaster] Pano güncellenemedi, yapıştırma iptal edildi.");
                    return;
                }

                // Kullanıcının fiziksel tuşları bıraktığından emin olmak için minik bir nefes payı
                await Task.Delay(25);

                if (targetWindow != IntPtr.Zero && GetForegroundWindow() != targetWindow)
                {
                    FileLog.Write("[ClipboardPaster] Uyarı: ön plandaki pencere değişti, yapıştırma farklı bir hedefe gidebilir.");
                }

                // 3. Win32 SendInput ile Ctrl + V simüle et
                SendCtrlV();

                // 4. Hedef uygulamanın (Notepad, Word, tarayıcı vb.) panodan veriyi okuması için bekle
                await Task.Delay(restoreDelayMs);
            }
            finally
            {
                // 5. Orijinal panoyu geri yükle
                if (originalData != null)
                {
                    await RunOnStaThreadAsync(() =>
                    {
                        SafeRestoreClipboardData(originalData);
                    });
                }
            }
        }

        private void SendCtrlV()
        {
            // Tüm enjekte olaylar imzalanır: kendi klavye hook'umuz bunları yok sayar
            var sig = Win32KeyboardHook.InjectedInputSignature;
            var inputs = new List<INPUT>(12);

            // Kullanıcının fiziksel olarak basılı tuttuğu modifier'ları serbest bırak.
            // Aksi halde simüle edilen Ctrl+V, hedef pencerede Ctrl+Shift+V (Word "Özel Yapıştır"),
            // Win+V (pano geçmişi) veya Ctrl+Alt+V gibi istenmeyen kombinasyonlara dönüşür.
            foreach (var (vk, extended) in ModifierKeys)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                {
                    uint flags = KEYEVENTF_KEYUP | (extended ? KEYEVENTF_EXTENDEDKEY : 0);
                    inputs.Add(MakeKeyInput(vk, flags, sig));
                }
            }

            // Temiz Ctrl+V dizisi
            inputs.Add(MakeKeyInput(VK_CONTROL, 0, sig));
            inputs.Add(MakeKeyInput(VK_V, 0, sig));
            inputs.Add(MakeKeyInput(VK_V, KEYEVENTF_KEYUP, sig));
            inputs.Add(MakeKeyInput(VK_CONTROL, KEYEVENTF_KEYUP, sig));

            var arr = inputs.ToArray();
            var sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
            if (sent != arr.Length)
            {
                FileLog.Write($"[ClipboardPaster] SendInput kısmi gönderim: {sent}/{arr.Length}, hata: {Marshal.GetLastWin32Error()}");
            }
        }

        private static INPUT MakeKeyInput(ushort vk, uint flags, UIntPtr sig) => new()
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk, dwFlags = flags, dwExtraInfo = sig }
            }
        };

        private IDataObject? SafeGetClipboardData()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    return Clipboard.GetDataObject();
                }
                catch (ExternalException)
                {
                    Thread.Sleep(20);
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[ClipboardPaster] Pano okuma hatası: {ex.Message}");
                    break;
                }
            }
            return null;
        }

        private void SafeSetClipboardText(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    return;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(20);
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[ClipboardPaster] Pano yazma hatası: {ex.Message}");
                    break;
                }
            }
        }

        private void SafeRestoreClipboardData(IDataObject data)
        {
            // Office uygulamaları (Word/Excel) yapıştırma sonrası panoyu daha uzun süre
            // açık tutar; geri yükleme için daha geniş bir retry penceresi kullan.
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    Clipboard.SetDataObject(data, true);
                    return;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(30);
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[ClipboardPaster] Pano geri yükleme hatası: {ex.Message}");
                    break;
                }
            }
        }

        // Pano OLE API'leri STA + kısa ömür gerektirir. Her pano işlemi kendi STA
        // thread'inde çalışır ve iş biter bitmez thread kapanır; böylece apartment
        // temiz teardown olur. (Kalıcı, mesaj pompalamayan bir STA thread, yapıştırma
        // sırasında hedef uygulamanın OLE clipboard okumasıyla deadlock'a girer.)
        private static Task RunOnStaThreadAsync(Action action)
        {
            var tcs = new TaskCompletionSource();
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            })
            {
                IsBackground = true,
                Name = "TRWhisper.ClipboardSta"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }
    }
}
