using System.Windows;
using TRWhisper.UI;
using Xunit;

namespace TRWhisper.Tests
{
    public class OverlaySnappingTests
    {
        // 1920x1040 çalışma alanı (48 px görev çubuğu), 460x56 kapsül.
        private static readonly Rect WorkArea = new(0, 0, 1920, 1040);
        private const double W = 460, H = 56;

        private static (double Left, double Top) Snap(double left, double top, bool enabled = true) =>
            PillOverlayWindow.SnapToEdges(left, top, W, H, WorkArea, enabled);

        [Fact]
        public void NearLeftAndTop_SnapsToMargin()
        {
            Assert.Equal((16.0, 16.0), Snap(10, 24));
        }

        [Fact]
        public void NearRightAndBottom_SnapsToMargin()
        {
            // Sağ kenar 1920 - (1440 + 460) = 20 px uzakta, alt kenar 1040 - (970 + 56) = 14 px.
            Assert.Equal((1920 - W - 16, 1040 - H - 16), Snap(1440, 970));
        }

        [Fact]
        public void NearHorizontalCenter_SnapsToCenter()
        {
            var centeredLeft = (1920 - W) / 2;   // 730
            var (left, top) = Snap(centeredLeft + 20, 500);
            Assert.Equal(centeredLeft, left);
            Assert.Equal(500, top);   // dikeyde kenara yakın değil, dokunulmaz
        }

        [Fact]
        public void OutsideTolerance_StaysPut()
        {
            Assert.Equal((200.0, 300.0), Snap(200, 300));
            Assert.Equal((25.0, 25.0), Snap(25, 25));   // 24 px sınırının hemen dışı
        }

        [Fact]
        public void WorkAreaOffset_IsRespected()
        {
            // Görev çubuğu üstte/solda olduğunda çalışma alanı sıfırdan başlamaz.
            var area = new Rect(100, 50, 1820, 990);
            Assert.Equal((116.0, 66.0), PillOverlayWindow.SnapToEdges(110, 60, W, H, area, enabled: true));
        }

        [Fact]
        public void Disabled_DoesNotSnap()
        {
            Assert.Equal((10.0, 24.0), Snap(10, 24, enabled: false));
            Assert.Equal((750.0, 970.0), Snap(750, 970, enabled: false));
        }
    }
}
