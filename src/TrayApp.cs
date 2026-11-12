using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace UaSwitch;

internal sealed class TrayApp : ApplicationContext
{
    private readonly Settings _settings = Settings.Load();
    private readonly Layouts _layouts = new();
    private readonly AutoDetector _detector = new();
    private readonly Exceptions _exceptions = new();
    private readonly AutoReplace _replace = new();
    private readonly KeyboardMonitor _monitor;
    private readonly Converter _converter;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _miEnabled, _miAutoMode, _miExcludeApp;
    private readonly int _ownPid = Process.GetCurrentProcess().Id;
    private string _iconState = "";
    private Icon? _icon;
    private int _flashUntil;
    private string? _lastApp, _lastAppTitle;
    private uint _lastPid;
    private SettingsForm? _settingsForm;

    private static readonly HashSet<string> ShellProcs = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp",
        "SystemSettings", "LockApp", "TextInputHost", "ApplicationFrameHost"
    };

    public TrayApp()
    {
        _layouts.Refresh();
        Log.Write($"Розкладки: UA=0x{(long)_layouts.Ua:X}, EN=0x{(long)_layouts.En:X}");
        _monitor = new KeyboardMonitor(_layouts, SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
        _exceptions.Load();
        Task.Run(() =>
        {
            try { _detector.Load(); }
            catch (Exception ex) { Log.Write($"ПОМИЛКА завантаження словників: {ex}"); }
        });
        _replace.Load();
        Settings.MigrateAutostart();
        Settings.RefreshAutostart();
        Log.Write($"Запуск з «{Application.ExecutablePath}»");
        _converter = new Converter(_layouts, _monitor, _detector, _exceptions, _settings, _replace);
        _monitor.IsAbbrev = _replace.Contains;
        _monitor.NeedsApostrophe = w => _settings.FixApostrophe && _detector.FixApostrophe(w.TrimEnd(".,!?;:\")»".ToCharArray()), '\'') != null;
        _monitor.ApostropheOnSpaceRequested += _converter.ApostropheOnSpace;
        _monitor.LayoutToggleRequested += _converter.ToggleLayout;
        _monitor.ReplaceOnEnterRequested += _converter.ReplaceOnEnter;
        _monitor.LastWordRequested += _converter.ConvertLastWord;
        _monitor.SelectionRequested += _converter.ConvertSelection;
        _monitor.AutoCheckRequested += _converter.AutoCheck;
        _monitor.EarlyCheckRequested += _converter.EarlyCheck;
        _monitor.WordStarted += () => { if (_settings.SkipPasswords) PasswordGuard.Warmup(Native.GetForegroundWindow()); };
        _monitor.UndoRequested += _converter.UndoAuto;
        _converter.Corrected += OnCorrected;

        _miEnabled = Check("Увімкнено", () => _settings.Enabled, v => _settings.Enabled = v);
        _miAutoMode = Check("Автоматичне виправлення", () => _settings.AutoMode, v => _settings.AutoMode = v);
        _miExcludeApp = new ToolStripMenuItem("Не виправляти в цій програмі");
        _miExcludeApp.Click += (_, _) => ToggleExcludeLastApp();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"UaSwitch {Application.ProductVersion.Split('+')[0]}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miEnabled);
        menu.Items.Add(_miAutoMode);
        menu.Items.Add(_miExcludeApp);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Налаштування…", null, (_, _) => ShowSettings());
        menu.Items.Add("Довідка", null, (_, _) => ShowHelp());
        menu.Items.Add("Перевірити оновлення", null, (_, _) => CheckForUpdates(manual: true));
        menu.Items.Add("Вихід", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => RefreshMenu();

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "UaSwitch" };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_pendingRelease is { } r) { _pendingRelease = null; Updater.ShowNewVersion(r, _settings); }
        };

        Apply(save: false);
        _monitor.Start();

        _timer = new System.Windows.Forms.Timer { Interval = 150 };
        _timer.Tick += (_, _) => { TrackForegroundApp(); UpdateIcon(); };
        _timer.Start();

        // Автоперевірка оновлень (якщо ввімкнена): через хвилину після старту, далі раз на добу
        _updateTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = 3_600_000;
            if (_settings.CheckUpdates && _settings.LastUpdateCheck != DateTime.Today.ToString("yyyy-MM-dd"))
                CheckForUpdates(manual: false);
        };
        _updateTimer.Start();
        UpdateIcon();

        Log.Write("Хуки та іконку встановлено");
        if (Settings.Autostart && Settings.RunAsAdminFlag())
        {
            Log.Write("Увімкнено «Запускати від імені адміністратора» — автозапуск не спрацює");
            _tray.ShowBalloonTip(15000, "Автозапуск не спрацює",
                "У властивостях UaSwitch.exe (вкладка «Сумісність») стоїть «Запускати від імені адміністратора». " +
                "Windows не запускає такі програми під час входу. Зніміть цю галочку.",
                ToolTipIcon.Warning);
        }
        else if (Settings.RunningFromTemp)
            _tray.ShowBalloonTip(10000, "UaSwitch запущено прямо з архіву",
                "Розпакуйте архів у постійну папку (наприклад, Документи) і запускайте звідти — інакше автозапуск не працюватиме.",
                ToolTipIcon.Warning);
        else if (!_layouts.Ready)
            _tray.ShowBalloonTip(8000, "UaSwitch",
                "Не знайдено українську та/або англійську розкладку. Додайте обидві в Параметри → Час і мова → Мова.",
                ToolTipIcon.Warning);
        else if (!_settings.WelcomeShown) // привітання — лише при першому запуску
        {
            _tray.ShowBalloonTip(4000, "UaSwitch працює",
                "Подвійний клік по іконці — налаштування. Backspace одразу після виправлення — скасувати.",
                ToolTipIcon.Info);
            _settings.WelcomeShown = true;
            _settings.Save();
        }
    }

    // ---------- Меню ----------

    private ToolStripMenuItem Check(string text, Func<bool> get, Action<bool> set)
    {
        var mi = new ToolStripMenuItem(text) { Checked = get() };
        mi.Click += (_, _) => { set(!get()); Apply(); };
        return mi;
    }

    private void RefreshMenu()
    {
        _miEnabled.Checked = _settings.Enabled;
        _miAutoMode.Checked = _settings.AutoMode;
        _miAutoMode.Enabled = _settings.Enabled;
        if (_lastApp == null)
        {
            _miExcludeApp.Text = "Не виправляти в цій програмі";
            _miExcludeApp.Enabled = false;
            _miExcludeApp.Checked = false;
        }
        else
        {
            _miExcludeApp.Text = $"Не виправляти в «{_lastAppTitle}»";
            _miExcludeApp.Enabled = true;
            _miExcludeApp.Checked = _settings.IsExcluded(_lastApp);
        }
    }

    private void ToggleExcludeLastApp()
    {
        if (_lastApp == null) return;
        bool now = !_settings.IsExcluded(_lastApp);
        _settings.SetExcluded(_lastApp, now);
        _settings.Save();
        _tray.ShowBalloonTip(3000, "UaSwitch",
            now ? $"Автовиправлення вимкнено в «{_lastAppTitle}»." : $"Автовиправлення знову працює в «{_lastAppTitle}».",
            ToolTipIcon.Info);
    }

    /// <summary>Запам'ятовує останню програму, з якою працював користувач (до кліку по іконці в треї).</summary>
    private void TrackForegroundApp()
    {
        try
        {
            var fg = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(fg, out uint pid);
            if (pid == 0 || pid == _ownPid || pid == _lastPid) return;
            using var p = Process.GetProcessById((int)pid);
            if (ShellProcs.Contains(p.ProcessName)) return;
            _lastPid = pid;
            _lastApp = p.ProcessName;
            string title = p.ProcessName;
            try
            {
                var d = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(d) && d!.Length <= 40) title = d.Trim();
            }
            catch { }
            _lastAppTitle = title;
        }
        catch { }
    }

    private void ShowSettings()
    {
        if (_settingsForm != null) { _settingsForm.Activate(); return; }
        _settingsForm = new SettingsForm(_settings, _exceptions, _replace);
        _settingsForm.Changed += () => Apply(save: false); // кожна зміна зберігається й діє одразу
        _settingsForm.CheckUpdatesRequested += () => CheckForUpdates(manual: true);
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
        };
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void Apply(bool save = true)
    {
        _monitor.Enabled = _settings.Enabled;
        _monitor.PauseHotkeys = _settings.PauseHotkeys;
        _monitor.DoubleShift = _settings.DoubleShift;
        _monitor.DoubleCtrl = _settings.DoubleCtrl;
        _monitor.AutoMode = _settings.AutoMode;
        _monitor.CapsSwitch = _settings.CapsSwitch;
        _monitor.ReplaceEnabled = _settings.AutoReplace;
        if (!_settings.Enabled) _monitor.ResetWord();
        if (save) _settings.Save();
        _iconState = "";
        UpdateIcon();
    }

    // ---------- Сигнал виправлення ----------

    private void OnCorrected()
    {
        if (_settings.Sound) Feedback.Click();
        if (_settings.Flash)
        {
            _flashUntil = Environment.TickCount + 450;
            _iconState = "";
            UpdateIcon();
        }
    }

    // ---------- Іконка ----------

    private void UpdateIcon()
    {
        if (!_layouts.Ready) _layouts.Refresh();
        var hkl = Native.ForegroundLayout(out _);
        string label = _layouts.IsUa(hkl) ? "UA" : (Native.LangId(hkl) & 0x3FF) == 0x09 ? "EN" : "??";
        bool flash = _flashUntil != 0 && Environment.TickCount - _flashUntil < 0;
        if (!flash) _flashUntil = 0;
        string state = label + (_settings.Enabled ? "+" : "-") + (flash ? "*" : "");
        if (state == _iconState) return;
        _iconState = state;

        var old = _icon;
        _icon = DrawIcon(label, _settings.Enabled, flash);
        _tray.Icon = _icon;
        _tray.Text = _settings.Enabled ? $"UaSwitch — {label}" : "UaSwitch — вимкнено";
        if (old != null) { Native.DestroyIcon(old.Handle); old.Dispose(); }
    }

    private static Icon DrawIcon(string label, bool enabled, bool flash)
    {
        const int s = 32;
        using var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Color bg = flash ? Color.FromArgb(22, 163, 74)
                     : !enabled ? Color.FromArgb(150, 150, 150)
                     : label == "UA" ? Color.FromArgb(0, 87, 183)
                     : Color.FromArgb(55, 60, 70);
            Color fg = !flash && enabled && label == "UA" ? Color.FromArgb(255, 215, 0) : Color.White;
            using (var path = RoundRect(new Rectangle(0, 2, s - 1, s - 5), 7))
            using (var b = new SolidBrush(bg))
                g.FillPath(b, path);
            using var font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var tb = new SolidBrush(fg);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(label, font, tb, new RectangleF(0, 1, s, s), fmt);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundRect(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private System.Windows.Forms.Timer _updateTimer = null!;
    private bool _checking;

    /// <summary>Перевірка нової версії у фоні. Вручну — завжди показуємо результат; автоматично — лише якщо є нова.</summary>
    private void CheckForUpdates(bool manual)
    {
        if (_checking) return;
        _checking = true;
        var ui = SynchronizationContext.Current;
        Task.Run(() =>
        {
            var r = Updater.Fetch();
            ui?.Post(_ =>
            {
                _checking = false;
                if (r == null)
                {
                    if (manual) OpenUrl(Updater.ReleasesUrl); // немає зв'язку з GitHub — хай людина гляне сама
                    return;
                }
                _settings.LastUpdateCheck = DateTime.Today.ToString("yyyy-MM-dd");
                _settings.Save();
                bool newer = r.Version > Updater.Current;
                Log.Write($"Перевірка оновлень: остання {r.Version}, у вас {Updater.Current}");
                if (newer && (manual || _settings.SkippedVersion != r.Version.ToString()))
                {
                    if (manual) Updater.ShowNewVersion(r, _settings);
                    else
                    {
                        _pendingRelease = r;
                        _tray.ShowBalloonTip(10000, $"Вийшла UaSwitch {r.Version}", "Клацніть, щоб побачити, що нового.", ToolTipIcon.Info);
                    }
                }
                else if (manual)
                    MessageBox.Show($"У вас остання версія — {Updater.Current}.", "UaSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }, null);
        });
    }

    private Updater.Release? _pendingRelease;

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Log.Write("Посилання: " + e.Message); }
    }

    private static void ShowHelp() => MessageBox.Show(
        "Автоматичний режим: слово, набране не в тій розкладці,\n" +
        "виправляється після пробілу. Не так? Натисніть Backspace\n" +
        "одразу — виправлення скасується, а слово потрапить у винятки.\n\n" +
        "Часто слово виправляється вже на 4-й літері.\n" +
        "Перед «що», «щоб», «але», «який» ставиться кома (Backspace — прибрати).\n\n" +
        "Також виправляється регістр: пРИВІТ → Привіт (і вимикається\n" +
        "Caps Lock), ПРивіт → Привіт, велика літера після крапки.\n" +
        "Апостроф: мясо → м'ясо, обєкт → об'єкт.\n\n" +
        "Вручну:\n" +
        "•  Pause або двічі Shift — виправити останнє слово\n" +
        "   (натисніть ще раз, щоб повернути як було)\n" +
        "•  Виділіть текст, потім Shift+Pause або двічі Ctrl —\n" +
        "   виправити виділений фрагмент\n\n" +
        "Автозаміна: скорочення + пробіл або Enter → ваш текст\n" +
        "(список — у налаштуваннях, вкладка «Автозаміна»).\n" +
        "Caps Lock може перемикати мову (вкладка «Гарячі клавіші»).\n\n" +
        "Іконка: подвійний клік — налаштування, права кнопка — меню.\n" +
        "Меню → «Не виправляти в …» — вимкнути автоматику в програмі,\n" +
        "з якою ви щойно працювали.\n\n" +
        "Набраний текст ніде не зберігається і нікуди не передається.\n" +
        "На диск записуються лише налаштування, винятки й автозаміна.\n\n" +
        "Розробник: Ruslan Pichkar · uaswitch.github.io/UaSwitch",
        "UaSwitch — довідка", MessageBoxButtons.OK, MessageBoxIcon.Information);

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _monitor.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        if (_icon != null) Native.DestroyIcon(_icon.Handle);
        base.ExitThreadCore();
    }
}
