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

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        // MOUSEINPUT hiç kullanılmıyor ama birliğin en büyük üyesi o; eksik olursa
        // Marshal.SizeOf<INPUT>() 64-bit'te 40 yerine 32 olur ve SendInput cbSize'ı
        // reddeder (ERROR_INVALID_PARAMETER, 0 olay gönderilir → hiç yapıştırılmaz).
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
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

        public async Task<bool> PasteTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // Önceki pano içeriği BİLEREK geri yüklenmiyor: Clipboard.GetDataObject() bir kopya
            // değil canlı OLE pano nesnesi döndürür; onu geri yazmak ~11 sn zaman aşımına
            // takılıp panoyu boşaltıyor ve kullanıcının Kopyala işlemini eziyordu.
            // Dikte metni panoda kalır.

            // Dikte edilen hedef pencereyi not al (yapıştırma öncesi tekrar kontrol edilir)
            var targetWindow = GetForegroundWindow();

            // 1. Metni panoya kopyala ve panonun gerçekten güncellendiğini doğrula
            if (!await SetTextAsync(text))
            {
                // Metnimiz panoya yazılamadı (başka uygulama panoyu kilitliyor).
                // Ctrl+V gönderirsek hedefe yanlış/eski içerik gider — iptal et.
                FileLog.Write("[ClipboardPaster] Pano güncellenemedi, yapıştırma iptal edildi.");
                return false;
            }

            // Kullanıcının fiziksel tuşları bıraktığından emin olmak için minik bir nefes payı
            await Task.Delay(25);

            if (targetWindow != IntPtr.Zero && GetForegroundWindow() != targetWindow)
            {
                FileLog.Write("[ClipboardPaster] Uyarı: ön plandaki pencere değişti, yapıştırma farklı bir hedefe gidebilir.");
            }

            // 2. Win32 SendInput ile Ctrl + V simüle et
            SendCtrlV();
            return true;
        }

        /// <summary>
        /// Metni kısa ömürlü bir STA thread'inde panoya yazar ve panonun gerçekten güncellendiğini
        /// (sequence number) doğrular. Otomatik yapıştırma ve pill'deki Kopyala butonu birlikte kullanır;
        /// çağıran thread'i (UI dahil) bloklamaz.
        /// </summary>
        public static async Task<bool> SetTextAsync(string text)
        {
            uint seqBefore = GetClipboardSequenceNumber();
            await RunOnStaThreadAsync(() =>
            {
                SafeSetClipboardText(text);
            });

            // Chromium/UWP tabanlı hedefler (VS Code, tarayıcı, Slack) panoyu asenkron
            // okur; Ctrl+V'den önce yazımın tamamlandığından emin ol
            // (normalde 0-10 ms, en fazla ~200 ms).
            for (int i = 0; i < 20; i++)
            {
                if (GetClipboardSequenceNumber() != seqBefore) return true;
                await Task.Delay(10);
            }
            return false;
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

        private static void SafeSetClipboardText(string text)
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
