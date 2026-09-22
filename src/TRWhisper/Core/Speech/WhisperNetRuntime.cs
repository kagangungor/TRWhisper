using System;
using System.Collections.Generic;
using System.IO;
using TRWhisper.Core.Diagnostics;
using Whisper.net.LibraryLoader;

namespace TRWhisper.Core.Speech
{
    /// <summary>
    /// Whisper.net'in native runtime seçimini uygulama açılışında bir kez sabitler.
    ///
    /// Whisper.net 1.9.1'in CUDA derlemesi CUDA 13'e bağlıdır ve NuGet paketi CUDA
    /// runtime DLL'lerini getirmez. Eksik kurulumda iki farklı davranış görülür:
    ///   - cudart64_13.dll yok            -> sessizce CPU'ya düşer (yavaş ama çalışır)
    ///   - cudart var, cuBLAS yok         -> GGML_ASSERT ile SÜREÇ ÇÖKER
    /// İkinci durumu önlemek için gereken dosyaların tamamı yoksa CPU runtime'ı baştan
    /// ve bilinçli olarak seçilir. Dosyalar tamsa seçim Whisper.net'e bırakılır; GPU
    /// veya sürücü yoksa kendi kontrolüyle (cudaGetDeviceCount) CPU'ya güvenle düşer.
    ///
    /// Beklenen yerleşim (exe dizinine göre):
    ///   cudart64_13.dll, cublas64_13.dll, cublasLt64_13.dll   -> exe'nin YANINDA
    ///   runtimes\cuda\win-x64\*.dll                           -> whisper CUDA native'leri
    /// CUDA runtime DLL'leri exe dizininde olmak zorundadır: Whisper.net cudart'ı
    /// işletim sisteminin varsayılan arama sırasıyla yükler, runtimes\ altına bakmaz.
    /// </summary>
    public static class WhisperNetRuntime
    {
        private static readonly string[] RequiredCudaRuntimeDlls =
        {
            "cudart64_13.dll",
            "cublas64_13.dll",
            "cublasLt64_13.dll",
        };

        private static readonly object Lock = new();
        private static bool _configured;

        /// <summary>CUDA yolunun denenip denenmeyeceği. false ise CPU'ya sabitlenmiştir.</summary>
        public static bool CudaEligible { get; private set; }

        public static void EnsureConfigured()
        {
            lock (Lock)
            {
                if (_configured) return;
                _configured = true;

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var missing = new List<string>();

                var cudaNative = Path.Combine(baseDir, "runtimes", "cuda", "win-x64", "ggml-cuda-whisper.dll");
                if (!File.Exists(cudaNative))
                    missing.Add("runtimes\\cuda\\win-x64\\ggml-cuda-whisper.dll");

                foreach (var dll in RequiredCudaRuntimeDlls)
                {
                    if (!File.Exists(Path.Combine(baseDir, dll)))
                        missing.Add(dll);
                }

                if (missing.Count == 0)
                {
                    CudaEligible = true;
                    RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                    {
                        RuntimeLibrary.Cuda,
                        RuntimeLibrary.Cpu,
                    };
                    FileLog.Write("[WhisperNetRuntime] CUDA dosyaları eksiksiz, runtime sırası: Cuda > Cpu.");
                }
                else
                {
                    CudaEligible = false;
                    RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary> { RuntimeLibrary.Cpu };
                    FileLog.Write($"[WhisperNetRuntime] CUDA devre dışı (eksik: {string.Join(", ", missing)}), CPU runtime'a sabitlendi.");
                }
            }
        }
    }
}
