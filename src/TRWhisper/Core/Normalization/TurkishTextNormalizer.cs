using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TRWhisper.Core.Config;

namespace TRWhisper.Core.Normalization
{
    /// <summary>
    /// Türkçe dikte çıktısındaki sayı, yüzde, para, tarih, saat ve ölçü ifadelerini
    /// iş yazışmasına uygun rakamsal biçime çevirir ("yüzde yirmi" → "%20").
    ///
    /// Kural tabanlıdır: metin kelimelere ayrılır, sayı öbekleri (run) bulunur ve her öbek
    /// KENDİ BAĞLAMINA göre değerlendirilir. Bağlamsız küçük sayılar bilerek dokunulmadan
    /// bırakılır ("bir bardak su" bozulmamalı) — bkz. <see cref="ShouldConvertBare"/>.
    /// </summary>
    public class TurkishTextNormalizer
    {
        private static readonly CultureInfo Turkish = new("tr-TR");

        private readonly ConfigManager _configManager;

        public TurkishTextNormalizer(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        // ------------------------------------------------------------------ sözlükler

        private static readonly Dictionary<string, long> Ones = new()
        {
            ["sıfır"] = 0, ["bir"] = 1, ["iki"] = 2, ["üç"] = 3, ["dört"] = 4,
            ["beş"] = 5, ["altı"] = 6, ["yedi"] = 7, ["sekiz"] = 8, ["dokuz"] = 9,
        };

        private static readonly Dictionary<string, long> Tens = new()
        {
            ["on"] = 10, ["yirmi"] = 20, ["otuz"] = 30, ["kırk"] = 40, ["elli"] = 50,
            ["altmış"] = 60, ["yetmiş"] = 70, ["seksen"] = 80, ["doksan"] = 90,
        };

        private static readonly Dictionary<string, long> Scales = new()
        {
            ["bin"] = 1_000L, ["milyon"] = 1_000_000L, ["milyar"] = 1_000_000_000L,
        };

        private static readonly string[] OnesWord =
            { "sıfır", "bir", "iki", "üç", "dört", "beş", "altı", "yedi", "sekiz", "dokuz" };

        private static readonly string[] TensWord =
            { "", "on", "yirmi", "otuz", "kırk", "elli", "altmış", "yetmiş", "seksen", "doksan" };

        private static readonly Dictionary<string, string> Months = new()
        {
            ["ocak"] = "Ocak", ["şubat"] = "Şubat", ["mart"] = "Mart", ["nisan"] = "Nisan",
            ["mayıs"] = "Mayıs", ["haziran"] = "Haziran", ["temmuz"] = "Temmuz",
            ["ağustos"] = "Ağustos", ["eylül"] = "Eylül", ["ekim"] = "Ekim",
            ["kasım"] = "Kasım", ["aralık"] = "Aralık",
        };

        private static readonly Dictionary<string, string> Currencies = new()
        {
            ["lira"] = "TL", ["tl"] = "TL", ["türklirası"] = "TL",
            ["dolar"] = "USD", ["usd"] = "USD",
            ["euro"] = "EUR", ["avro"] = "EUR", ["eur"] = "EUR",
            ["sterlin"] = "GBP", ["pound"] = "GBP",
        };

        private static readonly Dictionary<string, string> Units = new()
        {
            ["kilometre"] = "km", ["km"] = "km",
            ["metre"] = "m",
            ["santimetre"] = "cm", ["santim"] = "cm",
            ["milimetre"] = "mm",
            ["kilogram"] = "kg", ["kilo"] = "kg", ["kg"] = "kg",
            ["gram"] = "g",
            ["miligram"] = "mg",
            ["litre"] = "L",
            ["mililitre"] = "ml",
            ["ton"] = "ton",
        };

        /// <summary>
        /// Sayı kelimesi + ek gibi GÖRÜNEN ama aslında sayı olmayan yaygın kelimeler.
        /// "onu/ona/onun" (zamir) ve "bira/biraz" hiçbir koşulda 10 veya 1 olarak okunmamalı.
        /// </summary>
        private static readonly HashSet<string> NeverNumber = new(StringComparer.Ordinal)
        {
            "onu", "ona", "onun", "onda", "ondan", "onla", "onunla",
            "onlar", "onları", "onların", "onlara", "onlarda", "onlardan",
            "onca", "onar",
            "biri", "birini", "birine", "birinin", "biriyle", "birer", "birden",
            "bira", "biraz", "birkaç", "birçok", "bire", "birde",
            "ikide", "ikiye", "üçe",
            "binde", "binen", "bine", "bini",
            "yüze", "yüzü", "yüzün", "yüzde", "yüzden", "yüzle",
        };

        /// <summary>
        /// Çarpansız hâlde fiil olarak da okunabilen sayı kelimeleri: "otobüse bin",
        /// "denizde yüz". ("milyon"/"milyar" için böyle bir eşsesli yok.)
        /// </summary>
        private static readonly HashSet<string> AmbiguousBareScales = new(StringComparer.Ordinal)
        {
            "bin", "yüz",
        };

        /// <summary>
        /// Çıplak "bin"/"yüz"ün sayı sayıldığını kanıtlayan ardıl kelimeler: sayılan isimler,
        /// para ve ölçü birimleri. Bu liste yalnızca o iki kelimenin kararında kullanılır.
        /// </summary>
        private static readonly HashSet<string> CountableNouns = new(StringComparer.Ordinal)
        {
            // sayılan isimler
            "kişi", "tane", "adet", "parça", "kalem", "defa", "kere", "kez",
            "yıl", "ay", "hafta", "gün", "saat", "dakika", "saniye",
            "sayfa", "satır", "kelime", "ev", "araba", "ürün", "dosya", "müşteri",
            "öğrenci", "çalışan", "katılımcı", "koli", "kutu", "paket",
            // para birimleri
            "lira", "tl", "dolar", "usd", "euro", "avro", "eur", "sterlin", "pound",
            // ölçü birimleri
            "kilometre", "km", "metre", "metrekare", "santimetre", "santim", "milimetre",
            "kilogram", "kilo", "kg", "gram", "miligram", "litre", "mililitre", "ton",
        };

        /// <summary>Kelimeden ayrılabilecek Türkçe çekim ekleri (uzundan kısaya denenir).</summary>
        private static readonly string[] Suffixes =
        {
            "lardan", "lerden", "ların", "lerin", "larla", "lerle", "larda", "lerde",
            "lara", "lere", "ları", "leri", "lar", "ler",
            "ndan", "nden", "dan", "den", "tan", "ten",
            "nın", "nin", "nun", "nün", "ın", "in", "un", "ün",
            "nda", "nde", "da", "de", "ta", "te",
            "yla", "yle", "la", "le",
            "yı", "yi", "yu", "yü", "nı", "ni", "nu", "nü",
            "ya", "ye", "na", "ne",
            "dır", "dir", "dur", "dür", "tır", "tir", "tur", "tür",
            "ca", "ce", "ça", "çe",
            "ı", "i", "u", "ü", "a", "e",
        };

        /// <summary>Konuşulan ek → şablon. Büyük harfler çözümlenir: A=a/e, I=ı/i/u/ü, D=d/t, C=c/ç.</summary>
        private static readonly Dictionary<string, string> SuffixTemplates = new(StringComparer.Ordinal)
        {
            ["da"] = "DA", ["de"] = "DA", ["ta"] = "DA", ["te"] = "DA",
            ["nda"] = "nDA", ["nde"] = "nDA",
            ["dan"] = "DAn", ["den"] = "DAn", ["tan"] = "DAn", ["ten"] = "DAn",
            ["ndan"] = "nDAn", ["nden"] = "nDAn",
            ["a"] = "A", ["e"] = "A", ["ya"] = "yA", ["ye"] = "yA", ["na"] = "nA", ["ne"] = "nA",
            ["ı"] = "I", ["i"] = "I", ["u"] = "I", ["ü"] = "I",
            ["yı"] = "yI", ["yi"] = "yI", ["yu"] = "yI", ["yü"] = "yI",
            ["nı"] = "nI", ["ni"] = "nI", ["nu"] = "nI", ["nü"] = "nI",
            ["ın"] = "In", ["in"] = "In", ["un"] = "In", ["ün"] = "In",
            ["nın"] = "nIn", ["nin"] = "nIn", ["nun"] = "nIn", ["nün"] = "nIn",
            ["la"] = "lA", ["le"] = "lA", ["yla"] = "ylA", ["yle"] = "ylA",
            ["lar"] = "lAr", ["ler"] = "lAr",
            ["larda"] = "lArDA", ["lerde"] = "lArDA",
            ["lardan"] = "lArDAn", ["lerden"] = "lArDAn",
            ["ları"] = "lArI", ["leri"] = "lArI",
            ["ların"] = "lArIn", ["lerin"] = "lArIn",
            ["lara"] = "lArA", ["lere"] = "lArA",
            ["larla"] = "lArlA", ["lerle"] = "lArlA",
            ["dır"] = "DIr", ["dir"] = "DIr", ["dur"] = "DIr", ["dür"] = "DIr",
            ["tır"] = "DIr", ["tir"] = "DIr", ["tur"] = "DIr", ["tür"] = "DIr",
            ["ca"] = "CA", ["ce"] = "CA", ["ça"] = "CA", ["çe"] = "CA",
        };

        // ------------------------------------------------------------------ genel akış

        public string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (!_configManager.Current.General.EnableTextNormalization) return text;

            var parts = Tokenize(text);
            var wordPos = new List<int>();
            for (int i = 0; i < parts.Count; i++)
                if (IsWordPart(parts[i])) wordPos.Add(i);

            if (wordPos.Count == 0) return text;

            var lower = wordPos.Select(i => parts[i].ToLower(Turkish)).ToList();
            var ctx = new Context(parts, wordPos, lower);

            int w = 0;
            while (w < lower.Count)
            {
                int consumed = TryRules(ctx, w);
                w += consumed > 0 ? consumed : 1;
            }

            return string.Concat(parts);
        }

