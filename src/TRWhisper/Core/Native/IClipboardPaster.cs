using System.Threading.Tasks;

namespace TRWhisper.Core.Native
{
    /// <summary>
    /// Dikte metninin hedef pencereye ulaştırılma biçimi. Her değer pill'de farklı bir
    /// bilgilendirme metnine karşılık gelir; özellikle "metin panoda kaldı mı" ayrımı önemlidir.
    /// </summary>
    public enum PasteResult
    {
        /// <summary>Metin panoya yazılamadı (başka uygulama panoyu kilitliyor); Ctrl+V gönderilmedi.</summary>
        Failed,

        /// <summary>Ctrl+V gönderildi; dikte metni panoda KALDI (RestoreClipboard kapalı).</summary>
        Pasted,

        /// <summary>Ctrl+V gönderildi ve kullanıcının önceki pano içeriği geri yüklendi; dikte metni panoda DEĞİL.</summary>
        PastedClipboardRestored,

        /// <summary>
        /// Hedef pencere yükseltilmiş (Elevated) ve TRWhisper standart kullanıcı: UIPI nedeniyle
        /// SendInput yok sayılırdı. Metin panoya kopyalandı, kullanıcının elle Ctrl+V yapması gerekiyor.
        /// </summary>
        ElevatedTargetCopiedOnly,

        /// <summary>KEYEVENTF_UNICODE ile karakter karakter yazıldı; panoya hiç dokunulmadı.</summary>
        DirectTyped
    }

    public interface IClipboardPaster
    {
        /// <summary>
        /// Yapılandırmadaki PasteMode'a göre metni hedef pencereye ulaştırır: panoya yazıp Ctrl+V
        /// simüle eder ("Clipboard") ya da doğrudan karakter karakter yazar ("DirectType").
        /// Sonuç, metnin panoda kalıp kalmadığını ve elle yapıştırma gerekip gerekmediğini bildirir.
        /// </summary>
        Task<PasteResult> PasteTextAsync(string text);

        /// <summary>
        /// Panoyu hiç kullanmadan, KEYEVENTF_UNICODE ile metni ön plandaki pencereye karakter
        /// karakter yazar. Yükseltilmiş hedeflerde UIPI nedeniyle çalışmaz.
        /// </summary>
        Task DirectTypeAsync(string text);

        /// <summary>
        /// Ön plandaki pencerenin sahibi sürecin yükseltilmiş (Elevated / Admin) olup olmadığı.
        /// Sorgu başarısız olursa false döner.
        /// </summary>
        bool IsForegroundWindowElevated();
    }
}
