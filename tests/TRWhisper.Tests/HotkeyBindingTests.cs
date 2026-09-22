using System;
using TRWhisper.Core.Native;
using Xunit;

namespace TRWhisper.Tests
{
    public class HotkeyBindingTests
    {
        [Theory]
        [InlineData("RightCtrl", "Sağ Ctrl")]
        [InlineData("LeftCtrl", "Sol Ctrl")]
        [InlineData("RightAlt", "Sağ Alt (AltGr)")]
        [InlineData("LeftAlt", "Sol Alt")]
        [InlineData("RightShift", "Sağ Shift")]
        [InlineData("CapsLock", "Caps Lock")]
        [InlineData("F8", "F8")]
        [InlineData("F12", "F12")]
        [InlineData("Mouse4", "Fare Yan Tuşu 1 (Mouse 4 / Geri)")]
        [InlineData("Mouse5", "Fare Yan Tuşu 2 (Mouse 5 / İleri)")]
        [InlineData("None", "Kullanma (Devre Dışı)")]
        public void Parse_StandardKeys_ReturnsExpectedDisplayName(string raw, string expectedDisplayName)
        {
            var binding = HotkeyBinding.Parse(raw);
            Assert.NotNull(binding);
            Assert.Equal(expectedDisplayName, binding.DisplayName);
        }

        [Fact]
        public void Parse_None_SetsIsNoneTrue()
        {
            var binding = HotkeyBinding.Parse("None");
            Assert.True(binding.IsNone);
            Assert.False(binding.IsActiveNow());
        }

        [Theory]
        [InlineData("Ctrl+Space", HotkeyModifiers.Ctrl, 0x20)]
        [InlineData("Ctrl+Shift+D", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 'D')]
        [InlineData("Alt+F8", HotkeyModifiers.Alt, 0x77)]
        [InlineData("Win+Alt+R", HotkeyModifiers.Win | HotkeyModifiers.Alt, 'R')]
        public void Parse_Combinations_SetsModifiersAndPrimaryVk(string raw, HotkeyModifiers expectedMods, int expectedVk)
        {
            var binding = HotkeyBinding.Parse(raw);
            Assert.Equal(expectedMods, binding.Modifiers);
            Assert.Equal(expectedVk, binding.PrimaryVk);
            Assert.False(binding.IsNone);
        }

        [Fact]
        public void Parse_MouseCombination_SetsIsMouse()
        {
            var binding = HotkeyBinding.Parse("Ctrl+Mouse4");
            Assert.True(binding.IsMouse);
            Assert.Equal(HotkeyBinding.XBUTTON1, binding.MouseButton);
            Assert.True(binding.Modifiers.HasFlag(HotkeyModifiers.Ctrl));
        }

        [Fact]
        public void MatchesKey_SingleKey_MatchesCorrectly()
        {
            var binding = HotkeyBinding.Parse("F8");
            Assert.True(binding.MatchesKey(0x77, false));
            Assert.False(binding.MatchesKey(0x78, false));
        }
    }
}