        private sealed class Context
        {
            public readonly List<string> Parts;
            public readonly List<int> WordPos;
            public readonly List<string> Lower;

            public Context(List<string> parts, List<int> wordPos, List<string> lower)
            {
                Parts = parts; WordPos = wordPos; Lower = lower;
            }

            public int Count => Lower.Count;

            /// <summary>İki kelime arasında yalnızca boşluk varsa birleştirilebilir; virgül/nokta
            /// varsa kalıp o noktada kesilmeli (aksi hâlde noktalama silinir).</summary>
            public bool Joinable(int wA, int wB)
            {
                for (int p = WordPos[wA] + 1; p < WordPos[wB]; p++)
                    if (!Parts[p].All(c => c == ' ' || c == '\t')) return false;
                return true;
            }

            public void Replace(int startWord, int wordCount, string replacement)
            {
                int first = WordPos[startWord];
                int last = WordPos[startWord + wordCount - 1];
                Parts[first] = replacement;
                for (int p = first + 1; p <= last; p++) Parts[p] = "";
            }
        }

        private static int TryRules(Context ctx, int w)
        {
            int n;
            if ((n = TryPercent(ctx, w)) > 0) return n;
            if ((n = TryTime(ctx, w)) > 0) return n;

            var run = ParseRun(ctx, w);
            if (!run.Ok) return 0;

            if ((n = TryDate(ctx, w, run)) > 0) return n;
            if ((n = TryCurrencyOrUnit(ctx, w, run)) > 0) return n;
            return TryBare(ctx, w, run);
        }

