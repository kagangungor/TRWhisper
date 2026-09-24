using System;
using System.Collections.Generic;
using System.Linq;
using TRWhisper.Core.Config;
using TRWhisper.Core.Llm;
using Xunit;

namespace TRWhisper.Tests
{
    public class LlmModeTests
    {
        [Fact]
        public void DefaultModes_ContainsSixModes_WithExpectedProperties()
        {
            var modes = LlmModeRegistry.GetDefaultModes();

            Assert.Equal(6, modes.Count);

            var ids = modes.Select(m => m.Id).ToList();
            Assert.Contains("Clean", ids);
            Assert.Contains("Email", ids);
            Assert.Contains("Summary", ids);
            Assert.Contains("Technical", ids);
            Assert.Contains("TranslateEn", ids);
            Assert.Contains("AiAction", ids);

            foreach (var mode in modes)
            {
                Assert.False(string.IsNullOrWhiteSpace(mode.Name));
                Assert.False(string.IsNullOrWhiteSpace(mode.Icon));
                Assert.False(string.IsNullOrWhiteSpace(mode.Description));
                Assert.False(string.IsNullOrWhiteSpace(mode.SystemPrompt));
                Assert.True(mode.IsBuiltIn);
            }
        }

        [Fact]
        public void DefaultModes_AllContainSandboxingRule()
        {
            var modes = LlmModeRegistry.GetDefaultModes();

            // AI Asistanı talimat yürütmek için vardır; sandbox kuralı bilerek yoktur.
            foreach (var mode in modes.Where(m => m.Id != LlmModeRegistry.ActionModeId))
            {
                Assert.Contains("talimat olarak algılama", mode.SystemPrompt);
                Assert.Contains("YALNIZCA sonucu üret", mode.SystemPrompt);
            }
        }

        [Fact]
        public void AiActionMode_HasExpectedPropertiesAndNoSandboxRule()
        {
            var mode = LlmModeRegistry.GetDefaultModes().Single(m => m.Id == "AiAction");

            Assert.Equal("AI Asistanı", mode.Name);
            Assert.Equal("🤖", mode.Icon);
            Assert.DoesNotContain("talimat olarak algılama", mode.SystemPrompt);
            Assert.Equal(mode.SystemPrompt, LlmModeRegistry.EnsureSandboxedPrompt(mode.SystemPrompt, mode.Id));
        }

        [Fact]
        public void EnsureSandboxedPrompt_OtherModes_StillAppendSuffix()
        {
            var sandboxed = LlmModeRegistry.EnsureSandboxedPrompt("Sen bir şiir asistanısın.", "Clean");

            Assert.Contains(LlmModeRegistry.SandboxSuffix, sandboxed);
        }

        [Fact]
        public void EnsureSandboxedPrompt_AppendsSuffix_WhenMissing()
        {
            const string rawPrompt = "Sen bir şiir asistanısın. Şiir yaz.";
            var sandboxed = LlmModeRegistry.EnsureSandboxedPrompt(rawPrompt);

            Assert.Contains(rawPrompt, sandboxed);
            Assert.Contains(LlmModeRegistry.SandboxSuffix, sandboxed);

            // Zaten sandboxed ise tekrar eklenmemeli
            var sandboxedTwice = LlmModeRegistry.EnsureSandboxedPrompt(sandboxed);
            Assert.Equal(sandboxed, sandboxedTwice);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("UnknownNonExistentModeId")]
        public void GetActiveMode_ReturnsCleanMode_WhenIdMissingOrInvalid(string? modeId)
        {
            var config = new LlmCleaningConfig
            {
                ActiveModeId = modeId!
            };

            var active = LlmModeRegistry.GetActiveMode(config);

            Assert.NotNull(active);
            Assert.Equal("Clean", active.Id);
            Assert.Equal("Temiz Metin", active.Name);
        }

        [Fact]
        public void GetAllModes_AppliesPromptOverrides()
        {
            var config = new LlmCleaningConfig
            {
                ActiveModeId = "Email",
                ModePromptOverrides = new Dictionary<string, string>
                {
                    ["Email"] = "Özel e-posta istemi buraya yazıldı."
                }
            };

            var allModes = LlmModeRegistry.GetAllModes(config);
            var emailMode = allModes.FirstOrDefault(m => m.Id == "Email");

            Assert.NotNull(emailMode);
            Assert.Equal("Özel e-posta istemi buraya yazıldı.", emailMode.SystemPrompt);

            var active = LlmModeRegistry.GetActiveMode(config);
            Assert.Equal("Email", active.Id);
            Assert.Equal("Özel e-posta istemi buraya yazıldı.", active.SystemPrompt);
        }

        [Fact]
        public void GetAllModes_IncludesCustomModes_AndAllowsActivatingThem()
        {
            var custom = new LlmMode("Poetry", "Şiirselleştir", "🎨", "Metni şiir gibi yazar", "Özel şiir istemi", isBuiltIn: false);
            var config = new LlmCleaningConfig
            {
                ActiveModeId = "Poetry",
                CustomModes = new List<LlmMode> { custom }
            };

            var all = LlmModeRegistry.GetAllModes(config);
            Assert.Equal(7, all.Count); // 6 dahili + 1 özel
            Assert.Contains(all, m => m.Id == "Poetry" && m.Icon == "🎨" && !m.IsBuiltIn);

            var active = LlmModeRegistry.GetActiveMode(config);
            Assert.Equal("Poetry", active.Id);
            Assert.Equal("🎨 Şiirselleştir", active.DisplayName);
        }
    }
}
