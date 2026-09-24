using Xunit;

namespace TRWhisper.Tests
{
    /// <summary>
    /// Gerçek WPF penceresi açan testler birbirleriyle paralel koşmamalı: her biri kendi STA
    /// thread'inde pencere kurar ve WPF'in paylaşılan iç durumu (ör. WindowChrome'un özellik
    /// değişim sözlüğü) eşzamanlı erişimde bozulup XamlParseException fırlatır.
    /// </summary>
    [CollectionDefinition(Name)]
    public class WpfWindowCollection
    {
        public const string Name = "WPF pencereleri";
    }
}