        // ------------------------------------------------------------------ sayı öbeği

        private readonly struct NumRun
        {
            public bool Ok { get; init; }
            public decimal Value { get; init; }
            public int WordCount { get; init; }
            public string Suffix { get; init; }
            public bool Half { get; init; }

            public bool SingleWord => WordCount == 1;
            public bool IsInteger => Value == decimal.Truncate(Value);
        }

        private enum Kind { One, Ten, Hundred, Scale, Half }

        private static bool TryNumberWord(string word, out Kind kind, out long value)
        {
            if (Ones.TryGetValue(word, out value)) { kind = Kind.One; return true; }
            if (Tens.TryGetValue(word, out value)) { kind = Kind.Ten; return true; }
            if (word == "yüz") { kind = Kind.Hundred; value = 100; return true; }
            if (Scales.TryGetValue(word, out value)) { kind = Kind.Scale; return true; }
            if (word == "buçuk") { kind = Kind.Half; value = 0; return true; }
            kind = Kind.One; value = 0; return false;
        }

        /// <summary>
        /// "beşte" → ("beş","te"). Zamir/kalıp yanlış pozitiflerini ("onu", "bira") engellemek
        /// için önce <see cref="NeverNumber"/> listesi kontrol edilir.
        /// </summary>
        private static bool TryStripSuffix(string word, out string bare, out string suffix)
        {
            bare = word; suffix = "";
            if (NeverNumber.Contains(word)) return false;

            foreach (var s in Suffixes.OrderByDescending(x => x.Length))
            {
                if (word.Length <= s.Length || !word.EndsWith(s, StringComparison.Ordinal)) continue;
                var candidate = word.Substring(0, word.Length - s.Length);
                if (TryNumberWord(candidate, out _, out _))
                {
                    bare = candidate; suffix = s; return true;
                }
            }
            return false;
        }

