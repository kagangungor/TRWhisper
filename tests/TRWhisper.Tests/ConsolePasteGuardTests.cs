using TRWhisper.Core.Native;
using Xunit;

namespace TRWhisper.Tests
{
    public class ConsolePasteGuardTests
    {
        [Theory]
        [InlineData("ConsoleWindowClass", true)]             // cmd / PowerShell (conhost)
        [InlineData("CASCADIA_HOSTING_WINDOW_CLASS", true)]  // Windows Terminal
        [InlineData("mintty", true)]                         // Git Bash
        [InlineData("VirtualConsoleClass", true)]            // ConEmu / Cmder
        [InlineData("PuTTY", true)]
        [InlineData("Notepad", false)]
        [InlineData("Chrome_WidgetWin_1", false)]
        [InlineData("consolewindowclass", false)]            // sınıf adları büyük/küçük harfe duyarlı
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsConsoleWindowClass_RecognisesTerminals(string? className, bool expected)
            => Assert.Equal(expected, ConsolePasteGuard.IsConsoleWindowClass(className));

        [Fact]
        public void ToSingleLine_TrailingNewline_DoesNotPressEnter()
            => Assert.Equal("Remove-Item -Recurse C:\\veri", ConsolePasteGuard.ToSingleLine("Remove-Item -Recurse C:\\veri\r\n"));

        [Fact]
        public void ToSingleLine_MultiLineAnswer_BecomesOneLine()
            => Assert.Equal("git add . git commit -m \"x\"", ConsolePasteGuard.ToSingleLine("git add .\ngit commit -m \"x\"\n"));

        [Theory]
        [InlineData("a\r\nb", "a b")]
        [InlineData("a\rb", "a b")]
        [InlineData("a\u2028b\u2029c\u0085d", "a b c d")]
        [InlineData("a\tb", "a b")]
        [InlineData("a \n b", "a  b")]
        [InlineData("a\n\n\nb", "a b")]
        public void ToSingleLine_AllLineBreakKinds(string input, string expected)
            => Assert.Equal(expected, ConsolePasteGuard.ToSingleLine(input));

        [Fact]
        public void ToSingleLine_StripsControlCharacters()
            // \x03 Ctrl+C, \x1b ESC (terminal kaçış dizileri), \x08 geri silme
            => Assert.Equal("echo [31mmerhaba", ConsolePasteGuard.ToSingleLine("echo \u001b[31mmer\u0003haba\u0008"));

        [Fact]
        public void ToSingleLine_LeavesOrdinaryTextUntouched()
        {
            const string text = "Merhaba, bugün İstanbul'da %20 indirim var — ğüşıöç.";
            Assert.Equal(text, ConsolePasteGuard.ToSingleLine(text));
        }

        [Fact]
        public void ToSingleLine_OnlyNewlines_BecomesEmpty()
            => Assert.Equal("", ConsolePasteGuard.ToSingleLine("\r\n\n"));
    }
}
