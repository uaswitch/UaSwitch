using System.Diagnostics;
using System.Drawing;

namespace UaSwitch;

/// <summary>Вікно налаштувань: загальні, гарячі клавіші, програми-винятки, слова-винятки.</summary>
internal sealed class SettingsForm : Form
{
    private readonly Settings _s;
    private readonly Exceptions _ex;
    private readonly CheckBox _enabled, _auto, _early, _comma, _space, _caps, _twoCaps, _sentence, _apostrophe, _g, _cyrEn, _cyrUa, _passwords, _sound, _flash, _updates, _autostart, _pause, _dShift, _dCtrl, _capsSwitch, _translit, _amount, _case, _replaceOn,
        _indBig, _indNum, _indFull, _manual, _games, _sticky;
    private readonly AutoReplace _replace;
    private readonly ListView _replaceList = new();
    private readonly ListBox _apps = new(), _words = new(), _appLangs = new();
    private readonly ComboBox _appInput = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _wordInput = new();
    /// <summary>Будь-яка зміна вже збережена — застосувати її.</summary>
    public event Action? Changed;
    /// <summary>Натиснуто «Перевірити оновлення».</summary>
    public event Action? CheckUpdatesRequested;
    /// <summary>Імпортовано налаштування — програму треба перезапустити.</summary>
    public event Action? RestartRequested;
    private bool _ready;

    public SettingsForm(Settings settings, Exceptions exceptions, AutoReplace replace)
    {
        _s = settings;
        _ex = exceptions;
        _replace = replace;

        string version = Application.ProductVersion.Split('+')[0];
        Text = $"UaSwitch {version} — налаштування";
        SuspendLayout();
        // Розміри задано для 96 DPI; на 125–200 % вікно пропорційно збільшується
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 610);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        ShowInTaskbar = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var tabs = new TabControl { Dock = DockStyle.Fill };

        // ---- Загальне ----
        var general = Page("Загальне");
        _enabled = Check(general, "Програма увімкнена", _s.Enabled);
        _auto = Check(general, "Автоматично виправляти слова після пробілу", _s.AutoMode);
        _early = Check(general, "Виправляти слово вже на 4-й літері, не чекаючи пробілу", _s.EarlySwitch);
        _comma = Check(general, "Ставити кому перед «що», «щоб», «але», «який»…", _s.AutoComma);
        _space = Check(general, "Пробіл після коми  (Привіт,як → Привіт, як)", _s.SpaceAfterPunct);
        _caps = Check(general, "Виправляти випадковий Caps Lock  (пРИВІТ → Привіт)", _s.FixCaps);
        _twoCaps = Check(general, "Виправляти дві великі на початку  (ПРивіт → Привіт)", _s.FixTwoCaps);
        _sentence = Check(general, "Велика літера на початку речення  (так. добре → так. Добре)", _s.SentenceCaps);
        _apostrophe = Check(general, "Ставити апостроф  (мясо → м'ясо, обєкт → об'єкт)", _s.FixApostrophe);
        _g = Check(general, "Ставити ґ  (гудзик → ґудзик, агрус → аґрус)", _s.FixG);
        _passwords = Check(general, "Не виправляти в полях паролів  (і слова з цифрами: Qwe12rty)", _s.SkipPasswords);
        _sound = Check(general, "Тихий звук при виправленні", _s.Sound);
        _flash = Check(general, "Підсвічувати іконку при виправленні", _s.Flash);
        _updates = Check(general, "Повідомляти про нові версії  (раз на добу перевіряє GitHub)", _s.CheckUpdates);
        _autostart = Check(general, "Запускати разом з Windows", SafeAutostart());
        AddLink(general, "Відкрити журнал роботи", () => OpenNotepad(Log.FilePath));
        AddLabel(general, "\nПорада: Backspace одразу після автовиправлення скасовує його\nі додає слово у винятки.");

