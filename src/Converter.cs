using System.Runtime.InteropServices;

namespace UaSwitch;

/// <summary>Виконує конвертацію останнього слова (вручну та автоматично) і виділеного тексту.</summary>
internal sealed class Converter
{
    private const ushort VK_BACK = 0x08, VK_CONTROL = 0x11, VK_CAPITAL = 0x14, VK_C = 0x43, VK_V = 0x56;
    private readonly Layouts _layouts;
    private readonly KeyboardMonitor _monitor;
    private readonly AutoDetector _detector;
    private readonly Exceptions _exceptions;
    private readonly Settings _settings;
    private readonly Dictionary<uint, string> _procNames = new();
    private bool _busy;

    /// <summary>Слово виправлено (для звуку та підсвічування іконки).</summary>
    public event Action? Corrected;

    public Converter(Layouts layouts, KeyboardMonitor monitor, AutoDetector detector, Exceptions exceptions, Settings settings)
    {
        _layouts = layouts;
        _monitor = monitor;
        _detector = detector;
        _exceptions = exceptions;
        _settings = settings;
    }

    // ---------- Останнє слово: вручну ----------

    public void ConvertLastWord()
    {
        if (_busy || !_layouts.Ready) return;
        var fg = Native.GetForegroundWindow();
        lock (_monitor.Sync)
        {
            var w = _monitor.Word;
            if (w.Keys.Count == 0 || w.Hwnd != fg) return;
            if (w.AutoConverted) { RevertAuto(w, fg); return; }
            Toggle(w, fg, withPrev: false, switchLayout: true);
        }
        Corrected?.Invoke();
    }

    /// <summary>Backspace одразу після автовиправлення.</summary>
    public void UndoAuto()
    {
        var fg = Native.GetForegroundWindow();
        lock (_monitor.Sync)
        {
            var w = _monitor.Word;
            if (w.Hwnd != fg || w.Keys.Count == 0) return;
            if (w.CommaInserted) RevertComma(w);
            else if (w.AutoConverted) RevertAuto(w, fg);
        }
    }

    private void RevertAuto(WordBuffer w, IntPtr fg)
    {
        ApplyCaseFix(w.Keys, w.AutoCase); // та сама операція повертає регістр назад
        Toggle(w, fg, w.PrevConverted, w.AutoLayout);
        if (w.AutoOriginal != null)
        {
            _exceptions.Add(w.AutoOriginal); // запам'ятовуємо: це слово більше не виправляти
            Log.Write("Скасовано автовиправлення, слово додано у винятки");
        }
        w.AutoConverted = w.PrevConverted = w.AutoLayout = false;
        w.AutoCase = CaseFix.None;
        w.ArmedVersion = -1;
        w.NoAuto = true; // це слово більше не чіпаємо (ні за префіксом, ні після пробілу)
    }

    // ---------- Останнє слово: автоматично ----------

    public void AutoCheck(int version)
    {
        string reason = AutoCheckCore(version);
        if (reason.Length > 0) Log.Write("Авто-перевірка: " + reason);
    }

