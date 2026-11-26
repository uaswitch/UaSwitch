using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace UaSwitch;

/// <summary>
/// Мітка поточної мови біля курсора: «UA», «EN», з ⇪ при Caps Lock (і # при Num Lock, якщо ввімкнено).
/// Прозоре вікно поверх усіх: кліки проходять крізь нього, фокус воно не забирає.
/// Під час набору мітка стоїть біля текстового курсора (якщо програма його показує Windows), інакше — біля вказівника миші.
/// </summary>
internal sealed class LangIndicator : Form
{
    private readonly Settings _settings;
    private readonly Layouts _layouts;
    private readonly KeyboardMonitor _monitor;
    private readonly Func<IntPtr, bool> _paused;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly IntPtr _ibeam = Native.LoadCursor(IntPtr.Zero, (IntPtr)32513); // IDC_IBEAM

    private IntPtr _lastFg, _lastHkl;
    private bool _lastCaps, _numLock, _visible;
    private int _showUntil, _syncAt, _keyTick;
    private Native.POINT _keyPointer;
    private string _drawn = "";
    private Native.SIZE _size;
    private int _posX, _posY;

    public LangIndicator(Settings settings, Layouts layouts, KeyboardMonitor monitor, Func<IntPtr, bool> paused)
    {
        _settings = settings;
        _layouts = layouts;
        _monitor = monitor;
        _paused = paused;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1, 1);
        _timer.Tick += (_, _) => Tick();
        CreateHandle();
        Apply();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8 | 0x8000000;
            return cp;
        }
    }

    /// <summary>Застосувати змінені налаштування.</summary>
    public void Apply()
    {
        _drawn = "";
        if (_settings.IndicatorMode == "off") { _timer.Stop(); Hide(false); return; }
        _timer.Interval = 100;
        _timer.Start();
    }

    /// <summary>Мову перемкнули не за допомогою клавіатури (наприклад, автоматично) — коротко показати мітку.</summary>
    public void Flash() { if (_settings.IndicatorMode != "off") _showUntil = Environment.TickCount + 1500; }

    private void Tick()
    {
        try { TickCore(); }
        catch (Exception e) { Log.Write("Індикатор: " + e.Message); _timer.Stop(); Hide(false); }
    }

    private void TickCore()
    {
        int now = Environment.TickCount;
        var hkl = Native.ForegroundLayout(out var fg);

        // Caps і Num Lock звіряємо з Windows раз на секунду (і щоразу, коли їх натиснули)
        if (now - _syncAt > 1000 || _monitor.CapsOn != _lastCaps)
        {
            _syncAt = now;
            _monitor.SetCaps(Native.CapsLockOn());
            _numLock = Native.NumLockOn();
        }
        bool caps = _monitor.CapsOn;

        // Мова чи Caps Lock змінилися в тому самому вікні — показати мітку на мить
        if (fg == _lastFg && _lastHkl != IntPtr.Zero && (hkl != _lastHkl || caps != _lastCaps)) _showUntil = now + 1500;
        _lastFg = fg; _lastHkl = hkl; _lastCaps = caps;

        if (!_settings.Enabled || fg == IntPtr.Zero || hkl == IntPtr.Zero || _paused(fg) || (_settings.IndicatorHideFullScreen && Native.FullScreenAppRunning()))
        { Hide(true); return; }

        var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
        bool haveCursor = Native.GetCursorInfo(ref ci);
        bool pointerShown = haveCursor && (ci.flags & 1) != 0;

        // Щойно друкували — мітка біля текстового курсора; поворухнули мишу — знову біля вказівника
        if (_monitor.LastKeyTick != _keyTick) { _keyTick = _monitor.LastKeyTick; _keyPointer = ci.pt; }
        bool typing = now - _keyTick < 4000 && Math.Abs(ci.pt.X - _keyPointer.X) + Math.Abs(ci.pt.Y - _keyPointer.Y) < 4;

        bool want = _settings.IndicatorMode switch
        {
            "always" => true,
            "text" => (pointerShown && ci.hCursor == _ibeam) || typing,
            _ => now - _showUntil < 0,
        };
        if (!want) { Hide(true); return; }

        Native.POINT at;
        int dpi = DpiAt(ci.pt);
        if (typing && CaretPos(fg, out var caret)) at = new Native.POINT { X = caret.X + Scale(6, dpi), Y = caret.Y };
        else if (haveCursor) at = new Native.POINT { X = ci.pt.X + Scale(14, dpi), Y = ci.pt.Y + Scale(20, dpi) };
        else { Hide(true); return; }

        string label = _layouts.IsUa(hkl) ? "UA" : (Native.LangId(hkl) & 0x3FF) == 0x09 ? "EN" : LangCode(hkl);
        string marks = (caps ? " ⇪" : "") + (_settings.IndicatorNumLock && _numLock ? " #" : "");
        Render(label, marks, at, dpi);
        _timer.Interval = 30;
    }

    private void Hide(bool slow)
    {
        if (_visible) { Native.ShowWindow(Handle, 0); _visible = false; }
        if (slow && _timer.Enabled) _timer.Interval = 100;
    }

    private static int Scale(int px, int dpi) => px * dpi / 96;

    private void Render(string label, string marks, Native.POINT at, int dpi)
    {
        string key = $"{label}{marks}{dpi}{_settings.IndicatorBig}{_settings.IconStyle}";
        var screen = Screen.FromPoint(new Point(at.X, at.Y)).WorkingArea;
        if (key != _drawn)
        {
            _drawn = key;
            Draw(label, marks, dpi);
            _posX = int.MinValue;
        }
        // Біля краю екрана — по інший бік від курсора
        int x = at.X, y = at.Y;
        if (x + _size.cx > screen.Right) x = Math.Max(screen.Left, at.X - _size.cx - Scale(24, dpi));
        if (y + _size.cy > screen.Bottom) y = Math.Max(screen.Top, at.Y - _size.cy - Scale(28, dpi));
        if (_visible && x == _posX && y == _posY) return;
        _posX = x; _posY = y;
        Native.SetWindowPos(Handle, (IntPtr)(-1), x, y, 0, 0, 0x0001 | 0x0010 | 0x0040); // HWND_TOPMOST, SWP_NOSIZE|NOACTIVATE|SHOWWINDOW
        _visible = true;
    }

    /// <summary>Картинка мітки (окремо від вікна — щоб можна було перевірити вигляд).</summary>
    internal static Bitmap Pill(string label, string marks, int dpi, bool big, string style)
    {
        float fontPx = Scale(big ? 17 : 13, dpi);
        using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        string text = label + marks;
        Size measured;
        using (var tmp = new Bitmap(1, 1))
        using (var g0 = Graphics.FromImage(tmp))
            measured = Size.Ceiling(g0.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic));
        int padX = Scale(big ? 8 : 6, dpi), padY = Scale(3, dpi);
        int w = measured.Width + padX * 2, h = measured.Height + padY * 2;

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);
            var r = new Rectangle(0, 0, w - 1, h - 1);
            using var path = Round(r, h / 2 - 1);
            bool ua = label == "UA";
            if (ua && style == "flags")
            {
                // прапор: синя половина зверху, жовта знизу, текст — темний на жовтому не читається, тож з тінню
                g.SetClip(path);
                using var blue = new SolidBrush(Color.FromArgb(240, 0, 87, 183));
                using var yellow = new SolidBrush(Color.FromArgb(240, 255, 215, 0));
                g.FillRectangle(blue, 0, 0, w, h / 2 + 1);
                g.FillRectangle(yellow, 0, h / 2, w, h - h / 2);
                g.ResetClip();
                using var shadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
                using var white = new SolidBrush(Color.White);
                g.DrawString(text, font, shadow, padX + 1, padY + 1, StringFormat.GenericTypographic);
                g.DrawString(text, font, white, padX, padY, StringFormat.GenericTypographic);
            }
            else
            {
                Color bg = ua ? Color.FromArgb(235, 0, 87, 183) : label == "EN" ? Color.FromArgb(235, 45, 50, 60) : Color.FromArgb(235, 110, 116, 128);
                Color fg = ua ? Color.FromArgb(255, 215, 0) : Color.White;
                using var b = new SolidBrush(bg);
                g.FillPath(b, path);
                using var t = new SolidBrush(fg);
                g.DrawString(text, font, t, padX, padY, StringFormat.GenericTypographic);
            }
        }
        return bmp;
    }

    private void Draw(string label, string marks, int dpi)
    {
        using var bmp = Pill(label, marks, dpi, _settings.IndicatorBig, _settings.IconStyle);
        int w = bmp.Width, h = bmp.Height;
        IntPtr screenDc = Native.GetDC(IntPtr.Zero), memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0)), old = Native.SelectObject(memDc, hBmp);
        try
        {
            _size = new Native.SIZE { cx = w, cy = h };
            var src = new Native.POINT();
            var pos = new Native.POINT { X = Left, Y = Top };
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 }; // AC_SRC_ALPHA
            Native.UpdateLayeredWindow(Handle, screenDc, ref pos, ref _size, memDc, ref src, 0, ref blend, 2); // ULW_ALPHA
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteObject(hBmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static GraphicsPath Round(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        int d = Math.Max(2, rad * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Текстовий курсор активного вікна в координатах екрана (низ курсора). Багато програм його не показують Windows.</summary>
    private static bool CaretPos(IntPtr fg, out Native.POINT p)
    {
        p = default;
        uint tid = Native.GetWindowThreadProcessId(fg, out _);
        var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(tid, ref info) || info.hwndCaret == IntPtr.Zero) return false;
        var rc = info.rcCaret;
        if (rc.Bottom <= rc.Top) return false;
        p = new Native.POINT { X = rc.Right, Y = rc.Top };
        if (!Native.ClientToScreen(info.hwndCaret, ref p)) return false;
        p.Y += Math.Max(0, (rc.Bottom - rc.Top) / 2 - 8);
        return true;
    }

    private static int DpiAt(Native.POINT pt)
    {
        try
        {
            var mon = MonitorFromPoint(pt, 2);
            if (GetDpiForMonitor(mon, 0, out uint x, out _) == 0 && x > 0) return (int)x;
        }
        catch { }
        return 96;
    }

    private static string LangCode(IntPtr hkl)
    {
        try { return new System.Globalization.CultureInfo(Native.LangId(hkl)).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch { return "??"; }
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Native.POINT pt, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int type, out uint dpiX, out uint dpiY);
}
