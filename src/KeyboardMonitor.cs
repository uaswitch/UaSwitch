using System.Runtime.InteropServices;

namespace UaSwitch;

internal sealed class KeyStroke
{
    public uint Vk, Scan;
    public KeyMods Mods;
    /// <summary>Розкладка, у якій символ зараз відображається на екрані.</summary>
    public IntPtr Shown;
}

internal enum CaseFix { None, Caps, TwoCaps }

/// <summary>Останнє набране слово і пробіли після нього.</summary>
internal sealed class WordBuffer
{
    public readonly List<KeyStroke> Keys = new();
    public readonly List<char> Trailing = new();
    public IntPtr Hwnd;
    /// <summary>Однобуквене слово перед поточним (через один пробіл) — для «z ljvf» → «я вдома».</summary>
    public KeyStroke? Prev;
    /// <summary>Лічильник змін — автоперевірка відкидається, якщо користувач уже набирає далі.</summary>
    public int Version;
    /// <summary>Слово щойно виправлено автоматично; Backspace у цей момент скасовує виправлення.</summary>
    public bool AutoConverted;
    public bool PrevConverted;
    public bool AutoLayout;
    public CaseFix AutoCase;
    public string? AutoOriginal;
    /// <summary>Попередні слова рядка (як на екрані, кожне через один пробіл) — для ком і контексту мови.</summary>
    public readonly List<string> Recent = new();
    /// <summary>Як скасувати вставку коми чи пробілу: скільки символів стерти і що набрати замість них.</summary>
    public int UndoErase;
    public string? UndoText;
    public int UndoCommaAt = -1;
    /// <summary>Як слово виглядає на екрані після вставки пробілу («Привіт, як»), якщо відрізняється від набраного.</summary>
    public string? ScreenText;
    /// <summary>Автовиправлення цього слова скасовано — більше його не чіпати.</summary>
    public bool NoAuto;
    public bool CommaInserted;
    public int ArmedVersion = -1;

    public void Clear()
    {
        Keys.Clear(); Trailing.Clear(); Prev = null; Recent.Clear(); UndoErase = 0; UndoText = null; UndoCommaAt = -1; ScreenText = null; NoAuto = CommaInserted = false;
        AutoConverted = PrevConverted = AutoLayout = false; AutoCase = CaseFix.None; ArmedVersion = -1;
        Version++;
    }
}

/// <summary>
/// Глобальні хуки клавіатури та миші. Працюють в окремому потоці з власним циклом повідомлень,
/// щоб конвертація (яка чекає буфер обміну) ніколи не гальмувала введення в системі.
/// </summary>
internal sealed class KeyboardMonitor : IDisposable
{
    private const uint VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_PAUSE = 0x13, VK_CAPITAL = 0x14, VK_SPACE = 0x20;
    private const int TapMaxHoldMs = 350, DoubleTapMs = 450, MaxWord = 64;

    public readonly WordBuffer Word = new();
    public readonly object Sync = new();

    public volatile bool Enabled = true;
    public volatile bool PauseHotkeys = true;
    public volatile bool DoubleShift = true;
    public volatile bool DoubleCtrl = true;

    public volatile bool AutoMode = true;

    public event Action? LastWordRequested;
    public event Action? SelectionRequested;
    public event Action<int>? AutoCheckRequested;
    public event Action<int>? EarlyCheckRequested;
    public event Action? UndoRequested;

    private readonly Layouts _layouts;
    private readonly SynchronizationContext _ui;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _kbHook, _mouseHook;
    private Native.HookProc? _kbProc, _mouseProc;

    private bool _shift, _ctrl, _alt, _win, _caps;
    private enum Mod { None, Shift, Ctrl, Alt, Win }
    private Mod _downMod = Mod.None;      // модифікатор, натиснутий першим (кандидат на «тап»)
    private bool _otherSinceDown;
    private int _downAt;
    private Mod _lastTap = Mod.None;
    private int _lastTapAt;

    public KeyboardMonitor(Layouts layouts, SynchronizationContext ui)
    {
        _layouts = layouts;
        _ui = ui;
    }

