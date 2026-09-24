using System;
using System.IO;
using System.Text.Json;
using TRWhisper.Core.Config;
using Xunit;

namespace TRWhisper.Tests
{
    /// <summary>Güncelleme denetimi, Mica ve kenetlenme kullanıcı açmadıkça KAPALI kalmalı.</summary>
    public class ConfigDefaultsTests
    {
        [Fact]
        public void NewConfig_NewFeaturesAreOff()
        {
            var cfg = new AppConfig();
            Assert.False(cfg.General.EnableAutomaticUpdateCheck);
            Assert.False(cfg.General.EnableMicaEffect);
            Assert.False(cfg.Overlay.EnableEdgeSnapping);
        }

        [Fact]
        public void OldConfigWithoutFields_LoadsAsOff()
        {
            var cfg = JsonSerializer.Deserialize<AppConfig>("""{ "General": { "Language": "tr" }, "Overlay": { "Position": "Top" } }""")!;
            Assert.False(cfg.General.EnableAutomaticUpdateCheck);
            Assert.False(cfg.General.EnableMicaEffect);
            Assert.False(cfg.Overlay.EnableEdgeSnapping);
        }

        [Fact]
        public void ShippedTemplate_HasNewFeaturesOff()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            string? template = null;
            for (; dir != null && template == null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "TRWhisper", "config.default.json");
                if (File.Exists(candidate)) template = candidate;
            }
            Assert.NotNull(template);

            using var doc = JsonDocument.Parse(File.ReadAllText(template!));
            var root = doc.RootElement;
            Assert.False(root.GetProperty("General").GetProperty("EnableAutomaticUpdateCheck").GetBoolean());
            Assert.False(root.GetProperty("General").GetProperty("EnableMicaEffect").GetBoolean());
            Assert.False(root.GetProperty("Overlay").GetProperty("EnableEdgeSnapping").GetBoolean());
        }
    }
}
