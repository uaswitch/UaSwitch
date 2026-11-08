using System.Media;

namespace UaSwitch;

/// <summary>Короткий тихий «клік» при виправленні (генерується в пам'яті, без файлів).</summary>
internal static class Feedback
{
    private static SoundPlayer? _player;

    public static void Click()
    {
        try
        {
            if (_player == null)
            {
                _player = new SoundPlayer(new MemoryStream(MakeWav()));
                _player.Load();
            }
            _player.Play(); // асинхронно, не блокує
        }
        catch { }
    }

    private static byte[] MakeWav()
    {
        const int rate = 22050, ms = 35;
        int n = rate * ms / 1000;
        var pcm = new short[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate;
            double env = Math.Exp(-t * 140);                 // швидке згасання
            double v = Math.Sin(2 * Math.PI * 1500 * t) * 0.6 + Math.Sin(2 * Math.PI * 2300 * t) * 0.4;
            pcm[i] = (short)(v * env * 0.22 * short.MaxValue);
        }
        using var ms2 = new MemoryStream();
        using var bw = new BinaryWriter(ms2);
        int data = n * 2;
        bw.Write(new[] { 'R', 'I', 'F', 'F' }); bw.Write(36 + data);
        bw.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); bw.Write(16);
        bw.Write((short)1); bw.Write((short)1); bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write(new[] { 'd', 'a', 't', 'a' }); bw.Write(data);
        foreach (var s in pcm) bw.Write(s);
        bw.Flush();
        return ms2.ToArray();
    }
}
