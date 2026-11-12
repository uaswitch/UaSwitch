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
    public static bool IsPasswordField(IntPtr fg)
    {
        IntPtr focus = Native.FocusWindow(fg);
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
        bool result = false;
        var t = new Thread(() => { try { result = ProtectedByAccessibility(focus); } catch { } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
        if (!t.Join(250))
        {
            Log.Write("Поле пароля: програма не відповіла вчасно — виправлення пропущено");
            return true;
        }
        return result;
    }

    private static bool ProtectedByAccessibility(IntPtr hwnd)
    {
        if (AccessibleObjectFromWindow(hwnd, OBJID_CLIENT, ref IID_IAccessible, out var o) != 0 || o is not IAccessible acc)
            return false;
        // Спускаємося до елемента з фокусом (у браузері вікно — це вся сторінка)
        for (int depth = 0; depth < 6; depth++)
        {
            object? f;
            try { f = acc.accFocus; } catch { f = null; }
            if (f is int child && child != 0) return IsProtected(acc, child);
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
