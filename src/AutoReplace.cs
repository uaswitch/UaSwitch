namespace UaSwitch;

/// <summary>
/// Автозаміна: скорочення → текст (%APPDATA%\UaSwitch\autoreplace.txt, рядок «скорочення[TAB]текст»,
/// перенесення рядка в тексті записується як \n). Скорочення шукається без урахування регістру.
/// </summary>
internal sealed class AutoReplace
{
    public static string FilePath => Path.Combine(Log.Dir, "autoreplace.txt");
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public void Load()
    {
        lock (_sync)
        {
            _map.Clear();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    if (line.StartsWith("#")) continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    string key = line.Substring(0, tab).Trim();
                    string text = Unescape(line.Substring(tab + 1));
                    if (key.Length > 0 && text.Length > 0) _map[key] = text;
                }
            }
            catch { }
        }
    }

    public List<KeyValuePair<string, string>> GetAll()
    {
        lock (_sync) return _map.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void SetAll(IEnumerable<KeyValuePair<string, string>> items)
    {
        lock (_sync)
        {
            _map.Clear();
            foreach (var kv in items)
                if (kv.Key.Trim().Length > 0 && kv.Value.Length > 0) _map[kv.Key.Trim()] = Normalize(kv.Value);
            try
            {
                Directory.CreateDirectory(Log.Dir);
                var lines = new List<string> { "# Автозаміна UaSwitch: скорочення<TAB>текст (\\n — новий рядок)" };
                lines.AddRange(_map.OrderBy(kv => kv.Key).Select(kv => kv.Key + "\t" + Escape(kv.Value)));
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }

    /// <summary>Чи починається якесь скорочення з цього тексту (щоб не перемикати розкладку посеред скорочення).</summary>
    public bool IsPrefix(string text)
    {
        lock (_sync) return _map.Count > 0 && _map.Keys.Any(k => k.StartsWith(text, StringComparison.OrdinalIgnoreCase));
    }

    public bool Contains(string abbr)
    {
        lock (_sync) return _map.Count > 0 && _map.ContainsKey(abbr);
    }

    /// <summary>
    /// Шукає заміну для набраного слова. Розділові знаки в кінці («спс,») зберігаються.
    /// Якщо скорочення набрано з великої, а текст починається з малої — текст теж з великої.
    /// </summary>
    public bool TryMatch(string typed, out string replacement, out string suffix)
    {
        replacement = suffix = "";
        string core = typed.TrimEnd(TrailPunct);
        if (core.Length == 0) return false;
        string? text;
        lock (_sync) if (_map.Count == 0 || !_map.TryGetValue(core, out text)) return false;
        suffix = typed.Substring(core.Length);
        replacement = char.IsUpper(core[0]) && char.IsLower(text![0])
            ? char.ToUpper(text[0]) + text.Substring(1)
            : text!;
        return true;
    }

    public static readonly char[] TrailPunct = { '.', ',', '!', '?', ';', ':' };

    /// <summary>Скільки разів натиснути Backspace, щоб стерти текст (перенесення рядка — одне натискання).</summary>
    public static int EraseCount(string s) => s.Count(c => c != '\r');

    public static string Normalize(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');
    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t");

    private static string Unescape(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
