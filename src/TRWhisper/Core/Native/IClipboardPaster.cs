using System.Threading.Tasks;

namespace TRWhisper.Core.Native
{
    public interface IClipboardPaster
    {
        /// <summary>
        /// O anki panoyu yedekler, verilen metni panoya yazar, Win32 SendInput ile Ctrl+V simüle eder
        /// ve restoreDelayMs süre sonra orijinal pano içeriğini geri yükler.
        /// </summary>
        Task PasteTextAsync(string text, int restoreDelayMs = 150);
    }
}
