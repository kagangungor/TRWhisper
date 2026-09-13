using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace TRWhisper.Core.Native
{
    public class Win32KeyboardHook : IKeyboardHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private const int VK_RCONTROL = 0xA3;
        private const int VK_CONTROL = 0x11;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_SHIFT = 0x10;
        private const int LLKHF_EXTENDED = 0x01;

        /// <summary>
        /// ClipboardPaster'ın SendInput ile ürettiği sentetik tuş olaylarını işaretler.
        /// Hook bu imzayı taşıyan olayları yok sayar; böylece Ctrl+V simülasyonu
        /// hotkey mantığını yeniden tetikleyemez.
        /// </summary>
        internal static readonly UIntPtr InjectedInputSignature = new(0x54525748u); // 'TRWH'

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private IntPtr _hookId = IntPtr.Zero;
        private readonly LowLevelKeyboardProc _proc; // GC tarafından toplanmaması için referans tutulur
        private bool _isHotkeyHeld;
        private bool _isLlmModifierActive;
        private readonly object _lock = new();

        // Hook callback'i handler'lar üzerinde bloklanmasın diye olaylar ayrı bir
        // FIFO iş parçacığında dağıtılır (sıra korunur: Down her zaman Up'tan önce).
        private readonly BlockingCollection<Action> _dispatchQueue = new();
        private readonly Thread _dispatchThread;

        public event EventHandler<HotkeyEventArgs>? HotkeyDown;
        public event EventHandler<HotkeyEventArgs>? HotkeyUp;

        public bool IsHotkeyHeld
        {
            get
            {
                lock (_lock)
                {
                    return _isHotkeyHeld;
                }
            }
        }

        public Win32KeyboardHook()
        {
            _proc = HookCallback;
            _dispatchThread = new Thread(DispatchLoop)
            {
                IsBackground = true,
                Name = "TRWhisper.HotkeyDispatch"
            };
            _dispatchThread.Start();
        }

        private void DispatchLoop()
        {
            foreach (var action in _dispatchQueue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Win32KeyboardHook] Olay dağıtım hatası: {ex.Message}");
                }
            }
        }

        private void Dispatch(Action action)
        {
            try
            {
                if (!_dispatchQueue.IsAddingCompleted)
                {
                    _dispatchQueue.Add(action);
                }
            }
            catch (InvalidOperationException)
            {
                // Kapatma sırasındaki yarış: kuyruk kapanmış, olayı yut.
            }
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_hookId != IntPtr.Zero) return;

                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;

                if (curModule != null)
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                    if (_hookId == IntPtr.Zero)
                    {
                        var errorCode = Marshal.GetLastWin32Error();
                        Console.WriteLine($"[Win32KeyboardHook] Hook başlatılamadı. Hata kodu: {errorCode}");
                    }
                    else
                    {
                        Console.WriteLine("[Win32KeyboardHook] Düşük seviyeli klavye hook'u başarıyla kuruldu.");
                    }
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookId);
                    _hookId = IntPtr.Zero;
                    _isHotkeyHeld = false;
                    Console.WriteLine("[Win32KeyboardHook] Hook kaldırıldı.");
                }
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();

                // Kendi enjekte ettiğimiz Ctrl+V olaylarını yok say
                if (kbd.dwExtraInfo != InjectedInputSignature)
                {
                    bool isExtended = (kbd.flags & LLKHF_EXTENDED) != 0;
                    bool isRightCtrl = (kbd.vkCode == VK_RCONTROL) || (kbd.vkCode == VK_CONTROL && isExtended);

                    if (isRightCtrl)
                    {
                        if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                        {
                            bool shouldTrigger = false;
                            bool llmActive = false;
                            lock (_lock)
                            {
                                // Key-Repeat filtresi: Tuşa basılı tutulduğunda tekrarlanan mesajları yut
                                if (!_isHotkeyHeld)
                                {
                                    _isHotkeyHeld = true;
                                    // Sağ Shift veya herhangi bir Shift basılı mı kontrol et
                                    bool shiftHeld = (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0 ||
                                                     (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                                    _isLlmModifierActive = shiftHeld;
                                    llmActive = shiftHeld;
                                    shouldTrigger = true;
                                }
                            }

                            if (shouldTrigger)
                            {
                                Dispatch(() => HotkeyDown?.Invoke(this, new HotkeyEventArgs(llmActive)));
                            }
                        }
                        else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                        {
                            bool shouldTrigger = false;
                            bool wasLlm = false;

                            lock (_lock)
                            {
                                if (_isHotkeyHeld)
                                {
                                    _isHotkeyHeld = false;
                                    // LLM modifier'ını bırakma anında yeniden değerlendir: kullanıcı
                                    // Shift'i Ctrl'den sonra basmış olabilir (basış sırası bağımlılığını kaldırır).
                                    bool shiftHeld = (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0 ||
                                                     (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                                    wasLlm = _isLlmModifierActive || shiftHeld;
                                    shouldTrigger = true;
                                }
                            }

                            if (shouldTrigger)
                            {
                                Dispatch(() => HotkeyUp?.Invoke(this, new HotkeyEventArgs(wasLlm)));
                            }
                        }
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
            _dispatchQueue.CompleteAdding();
            GC.SuppressFinalize(this);
        }
    }
}
