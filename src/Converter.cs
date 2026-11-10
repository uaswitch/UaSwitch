using System.Runtime.InteropServices;

namespace UaSwitch;

/// <summary>Виконує конвертацію останнього слова (вручну та автоматично) і виділеного тексту.</summary>
internal sealed class Converter
{
    private const ushort VK_SPACE = 0x20, VK_BACK = 0x08, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_CAPITAL = 0x14, VK_C = 0x43, VK_V = 0x56;
    private readonly Layouts _layouts;
    private readonly KeyboardMonitor _monitor;
    private readonly AutoDetector _detector;
    private readonly Exceptions _exceptions;
    private readonly Settings _settings;
    private readonly AutoReplace _replace;
    private readonly Dictionary<uint, string> _procNames = new();
    private bool _busy;

    /// <summary>Слово виправлено (для звуку та підсвічування іконки).</summary>
    public event Action? Corrected;

    public Converter(Layouts layouts, KeyboardMonitor monitor, AutoDetector detector, Exceptions exceptions, Settings settings, AutoReplace replace)
    {
        _replace = replace;
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
        var fg = Native.GetForegroundWindow();
        if (IsExcludedApp(fg, out _)) return "";

        bool corrected;
        lock (_monitor.Sync)
        {
            var w = _monitor.Word;
            if (w.Version != version || w.Keys.Count == 0 || w.Trailing.Count != 1 || w.NoAuto || w.Hwnd != fg)
                return "";
            if (_settings.AutoReplace && TryAutoReplace(w, sendEnter: false)) { corrected = true; goto done; }
            if (!_settings.AutoMode) return "";
            if (!_detector.Ready) return "словники ще не завантажені";
            if (w.AutoConverted) // слово вже виправлено за першими буквами — лишається велика літера чи кома
            {
                string? now = TextOf(w.Keys, w.Keys[0].Shown);
                if (w.AutoCase == CaseFix.None && now != null && WantsSentenceCap(w, now))
                {
                    ApplyCaseFix(w.Keys, CaseFix.Sentence);
                    Toggle(w, fg, withPrev: false, switchLayout: false);
                    w.AutoCase = CaseFix.Sentence;
                    w.ArmedVersion = w.Version;
                    corrected = true;
                }
                else corrected = TryComma(w);
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
            if (TrySpaceAfterPunct(w, curText, curIsUa)) { corrected = true; goto done; }
            if (HasInnerPunct(curText) && !altText.All(IsWordChar) || LooksLikeFileOrDomain(curText))
            {
                corrected = TryComma(w); // «Привіт,як» чи «file.txt» — розкладку не чіпаємо
                goto done;
            }

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

            var (ctxUa, ctxEn) = AutoDetector.Context(w.Recent);
            bool layout = _detector.ShouldConvert(curFixed, altFixed, curIsUa, ctxUa, ctxEn);
            if (!layout && fix == CaseFix.TwoCaps && !_detector.IsKnownWord(curFixed, curIsUa)) fix = CaseFix.None;
            if (fix == CaseFix.None && WantsSentenceCap(w, layout ? altText : curText)) fix = CaseFix.Sentence;
            if (!layout && fix == CaseFix.None)
            {
                corrected = curIsUa && TryApostrophe(w, curText) || TryComma(w);
                goto done;
            }

            bool withPrev = false;
            if (layout && w.Prev != null && w.Prev.Shown == cur)
            {
                string? pc = TextOf(new[] { w.Prev }, cur), pa = TextOf(new[] { w.Prev }, alt);
                withPrev = pc != null && pa != null && !_exceptions.Contains(pc)
                           && AutoDetector.SingleLetterWord(pa, !curIsUa) && !AutoDetector.SingleLetterWord(pc, curIsUa);
            }

            // Скасування лише великої літери не повинно додавати слово у винятки
            w.AutoOriginal = layout || fix != CaseFix.Sentence ? curText.ToLowerInvariant() : null;
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

    // ---------- Автозаміна ----------

    /// <summary>Enter після скорочення: хук затримав Enter — підставляємо текст і лише тоді натискаємо Enter.</summary>
    public void ReplaceOnEnter(int version)
    {
        bool done = false;
        try
        {
            var fg = Native.GetForegroundWindow();
            if (!_busy && _settings.AutoReplace && !IsExcludedApp(fg, out _))
                lock (_monitor.Sync)
                {
                    var w = _monitor.Word;
                    if (w.Version == version && w.Keys.Count > 0 && w.Trailing.Count == 0 && w.Hwnd == fg)
                        done = TryAutoReplace(w, sendEnter: true);
                }
        }
        catch { }
        if (!done) { var inputs = new List<Native.INPUT>(); AddKey(inputs, VK_RETURN); Send(inputs); }
        _monitor.ResetWord();
        if (done) Corrected?.Invoke();
    }

    /// <summary>Скорочення → текст. Скорочення, набране не в тій розкладці, теж спрацьовує. Під замком.</summary>
    private bool TryAutoReplace(WordBuffer w, bool sendEnter)
    {
        var shown = w.Keys[0].Shown;
        if (w.Keys.Any(k => k.Shown != shown)) return false;
        string? typed = TextOf(w.Keys, shown);
        if (typed == null) return false;
        if (!_replace.TryMatch(typed, out var repl, out var suffix))
        {
            string? alt = TextOf(w.Keys, _layouts.Other(shown));
            if (alt == null || alt.IndexOfAny(AutoReplace.TrailPunct) >= 0 || !_replace.TryMatch(alt, out repl, out suffix)) return false;
        }
        string trailing = new string(w.Trailing.ToArray());
        string newText = repl + suffix + trailing;
        var inputs = new List<Native.INPUT>();
        for (int i = 0; i < typed.Length + trailing.Length; i++) AddKey(inputs, VK_BACK);
        AddText(inputs, newText);
        if (sendEnter) AddKey(inputs, VK_RETURN);
        Send(inputs);

        w.UndoErase = AutoReplace.EraseCount(newText);
        w.UndoText = typed + trailing;
        w.ScreenText = (repl + suffix).Replace('\n', ' ');
        w.CommaInserted = true; // спільний механізм скасування через Backspace
        w.ArmedVersion = w.Version;
        return true;
    }

    // ---------- Caps Lock перемикає мову ----------

    public void ToggleLayout()
    {
        if (!_layouts.Ready) return;
        var hkl = Native.ForegroundLayout(out var fg);
        SwitchLayout(fg, _layouts.Other(hkl));
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
            if (_settings.AutoReplace && (_replace.IsPrefix(curText) || _replace.IsPrefix(altText))) return; // набирається скорочення
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

    /// <summary>Кома перед підрядним (див. CommaRules). Викликається під замком одразу після пробілу за словом.</summary>
    private bool TryComma(WordBuffer w)
    {
        if (!_settings.AutoComma || w.CommaInserted) return false;
        var shown = w.Keys[0].Shown;
        if (!_layouts.IsUa(shown) || w.Keys.Any(k => k.Shown != shown)) return false;
        string? word = TextOf(w.Keys, shown);
        if (word == null) return false;
        int where = CommaRules.Where(w.Recent, word);
        if (where < 0) return false;

        // Хвіст рядка, який переписуємо: « [попереднє] слово » → «, [попереднє] слово »
        string trailing = new string(w.Trailing.ToArray());
        string tail = (where == 1 ? w.Recent[w.Recent.Count - 1] + " " : "") + word + trailing;
        Replace(" " + tail, ", " + tail);
        w.UndoErase = tail.Length + 2;
        w.UndoText = " " + tail;
        int target = w.Recent.Count - 1 - where;          // слово, після якого стала кома
        if (target >= 0 && target < w.Recent.Count) { w.Recent[target] += ","; w.UndoCommaAt = target; }
        w.CommaInserted = true;
        w.ArmedVersion = w.Version;
        return true;
    }

    /// <summary>«Привіт,як» → «Привіт, як»: обидві частини мають бути словами зі словника.</summary>
    private bool TrySpaceAfterPunct(WordBuffer w, string text, bool isUa)
    {
        if (!_settings.SpaceAfterPunct) return false;
        int p = -1;
        for (int i = 1; i < text.Length - 1; i++)
            if (",;!?".IndexOf(text[i]) >= 0) { if (p >= 0) return false; p = i; }
        if (p < 0) return false;
        string left = text.Substring(0, p), right = text.Substring(p + 1);
        if (!left.All(IsWordChar) || !right.All(IsWordChar)) return false;
        if (!_detector.IsKnownWord(left, isUa) || !_detector.IsKnownWord(right, isUa)) return false;

        string trailing = new string(w.Trailing.ToArray());
        string fixedText = left + text[p] + " " + right + trailing;
        Replace(text + trailing, fixedText);
        w.UndoErase = fixedText.Length;
        w.UndoText = text + trailing;
        w.ScreenText = left + text[p] + " " + right;
        w.CommaInserted = true; // той самий механізм скасування через Backspace
        w.ArmedVersion = w.Version;
        return true;
    }

    private static bool IsWordChar(char c) => char.IsLetter(c) || c == '\'' || c == '’';

    /// <summary>Розділовий знак усередині слова між літерами: «Привіт,як», «file.txt».</summary>
    private static bool HasInnerPunct(string s)
    {
        for (int i = 1; i < s.Length - 1; i++)
            if (!IsWordChar(s[i]) && char.IsLetter(s[i - 1]) && char.IsLetter(s[i + 1]) && s[i] != '-') return true;
        return false;
    }

    /// <summary>«file.txt», «google.com», «e.g» — латиниця з крапкою, після якої 1–4 літери.</summary>
    private static bool LooksLikeFileOrDomain(string s)
    {
        int dot = s.LastIndexOf('.');
        if (dot <= 0 || dot >= s.Length - 1) return false;
        string ext = s.Substring(dot + 1);
        return ext.Length <= 4 && ext.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
               && char.IsLetter(s[dot - 1]) && s.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
    }

    private void RevertComma(WordBuffer w)
    {
        if (w.UndoText == null) return;
        var inputs = new List<Native.INPUT>();
        for (int i = 0; i < w.UndoErase; i++) AddKey(inputs, VK_BACK);
        AddText(inputs, w.UndoText);
        Send(inputs);
        if (w.UndoCommaAt >= 0 && w.UndoCommaAt < w.Recent.Count && w.Recent[w.UndoCommaAt].EndsWith(","))
            w.Recent[w.UndoCommaAt] = w.Recent[w.UndoCommaAt].Substring(0, w.Recent[w.UndoCommaAt].Length - 1);
        w.UndoCommaAt = -1;
        w.ScreenText = null;
        w.UndoText = null;
        w.CommaInserted = false;
        w.ArmedVersion = -1;
        w.NoAuto = true;
    }

    /// <summary>Стирає old (з кінця) і набирає new.</summary>
    private void Replace(string oldTail, string newTail)
    {
        var inputs = new List<Native.INPUT>();
        for (int i = 0; i < oldTail.Length; i++) AddKey(inputs, VK_BACK);
        AddText(inputs, newTail);
        Send(inputs);
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
        else if (fix == CaseFix.Sentence && keys.Count > 0) keys[0].Mods ^= KeyMods.Shift;
    }

    // ---------- Велика літера на початку речення ----------

    // Скорочення з крапкою, після яких речення не закінчується
    private static readonly HashSet<string> Abbr = new(StringComparer.OrdinalIgnoreCase)
    {
        "вул", "просп", "пров", "бул", "пл", "буд", "кв", "обл", "смт", "ім", "тис", "млн", "млрд", "грн", "коп",
        "див", "напр", "рис", "табл", "ст", "рр", "пп", "ін", "інш", "тел", "проф", "доц", "акад", "гр", "зам",
        "кг", "км", "см", "мм", "хв", "сек", "год", "розд", "абз", "арк", "вип", "стор", "прим", "порівн",
        "etc", "vs", "mr", "mrs", "ms", "dr", "st", "no", "jr", "sr", "inc", "ltd", "approx", "dept", "fig", "vol",
    };

    /// <summary>Чи закінчилося речення на попередньому слові: «справи.», «Так!», «чому?»</summary>
    private static bool SentenceEnded(IReadOnlyList<string> recent)
    {
        if (recent.Count == 0) return false;
        string p = recent[recent.Count - 1].TrimEnd('»', '"', ')', '\'');
        if (p.Length < 3 || ".!?".IndexOf(p[p.Length - 1]) < 0) return false;
        if (p.EndsWith("..") || p.EndsWith("…")) return false; // три крапки — часто посеред думки
        string core = p.TrimEnd('.', '!', '?').TrimStart('«', '"', '(');
        if (core.Length < 2 || core.Any(char.IsDigit) || core.IndexOf('.') >= 0) return false; // ініціали, «т.д.», «5.»
        if (!core.All(c => char.IsLetter(c) || c == '\'' || c == '’' || c == '-')) return false;
        return p[p.Length - 1] != '.' || !Abbr.Contains(core);
    }

    private bool WantsSentenceCap(WordBuffer w, string finalText)
    {
        if (!_settings.SentenceCaps || _monitor.CapsOn || finalText.Length == 0 || HasOwnAutocorrect()) return false; // Office робить це сам
        if (!char.IsLower(finalText[0])) return false;
        if (!finalText.TrimEnd(".,!?;:\")»".ToCharArray()).All(c => char.IsLetter(c) || c == '\'' || c == '’' || c == '-')) return false;
        return (w.Keys[0].Mods & KeyMods.AltGr) == 0 && SentenceEnded(w.Recent);
    }

    // Програми з власною автозаміною: там велику літеру на початку речення ставить сама програма
    private static readonly string[] OfficeApps = { "WINWORD", "OUTLOOK", "POWERPNT", "ONENOTE", "MSPUB", "EXCEL", "MSACCESS", "soffice.bin", "soffice" };
    private static uint _officePid;
    private static bool _officeIs;

    private static bool HasOwnAutocorrect()
    {
        try
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
            if (pid != _officePid)
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                _officeIs = OfficeApps.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase);
                _officePid = pid;
            }
            return _officeIs;
        }
        catch { return false; }
    }

    // ---------- Апостроф ----------

    private char ApostropheChar()
    {
        var ch = _layouts.CharFor(0xC0, 0x29, KeyMods.None, _layouts.Ua);
        return ch is "'" or "’" or "ʼ" ? ch[0] : '\'';
    }

    /// <summary>Запасний шлях (якщо хук не затримав пробіл): «мясо » → «м'ясо ».</summary>
    private bool TryApostrophe(WordBuffer w, string text)
    {
        if (!_settings.FixApostrophe) return false;
        string core = text.TrimEnd(".,!?;:\")»".ToCharArray());
        string tail = text.Substring(core.Length);
        string? fixedCore = _detector.FixApostrophe(core, ApostropheChar());
        if (fixedCore == null) return false;

        string trailing = new string(w.Trailing.ToArray());
        Replace(text + trailing, fixedCore + tail + trailing);
        w.UndoErase = fixedCore.Length + tail.Length + trailing.Length;
        w.UndoText = text + trailing;
        w.ScreenText = fixedCore + tail;
        w.CommaInserted = true; // скасування — той самий Backspace
        w.ArmedVersion = w.Version;
        return true;
    }

    /// <summary>
    /// Хук затримав пробіл після «мясо»: ставимо апостроф і лише тоді друкуємо пробіл.
    /// Так Word/Excel не встигають зробити власну автозаміну, і кількість літер для стирання завжди точна.
    /// </summary>
    public void ApostropheOnSpace(int version)
    {
        bool done = false;
        try
        {
            var fg = Native.GetForegroundWindow();
            if (!_busy && _settings.FixApostrophe && _settings.AutoMode && !IsExcludedApp(fg, out _))
                lock (_monitor.Sync)
                {
                    var w = _monitor.Word;
                    if (w.Version == version && w.Keys.Count > 0 && w.Trailing.Count == 0 && w.Hwnd == fg)
                    {
                        string? text = TextOf(w.Keys, w.Keys[0].Shown);
                        string core = text?.TrimEnd(".,!?;:\")»".ToCharArray()) ?? "";
                        string? fixedCore = text == null ? null : _detector.FixApostrophe(core, ApostropheChar());
                        if (text != null && fixedCore != null)
                        {
                            string tail = text.Substring(core.Length);
                            var inputs = new List<Native.INPUT>();
                            for (int i = 0; i < text.Length; i++) AddKey(inputs, VK_BACK);
                            AddText(inputs, fixedCore + tail + " ");
                            Send(inputs, guard: false); // пробілу ще не було — автозаміна Word не встигла спрацювати

                            w.Trailing.Add(' ');
                            w.Version++;
                            w.UndoErase = fixedCore.Length + tail.Length + 1;
                            w.UndoText = text + " ";
                            w.ScreenText = fixedCore + tail;
                            w.CommaInserted = true; // Backspace одразу після — повернути як було
                            w.ArmedVersion = w.Version;
                            done = true;
                        }
                    }
                }
        }
        catch (Exception e) { Log.Write("Апостроф: " + e.Message); }
        if (done) { Corrected?.Invoke(); return; }

        // Апостроф не знадобився — друкуємо затриманий пробіл і робимо звичайну перевірку
        var space = new List<Native.INPUT>();
        AddKey(space, VK_SPACE);
        Send(space);
        lock (_monitor.Sync)
        {
            var w = _monitor.Word;
            if (w.Version == version && w.Keys.Count > 0) { w.Trailing.Add(' '); w.Version++; }
            version = w.Version;
        }
        AutoCheck(version);
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
        if (withPrev && w.Prev != null && w.Recent.Count > 0)
            w.Recent[w.Recent.Count - 1] = _layouts.CharFor(w.Prev.Vk, w.Prev.Scan, w.Prev.Mods, target) ?? w.Recent[w.Recent.Count - 1];
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
            if (c == '\r') continue;
            if (c == '\n')
            {
                list.Add(Key(VK_SHIFT, false));
                AddKey(list, VK_RETURN);
                list.Add(Key(VK_SHIFT, true));
                continue;
            }
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

    private static void Send(List<Native.INPUT> inputs, bool guard = true)
    {
        if (inputs.Count == 0) return;
        // Word, Excel, Google Docs: перший Backspace одразу після їхньої автозаміни (велика літера на початку речення)
        // не стирає символ, а скасовує автозаміну — і одна літера лишається («Мм'ясо»).
        // Тому спершу друкуємо пробіл і стираємо його: після цього всі Backspace стирають звичайно.
        if (guard && inputs[0].U.ki.wVk == VK_BACK && (inputs[0].U.ki.dwFlags & Native.KEYEVENTF_KEYUP) == 0)
            inputs.InsertRange(0, new[] { Unicode(' ', false), Unicode(' ', true), Key(VK_BACK, false), Key(VK_BACK, true) });
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
