using System.Threading.Tasks;

namespace TRWhisper.Core.Native
{
    public interface IClipboardPaster
    {
        /// <summary>
        /// Verilen metni panoya yazar ve Win32 SendInput ile Ctrl+V simüle eder. Metin panoda kalır
        /// (önceki pano içeriği geri yüklenmez). Metin panoya yazılabildiyse true döner; yazılamadıysa
        /// Ctrl+V gönderilmez ve false döner.
        /// </summary>
        Task<bool> PasteTextAsync(string text);
    }
}