    public void Start()
    {
        _caps = (Native.GetKeyState((int)VK_CAPITAL) & 1) != 0;
        var started = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            try { HookThread(started); }
            catch (Exception ex) { Log.Write($"ПОМИЛКА (потік хуків): {ex}"); started.Set(); }
        }) { IsBackground = true, Name = "UaSwitch hooks" };
        _thread.Start();
        started.Wait();
        if (_kbHook == IntPtr.Zero)
            throw new InvalidOperationException($"Не вдалося встановити хук клавіатури (код Win32: {_hookError}).");
    }

    private int _hookError;

    private void HookThread(ManualResetEventSlim started)
    {
        {
            _threadId = Native.GetCurrentThreadId();
            _kbProc = KeyboardProc;
            _mouseProc = MouseProc;
            var mod = Native.GetModuleHandle(null);
            _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, mod, 0);
            if (_kbHook == IntPtr.Zero) _hookError = Marshal.GetLastWin32Error();
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, mod, 0);
            Log.Write($"Хук клавіатури: {(_kbHook != IntPtr.Zero ? "OK" : "ПОМИЛКА " + _hookError)}, хук миші: {(_mouseHook != IntPtr.Zero ? "OK" : "ПОМИЛКА")}");
            started.Set();
            while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            Native.UnhookWindowsHookEx(_kbHook);
            Native.UnhookWindowsHookEx(_mouseHook);
        }
    }

    public void Dispose()
    {
        if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(1000);
    }

    public void ResetWord() { lock (Sync) Word.Clear(); }

    private string? TextOf(List<KeyStroke> keys)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var k in keys)
        {
            var c = _layouts.CharFor(k.Vk, k.Scan, k.Mods, k.Shown);
            if (c == null) return null;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public bool CapsOn => _caps;
    public void SetCaps(bool on) => _caps = on;

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int m = (int)wParam;
            if (m is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN)
            {
                ResetWord();
                _lastTap = Mod.None;
                _otherSinceDown = true;
            }
        }
        return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        try
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool down = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            bool up = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;

            // Власний синтетичний ввід (і будь-який інший інжектований) не записуємо
            if ((k.flags & Native.LLKHF_INJECTED) != 0 || k.dwExtraInfo == Native.OwnInputMarker)
                return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);

            var mod = ModOf(k.vkCode);
            if (mod != Mod.None)
            {
                bool wasDown = IsModDown(mod);
                SetModDown(mod, down);
                if (down && !wasDown) OnModifierDown(mod);
                else if (up) OnModifierUp(mod);
                return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
            }

            if (!down) return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);

            // Будь-яка звичайна клавіша скасовує «тап» модифікатора
            _otherSinceDown = true;
            _lastTap = Mod.None;

            if (k.vkCode == VK_CAPITAL) { _caps = !_caps; return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam); }

            if (!Enabled) return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);

            if (k.vkCode == VK_PAUSE && PauseHotkeys && !_ctrl && !_alt && !_win)
            {
                Post(_shift ? SelectionRequested : LastWordRequested);
                return (IntPtr)1; // поглинаємо клавішу
            }

            if (Record(k)) return (IntPtr)1;
        }
        catch { /* хук ніколи не повинен падати */ }
        return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    /// <summary>Записує натискання в буфер слова. Повертає true, якщо клавішу треба поглинути.</summary>
    private bool Record(Native.KBDLLHOOKSTRUCT k)
    {
        bool altGr = _ctrl && _alt;
        lock (Sync)
        {
            if (_win || ((_ctrl || _alt) && !altGr)) { Word.Clear(); return false; } // гарячі клавіші програм

            uint vk = k.vkCode;
            if (vk == VK_BACK)
            {
                // Backspace одразу після автовиправлення — скасувати його
                if ((Word.AutoConverted || Word.CommaInserted) && Word.ArmedVersion == Word.Version)
                {
                    Word.ArmedVersion = -1;
                    Post(UndoRequested);
                    return true;
                }
                if (Word.Trailing.Count > 0) Word.Trailing.RemoveAt(Word.Trailing.Count - 1);
                else if (Word.Keys.Count > 0) Word.Keys.RemoveAt(Word.Keys.Count - 1);
                Word.Version++;
                return false;
            }
            if (vk is VK_SPACE or VK_TAB)
            {
                if (Word.Keys.Count > 0 && Word.Trailing.Count < 8)
                {
                    Word.Trailing.Add(vk == VK_SPACE ? ' ' : '\t');
                    Word.Version++;
                    if (vk == VK_SPACE && Word.Trailing.Count == 1 && AutoMode && !Word.NoAuto)
                    {
                        int ver = Word.Version;
                        var h = AutoCheckRequested;
                        if (h != null) _ui.Post(_ => h(ver), null);
                        else Log.Write("Авто: немає обробника");
                    }
                }
                else Word.Clear();
                return false;
            }
            if (vk == VK_RETURN) { Word.Clear(); return false; }

            var hkl = Native.ForegroundLayout(out var hwnd);
            var mods = (_shift ? KeyMods.Shift : 0) | (_caps ? KeyMods.Caps : 0) | (altGr ? KeyMods.AltGr : 0);
            if (_layouts.CharFor(vk, k.scanCode, mods, hkl) == null)
            {
                Word.Clear(); // стрілки, Home/End, F-клавіші, Delete, мертві клавіші тощо
                return false;
            }

            if (hwnd != Word.Hwnd || Word.Trailing.Count > 0 || Word.Keys.Count >= MaxWord)
            {
                // Однобуквене слово, за яким іде рівно один пробіл, запам'ятовуємо як попереднє
                bool oneSpace = hwnd == Word.Hwnd && Word.Keys.Count > 0 && Word.Trailing.Count == 1 && Word.Trailing[0] == ' ';
                KeyStroke? prev = oneSpace && Word.Keys.Count == 1 && !Word.AutoConverted ? Word.Keys[0] : null;
                string? prevText = oneSpace ? Word.ScreenText ?? TextOf(Word.Keys) : null;
                var recent = new List<string>();
                if (oneSpace && prevText != null)
                {
                    recent.AddRange(Word.Recent);
                    recent.AddRange(prevText.Split(' '));
                    while (recent.Count > 8) recent.RemoveAt(0);
                }
                Word.Clear();
                Word.Hwnd = hwnd;
                Word.Prev = prev;
                Word.Recent.AddRange(recent);
            }
            Word.Keys.Add(new KeyStroke { Vk = vk, Scan = k.scanCode, Mods = mods, Shown = hkl });
            Word.Version++;
            if (AutoMode && Word.Keys.Count >= 4 && !Word.AutoConverted && !Word.NoAuto)
            {
                int ver = Word.Version;
                var h = EarlyCheckRequested;
                if (h != null) _ui.Post(_ => h(ver), null);
            }
            return false;
        }
    }

    private void OnModifierDown(Mod mod)
    {
        _downMod = mod; // комбінації модифікаторів відсіюються в OnModifierUp
        _otherSinceDown = false;
        _downAt = Environment.TickCount;
    }

    private void OnModifierUp(Mod mod)
    {
        int now = Environment.TickCount;
        bool isTap = mod == _downMod && !_otherSinceDown && now - _downAt <= TapMaxHoldMs
                     && !_shift && !_ctrl && !_alt && !_win;
        _downMod = Mod.None;
        if (!isTap || !Enabled || mod is not (Mod.Shift or Mod.Ctrl)) { _lastTap = Mod.None; return; }

        if (_lastTap == mod && now - _lastTapAt <= DoubleTapMs)
        {
            _lastTap = Mod.None;
            if (mod == Mod.Shift && DoubleShift) Post(LastWordRequested);
            else if (mod == Mod.Ctrl && DoubleCtrl) Post(SelectionRequested);
        }
        else
        {
            _lastTap = mod;
            _lastTapAt = now;
        }
    }

    private void Post(Action? a) { if (a != null) _ui.Post(_ => a(), null); }

    private static Mod ModOf(uint vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => Mod.Shift,
        0x11 or 0xA2 or 0xA3 => Mod.Ctrl,
        0x12 or 0xA4 or 0xA5 => Mod.Alt,
        0x5B or 0x5C => Mod.Win,
        _ => Mod.None
    };

    private bool IsModDown(Mod m) => m switch { Mod.Shift => _shift, Mod.Ctrl => _ctrl, Mod.Alt => _alt, Mod.Win => _win, _ => false };

    private void SetModDown(Mod m, bool v)
    {
        switch (m)
        {
            case Mod.Shift: _shift = v; break;
            case Mod.Ctrl: _ctrl = v; break;
            case Mod.Alt: _alt = v; break;
            case Mod.Win: _win = v; break;
        }
    }
}
