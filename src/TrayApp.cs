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
    private readonly ToolStripMenuItem _miEnabled, _miAutoMode, _miExcludeApp, _miAppLang, _miTranslit, _miAmount, _miCase;
    private readonly LangIndicator _indicator;
    private IntPtr _langHwnd;
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
            Native.TrimMemory(); // після завантаження лишається багато тимчасових даних
        });
        _replace.Load();
        if (!_settings.ReplaceDefaultsAdded)
        {
            _replace.AddDefaults();
            _settings.ReplaceDefaultsAdded = true;
            _settings.Save();
        }
        Settings.MigrateAutostart();
        Settings.RefreshAutostart();
        Log.Write($"Запуск з «{Application.ExecutablePath}»");
        _converter = new Converter(_layouts, _monitor, _detector, _exceptions, _settings, _replace);
        _monitor.IsAbbrev = _replace.Contains;
        _monitor.NeedsApostrophe = w => (_settings.FixApostrophe || _settings.FixG) &&
            _detector.FixSpelling(w.TrimEnd(".,!?;:\")»".ToCharArray()), '\'', _settings.FixApostrophe, _settings.FixG) != null;
        _monitor.ApostropheOnSpaceRequested += _converter.ApostropheOnSpace;
        _monitor.LayoutToggleRequested += _converter.ToggleLayout;
        _monitor.ReplaceOnEnterRequested += _converter.ReplaceOnEnter;
        _monitor.ConvertRequested += _converter.ConvertSmart;
        _monitor.AutoCheckRequested += _converter.AutoCheck;
        _monitor.EarlyCheckRequested += _converter.EarlyCheck;
        _monitor.WordStarted += () =>
        {
            _monitor.SetCaps(Native.CapsLockOn()); // звіряємо Caps Lock з Windows на початку кожного слова
            if (_settings.SkipPasswords) PasswordGuard.Warmup(Native.GetForegroundWindow());
        };
        _monitor.IsExcludedWindow = hwnd => _converter.IsExcludedApp(hwnd, out _) || GamePaused();
        _converter.Notify += (title, text) => { _pendingRelease = null; _lastException = null; _tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Info); };
        _hotkeys = new HotkeyWindow(id =>
        {
            if (id == 1) _converter.TransliterateSelection();
            else if (id == 2) _converter.AmountSelection();
            else if (id == 3) _converter.CycleCase();
        });
        _converter.ExceptionAdded += word => { _pendingRelease = null; _lastException = word; _lastExceptionAt = Environment.TickCount; _tray.ShowBalloonTip(6000, $"«{word}» більше не виправлятиметься",
            "Виправлення скасували Backspace, тож слово додано у винятки.\nПросто передумали? Натисніть тут — і слово знову виправлятиметься.", ToolTipIcon.Info); };
        _monitor.UndoRequested += _converter.UndoAuto;
        _converter.Corrected += OnCorrected;

        _miEnabled = Check("Увімкнено", () => _settings.Enabled, v => _settings.Enabled = v);
        _miAutoMode = Check("Автоматичне виправлення", () => _settings.AutoMode, v => _settings.AutoMode = v);
        _miExcludeApp = new ToolStripMenuItem("Не виправляти в цій програмі");
        _miExcludeApp.Click += (_, _) => ToggleExcludeLastApp();
        _miAppLang = new ToolStripMenuItem("Мова для цієї програми");
        foreach (var (code, text) in new[] { ("", "Не змінювати"), ("UA", "Українська"), ("EN", "English") })
        {
            var mi = new ToolStripMenuItem(text) { Tag = code };
            mi.Click += (_, _) => SetAppLanguage(code);
            _miAppLang.DropDownItems.Add(mi);
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"UaSwitch {Application.ProductVersion.Split('+')[0]}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miEnabled);
        menu.Items.Add(_miAutoMode);
        menu.Items.Add(_miExcludeApp);
        menu.Items.Add(_miAppLang);
        menu.Items.Add(new ToolStripSeparator());
        _miTranslit = new ToolStripMenuItem("", null, (_, _) => _converter.TransliterateSelection(_monitor.LastWindow));
        _miAmount = new ToolStripMenuItem("", null, (_, _) => _converter.AmountSelection(_monitor.LastWindow));
        menu.Items.Add(_miTranslit);
        menu.Items.Add(_miAmount);
        _miCase = new ToolStripMenuItem("", null, (_, _) => _converter.CycleCase(_monitor.LastWindow));
        menu.Items.Add(_miCase);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Налаштування…", null, (_, _) => ShowSettings());
        menu.Items.Add("Довідка", null, (_, _) => ShowHelp());
        menu.Items.Add("Перевірити оновлення", null, (_, _) => CheckForUpdates(manual: true));
        menu.Items.Add("Повідомити про проблему…", null, (_, _) => ReportProblem());
        menu.Items.Add("Вихід", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => RefreshMenu();

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "UaSwitch" };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_pendingRelease is { } r) { _pendingRelease = null; Updater.ShowNewVersion(r, _settings); }
            else if (_lastException is { } word && Environment.TickCount - _lastExceptionAt < 8000)
            {
                _lastException = null;
                _exceptions.Remove(word);
                Log.Write("Слово прибрано з винятків (клік на сповіщення)");
                _tray.ShowBalloonTip(3000, "UaSwitch", $"«{word}» знову виправлятиметься.", ToolTipIcon.Info);
            }
        };

        _indicator = new LangIndicator(_settings, _layouts, _monitor, _ => false);
        Converter.LayoutSwitched += _indicator.Flash;

        Apply(save: false);
        _monitor.Start();

        _timer = new System.Windows.Forms.Timer { Interval = 150 };
        _timer.Tick += (_, _) => { TrackForegroundApp(); ApplyAppLanguage(); _monitor.RefreshExcluded(); UpdateIcon(); };
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
        _miTranslit.Text = $"Транслітерувати виділене  (Ctrl+Alt+{Settings.KeyName(_settings.TranslitKey)})";
        _miAmount.Text = $"Сума прописом  (Ctrl+Alt+{Settings.KeyName(_settings.AmountKey)})";
        _miCase.Text = $"Змінити регістр  (Ctrl+Alt+{Settings.KeyName(_settings.CaseKey)})";
        if (_lastApp == null)
        {
            _miExcludeApp.Text = "Не виправляти в цій програмі";
            _miExcludeApp.Enabled = false;
            _miExcludeApp.Checked = false;
            _miAppLang.Enabled = false;
        }
        else
        {
            _miExcludeApp.Text = $"Не виправляти в «{_lastAppTitle}»";
            _miExcludeApp.Enabled = true;
            _miExcludeApp.Checked = _settings.IsExcluded(_lastApp);
            _miAppLang.Enabled = true;
            _miAppLang.Text = $"Мова для «{_lastAppTitle}»";
            _settings.AppLanguageMap.TryGetValue(_lastApp, out var code);
            foreach (ToolStripMenuItem mi in _miAppLang.DropDownItems) mi.Checked = (string)mi.Tag == (code ?? "");
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

    private void SetAppLanguage(string code)
    {
        if (_lastApp == null) return;
        var map = _settings.AppLanguageMap;
        if (code.Length == 0) map.Remove(_lastApp); else map[_lastApp] = code;
        _settings.AppLanguageMap = map;
        _settings.Save();
        _langHwnd = IntPtr.Zero; // застосувати одразу, коли людина повернеться в програму
        _tray.ShowBalloonTip(3000, "UaSwitch", code.Length == 0
            ? $"У «{_lastAppTitle}» мова більше не перемикається сама."
            : $"У «{_lastAppTitle}» щоразу вмикатиметься {(code == "UA" ? "українська" : "англійська")}.", ToolTipIcon.Info);
    }

    /// <summary>Перейшли в інше вікно програми, для якої задано мову, — вмикаємо її (один раз на вікно).</summary>
    private void ApplyAppLanguage()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == _langHwnd || fg == IntPtr.Zero || !_layouts.Ready || !_settings.Enabled) return;
        _langHwnd = fg;
        if (_settings.AppLanguages.Length == 0) return;
        _converter.IsExcludedApp(fg, out var name);
        if (!_settings.AppLanguageMap.TryGetValue(name, out var code)) return;
        var target = code == "UA" ? _layouts.Ua : code == "EN" ? _layouts.En : IntPtr.Zero;
        var cur = Native.ForegroundLayout(out _);
        if (target != IntPtr.Zero && Native.LangId(cur) != Native.LangId(target))
        {
            Converter.SwitchLayout(fg, target);
            Log.Write($"Мова для програми: {code}");
        }
    }

    /// <summary>Гра чи інша програма на весь екран — повна пауза (якщо ввімкнено).</summary>
    private bool GamePaused() => _settings.PauseInGames && Native.FullScreenAppRunning();

    private void ReportProblem()
    {
        string layouts = "";
        try
        {
            int n = Native.GetKeyboardLayoutList(0, null);
            var list = new IntPtr[n];
            Native.GetKeyboardLayoutList(n, list);
            layouts = string.Join(", ", list.Select(h => $"0x{(long)h:X}"));
        }
        catch { }
        string body = "Що сталося:\n\n\nЩо мало статися:\n\n\nУ якій програмі (Word, Chrome, Telegram…):\n\n" +
                      $"---\nUaSwitch {Updater.Current}\nWindows {Environment.OSVersion.Version}\nРозкладки: {layouts}\n";
        OpenUrl("https://github.com/uaswitch/UaSwitch/issues/new?body=" + Uri.EscapeDataString(body));
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
        _settingsForm.RestartRequested += () =>
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, "/restart") { UseShellExecute = false });
            _settingsForm?.Close();
            ExitThread();
        };
        _settingsForm.CaptureChanged += on => _monitor.HotkeyCapture = on;
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            Native.TrimMemory(); // вікно налаштувань більше не потрібне
        };
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void Apply(bool save = true)
    {
        _monitor.Enabled = _settings.Enabled;
        _monitor.PauseHotkeys = _settings.PauseHotkeys;
        _monitor.PauseKey = _settings.PauseKey;
        _monitor.DoubleShift = _settings.DoubleShift;
        _monitor.DoubleCtrl = _settings.DoubleCtrl;
        _monitor.AutoMode = _settings.AutoMode;
        _monitor.CapsSwitch = _settings.CapsSwitch;
        _hotkeys?.Set(1, _settings.HotkeyTranslit && _settings.Enabled, Native.MOD_CONTROL | Native.MOD_ALT, (char)_settings.TranslitKey);
        _hotkeys?.Set(2, _settings.HotkeyAmount && _settings.Enabled, Native.MOD_CONTROL | Native.MOD_ALT, (char)_settings.AmountKey);
        _hotkeys?.Set(3, _settings.HotkeyCase && _settings.Enabled, Native.MOD_CONTROL | Native.MOD_ALT, (char)_settings.CaseKey);
        _monitor.OwnHotkeys = new uint[] { (uint)_settings.TranslitKey, (uint)_settings.AmountKey, (uint)_settings.CaseKey };
        _monitor.ReplaceEnabled = _settings.AutoReplace;
        _indicator?.Apply();
        StickyKeys.Set(_settings.NoStickyPrompt && _settings.DoubleShift && _settings.Enabled);
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
        var hkl = Native.ForegroundLayout(out var fg);
        int lang = Native.LangId(hkl) & 0x3FF;
        string label = _layouts.IsUa(hkl) ? "UA" : lang == 0x09 ? "EN" : LangCode(hkl);
        bool flash = _flashUntil != 0 && Environment.TickCount - _flashUntil < 0;
        if (!flash) _flashUntil = 0;
        bool game = fg != IntPtr.Zero && GamePaused();
        bool excluded = fg != IntPtr.Zero && (_converter.IsExcludedApp(fg, out _) || game);
        string state = label + (_settings.Enabled ? "+" : "-") + (flash ? "*" : "") + (excluded ? "x" : "") + _settings.IconStyle + (_lightTaskbar ? "L" : "D");
        if (state == _iconState) return;
        _iconState = state;

        var old = _icon;
        _icon = DrawIcon(label, _settings.Enabled, flash, excluded, _settings.IconStyle, _lightTaskbar);
        _tray.Icon = _icon;
        _tray.Text = !_settings.Enabled ? "UaSwitch — вимкнено" : game ? $"UaSwitch — {label}, пауза: програма на весь екран" : excluded ? $"UaSwitch — {label}, у цій програмі не виправляє" : $"UaSwitch — {label}";
        if (old != null) { Native.DestroyIcon(old.Handle); old.Dispose(); }
    }

    /// <summary>Світла панель завдань (для однотонної іконки) — перевіряємо раз на хвилину.</summary>
    private bool _lightTaskbar => (Environment.TickCount - _themeAt > 60000 || _themeAt == 0) ? (_light = ReadLight()) : _light;
    private bool _light;
    private int _themeAt;
    private bool ReadLight()
    {
        _themeAt = Environment.TickCount | 1;
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    private static string LangCode(IntPtr hkl)
    {
        try { return new System.Globalization.CultureInfo(Native.LangId(hkl)).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch { return "??"; }
    }

    private static Icon DrawIcon(string label, bool enabled, bool flash, bool excluded, string style, bool lightTaskbar)
    {
        const int s = 32;
        bool muted = !enabled || excluded;
        using var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var box = new Rectangle(0, 2, s - 1, s - 5);

            if (style == "flags" && (label == "UA" || label == "EN"))
            {
                using var path = RoundRect(box, 6);
                g.SetClip(path);
                if (label == "UA")
                {
                    using var blue = new SolidBrush(Color.FromArgb(0, 87, 183));
                    using var yellow = new SolidBrush(Color.FromArgb(255, 215, 0));
                    g.FillRectangle(blue, box.X, box.Y, box.Width + 1, box.Height / 2 + 1);
                    g.FillRectangle(yellow, box.X, box.Y + box.Height / 2, box.Width + 1, box.Height / 2 + 2);
                }
                else // спрощений прапор Великої Британії: на 16–32 px деталі зливаються
                {
                    using var navy = new SolidBrush(Color.FromArgb(1, 33, 105));
                    g.FillRectangle(navy, box);
                    using var white = new Pen(Color.White, 6f);
                    using var red = new Pen(Color.FromArgb(200, 16, 46), 2.5f);
                    g.DrawLine(white, box.Left, box.Top, box.Right, box.Bottom); g.DrawLine(white, box.Left, box.Bottom, box.Right, box.Top);
                    g.DrawLine(red, box.Left, box.Top, box.Right, box.Bottom); g.DrawLine(red, box.Left, box.Bottom, box.Right, box.Top);
                    using var whiteX = new SolidBrush(Color.White);
                    using var redX = new SolidBrush(Color.FromArgb(200, 16, 46));
                    int cx = box.Left + box.Width / 2, cy = box.Top + box.Height / 2;
                    g.FillRectangle(whiteX, cx - 5, box.Top, 10, box.Height + 1); g.FillRectangle(whiteX, box.Left, cy - 5, box.Width + 1, 10);
                    g.FillRectangle(redX, cx - 3, box.Top, 6, box.Height + 1); g.FillRectangle(redX, box.Left, cy - 3, box.Width + 1, 6);
                }
                g.ResetClip();
                if (flash) { using var pen = new Pen(Color.FromArgb(22, 163, 74), 3f); g.DrawPath(pen, path); }
            }
            else if (style == "mono")
            {
                Color ink = lightTaskbar ? Color.FromArgb(30, 30, 30) : Color.White;
                if (flash) ink = Color.FromArgb(22, 163, 74);
                using var font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var tb = new SolidBrush(ink);
                g.DrawString(label, font, tb, new RectangleF(0, 1, s, s), fmt);
            }
            else if (style == "dot")
            {
                var r = new Rectangle(6, 6, 20, 20);
                if (label == "UA")
                {
                    using var blue = new SolidBrush(Color.FromArgb(0, 87, 183));
                    using var yellow = new SolidBrush(Color.FromArgb(255, 215, 0));
                    g.FillPie(blue, r, 180, 180); g.FillPie(yellow, r, 0, 180);
                }
                else { using var b = new SolidBrush(Color.FromArgb(120, 128, 140)); g.FillEllipse(b, r); }
                if (flash) { using var pen = new Pen(Color.FromArgb(22, 163, 74), 3f); g.DrawEllipse(pen, 4, 4, 24, 24); }
            }
            else
            {
                Color bg = flash ? Color.FromArgb(22, 163, 74)
                         : label == "UA" ? Color.FromArgb(0, 87, 183)
                         : Color.FromArgb(55, 60, 70);
                Color fg = !flash && label == "UA" ? Color.FromArgb(255, 215, 0) : Color.White;
                using (var path = RoundRect(box, 7))
                using (var b = new SolidBrush(bg))
                    g.FillPath(b, path);
                using var font = new Font("Segoe UI", label.Length > 2 ? 11f : 14f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var tb = new SolidBrush(fg);
                g.DrawString(label, font, tb, new RectangleF(0, 1, s, s), fmt);
            }

        }
        if (!muted || flash) return Icon.FromHandle(bmp.GetHicon());

        // Пауза чи програма з винятків: іконка сіріє й блякне, а виключена програма ще й перекреслена
        using var grey = new Bitmap(s, s);
        using (var g = Graphics.FromImage(grey))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var m = new System.Drawing.Imaging.ColorMatrix(new[]
            {
                new[] { .3f, .3f, .3f, 0, 0 }, new[] { .59f, .59f, .59f, 0, 0 }, new[] { .11f, .11f, .11f, 0, 0 },
                new[] { 0f, 0, 0, .6f, 0 }, new[] { 0f, 0, 0, 0, 1 },
            });
            using var attr = new System.Drawing.Imaging.ImageAttributes();
            attr.SetColorMatrix(m);
            g.DrawImage(bmp, new Rectangle(0, 0, s, s), 0, 0, s, s, GraphicsUnit.Pixel, attr);
            if (excluded && enabled)
            {
                using var pen = new Pen(Color.FromArgb(220, 38, 38), 3f);
                g.DrawLine(pen, 4, s - 4, s - 4, 4);
            }
        }
        return Icon.FromHandle(grey.GetHicon());
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

    private HotkeyWindow? _hotkeys;

    /// <summary>Невидиме вікно для глобальних гарячих клавіш (RegisterHotKey).</summary>
    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly Action<int> _onHotkey;
        private readonly HashSet<int> _registered = new();
        public HotkeyWindow(Action<int> onHotkey) { _onHotkey = onHotkey; CreateHandle(new CreateParams()); }

        public void Set(int id, bool on, uint mods, char key)
        {
            if (_registered.Remove(id)) Native.UnregisterHotKey(Handle, id);
            if (!on) return;
            if (Native.RegisterHotKey(Handle, id, mods | Native.MOD_NOREPEAT, key)) _registered.Add(id);
            else Log.Write($"Гаряча клавіша Ctrl+Alt+{key} зайнята іншою програмою");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) _onHotkey((int)m.WParam); // WM_HOTKEY
            base.WndProc(ref m);
        }

        public void Dispose() { foreach (var id in _registered) Native.UnregisterHotKey(Handle, id); DestroyHandle(); }
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
    private string? _lastException;
    private int _lastExceptionAt;

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Log.Write("Посилання: " + e.Message); }
    }

    private void ShowHelp() { string key = Settings.KeyName(_settings.PauseKey); MessageBox.Show(
        "Автоматичний режим: слово, набране не в тій розкладці,\n" +
        "виправляється після пробілу. Не так? Натисніть Backspace\n" +
        "одразу — виправлення скасується, а слово потрапить у винятки.\n\n" +
        "Часто слово виправляється вже на 4-й літері.\n" +
        "Перед «що», «щоб», «але», «який» ставиться кома (Backspace — прибрати).\n\n" +
        "Також виправляється регістр: пРИВІТ → Привіт (і вимикається\n" +
        "Caps Lock), ПРивіт → Привіт, велика літера після крапки.\n" +
        "Апостроф: мясо → м'ясо, обєкт → об'єкт.\n\n" +
        "Вручну:\n" +
        $"•  {key}, двічі Shift або двічі Ctrl — виправити щойно\n" +
        "   набране слово, а якщо виділено текст — його\n" +
        "   (натисніть ще раз, щоб повернути слово як було)\n\n" +
        $"•  Ctrl+Alt+{Settings.KeyName(_settings.CaseKey)} — регістр слова чи виділеного: малі → ВЕЛИКІ → З великої\n\n" +
        "Автозаміна: скорочення + пробіл або Enter → ваш текст\n" +
        "(список — у налаштуваннях; готові .дата і .час).\n" +
        "Мітка мови біля курсора й вигляд іконки — вкладка «Вигляд».\n" +
        "Caps Lock може перемикати мову (вкладка «Гарячі клавіші»).\n\n" +
        "Іконка: подвійний клік — налаштування, права кнопка — меню.\n" +
        "Меню → «Не виправляти в …» — вимкнути автоматику в програмі,\n" +
        "з якою ви щойно працювали; «Мова для …» — завжди вмикати в ній\n" +
        "українську чи англійську.\n" +
        "У вікнах, запущених від імені адміністратора, програма не працює\n" +
        "(обмеження Windows).\n\n" +
        "Набраний текст ніде не зберігається і нікуди не передається.\n" +
        "На диск записуються лише налаштування, винятки й автозаміна.\n\n" +
        "Розробник: Ruslan Pichkar · uaswitch.github.io/UaSwitch",
        "UaSwitch — довідка", MessageBoxButtons.OK, MessageBoxIcon.Information); }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _monitor.Dispose();
        StickyKeys.Set(false);
        _indicator.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        if (_icon != null) Native.DestroyIcon(_icon.Handle);
        base.ExitThreadCore();
    }
}
