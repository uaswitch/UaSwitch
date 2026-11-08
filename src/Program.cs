namespace UaSwitch;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Log.Write($"--- Старт UaSwitch {Application.ProductVersion}, Windows {Environment.OSVersion.Version}, " +
                  $"CLR {Environment.Version}, x64={Environment.Is64BitProcess}, шлях: {Application.ExecutablePath}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal("необроблений виняток", (Exception)e.ExceptionObject);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write($"ПОМИЛКА (UI): {e.Exception}");

        try
        {
            using var mutex = new Mutex(true, @"Local\UaSwitch.SingleInstance", out bool first);
            if (!first)
            {
                Log.Write("Друга копія — вихід");
                MessageBox.Show("UaSwitch уже запущено — шукайте іконку UA/EN в області сповіщень (стрілка ^ біля годинника).", "UaSwitch");
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Контекст синхронізації потрібен до створення TrayApp, щоб хуки передавали дії в UI-потік
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var app = new TrayApp();
            Log.Write("Запущено успішно");
            Application.Run(app);
            Log.Write("Вихід");
        }
        catch (Exception ex)
        {
            Log.Fatal("запуск", ex);
        }
    }
}