        // ---- Гарячі клавіші ----
        var keys = Page("Гарячі клавіші");
        _pause = Check(keys, "Pause — останнє слово,  Shift+Pause — виділений текст", _s.PauseHotkeys);
        _dShift = Check(keys, "Двічі Shift — виправити останнє слово", _s.DoubleShift);
        _dCtrl = Check(keys, "Двічі Ctrl — виправити виділений текст", _s.DoubleCtrl);
        _capsSwitch = Check(keys, "Caps Lock перемикає мову  (Shift+Caps — великі літери)", _s.CapsSwitch);
        _translit = Check(keys, "Ctrl+Alt+T — транслітерація  (Шевченко → Shevchenko)", _s.HotkeyTranslit);
        _amount = Check(keys, "Ctrl+Alt+S — сума прописом  (1250,50 → Одна тисяча…)", _s.HotkeyAmount);
        _case = Check(keys, "Ctrl+Alt+R — регістр  (привіт → ПРИВІТ → Привіт)", _s.HotkeyCase);
        AddLabel(keys, "\nТранслітерація — за постановою КМУ №55 (як у паспорті).\n" +
                       "Сума прописом — у гривнях і копійках, для актів і рахунків.\n" +
                       "Регістр — останнього слова або виділеного тексту.\n" +
                       "Усі три працюють і з меню іконки.\n\n" +
                       "Повторне натискання повертає слово як було.\n" +
                       "Подвійне натискання — два короткі натискання поспіль,\nбез інших клавіш між ними.");

        // ---- Програми ----
        var apps = Page("Програми");
        AddLabel(apps, "Автоматичне виправлення вимкнено в цих програмах:");
        foreach (var a in _s.ExcludedAppList) _apps.Items.Add(a);
        foreach (var name in RunningApps()) _appInput.Items.Add(name);
        ListEditor(apps, _apps, _appInput, () => _appInput.Text, Save, 150);
        BuildAppLanguages(apps);

        // ---- Слова-винятки ----
        var words = Page("Слова-винятки");
        AddLabel(words, "Ці слова не виправляються автоматично (як набрано):");
        foreach (var w in _ex.GetAll()) _words.Items.Add(w);
        ListEditor(words, _words, _wordInput, () => _wordInput.Text, Save);

        // ---- Автозаміна ----
        var repl = Page("Автозаміна");
        _replaceOn = Check(repl, "Замінювати скорочення текстом після пробілу чи Enter", _s.AutoReplace);
        BuildReplaceEditor(repl);

        // ---- Вигляд: іконка й мітка біля курсора ----
        var look = Page("Вигляд");
        AddLabel(look, "Іконка біля годинника:");
        Radios(look, _s.IconStyle, v => _s.IconStyle = v, ("text", "Текст  UA / EN"), ("flags", "Прапори"),
               ("mono", "Однотонна, під колір панелі завдань"), ("dot", "Лише кольорова крапка"));
        AddLabel(look, "\nМітка мови біля курсора (видно, яка мова, ще до набору):");
        Radios(look, _s.IndicatorMode, v => _s.IndicatorMode = v, ("off", "Не показувати"),
               ("switch", "На мить, коли мова чи Caps Lock змінилися"),
               ("text", "Над полями для тексту і під час набору"), ("always", "Завжди"));
        _indBig = Check(look, "Більша мітка", _s.IndicatorBig);
        _indNum = Check(look, "Показувати # , коли ввімкнено Num Lock  (⇪ для Caps Lock — завжди)", _s.IndicatorNumLock);
        _indFull = Check(look, "Ховати в іграх і відео на весь екран", _s.IndicatorHideFullScreen);
        AddLabel(look, "Під час набору мітка стоїть біля текстового курсора (якщо програма\nйого показує), інакше — біля вказівника миші.");

        // ---- Додатково ----
        var extra = Page("Додатково");
        _manual = Check(extra, "Не перекладати перше слово після ручного перемикання мови", _s.RespectManual);
        _games = Check(extra, "Пауза в іграх та інших програмах на весь екран", _s.PauseInGames);
        _sticky = Check(extra, "Не показувати вікно «Залипання клавіш» після подвійних Shift", _s.NoStickyPrompt);
        AddLabel(extra, "\nЯкщо в системі є ще російська (чи білоруська) розкладка:");
        _cyrEn = Check(extra, "Англійське слово, набране нею, виправляти  (руддщ → hello)", _s.FixOtherCyrillic);
        _cyrUa = Check(extra, "Українське слово, набране нею, виправляти  (привыт → привіт) — бета", _s.FixUaInRu);
        AddLabel(extra, "Звичайний текст цією мовою програма не чіпає.");
        AddLabel(extra, "\nПеренести на інший комп\'ютер (налаштування, винятки, автозаміна):");
        var io = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var exp = new Button { Text = "Експортувати…", Size = new Size(150, 30) };
        var imp = new Button { Text = "Імпортувати…", Size = new Size(150, 30) };
        exp.Click += (_, _) => ExportSettings();
        imp.Click += (_, _) => ImportSettings();
        io.Controls.Add(exp); io.Controls.Add(imp);
        Flow(extra).Controls.Add(io);

        tabs.TabPages.AddRange(new[] { general, keys, apps, words, repl, look, extra });

