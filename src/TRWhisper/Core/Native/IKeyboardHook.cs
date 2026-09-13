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
    }
}
