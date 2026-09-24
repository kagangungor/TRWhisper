using System.Diagnostics;

namespace TRWhisper.Core.Diagnostics
{
    /// <summary>Ayarlar > Model sekmesindeki canlı kaynak kartı için hafif ölçümler.</summary>
    public static class ResourceMonitor
    {
        /// <summary>Sürecin anlık çalışma kümesi (RAM). Her çağrıda taze bir Process nesnesi okunur.</summary>
        public static long ProcessWorkingSetBytes()
        {
            using var process = Process.GetCurrentProcess();
            return process.WorkingSet64;
        }

        public static string FormatBytes(long bytes)
        {
            const double Mb = 1024 * 1024;
            if (bytes <= 0) return "0 MB";
            return bytes >= 1024 * Mb ? $"{bytes / (1024 * Mb):0.0} GB" : $"{bytes / Mb:0} MB";
        }

        public static string AccelerationLabel(bool cudaActive)
            => cudaActive ? "NVIDIA GPU Hızlandırması Aktif (CUDA 13)" : "İşlemci (CPU) Modu";
    }
}
