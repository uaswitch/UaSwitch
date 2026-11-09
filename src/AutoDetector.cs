using System.IO.Compression;
using System.Reflection;

namespace UaSwitch;

/// <summary>
/// Мовна модель для автоматичного виправлення: частотні словники + триграми букв.
/// Логіка й пороги відкалібровані скриптом model/build_model.py (там ті самі формули).
/// Словники: wordfreq (CC BY-SA 4.0).
/// </summary>
internal sealed class AutoDetector
{
    private const double Margin = 2.0, AbsBad = -4.6, Smooth = 0.02, OutOfAlphabet = -12.0;
    private const int Ratio = 30;

    private sealed class Lang
    {
        public readonly Dictionary<string, int> Rank = new();
        public readonly HashSet<char> Alpha;
        private readonly Dictionary<long, double> _tri = new();
        private readonly Dictionary<long, double> _bi = new();
        private readonly int _v;

        public Lang(IEnumerable<string> words, string alphabet)
        {
            Alpha = new HashSet<char>(alphabet);
            _v = Alpha.Count + 1;
            int i = 0;
            foreach (var w in words)
            {
                if (w.Length == 0 || Rank.ContainsKey(w)) continue;
                Rank[w] = i;
                double wt = 1.0 / Math.Sqrt(i + 10);
                string s = "^^" + w + "$";
                for (int j = 2; j < s.Length; j++)
                {
                    long b = Key2(s[j - 2], s[j - 1]);
                    long t = (b << 16) | s[j];
                    _tri[t] = (_tri.TryGetValue(t, out var x) ? x : 0) + wt;
                    _bi[b] = (_bi.TryGetValue(b, out var y) ? y : 0) + wt;
                }
                i++;
            }
        }

        // Відсортовані слова + накопичені ваги: вага префікса = сума ваг слів, що з нього починаються
        private string[] _sorted = Array.Empty<string>();
        private double[] _cum = Array.Empty<double>();

        public void BuildPrefixIndex()
        {
            var pairs = Rank.Select(kv => (w: kv.Key, wt: 1.0 / Math.Sqrt(kv.Value + 10))).ToList();
            pairs.Sort((a, b) => string.CompareOrdinal(a.w, b.w));
            _sorted = pairs.Select(p => p.w).ToArray();
            _cum = new double[pairs.Count + 1];
            for (int i = 0; i < pairs.Count; i++) _cum[i + 1] = _cum[i] + pairs[i].wt;
        }

        public double PrefixWeight(string prefix)
        {
            int lo = LowerBound(prefix), hi = LowerBound(prefix + '\uffff');
            return _cum[hi] - _cum[lo];
        }

        private int LowerBound(string key)
        {
            int lo = 0, hi = _sorted.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (string.CompareOrdinal(_sorted[mid], key) < 0) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private static long Key2(char a, char b) => ((long)a << 16) | b;

        public bool Known(string w, out int rank)
        {
            if (!Rank.TryGetValue(w, out rank)) return false;
            int n = w.Length;
            return rank < (n <= 2 ? 1500 : n == 3 ? 8000 : int.MaxValue);
        }

        public double Score(string w)
        {
            string s = "^^" + w + "$";
            double tot = 0;
            for (int j = 2; j < s.Length; j++)
            {
                char c = s[j];
                if (c != '$' && !Alpha.Contains(c)) { tot += OutOfAlphabet; continue; }
                long b = Key2(s[j - 2], s[j - 1]);
                long t = (b << 16) | c;
                double tc = _tri.TryGetValue(t, out var x) ? x : 0;
                double bc = _bi.TryGetValue(b, out var y) ? y : 0;
                tot += Math.Log((tc + Smooth) / (bc + Smooth * _v));
            }
            return tot / (s.Length - 2);
        }
    }

    private Lang? _ua, _en;
    public bool Ready => _ua != null && _en != null;

    public void Load()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ua = new Lang(ReadWords("UaSwitch.uk.txt.gz"), "абвгґдеєжзиіїйклмнопрстуфхцчшщьюя'");
        var en = new Lang(ReadWords("UaSwitch.en.txt.gz"), "abcdefghijklmnopqrstuvwxyz'");
        ua.BuildPrefixIndex();
        en.BuildPrefixIndex();
        _en = en;
        _ua = ua; // Ready стає true лише коли обидві мови готові
        Log.Write($"Словники: UA {ua.Rank.Count}, EN {en.Rank.Count}, {sw.ElapsedMilliseconds} мс");
    }