        /// <summary>
        /// w'den başlayan en uzun geçerli Türkçe sayı öbeğini ayrıştırır. Türkçe sayı dizilişi
        /// [yüzler][onlar][birler] olduğundan, bu sırayı bozan kelime öbeği BİTİRİR — bu sayede
        /// "on dört otuz" tek sayı (44) değil, iki ayrı sayı (14 ve 30) olarak okunur ki
        /// saat kalıbı çalışabilsin.
        /// </summary>
        private static NumRun ParseRun(Context ctx, int start)
        {
            decimal total = 0, group = 0;
            bool sawHundred = false, sawTen = false, sawUnit = false;
            bool any = false, half = false, afterHalf = false;
            long lastScale = long.MaxValue;
            string suffix = "";
            int i = start;

            while (i < ctx.Count)
            {
                if (i > start && !ctx.Joinable(i - 1, i)) break;

                var word = ctx.Lower[i];
                string bare = word, sfx = "";

                if (!TryNumberWord(word, out var kind, out var value))
                {
                    if (!TryStripSuffix(word, out bare, out sfx)) break;
                    TryNumberWord(bare, out kind, out value);
                }

                // "buçuk"tan sonra yalnızca basamak gelebilir ("üç buçuk milyon"),
                // başka bir sayı kelimesi gelemez ("üç buçuk dört" iki ayrı sayıdır).
                if (afterHalf && kind != Kind.Scale) break;

                if (kind == Kind.Half)
                {
                    if (!any || half) break;   // tek başına "buçuk" sayı değil
                    half = true; afterHalf = true; i++; suffix = sfx;
                    if (sfx.Length > 0) break; // "buçukta" öbeği bitirir
                    continue;
                }

                if (kind == Kind.One)
                {
                    if (sawUnit) break;         // "üç dört" → ayrı sayılar
                    group += value; sawUnit = true;
                }
                else if (kind == Kind.Ten)
                {
                    if (sawTen || sawUnit) break;
                    group += value; sawTen = true;
                }
                else if (kind == Kind.Hundred)
                {
                    if (sawHundred || sawTen) break;
                    group = (sawUnit ? group : 1) * 100;
                    sawHundred = true; sawUnit = false;
                }
                else // Scale
                {
                    if (value >= lastScale) break;   // "bin milyon" geçersiz
                    // "üç buçuk milyon" = 3,5 milyon: yarım, basamakla çarpılan gruba dahildir.
                    var groupValue = group + (half ? 0.5m : 0m);
                    total += (groupValue == 0 ? 1 : groupValue) * value;
                    group = 0; lastScale = value;
                    sawHundred = sawTen = sawUnit = false;
                    half = false; afterHalf = false;
                }

                any = true;
                i++;

                if (sfx.Length > 0) { suffix = sfx; break; }   // ekli kelime öbeği bitirir
            }

            if (!any) return new NumRun { Ok = false };

            return new NumRun
            {
                Ok = true,
                Value = total + group + (half ? 0.5m : 0m),
                WordCount = i - start,
                Suffix = suffix,
                Half = half
            };
        }

