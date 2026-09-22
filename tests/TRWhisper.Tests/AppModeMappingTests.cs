using System;
using System.Collections.Generic;
using System.Linq;
using TRWhisper.Core.Config;
using TRWhisper.Core.Llm;
using TRWhisper.Core.Native;
using Xunit;

namespace TRWhisper.Tests
{
    public class AppModeMappingTests
    {
        [Fact]
        public void DefaultAppModeMappings_ContainExpectedMappings()
        {
            var mappings = LlmCleaningConfig.GetDefaultAppModeMappings();

            Assert.NotNull(mappings);

            // Varsayılan olarak kalması gerekenler
            Assert.True(mappings.ContainsKey("thunderbird"));
            Assert.Equal("Email", mappings["thunderbird"]);

            Assert.True(mappings.ContainsKey("WindowsTerminal"));
            Assert.Equal("Technical", mappings["WindowsTerminal"]);

            Assert.True(mappings.ContainsKey("cmd"));
            Assert.Equal("Technical", mappings["cmd"]);

            Assert.True(mappings.ContainsKey("powershell"));
            Assert.Equal("Technical", mappings["powershell"]);

            Assert.True(mappings.ContainsKey("pwsh"));
            Assert.Equal("Technical", mappings["pwsh"]);

            // Kaldırılanlar artık varsayılanda OLMAMALI
            Assert.False(mappings.ContainsKey("OUTLOOK"));
            Assert.False(mappings.ContainsKey("Code"));
            Assert.False(mappings.ContainsKey("devenv"));
            Assert.False(mappings.ContainsKey("Discord"));
            Assert.False(mappings.ContainsKey("Telegram"));
            Assert.False(mappings.ContainsKey("Slack"));
        }

        [Fact]
        public void AppModeMappings_AreCaseInsensitive()
        {
            var config = new LlmCleaningConfig();

            Assert.True(config.AppModeMappings.ContainsKey("thunderbird"));
            Assert.True(config.AppModeMappings.ContainsKey("THUNDERBIRD"));
            Assert.True(config.AppModeMappings.ContainsKey("ThunderBird"));
            Assert.Equal("Email", config.AppModeMappings["thunderbird"]);

            Assert.True(config.AppModeMappings.ContainsKey("windowsterminal"));
            Assert.True(config.AppModeMappings.ContainsKey("WINDOWSTERMINAL"));
            Assert.Equal("Technical", config.AppModeMappings["windowsterminal"]);
        }

        [Theory]
        [InlineData("thunderbird", "Email", "Thunderbird")]
        [InlineData("WindowsTerminal", "Technical", "Terminal")]
        [InlineData("cmd", "Technical", "Komut Satırı")]
        [InlineData("powershell", "Technical", "PowerShell")]
        public void ResolveActiveMode_MatchesDefaultAppAndReturnsFriendlyName(string processName, string expectedModeId, string expectedFriendlyName)
        {
            var config = new LlmCleaningConfig
            {
                EnableAutoAppMode = true,
                ActiveModeId = "Clean"
            };

            var (mode, friendlyName) = LlmModeRegistry.ResolveActiveMode(config, processName);

            Assert.NotNull(mode);
            Assert.Equal(expectedModeId, mode.Id);
            Assert.Equal(expectedFriendlyName, friendlyName);
        }

        [Theory]
        [InlineData("OUTLOOK", "Email", "Outlook")]
        [InlineData("Code", "Technical", "VS Code")]
        [InlineData("devenv", "Technical", "Visual Studio")]
        [InlineData("Discord", "Clean", "Discord")]
        [InlineData("slack", "Clean", "Slack")]
        public void ResolveActiveMode_MatchesCustomUserMapping(string processName, string expectedModeId, string expectedFriendlyName)
        {
            var config = new LlmCleaningConfig
            {
                EnableAutoAppMode = true,
                ActiveModeId = "Clean",
                AppModeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "OUTLOOK", "Email" },
                    { "Code", "Technical" },
                    { "devenv", "Technical" },
                    { "Discord", "Clean" },
                    { "slack", "Clean" }
                }
            };

