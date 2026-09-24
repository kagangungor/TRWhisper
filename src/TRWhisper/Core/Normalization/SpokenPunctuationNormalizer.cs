using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TRWhisper.Core.Config;

namespace TRWhisper.Core.Normalization
{
    /// <summary>
    /// Sesli noktalama ve satır komutlarını gerçek işaretlere çevirir
    /// ("merhaba nokta yeni satır nasılsın soru işareti" → "merhaba.\nNasılsın?").
    ///
    /// Yalnızca bağımsız kelime öbekleri eşleşir ("noktalamak", "virgüllü" bozulmaz).
    /// Whisper'ın komutun yanına kendiliğinden koyduğu noktalama ("dünya, nokta.") komutla
    /// birlikte yutulur; aksi hâlde "dünya,." gibi çift işaret oluşurdu.
    /// </summary>
    public class SpokenPunctuationNormalizer
    {
        private static readonly CultureInfo Turkish = new("tr-TR");

        /// <summary>
        /// Attach: önceki kelimeye bitişik (. , ? ! : ; ...). Open: önünde boşluk, arkasında yok.
        /// Close: önünde boşluk yok, arkasında var. Join: iki yanı bitişik (e-posta).
        /// Spaced: iki yanı boşluklu (—). Break: satır sonu.
        /// </summary>
        private enum Kind { Attach, Open, Close, Join, Spaced, Break }

        private static readonly Dictionary<string, (string Symbol, Kind Kind)> Commands = new()
        {
            ["nokta"] = (".", Kind.Attach),
            ["virgül"] = (",", Kind.Attach),
            ["soru işareti"] = ("?", Kind.Attach),
            ["ünlem"] = ("!", Kind.Attach),
            ["ünlem işareti"] = ("!", Kind.Attach),
            ["iki nokta"] = (":", Kind.Attach),
            ["iki nokta üst üste"] = (":", Kind.Attach),
            ["noktalı virgül"] = (";", Kind.Attach),
            ["üç nokta"] = ("...", Kind.Attach),
            ["tire"] = ("-", Kind.Join),
            ["kısa çizgi"] = ("-", Kind.Join),
            ["uzun çizgi"] = ("—", Kind.Spaced),
            ["yeni satır"] = ("\n", Kind.Break),
            ["satır başı"] = ("\n", Kind.Break),
            ["alt satır"] = ("\n", Kind.Break),
            ["yeni paragraf"] = ("\n\n", Kind.Break),
            ["parantez aç"] = ("(", Kind.Open),
            ["parantez kapa"] = (")", Kind.Close),
            ["parantezi kapat"] = (")", Kind.Close),
            ["tırnak aç"] = ("\"", Kind.Open),
            ["tırnak işareti aç"] = ("\"", Kind.Open),
            ["tırnak kapa"] = ("\"", Kind.Close),
            ["tırnak işareti kapat"] = ("\"", Kind.Close),
        };

        private static readonly HashSet<string> SentenceEnders = new() { ".", "?", "!", "...", "\n", "\n\n" };

        /// <summary>Komutun hemen yanındaki, Whisper'ın eklediği noktalama ve boşluklar.</summary>
        private static readonly char[] AdjacentNoise = { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?' };

        // En uzun öbek önce denenir ("iki nokta üst üste" > "iki nokta" > "nokta").
        // Eşleşme tr-TR ile küçültülmüş kopyada yapılır; bu yüzden IgnoreCase gerekmez
        // (i/İ, ı/I eşlemesi kültüre göre doğru kalır).
        private static readonly Regex CommandRegex = new(
            @"(?<![\p{L}\p{N}])(?:"
            + string.Join("|", Commands.Keys
                .OrderByDescending(k => k.Length)
                .Select(k => string.Join(@"\s+", k.Split(' ').Select(Regex.Escape))))
            + @")(?![\p{L}\p{N}'’])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        private readonly ConfigManager _configManager;

        public SpokenPunctuationNormalizer(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        public string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !_configManager.Current.General.EnableSpokenPunctuation)
            {
                return text;
            }

            // tr-TR küçültme uzunluğu korur (İ→i, I→ı); indeksler orijinal metne birebir uyar.
            var matches = CommandRegex.Matches(text.ToLower(Turkish));
            if (matches.Count == 0)
            {
                return text;
            }

            var sb = new StringBuilder(text.Length);
            Kind? prev = null;
            bool capitalize = false;
            int pos = 0;

            foreach (Match m in matches)
            {
                var (symbol, kind) = Commands[Whitespace.Replace(m.Value, " ")];

                var segment = text[pos..m.Index];
                if (kind != Kind.Open)
                {
                    segment = segment.TrimEnd(AdjacentNoise);
                }
                AppendText(sb, segment, prev, ref capitalize);
                AppendCommand(sb, symbol, kind, prev);

                if (SentenceEnders.Contains(symbol)) capitalize = true;
                else if (kind is Kind.Attach or Kind.Join or Kind.Spaced) capitalize = false;

                prev = kind;
                pos = m.Index + m.Length;
            }

            AppendText(sb, text[pos..], prev, ref capitalize);
            return sb.ToString();
        }

        private static void AppendText(StringBuilder sb, string segment, Kind? prev, ref bool capitalize)
        {
            if (prev == null)
            {
                sb.Append(segment);
                return;
            }

            segment = segment.TrimStart(AdjacentNoise);
            if (segment.Length == 0)
            {
                return;
            }

            if (prev is Kind.Attach or Kind.Close or Kind.Spaced)
            {
                sb.Append(' ');
            }

            if (capitalize)
            {
                segment = char.ToUpper(segment[0], Turkish) + segment[1..];
                capitalize = false;
            }
            sb.Append(segment);
        }

        private static void AppendCommand(StringBuilder sb, string symbol, Kind kind, Kind? prev)
        {
            TrimEndSpaces(sb);

            if (kind is Kind.Open or Kind.Spaced
                && sb.Length > 0 && sb[^1] != '\n' && prev != Kind.Open && prev != Kind.Join)
            {
                sb.Append(' ');
            }
            sb.Append(symbol);
        }

        private static void TrimEndSpaces(StringBuilder sb)
        {
            int end = sb.Length;
            while (end > 0 && sb[end - 1] is ' ' or '\t') end--;
            sb.Length = end;
        }
    }
}
