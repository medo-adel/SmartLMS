namespace SmartLMS.Infrastructure.Services.Lms;

/// <summary>
/// Measures playable duration from the stored bytes, never from what an instructor typed. Supported: ISO base media (mp4, m4a, mov: the movie header's timescale and duration)
/// and WAV. Other formats (webm, mp3, anything unreadable or inconsistent) return null, which means "not measured" and earns no subscription learning time.
/// </summary>
public static class MediaDuration
{
    public const int MaxSeconds = 24 * 3600;

    public static int? TryMeasure(string path)
    {
        try { using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return TryMeasure(fs); } catch { return null; }
    }

    public static int? TryMeasure(Stream s)
    {
        try
        {
            if (!s.CanSeek) return null;
            Span<byte> h = stackalloc byte[12]; s.Position = 0; if (s.Read(h) < 12) return null;
            if (h[4] == 'f' && h[5] == 't' && h[6] == 'y' && h[7] == 'p') return Mp4(s);
            if (h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'A' && h[10] == 'V' && h[11] == 'E') return Wav(s);
            return null;
        }
        catch { return null; }
    }

    private static int? Valid(double seconds) => double.IsFinite(seconds) && seconds >= 1 && seconds <= MaxSeconds ? (int)Math.Floor(seconds) : null;

    private static int? Mp4(Stream s) => Boxes(s, 0, s.Length, 0);

    private static int? Boxes(Stream s, long start, long end, int depth)
    {
        if (depth > 4) return null;
        var pos = start; Span<byte> b = stackalloc byte[16];
        while (pos + 8 <= end)
        {
            s.Position = pos; if (s.Read(b[..8]) < 8) return null;
            long size = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); var hdr = 8L; var type = System.Text.Encoding.ASCII.GetString(b.Slice(4, 4));
            if (size == 1) { if (s.Read(b[8..16]) < 8) return null; size = 0; for (var i = 8; i < 16; i++) size = size << 8 | b[i]; hdr = 16; }
            else if (size == 0) size = end - pos;
            if (size < hdr || pos + size > end) return null;
            if (type == "moov") return Boxes(s, pos + hdr, pos + size, depth + 1);
            if (type == "mvhd")
            {
                s.Position = pos + hdr; Span<byte> m = stackalloc byte[32]; if (s.Read(m) < 32) return null;
                long ts, dur;
                if (m[0] == 1) { if (s.Position + 0 > end) return null; Span<byte> w = stackalloc byte[32]; s.Position = pos + hdr + 20; if (s.Read(w[..12]) < 12) return null; ts = (uint)(w[0] << 24 | w[1] << 16 | w[2] << 8 | w[3]); dur = 0; for (var i = 4; i < 12; i++) dur = dur << 8 | w[i]; }
                else { ts = (uint)(m[12] << 24 | m[13] << 16 | m[14] << 8 | m[15]); dur = (uint)(m[16] << 24 | m[17] << 16 | m[18] << 8 | m[19]); }
                return ts <= 0 ? null : Valid((double)dur / ts);
            }
            pos += size;
        }
        return null;
    }

    private static int? Wav(Stream s)
    {
        long pos = 12; Span<byte> b = stackalloc byte[8]; long byteRate = 0;
        while (pos + 8 <= s.Length)
        {
            s.Position = pos; if (s.Read(b) < 8) return null;
            var id = System.Text.Encoding.ASCII.GetString(b[..4]); long size = (uint)(b[7] << 24 | b[6] << 16 | b[5] << 8 | b[4]);
            if (id == "fmt ") { Span<byte> f = stackalloc byte[16]; if (s.Read(f) < 16) return null; byteRate = (uint)(f[11] << 24 | f[10] << 16 | f[9] << 8 | f[8]); }
            else if (id == "data") { var real = Math.Min(size, s.Length - (pos + 8)); return byteRate <= 0 ? null : Valid((double)real / byteRate); }
            pos += 8 + size + (size & 1);
        }
        return null;
    }
}