            var (mode, friendlyName) = LlmModeRegistry.ResolveActiveMode(config, processName);

            Assert.NotNull(mode);
            Assert.Equal(expectedModeId, mode.Id);
            Assert.Equal(expectedFriendlyName, friendlyName);
        }

        [Fact]
        public void ResolveActiveMode_FallsBackToActiveMode_WhenUnknownProcess()
        {
            var config = new LlmCleaningConfig
            {
                EnableAutoAppMode = true,
                ActiveModeId = "Summary"
            };

            var (mode, friendlyName) = LlmModeRegistry.ResolveActiveMode(config, "UnknownCalculatorApp");

            Assert.NotNull(mode);
            Assert.Equal("Summary", mode.Id);
            Assert.Null(friendlyName);
        }

        [Fact]
        public void ResolveActiveMode_FallsBackToActiveMode_WhenAutoAppModeDisabled()
        {
            var config = new LlmCleaningConfig
            {
                EnableAutoAppMode = false,
                ActiveModeId = "TranslateEn"
            };

            var (mode, friendlyName) = LlmModeRegistry.ResolveActiveMode(config, "thunderbird");

            Assert.NotNull(mode);
            Assert.Equal("TranslateEn", mode.Id);
            Assert.Null(friendlyName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ResolveActiveMode_FallsBackToActiveMode_WhenProcessNullOrEmpty(string? processName)
        {
            var config = new LlmCleaningConfig
            {
                EnableAutoAppMode = true,
                ActiveModeId = "Technical"
            };

            var (mode, friendlyName) = LlmModeRegistry.ResolveActiveMode(config, processName);

            Assert.NotNull(mode);
            Assert.Equal("Technical", mode.Id);
            Assert.Null(friendlyName);
        }

        [Fact]
        public void ResolveActiveMode_SupportsCustomUserMappings()
        {
            var config = new LlmCleaningConfig
            {
                EnableAutoAppMode = true,
                ActiveModeId = "Clean",
                AppModeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "notepad", "Summary" },
                    { "wordpad", "Email" }
                }
            };

            var (notepadMode, notepadApp) = LlmModeRegistry.ResolveActiveMode(config, "notepad");
            Assert.Equal("Summary", notepadMode.Id);
            Assert.Equal("Notepad", notepadApp);

            var (wordpadMode, wordpadApp) = LlmModeRegistry.ResolveActiveMode(config, "wordpad");
            Assert.Equal("Email", wordpadMode.Id);
            Assert.Equal("Wordpad", wordpadApp);
        }

        [Theory]
        [InlineData("Code", "VS Code")]
        [InlineData("devenv", "Visual Studio")]
        [InlineData("windowsterminal", "Terminal")]
        [InlineData("cmd", "Komut Satırı")]
        [InlineData("powershell", "PowerShell")]
        [InlineData("pwsh", "PowerShell")]
        [InlineData("outlook", "Outlook")]
        [InlineData("thunderbird", "Thunderbird")]
        [InlineData("winword", "Word")]
        [InlineData("excel", "Excel")]
        [InlineData("powerpnt", "PowerPoint")]
        [InlineData("slack", "Slack")]
        [InlineData("discord", "Discord")]
        [InlineData("telegram", "Telegram")]
        [InlineData("customapp", "Customapp")]
        public void GetFriendlyAppName_FormatsCorrectly(string processName, string expected)
        {
            var detector = new ForegroundAppDetector();
            var friendly = detector.GetFriendlyAppName(processName);
            Assert.Equal(expected, friendly);
        }

        [Fact]
        public void GetFriendlyAppName_ReturnsEmpty_WhenNullOrWhitespace()
        {
            var detector = new ForegroundAppDetector();
            Assert.Equal(string.Empty, detector.GetFriendlyAppName(null));
            Assert.Equal(string.Empty, detector.GetFriendlyAppName(""));
            Assert.Equal(string.Empty, detector.GetFriendlyAppName("   "));
        }
    }
}
