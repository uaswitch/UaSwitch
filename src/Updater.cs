using System.Drawing;
using System.Net;
using System.Text.RegularExpressions;

namespace UaSwitch;

/// <summary>
/// Перевірка нової версії: один запит до GitHub (опис останнього релізу).
/// Нічого не завантажує й не встановлює — лише показує, що нового, і відкриває сторінку релізу.
/// </summary>
internal static class Updater
{
    public const string ReleasesUrl = "https://github.com/uaswitch/UaSwitch/releases/latest";
    private const string ApiUrl = "https://api.github.com/repos/uaswitch/UaSwitch/releases/latest";

    public sealed class Release
    {
        public Version Version = new(0, 0);
        public string Tag = "";
        public string Notes = "";
        public string Url = ReleasesUrl;
    }

    public static Version Current => Version.TryParse(Application.ProductVersion.Split('+', '-')[0], out var v) ? v : new Version(0, 0);

    /// <summary>Останній реліз (без pre-release) або null, якщо немає зв'язку.</summary>
    public static Release? Fetch()
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(ApiUrl);
            req.UserAgent = "UaSwitch/" + Current;
            req.Accept = "application/vnd.github+json";
            req.Timeout = req.ReadWriteTimeout = 10000;
            using var resp = req.GetResponse();
            using var sr = new StreamReader(resp.GetResponseStream()!);
            return Parse(sr.ReadToEnd());
        }
        catch (Exception e)
        {
            Log.Write("Перевірка оновлень: " + e.Message);
            return null;
        }
    }

    internal static Release? Parse(string json)
    {
        {
            string tag = Field(json, "tag_name");
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var ver)) return null;
            string url = Field(json, "html_url");
            return new Release
            {
                Version = ver,
                Tag = tag,
                Notes = PlainText(Field(json, "body")),
                Url = url.StartsWith("https://github.com/") ? url : ReleasesUrl,
            };
        }
    }

    // Перше входження поля — це поле самого релізу (поля автора йдуть пізніше)
    private static string Field(string json, string name)
    {
        var m = Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? Regex.Unescape(m.Groups[1].Value) : "";
    }

    /// <summary>Markdown опису релізу → звичайний текст для вікна.</summary>
    private static string PlainText(string md)
    {
        var lines = md.Replace("\r", "").Split('\n').Select(l =>
        {
            l = Regex.Replace(l, @"^#+\s*", "");
            l = Regex.Replace(l, @"^\s*[-*]\s+", "•  ");
            l = l.Replace("**", "").Replace("`", "");
            return Regex.Replace(l, @"\[([^\]]+)\]\([^)]+\)", "$1");
        });
        return string.Join(Environment.NewLine, lines).Trim();
    }

    /// <summary>Вікно «Вийшла нова версія»: список змін і кнопки.</summary>
    public static void ShowNewVersion(Release r, Settings settings)
    {
        using var f = new Form
        {
            Text = $"UaSwitch {r.Version} — що нового",
            AutoScaleDimensions = new SizeF(96f, 96f), AutoScaleMode = AutoScaleMode.Dpi,
            Font = new Font("Segoe UI", 9f), StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(480, 400), MinimizeBox = false, MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.Sizable, KeyPreview = true, TopMost = true,
        };
        try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        var head = new Label
        {
            Text = $"Вийшла нова версія {r.Version}. У вас {Current}.",
            Dock = DockStyle.Top, Height = 34, Padding = new Padding(10, 10, 10, 0),
        };
        var notes = new TextBox
        {
            Text = r.Notes.Length > 0 ? r.Notes : "Опис змін — на сторінці релізу.",
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            BackColor = SystemColors.Window, BorderStyle = BorderStyle.None,
        };
        var notesBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4) };
        notesBox.Controls.Add(notes);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6, 8, 6, 0),
        };
        var download = new Button { Text = "Завантажити", Size = new Size(120, 30) };
        var later = new Button { Text = "Нагадати пізніше", Size = new Size(130, 30) };
        var skip = new Button { Text = "Пропустити цю версію", Size = new Size(160, 30) };
        download.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(r.Url) { UseShellExecute = true }); } catch { }
            f.Close();
        };
        later.Click += (_, _) => f.Close();
        skip.Click += (_, _) => { settings.SkippedVersion = r.Version.ToString(); settings.Save(); f.Close(); };
        f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
        buttons.Controls.AddRange(new Control[] { download, later, skip });

        f.Controls.Add(notesBox);
        f.Controls.Add(head);
        f.Controls.Add(buttons);
        f.AcceptButton = download;
        f.ShowDialog();
    }
}
