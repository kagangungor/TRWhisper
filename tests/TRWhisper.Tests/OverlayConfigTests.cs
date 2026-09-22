using System;
using System.Text.Json;
using TRWhisper.Core.Config;
using Xunit;

namespace TRWhisper.Tests
{
    public class OverlayConfigTests
    {
        [Fact]
        public void OverlayConfig_DefaultsToBottomAndNullCustomCoordinates()
        {
            var config = new OverlayConfig();
            Assert.Equal("Bottom", config.Position);
            Assert.Null(config.CustomX);
            Assert.Null(config.CustomY);
            Assert.Equal(10, config.ResultDurationSeconds);
        }

        [Fact]
        public void OverlayConfig_SerializesAndDeserializesCustomPosition()
        {
            var config = new OverlayConfig
            {
                Position = "Custom",
                CustomX = 450.5,
                CustomY = 820.0,
                ResultDurationSeconds = 15
            };

            var json = JsonSerializer.Serialize(config);
            var deserialized = JsonSerializer.Deserialize<OverlayConfig>(json);

            Assert.NotNull(deserialized);
            Assert.Equal("Custom", deserialized.Position);
            Assert.Equal(450.5, deserialized.CustomX);
            Assert.Equal(820.0, deserialized.CustomY);
            Assert.Equal(15, deserialized.ResultDurationSeconds);
        }
    }
}
