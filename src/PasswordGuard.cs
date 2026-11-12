using System.Runtime.InteropServices;
using System.Text;
using Accessibility;

namespace UaSwitch;

/// <summary>
/// Чи стоїть курсор у полі пароля. Ті самі дані Windows дає екранним читачам:
/// класичні поля — стиль ES_PASSWORD, браузери/Telegram/сучасні програми — стан «захищено» (STATE_SYSTEM_PROTECTED).
/// </summary>
internal static class PasswordGuard
{
    private const int GWL_STYLE = -16, ES_PASSWORD = 0x20;
    private const uint OBJID_CLIENT = 0xFFFFFFFC;
    private const int STATE_SYSTEM_PROTECTED = 0x20000000;
    private static Guid IID_IAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);

    /// <summary>
    /// true — поле пароля, або програма не відповіла вчасно (тоді краще нічого не виправляти).
    /// </summary>
    // Останній результат «пароль» для вікна: Chrome вмикає підтримку спецможливостей не миттєво,
    // тому перевіряємо вже на першій літері слова й пам'ятаємо результат кілька секунд.
    private static IntPtr _pwdHwnd;
    private static DateTime _pwdUntil;

    /// <summary>Перевірка у фоні на першій літері слова (нічого не чекає).</summary>
    public static void Warmup(IntPtr fg)
    {
        IntPtr focus = Native.FocusWindow(fg);
        var t = new Thread(() =>
        {
            try
            {
                bool pwd = UiaIsPassword() == true || ProtectedByAccessibility(focus);
                if (pwd) { _pwdHwnd = focus; _pwdUntil = DateTime.UtcNow.AddSeconds(5); }
            }
            catch { }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
    }

    public static bool IsPasswordField(IntPtr fg)
    {
        IntPtr focus = Native.FocusWindow(fg);
        if (focus == _pwdHwnd && DateTime.UtcNow < _pwdUntil) return true; // нещодавно було поле пароля (не продовжуємо)
        try
        {
            var cls = new StringBuilder(64);
            GetClassName(focus, cls, cls.Capacity);
            if (cls.ToString().IndexOf("edit", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (GetWindowLong(focus, GWL_STYLE) & ES_PASSWORD) != 0)
                return true;
        }
        catch { }

        // Запит до іншої програми — у фоні з обмеженням часу, щоб «завислий» застосунок не гальмував UaSwitch
        bool? uia = null, msaa = null;
        var t = new Thread(() =>
        {
            try { uia = UiaIsPassword(); } catch { }
            if (uia != true) try { msaa = ProtectedByAccessibility(focus); } catch { }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
        bool timedOut = !t.Join(300);
        bool result = timedOut || uia == true || msaa == true;
        if (result) Remember(focus);

        // Діагностика в журнал (без тексту): лише коли щось змінилося
        string cls2 = ClassOf(focus);
        string diag = $"Поле: {cls2}, UIA={Show(uia)}, MSAA={Show(msaa)}{(timedOut ? ", не відповіло за 300 мс" : "")} → {(result ? "пароль" : "звичайне")}";
        if (diag != _lastDiag) { _lastDiag = diag; Log.Write(diag); }
        return result;
    }

    private static bool Remember(IntPtr focus)
    {
        _pwdHwnd = focus; _pwdUntil = DateTime.UtcNow.AddSeconds(5);
        return true;
    }

    private static string _lastDiag = "";
    private static string Show(bool? b) => b == null ? "?" : b.Value ? "так" : "ні";
    private static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(64);
        try { GetClassName(h, sb, sb.Capacity); } catch { }
        return sb.ToString();
    }

    // ---------- UI Automation: властивість IsPassword елемента з фокусом ----------
    private const int UIA_IsPasswordPropertyId = 30019;

    [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
    private class CUIAutomation { }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements(); void CompareRuntimeIds(); void GetRootElement(); void ElementFromHandle(); void ElementFromPoint();
        [PreserveSig] int GetFocusedElement(out IUIAutomationElement? element);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus(); void GetRuntimeId(); void FindFirst(); void FindAll(); void FindFirstBuildCache(); void FindAllBuildCache(); void BuildUpdatedCache();
        [PreserveSig] int GetCurrentPropertyValue(int propertyId, out object? value);
    }

    private static IUIAutomation? _uia;

    private static bool? UiaIsPassword()
    {
        _uia ??= (IUIAutomation)new CUIAutomation();
        if (_uia.GetFocusedElement(out var el) != 0 || el == null) return null;
        try
        {
            if (el.GetCurrentPropertyValue(UIA_IsPasswordPropertyId, out var v) != 0) return null;
            return v is bool b && b;
        }
        finally { Marshal.ReleaseComObject(el); }
    }

    // ---------- MSAA (запасний шлях): стан «захищено» ----------
    private static bool ProtectedByAccessibility(IntPtr hwnd)
    {
        if (AccessibleObjectFromWindow(hwnd, OBJID_CLIENT, ref IID_IAccessible, out var o) != 0 || o is not IAccessible acc)
            return false;
        // Спускаємося до елемента з фокусом (у браузері вікно — це вся сторінка)
        for (int depth = 0; depth < 6; depth++)
        {
            object? f;
            try { f = acc.accFocus; } catch { f = null; }
            if (f is int child && child != 0)
            {
                // Chrome повертає від'ємні ідентифікатори нащадків — беремо сам елемент
                try { if (acc.get_accChild(child) is IAccessible c) return IsProtected(c, 0); } catch { }
                return IsProtected(acc, child);
            }
            if (f is IAccessible inner && !ReferenceEquals(inner, acc)) { acc = inner; continue; }
            break;
        }
        return IsProtected(acc, 0);
    }

    private static bool IsProtected(IAccessible acc, int child)
    {
        try { return acc.get_accState(child) is int s && (s & STATE_SYSTEM_PROTECTED) != 0; }
        catch { return false; }
    }

    /// <summary>
    /// Слово схоже на пароль чи код: літери впереміш з цифрами або символами всередині («Qwe12rty», «a1b2», «p@ss»).
    /// </summary>
    public static bool LooksLikeSecret(string s)
    {
        string core = s.TrimEnd(".,!?;:\")»".ToCharArray());
        bool letters = core.Any(char.IsLetter), digits = core.Any(char.IsDigit);
        if (letters && digits) return true;
        for (int i = 1; i < core.Length - 1; i++)
            // Лише символи, яких немає на місці літер в жодній розкладці ([ ] ; ' , . / — це українські х ї ж є б ю .)
            if ("@#$%^&*_=+~".IndexOf(core[i]) >= 0) return true;
        return false;
    }
}
