using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TRWhisper.Core.Native
{
    [Flags]
    public enum HotkeyModifiers
    {
        None = 0,
        Alt = 1,
        Ctrl = 2,
        Shift = 4,
        Win = 8
    }

    public class HotkeyBinding
    {
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt
        private const int VK_SHIFT = 0x10;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_CAPITAL = 0x14;
        private const int VK_SPACE = 0x20;

        public const int XBUTTON1 = 1; // Mouse 4 (Geri)
        public const int XBUTTON2 = 2; // Mouse 5 (İleri)

        public string RawString { get; }
        public bool IsNone { get; }
        public bool IsMouse { get; }
        public int MouseButton { get; } // 0 = not mouse, 1 = XBUTTON1, 2 = XBUTTON2
        public HotkeyModifiers Modifiers { get; }
        public int PrimaryVk { get; }
        public int GenericVk { get; }
        public bool? ExtendedRequired { get; }
        public bool Suppress { get; }
        public string DisplayName { get; }

        public HotkeyBinding(
            string rawString,
            bool isNone,
            bool isMouse,
            int mouseButton,
            HotkeyModifiers modifiers,
            int primaryVk,
            int genericVk = 0,
            bool? extendedRequired = null,
            bool suppress = false,
            string? displayName = null)
        {
            RawString = rawString;
            IsNone = isNone;
            IsMouse = isMouse;
            MouseButton = mouseButton;
            Modifiers = modifiers;
            PrimaryVk = primaryVk;
            GenericVk = genericVk;
            ExtendedRequired = extendedRequired;
            Suppress = suppress;
            DisplayName = displayName ?? GenerateDisplayName(rawString, isNone, isMouse, mouseButton, modifiers, primaryVk, extendedRequired);
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public bool MatchesKey(uint vkCode, bool isExtended)
        {
            if (IsNone || IsMouse) return false;

            // 1. Birincil tuş kodu eşleşiyor mu?
            bool keyMatches = false;
            if (PrimaryVk != 0 && vkCode == (uint)PrimaryVk)
            {
                keyMatches = true;
            }
            else if (GenericVk != 0 && vkCode == (uint)GenericVk)
            {
                if (ExtendedRequired.HasValue)
                {
                    if (isExtended == ExtendedRequired.Value) keyMatches = true;
                }
                else
                {
                    keyMatches = true;
                }
            }

            if (!keyMatches) return false;

            // 2. Eğer kombinasyonda yardımcı tuşlar (Ctrl, Alt, Shift vb.) tanımlıysa onların basılı olduğunu kontrol et
            if (Modifiers != HotkeyModifiers.None)
            {
                if (!AreModifiersHeld(Modifiers, (int)vkCode))
                    return false;
            }

            return true;
        }

        public bool MatchesMouse(int button)
        {
            if (!IsMouse || button != MouseButton) return false;

            if (Modifiers != HotkeyModifiers.None)
            {
                if (!AreModifiersHeld(Modifiers, 0))
                    return false;
            }

            return true;
        }

        public bool IsActiveNow()
        {
            if (IsNone) return false;

            if (IsMouse)
            {
                // Fare tuşunun GetAsyncKeyState ile yoklanması (VK_XBUTTON1 / VK_XBUTTON2)
                int mouseVk = MouseButton == XBUTTON1 ? 0x05 : 0x06;
                bool mouseHeld = (GetAsyncKeyState(mouseVk) & 0x8000) != 0;
                if (!mouseHeld) return false;

                return Modifiers == HotkeyModifiers.None || AreModifiersHeld(Modifiers, 0);
            }

            // Birincil tuş basılı mı?
            int testVk = PrimaryVk != 0 ? PrimaryVk : GenericVk;
            if (testVk == 0) return false;

            bool primaryHeld = (GetAsyncKeyState(testVk) & 0x8000) != 0;
            if (!primaryHeld) return false;

            if (Modifiers != HotkeyModifiers.None)
            {
                return AreModifiersHeld(Modifiers, testVk);
            }

            return true;
        }

        private static bool AreModifiersHeld(HotkeyModifiers mods, int currentKeyVk)
        {
            if (mods.HasFlag(HotkeyModifiers.Ctrl))
            {
                if (currentKeyVk != VK_CONTROL && currentKeyVk != VK_LCONTROL && currentKeyVk != VK_RCONTROL)
                {
                    if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0) return false;
                }
            }

            if (mods.HasFlag(HotkeyModifiers.Alt))
            {
                if (currentKeyVk != VK_MENU && currentKeyVk != VK_LMENU && currentKeyVk != VK_RMENU)
                {
                    if ((GetAsyncKeyState(VK_MENU) & 0x8000) == 0) return false;
                }
            }

            if (mods.HasFlag(HotkeyModifiers.Shift))
            {
                if (currentKeyVk != VK_SHIFT && currentKeyVk != VK_LSHIFT && currentKeyVk != VK_RSHIFT)
                {
                    if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0) return false;
                }
            }

            if (mods.HasFlag(HotkeyModifiers.Win))
            {
                if (currentKeyVk != VK_LWIN && currentKeyVk != VK_RWIN)
                {
                    if ((GetAsyncKeyState(VK_LWIN) & 0x8000) == 0 && (GetAsyncKeyState(VK_RWIN) & 0x8000) == 0)
                        return false;
                }
            }

            return true;
        }

        public static HotkeyBinding Parse(string? text, string defaultFallback = "RightCtrl")
        {
            var raw = (text ?? "").Trim();
            if (string.IsNullOrEmpty(raw) || string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase))
            {
                return new HotkeyBinding("None", isNone: true, isMouse: false, mouseButton: 0,
                    modifiers: HotkeyModifiers.None, primaryVk: 0, displayName: "Kullanma (Devre Dışı)");
            }

            // Bilinen hazır tuşlar
            if (string.Equals(raw, "RightCtrl", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("RightCtrl", false, false, 0, HotkeyModifiers.None, VK_RCONTROL, VK_CONTROL, extendedRequired: true, displayName: "Sağ Ctrl");

            if (string.Equals(raw, "LeftCtrl", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("LeftCtrl", false, false, 0, HotkeyModifiers.None, VK_LCONTROL, VK_CONTROL, extendedRequired: false, displayName: "Sol Ctrl");

            if (string.Equals(raw, "RightAlt", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "AltGr", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("RightAlt", false, false, 0, HotkeyModifiers.None, VK_RMENU, VK_MENU, extendedRequired: true, displayName: "Sağ Alt (AltGr)");

            if (string.Equals(raw, "LeftAlt", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("LeftAlt", false, false, 0, HotkeyModifiers.None, VK_LMENU, VK_MENU, extendedRequired: false, displayName: "Sol Alt");

            if (string.Equals(raw, "RightShift", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("RightShift", false, false, 0, HotkeyModifiers.None, VK_RSHIFT, VK_SHIFT, displayName: "Sağ Shift");

            if (string.Equals(raw, "LeftShift", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("LeftShift", false, false, 0, HotkeyModifiers.None, VK_LSHIFT, VK_SHIFT, displayName: "Sol Shift");

            if (string.Equals(raw, "Shift", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("Shift", false, false, 0, HotkeyModifiers.Shift, VK_SHIFT, displayName: "Shift");

            if (string.Equals(raw, "Ctrl", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("Ctrl", false, false, 0, HotkeyModifiers.Ctrl, VK_CONTROL, displayName: "Ctrl");

            if (string.Equals(raw, "Alt", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("Alt", false, false, 0, HotkeyModifiers.Alt, VK_MENU, displayName: "Alt");

            if (string.Equals(raw, "CapsLock", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("CapsLock", false, false, 0, HotkeyModifiers.None, VK_CAPITAL, suppress: true, displayName: "Caps Lock");

            if (string.Equals(raw, "Mouse4", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "XButton1", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("Mouse4", false, true, XBUTTON1, HotkeyModifiers.None, 0, suppress: true, displayName: "Fare Yan Tuşu 1 (Mouse 4 / Geri)");

            if (string.Equals(raw, "Mouse5", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "XButton2", StringComparison.OrdinalIgnoreCase))
                return new HotkeyBinding("Mouse5", false, true, XBUTTON2, HotkeyModifiers.None, 0, suppress: true, displayName: "Fare Yan Tuşu 2 (Mouse 5 / İleri)");

            // Kombinasyon ayrıştırma: "Ctrl+Space", "Ctrl + Shift + D", vb.
            var parts = raw.Split(new[] { '+', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return Parse(defaultFallback);
            }

            HotkeyModifiers modifiers = HotkeyModifiers.None;
            int primaryVk = 0;
            int genericVk = 0;
            bool? extended = null;
            bool isMouse = false;
            int mouseButton = 0;
            bool suppress = false;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();

                if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= HotkeyModifiers.Ctrl;
                }
                else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= HotkeyModifiers.Alt;
                }
                else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= HotkeyModifiers.Shift;
                }
                else if (string.Equals(part, "Win", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "Windows", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= HotkeyModifiers.Win;
                }
                else if (string.Equals(part, "Mouse4", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "XButton1", StringComparison.OrdinalIgnoreCase))
                {
                    isMouse = true;
                    mouseButton = XBUTTON1;
                    suppress = true;
                }
                else if (string.Equals(part, "Mouse5", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "XButton2", StringComparison.OrdinalIgnoreCase))
                {
                    isMouse = true;
                    mouseButton = XBUTTON2;
                    suppress = true;
                }
                else
                {
                    // Asıl tuş
                    primaryVk = ParseSingleVk(part, out genericVk, out extended, out suppress);
                }
            }

            if (primaryVk == 0 && !isMouse)
            {
                // Sadece modifier verilmiş olabilir (örn. "Ctrl", "Alt", "Shift")
                if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) primaryVk = VK_CONTROL;
                else if (modifiers.HasFlag(HotkeyModifiers.Alt)) primaryVk = VK_MENU;
                else if (modifiers.HasFlag(HotkeyModifiers.Shift)) primaryVk = VK_SHIFT;
                else
                {
                    return Parse(defaultFallback);
                }
            }

            return new HotkeyBinding(raw, false, isMouse, mouseButton, modifiers, primaryVk, genericVk, extended, suppress);
        }

        private static int ParseSingleVk(string name, out int genericVk, out bool? extended, out bool suppress)
        {
            genericVk = 0;
            extended = null;
            suppress = false;

            if (string.Equals(name, "RightCtrl", StringComparison.OrdinalIgnoreCase))
            {
                genericVk = VK_CONTROL;
                extended = true;
                return VK_RCONTROL;
            }
            if (string.Equals(name, "LeftCtrl", StringComparison.OrdinalIgnoreCase))
            {
                genericVk = VK_CONTROL;
                extended = false;
                return VK_LCONTROL;
            }
            if (string.Equals(name, "RightAlt", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "AltGr", StringComparison.OrdinalIgnoreCase))
            {
                genericVk = VK_MENU;
                extended = true;
                return VK_RMENU;
            }
            if (string.Equals(name, "LeftAlt", StringComparison.OrdinalIgnoreCase))
            {
                genericVk = VK_MENU;
                extended = false;
                return VK_LMENU;
            }
            if (string.Equals(name, "RightShift", StringComparison.OrdinalIgnoreCase))
            {
                genericVk = VK_SHIFT;
                return VK_RSHIFT;
            }
            if (string.Equals(name, "LeftShift", StringComparison.OrdinalIgnoreCase))
            {
                genericVk = VK_SHIFT;
                return VK_LSHIFT;
            }
            if (string.Equals(name, "CapsLock", StringComparison.OrdinalIgnoreCase))
            {
                suppress = true;
                return VK_CAPITAL;
            }
            if (string.Equals(name, "Space", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Boşluk", StringComparison.OrdinalIgnoreCase))
            {
                return VK_SPACE;
            }

            // F1 - F24
            if (name.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[1..], out int fNum) && fNum >= 1 && fNum <= 24)
            {
                return 0x70 + (fNum - 1);
            }

            // Tek harf (A-Z)
            if (name.Length == 1 && char.IsLetter(name[0]))
            {
                return char.ToUpperInvariant(name[0]);
            }

            // Rakam (0-9)
            if (name.Length == 1 && char.IsDigit(name[0]))
            {
                return name[0];
            }

            // Diğer yaygın tuşlar
            return name.ToUpperInvariant() switch
            {
                "TAB" => 0x09,
                "ENTER" or "RETURN" => 0x0D,
                "ESC" or "ESCAPE" => 0x1B,
                "BACKSPACE" or "BACK" => 0x08,
                "INSERT" or "INS" => 0x2D,
                "DELETE" or "DEL" => 0x2E,
                "HOME" => 0x24,
                "END" => 0x23,
                "PAGEUP" or "PGUP" => 0x21,
                "PAGEDOWN" or "PGDN" => 0x22,
                "PAUSE" => 0x13,
                "SCROLLLOCK" or "SCROLL" => 0x91,
                "PRINTSCREEN" or "PRTSC" => 0x2C,
                _ => 0
            };
        }

        private static string GenerateDisplayName(string raw, bool isNone, bool isMouse, int mouseButton, HotkeyModifiers modifiers, int primaryVk, bool? extended)
        {
            if (isNone) return "Kullanma (Devre Dışı)";

            var sb = new StringBuilder();
            if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) sb.Append("Ctrl + ");
            if (modifiers.HasFlag(HotkeyModifiers.Alt)) sb.Append("Alt + ");
            if (modifiers.HasFlag(HotkeyModifiers.Shift)) sb.Append("Shift + ");
            if (modifiers.HasFlag(HotkeyModifiers.Win)) sb.Append("Win + ");

            if (isMouse)
            {
                sb.Append(mouseButton == XBUTTON1 ? "Fare Yan Tuşu 1 (Mouse 4)" : "Fare Yan Tuşu 2 (Mouse 5)");
                return sb.ToString();
            }

            string keyName = primaryVk switch
            {
                VK_RCONTROL => "Sağ Ctrl",
                VK_LCONTROL => "Sol Ctrl",
                VK_RMENU => "Sağ Alt (AltGr)",
                VK_LMENU => "Sol Alt",
                VK_RSHIFT => "Sağ Shift",
                VK_LSHIFT => "Sol Shift",
                VK_CAPITAL => "Caps Lock",
                VK_SPACE => "Boşluk (Space)",
                >= 0x70 and <= 0x87 => $"F{primaryVk - 0x70 + 1}",
                >= 'A' and <= 'Z' => ((char)primaryVk).ToString(),
                >= '0' and <= '9' => ((char)primaryVk).ToString(),
                0x09 => "Tab",
                0x0D => "Enter",
                0x1B => "Esc",
                0x08 => "Backspace",
                0x2D => "Insert",
                0x2E => "Delete",
                0x24 => "Home",
                0x23 => "End",
                0x21 => "Page Up",
                0x22 => "Page Down",
                0x13 => "Pause",
                0x91 => "Scroll Lock",
                0x2C => "Print Screen",
                _ => raw
            };

            // Eğer sadece modifier varsa ve keyName o modifier ise fazladan ekleme
            if (modifiers != HotkeyModifiers.None && (keyName == "Ctrl" || keyName == "Alt" || keyName == "Shift"))
            {
                return sb.ToString().TrimEnd(' ', '+');
            }

            sb.Append(keyName);
            return sb.ToString();
        }

        public override string ToString() => DisplayName;
    }
}