    /// <summary>Повертає причину для журналу (лише технічну, без набраного тексту) або порожній рядок.</summary>
    private string AutoCheckCore(int version)
    {
        if (_busy) return "зайнято";
        if (!_layouts.Ready) return "розкладки не знайдено";
        if (!_detector.Ready) return "словники ще не завантажені";
        if (!_settings.AutoMode) return "";
        var fg = Native.GetForegroundWindow();
        if (IsExcludedApp(fg, out _)) return "";

        bool corrected;
        lock (_monitor.Sync)
        {
            var w = _monitor.Word;
            if (w.Version != version || w.Keys.Count == 0 || w.Trailing.Count != 1 || w.NoAuto || w.Hwnd != fg)
                return "";
            if (w.AutoConverted) // слово вже виправлено за першими буквами — лишається тільки кома
            {
                corrected = TryComma(w);
                goto done;
            }

            var cur = w.Keys[0].Shown;
            if (w.Keys.Any(k => k.Shown != cur || k.Mods.HasFlag(KeyMods.AltGr))) return "";
            bool curIsUa = _layouts.IsUa(cur);
            if (!curIsUa && Native.LangId(cur) != Native.LangId(_layouts.En))
                return $"розкладка 0x{(long)cur:X} не UA/EN";
            var alt = _layouts.Other(cur);

            string? curText = TextOf(w.Keys, cur), altText = TextOf(w.Keys, alt);
            if (curText == null || altText == null) return "не вдалося прочитати символи";
            if (_exceptions.Blocks(curText)) return "";

            // Регістр: випадковий Caps Lock (пРИВІТ) або дві великі на початку (ПРивіт)
            var fix = CaseFix.None;
            if (_settings.FixCaps && _monitor.CapsOn && (IsCapsPattern(curText) || IsCapsPattern(altText)))
                fix = CaseFix.Caps;
            else if (_settings.FixTwoCaps && (IsTwoCapsPattern(curText) || IsTwoCapsPattern(altText)))
                fix = CaseFix.TwoCaps;

            string curFixed = curText, altFixed = altText;
            if (fix != CaseFix.None)
            {
                var tmp = w.Keys.Select(k => new KeyStroke { Vk = k.Vk, Scan = k.Scan, Mods = k.Mods, Shown = k.Shown }).ToList();
                ApplyCaseFix(tmp, fix);
                curFixed = TextOf(tmp, cur) ?? curText;
                altFixed = TextOf(tmp, alt) ?? altText;
            }

            bool layout = _detector.ShouldConvert(curFixed, altFixed, curIsUa);
            if (!layout && fix == CaseFix.TwoCaps && !_detector.IsKnownWord(curFixed, curIsUa)) fix = CaseFix.None;
            if (!layout && fix == CaseFix.None)
            {
                corrected = TryComma(w);
                goto done;
            }

            bool withPrev = false;
            if (layout && w.Prev != null && w.Prev.Shown == cur)
            {
                string? pc = TextOf(new[] { w.Prev }, cur), pa = TextOf(new[] { w.Prev }, alt);
                withPrev = pc != null && pa != null && !_exceptions.Contains(pc)
                           && AutoDetector.SingleLetterWord(pa, !curIsUa) && !AutoDetector.SingleLetterWord(pc, curIsUa);
            }

            w.AutoOriginal = curText.ToLowerInvariant();
            ApplyCaseFix(w.Keys, fix);
            Toggle(w, fg, withPrev, layout);
            if (fix == CaseFix.Caps) TurnCapsOff();
            w.AutoConverted = true;
            w.PrevConverted = withPrev;
            w.AutoLayout = layout;
            w.AutoCase = fix;
            w.ArmedVersion = w.Version;
            corrected = true;
        }
    done:
        if (corrected) Corrected?.Invoke();
        return "";
    }

    // ---------- Перемикання за першими буквами ----------

