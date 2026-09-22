using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Native
{
    public class Win32KeyboardHook : IKeyboardHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;

        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

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

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;   // üst 16 bit: hangi XBUTTON
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        private IntPtr _hookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;
        private readonly LowLevelKeyboardProc _proc;      // GC tarafından toplanmaması için referans tutulur
        private readonly LowLevelKeyboardProc _mouseProc; // aynı imza; lParam farklı yorumlanır
        private bool _isHotkeyHeld;
        private bool _isLlmModifierActive;
        private readonly object _lock = new();

        private readonly ConfigManager? _configManager;

        // Hook callback'i ~300 ms bütçeyle çalışır; her olayda yapılandırma okumak yerine
        // çözümlenmiş değerler burada tutulur ve ConfigChanged'de tazelenir.
        private volatile HotkeyBinding _pushToTalk = HotkeyBinding.Parse("RightCtrl");
        private volatile HotkeyBinding _llmModifier = HotkeyBinding.Parse("Shift");

        public void ReloadConfig()
        {
            lock (_lock)
            {
                _isHotkeyHeld = false;
                ApplyConfig();
            }
        }

        private void ApplyConfig()
        {
            var cfg = _configManager?.Current.Hotkey;
            var ptt = cfg?.PushToTalkKey ?? "RightCtrl";
            var llm = cfg?.LlmModifierKey ?? "Shift";

            _pushToTalk = HotkeyBinding.Parse(ptt, "RightCtrl");
            _llmModifier = HotkeyBinding.Parse(llm, "Shift");
            FileLog.Write($"[Win32KeyboardHook] Kısayollar güncellendi: PTT='{_pushToTalk.DisplayName}' ({ptt}), LLM='{_llmModifier.DisplayName}' ({llm})");
        }

        private bool IsLlmModifierHeld()
        {
            return _llmModifier.IsActiveNow();
        }

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

        public Win32KeyboardHook(ConfigManager? configManager = null)
        {
            _configManager = configManager;
            ApplyConfig();
            if (_configManager != null) _configManager.ConfigChanged += _ => ApplyConfig();

            _proc = HookCallback;
            _mouseProc = MouseHookCallback;
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
                    var moduleHandle = GetModuleHandle(curModule.ModuleName);

                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);
                    if (_hookId == IntPtr.Zero)
                    {
                        FileLog.Write($"[Win32KeyboardHook] Klavye hook'u kurulamadı. Hata: {Marshal.GetLastWin32Error()}");
                    }

                    // Fare hook'u KOŞULSUZ kurulur. Yalnızca Mouse4/Mouse5 seçiliyken kurmak
                    // daha az yük olurdu ama hook'lar kuruldukları (mesaj döngüsü olan) iş
                    // parçacığına bağlıdır; ayar değişince oraya iş marshal etmek gerekirdi.
                    // Geri dönüş: XBUTTON dışındaki her mesajda callback anında çıkar.
                    _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
                    if (_mouseHookId == IntPtr.Zero)
                    {
                        FileLog.Write($"[Win32KeyboardHook] Fare hook'u kurulamadı. Hata: {Marshal.GetLastWin32Error()}");
                    }

                    FileLog.Write($"[Win32KeyboardHook] Hook'lar kuruldu (klavye={_hookId != IntPtr.Zero}, fare={_mouseHookId != IntPtr.Zero}).");
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
                }

                if (_mouseHookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_mouseHookId);
                    _mouseHookId = IntPtr.Zero;
                }

                _isHotkeyHeld = false;
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
                    var binding = _pushToTalk;
                    bool isExtended = (kbd.flags & LLKHF_EXTENDED) != 0;
                    bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                    if (isDown && binding.MatchesKey(kbd.vkCode, isExtended))
                    {
                        RaiseDown();
                        if (binding.Suppress) return (IntPtr)1;
                    }
                    else if (isUp && _isHotkeyHeld)
                    {
                        // Bas-konuş tuşu veya kombinasyondaki bir tuş bırakıldıysa bitir
                        if (binding.MatchesKey(kbd.vkCode, isExtended) ||
                            (binding.PrimaryVk != 0 && kbd.vkCode == (uint)binding.PrimaryVk) ||
                            (binding.GenericVk != 0 && kbd.vkCode == (uint)binding.GenericVk) ||
                            (binding.Modifiers.HasFlag(HotkeyModifiers.Ctrl) && (kbd.vkCode == 0x11 || kbd.vkCode == 0xA2 || kbd.vkCode == 0xA3)) ||
                            (binding.Modifiers.HasFlag(HotkeyModifiers.Alt) && (kbd.vkCode == 0x12 || kbd.vkCode == 0xA4 || kbd.vkCode == 0xA5)) ||
                            (binding.Modifiers.HasFlag(HotkeyModifiers.Shift) && (kbd.vkCode == 0x10 || kbd.vkCode == 0xA0 || kbd.vkCode == 0xA1)) ||
                            (binding.Modifiers.HasFlag(HotkeyModifiers.Win) && (kbd.vkCode == 0x5B || kbd.vkCode == 0x5C)))
                        {
                            RaiseUp();
                            if (binding.Suppress) return (IntPtr)1;
                        }
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        /// <summary>
        /// Fare yan tuşları (Mouse4/Mouse5). Bu geri çağrı HER fare olayında çalışır —
        /// hareket dâhil — bu yüzden ilgilenmediğimiz mesajlarda hiçbir iş yapmadan çıkar.
        /// </summary>
        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP)
                {
                    var binding = _pushToTalk;
                    if (binding.IsMouse)
                    {
                        var mouse = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                        int button = (int)((mouse.mouseData >> 16) & 0xFFFF);

                        if (button == binding.MouseButton)
                        {
                            if (msg == WM_XBUTTONDOWN && binding.MatchesMouse(button))
                            {
                                RaiseDown();
                                if (binding.Suppress) return (IntPtr)1;
                            }
                            else if (msg == WM_XBUTTONUP && _isHotkeyHeld)
                            {
                                RaiseUp();
                                if (binding.Suppress) return (IntPtr)1;
                            }
                        }
                    }
                }
            }

            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        /// <summary>
        /// Kısayola basılma olayı. Tekrar (key-repeat) filtrelenir: tuş basılı tutulurken
        /// Windows aynı mesajı sürekli gönderir, yalnızca ilki dikteyi başlatmalı.
        /// </summary>
        private void RaiseDown()
        {
            bool shouldTrigger = false;
            bool llmActive = false;

            lock (_lock)
            {
                if (!_isHotkeyHeld)
                {
                    _isHotkeyHeld = true;
                    bool modifierHeld = IsLlmModifierHeld();
                    _isLlmModifierActive = modifierHeld;
                    llmActive = modifierHeld;
                    shouldTrigger = true;
                }
            }

            if (shouldTrigger)
                Dispatch(() => HotkeyDown?.Invoke(this, new HotkeyEventArgs(llmActive)));
        }

        private void RaiseUp()
        {
            bool shouldTrigger = false;
            bool wasLlm = false;

            lock (_lock)
            {
                if (_isHotkeyHeld)
                {
                    _isHotkeyHeld = false;
                    // LLM tuşunu bırakma anında yeniden değerlendir: kullanıcı onu
                    // kısayoldan SONRA basmış olabilir (basış sırası bağımlılığını kaldırır).
                    wasLlm = _isLlmModifierActive || IsLlmModifierHeld();
                    shouldTrigger = true;
                }
            }

            if (shouldTrigger)
                Dispatch(() => HotkeyUp?.Invoke(this, new HotkeyEventArgs(wasLlm)));
        }

        public void Dispose()
        {
            Stop();
            _dispatchQueue.CompleteAdding();
            GC.SuppressFinalize(this);
        }
    }
}
