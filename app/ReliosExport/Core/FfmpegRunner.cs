using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Core;

public static class Tools
{
    /// <summary>
    /// ffmpeg.exe ve ffprobe.exe'nin klasörü. Tek exe sürümünde ikisi programın içine gömülüdür;
    /// .NET ilk açılışta onları geçici klasöre açar (AppContext.BaseDirectory\ffmpeg) ve sonraki
    /// açılışlarda aynısını kullanır. Gömülü değilse programın yanındaki "ffmpeg" klasörüne bakılır.
    /// </summary>
    public static string Folder { get; } = FindFolder();

    private static string FindFolder()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        if (File.Exists(Path.Combine(bundled, "ffmpeg.exe")) && File.Exists(Path.Combine(bundled, "ffprobe.exe")))
        {
            return bundled;
        }

        return Path.Combine(AppPaths.AppFolder, "ffmpeg");
    }

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

        // Hata günlüğü için ffmpeg'in çıktısı: ilk 200 satır (ayarlar, kodlayıcı açılışı) ve son 400 satır.
        var errHead = new List<string>();
        var errTail = new Queue<string>();
        var atlanan = 0;
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (errTail)
            {
                if (errHead.Count < 200)
                {
                    errHead.Add(e.Data);
                    return;
                }

                errTail.Enqueue(e.Data);
                if (errTail.Count > 400)
                {
                    errTail.Dequeue();
                    atlanan++;
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
                // Kill hemen döner; ffmpeg gerçekten kapanmadan yarım dosya silinemez.
                p.WaitForExit(10_000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // zaten kapanmış
            }

            throw;
        }

        if (p.ExitCode != 0)
        {
            string gunluk;
            List<string> son;
            lock (errTail)
            {
                var satirlar = new List<string>(errHead);
                if (atlanan > 0)
                {
                    satirlar.Add($"... ({atlanan} satır atlandı) ...");
                }

                satirlar.AddRange(errTail);
                gunluk = string.Join(Environment.NewLine, satirlar);
                son = errHead.Concat(errTail).Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(8).ToList();
            }

            var aciklama = HataAciklamasi.Bul(gunluk) ?? $"ffmpeg hata verdi (kod {p.ExitCode}).";
            throw new FfmpegHatasi(aciklama + Environment.NewLine + string.Join(Environment.NewLine, son), gunluk);
        }

        onProgress?.Report(1);
    }
}

/// <summary>Tek bir videoyu seçilen profilin ayarlarına göre dışa aktarır.</summary>
public static class Exporter
{
    public static string OutputPathFor(string input, string outputFolder, ExportProfile profile, string extension = ".mp4")
    {
        var name = Path.GetFileNameWithoutExtension(input) + profile.FileSuffix;
        var path = Path.Combine(outputFolder, name + extension);
        var i = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(outputFolder, $"{name}_{i}{extension}");
            i++;
        }

