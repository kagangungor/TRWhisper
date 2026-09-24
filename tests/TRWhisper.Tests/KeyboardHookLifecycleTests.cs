using System;
using System.Diagnostics;
using System.Threading;
using TRWhisper.Core.Native;
using Xunit;

namespace TRWhisper.Tests
{
    /// <summary>
    /// Hook'lar adanmış bir iş parçacığında kendi mesaj döngüsüyle çalışır; başlatan iş
    /// parçacığı (uygulamada UI) meşgulken de sistem girdisi bekletilmez.
    /// </summary>
    public class KeyboardHookLifecycleTests
    {
        [Fact]
        public void StartStop_IsIdempotentRestartableAndFast()
        {
            using var hook = new Win32KeyboardHook();

            var ex = Record.Exception(() =>
            {
                hook.Stop();          // başlamadan durdurma: işlem yok
                hook.Start();
                hook.Start();         // ikinci başlatma: işlem yok
                var sw = Stopwatch.StartNew();
                hook.Stop();          // WM_QUIT ile mesaj döngüsü kapanır, iş parçacığı beklenir
                Assert.True(sw.ElapsedMilliseconds < 1500, $"Stop {sw.ElapsedMilliseconds} ms sürdü");
                hook.Start();         // yeniden başlatılabilir
            });

            Assert.Null(ex);
            Assert.False(hook.IsHotkeyHeld);
        }

        [Fact]
        public void Start_DoesNotDependOnCallerThreadMessageLoop()
        {
            // Mesaj döngüsü OLMAYAN ve hemen biten bir iş parçacığından başlatılır. Hook'lar
            // çağıranın iş parçacığına bağlı olsaydı bu iş parçacığı bitince ölürlerdi.
            using var hook = new Win32KeyboardHook();
            var starter = new Thread(hook.Start);
            starter.Start();
            Assert.True(starter.Join(TimeSpan.FromSeconds(5)));

            var sw = Stopwatch.StartNew();
            hook.Stop();
            Assert.True(sw.ElapsedMilliseconds < 1500);
        }
    }
}
