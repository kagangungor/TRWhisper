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
    }
}
