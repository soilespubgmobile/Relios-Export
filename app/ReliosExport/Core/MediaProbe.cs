using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ReliosExport.Core;

/// <summary>ffprobe ile okunan kaynak video bilgisi.</summary>
public sealed record MediaInfo(
    int Width,
    int Height,
    int SarNum,
    int SarDen,
    int Rotation,
    double DurationSeconds,
    bool HasAudio,
    double FrameRate,
    string? ColorSpace,
    string? ColorTransfer,
    string? ColorRange)
{
    /// <summary>Kaynakta renk matrisi etiketi yoksa true (ffprobe "unknown" ya da hiç yazmaz).</summary>
    public bool ColorSpaceUnknown => ColorSpace is null or "" or "unknown" or "unspecified" or "reserved";

    /// <summary>HDR (PQ / HLG) kaynak mı.</summary>
    public bool IsHdr => ColorTransfer is "smpte2084" or "arib-std-b67";

    /// <summary>Döndürme uygulandıktan sonraki görüntü genişliği (piksel).</summary>
    public int DisplayWidth => Rotation is 90 or 270 ? Height : Width;

    /// <summary>Döndürme uygulandıktan sonraki görüntü yüksekliği (piksel).</summary>
    public int DisplayHeight => Rotation is 90 or 270 ? Width : Height;

    public double Sar => SarDen == 0 || SarNum == 0 ? 1.0 : (double)SarNum / SarDen;
}

public static class MediaProbe
{
    public static async Task<MediaInfo> ProbeAsync(string ffprobePath, string inputPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffprobePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[]
                 {
                     "-v", "error", "-print_format", "json",
                     "-show_format", "-show_streams", inputPath,
                 })
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffprobe başlatılamadı.");
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var json = await stdoutTask;
        var err = await stderrTask;
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe dosyayı okuyamadı: {err.Trim()}");
        }

        return Parse(json);
    }

    public static MediaInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        JsonElement? video = null;
        var hasAudio = false;
        foreach (var s in root.GetProperty("streams").EnumerateArray())
        {
            var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
            if (type == "video" && video is null && !IsAttachedPicture(s))
            {
                video = s;
            }
            else if (type == "audio")
            {
                hasAudio = true;
            }
        }

        if (video is not { } v)
        {
            throw new InvalidOperationException("Dosyada video akışı yok.");
        }

        var w = v.GetProperty("width").GetInt32();
        var h = v.GetProperty("height").GetInt32();
        var (sn, sd) = ParseRatio(v.TryGetProperty("sample_aspect_ratio", out var sar) ? sar.GetString() : null);
        var rotation = ReadRotation(v);
        var duration = ReadDuration(root, v);
        var fps = ReadFrameRate(v);
        return new MediaInfo(
            w, h, sn, sd, rotation, duration, hasAudio, fps,
            ReadString(v, "color_space"),
            ReadString(v, "color_transfer"),
            ReadString(v, "color_range"));
    }

    private static bool IsAttachedPicture(JsonElement s) =>
        s.TryGetProperty("disposition", out var d)
        && d.TryGetProperty("attached_pic", out var ap)
        && ap.GetInt32() == 1;

    private static (int, int) ParseRatio(string? r)
    {
        if (string.IsNullOrEmpty(r) || r == "N/A")
        {
            return (1, 1);
        }

        var parts = r.Split(':');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var n)
            && int.TryParse(parts[1], out var d)
            && n > 0 && d > 0)
        {
            return (n, d);
        }

        return (1, 1);
    }

    private static int ReadRotation(JsonElement v)
    {
        double? rot = null;
        if (v.TryGetProperty("side_data_list", out var sdl))
        {
            foreach (var sd in sdl.EnumerateArray())
            {
                if (sd.TryGetProperty("rotation", out var r) && r.TryGetDouble(out var val))
                {
                    rot = val;
                }
            }
        }

        if (rot is null
            && v.TryGetProperty("tags", out var tags)
            && tags.TryGetProperty("rotate", out var rt)
            && double.TryParse(rt.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var tv))
        {
            rot = tv;
        }

        var deg = (int)Math.Round(rot ?? 0) % 360;
        if (deg < 0)
        {
            deg += 360;
        }

        return deg;
    }

    private static string? ReadString(JsonElement v, string key) =>
        v.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    /// <summary>Ortalama kare hızı (ör. 60, 29.97); okunamazsa 0. Yalnızca bilgi için gösterilir.</summary>
    private static double ReadFrameRate(JsonElement v)
    {
        foreach (var key in new[] { "avg_frame_rate", "r_frame_rate" })
        {
            if (v.TryGetProperty(key, out var r) && r.GetString() is { } text)
            {
                var parts = text.Split('/');
                if (parts.Length == 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den)
                    && num > 0 && den > 0)
                {
                    return num / den;
                }
            }
        }

        return 0;
    }

    private static double ReadDuration(JsonElement root, JsonElement v)
    {
        static double Read(JsonElement e) =>
            e.TryGetProperty("duration", out var d)
            && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var val)
                ? val
                : 0;

        var fromFormat = root.TryGetProperty("format", out var f) ? Read(f) : 0;
        return fromFormat > 0 ? fromFormat : Read(v);
    }
}
