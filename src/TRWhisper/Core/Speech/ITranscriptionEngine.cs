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
    }
}
