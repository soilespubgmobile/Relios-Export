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
        CancellationToken ct,
        List<string>? tumCikti = null)
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
                tumCikti?.Add(e.Data);
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
    /// <param name="onEsitleme">LUFS eşitleme açıksa, bitince eşitlemenin raporu (ölçüm, karar, çıktının ölçülen yüksekliği).</param>
    public static async Task<string> ExportAsync(
        string input,
        string outputFolder,
        ExportProfile profile,
        ProfilAyarlari ayarlar,
        IProgress<double>? progress,
        Action<string>? onInfo,
        CancellationToken ct,
        Action<EsitlemeRaporu>? onEsitleme = null)
    {
        // Kodlama sürerken arayüzde ayar değişse bile bu video başladığı ayarlarla biter.
        var a = ayarlar.Kopya();
        var media = await MediaProbe.ProbeAsync(Tools.Ffprobe, input, ct);
        var ses = await ArgsBuilder.SesSecAsync(Tools.Ffprobe, a.Ses, media, input, ct);
        onInfo?.Invoke(Describe(media, a, ses));
        EsitlemeRaporu? rapor = null;
        if (a.Ses.Esitleme && ses.Tur != SesSecimTuru.Yok)
        {
            // LUFS eşitleme, 1. geçiş: sesin yüksekliği ölçülür (yalnızca ses okunur, kısa sürer); sonra karar:
            // dokunulmaz ya da bütün ses aynı miktarda (sabit kazanç) hedefe.
            onInfo?.Invoke(Describe(media, a, ses) + " · ses ölçülüyor");
            var cikti = new List<string>();
            var olcumKomutu = ArgsBuilder.OlcumKomutu(a.Ses, input, ses, media.DurationSeconds);
            try
            {
                await FfmpegRunner.RunAsync(olcumKomutu, 0, null, ct, cikti);
            }
            catch (FfmpegHatasi ex)
            {
                // Ölçüm de hata günlüğüne yazılır (satırdaki "Günlük" düğmesi açar).
                ex.GunlukDosyasi = HataAciklamasi.GunlukYaz(input, GunlukMetni(ex, input, profile, media, a, ses, null, [olcumKomutu]));
                throw;
            }

            rapor = Esitleme.KararVer(a.Ses, ArgsBuilder.OlcumOku(string.Join('\n', cikti)));
            onInfo?.Invoke(Describe(media, a, ses, rapor));

            if (rapor is { Sinirli: true, Olcum: { } o })
            {
                // Kazanç tepeyi tavanın üstüne çıkarıyor: tepeleri sınırlayıcı tutar, kıstığı kadar yükseklik eksik kalır.
                // Kodlamadaki ses zinciriyle deneme ölçümü yapılır (yalnızca ses), eksik kadar telafi eklenir.
                var telafi = 0.0;
                var sinir = false;
                for (var d = 0; d < Esitleme.EnCokDeneme; d++)
                {
                    onInfo?.Invoke(Describe(media, a, ses, rapor) + " · sınırlayıcının payı ölçülüyor");
                    var denemeCikti = new List<string>();
                    var deneme = ArgsBuilder.DenemeOlcumKomutu(a.Ses, input, ses, media.DurationSeconds, o, telafi);
                    try
                    {
                        await FfmpegRunner.RunAsync(deneme, 0, null, ct, denemeCikti);
                    }
                    catch (FfmpegHatasi ex)
                    {
                        ex.GunlukDosyasi = HataAciklamasi.GunlukYaz(input, GunlukMetni(ex, input, profile, media, a, ses, rapor, [deneme]));
                        throw;
                    }

                    if (ArgsBuilder.OlcumOku(string.Join('\n', denemeCikti)) is not { } olculen)
                    {
                        break;
                    }

                    var (yeni, sinirda) = Esitleme.TelafiAdimi(telafi, olculen.Yukseklik, a.Ses.EsitlemeHedef);
                    sinir = sinirda;
                    if (yeni is not { } t)
                    {
                        break;
                    }

                    telafi = t;
                    rapor = rapor with { Telafi = telafi, TelafiSiniri = sinir };
                }

                rapor = rapor with { Telafi = telafi, TelafiSiniri = sinir };
                onInfo?.Invoke(Describe(media, a, ses, rapor));
            }
        }

        // Ölçüm yalnızca ses değiştirilecekse komuta girer (dokunulmayan ses olduğu gibi kalır).
        var olcum = rapor is { Uygulandi: true } ? rapor.Olcum : null;

        Directory.CreateDirectory(outputFolder);
        var output = OutputPathFor(input, outputFolder, profile, ArgsBuilder.Uzanti(a));
        var temp = output + ".part" + ArgsBuilder.Uzanti(a);
        var gecisKlasoru = Path.Combine(Path.GetTempPath(), "Relios Export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gecisKlasoru);
        var plan = ArgsBuilder.Olustur(a, media, input, ses, temp, Path.Combine(gecisKlasoru, "gecis"), olcum, rapor?.Telafi ?? 0);
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
            ex.GunlukDosyasi = HataAciklamasi.GunlukYaz(input, GunlukMetni(ex, input, profile, media, a, ses, rapor, plan.Gecisler));
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

        if (rapor is { Uygulandi: true })
        {
            // Eşitlemenin sonucu: çıktının sesi bir kez daha ölçülür (yalnızca ses okunur). Ölçülemese de video hazırdır.
            onInfo?.Invoke(Describe(media, a, ses, rapor) + " · çıktının sesi ölçülüyor");
            try
            {
                var cikti = new List<string>();
                await FfmpegRunner.RunAsync(Esitleme.SonucOlcumKomutu(output), 0, null, ct, cikti);
                if (ArgsBuilder.OlcumOku(string.Join('\n', cikti)) is { } sonuc)
                {
                    rapor = rapor with { SonucYukseklik = sonuc.Yukseklik, SonucTepe = sonuc.Tepe };
                }
            }
            catch (Exception ex) when (ex is FfmpegHatasi or OperationCanceledException or InvalidOperationException
                                           or System.ComponentModel.Win32Exception)
            {
                // Sonuç yazılmaz; çıktı dosyası tamamdır.
            }
        }

        if (rapor is not null)
        {
            onEsitleme?.Invoke(rapor);
        }

        return output;
    }

    /// <summary>Hata günlüğünün içeriği: kaynak, bilgi, açıklama, komutlar, ffmpeg çıktısı ve ayarlar.</summary>
    private static string GunlukMetni(
        FfmpegHatasi ex,
        string input,
        ExportProfile profile,
        MediaInfo media,
        ProfilAyarlari a,
        SesSecimi ses,
        EsitlemeRaporu? rapor,
        IReadOnlyList<IReadOnlyList<string>> komutlar)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Relios Export · hata günlüğü · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.AppendLine("Kaynak : " + input);
        sb.AppendLine("Profil : " + profile.DisplayName);
        sb.AppendLine("Bilgi  : " + Describe(media, a, ses, rapor));
        sb.AppendLine("Açıklama: " + ex.Message);
        sb.AppendLine();
        for (var i = 0; i < komutlar.Count; i++)
        {
            sb.AppendLine($"Komut {i + 1}/{komutlar.Count}: \"{Tools.Ffmpeg}\" " + ArgsBuilder.KomutMetni(komutlar[i]));
        }

        sb.AppendLine();
        sb.AppendLine("----- ffmpeg çıktısı -----");
        sb.AppendLine(ex.Gunluk);
        sb.AppendLine();
        sb.AppendLine("----- ayarlar -----");
        sb.AppendLine(AyarDosyasi.Yaz(a));
        return sb.ToString();
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
    public static string Describe(MediaInfo media, ProfilAyarlari a, SesSecimi ses, EsitlemeRaporu? esitleme = null)
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

        var nvenc = a.Video.Kodlayici == VideoKodlayici.Nvenc;
        if ((nvenc ? a.Video.Nvenc.Seviye : a.Video.X264.Seviye) == ArgsBuilder.Kaynak)
        {
            // Kaynağın seviyesi çıktının bit hızına / tamponuna / boyutuna yetmiyorsa yükseltilir (NVENC aşan
            // seviyeyle kodlamayı hiç başlatmaz; x264 başlatır ama dosya yazdığı seviyeye uymazdı).
            var kaynakSeviye = ArgsBuilder.SeviyeCoz(ArgsBuilder.Kaynak, media);
            var seviye = nvenc ? ArgsBuilder.NvencSeviyesi(a, media) : ArgsBuilder.X264Seviyesi(a, media);
            if (kaynakSeviye.Length > 0 && seviye != kaynakSeviye)
            {
                text += $" · seviye {kaynakSeviye} → {seviye}";
            }
        }

        text += " · Ses: " + ses.Aciklama;
        if (esitleme is not null)
        {
            // Ör. "-21,3 → -14 LUFS (yalnızca yükseltilir)", "-9,2 LUFS · zaten yüksek, dokunulmuyor"
            text += " · " + esitleme.BilgiMetni;
        }

        return text;
    }
}
