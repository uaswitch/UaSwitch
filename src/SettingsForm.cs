using System.Diagnostics;
using System.Drawing;

namespace UaSwitch;

/// <summary>Вікно налаштувань: загальні, гарячі клавіші, програми-винятки, слова-винятки.</summary>
internal sealed class SettingsForm : Form
{
    private readonly Settings _s;
    private readonly Exceptions _ex;
    private readonly CheckBox _enabled, _auto, _early, _comma, _space, _caps, _twoCaps, _sound, _flash, _autostart, _pause, _dShift, _dCtrl;
    private readonly ListBox _apps = new(), _words = new();
    private readonly ComboBox _appInput = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _wordInput = new();

    public SettingsForm(Settings settings, Exceptions exceptions)
    {
        _s = settings;
        _ex = exceptions;

        string version = Application.ProductVersion.Split('+')[0];
        Text = $"UaSwitch {version} — налаштування";
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 570);
        ShowInTaskbar = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var tabs = new TabControl { Dock = DockStyle.Top, Height = 520 };

        // ---- Загальне ----
        var general = Page("Загальне");
        _enabled = Check(general, "Програма увімкнена", _s.Enabled);
        _auto = Check(general, "Автоматично виправляти слова після пробілу", _s.AutoMode);
        _early = Check(general, "Виправляти слово вже на 4-й літері, не чекаючи пробілу", _s.EarlySwitch);
        _comma = Check(general, "Ставити кому перед «що», «щоб», «але», «який»…", _s.AutoComma);
        _space = Check(general, "Пробіл після коми  (Привіт,як → Привіт, як)", _s.SpaceAfterPunct);
        _caps = Check(general, "Виправляти випадковий Caps Lock  (пРИВІТ → Привіт)", _s.FixCaps);
        _twoCaps = Check(general, "Виправляти дві великі на початку  (ПРивіт → Привіт)", _s.FixTwoCaps);
        _sound = Check(general, "Тихий звук при виправленні", _s.Sound);
        _flash = Check(general, "Підсвічувати іконку при виправленні", _s.Flash);
        _autostart = Check(general, "Запускати разом з Windows", SafeAutostart());
        AddLink(general, "Відкрити журнал роботи", () => OpenNotepad(Log.FilePath));
        AddLabel(general, "\nПорада: Backspace одразу після автовиправлення скасовує його\nі додає слово у винятки.");

        // ---- Гарячі клавіші ----
        var keys = Page("Гарячі клавіші");
        _pause = Check(keys, "Pause — останнє слово,  Shift+Pause — виділений текст", _s.PauseHotkeys);
        _dShift = Check(keys, "Двічі Shift — виправити останнє слово", _s.DoubleShift);
        _dCtrl = Check(keys, "Двічі Ctrl — виправити виділений текст", _s.DoubleCtrl);
        AddLabel(keys, "\nПовторне натискання повертає слово як було.\n" +
                       "Подвійне натискання — два короткі натискання поспіль,\nбез інших клавіш між ними.");

        // ---- Програми ----
        var apps = Page("Програми");
        AddLabel(apps, "Автоматичне виправлення вимкнено в цих програмах:");
        foreach (var a in _s.ExcludedAppList) _apps.Items.Add(a);
        foreach (var name in RunningApps()) _appInput.Items.Add(name);
        ListEditor(apps, _apps, _appInput, () => _appInput.Text);

        // ---- Слова-винятки ----
        var words = Page("Слова-винятки");
        AddLabel(words, "Ці слова не виправляються автоматично (як набрано):");
        foreach (var w in _ex.GetAll()) _words.Items.Add(w);
        ListEditor(words, _words, _wordInput, () => _wordInput.Text);

        tabs.TabPages.AddRange(new[] { general, keys, apps, words });
        Controls.Add(tabs);

        var ok = new Button { Text = "Зберегти", DialogResult = DialogResult.OK, Size = new Size(100, 30), Location = new Point(282, 530) };
        var cancel = new Button { Text = "Скасувати", DialogResult = DialogResult.Cancel, Size = new Size(100, 30), Location = new Point(390, 530) };
        // Вікно немодальне (Show), тому закриваємо його явно: DialogResult на кнопках тут не спрацьовує
        ok.Click += (_, _) => { SaveAll(); DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(ok);
        Controls.Add(cancel);
        var about = new LinkLabel
        {
            Text = $"Версія {version} · uaswitch.github.io",
            AutoSize = true, Location = new Point(12, 537), LinkColor = SystemColors.GrayText,
        };
        about.Links.Add(about.Text.IndexOf("uaswitch"), "uaswitch.github.io".Length, "https://uaswitch.github.io/UaSwitch/");
        about.LinkClicked += (_, e) => { try { Process.Start(new ProcessStartInfo((string)e.Link.LinkData) { UseShellExecute = true }); } catch { } };
        Controls.Add(about);
        AcceptButton = ok;
        CancelButton = cancel;
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
        _s.Sound = _sound.Checked;
        _s.Flash = _flash.Checked;
        _s.PauseHotkeys = _pause.Checked;
        _s.DoubleShift = _dShift.Checked;
        _s.DoubleCtrl = _dCtrl.Checked;
        _s.ExcludedApps = string.Join(",", _apps.Items.Cast<string>());
        _s.Save();
        _ex.SetAll(_words.Items.Cast<string>());
        try { Settings.Autostart = _autostart.Checked; } catch (Exception e) { Log.Write("Автозапуск: " + e.Message); }
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

    private static void ListEditor(TabPage p, ListBox list, Control input, Func<string> value)
    {
        list.Size = new Size(420, 220);
        list.SelectionMode = SelectionMode.MultiExtended;
        input.Width = 300;
        var add = new Button { Text = "Додати", AutoSize = true };
        var remove = new Button { Text = "Видалити вибране", AutoSize = true };
        add.Click += (_, _) =>
        {
            var v = value().Trim();
            if (v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 4);
            if (v.Length > 0 && !list.Items.Cast<string>().Any(x => x.Equals(v, StringComparison.OrdinalIgnoreCase)))
                list.Items.Add(v);
            input.Text = "";
        };
        remove.Click += (_, _) =>
        {
            foreach (var item in list.SelectedItems.Cast<object>().ToList()) list.Items.Remove(item);
        };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.Add(input);
        row.Controls.Add(add);
        var flow = Flow(p);
        flow.Controls.Add(list);
        flow.Controls.Add(row);
        flow.Controls.Add(remove);
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
