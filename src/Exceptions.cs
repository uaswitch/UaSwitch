namespace UaSwitch;

/// <summary>
/// Слова, які не треба виправляти автоматично (%APPDATA%\UaSwitch\exceptions.txt).
/// Поповнюється, коли користувач скасовує автовиправлення; можна редагувати вручну.
/// </summary>
internal sealed class Exceptions
{
    public static string FilePath => Path.Combine(Log.Dir, "exceptions.txt");
    private readonly HashSet<string> _set = new(StringComparer.Ordinal);
    private DateTime _loadedAt;

    public void Load()
    {
        lock (_set)
        {
            _set.Clear();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        var w = line.Trim();
                        if (w.Length > 0 && !w.StartsWith("#")) _set.Add(w.ToLowerInvariant());
                    }
                    _loadedAt = File.GetLastWriteTimeUtc(FilePath);
                }
            }
            catch { }
        }
    }

    public bool Contains(string word)
    {
        ReloadIfChanged();
        lock (_set) return _set.Contains(word.ToLowerInvariant());
    }

    /// <summary>Слово заблоковане: точний виняток або виняток-префікс (4+ літери), записаний при скасуванні за першими буквами.</summary>
    public bool Blocks(string word)
    {
        ReloadIfChanged();
        string w = word.ToLowerInvariant();
        lock (_set) return _set.Contains(w) || _set.Any(e => e.Length >= 4 && w.StartsWith(e, StringComparison.Ordinal));
    }

    public void Add(string word)
    {
        word = word.ToLowerInvariant();
        lock (_set)
        {
            if (!_set.Add(word)) return;
            try
            {
                Directory.CreateDirectory(Log.Dir);
                if (!File.Exists(FilePath))
                    File.WriteAllText(FilePath, "# Слова, які UaSwitch не виправляє автоматично (по одному в рядку)" + Environment.NewLine);
                File.AppendAllText(FilePath, word + Environment.NewLine);
                _loadedAt = File.GetLastWriteTimeUtc(FilePath);
            }
            catch { }
        }
    }

    public List<string> GetAll()
    {
        ReloadIfChanged();
        lock (_set) return _set.OrderBy(x => x).ToList();
    }

    public void SetAll(IEnumerable<string> words)
    {
        lock (_set)
        {
            _set.Clear();
            foreach (var w in words) if (w.Trim().Length > 0) _set.Add(w.Trim().ToLowerInvariant());
            try
            {
                Directory.CreateDirectory(Log.Dir);
                var lines = new List<string> { "# Слова, які UaSwitch не виправляє автоматично (по одному в рядку)" };
                lines.AddRange(_set.OrderBy(x => x));
                File.WriteAllLines(FilePath, lines);
                _loadedAt = File.GetLastWriteTimeUtc(FilePath);
            }
            catch { }
        }
    }

    /// <summary>Підхоплює ручні зміни файлу без перезапуску програми.</summary>
    private void ReloadIfChanged()
    {
        try
        {
            if (File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != _loadedAt) Load();
        }
        catch { }
    }

    public static void OpenInEditor()
    {
        try
        {
            Directory.CreateDirectory(Log.Dir);
            if (!File.Exists(FilePath))
                File.WriteAllText(FilePath, "# Слова, які UaSwitch не виправляє автоматично (по одному в рядку)" + Environment.NewLine);
            System.Diagnostics.Process.Start("notepad.exe", $"\"{FilePath}\"");
        }
        catch { }
    }
}