        // Нижня смужка: версія ліворуч, підказка про автозбереження праворуч
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
        const string update = "Перевірити оновлення", site = "uaswitch.github.io";
        var about = new LinkLabel
        {
            Text = $"Версія {version} · {update}\nRuslan Pichkar · {site}",
            AutoSize = true, Dock = DockStyle.Left, Padding = new Padding(8, 6, 0, 0), LinkColor = SystemColors.GrayText,
        };
        about.Links.Clear();
        about.Links.Add(about.Text.IndexOf(update), update.Length, "update");
        about.Links.Add(about.Text.IndexOf(site), site.Length, "https://uaswitch.github.io/UaSwitch/");
        about.LinkClicked += (_, e) => { if ((string)e.Link.LinkData == "update") { CheckUpdatesRequested?.Invoke(); return; } try { Process.Start(new ProcessStartInfo((string)e.Link.LinkData) { UseShellExecute = true }); } catch { } };
        var hint = new Label
        {
            Text = "Зміни зберігаються одразу", AutoSize = true, Dock = DockStyle.Right,
            Padding = new Padding(0, 15, 10, 0), ForeColor = SystemColors.GrayText,
        };
        bottom.Controls.Add(about);
        bottom.Controls.Add(hint);
        Controls.Add(tabs);
        Controls.Add(bottom);

