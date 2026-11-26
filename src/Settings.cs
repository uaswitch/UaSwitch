using Microsoft.Win32;

namespace UaSwitch;

/// <summary>Налаштування у файлі %APPDATA%\UaSwitch\settings.ini (формат ключ=значення).</summary>
internal sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public bool PauseHotkeys { get; set; } = true;
    public bool DoubleShift { get; set; } = true;
    public bool DoubleCtrl { get; set; } = true;
    public bool AutoMode { get; set; } = true;
    public bool FixCaps { get; set; } = true;
    public bool EarlySwitch { get; set; } = true;
    public bool AutoComma { get; set; } = true;
    public bool SpaceAfterPunct { get; set; } = true;
    public bool CapsSwitch { get; set; } = false;
    public bool HotkeyTranslit { get; set; } = true;
    public bool HotkeyAmount { get; set; } = true;
    public bool AutoReplace { get; set; } = true;
    public bool FixTwoCaps { get; set; } = true;
    public bool SentenceCaps { get; set; } = true;
    public bool FixApostrophe { get; set; } = true;
    public bool FixG { get; set; } = true;
    /// <summary>Українська, набрана в російській розкладці («привыт» → «привіт»), і англійська в ній («руддщ» → «hello»).</summary>
    public bool FixOtherCyrillic { get; set; } = true;
    /// <summary>Бета: «привыт» → «привіт» (українська в російській розкладці). За замовчуванням вимкнено.</summary>
    public bool FixUaInRu { get; set; } = false;
    /// <summary>Вигляд іконки: text, flags, mono, dot.</summary>
    public string IconStyle { get; set; } = "text";
    /// <summary>Привітальне сповіщення вже показано (показуємо лише при першому запуску).</summary>
    public bool WelcomeShown { get; set; }
    /// <summary>Не виправляти нічого в полях паролів і в «словах» з цифрами чи символами.</summary>
    public bool SkipPasswords { get; set; } = true;
    /// <summary>Раз на добу перевіряти на GitHub, чи вийшла нова версія. За замовчуванням вимкнено.</summary>
    public bool CheckUpdates { get; set; }
    public string SkippedVersion { get; set; } = "";
    public string LastUpdateCheck { get; set; } = "";
    /// <summary>Мітка мови біля курсора: off, switch (на мить після перемикання), text (над полями вводу), always.</summary>
    public string IndicatorMode { get; set; } = "switch";
    public bool IndicatorBig { get; set; }
    public bool IndicatorNumLock { get; set; }
    public bool IndicatorHideFullScreen { get; set; } = true;
    /// <summary>Перше слово після ручного перемикання мови не перекладати.</summary>
    public bool RespectManual { get; set; } = true;
    /// <summary>Повна пауза, коли гра чи інша програма працює на весь екран.</summary>
    public bool PauseInGames { get; set; } = true;
    /// <summary>Не показувати вікно «Залипання клавіш» після кількох подвійних Shift.</summary>
    public bool NoStickyPrompt { get; set; } = true;
    public bool HotkeyCase { get; set; } = true;
    /// <summary>Мова для програм: «Telegram=UA;Code=EN».</summary>
    public string AppLanguages { get; set; } = "";
    /// <summary>Готові скорочення .дата і .час уже додано (один раз, щоб не повертати видалені).</summary>
    public bool ReplaceDefaultsAdded { get; set; }

    public Dictionary<string, string> AppLanguageMap
    {
        get
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in AppLanguages.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) d[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim().ToUpperInvariant();
            }
            return d;
        }
        set => AppLanguages = string.Join(";", value.Select(kv => kv.Key + "=" + kv.Value));
    }

    public bool Sound { get; set; } = true;
    public bool Flash { get; set; } = true;
    /// <summary>Програми (імена процесів без .exe), де автовиправлення вимкнено.</summary>
    public string ExcludedApps { get; set; } = "mstsc,vmconnect,KeePass,KeePassXC,1Password,Bitwarden,cmd,powershell,pwsh,WindowsTerminal";
    public bool IsExcluded(string app) => ExcludedAppList.Contains(app, StringComparer.OrdinalIgnoreCase);

    public void SetExcluded(string app, bool excluded)
    {
        var list = ExcludedAppList.Where(a => !a.Equals(app, StringComparison.OrdinalIgnoreCase)).ToList();
        if (excluded) list.Add(app);
        ExcludedApps = string.Join(",", list);
    }

    public string[] ExcludedAppList => ExcludedApps.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();

    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UaSwitch");
    public static string FilePath => Path.Combine(Dir, "settings.ini");

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (!File.Exists(FilePath)) return s;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.TrimStart().StartsWith("#")) continue;
                string key = line.Substring(0, eq).Trim();
                string raw = line.Substring(eq + 1).Trim();
                bool val = raw.Equals("true", StringComparison.OrdinalIgnoreCase);
                switch (key)
                {
                    case nameof(Enabled): s.Enabled = val; break;
                    case nameof(PauseHotkeys): s.PauseHotkeys = val; break;
                    case nameof(DoubleShift): s.DoubleShift = val; break;
                    case nameof(DoubleCtrl): s.DoubleCtrl = val; break;
                    case nameof(AutoMode): s.AutoMode = val; break;
                    case nameof(FixCaps): s.FixCaps = val; break;
                    case nameof(EarlySwitch): s.EarlySwitch = val; break;
                    case nameof(AutoComma): s.AutoComma = val; break;
                    case nameof(SpaceAfterPunct): s.SpaceAfterPunct = val; break;
                    case nameof(CapsSwitch): s.CapsSwitch = val; break;
                    case nameof(HotkeyTranslit): s.HotkeyTranslit = val; break;
                    case nameof(HotkeyAmount): s.HotkeyAmount = val; break;
                    case nameof(AutoReplace): s.AutoReplace = val; break;
                    case nameof(FixTwoCaps): s.FixTwoCaps = val; break;
                    case nameof(SentenceCaps): s.SentenceCaps = val; break;
                    case nameof(FixApostrophe): s.FixApostrophe = val; break;
                    case nameof(FixG): s.FixG = val; break;
                    case nameof(FixOtherCyrillic): s.FixOtherCyrillic = val; break;
                    case nameof(FixUaInRu): s.FixUaInRu = val; break;
                    case nameof(IconStyle): s.IconStyle = raw; break;
                    case nameof(WelcomeShown): s.WelcomeShown = val; break;
                    case nameof(SkipPasswords): s.SkipPasswords = val; break;
                    case nameof(CheckUpdates): s.CheckUpdates = val; break;
                    case nameof(SkippedVersion): s.SkippedVersion = raw; break;
                    case nameof(LastUpdateCheck): s.LastUpdateCheck = raw; break;
                    case nameof(Sound): s.Sound = val; break;
                    case nameof(IndicatorMode): s.IndicatorMode = raw; break;
                    case nameof(IndicatorBig): s.IndicatorBig = val; break;
                    case nameof(IndicatorNumLock): s.IndicatorNumLock = val; break;
                    case nameof(IndicatorHideFullScreen): s.IndicatorHideFullScreen = val; break;
                    case nameof(RespectManual): s.RespectManual = val; break;
                    case nameof(PauseInGames): s.PauseInGames = val; break;
                    case nameof(NoStickyPrompt): s.NoStickyPrompt = val; break;
                    case nameof(HotkeyCase): s.HotkeyCase = val; break;
                    case nameof(AppLanguages): s.AppLanguages = raw; break;
                    case nameof(ReplaceDefaultsAdded): s.ReplaceDefaultsAdded = val; break;
                    case nameof(Flash): s.Flash = val; break;
                    case nameof(ExcludedApps): s.ExcludedApps = raw; break;
                }
            }
        }
        catch { }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(FilePath, new[]
            {
                $"{nameof(Enabled)}={Enabled.ToString().ToLower()}",
                $"{nameof(PauseHotkeys)}={PauseHotkeys.ToString().ToLower()}",
                $"{nameof(DoubleShift)}={DoubleShift.ToString().ToLower()}",
                $"{nameof(DoubleCtrl)}={DoubleCtrl.ToString().ToLower()}",
                $"{nameof(AutoMode)}={AutoMode.ToString().ToLower()}",
                $"{nameof(FixCaps)}={FixCaps.ToString().ToLower()}",
                $"{nameof(EarlySwitch)}={EarlySwitch.ToString().ToLower()}",
                $"{nameof(AutoComma)}={AutoComma.ToString().ToLower()}",
                $"{nameof(SpaceAfterPunct)}={SpaceAfterPunct.ToString().ToLower()}",
                $"{nameof(CapsSwitch)}={CapsSwitch.ToString().ToLower()}",
                $"{nameof(HotkeyTranslit)}={HotkeyTranslit.ToString().ToLower()}",
                $"{nameof(HotkeyAmount)}={HotkeyAmount.ToString().ToLower()}",
                $"{nameof(AutoReplace)}={AutoReplace.ToString().ToLower()}",
                $"{nameof(FixTwoCaps)}={FixTwoCaps.ToString().ToLower()}",
                $"{nameof(SentenceCaps)}={SentenceCaps.ToString().ToLower()}",
                $"{nameof(FixApostrophe)}={FixApostrophe.ToString().ToLower()}",
                $"{nameof(FixG)}={FixG.ToString().ToLower()}",
                $"{nameof(FixOtherCyrillic)}={FixOtherCyrillic.ToString().ToLower()}",
                $"{nameof(FixUaInRu)}={FixUaInRu.ToString().ToLower()}",
                $"{nameof(IconStyle)}={IconStyle}",
                $"{nameof(WelcomeShown)}={WelcomeShown.ToString().ToLower()}",
                $"{nameof(SkipPasswords)}={SkipPasswords.ToString().ToLower()}",
                $"{nameof(CheckUpdates)}={CheckUpdates.ToString().ToLower()}",
                $"{nameof(SkippedVersion)}={SkippedVersion}",
                $"{nameof(LastUpdateCheck)}={LastUpdateCheck}",
                $"{nameof(Sound)}={Sound.ToString().ToLower()}",
                $"{nameof(IndicatorMode)}={IndicatorMode}",
                $"{nameof(IndicatorBig)}={IndicatorBig.ToString().ToLower()}",
                $"{nameof(IndicatorNumLock)}={IndicatorNumLock.ToString().ToLower()}",
                $"{nameof(IndicatorHideFullScreen)}={IndicatorHideFullScreen.ToString().ToLower()}",
                $"{nameof(RespectManual)}={RespectManual.ToString().ToLower()}",
                $"{nameof(PauseInGames)}={PauseInGames.ToString().ToLower()}",
                $"{nameof(NoStickyPrompt)}={NoStickyPrompt.ToString().ToLower()}",
                $"{nameof(HotkeyCase)}={HotkeyCase.ToString().ToLower()}",
                $"{nameof(ReplaceDefaultsAdded)}={ReplaceDefaultsAdded.ToString().ToLower()}",
                "# Мова для програм (процес=UA чи EN, через крапку з комою):",
                $"{nameof(AppLanguages)}={AppLanguages}",
                $"{nameof(Flash)}={Flash.ToString().ToLower()}",
                "# Програми без автовиправлення (імена процесів через кому):",
                $"{nameof(ExcludedApps)}={ExcludedApps}",
            });
        }
        catch { }
    }

    // ---------- Експорт / імпорт ----------

    private static readonly string[] DataFiles = { "settings.ini", "exceptions.txt", "autoreplace.txt" };
    private const string Marker = "@@@ ";

    /// <summary>Налаштування, слова-винятки й автозаміна — в один текстовий файл.</summary>
    public static void Export(string path)
    {
        var lines = new List<string> { "# UaSwitch — експорт налаштувань " + DateTime.Now.ToString("dd.MM.yyyy HH:mm") };
        foreach (var f in DataFiles)
        {
            string full = Path.Combine(Dir, f);
            lines.Add(Marker + f);
            if (File.Exists(full)) lines.AddRange(File.ReadAllLines(full));
        }
        File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
    }

    /// <summary>Записує файли з експорту. Повертає кількість відновлених файлів.</summary>
    public static int Import(string path)
    {
        var parts = new Dictionary<string, List<string>>();
        List<string>? cur = null;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith(Marker))
            {
                string name = line.Substring(Marker.Length).Trim();
                cur = DataFiles.Contains(name) ? (parts[name] = new List<string>()) : null;
            }
            else cur?.Add(line);
        }
        if (parts.Count == 0) throw new InvalidDataException("Це не файл експорту UaSwitch.");
        Directory.CreateDirectory(Dir);
        foreach (var kv in parts) File.WriteAllLines(Path.Combine(Dir, kv.Key), kv.Value);
        return parts.Count;
    }

    // ---------- Автозапуск ----------
    // Запис у реєстрі HKCU\...\Run — так роблять Viber, Chrome, Google Drive. Ярлик у папці «Автозавантаження»
    // (версії 0.6–0.7.1) Windows могла не запускати для файлів, завантажених з інтернету, — його переносимо в реєстр.
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "UaSwitch";
    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "UaSwitch.lnk");

    public static bool Autostart
    {
        get => RunKeyExists() || File.Exists(ShortcutPath);
        set
        {
            if (value) SetRunKey(Application.ExecutablePath);
            else RemoveRunKey();
            try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); } catch { }
        }
    }

    /// <summary>Переносить автозапуск зі старого ярлика в реєстр (один раз, тихо).</summary>
    public static void MigrateAutostart()
    {
        try { if (File.Exists(ShortcutPath)) { Autostart = true; Log.Write("Автозапуск перенесено з ярлика в реєстр"); } }
        catch (Exception e) { Log.Write("Перенесення автозапуску: " + e.Message); }
    }

    /// <summary>Програму запущено з тимчасової папки (прямо з архіву) — після закриття архіву файлу вже не буде.</summary>
    public static bool RunningFromTemp =>
        Application.ExecutablePath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Запис автозапуску пам'ятає шлях до exe. Якщо програму перенесли або розпакували нову версію в іншу папку,
    /// він веде в нікуди, а галочка все одно «увімкнена». Тому при кожному запуску спрямовуємо його на поточний файл.
    /// </summary>
    public static void RefreshAutostart()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            if (k?.GetValue(RunName) is not string cmd) return;
            string target = cmd.Trim().Trim('"');
            string self = Application.ExecutablePath;
            Log.Write($"Автозапуск: реєстр веде на «{target}»");
            if (string.Equals(target, self, StringComparison.OrdinalIgnoreCase)) return;
            if (RunningFromTemp && File.Exists(target)) return; // робоча копія є — не псуємо запис тимчасовою
            SetRunKey(self);
            Log.Write($"Автозапуск: оновлено на «{self}»");
        }
        catch (Exception e) { Log.Write("Оновлення автозапуску: " + e.Message); }
    }

    /// <summary>
    /// Стоїть галочка «Запускати від імені адміністратора» (Властивості → Сумісність).
    /// Такі програми Windows мовчки не запускає під час входу в систему.
    /// </summary>
    public static bool RunAsAdminFlag()
    {
        const string layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var k = hive.OpenSubKey(layers);
                if (k?.GetValue(Application.ExecutablePath) is string v && v.IndexOf("RUNASADMIN", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch { }
        }
        return false;
    }

    private static void SetRunKey(string exe)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        k.SetValue(RunName, "\"" + exe + "\"");
    }

    private static bool RunKeyExists()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) is string;
    }

    private static void RemoveRunKey()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        k?.DeleteValue(RunName, false);
    }
}