        // ------------------------------------------------------------------ kalıplar

        private static int TryPercent(Context ctx, int w)
        {
            if (ctx.Lower[w] != "yüzde") return 0;
            if (w + 1 >= ctx.Count || !ctx.Joinable(w, w + 1)) return 0;

            var run = ParseRun(ctx, w + 1);
            if (!run.Ok) return 0;

            var text = "%" + FormatNumber(run.Value) + SuffixFor(run.Value, run.Suffix);
            ctx.Replace(w, 1 + run.WordCount, text);
            return 1 + run.WordCount;
        }

        private static int TryTime(Context ctx, int w)
        {
            if (ctx.Lower[w] != "saat") return 0;
            if (w + 1 >= ctx.Count || !ctx.Joinable(w, w + 1)) return 0;

            var hourRun = ParseRun(ctx, w + 1);
            if (!hourRun.Ok || hourRun.Value < 0) return 0;

            int consumed = hourRun.WordCount;
            long hour;
            int minute;
            string suffix = hourRun.Suffix;
            decimal anchor;

            if (hourRun.Half)
            {
                hour = (long)decimal.Truncate(hourRun.Value);
                minute = 30;
                anchor = 30;
            }
            else
            {
                if (!hourRun.IsInteger) return 0;
                hour = (long)hourRun.Value;
                minute = -1;
                anchor = hourRun.Value;

                int next = w + 1 + hourRun.WordCount;
                if (suffix.Length == 0 && next < ctx.Count && ctx.Joinable(next - 1, next))
                {
                    var minRun = ParseRun(ctx, next);
                    if (minRun.Ok && minRun.IsInteger && !minRun.Half &&
                        minRun.Value >= 0 && minRun.Value <= 59)
                    {
                        minute = (int)minRun.Value;
                        consumed += minRun.WordCount;
                        suffix = minRun.Suffix;
                        anchor = minRun.Value;
                    }
                }
            }

            if (hour > 23) return 0;

            // Yalnızca saat söylenmişse uydurma ":00" eklenmez; "saat 15'te" doğal Türkçedir.
            var text = minute >= 0
                ? $"{hour:00}:{minute:00}"
                : FormatNumber(hour);

            ctx.Replace(w + 1, consumed, text + SuffixFor(anchor, suffix));
            return 1 + consumed;
        }

        private static int TryDate(Context ctx, int w, NumRun day)
        {
            if (!day.IsInteger || day.Half || day.Suffix.Length > 0) return 0;
            if (day.Value < 1 || day.Value > 31) return 0;

            int monthIdx = w + day.WordCount;
            if (monthIdx >= ctx.Count || !ctx.Joinable(monthIdx - 1, monthIdx)) return 0;

            var monthWord = ctx.Lower[monthIdx];
            string monthSuffix = "";
            if (!Months.TryGetValue(monthWord, out var monthName))
            {
                foreach (var s in Suffixes.OrderByDescending(x => x.Length))
                {
                    if (monthWord.Length <= s.Length || !monthWord.EndsWith(s, StringComparison.Ordinal)) continue;
                    var candidate = monthWord.Substring(0, monthWord.Length - s.Length);
                    if (Months.TryGetValue(candidate, out monthName)) { monthSuffix = s; break; }
                }
                if (monthName == null) return 0;
            }

            var sb = new StringBuilder();
            sb.Append((long)day.Value).Append(' ').Append(monthName);
            int consumed = day.WordCount + 1;

            // İsteğe bağlı yıl: "otuz bir aralık iki bin yirmi dört"
            int yearIdx = monthIdx + 1;
            if (monthSuffix.Length == 0 && yearIdx < ctx.Count && ctx.Joinable(yearIdx - 1, yearIdx))
            {
                var yearRun = ParseRun(ctx, yearIdx);
                if (yearRun.Ok && yearRun.IsInteger && !yearRun.Half &&
                    yearRun.Value >= 1000 && yearRun.Value <= 2999)
                {
                    sb.Append(' ').Append((long)yearRun.Value);
                    consumed += yearRun.WordCount;
                    monthSuffix = yearRun.Suffix;
                    if (monthSuffix.Length > 0)
                        sb.Append(SuffixFor(yearRun.Value, monthSuffix));
                    monthSuffix = "";
                }
            }

            if (monthSuffix.Length > 0)
                sb.Append(SuffixForWord(monthName, monthSuffix));

            ctx.Replace(w, consumed, sb.ToString());
            return consumed;
        }