        // Автозбереження: кожна галочка зберігається й застосовується одразу
        foreach (var c in new[] { _enabled, _auto, _early, _comma, _space, _caps, _twoCaps, _sentence, _apostrophe, _g, _cyrEn, _cyrUa, _passwords, _sound, _flash, _updates,
                                  _pause, _dShift, _dCtrl, _capsSwitch, _translit, _amount, _case, _replaceOn,
                                  _indBig, _indNum, _indFull, _manual, _games, _sticky })
            c.CheckedChanged += (_, _) => Save();
        _autostart.CheckedChanged += (_, _) =>
        {
            if (!_ready) return;
            try { Settings.Autostart = _autostart.Checked; } catch (Exception e) { Log.Write("Автозапуск: " + e.Message); }
            if (_autostart.Checked && Settings.RunningFromTemp)
                MessageBox.Show(this, "Програму запущено прямо з архіву, з тимчасової папки. Після перезавантаження цього файлу вже не буде.\n\n" +
                    "Розпакуйте архів у постійну папку (наприклад, Документи), запустіть UaSwitch звідти — і автозапуск запрацює.",
                    "UaSwitch — автозапуск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        ResumeLayout(true);
        MinimumSize = Size;
        _ready = true;
    }

    private void Save()
    {
        if (!_ready) return;
        SaveAll();
        Changed?.Invoke();
    }

    private void SaveAll()
    {
        _s.Enabled = _enabled.Checked;
        _s.AutoMode = _auto.Checked;
        _s.FixCaps = _caps.Checked;
        _s.EarlySwitch = _early.Checked;
        _s.AutoComma = _comma.Checked;
        _s.SpaceAfterPunct = _space.Checked;
        _s.FixTwoCaps = _twoCaps.Checked;
        _s.SentenceCaps = _sentence.Checked;
        _s.FixApostrophe = _apostrophe.Checked;
        _s.FixG = _g.Checked;
        _s.FixOtherCyrillic = _cyrEn.Checked;
        _s.FixUaInRu = _cyrUa.Checked;
        _s.CheckUpdates = _updates.Checked;
        _s.SkipPasswords = _passwords.Checked;
        _s.Sound = _sound.Checked;
        _s.Flash = _flash.Checked;
        _s.PauseHotkeys = _pause.Checked;
        _s.DoubleShift = _dShift.Checked;
        _s.DoubleCtrl = _dCtrl.Checked;
        _s.CapsSwitch = _capsSwitch.Checked;
        _s.HotkeyTranslit = _translit.Checked;
        _s.HotkeyAmount = _amount.Checked;
        _s.AutoReplace = _replaceOn.Checked;
        _s.HotkeyCase = _case.Checked;
        _s.IndicatorBig = _indBig.Checked;
        _s.IndicatorNumLock = _indNum.Checked;
        _s.IndicatorHideFullScreen = _indFull.Checked;
        _s.RespectManual = _manual.Checked;
        _s.PauseInGames = _games.Checked;
        _s.NoStickyPrompt = _sticky.Checked;
        _s.AppLanguages = string.Join(";", _appLangs.Items.Cast<string>().Select(x =>
        {
            int i = x.LastIndexOf(" — ");
            return x.Substring(0, i) + "=" + (x.EndsWith("українська") ? "UA" : "EN");
        }));
        _s.ExcludedApps = string.Join(",", _apps.Items.Cast<string>());
        _s.Save();
        _ex.SetAll(_words.Items.Cast<string>());
        _replace.SetAll(_replaceList.Items.Cast<ListViewItem>()
            .Select(i => new KeyValuePair<string, string>(i.Text, (string)i.Tag)));
    }

    // ---------- Автозаміна: редактор ----------

    private void BuildReplaceEditor(TabPage p)
    {
        _replaceList.View = View.Details;
        _replaceList.FullRowSelect = true;
        _replaceList.HideSelection = false;
        _replaceList.Size = new Size(450, 150);
        _replaceList.Columns.Add("Скорочення", 110);
        _replaceList.Columns.Add("Текст", 300);
        foreach (var kv in _replace.GetAll()) _replaceList.Items.Add(Row(kv.Key, kv.Value));

        var abbr = new TextBox { Width = 150 };
        var text = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Size = new Size(450, 76) };
        var add = new Button { Text = "Додати / змінити", Size = new Size(160, 32) };
        var remove = new Button { Text = "Видалити вибране", Size = new Size(160, 32) };

        _replaceList.SelectedIndexChanged += (_, _) =>
        {
            if (_replaceList.SelectedItems.Count != 1) return;
            var it = _replaceList.SelectedItems[0];
            abbr.Text = it.Text;
            text.Text = ((string)it.Tag).Replace("\n", Environment.NewLine);
        };
        add.Click += (_, _) =>
        {
            string a = abbr.Text.Trim(), t = AutoReplace.Normalize(text.Text);
            if (a.Length == 0 || t.Trim().Length == 0 || a.Contains(' ')) return;
            foreach (var it in _replaceList.Items.Cast<ListViewItem>().Where(i => i.Text.Equals(a, StringComparison.OrdinalIgnoreCase)).ToList())
                _replaceList.Items.Remove(it);
            _replaceList.Items.Add(Row(a, t));
            abbr.Text = text.Text = "";
            Save();
        };
        remove.Click += (_, _) =>
        {
            foreach (var it in _replaceList.SelectedItems.Cast<ListViewItem>().ToList()) _replaceList.Items.Remove(it);
            Save();
        };

        var flow = Flow(p);
        flow.Controls.Add(_replaceList);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        row.Controls.Add(new Label { Text = "Скорочення:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        row.Controls.Add(abbr);
        flow.Controls.Add(row);
        flow.Controls.Add(new Label { Text = "Текст (можна в кілька рядків):", AutoSize = true, Margin = new Padding(3, 6, 3, 2) });
        flow.Controls.Add(text);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        buttons.Controls.Add(add);
        buttons.Controls.Add(remove);
        flow.Controls.Add(buttons);
        AddLabel(p, "Наприклад: .зп → «З повагою, Руслан».\n" +
                    "У тексті можна писати {дата}, {час}, {буфер}.\n" +
                    "Скорочення краще починати з крапки, щоб вони\n" +
                    "не збігалися зі звичайними словами.\n" +
                    "Backspace одразу після заміни — скасувати.");
    }

    private static ListViewItem Row(string abbr, string text)
    {
        var it = new ListViewItem(abbr) { Tag = text };
        it.SubItems.Add(text.Replace("\n", " ⏎ "));
        return it;
    }

    // ---------- Побудова інтерфейсу ----------

    private static TabPage Page(string title)
    {
        var p = new TabPage(title) { Padding = new Padding(12) };
        p.Controls.Add(new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true
        });
        return p;
    }

    private static FlowLayoutPanel Flow(TabPage p) => (FlowLayoutPanel)p.Controls[0];

    private static CheckBox Check(TabPage p, string text, bool value)
    {
        var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
        Flow(p).Controls.Add(c);
        return c;
    }

    private static void AddLabel(TabPage p, string text) =>
        Flow(p).Controls.Add(new Label { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 6) });

    private static void AddLink(TabPage p, string text, Action onClick)
    {
        var l = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
        l.LinkClicked += (_, _) => onClick();
        Flow(p).Controls.Add(l);
    }

