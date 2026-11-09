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
    public bool FixTwoCaps { get; set; } = true;
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
                    case nameof(FixTwoCaps): s.FixTwoCaps = val; break;
                    case nameof(Sound): s.Sound = val; break;
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
                $"{nameof(FixTwoCaps)}={FixTwoCaps.ToString().ToLower()}",
                $"{nameof(Sound)}={Sound.ToString().ToLower()}",
                $"{nameof(Flash)}={Flash.ToString().ToLower()}",
                "# Програми без автовиправлення (імена процесів через кому):",
                $"{nameof(ExcludedApps)}={ExcludedApps}",
            });
        }
        catch { }
    }

    // ---------- Автозапуск ----------
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "UaSwitch";

    public static bool Autostart
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunName) is string;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue(RunName, $"\"{Application.ExecutablePath}\"");
            else k.DeleteValue(RunName, false);
        }
    }
}
