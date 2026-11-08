namespace UaSwitch;

/// <summary>Журнал запуску та помилок: %APPDATA%\UaSwitch\uaswitch.log</summary>
internal static class Log
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UaSwitch");
    public static string FilePath => Path.Combine(Dir, "uaswitch.log");
    private static readonly object Sync = new();

    public static void Write(string msg)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {msg}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void Fatal(string where, Exception ex)
    {
        Write($"ПОМИЛКА ({where}): {ex}");
        try
        {
            MessageBox.Show(
                $"UaSwitch не зміг запуститися.\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                $"Повний текст помилки записано у файл:\n{FilePath}\n\nНадішліть його для виправлення.",
                "UaSwitch — помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
