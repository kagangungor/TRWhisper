using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Native
{
    public class ClipboardPaster : IClipboardPaster
    {
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_V = 0x56;
        private const ushort VK_RETURN = 0x0D;

        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenElevation = 20;

        // Pano anlık görüntüsü / geri yükleme üst sınırları. OLE pano çağrıları başka bir
        // uygulama yüzünden takılabilir (bkz. 2026-09-17 ~11 sn vakası); süre aşılırsa
        // geri yüklemeden vazgeçilir, dikte akışı asla bloklanmaz.
        private const int ClipboardCaptureTimeoutMs = 700;
        private const int ClipboardRestoreTimeoutMs = 1500;

        // DirectType'ta tek SendInput çağrısına sığdırılacak karakter sayısı; aradaki minik
        // gecikme yavaş hedeflerin olay kuyruğunu taşırmasını önler.
        private const int DirectTypeChunkChars = 200;

        private readonly ConfigManager _configManager;

        public ClipboardPaster(ConfigManager configManager)
        {
            _configManager = configManager;
        }

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

        // TRWhisper'ın kendisi yükseltilmişse UIPI bizi engellemez; süreç ömrü boyunca sabit.
        private static readonly Lazy<bool> SelfElevated = new(() =>
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[ClipboardPaster] Kendi yükseltilme durumu okunamadı: {ex.Message}");
                return false;
            }
        });

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

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_ELEVATION
        {
            public int TokenIsElevated;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        private static string GetWindowClassName(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return string.Empty;
            var sb = new System.Text.StringBuilder(256);
            return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr TokenHandle,
            int TokenInformationClass,
            out TOKEN_ELEVATION TokenInformation,
            int TokenInformationLength,
            out int ReturnLength);

        // ---------------------------------------------------------------- yapıştırma akışı

        public async Task<PasteResult> PasteTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return PasteResult.Failed;

            var paste = _configManager.Current.Paste;

            // Dikte edilen hedef pencereyi not al (yapıştırma öncesi tekrar kontrol edilir)
            var targetWindow = GetForegroundWindow();

            // Konsolda satır sonu Enter'dır: metin tek satıra indirilir ki hiçbir komut
            // kullanıcı onaylamadan çalışmasın. Yükseltilmiş (yönetici) konsola panodan elle
            // yapıştırılacak metin için de geçerli, bu yüzden UIPI dalından önce yapılır.
            if (ConsolePasteGuard.IsConsoleWindowClass(GetWindowClassName(targetWindow)))
            {
                var singleLine = ConsolePasteGuard.ToSingleLine(text);
                if (!string.Equals(singleLine, text, StringComparison.Ordinal))
                {
                    FileLog.Write("[ClipboardPaster] Hedef bir konsol: satır sonları ve denetim karakterleri kaldırıldı.");
                    text = singleLine;
                }
                if (string.IsNullOrEmpty(text)) return PasteResult.Failed;
            }

            bool blockedByUipi = !SelfElevated.Value && IsWindowElevated(targetWindow);

            // UIPI: standart kullanıcıdan yükseltilmiş pencereye giden SendInput sessizce yutulur.
            // Ctrl+V de DirectType da işe yaramaz — metni panoda bırak, kullanıcı elle yapıştırsın.
            if (blockedByUipi)
            {
                FileLog.Write("[ClipboardPaster] Hedef pencere yükseltilmiş (UIPI): SendInput gönderilmeyecek, metin panoda bırakılıyor.");
                if (!await SetTextAsync(text).ConfigureAwait(false))
                {
                    FileLog.Write("[ClipboardPaster] Pano güncellenemedi (yükseltilmiş hedef).");
                    return PasteResult.Failed;
                }
                return PasteResult.ElevatedTargetCopiedOnly;
            }

            if (string.Equals(paste.PasteMode, "DirectType", StringComparison.OrdinalIgnoreCase))
            {
                await DirectTypeAsync(text).ConfigureAwait(false);
                return PasteResult.DirectTyped;
            }

            // Kullanıcının mevcut pano içeriğini (metin / dosya / bitmap) hafızaya al.
            // Canlı OLE nesnesi TUTULMAZ; formatlar kopyalanır, böylece geri yazarken
            // kaynak uygulamanın yanıt vermesine bağımlı kalmayız.
            ClipboardSnapshot? snapshot = paste.RestoreClipboard
                ? await CaptureClipboardAsync().ConfigureAwait(false)
                : null;

            // 1. Metni panoya kopyala ve panonun gerçekten güncellendiğini doğrula.
            //    Geçici dikte metni Win+V pano geçmişine girmemeli.
            if (!await SetTextAsync(text, excludeFromClipboardHistory: true).ConfigureAwait(false))
            {
                // Metnimiz panoya yazılamadı (başka uygulama panoyu kilitliyor).
                // Ctrl+V gönderirsek hedefe yanlış/eski içerik gider — iptal et.
                // Panoya dokunamadığımız için geri yüklenecek bir şey de yok.
                FileLog.Write("[ClipboardPaster] Pano güncellenemedi, yapıştırma iptal edildi.");
                snapshot?.Dispose();
                return PasteResult.Failed;
            }

            // Kullanıcının fiziksel tuşları bıraktığından emin olmak için minik bir nefes payı
            await Task.Delay(25).ConfigureAwait(false);

            if (targetWindow != IntPtr.Zero && GetForegroundWindow() != targetWindow)
            {
                FileLog.Write("[ClipboardPaster] Uyarı: ön plandaki pencere değişti, yapıştırma farklı bir hedefe gidebilir.");
            }

            // 2. Win32 SendInput ile Ctrl + V simüle et
            SendCtrlV();

            // 3. Hedef uygulamanın panoyu okuması için bekle, sonra eski içeriği geri yükle.
            if (snapshot == null) return PasteResult.Pasted;

            try
            {
                var delay = paste.RestoreDelayMs;
                if (delay < 0) delay = 0;
                await Task.Delay(delay).ConfigureAwait(false);

                // Geri yükleme başarısızsa dikte metni panoda kaldı; kullanıcıya doğru bilgiyi ver.
                return await RestoreClipboardAsync(snapshot).ConfigureAwait(false)
                    ? PasteResult.PastedClipboardRestored
                    : PasteResult.Pasted;
            }
            finally
            {
                snapshot.Dispose();
            }
        }

        // ---------------------------------------------------------------- yükseltilmiş pencere tespiti

        public bool IsForegroundWindowElevated() => IsWindowElevated(GetForegroundWindow());

        /// <summary>
        /// Pencerenin sahibi sürecin yükseltilmiş olup olmadığını token'ından okur.
        /// Süreç hiç açılamıyorsa (erişim reddi) bu da yükseltilmişlik göstergesidir.
        /// </summary>
        private static bool IsWindowElevated(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            if (GetWindowThreadProcessId(hWnd, out uint pid) == 0 || pid == 0) return false;

            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero)
            {
                // Aynı kullanıcının yükseltilmiş süreçleri normalde sorgulanabilir; açılamıyorsa
                // hedef ya yükseltilmiş ya da başka bir oturumda. İkisinde de SendInput geçmez.
                FileLog.Write($"[ClipboardPaster] Hedef süreç açılamadı (pid={pid}, hata={Marshal.GetLastWin32Error()}), yükseltilmiş kabul ediliyor.");
                return true;
            }

            IntPtr hToken = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(hProcess, TOKEN_QUERY, out hToken))
                {
                    FileLog.Write($"[ClipboardPaster] Hedef süreç token'ı açılamadı (pid={pid}, hata={Marshal.GetLastWin32Error()}), yükseltilmiş kabul ediliyor.");
                    return true;
                }

                if (!GetTokenInformation(hToken, TokenElevation, out TOKEN_ELEVATION elevation,
                        Marshal.SizeOf<TOKEN_ELEVATION>(), out _))
                {
                    FileLog.Write($"[ClipboardPaster] TokenElevation okunamadı (pid={pid}, hata={Marshal.GetLastWin32Error()}).");
                    return false;
                }

                return elevation.TokenIsElevated != 0;
            }
            finally
            {
                if (hToken != IntPtr.Zero) CloseHandle(hToken);
                CloseHandle(hProcess);
            }
        }

        // ---------------------------------------------------------------- pano yazma

        /// <summary>
        /// Metni kısa ömürlü bir STA thread'inde panoya yazar ve panonun gerçekten güncellendiğini
        /// (sequence number) doğrular. Otomatik yapıştırma ve pill'deki Kopyala butonu birlikte kullanır;
        /// çağıran thread'i (UI dahil) bloklamaz.
        /// </summary>
        /// <param name="excludeFromClipboardHistory">
        /// true ise CanIncludeInClipboardHistory=0 eklenir: geçici dikte metni Win+V geçmişini kirletmez.
        /// Pill'deki Kopyala gibi kullanıcının bilerek kopyaladığı durumlarda false kalmalı.
        /// </param>
        public static async Task<bool> SetTextAsync(string text, bool excludeFromClipboardHistory = false)
        {
            uint seqBefore = GetClipboardSequenceNumber();
            await RunOnStaThreadAsync(() =>
            {
                SafeSetClipboardText(text, excludeFromClipboardHistory);
            }).ConfigureAwait(false);

            // Chromium/UWP tabanlı hedefler (VS Code, tarayıcı, Slack) panoyu asenkron
            // okur; Ctrl+V'den önce yazımın tamamlandığından emin ol
            // (normalde 0-10 ms, en fazla ~200 ms).
            for (int i = 0; i < 20; i++)
            {
                if (GetClipboardSequenceNumber() != seqBefore) return true;
                await Task.Delay(10).ConfigureAwait(false);
            }
            return false;
        }

        private static void SafeSetClipboardText(string text, bool excludeFromClipboardHistory)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (RawSetClipboardText(text, excludeFromClipboardHistory)) return;
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[ClipboardPaster] Pano yazma hatası: {ex.Message}");
                    return;
                }
                Thread.Sleep(20);
            }
            FileLog.Write("[ClipboardPaster] Pano 5 denemede açılamadı.");
        }

        /// <summary>
        /// Panoyu doğrudan Win32 ile yazar. Yönetilen Clipboard.SetText yerine bu kullanılıyor çünkü
        /// (a) CanIncludeInClipboardHistory gibi ham DWORD formatlarını ancak böyle ekleyebiliyoruz,
        /// (b) OLE flush adımı başka bir uygulama panoyu kilitlediğinde takılabiliyordu.
        /// </summary>
        private static bool RawSetClipboardText(string text, bool excludeFromClipboardHistory)
        {
            if (!OpenClipboard(IntPtr.Zero)) return false;

            try
            {
                if (!EmptyClipboard())
                {
                    FileLog.Write($"[ClipboardPaster] EmptyClipboard başarısız (hata={Marshal.GetLastWin32Error()}).");
                    return false;
                }

                IntPtr hText = AllocUnicodeText(text);
                if (hText == IntPtr.Zero) return false;

                if (SetClipboardData(CF_UNICODETEXT, hText) == IntPtr.Zero)
                {
                    FileLog.Write($"[ClipboardPaster] SetClipboardData başarısız (hata={Marshal.GetLastWin32Error()}).");
                    GlobalFree(hText);
                    return false;
                }
                // Başarılı SetClipboardData'dan sonra HGLOBAL'in sahibi işletim sistemidir; free edilmez.

                if (excludeFromClipboardHistory)
                {
                    // Win+V geçmişi VE cihazlar arası bulut eşitlemesi. İkisi ayrı formatlar;
                    // yalnızca geçmiş engellenirse dikte metni yine de kullanıcının diğer
                    // cihazlarına gidebilir, bu yüzden ikisi birlikte 0 yazılır.
                    ExcludeFormat("CanIncludeInClipboardHistory");
                    ExcludeFormat("CanUploadToCloudClipboard");
                }

                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }

        /// <summary>
        /// Adlandırılmış pano formatını DWORD 0 olarak yazar (dışlama bayrağı).
        /// Pano zaten açık olmalıdır.
        /// </summary>
        private static void ExcludeFormat(string formatName)
        {
            uint cf = RegisterClipboardFormat(formatName);
            if (cf == 0) return;

            IntPtr hFlag = AllocDword(0);
            if (hFlag == IntPtr.Zero) return;

            if (SetClipboardData(cf, hFlag) == IntPtr.Zero)
            {
                FileLog.Write($"[ClipboardPaster] {formatName} yazılamadı (hata={Marshal.GetLastWin32Error()}).");
                GlobalFree(hFlag);
            }
        }

        private static IntPtr AllocUnicodeText(string text)
        {
            int bytes = (text.Length + 1) * sizeof(char);
            IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(ulong)bytes);
            if (hMem == IntPtr.Zero) return IntPtr.Zero;

            IntPtr ptr = GlobalLock(hMem);
            if (ptr == IntPtr.Zero)
            {
                GlobalFree(hMem);
                return IntPtr.Zero;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                Marshal.WriteInt16(ptr, text.Length * sizeof(char), 0); // sonlandırıcı NUL
            }
            finally
            {
                GlobalUnlock(hMem);
            }
            return hMem;
        }

        private static IntPtr AllocDword(int value)
        {
            IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)sizeof(int));
            if (hMem == IntPtr.Zero) return IntPtr.Zero;

            IntPtr ptr = GlobalLock(hMem);
            if (ptr == IntPtr.Zero)
            {
                GlobalFree(hMem);
                return IntPtr.Zero;
            }

            try { Marshal.WriteInt32(ptr, 0, value); }
            finally { GlobalUnlock(hMem); }
            return hMem;
        }

        // ---------------------------------------------------------------- pano yedekleme / geri yükleme

        /// <summary>
        /// Panonun temel formatlarını (metin, dosya listesi, bitmap) kendi hafızamıza kopyalar.
        /// Canlı OLE IDataObject referansı TUTULMAZ. Çağrı takılırsa (başka uygulama panoyu
        /// kilitliyor / yanıt vermiyor) null döner ve geri yükleme yapılmaz.
        /// </summary>
        private static async Task<ClipboardSnapshot?> CaptureClipboardAsync()
        {
            ClipboardSnapshot? snapshot = null;
            var work = RunOnStaThreadAsync(() => snapshot = CaptureClipboardCore());

            if (await Task.WhenAny(work, Task.Delay(ClipboardCaptureTimeoutMs)).ConfigureAwait(false) != work)
            {
                FileLog.Write($"[ClipboardPaster] Pano yedekleme {ClipboardCaptureTimeoutMs}ms içinde tamamlanmadı, geri yükleme atlanacak.");
                return null;
            }

            try
            {
                await work.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[ClipboardPaster] Pano yedeklenemedi: {ex.Message}");
                return null;
            }

            return snapshot;
        }

        private static ClipboardSnapshot CaptureClipboardCore()
        {
            var snapshot = new ClipboardSnapshot();
            var data = Clipboard.GetDataObject();
            if (data == null) return snapshot;

            try
            {
                if (data.GetDataPresent(DataFormats.UnicodeText))
                    snapshot.Text = data.GetData(DataFormats.UnicodeText) as string;
            }
            catch (Exception ex) { FileLog.Write($"[ClipboardPaster] Pano metni okunamadı: {ex.Message}"); }

            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop))
                    snapshot.Files = data.GetData(DataFormats.FileDrop) as string[];
            }
            catch (Exception ex) { FileLog.Write($"[ClipboardPaster] Pano dosya listesi okunamadı: {ex.Message}"); }

            try
            {
                // Görüntü mutlaka KOPYALANIR: kaynak nesne OLE'ye bağlı kalırsa geri yazarken takılır.
                if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is Image image)
                    snapshot.Image = new Bitmap(image);
            }
            catch (Exception ex) { FileLog.Write($"[ClipboardPaster] Pano görüntüsü okunamadı: {ex.Message}"); }

            return snapshot;
        }

        private static async Task<bool> RestoreClipboardAsync(ClipboardSnapshot snapshot)
        {
            bool restored = false;
            var work = RunOnStaThreadAsync(() => restored = RestoreClipboardCore(snapshot));

            if (await Task.WhenAny(work, Task.Delay(ClipboardRestoreTimeoutMs)).ConfigureAwait(false) != work)
            {
                FileLog.Write($"[ClipboardPaster] Pano geri yükleme {ClipboardRestoreTimeoutMs}ms içinde tamamlanmadı.");
                return false;
            }

            try
            {
                await work.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[ClipboardPaster] Pano geri yüklenemedi: {ex.Message}");
                return false;
            }

            return restored;
        }

        private static bool RestoreClipboardCore(ClipboardSnapshot snapshot)
        {
            // Yedek boştu: kullanıcının panosu da boştu, dikte metnini orada bırakma.
            if (snapshot.IsEmpty)
            {
                Clipboard.Clear();
                return true;
            }

            try
            {
                var data = new DataObject();
                if (snapshot.Text != null) data.SetText(snapshot.Text, TextDataFormat.UnicodeText);
                if (snapshot.Files is { Length: > 0 })
                {
                    var files = new StringCollection();
                    files.AddRange(snapshot.Files);
                    data.SetFileDropList(files);
                }
                if (snapshot.Image != null) data.SetImage(snapshot.Image);

                // copy:true → veriler panoya flush edilir, bu STA thread ölünce kaybolmaz.
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (Exception ex) when (snapshot.Text != null)
            {
                // Bitmap/dosya formatlarından biri yazılamadı; en azından metni kurtar.
                FileLog.Write($"[ClipboardPaster] Tam geri yükleme başarısız ({ex.Message}), yalnızca metin geri yükleniyor.");
                return RawSetClipboardText(snapshot.Text, excludeFromClipboardHistory: false);
            }
        }

        private sealed class ClipboardSnapshot : IDisposable
        {
            public string? Text { get; set; }
            public string[]? Files { get; set; }
            public Image? Image { get; set; }

            public bool IsEmpty => Text == null && (Files == null || Files.Length == 0) && Image == null;

            public void Dispose() => Image?.Dispose();
        }

        // ---------------------------------------------------------------- tuş enjeksiyonu

        private static void SendCtrlV()
        {
            // Tüm enjekte olaylar imzalanır: kendi klavye hook'umuz bunları yok sayar
            var sig = Win32KeyboardHook.InjectedInputSignature;
            var inputs = new List<INPUT>(12);
            AddModifierRelease(inputs, sig);

            // Temiz Ctrl+V dizisi
            inputs.Add(MakeKeyInput(VK_CONTROL, 0, sig));
            inputs.Add(MakeKeyInput(VK_V, 0, sig));
            inputs.Add(MakeKeyInput(VK_V, KEYEVENTF_KEYUP, sig));
            inputs.Add(MakeKeyInput(VK_CONTROL, KEYEVENTF_KEYUP, sig));

            SendInputs(inputs);
        }

        /// <summary>
        /// Panoyu hiç kullanmadan metni karakter karakter yazar (KEYEVENTF_UNICODE).
        /// Klavye düzeninden bağımsızdır; Türkçe karakterler doğru gider.
        /// </summary>
        public async Task DirectTypeAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var sig = Win32KeyboardHook.InjectedInputSignature;

            // Basılı modifier'lar KEYEVENTF_UNICODE olaylarını da bozar (Ctrl+harf kısayoluna dönüşür).
            var release = new List<INPUT>(ModifierKeys.Length);
            AddModifierRelease(release, sig);
            SendInputs(release);
            await Task.Delay(25).ConfigureAwait(false);

            var chunk = new List<INPUT>(DirectTypeChunkChars * 2);
            foreach (char c in text)
            {
                if (c == '\r') continue; // \r\n → tek Enter

                if (c == '\n')
                {
                    chunk.Add(MakeKeyInput(VK_RETURN, 0, sig));
                    chunk.Add(MakeKeyInput(VK_RETURN, KEYEVENTF_KEYUP, sig));
                }
                else
                {
                    // Vekil çiftlerinin (surrogate pair) iki kod birimi ardışık olaylar olarak gider.
                    chunk.Add(MakeUnicodeInput(c, 0, sig));
                    chunk.Add(MakeUnicodeInput(c, KEYEVENTF_KEYUP, sig));
                }

                if (chunk.Count >= DirectTypeChunkChars * 2)
                {
                    SendInputs(chunk);
                    chunk.Clear();
                    await Task.Delay(1).ConfigureAwait(false);
                }
            }

            SendInputs(chunk);
        }

        private static void AddModifierRelease(List<INPUT> inputs, UIntPtr sig)
        {
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
        }

        private static void SendInputs(List<INPUT> inputs)
        {
            if (inputs.Count == 0) return;

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

        private static INPUT MakeUnicodeInput(char unit, uint extraFlags, UIntPtr sig) => new()
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = unit,
                    dwFlags = KEYEVENTF_UNICODE | extraFlags,
                    dwExtraInfo = sig
                }
            }
        };

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
