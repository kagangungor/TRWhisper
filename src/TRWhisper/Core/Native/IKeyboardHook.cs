using System;

namespace TRWhisper.Core.Native
{
    public record HotkeyEventArgs(bool IsLlmModifierActive);

    public interface IKeyboardHook : IDisposable
    {
        /// <summary>
        /// Sağ Ctrl tuşuna basıldığında tetiklenir.
        /// </summary>
        event EventHandler<HotkeyEventArgs>? HotkeyDown;

        /// <summary>
        /// Sağ Ctrl tuşu bırakıldığında tetiklenir.
        /// </summary>
        event EventHandler<HotkeyEventArgs>? HotkeyUp;

        /// <summary>
        /// Hızlı dil geçişi kısayoluna (varsayılan Alt+L) basıldığında tetiklenir. Ayar
        /// kapalıyken hiç tetiklenmez.
        /// </summary>
        event EventHandler? LanguageSwitchRequested;

        /// <summary>
        /// Hook başlatılır.
        /// </summary>
        void Start();

        /// <summary>
        /// Hook durdurulur.
        /// </summary>
        void Stop();

        /// <summary>
        /// Şu anda tuş basılı mı?
        /// </summary>
        bool IsHotkeyHeld { get; }

        /// <summary>
        /// Kısayol yapılandırmasını yeniden yükler.
        /// </summary>
        void ReloadConfig();
    }
}
