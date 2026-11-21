namespace UaSwitch;

/// <summary>
/// Після кількох подвійних Shift Windows вважає, що Shift натиснули 5 разів, і пропонує ввімкнути «Залипання клавіш».
/// Поки UaSwitch працює, вимикаємо лише цю гарячу клавішу (саму функцію не чіпаємо) і повертаємо як було при виході.
/// Зміна не записується в профіль, тож після перезавантаження Windows усе одно буде як раніше.
/// </summary>
internal static class StickyKeys
{
    private static bool _changed;

    public static void Set(bool suppress)
    {
        try
        {
            var sk = new Native.STICKYKEYS { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.STICKYKEYS>() };
            if (!Native.SystemParametersInfo(Native.SPI_GETSTICKYKEYS, (uint)sk.cbSize, ref sk, 0)) return;
            if (suppress && !_changed && (sk.dwFlags & Native.SKF_HOTKEYACTIVE) != 0 && (sk.dwFlags & Native.SKF_STICKYKEYSON) == 0)
            {
                sk.dwFlags &= ~Native.SKF_HOTKEYACTIVE;
                if (Native.SystemParametersInfo(Native.SPI_SETSTICKYKEYS, (uint)sk.cbSize, ref sk, 0))
                {
                    _changed = true;
                    Log.Write("Вікно «Залипання клавіш» на 5× Shift вимкнено, поки працює UaSwitch");
                }
            }
            else if (!suppress && _changed)
            {
                sk.dwFlags |= Native.SKF_HOTKEYACTIVE;
                Native.SystemParametersInfo(Native.SPI_SETSTICKYKEYS, (uint)sk.cbSize, ref sk, 0);
                _changed = false;
            }
        }
        catch (Exception e) { Log.Write("Залипання клавіш: " + e.Message); }
    }
}
