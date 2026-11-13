using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ReliosExport.Core;

public static class Tools
{
    /// <summary>ffmpeg.exe ve ffprobe.exe programın yanındaki "ffmpeg" klasöründe aranır.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    public static string Ffmpeg => Path.Combine(Folder, "ffmpeg.exe");

    public static string Ffprobe => Path.Combine(Folder, "ffprobe.exe");

    public static bool Available => File.Exists(Ffmpeg) && File.Exists(Ffprobe);

    public static string? MissingMessage()
    {
        if (!File.Exists(Ffmpeg) || !File.Exists(Ffprobe))
        {
            return $"FFmpeg bulunamadı. ffmpeg.exe ve ffprobe.exe şu klasörde olmalı:\n{Folder}";
        }

        return null;
    }
}

public sealed class FfmpegRunner
{
    /// <summary>
    /// ffmpeg'i çalıştırır. <paramref name="onProgress"/> 0..1 arası ilerleme bildirir.
    /// Başarısız olursa ffmpeg'in son hata satırlarıyla birlikte istisna fırlatır.
    /// </summary>
    public static async Task RunAsync(
        IReadOnlyList<string> args,
        double durationSeconds,
        IProgress<double>? onProgress,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Tools.Ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var errTail = new Queue<string>();
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (errTail)
            {
                errTail.Enqueue(e.Data);
                while (errTail.Count > 30)
                {
                    errTail.Dequeue();
                }
            }
        };
        p.OutputDataReceived += (_, e) =>
        {
            // -progress pipe:1 → "out_time_us=12345678"
            if (e.Data is { } line
                && line.StartsWith("out_time_us=", StringComparison.Ordinal)
                && durationSeconds > 0
                && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
            {
                onProgress?.Report(Math.Clamp(us / 1_000_000.0 / durationSeconds, 0, 1));
            }
        };

        if (!p.Start())
        {
            throw new InvalidOperationException("ffmpeg başlatılamadı.");
        }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // zaten kapanmış
            }

            throw;
        }

        if (p.ExitCode != 0)
        {
            string tail;
            lock (errTail)
            {
                tail = string.Join(Environment.NewLine, errTail);
            }

            throw new InvalidOperationException($"ffmpeg hata verdi (kod {p.ExitCode}):\n{tail}");
        }

        onProgress?.Report(1);
    }
}

/// <summary>Tek bir videoyu seçilen profile göre dışa aktarır.</summary>
public static class Exporter
{
    public static string OutputPathFor(string input, string outputFolder, ExportProfile profile)
    {
        var name = Path.GetFileNameWithoutExtension(input) + profile.FileSuffix;
        var path = Path.Combine(outputFolder, name + ".mp4");
        var i = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(outputFolder, $"{name}_{i}.mp4");
            i++;
        }

        return path;
    }

    /// <param name="onInfo">Kaynak incelendikten sonra kısa bilgi verir (ör. "1920×1080 · 60 fps · 9:16 kırpılıyor").</param>
    public static async Task<string> ExportAsync(
        string input,
        string outputFolder,
        ExportProfile profile,
        IProgress<double>? progress,
        Action<string>? onInfo,
        CancellationToken ct)
    {
        if (!profile.IsReady)
        {
            throw new NotSupportedException($"{profile.DisplayName} ayarları henüz belirlenmedi.");
        }

        var media = await MediaProbe.ProbeAsync(Tools.Ffprobe, input, ct);
        onInfo?.Invoke(Describe(media));
        Directory.CreateDirectory(outputFolder);
        var output = OutputPathFor(input, outputFolder, profile);
        var temp = output + ".part.mp4";
        var args = ArgsBuilder.Build(profile, media, input, temp);
        try
        {
            await FfmpegRunner.RunAsync(args, media.DurationSeconds, progress, ct);
            File.Move(temp, output, overwrite: false);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return output;
    }

    /// <summary>Kaynağın kısa Türkçe özeti: çözünürlük, kare hızı ve 9:16 kırpma gerekip gerekmediği.</summary>
    public static string Describe(MediaInfo media)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var text = $"{media.DisplayWidth}×{media.DisplayHeight}";
        if (media.FrameRate > 0)
        {
            text += " · " + media.FrameRate.ToString("0.##", tr) + " fps";
        }

        if (ArgsBuilder.PlanCrop916(media).IsNeeded)
        {
            text += " · 9:16 kırpılıyor";
        }

        if (media.IsHdr)
        {
            // Ton eşleme yapılmaz; HDR kaynak SDR çıktıda soluk görünebilir.
            text += " · HDR kaynak";
        }

        return text;
    }
}
