using System;
using System.Threading.Tasks;

namespace TRWhisper.Core.Audio
{
    public interface IAudioRecorder : IDisposable
    {
        /// <summary>
        /// 16kHz, 16-bit Mono WAV formatında kayıt başlatır.
        /// </summary>
        void StartRecording(string outputWavPath);

        /// <summary>
        /// Kaydı durdurur, dosya tamponunu kapatır ve diske yazılan WAV dosyasının yolunu döner.
        /// </summary>
        Task<string> StopRecordingAsync();

        /// <summary>
        /// Şu an kayıt yapılıyor mu?
        /// </summary>
        bool IsRecording { get; }

        /// <summary>
        /// Son ses tamponundaki tepe genlik (0..1). Eller serbest modda sessizlik algılamak
        /// için kullanılır; kayıt yokken 0 döner.
        /// </summary>
        float CurrentLevel { get; }

        /// <summary>
        /// Kayıt devam ederken o ana kadar biriken 16 kHz mono float ses örneklerini döndürür.
        /// maxLastSamples belirtilmişse yalnızca sondaki dilimi alır (örn. son 10 saniye için 160000).
        /// </summary>
        float[] GetRecordedSamplesSnapshot(int? maxLastSamples = null);
    }
}