        private static int TryCurrencyOrUnit(Context ctx, int w, NumRun run)
        {
            if (run.Suffix.Length > 0) return 0;

            int idx = w + run.WordCount;
            if (idx >= ctx.Count || !ctx.Joinable(idx - 1, idx)) return 0;

            var word = ctx.Lower[idx];
            if (!Currencies.TryGetValue(word, out var symbol) && !Units.TryGetValue(word, out symbol))
                return 0;

            ctx.Replace(w, run.WordCount + 1, FormatNumber(run.Value) + " " + symbol);
            return run.WordCount + 1;
        }

        private static int TryBare(Context ctx, int w, NumRun run)
        {
            if (!ShouldConvertBare(ctx, w, run)) return 0;
            ctx.Replace(w, run.WordCount, FormatNumber(run.Value) + SuffixFor(run.Value, run.Suffix));
            return run.WordCount;
        }

        /// <summary>
        /// Bağlamsız sayı dönüşümü kuralı: yalnızca iki ve daha fazla basamaklı sayılar
        /// rakama çevrilir. "bir bardak su" / "bir gün" gibi ifadeler bu sayede korunur.
        /// Tek kelimelik EKLİ sayılar hiç çevrilmez ("onda", "beşe" çoğu zaman sayı değildir).
        ///
        /// Ek koruma: çarpanı olmayan çıplak "bin"/"yüz" aynı zamanda FİİLDİR
        /// ("otobüse bin", "denizde yüz"). Bu iki kelime yalnızca ardından sayılan bir
        /// isim/birim geliyorsa rakama çevrilir ("bin kişi" → "1000 kişi").
        /// </summary>
        private static bool ShouldConvertBare(Context ctx, int w, NumRun run)
        {
            if (run.SingleWord && run.Suffix.Length > 0) return false;
            if (Math.Abs(run.Value) < 10m) return false;

            if (run.SingleWord && AmbiguousBareScales.Contains(ctx.Lower[w]))
                return FollowedByCountable(ctx, w + run.WordCount);

            return true;
        }

