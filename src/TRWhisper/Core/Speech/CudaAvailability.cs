using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TRWhisper.Core.Speech
{
    /// <summary>
    /// whisper-cli'nin GPU'da çalışıp çalışamayacağını tahmin eder: yanındaki klasörde CUDA
    /// derlemesi (ggml-cuda.dll) olmalı ve NVIDIA sürücüsü (nvcuda.dll) en az bir CUDA aygıtı
    /// bildirmeli. whisper'ın CUDA backend'i de aynı sürücü çağrılarını yapar; ikisi de
    /// CUDA_VISIBLE_DEVICES ortam değişkenine uyar. GPU yoksa whisper CPU'da (çok daha yavaş) çalışır.
    /// </summary>
    public static class CudaAvailability
    {
        private static bool? _hasCudaDevice;

        public static bool IsAvailable(string whisperCliPath)
        {
            var dir = Path.GetDirectoryName(whisperCliPath) ?? string.Empty;
            return File.Exists(Path.Combine(dir, "ggml-cuda.dll")) && HasCudaDevice();
        }

        private static bool HasCudaDevice()
        {
            if (_hasCudaDevice.HasValue) return _hasCudaDevice.Value;

            bool result;
            try
            {
                result = cuInit(0) == 0 && cuDeviceGetCount(out var count) == 0 && count > 0;
            }
            catch (Exception)
            {
                // DllNotFoundException / EntryPointNotFoundException: NVIDIA sürücüsü yok.
                result = false;
            }

            _hasCudaDevice = result;
            return result;
        }

        [DllImport("nvcuda.dll")]
        private static extern int cuInit(uint flags);

        [DllImport("nvcuda.dll")]
        private static extern int cuDeviceGetCount(out int count);
    }
}
