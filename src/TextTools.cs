using System.Text;
using System.Text.RegularExpressions;

namespace UaSwitch;

/// <summary>Перетворення виділеного тексту: транслітерація (КМУ №55) і сума прописом.</summary>
internal static class TextTools
{
    // ---------- Транслітерація за постановою КМУ №55 від 27.01.2010 ----------

    private static readonly Dictionary<char, string> Tr = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "h", ['ґ'] = "g", ['д'] = "d", ['е'] = "e", ['є'] = "ie",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "y", ['і'] = "i", ['ї'] = "i", ['й'] = "i", ['к'] = "k", ['л'] = "l",
        ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ь'] = "", ['ю'] = "iu", ['я'] = "ia",
    };
    // На початку слова: Є→Ye, Ї→Yi, Й→Y, Ю→Yu, Я→Ya
    private static readonly Dictionary<char, string> TrStart = new()
    {
        ['є'] = "ye", ['ї'] = "yi", ['й'] = "y", ['ю'] = "yu", ['я'] = "ya",
    };

    public static bool HasCyrillic(string s) => s.Any(c => c is >= 'А' and <= 'ї' or 'ґ' or 'Ґ' or 'є' or 'Є' or 'і' or 'І' or 'ї' or 'Ї');

    /// <summary>«Шевченко Олександр» → «Shevchenko Oleksandr», «Згурський» → «Zghurskyi».</summary>
    public static string Transliterate(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        // Межі слів: апостроф усередині слова — частина слова (і просто пропускається)
        foreach (Match m in Regex.Matches(text, @"[\p{L}'’ʼ`]+|[^\p{L}'’ʼ`]+"))
        {
            string part = m.Value;
            if (!char.IsLetter(part[0]) && !"'’ʼ`".Contains(part[0])) { sb.Append(part); continue; }
            sb.Append(TransliterateWord(part));
        }
        return sb.ToString();
    }

    private static string TransliterateWord(string word)
    {
        var letters = word.Where(char.IsLetter).ToList();
        bool allUpper = letters.Count > 1 && letters.All(char.IsUpper);
        var sb = new StringBuilder();
        bool start = true;
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            if ("'’ʼ`".IndexOf(c) >= 0) continue; // апостроф не передається
            char lc = char.ToLowerInvariant(c);
            string? t;
            if (start && TrStart.TryGetValue(lc, out var st)) t = st;
            else if (lc == 'г' && i > 0 && char.ToLowerInvariant(word[i - 1]) == 'з') t = "gh"; // «зг» → «zgh»
            else if (!Tr.TryGetValue(lc, out t)) t = c.ToString();
            start = false;
            if (t.Length == 0) continue;
            if (allUpper) t = t.ToUpperInvariant();
            else if (char.IsUpper(c)) t = char.ToUpperInvariant(t[0]) + t.Substring(1);
            sb.Append(t);
        }
        return sb.ToString();
    }

    // ---------- Сума прописом (гривні й копійки) ----------

    private static readonly string[] UnitsM = { "", "один", "два", "три", "чотири", "п'ять", "шість", "сім", "вісім", "дев'ять" };
    private static readonly string[] UnitsF = { "", "одна", "дві", "три", "чотири", "п'ять", "шість", "сім", "вісім", "дев'ять" };
    private static readonly string[] Teens = { "десять", "одинадцять", "дванадцять", "тринадцять", "чотирнадцять", "п'ятнадцять",
                                                "шістнадцять", "сімнадцять", "вісімнадцять", "дев'ятнадцять" };
    private static readonly string[] Tens = { "", "", "двадцять", "тридцять", "сорок", "п'ятдесят", "шістдесят", "сімдесят", "вісімдесят", "дев'яносто" };
    private static readonly string[] Hundreds = { "", "сто", "двісті", "триста", "чотириста", "п'ятсот", "шістсот", "сімсот", "вісімсот", "дев'ятсот" };

    /// <summary>1 — перша форма, 2–4 — друга, 0 і 5–20 — третя: «гривня / гривні / гривень».</summary>
    private static string Form(long n, string one, string few, string many)
    {
        long d100 = n % 100, d10 = n % 10;
        if (d100 is >= 11 and <= 14) return many;
        return d10 == 1 ? one : d10 is >= 2 and <= 4 ? few : many;
    }

    private static string Triad(int n, bool feminine)
    {
        var parts = new List<string>();
        if (n / 100 > 0) parts.Add(Hundreds[n / 100]);
        int r = n % 100;
        if (r >= 10 && r < 20) parts.Add(Teens[r - 10]);
        else
        {
            if (r / 10 > 0) parts.Add(Tens[r / 10]);
            if (r % 10 > 0) parts.Add((feminine ? UnitsF : UnitsM)[r % 10]);
        }
        return string.Join(" ", parts);
    }

    /// <summary>Ціле число словами; feminine — для «гривня» (одна, дві).</summary>
    public static string NumberToWords(long n, bool feminine)
    {
        if (n == 0) return "нуль";
        var parts = new List<string>();
        var scales = new[]
        {
            (1_000_000_000_000L, "трильйон", "трильйони", "трильйонів", false),
            (1_000_000_000L, "мільярд", "мільярди", "мільярдів", false),
            (1_000_000L, "мільйон", "мільйони", "мільйонів", false),
            (1_000L, "тисяча", "тисячі", "тисяч", true),
        };
        foreach (var (size, one, few, many, fem) in scales)
        {
            int t = (int)(n / size % 1000);
            if (t == 0) continue;
            parts.Add(Triad(t, fem));
            parts.Add(Form(t, one, few, many));
        }
        int rest = (int)(n % 1000);
        if (rest > 0) parts.Add(Triad(rest, feminine));
        return string.Join(" ", parts);
    }

    /// <summary>
    /// «1250,50», «1 250.5 грн», «₴1250» → «Одна тисяча двісті п'ятдесят гривень 50 копійок». Не число — null.
    /// </summary>
    public static string? AmountInWords(string text)
    {
        string s = text.Trim().Replace(' ', ' ').Replace(' ', ' ');
        s = Regex.Replace(s, @"(?i)\s*(грн\.?|гривень|гривні|гривня|uah|₴)\s*", " ").Trim();
        var m = Regex.Match(s, @"^(\d{1,3}(?:[ '’]\d{3})+|\d+)(?:[.,](\d{1,2}))?$");
        if (!m.Success) return null;
        string intPart = Regex.Replace(m.Groups[1].Value, @"[ '’]", "");
        if (intPart.Length > 15 || !long.TryParse(intPart, out long hrn)) return null;
        int kop = 0;
        if (m.Groups[2].Success) kop = int.Parse(m.Groups[2].Value.PadRight(2, '0'));

        string words = NumberToWords(hrn, feminine: true) + " " + Form(hrn, "гривня", "гривні", "гривень")
                       + " " + kop.ToString("00") + " " + Form(kop, "копійка", "копійки", "копійок");
        return char.ToUpperInvariant(words[0]) + words.Substring(1);
    }
}