        private static bool FollowedByCountable(Context ctx, int idx)
        {
            if (idx >= ctx.Count || !ctx.Joinable(idx - 1, idx)) return false;

            var word = ctx.Lower[idx];
            if (CountableNouns.Contains(word)) return true;

            // "bin kişiyle", "yüz günde" gibi ekli biçimler de sayılan isimdir. İlk eşleşen ekte
            // durulmaz: "günde" önce "nde" ile eşleşip "gü" verir, doğru ayrım "de" + "gün"dür.
            foreach (var s in Suffixes.OrderByDescending(x => x.Length))
            {
                if (word.Length <= s.Length || !word.EndsWith(s, StringComparison.Ordinal)) continue;
                if (CountableNouns.Contains(word.Substring(0, word.Length - s.Length))) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ biçimlendirme

        private static string FormatNumber(decimal value)
            => value == decimal.Truncate(value)
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.###", Turkish);

        private static string SuffixFor(decimal value, string spokenSuffix)
            => SuffixForWord(LastNumberWord(value), spokenSuffix);

        /// <summary>
        /// Eki, KONUŞULDUĞU gibi değil, yazılan sayının okunuşuna göre yeniden üretir:
        /// "iki buçukta" → "02:30'da" ("otuz" sonu 'z', ünlüsü 'u' → "da"), "buçuk"un
        /// getirdiği "ta" değil. Türkçe ses uyumu rakamla değil okunuşla belirlenir.
        /// </summary>
        private static string SuffixForWord(string anchor, string spokenSuffix)
        {
            if (string.IsNullOrEmpty(spokenSuffix)) return "";
            if (!SuffixTemplates.TryGetValue(spokenSuffix, out var template))
                return "'" + spokenSuffix;

            char lastVowel = 'a';
            for (int i = anchor.Length - 1; i >= 0; i--)
                if (IsVowel(anchor[i])) { lastVowel = anchor[i]; break; }

            bool voiceless = anchor.Length > 0 && IsVoiceless(anchor[^1]);
            return "'" + RenderTemplate(template, lastVowel, voiceless);
        }

        private static string RenderTemplate(string template, char lastVowel, bool prevVoiceless)
        {
            var sb = new StringBuilder(template.Length + 2);
            foreach (var t in template)
            {
                switch (t)
                {
                    case 'A':
                        {
                            char c = IsBack(lastVowel) ? 'a' : 'e';
                            sb.Append(c); lastVowel = c; prevVoiceless = false;
                            break;
                        }
                    case 'I':
                        {
                            char c = IsBack(lastVowel)
                                ? (IsRounded(lastVowel) ? 'u' : 'ı')
                                : (IsRounded(lastVowel) ? 'ü' : 'i');
                            sb.Append(c); lastVowel = c; prevVoiceless = false;
                            break;
                        }
                    case 'D':
                        {
                            char c = prevVoiceless ? 't' : 'd';
                            sb.Append(c); prevVoiceless = c == 't';
                            break;
                        }
                    case 'C':
                        {
                            char c = prevVoiceless ? 'ç' : 'c';
                            sb.Append(c); prevVoiceless = c == 'ç';
                            break;
                        }
                    default:
                        sb.Append(t);
                        prevVoiceless = IsVoiceless(t);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Sayının okunuşundaki SON kelime; ses uyumu buna göre hesaplanır (15 → "beş", 30 → "otuz").</summary>
        private static string LastNumberWord(decimal value)
        {
            if (value != decimal.Truncate(value))
            {
                var formatted = FormatNumber(value);
                char last = formatted[^1];
                return char.IsDigit(last) ? OnesWord[last - '0'] : "beş";
            }

            long n = Math.Abs((long)value);
            if (n == 0) return "sıfır";

            int d1 = (int)(n % 10);
            if (d1 != 0) return OnesWord[d1];

            int d2 = (int)(n % 100);
            if (d2 != 0) return TensWord[d2 / 10];

            if (n % 1_000_000_000L == 0) return "milyar";
            if (n % 1_000_000L == 0) return "milyon";
            if (n % 1_000L == 0) return "bin";
            return "yüz";
        }

        private static bool IsVowel(char c) => "aeıioöuü".IndexOf(c) >= 0;
        private static bool IsBack(char c) => "aıou".IndexOf(c) >= 0;
        private static bool IsRounded(char c) => "oöuü".IndexOf(c) >= 0;
        private static bool IsVoiceless(char c) => "fstkçşhp".IndexOf(c) >= 0;

        // ------------------------------------------------------------------ belirteçleme

        private static List<string> Tokenize(string text)
        {
            var parts = new List<string>();
            foreach (Match m in Regex.Matches(text, @"[\p{L}\p{Nd}]+|[^\p{L}\p{Nd}]+"))
                parts.Add(m.Value);
            return parts;
        }

        private static bool IsWordPart(string part)
            => part.Length > 0 && char.IsLetterOrDigit(part[0]);
    }
}