    private static void ListEditor(TabPage p, ListBox list, Control input, Func<string> value, Action changed, int height = 220)
    {
        list.Size = new Size(420, height);
        list.SelectionMode = SelectionMode.MultiExtended;
        input.Width = 300;
        var add = new Button { Text = "Додати", Size = new Size(76, 30) };
        var remove = new Button { Text = "Видалити вибране", Size = new Size(160, 30) };
        add.Click += (_, _) =>
        {
            var v = value().Trim();
            if (v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 4);
            if (v.Length > 0 && !list.Items.Cast<string>().Any(x => x.Equals(v, StringComparison.OrdinalIgnoreCase)))
                list.Items.Add(v);
            input.Text = "";
            changed();
        };
        remove.Click += (_, _) =>
        {
            foreach (var item in list.SelectedItems.Cast<object>().ToList()) list.Items.Remove(item);
            changed();
        };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.Add(input);
        row.Controls.Add(add);
        var flow = Flow(p);
        flow.Controls.Add(list);
        flow.Controls.Add(row);
        flow.Controls.Add(remove);
    }

    private void Radios(TabPage p, string current, Action<string> set, params (string key, string text)[] items)
    {
        foreach (var (key, text) in items)
        {
            var rb = new RadioButton { Text = text, AutoSize = true, Checked = current == key, Margin = new Padding(12, 2, 3, 2), Tag = key };
            rb.CheckedChanged += (o, _) => { if (((RadioButton)o!).Checked) { set((string)((RadioButton)o!).Tag); Save(); } };
            Flow(p).Controls.Add(rb);
        }
    }

    private void BuildAppLanguages(TabPage p)
    {
        AddLabel(p, "\nМова для програм (вмикається, щойно переходите в програму):");
        foreach (var kv in _s.AppLanguageMap) _appLangs.Items.Add($"{kv.Key} — {(kv.Value == "UA" ? "українська" : "англійська")}");
        _appLangs.Size = new Size(420, 90);
        _appLangs.SelectionMode = SelectionMode.MultiExtended;
        var app = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 200 };
        foreach (var name in _appInput.Items) app.Items.Add(name);
        var lang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        lang.Items.AddRange(new object[] { "українська", "англійська" });
        lang.SelectedIndex = 0;
        var add = new Button { Text = "Додати", Size = new Size(76, 30) };
        var remove = new Button { Text = "Видалити вибране", Size = new Size(160, 30) };
        add.Click += (_, _) =>
        {
            var v = app.Text.Trim();
            if (v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 4);
            if (v.Length == 0 || v.Contains('=') || v.Contains(';')) return;
            foreach (var old in _appLangs.Items.Cast<string>().Where(x => x.StartsWith(v + " — ", StringComparison.OrdinalIgnoreCase)).ToList())
                _appLangs.Items.Remove(old);
            _appLangs.Items.Add($"{v} — {lang.Text}");
            app.Text = "";
            Save();
        };
        remove.Click += (_, _) =>
        {
            foreach (var item in _appLangs.SelectedItems.Cast<object>().ToList()) _appLangs.Items.Remove(item);
            Save();
        };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.Add(app); row.Controls.Add(lang); row.Controls.Add(add);
        var flow = Flow(p);
        flow.Controls.Add(_appLangs);
        flow.Controls.Add(row);
        flow.Controls.Add(remove);
        AddLabel(p, "Те саме — у меню іконки: «Мова для …».");
    }

    private void ExportSettings()
    {
        using var d = new SaveFileDialog { Filter = "Налаштування UaSwitch|*.uaswitch|Усі файли|*.*", FileName = "UaSwitch-налаштування.uaswitch" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SaveAll();
            Settings.Export(d.FileName);
            MessageBox.Show(this, "Готово. Скопіюйте цей файл на інший комп\'ютер і там натисніть «Імпортувати…».", "UaSwitch");
        }
        catch (Exception e) { MessageBox.Show(this, "Не вдалося зберегти: " + e.Message, "UaSwitch"); }
    }

    private void ImportSettings()
    {
        using var d = new OpenFileDialog { Filter = "Налаштування UaSwitch|*.uaswitch|Усі файли|*.*" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, "Поточні налаштування, слова-винятки й автозаміна будуть замінені тими, що у файлі. UaSwitch перезапуститься.\n\nПродовжити?",
                "UaSwitch — імпорт", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            Settings.Import(d.FileName);
            Log.Write("Налаштування імпортовано — перезапуск");
            RestartRequested?.Invoke();
        }
        catch (Exception e) { MessageBox.Show(this, "Не вдалося імпортувати: " + e.Message, "UaSwitch"); }
    }

    private static IEnumerable<string> RunningApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.MainWindowHandle != IntPtr.Zero) names.Add(p.ProcessName); }
            catch { }
            finally { p.Dispose(); }
        }
        return names;
    }

    private static bool SafeAutostart() { try { return Settings.Autostart; } catch { return false; } }

    private static void OpenNotepad(string path)
    {
        try { Process.Start("notepad.exe", $"\"{path}\""); } catch { }
    }
}
