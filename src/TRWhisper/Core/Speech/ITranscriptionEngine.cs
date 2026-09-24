using System.Threading;
using System.Threading.Tasks;

namespace TRWhisper.Core.Speech
{
    public interface ITranscriptionEngine
    {
        /// <summary>
        /// Verilen WAV ses dosyasını yerel whisper motoru ile transkribe eder ve metni döner.
        /// </summary>
        Task<string> TranscribeAsync(string wavFilePath, string language = "tr", CancellationToken cancellationToken = default);

        /// <summary>
        /// Canlı akış (real-time live preview) için ham ses örneklerini transkribe eder.
        /// Motor meşgulse kuyruk oluşturmamak için hemen boş metin döner.
        /// </summary>
        Task<string> TranscribeLivePreviewAsync(float[] samples, string language = "tr", CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        /// <summary>
        /// Son <see cref="TranscribeAsync"/> çıktısının dili (ör. "auto" seçiliyken Whisper'ın
        /// algıladığı dil). Motor bunu bildirmiyorsa null; çağıran ayardaki dile düşer.
        /// </summary>
        string? LastDetectedLanguage => null;
    }
}
