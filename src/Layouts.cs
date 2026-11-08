using System.Text;

namespace UaSwitch;

/// <summary>Знаходить українську та англійську розкладки і перекладає натискання клавіш у символи.</summary>
internal sealed class Layouts
{
    private const int LANG_ENGLISH = 0x09, LANG_UKRAINIAN = 0x22;
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_CAPITAL = 0x14;

    public IntPtr Ua { get; private set; }
    public IntPtr En { get; private set; }
    public bool Ready => Ua != IntPtr.Zero && En != IntPtr.Zero;

    private Dictionary<char, char> _enToUa = new(), _uaToEn = new();
    private readonly Dictionary<(uint, int, IntPtr), string?> _cache = new();

    public void Refresh()
    {
        int n = Native.GetKeyboardLayoutList(0, null);
        var list = new IntPtr[n];
        Native.GetKeyboardLayoutList(n, list);
        Ua = En = IntPtr.Zero;
        foreach (var hkl in list)
        {
            int primary = Native.LangId(hkl) & 0x3FF;
            if (primary == LANG_UKRAINIAN && Ua == IntPtr.Zero) Ua = hkl;
            // Віддаємо перевагу English (US), але згодиться будь-яка англійська
            if (primary == LANG_ENGLISH && (En == IntPtr.Zero || Native.LangId(hkl) == 0x0409)) En = hkl;
        }
        lock (_cache) _cache.Clear();
        if (Ready) BuildMaps();
    }

    public bool IsUa(IntPtr hkl) => (Native.LangId(hkl) & 0x3FF) == LANG_UKRAINIAN;
    public IntPtr Other(IntPtr hkl) => IsUa(hkl) ? En : Ua;

    /// <summary>Символ, який дає клавіша в заданій розкладці (null — якщо нічого або мертва клавіша).</summary>
    public string? CharFor(uint vk, uint scan, KeyMods mods, IntPtr hkl)
    {
        var key = (vk, (int)mods, hkl);
        lock (_cache)
            if (_cache.TryGetValue(key, out var cached)) return cached;

        var state = new byte[256];
        if (mods.HasFlag(KeyMods.Shift)) state[VK_SHIFT] = 0x80;
        if (mods.HasFlag(KeyMods.Caps)) state[VK_CAPITAL] = 0x01;
        if (mods.HasFlag(KeyMods.AltGr)) { state[VK_CONTROL] = 0x80; state[VK_MENU] = 0x80; }
        if (scan == 0) scan = Native.MapVirtualKeyEx(vk, 0, hkl);

        var buf = new char[16];
        // flags 0x4: не змінювати стан клавіатури ядра (Windows 10 1607+)
        int r = Native.ToUnicodeEx(vk, scan, state, buf, buf.Length, 0x4, hkl);
        string? result = r > 0 ? new string(buf, 0, Math.Min(r, buf.Length)).TrimEnd('\0') : null;
        if (result != null && result.Length == 0) result = null;
        if (result != null && result.Length == 1 && char.IsControl(result[0])) result = null;

        lock (_cache) _cache[key] = result;
        return result;
    }

    private void BuildMaps()
    {
        var enToUa = new Dictionary<char, char>();
        var uaToEn = new Dictionary<char, char>();
        foreach (var mods in new[] { KeyMods.None, KeyMods.Shift })
        {
            for (uint vk = 0x20; vk <= 0xFE; vk++)
            {
                if (vk is >= 0x60 and <= 0x6F) continue; // цифровий блок однаковий у всіх розкладках
                var en = CharFor(vk, 0, mods, En);
                var ua = CharFor(vk, 0, mods, Ua);
                if (en is not { Length: 1 } || ua is not { Length: 1 } || en == ua) continue;
                if (!enToUa.ContainsKey(en[0])) enToUa[en[0]] = ua[0];
                if (!uaToEn.ContainsKey(ua[0])) uaToEn[ua[0]] = en[0];
            }
        }
        _enToUa = enToUa;
        _uaToEn = uaToEn;
    }

    /// <summary>Конвертує довільний текст. Напрямок визначається за переважанням кирилиці чи латиниці.</summary>
    public string ConvertText(string text, out bool toUa)
    {
        int cyr = 0, lat = 0;
        foreach (char c in text)
        {
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') lat++;
            else if (c is >= 'Ѐ' and <= 'ӿ') cyr++;
        }
        toUa = lat >= cyr;
        var map = toUa ? _enToUa : _uaToEn;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text) sb.Append(map.TryGetValue(c, out var m) ? m : c);
        return sb.ToString();
    }
}

[Flags]
internal enum KeyMods { None = 0, Shift = 1, Caps = 2, AltGr = 4 }