    public void EarlyCheck(int version)
    {
        if (_busy || !_layouts.Ready || !_detector.Ready || !_settings.AutoMode || !_settings.EarlySwitch) return;
        var fg = Native.GetForegroundWindow();
        if (IsExcludedApp(fg, out _)) return;

        lock (_monitor.Sync)
        {
            var w = _monitor.Word;
            if (w.Version != version || w.Keys.Count < 4 || w.Trailing.Count != 0 || w.AutoConverted || w.NoAuto || w.Hwnd != fg)
                return;
            var cur = w.Keys[0].Shown;
            if (w.Keys.Any(k => k.Shown != cur || k.Mods.HasFlag(KeyMods.AltGr))) return;
            bool curIsUa = _layouts.IsUa(cur);
            if (!curIsUa && Native.LangId(cur) != Native.LangId(_layouts.En)) return;
            var alt = _layouts.Other(cur);

            string? curText = TextOf(w.Keys, cur), altText = TextOf(w.Keys, alt);
            if (curText == null || altText == null || _exceptions.Blocks(curText)) return;
            if (!_detector.ShouldSwitchEarly(curText, altText, curIsUa)) return;

            bool withPrev = false;
            if (w.Prev != null && w.Prev.Shown == cur)
            {
                string? pc = TextOf(new[] { w.Prev }, cur), pa = TextOf(new[] { w.Prev }, alt);
                withPrev = pc != null && pa != null && !_exceptions.Contains(pc)
                           && AutoDetector.SingleLetterWord(pa, !curIsUa) && !AutoDetector.SingleLetterWord(pc, curIsUa);
            }

            w.AutoOriginal = curText.ToLowerInvariant();
            Toggle(w, fg, withPrev, switchLayout: true);
            w.AutoConverted = true;
            w.PrevConverted = withPrev;
            w.AutoLayout = true;
            w.AutoCase = CaseFix.None;
            w.ArmedVersion = w.Version;
        }
        Corrected?.Invoke();
    }

    // ---------- Кома перед сполучником ----------

    /// <summary>«знаю що» → «знаю, що». Викликається під замком, коли щойно набрано пробіл після слова.</summary>
    private bool TryComma(WordBuffer w)
    {
        if (!_settings.AutoComma || w.CommaInserted) return false;
        var shown = w.Keys[0].Shown;
        if (!_layouts.IsUa(shown) || w.Keys.Any(k => k.Shown != shown)) return false;
        string? word = TextOf(w.Keys, shown);
        if (word == null || !CommaRules.ShouldInsert(w.PrevText, word)) return false;

        // Стираємо « слово » разом із пробілом перед ним і набираємо «, слово »
        var inputs = new List<Native.INPUT>();
        int erase = word.Length + w.Trailing.Count + 1;
        for (int i = 0; i < erase; i++) AddKey(inputs, VK_BACK);
        AddText(inputs, ", " + word + new string(w.Trailing.ToArray()));
        Send(inputs);
        w.CommaInserted = true;
        w.ArmedVersion = w.Version;
        return true;
    }

    private void RevertComma(WordBuffer w)
    {
        string? word = TextOf(w.Keys, w.Keys[0].Shown);
        if (word == null) return;
        var inputs = new List<Native.INPUT>();
        int erase = word.Length + w.Trailing.Count + 2;
        for (int i = 0; i < erase; i++) AddKey(inputs, VK_BACK);
        AddText(inputs, " " + word + new string(w.Trailing.ToArray()));
        Send(inputs);
        w.CommaInserted = false;
        w.ArmedVersion = -1;
        w.NoAuto = true;
    }

    // ---------- Регістр ----------

    /// <summary>пРИВІТ: перша літера мала, решта великі (наслідок увімкненого Caps Lock).</summary>
    private static bool IsCapsPattern(string s)
    {
        var letters = s.Where(char.IsLetter).ToList();
        return letters.Count >= 2 && char.IsLower(letters[0]) && letters.Skip(1).All(char.IsUpper);
    }

    /// <summary>ПРивіт: дві перші великі, далі щонайменше дві малі.</summary>
    private static bool IsTwoCapsPattern(string s)
    {
        if (s.Length < 4 || !char.IsLetter(s[0]) || !char.IsLetter(s[1])) return false;
        var rest = s.Skip(2).Where(char.IsLetter).ToList();
        return char.IsUpper(s[0]) && char.IsUpper(s[1]) && rest.Count >= 2 && rest.All(char.IsLower);
    }

    /// <summary>Змінює модифікатори натискань так, щоб вийшов правильний регістр. Повторний виклик скасовує зміну.</summary>
    private static void ApplyCaseFix(IList<KeyStroke> keys, CaseFix fix)
    {
        if (fix == CaseFix.Caps) foreach (var k in keys) k.Mods ^= KeyMods.Caps;
        else if (fix == CaseFix.TwoCaps && keys.Count > 1) keys[1].Mods ^= KeyMods.Shift;
    }

