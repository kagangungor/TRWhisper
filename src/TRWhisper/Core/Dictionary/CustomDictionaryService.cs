using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Dictionary
{
    /// <summary>
    /// Tek bir sözlük kuralı: Whisper'ın ürettiği Türkçe fonetik yazımı (<see cref="Match"/>)
    /// doğru yazımla (<see cref="Replace"/>) değiştirir.
    /// </summary>
    public class DictionaryRule
    {
        /// <summary>Aranacak (hatalı/fonetik) yazım, ör. "piton". Boşluk içerebilir: "si şarp".</summary>
        public string Match { get; set; } = "";

        /// <summary>Yerine yazılacak doğru biçim, ör. "Python".</summary>
        public string Replace { get; set; } = "";

        /// <summary>
        /// true ise kelimeye bitişik Türkçe çekim eki kesme işaretiyle ayrılarak korunur
        /// ("pitonda" -> "Python'da"). false ise ek varsa kural hiç uygulanmaz.
        /// </summary>
        public bool PreserveSuffix { get; set; } = true;

        /// <summary>true ise büyük/küçük harf birebir eşleşmeli.</summary>
        public bool CaseSensitive { get; set; } = false;
    }

    /// <summary>
    /// Özel sözlük: Whisper'a başlangıç istemi (prompt) olarak doğru terimleri verir ve
    /// çıktıyı ayrıca kurallardan geçirerek düzeltir. İki yönlü çalışır çünkü prompt tek
    /// başına garanti değildir; model yine de fonetik yazabilir.
    /// </summary>
    public class CustomDictionaryService
    {
        public const string FileName = "dictionary.json";

        /// <summary>
        /// Tüm harf dönüşümleri ve karşılaştırmalar Türkçe kültürüyle yapılır; aksi hâlde
        /// I/ı ve i/İ eşleşmeleri yanlış sonuç verir.
        /// </summary>
        private static readonly CultureInfo Turkish = new("tr-TR");

        /// <summary>
        /// whisper'ın initial_prompt'u n_text_ctx/2 = 224 token ile sınırlıdır; aşılırsa
        /// baştaki terimler sessizce kırpılır. Latin metinde ~4 karakter/token kabul edip
        /// güvenli bir üst sınır uyguluyoruz.
        /// </summary>
        private const int MaxPromptChars = 800;

        /// <summary>
        /// Kelimeye bitişik yazılan Türkçe çekim ekleri. Uzun olanlar önce denenmeli ki
        /// "pitonlardan" -> "lardan" olarak ayrılsın, "lar" + artık olarak değil.
        /// </summary>
        private static readonly string[] TurkishSuffixes =
        {
            // çoğul + hâl birleşimleri
            "lardan", "lerden", "ların", "lerin", "larla", "lerle", "larda", "lerde",
            "lara", "lere", "ları", "leri", "lar", "ler",
            // ayrılma (ablatif)
            "ndan", "nden", "dan", "den", "tan", "ten",
            // tamlayan (genitif)
            "nın", "nin", "nun", "nün", "ın", "in", "un", "ün",
            // bulunma (lokatif)
            "nda", "nde", "da", "de", "ta", "te",
            // vasıta
            "yla", "yle", "la", "le",
            // belirtme (akuzatif)
            "yı", "yi", "yu", "yü", "nı", "ni", "nu", "nü",
            // yönelme (datif)
            "ya", "ye", "na", "ne",
            // eşitlik
            "ca", "ce", "ça", "çe",
            // bildirme
            "dır", "dir", "dur", "dür", "tır", "tir", "tur", "tür",
            // iyelik
            "sı", "si", "su", "sü", "ım", "im", "um", "üm",
            // tek harfli hâl ekleri en sona
            "ı", "i", "u", "ü", "a", "e",
        };

        private static readonly string SuffixAlternation = string.Join(
            "|",
            TurkishSuffixes.Distinct()
                           .OrderByDescending(s => s.Length)
                           .ThenBy(s => s, StringComparer.Ordinal)
                           .Select(Regex.Escape));

        private readonly ConfigManager _configManager;
        private readonly string _dictionaryPath;

        // Kural dizisi ve prompt DEĞİŞTİRİLMEZ olarak tutulur: yeniden yükleme yeni bir dizi
        // atar, okuyucular kendi anlık görüntüsünü alır. Böylece Apply() kilit tutmadan
        // güvenle döngüye girer (dikte hattı ile yeniden yükleme çakışsa bile).
        private volatile CompiledRule[] _rules = Array.Empty<CompiledRule>();
        private volatile string _initialPrompt = "";

        // Ayarlar penceresinin düzenleyip geri yazabilmesi için ham kural listesi de saklanır.
        private List<DictionaryRule> _sourceRules = new();

        private readonly object _sync = new();
        private DateTime _fileStampUtc;
        private long _fileLength = -1;
        private int _revision;

        private sealed class CompiledRule
        {
            public required Regex Regex { get; init; }
            public required string Replacement { get; init; }
            public required bool PreserveSuffix { get; init; }
        }

        public CustomDictionaryService(ConfigManager configManager)
        {
            _configManager = configManager;

            var configDir = Path.GetDirectoryName(configManager.ConfigFilePath);
            if (string.IsNullOrEmpty(configDir)) configDir = AppDomain.CurrentDomain.BaseDirectory;
            _dictionaryPath = Path.Combine(configDir, FileName);

            EnsureFresh();
        }

        /// <summary>Yüklenen kural sayısı (tanı amaçlı).</summary>
        public int RuleCount => _rules.Length;

        public string DictionaryPath => _dictionaryPath;

        /// <summary>
        /// Her yeniden yüklemede artar. WhisperNetEngine bunu izler: prompt processor
        /// kurulurken gömüldüğü için, sözlük değiştiğinde processor yeniden kurulmalıdır.
        /// </summary>
        public int Revision
        {
            get
            {
                EnsureFresh();
                return Volatile.Read(ref _revision);
            }
        }

        /// <summary>
        /// Dosya diskte değiştiyse kuralları tazeler; silinmişse varsayılanlarla yeniden
        /// oluşturur. Normalde kendiliğinden çağrılır; tepsiden sözlüğü açmadan önce
        /// dosyanın var olduğundan emin olmak için de kullanılır.
        /// </summary>
        public void Refresh() => EnsureFresh();

        /// <summary>
        /// Sözlüğün o anki kural listesinin kopyasını döndürür (Ayarlar penceresi düzenler).
        /// </summary>
        public List<DictionaryRule> GetRules()
        {
            EnsureFresh();
            lock (_sync)
            {
                return _sourceRules.Select(r => new DictionaryRule
                {
                    Match = r.Match,
                    Replace = r.Replace,
                    PreserveSuffix = r.PreserveSuffix,
                    CaseSensitive = r.CaseSensitive
                }).ToList();
            }
        }

        /// <summary>
        /// Kuralları dictionary.json'a yazar ve belleği anında tazeler. Yazma başarısızsa
        /// false döner ve mevcut kurallar korunur.
        /// </summary>
        public bool SaveRules(List<DictionaryRule> rules)
        {
            lock (_sync)
            {
                try
                {
                    File.WriteAllText(
                        _dictionaryPath,
                        JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }),
                        new UTF8Encoding(false));

                    CompileLocked(rules);
                    StampLocked();
                    _revision++;
                    FileLog.Write($"[CustomDictionary] {_rules.Length} kural kaydedildi (r{_revision}).");
                    return true;
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[CustomDictionary] Sözlük kaydedilemedi: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Whisper'a verilecek başlangıç istemi: sözlükteki doğru yazımların virgülle
        /// birleştirilmiş hâli. Sözlük kapalıysa veya kural yoksa boş döner.
        /// </summary>
        public string GetInitialPrompt()
        {
            if (!_configManager.Current.General.EnableCustomDictionary) return "";
            EnsureFresh();
            return _initialPrompt;
        }

        /// <summary>
        /// Transkript metnine sözlük kurallarını uygular. Kök değiştirilir, Türkçe çekim eki
        /// kesme işaretiyle korunur ("pitonda" -> "Python'da"). Tam kelime eşleşmesi zorunludur;
        /// "pitonik" gibi türemiş sözcükler bozulmaz. Tekrar uygulanabilir (idempotent).
        /// </summary>
        public string Apply(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (!_configManager.Current.General.EnableCustomDictionary) return text;

            EnsureFresh();

            foreach (var rule in _rules)
            {
                text = rule.Regex.Replace(text, m =>
                {
                    var suffix = m.Groups["sfx"];
                    if (!suffix.Success || suffix.Length == 0) return rule.Replacement;

                    // PreserveSuffix kapalıyken ekli biçime dokunma (kural yalnızca çıplak köke uygulanır).
                    if (!rule.PreserveSuffix) return m.Value;

                    return rule.Replacement + "'" + suffix.Value.ToLower(Turkish);
                });
            }

            return text;
        }

        // ------------------------------------------------------------------ yükleme / canlı izleme

        /// <summary>
        /// dictionary.json diskte değiştiyse kuralları yeniden yükler. FileSystemWatcher yerine
        /// çağrı anında damga (LastWriteTimeUtc + uzunluk) karşılaştırması kullanılıyor: watcher
        /// tek kaydetmede birden çok olay üretir, editörler dosyayı sil-oluştur ile değiştirir ve
        /// olay geldiğinde dosya hâlâ kilitli olabilir. Damga kontrolü dikte başına bir kez
        /// çalışan tek bir stat çağrısıdır ve bu yarışların hiçbirine açık değildir.
        /// </summary>
        private void EnsureFresh()
        {
            lock (_sync)
            {
                DateTime stamp = default;
                long length = -1;
                try
                {
                    var info = new FileInfo(_dictionaryPath);
                    if (info.Exists)
                    {
                        stamp = info.LastWriteTimeUtc;
                        length = info.Length;
                    }
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[CustomDictionary] sözlük dosyası incelenemedi: {ex.Message}");
                    return;
                }

                if (_fileLength >= 0 && stamp == _fileStampUtc && length == _fileLength) return;

                LoadLocked();
            }
        }

        private void LoadLocked()
        {
            var firstLoad = _fileLength < 0;
            List<DictionaryRule>? rules = null;

            try
            {
                if (File.Exists(_dictionaryPath))
                {
                    rules = JsonSerializer.Deserialize<List<DictionaryRule>>(
                        ReadShared(_dictionaryPath),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip });
                }
                else
                {
                    rules = CreateDefaultRules();
                    File.WriteAllText(
                        _dictionaryPath,
                        JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }),
                        new UTF8Encoding(false));
                    FileLog.Write($"[CustomDictionary] varsayılan sözlük oluşturuldu: {_dictionaryPath}");
                }
            }
            catch (Exception ex)
            {
                // Kullanıcı dosyayı bozduysa çalışan kuralları KORU; dikte bozulmasın.
                // Damga yine de kaydedilir ki her diktede aynı hata tekrar loglanmasın;
                // dosya bir daha kaydedildiğinde damga değişeceği için yeniden denenir.
                FileLog.Write($"[CustomDictionary] sözlük okunamadı ({ex.Message}); " +
                              (firstLoad ? "varsayılanlar kullanılacak." : "önceki kurallar korunuyor."));
                if (!firstLoad)
                {
                    StampLocked();
                    return;
                }
                rules = CreateDefaultRules();
            }

            CompileLocked(rules ?? CreateDefaultRules());
            StampLocked();
            _revision++;

            FileLog.Write($"[CustomDictionary] {_rules.Length} kural yüklendi (r{_revision}), prompt {_initialPrompt.Length} karakter.");
        }

        /// <summary>
        /// Dosyayı YAZARI BLOKLAMADAN okur. File.ReadAllText paylaşımı FileShare.Read ile açar;
        /// kullanıcı tam o anda Not Defteri'nden kaydederse kaydetme "dosya başka bir süreç
        /// tarafından kullanılıyor" hatasıyla BAŞARISIZ olur. Sözlüğü tepsiden Not Defteri ile
        /// düzenlettiğimiz için bu çakışma gerçekçi. FileShare.ReadWrite ile biz hiç kilitlemeyiz;
        /// yarım yazılmış içeriği okursak JSON çözümlemesi hata verir, önceki kurallar korunur ve
        /// dosya bir sonraki damga değişiminde yeniden okunur.
        /// </summary>
        private static string ReadShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private void StampLocked()
        {
            try
            {
                var info = new FileInfo(_dictionaryPath);
                _fileStampUtc = info.Exists ? info.LastWriteTimeUtc : default;
                _fileLength = info.Exists ? info.Length : 0;
            }
            catch
            {
                // Damga alınamadıysa 0 bırak: bir sonraki çağrı yeniden dener.
                _fileLength = 0;
            }
        }

        private void CompileLocked(List<DictionaryRule> rules)
        {
            var compiled = new List<CompiledRule>(rules.Count);

            // Uzun eşleşmeler önce uygulanmalı: "visual studio" kuralı, "visual" kuralından
            // önce çalışmazsa çok kelimeli terim hiç yakalanamaz.
            foreach (var rule in rules.Where(r => r != null &&
                                                  !string.IsNullOrWhiteSpace(r.Match) &&
                                                  !string.IsNullOrEmpty(r.Replace))
                                      .OrderByDescending(r => r.Match.Trim().Length))
            {
                try
                {
                    compiled.Add(new CompiledRule
                    {
                        Regex = BuildRegex(rule),
                        Replacement = rule.Replace,
                        PreserveSuffix = rule.PreserveSuffix
                    });
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[CustomDictionary] kural derlenemedi ('{rule.Match}'): {ex.Message}");
                }
            }

            // Tek atama: okuyucular ya eski ya yeni diziyi görür, yarı kurulmuş olanı asla.
            _rules = compiled.ToArray();
            _initialPrompt = BuildInitialPrompt(rules);
            _sourceRules = rules.Where(r => r != null).ToList();
        }

        private static Regex BuildRegex(DictionaryRule rule)
        {
            var core = BuildCorePattern(rule.Match.Trim(), rule.CaseSensitive);

            // (?<!\w) / (?!\w): \b'nin aksine, Match'in başı/sonu harf olmasa da (ör. "c#")
            // doğru çalışan kelime sınırı. Ek grubu isteğe bağlıdır; "pitonik"te ek "i"
            // denendiğinde sondaki (?!\w) 'k' yüzünden başarısız olur ve eşleşme iptal edilir.
            var pattern = $@"(?<!\w){core}(?:(?<sfx>{SuffixAlternation}))?(?!\w)";

            var options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
            if (!rule.CaseSensitive) options |= RegexOptions.IgnoreCase;

            return new Regex(pattern, options);
        }

        /// <summary>
        /// Match dizesini regex desenine çevirir. Büyük/küçük harf duyarsız modda Türkçe
        /// i/İ ve ı/I çiftleri AÇIKÇA karakter sınıfına yazılır: RegexOptions.IgnoreCase
        /// invariant kurallarla çalışır ve 'İ' (U+0130) ile 'i' (U+0069) eşleşmesini
        /// garanti etmez. Bu yüzden "PİTON" yalnızca bu genişletmeyle yakalanır.
        /// </summary>
        private static string BuildCorePattern(string match, bool caseSensitive)
        {
            if (caseSensitive) return Regex.Escape(match);

            var sb = new StringBuilder(match.Length * 4);
            foreach (var c in match)
            {
                switch (c)
                {
                    case 'i': case 'İ': sb.Append("[iİ]"); break;
                    case 'ı': case 'I': sb.Append("[ıI]"); break;
                    default: sb.Append(Regex.Escape(c.ToString())); break;
                }
            }
            return sb.ToString();
        }

        private static string BuildInitialPrompt(List<DictionaryRule> rules)
        {
            var terms = rules.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Replace))
                             .Select(r => r.Replace.Trim())
                             .Distinct(StringComparer.Ordinal)
                             .ToList();

            var sb = new StringBuilder();
            foreach (var term in terms)
            {
                var addition = sb.Length == 0 ? term : ", " + term;
                if (sb.Length + addition.Length > MaxPromptChars)
                {
                    FileLog.Write($"[CustomDictionary] prompt {MaxPromptChars} karakter sınırına ulaştı, kalan terimler atlandı.");
                    break;
                }
                sb.Append(addition);
            }

            return sb.ToString();
        }

        /// <summary>
        /// İlk çalıştırmada yazılan varsayılan sözlük. Whisper'ın Türkçe dikte sırasında
        /// sık ürettiği fonetik yazımlar hedeflenir.
        /// </summary>
        private static List<DictionaryRule> CreateDefaultRules() => new()
        {
            new() { Match = "piton",          Replace = "Python" },
            new() { Match = "payton",         Replace = "Python" },
            new() { Match = "python",         Replace = "Python" },
            new() { Match = "gıthab",         Replace = "GitHub" },
            new() { Match = "gıthap",         Replace = "GitHub" },
            new() { Match = "githab",         Replace = "GitHub" },
            new() { Match = "github",         Replace = "GitHub" },
            new() { Match = "sişarp",         Replace = "C#" },
            new() { Match = "si şarp",        Replace = "C#" },
            new() { Match = "dokır",          Replace = "Docker" },
            new() { Match = "doker",          Replace = "Docker" },
            new() { Match = "docker",         Replace = "Docker" },
            new() { Match = "kubernetis",     Replace = "Kubernetes" },
            new() { Match = "kubernetes",     Replace = "Kubernetes" },
            new() { Match = "api",            Replace = "API" },
            new() { Match = "elelem",         Replace = "LLM" },
            new() { Match = "el el em",       Replace = "LLM" },
            new() { Match = "llm",            Replace = "LLM" },
            new() { Match = "vizual studyo",  Replace = "Visual Studio" },
            new() { Match = "vijual studyo",  Replace = "Visual Studio" },
            new() { Match = "visual studio",  Replace = "Visual Studio" },
            new() { Match = "vs kod",         Replace = "VS Code" },
            new() { Match = "cavaskript",     Replace = "JavaScript" },
            new() { Match = "javascript",     Replace = "JavaScript" },
            new() { Match = "tayipskript",    Replace = "TypeScript" },
            new() { Match = "typescript",     Replace = "TypeScript" },
            new() { Match = "ceyson",         Replace = "JSON" },
            new() { Match = "json",           Replace = "JSON" },
            new() { Match = "linuks",         Replace = "Linux" },
            new() { Match = "linux",          Replace = "Linux" },
            new() { Match = "sikıl",          Replace = "SQL" },
            new() { Match = "postgresql",     Replace = "PostgreSQL" },
            new() { Match = "postgres",       Replace = "PostgreSQL" },
            new() { Match = "reakt",          Replace = "React" },
            new() { Match = "react",          Replace = "React" },
            new() { Match = "nod ceyes",      Replace = "Node.js" },
            new() { Match = "eyçtiemel",      Replace = "HTML" },
            new() { Match = "html",           Replace = "HTML" },
            new() { Match = "sisies",         Replace = "CSS" },
            new() { Match = "css",            Replace = "CSS" },
        };
    }
}
