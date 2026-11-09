namespace UaSwitch;

/// <summary>
/// Консервативні правила коми в українському тексті. Кома ставиться лише тоді, коли перед підрядним
/// стоїть звичайне слово, а не сполучник, частка чи прийменник. Краще пропустити кому, ніж поставити зайву.
/// </summary>
internal static class CommaRules
{
    /// <summary>Сполучники, перед якими кома ставиться завжди: «знаю, що», «прийшов, бо».</summary>
    private static readonly HashSet<string> Conjunctions = new()
    {
        "що", "щоб", "але", "проте", "однак", "зате", "тобто", "а", "бо", "якщо", "хоча", "оскільки",
        "ніби", "наче", "неначе", "немов", "немовби", "нібито", "доки",
    };

    /// <summary>«Який», «котрий», «чий» в усіх формах: «місто, яке», а з прийменником — «місто, в якому».</summary>
    private static readonly HashSet<string> Relatives = new()
    {
        "який", "яка", "яке", "які", "якого", "якої", "якому", "якій", "яким", "якими", "яких", "яку", "якім",
        "котрий", "котра", "котре", "котрі", "котрого", "котрої", "котрому", "котрій", "котрим", "котрими", "котрих", "котру",
        "чий", "чия", "чиє", "чиї", "чийого", "чиєї", "чийому", "чиїй", "чиїм", "чиїх", "чию",
    };

    /// <summary>Питальні слова починають підрядне, лише коли за ними займенник: «знаю, де він», але «такий як я» — без коми.</summary>
    private static readonly HashSet<string> QuestionWords = new()
    {
        "як", "де", "куди", "звідки", "коли", "чому", "навіщо", "скільки",
    };

    private static readonly HashSet<string> Pronouns = new()
    {
        "я", "ти", "він", "вона", "воно", "ми", "ви", "вони", "його", "її", "їх", "мене", "тебе", "нас", "вас",
        "мені", "тобі", "нам", "вам", "їм", "йому", "їй",
    };

    private static readonly HashSet<string> Prepositions = new()
    {
        "в", "у", "на", "за", "про", "до", "з", "із", "зі", "від", "над", "під", "по", "без", "при", "через", "між",
        "перед", "після", "біля", "для", "серед", "крізь", "щодо", "завдяки",
    };

    /// <summary>Після цих слів кома не ставиться: «тому що», «так як», «поки що», «для того щоб».</summary>
    private static readonly HashSet<string> NoCommaAfter = new()
    {
        "і", "й", "та", "а", "але", "або", "чи", "ні", "не", "то", "бо", "ніж", "аніж", "тому", "того", "так", "тоді",
        "лише", "тільки", "хоч", "хоча", "якщо", "коли", "де", "куди", "проте", "однак", "щоб", "що", "аби", "ж", "же", "б", "би",
        "це", "все", "усе", "вже", "уже", "ще", "саме", "навіть", "аж", "от", "ось", "ото", "ну", "мов", "мовби",
        "наче", "неначе", "ніби", "нібито", "начебто", "мабуть", "адже", "тож", "отже", "також", "теж", "поки", "доки",
        "мало", "дещо", "хто", "дехто", "ніхто", "хтось", "кожен", "кожний", "чого", "такий", "така", "таке", "такі", "таким",
        "там", "тут", "більше", "менше", "більш", "менш", "ледве", "мовляв", "незважаючи", "попри", "тобто", "зате",
        "який", "яка", "яке", "які", "котрий", "котра", "котре", "котрі", "як",
    };

    /// <summary>
    /// recent — попередні слова (як на екрані, через один пробіл), word — щойно набране слово.
    /// Повертає, перед яким словом ставити кому: 0 — перед word, 1 — перед останнім з recent; -1 — не ставити.
    /// </summary>
    public static int Where(IReadOnlyList<string> recent, string word)
    {
        string w = Lower(word);
        if (w.Length == 0 || !IsUkrainian(word)) return -1;
        int n = recent.Count;

        // «знаю де він» → «знаю, де він»: займенник після питального слова
        if (Pronouns.Contains(w) && n >= 2 && QuestionWords.Contains(Lower(recent[n - 1])) && IsPlainWord(recent[n - 1])
            && CanPrecede(recent[n - 2]))
            return 1;

        if (!Conjunctions.Contains(w) && !Relatives.Contains(w)) return -1;
        if (n == 0) return -1;

        // «місто в якому» → «місто, в якому»
        if (Relatives.Contains(w) && Prepositions.Contains(Lower(recent[n - 1])) && IsPlainWord(recent[n - 1]))
            return n >= 2 && CanPrecede(recent[n - 2]) ? 1 : -1;

        return CanPrecede(recent[n - 1]) ? 0 : -1;
    }

    /// <summary>Чи можна поставити кому одразу після цього слова.</summary>
    private static bool CanPrecede(string prev)
    {
        if (prev.Length == 0 || !char.IsLetter(prev[prev.Length - 1])) return false; // уже є кома чи інший знак
        if (!IsUkrainian(prev) || prev.Any(char.IsDigit)) return false;
        return !NoCommaAfter.Contains(Lower(prev));
    }

    private static bool IsPlainWord(string s) => s.Length > 0 && s.All(c => char.IsLetter(c) || c == '\'' || c == '’');
    private static bool IsUkrainian(string s) => s.Any(c => c is >= 'Ѐ' and <= 'ӿ');
    private static string Lower(string s) => s.ToLowerInvariant();
}