    private static IEnumerable<string> ReadWords(string resource)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                      ?? throw new FileNotFoundException("Немає вбудованого словника " + resource);
        using var gz = new GZipStream(s, CompressionMode.Decompress);
        using var r = new StreamReader(gz, System.Text.Encoding.UTF8);
        var list = new List<string>(90000);
        string? line;
        while ((line = r.ReadLine()) != null) list.Add(line.Trim());
        return list;
    }

    private const string Punct = ".,!?;:\")»";
    private const string CurPunctExtra = "/?&^$#*";

    public static string Normalize(string s) => s.Replace('’', '\'').Replace('ʼ', '\'');

    /// <summary>
    /// Чи набрано слово не в тій розкладці. cur — текст у поточній розкладці, alt — ті ж клавіші в іншій.
    /// curIsUa — мова поточної розкладки.
    /// </summary>
    public bool ShouldConvert(string curText, string altText, bool curIsUa, int ctxUa = 0, int ctxEn = 0)
    {
        if (!Ready || curText.Length != altText.Length) return false;
        curText = Normalize(curText);
        altText = Normalize(altText);
        if (curText.Any(char.IsDigit) || curText.Contains('@')) return false;

        // Хвостові розділові знаки — лише ті, що є знаками в обох прочитаннях
        int n = curText.Length;
        while (n > 0 && Punct.IndexOf(altText[n - 1]) >= 0
               && (Punct.IndexOf(curText[n - 1]) >= 0 || CurPunctExtra.IndexOf(curText[n - 1]) >= 0)) n--;
        string craw = curText.Substring(0, n), araw = altText.Substring(0, n);
        string c = craw.ToLowerInvariant(), a = araw.ToLowerInvariant();
        if (c.Length < 2 || a.Length < 2) return false;
        if (IsCamelCase(craw)) return false;

        var cur = curIsUa ? _ua! : _en!;
        var alt = curIsUa ? _en! : _ua!;
        var curShort = curIsUa ? ShortUa : ShortEn;
        var altShort = curIsUa ? ShortEn : ShortUa;

        // Контекст речення: скільки попередніх слів кожною мовою
        int nAlt = curIsUa ? ctxEn : ctxUa, nCur = curIsUa ? ctxUa : ctxEn;
        bool favorsAlt = nAlt >= CtxMin && nAlt > nCur;
        bool favorsCur = nCur >= CtxMin && nCur > nAlt;

        bool altKnown = alt.Known(a, out int ar);
        bool curKnown = cur.Rank.TryGetValue(c, out int cr);

        // Короткі слова (1–3 літери) — за списками поширених: «yt» → «не», «ye» → «ну», але «ok» лишається
        if (c.Length <= 3)
        {
            if (curShort.Contains(c)) return false;
            if (altShort.Contains(a) && (!curKnown || cr > ShortCurRank)) return true;
        }
        if (curKnown)
        {
            // Коротке рідкісне слово, коли в іншій мові воно дуже часте
            if (c.Length <= 3 && altKnown && (long)ar * Ratio < cr) return true;
            // Відоме слово міняємо лише всередині речення іншою мовою і лише якщо воно не надто часте («herb» → «руки»)
            return favorsAlt && altKnown && cr > CtxCurRank && c.Length >= 3;
        }
        if (altKnown) return true;
        if (c.Length < 4 || IsAllUpper(craw)) return false; // абревіатури (PFMEA, NCR) — лише за словником
        if (favorsCur) return false; // у реченні поточною мовою незнайоме слово — скоріше описка чи назва
        double sc = cur.Score(c), sa = alt.Score(a);
        return sc < AbsBad && sa - sc > Margin;
    }

    private const int CtxMin = 2, CtxCurRank = 1500, ShortCurRank = 3000;

    // Поширені слова з 1–3 літер (перші 500 слів частотних словників, без скорочень)
    private static readonly HashSet<string> ShortUa = new()
    {
        "в", "на", "не", "і", "у", "з", "що", "та", "до", "це", "за", "а", "я", "як", "про", "для", "від", "так", "але", "він",
        "все", "то", "є", "ви", "по", "ми", "й", "ще", "які", "під", "вже", "або", "із", "щоб", "ж", "тут", "чи", "час", "ти",
        "був", "нас", "ну", "її", "їх", "хто", "де", "там", "б", "всі", "ні", "те", "при", "без", "яка", "має", "цей", "зі", "бо",
        "вас", "вам", "них", "ось", "рік", "нам", "же", "між", "ці", "два", "би", "той", "ніж", "раз", "теж", "над", "тим", "три",
        "ця", "їм", "мій", "сам", "цю", "яку", "ті", "неї", "тих", "яке", "цих", "цим", "наш", "ним", "моя", "усі", "дві", "чим",
        "хоч", "мав", "мої", "дії", "маю", "усе", "їй", "аби", "всю", "син", "хай", "цій", "ато",
    };
    private static readonly HashSet<string> ShortEn = new()
    {
        "the", "to", "and", "of", "a", "in", "i", "is", "for", "you", "it", "on", "was", "be", "as", "are", "at", "he", "not",
        "by", "but", "my", "or", "we", "an", "all", "so", "his", "me", "if", "one", "can", "up", "out", "has", "do", "no", "who",
        "had", "her", "get", "she", "new", "how", "now", "its", "our", "him", "see", "two", "any", "us", "go", "way", "may",
        "did", "day", "too", "off", "why", "got", "say", "man", "use", "am", "old", "own", "end", "big", "put", "lot", "few",
        "let", "set", "god", "top", "yet", "bad", "men", "far", "job", "try", "yes", "run", "law", "war", "car", "per", "oh",
        "ago", "guy", "pay", "win", "air", "bit", "hit", "i'd", "due", "ask", "saw", "low", "age", "buy", "red", "act", "fun", "art",
    };

    /// <summary>Мова слів у реченні до поточного слова (за алфавітом), не більше 6 слів.</summary>
    public static (int ua, int en) Context(IReadOnlyList<string> recent)
    {
        int ua = 0, en = 0, taken = 0;
        for (int i = recent.Count - 1; i >= 0 && taken < 6; i--)
        {
            var w = recent[i].TrimEnd(',', ';', ':', ')', '»', '"');
            if (w.Length == 0) continue;
            char last = w[w.Length - 1];
            if (last is '.' or '!' or '?' or '…') break; // попереднє речення
            if (w.Any(ch => ch is >= 'а' and <= 'я' or 'і' or 'ї' or 'є' or 'ґ' or >= 'А' and <= 'Я' or 'І' or 'Ї' or 'Є' or 'Ґ')) ua++;
            else if (w.Any(ch => ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z')) en++;
            taken++;
        }
        return (ua, en);
    }

    private const int EarlyMinLen = 4;
    private const double EarlyAltWeight = 0.02;

    /// <summary>
    /// Перемикання за першими буквами: у поточній мові жодне слово так не починається,
    /// а в іншій — є помітна кількість. Калібровано в model/early.py.
    /// </summary>
    public bool ShouldSwitchEarly(string curPrefix, string altPrefix, bool curIsUa)
    {
        if (!Ready || curPrefix.Length < EarlyMinLen || curPrefix.Length != altPrefix.Length) return false;
        curPrefix = Normalize(curPrefix);
        altPrefix = Normalize(altPrefix);
        if (curPrefix.Any(char.IsDigit)) return false;
        if (IsAllUpper(curPrefix) || curPrefix.Skip(1).Any(char.IsUpper)) return false;
        var cur = curIsUa ? _ua! : _en!;
        var alt = curIsUa ? _en! : _ua!;
        string c = curPrefix.ToLowerInvariant(), a = altPrefix.ToLowerInvariant();
        if (!a.All(alt.Alpha.Contains)) return false;
        return cur.PrefixWeight(c) == 0 && alt.PrefixWeight(a) > EarlyAltWeight;
    }

    /// <summary>Чи є слово (без хвостових розділових знаків) у словнику мови.</summary>
    public bool IsKnownWord(string text, bool isUa)
    {
        if (!Ready) return false;
        string w = Normalize(text).TrimEnd(Punct.ToCharArray()).ToLowerInvariant();
        return w.Length > 0 && (isUa ? _ua! : _en!).Rank.ContainsKey(w);
    }

    /// <summary>Однобуквене слово, яке варто виправити разом із наступним («z ljvf» → «я вдома»).</summary>
    public static bool SingleLetterWord(string ch, bool isUa)
    {
        if (ch.Length != 1) return false;
        string l = ch.ToLowerInvariant();
        return isUa ? "візуаяйоє".Contains(l) : l == "a" || l == "i";
    }

    private static bool IsAllUpper(string s) => s.Any(char.IsLetter) && s.Where(char.IsLetter).All(char.IsUpper);

    private static bool IsCamelCase(string s)
    {
        if (IsAllUpper(s)) return false;
        for (int i = 1; i < s.Length; i++) if (char.IsUpper(s[i])) return true;
        return false;
    }
}