    private void TurnCapsOff()
    {
        var inputs = new List<Native.INPUT>();
        AddKey(inputs, VK_CAPITAL);
        Send(inputs);
        _monitor.SetCaps(false);
    }

    private bool IsExcludedApp(IntPtr fg, out string name)
    {
        name = "?";
        try
        {
            Native.GetWindowThreadProcessId(fg, out uint pid);
            if (!_procNames.TryGetValue(pid, out name))
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                name = p.ProcessName;
                if (_procNames.Count > 200) _procNames.Clear();
                _procNames[pid] = name;
            }
            return _settings.IsExcluded(name);
        }
        catch { return false; }
    }

    private string? TextOf(IEnumerable<KeyStroke> keys, IntPtr hkl)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var k in keys)
        {
            var c = _layouts.CharFor(k.Vk, k.Scan, k.Mods, hkl);
            if (c == null) return null;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Перемикає слово (і за потреби попереднє однобуквене) в іншу розкладку на екрані.</summary>
    private void Toggle(WordBuffer w, IntPtr fg, bool withPrev, bool switchLayout)
    {
        var shownNow = w.Keys[w.Keys.Count - 1].Shown;
        var target = switchLayout ? _layouts.Other(shownNow) : shownNow;
        int erase = w.Trailing.Count;
        var text = new System.Text.StringBuilder();

        var all = new List<KeyStroke>();
        if (withPrev && w.Prev != null) all.Add(w.Prev);
        all.AddRange(w.Keys);
        for (int i = 0; i < all.Count; i++)
        {
            var k = all[i];
            var shown = _layouts.CharFor(k.Vk, k.Scan, k.Mods, k.Shown) ?? "";
            erase += shown.Length;
            text.Append(_layouts.CharFor(k.Vk, k.Scan, k.Mods, target) ?? shown);
            if (withPrev && i == 0) { text.Append(' '); erase++; }
        }
        text.Append(w.Trailing.ToArray());

        var inputs = new List<Native.INPUT>();
        for (int i = 0; i < erase; i++) AddKey(inputs, VK_BACK);
        AddText(inputs, text.ToString());
        Send(inputs);

        foreach (var k in all) k.Shown = target; // повторне перемикання поверне назад
        if (switchLayout) SwitchLayout(fg, target);
    }

    // ---------- Виділений текст ----------

    public async void ConvertSelection()
    {
        if (_busy || !_layouts.Ready) return;
        _busy = true;
        try
        {
            await WaitModifiersReleased();
            var fg = Native.GetForegroundWindow();
            var backup = ClipboardBackup.Save();

            uint seq = Native.GetClipboardSequenceNumber();
            SendChord(VK_CONTROL, VK_C);
            if (!await WaitClipboardChange(seq, 700)) { backup.Restore(); return; }

            string? text = TryGetText();
            // Деякі редактори (VS Code тощо) без виділення копіюють увесь рядок — такий випадок пропускаємо
            bool wholeLineCopy = text != null && text.EndsWith("\n") && text.IndexOf('\n') == text.Length - 1;
            if (text == null || text.Length == 0 || wholeLineCopy) { backup.Restore(); return; }

            string converted = _layouts.ConvertText(text, out bool toUa);
            if (converted != text)
            {
                seq = Native.GetClipboardSequenceNumber();
                TrySetText(converted);
                await WaitClipboardChange(seq, 300);
                SendChord(VK_CONTROL, VK_V);
                await Task.Delay(250); // програма має встигнути прочитати буфер
                SwitchLayout(fg, toUa ? _layouts.Ua : _layouts.En);
            }
            backup.Restore();
            _monitor.ResetWord();
        }
        catch { /* не заважаємо роботі користувача */ }
        finally { _busy = false; }
    }

    // ---------- Допоміжне ----------

    public static void SwitchLayout(IntPtr fg, IntPtr hkl)
    {
        if (hkl == IntPtr.Zero) return;
        Native.PostMessage(Native.FocusWindow(fg), Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
    }

    private static async Task WaitModifiersReleased()
    {
        int[] mods = { 0x10, 0x11, 0x12, 0x5B, 0x5C };
        for (int i = 0; i < 100; i++)
        {
            if (mods.All(v => (Native.GetAsyncKeyState(v) & 0x8000) == 0)) return;
            await Task.Delay(15);
        }
    }

    private static async Task<bool> WaitClipboardChange(uint seq, int timeoutMs)
    {
        for (int t = 0; t < timeoutMs; t += 20)
        {
            if (Native.GetClipboardSequenceNumber() != seq) { await Task.Delay(30); return true; }
            await Task.Delay(20);
        }
        return false;
    }

    private static string? TryGetText()
    {
        for (int i = 0; i < 5; i++)
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (ExternalException) { Thread.Sleep(40); }
        }
        return null;
    }

    private static void TrySetText(string s)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                var d = new DataObject();
                d.SetData(DataFormats.UnicodeText, s);
                // Не засмічувати журнал буфера обміну Windows (Win+V) проміжним текстом
                d.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
                Clipboard.SetDataObject(d, true);
                return;
            }
            catch (ExternalException) { Thread.Sleep(40); }
        }
    }

    private static void SendChord(ushort mod, ushort key)
    {
        var inputs = new List<Native.INPUT>();
        inputs.Add(Key(mod, false));
        inputs.Add(Key(key, false));
        inputs.Add(Key(key, true));
        inputs.Add(Key(mod, true));
        Send(inputs);
    }

    private static void AddKey(List<Native.INPUT> list, ushort vk)
    {
        list.Add(Key(vk, false));
        list.Add(Key(vk, true));
    }

    private static void AddText(List<Native.INPUT> list, string text)
    {
        foreach (char c in text)
        {
            list.Add(Unicode(c, false));
            list.Add(Unicode(c, true));
        }
    }

    private static Native.INPUT Key(ushort vk, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion { ki = new Native.KEYBDINPUT
        {
            wVk = vk, dwFlags = up ? Native.KEYEVENTF_KEYUP : 0, dwExtraInfo = Native.OwnInputMarker
        } }
    };

    private static Native.INPUT Unicode(char c, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion { ki = new Native.KEYBDINPUT
        {
            wScan = c, dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0), dwExtraInfo = Native.OwnInputMarker
        } }
    };

    private static void Send(List<Native.INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        Native.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Native.INPUT>());
    }
}

/// <summary>Зберігає та відновлює вміст буфера обміну (основні формати).</summary>
internal sealed class ClipboardBackup
{
    private static readonly string[] Formats =
    {
        DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf, DataFormats.Html,
        DataFormats.Bitmap, DataFormats.FileDrop, DataFormats.CommaSeparatedValue
    };
    private readonly DataObject? _data;

    private ClipboardBackup(DataObject? data) => _data = data;

    public static ClipboardBackup Save()
    {
        try
        {
            var src = Clipboard.GetDataObject();
            if (src == null) return new ClipboardBackup(null);
            var copy = new DataObject();
            bool any = false;
            foreach (var f in Formats)
            {
                try
                {
                    if (!src.GetDataPresent(f, false)) continue;
                    var d = src.GetData(f, false);
                    if (d != null) { copy.SetData(f, d); any = true; }
                }
                catch { }
            }
            return new ClipboardBackup(any ? copy : null);
        }
        catch { return new ClipboardBackup(null); }
    }

    public void Restore()
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (_data != null) Clipboard.SetDataObject(_data, true);
                else Clipboard.Clear();
                return;
            }
            catch (ExternalException) { Thread.Sleep(40); }
        }
    }
}