        return path;
    }

    /// <param name="onInfo">Kaynak incelendikten sonra kısa bilgi verir (ör. "1920×1080 · 60 fps · 9:16 kırpılıyor").</param>
    public static async Task<string> ExportAsync(
        string input,
        string outputFolder,
        ExportProfile profile,
        ProfilAyarlari ayarlar,
        IProgress<double>? progress,
        Action<string>? onInfo,
        CancellationToken ct)
    {
        // Kodlama sürerken arayüzde ayar değişse bile bu video başladığı ayarlarla biter.
        var a = ayarlar.Kopya();
        var media = await MediaProbe.ProbeAsync(Tools.Ffprobe, input, ct);
        var ses = await ArgsBuilder.SesSecAsync(Tools.Ffprobe, a.Ses, media, input, ct);
        onInfo?.Invoke(Describe(media, a, ses));
        Directory.CreateDirectory(outputFolder);
        var output = OutputPathFor(input, outputFolder, profile, ArgsBuilder.Uzanti(a));
        var temp = output + ".part" + ArgsBuilder.Uzanti(a);
        var gecisKlasoru = Path.Combine(Path.GetTempPath(), "Relios Export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gecisKlasoru);
        var plan = ArgsBuilder.Olustur(a, media, input, ses, temp, Path.Combine(gecisKlasoru, "gecis"));
        try
        {
            var n = plan.Gecisler.Count;
            for (var i = 0; i < n; i++)
            {
                var basla = (double)i / n;
                var ilerleme = progress is null ? null : new Progress<double>(p => progress.Report(basla + (p / n)));
                await FfmpegRunner.RunAsync(plan.Gecisler[i], media.DurationSeconds, ilerleme, ct);
            }

            File.Move(temp, output, overwrite: false);
            progress?.Report(1);
        }
        catch (FfmpegHatasi ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Relios Export · hata günlüğü · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Kaynak : " + input);
            sb.AppendLine("Profil : " + profile.DisplayName);
            sb.AppendLine("Bilgi  : " + Describe(media, a, ses));
            sb.AppendLine("Açıklama: " + ex.Message);
            sb.AppendLine();
            for (var i = 0; i < plan.Gecisler.Count; i++)
            {
                sb.AppendLine($"Komut {i + 1}/{plan.Gecisler.Count}: \"{Tools.Ffmpeg}\" " + ArgsBuilder.KomutMetni(plan.Gecisler[i]));
            }

            sb.AppendLine();
            sb.AppendLine("----- ffmpeg çıktısı -----");
            sb.AppendLine(ex.Gunluk);
            sb.AppendLine();
            sb.AppendLine("----- ayarlar -----");
            sb.AppendLine(AyarDosyasi.Yaz(a));
            ex.GunlukDosyasi = HataAciklamasi.GunlukYaz(input, sb.ToString());
            throw;
        }
        finally
        {
            DeleteQuietly(temp);
            try
            {
                Directory.Delete(gecisKlasoru, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Geçici klasör sonra silinir.
            }
        }

        return output;
    }

    /// <summary>
    /// Yarım kalan geçici dosyayı siler. Dosya bir an kilitli kalırsa birkaç kez dener;
    /// silinemese bile asıl sonucu (iptal / hata) gizlemez.
    /// </summary>
    private static void DeleteQuietly(string path)
    {
        for (var i = 0; i < 10 && File.Exists(path); i++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>Kaynağın kısa Türkçe özeti: çözünürlük, kare hızı, kırpma ve hangi sesin kullanılacağı.</summary>
    public static string Describe(MediaInfo media, ProfilAyarlari a, SesSecimi ses)
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var text = $"{media.DisplayWidth}×{media.DisplayHeight}";
        if (media.FrameRate > 0)
        {
            text += " · " + media.FrameRate.ToString("0.##", tr) + " fps";
        }

        if (ArgsBuilder.KirpilacakMi(a, media))
        {
            text += " · kırpılıyor";
        }

        if (media.IsHdr)
        {
            // Ton eşleme yapılmaz; HDR kaynak SDR çıktıda soluk görünebilir.
            text += " · HDR kaynak";
        }

        if (a.AnahtarKare.Birim != AralikBirimi.Kare)
        {
            // Kare hızına göre hesaplanan anahtar kare aralığı (ör. 60 fps'te GOP 100 / 51).
            var (g, m) = AnahtarKareHesabi.Aralik(a.AnahtarKare, ArgsBuilder.CiktiKareHizi(a, media));
            if (g is { } enBuyuk)
            {
                text += " · GOP " + enBuyuk + (m is { } enKucuk ? " / " + enKucuk : string.Empty);
            }
        }

        if (a.Video.Kodlayici == VideoKodlayici.Nvenc && a.Video.Nvenc.Seviye == ArgsBuilder.Kaynak)
        {
            // Kaynağın seviyesi bit hızına / boyuta yetmiyorsa NVENC için yükseltilir (yoksa kodlama başlamaz).
            var kaynakSeviye = ArgsBuilder.SeviyeCoz(ArgsBuilder.Kaynak, media);
            var seviye = ArgsBuilder.NvencSeviyesi(a, media);
            if (kaynakSeviye.Length > 0 && seviye != kaynakSeviye)
            {
                text += $" · seviye {kaynakSeviye} → {seviye}";
            }
        }

        text += " · Ses: " + ses.Aciklama;
        return text;
    }
}
