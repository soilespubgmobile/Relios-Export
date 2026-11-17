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
    /// <param name="onNot">
    /// Programın ayarlarda yaptığı her değişikliğin kısa açıklaması (kodlayıcının açamadığı ya da oynatıcıların
    /// çözemediği birleşimde en yakın çalışan ayar; ör. "AAC-LD yalnızca 22,05–48 kHz'te çalınabiliyor: 48 kHz"):
    /// baştakiler kodlama başlamadan, x264'ün açılışta reddettiği ayarın düzeltmesi olduğu anda. Satırın ipucuna yazılır.
    /// </param>
    public static async Task<string> ExportAsync(
        string input,
        string outputFolder,
        ExportProfile profile,
        ProfilAyarlari ayarlar,
        IProgress<double>? progress,
        Action<string>? onInfo,
        CancellationToken ct,
        Action<EsitlemeRaporu>? onEsitleme = null,
        Action<string>? onNot = null)
    {
        // Kodlama sürerken arayüzde ayar değişse bile bu video başladığı ayarlarla biter.
        var a = ayarlar.Kopya();
        MediaInfo media;
        try
        {
            media = await MediaProbe.ProbeAsync(Tools.Ffprobe, input, ct);
        }
        catch (FfmpegHatasi ex)
        {
            // Okunamayan video (bozuk, yarım kopyalanmış, silinmiş): okuma komutu ve çıktısı günlüğe yazılır.
            ex.GunlukDosyasi = HataAciklamasi.GunlukYaz(input, OkumaGunlugu(ex, input, profile, a));
            throw;
        }

        var ses = await ArgsBuilder.SesSecAsync(Tools.Ffprobe, a.Ses, media, input, ct);
        var ekler = new List<string>();
        foreach (var bastaki in Notlar(media, a, ses))
        {
            onNot?.Invoke(bastaki);
        }

        onInfo?.Invoke(Describe(media, a, ses));
        EsitlemeRaporu? rapor = null;
        SesOlcum? olcum = null;
        var duzeltme = new SesDuzeltme();
        if (ses.Tur != SesSecimTuru.Yok && (a.Ses.Esitleme || a.Ses.Sinirlayici))
        {
            // 1. geçiş: sesin yüksekliği ve gerçek tepesi ölçülür (yalnızca ses okunur, kısa sürer; kanal sayısı ve
            // örnekleme hızı çıktıdaki gibi). LUFS eşitleme açıksa karar: dokunulmaz ya da bütün ses aynı miktarda
            // (sabit kazanç) hedefe. Tepe koruması kaynağın tepesiyle başlar (sınırlayıcı açıksa ya da eşitleme sesi
            // değiştiriyorsa).
            onInfo?.Invoke(Describe(media, a, ses) + " · ses ölçülüyor");
            var cikti = new List<string>();
            // Ölçüm, denemeler ve kodlama aynı süreyle (aynı adlı harici ses videonun süresine tam getirilir).
            var sesSuresi = ArgsBuilder.SesSuresi(media, ses);
            var olcumKomutu = ArgsBuilder.OlcumKomutu(a.Ses, input, ses, sesSuresi);
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

            var olcumGunlugu = string.Join('\n', cikti);
            var kaynakOlcum = ArgsBuilder.OlcumOku(olcumGunlugu);
            var kaynakTepe = ArgsBuilder.TepeOku(olcumGunlugu)?.Db;
            var hedef = a.Ses.EsitlemeHedef;
            var tavan = a.Ses.SinirlayiciTavan;
            var kayipli = Esitleme.KayipliMi(a.Ses);
            var denemeDosyasi = kayipli
                ? Path.Combine(Path.GetTempPath(), "Relios Export", "deneme-" + Guid.NewGuid().ToString("N") + ".m4a")
                : null;

            // Deneme (yalnızca ses): ses kodlamadaki zincirle ölçülür; çıktının sesi AAC ise önce AAC'ye çevrilir
            // (ölçülen, çözülen sesinki: oynatıcıların ve platformların duyduğu). Ölçerin çıktısını döndürür.
            async Task<string> Dene(SesOlcum? o, SesDuzeltme d)
            {
                var denemeCikti = new List<string>();
                var denemeler = new List<List<string>>();
                if (denemeDosyasi is not null)
                {
                    denemeler.Add(ArgsBuilder.DenemeKodlamaKomutu(a.Ses, input, ses, sesSuresi, o, d.Telafi, denemeDosyasi,
                        d.TasmaPayi, d.Sinirla));
                    denemeler.Add(Esitleme.SonucOlcumKomutu(denemeDosyasi));
                }
                else
                {
                    denemeler.Add(ArgsBuilder.DenemeOlcumKomutu(a.Ses, input, ses, sesSuresi, o, d.Telafi, d.TasmaPayi, d.Sinirla));
                }

                try
                {
                    if (denemeDosyasi is not null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(denemeDosyasi)!);
                        await FfmpegRunner.RunAsync(denemeler[0], 0, null, ct);
                    }

                    await FfmpegRunner.RunAsync(denemeler[^1], 0, null, ct, denemeCikti);
                }
                catch (FfmpegHatasi ex)
                {
                    ex.GunlukDosyasi = HataAciklamasi.GunlukYaz(input, GunlukMetni(ex, input, profile, media, a, ses, rapor, denemeler));
                    throw;
                }

                return string.Join('\n', denemeCikti);
            }

            try
            {
                // Kararın ölçümü: sesin çıktıdaki hâli. Kaynağın ölçümü kodlamadan önceki sesinkidir; AAC'de çözülen ses
                // ondan kısık olabilir (AAC tizleri keser; FDK'nin DRC ayarlarıyla çözücü sesi kısıp sıkıştırır; çok
                // düşük kesim frekansı sesin çoğunu siler), sınırlayıcı (elle yükseltmede ya da tavanı aşan tepede)
                // yüksek sesi kısar. Ses o zaman bir kez kazançsız denenir (AAC'de kodlanıp çözülür), "zaten yüksek" /
                // "hedefte" kararı ve kazanç çıktının gerçek yüksekliğiyle verilir. WAV'da sınırlayıcı yoksa ölçüm
                // zaten çıktının sesidir (aynı kanallar, aynı hız). Tepe: kaynağınki (kodlayıcıya giren sesin tahmini).
                var ilk = new SesDuzeltme(Sinirla: a.Ses.Sinirlayici && kaynakTepe is { } t0 && t0 + Esitleme.TepePayi > tavan);
                string? ilkGunluk = null;
                if (a.Ses.Esitleme)
                {
                    var kararOlcum = kaynakOlcum;
                    if (kaynakOlcum is not null && (kayipli || ArgsBuilder.SinirlayiciVar(a.Ses, null, 0, 0, ilk.Sinirla)))
                    {
                        onInfo?.Invoke(Describe(media, a, ses) + " · sesin çıktıdaki yüksekliği ölçülüyor");
                        ilkGunluk = await Dene(null, ilk);
                        if (ArgsBuilder.OlcumOku(ilkGunluk) is { } cikis)
                        {
                            kararOlcum = kaynakOlcum with { Yukseklik = cikis.Yukseklik };
                        }
                    }

                    rapor = Esitleme.KararVer(a.Ses, kararOlcum) with { KaynakYukseklik = kaynakOlcum?.Yukseklik };
                    onInfo?.Invoke(Describe(media, a, ses, rapor));
                }

                // Ölçüm yalnızca ses yükseltilecek / kısılacaksa zincire girer (dokunulmayan sesin yüksekliği aynı kalır).
                var uygulandi = rapor is { Uygulandi: true };
                olcum = uygulandi ? rapor!.Olcum : null;

                // Tepe koruması: sınırlayıcı açıksa (ya da eşitleme sesi değiştiriyorsa: kutu kapalı olsa da) çıktının
                // gerçek tepesi tavanı aşmaz; AAC'de çözülen sesinki. AAC kodlaması ve örnekleme hızı dönüşümü tepeyi
                // biraz yükseltir (kullanıcının videolarında AAC 128 kb/s 0,1–1 dB): ses kodlanıp ölçülür, aşarsa
                // sınırlayıcı aşağı çekilip yeniden denenir. Tepesi tavanın altında kalan sese dokunulmaz (WAV'da bit bit
                // aynı).
                var koruma = a.Ses.Sinirlayici || uygulandi;
                double Kazanc(SesDuzeltme d) => uygulandi ? hedef - olcum!.Yukseklik + d.Telafi : 0;
                bool Sinirlayici(SesDuzeltme d) => ArgsBuilder.SinirlayiciVar(a.Ses, olcum, d.Telafi, d.TasmaPayi, d.Sinirla);
                // Yükseklik denemesi: sınırlayıcı tepeleri kıstıkça yükseklik hedefin altında kalır; AAC tizleri kestiği
                // için yükseklik biraz düşer (kullanıcının OBS kaydında 128 kb/s'de 0,16 LU): ölçülen eksik kadar telafi
                // edilir.
                bool YukseklikDenemesi(SesDuzeltme d) => uygulandi && (kayipli || Sinirlayici(d));

                // Kaynağın tepesi (kazançla) tavanı zaten aşıyorsa sınırlayıcı baştan zincirde (ses yükseltilmese de).
                if (koruma && kaynakTepe is { } kt && !Sinirlayici(duzeltme) && kt + Kazanc(duzeltme) + Esitleme.TepePayi > tavan)
                {
                    duzeltme = duzeltme with { Sinirla = true };
                }

                // Tepe denemesi gerekmez: ses sessiz ya da tavandan çok kısık (kodlama tepeyi en çok EnCokTasmaPayi kadar
                // yükseltse de tavanı aşmaz). FDK'nin DRC'si açıksa çözücü sesi yükseltebilir: her zaman denenir.
                var tepeDenemesi = koruma && kaynakTepe is { } kt2
                    && (kt2 + Kazanc(duzeltme) + Esitleme.EnCokTasmaPayi > tavan || Esitleme.DrcVar(a.Ses));
                double? ilkTepe = null;
                var tasma = 0.0;
                var sinir = false;
                var denendi = false;
                if (tepeDenemesi || YukseklikDenemesi(duzeltme))
                {
                    // Gerekirse telafi ve sınırlayıcının tavanı düzeltilip yeniden denenir; kodlama son denemenin
                    // ayarıyla yapılır.
                    for (var d = 0; d < Esitleme.EnCokDeneme; d++)
                    {
                        var yukseklikDenemesi = YukseklikDenemesi(duzeltme);
                        onInfo?.Invoke(Describe(media, a, ses, rapor) + (yukseklikDenemesi
                            ? (kayipli
                                ? (Sinirlayici(duzeltme) ? " · sınırlayıcının ve AAC'nin payı ölçülüyor" : " · AAC'nin payı ölçülüyor")
                                : " · sınırlayıcının payı ölçülüyor")
                            : (kayipli ? " · AAC'nin tepeye etkisi ölçülüyor" : " · tepe ölçülüyor")));

                        // Kazançsız ilk deneme kararın ölçümüyle aynı zincirse yeniden yapılmaz.
                        var denemeGunlugu = ilkGunluk is not null && olcum is null && duzeltme == ilk
                            ? ilkGunluk
                            : await Dene(olcum, duzeltme);
                        ilkGunluk = null;
                        denendi = true;
                        var tepe = tepeDenemesi ? ArgsBuilder.TepeOku(denemeGunlugu) : null;
                        double? yukseklik = null;
                        if (yukseklikDenemesi)
                        {
                            if (ArgsBuilder.OlcumOku(denemeGunlugu) is not { } olculen)
                            {
                                break;
                            }

                            yukseklik = olculen.Yukseklik;
                        }

                        if (tepeDenemesi && tepe is null)
                        {
                            break;
                        }

                        ilkTepe ??= tepe?.Db;
                        var giris = Esitleme.GirisTepesi(kaynakTepe, Kazanc(duzeltme), Sinirlayici(duzeltme), tavan - duzeltme.TasmaPayi);
                        var (yeni, sinirda, gorulen) = Esitleme.DenemeAdimi(
                            duzeltme, tasma, yukseklik, hedef, kayipli, tepe?.Db, tepe?.Hassas ?? false, tavan, giris);
                        tasma = gorulen;
                        if (yukseklikDenemesi)
                        {
                            sinir = sinirda;
                        }

                        if (yeni is null)
                        {
                            break;
                        }

                        duzeltme = yeni;
                        if (rapor is not null)
                        {
                            rapor = rapor with { Telafi = duzeltme.Telafi, TelafiSiniri = sinir, Kayipli = kayipli, TasmaPayi = duzeltme.TasmaPayi };
                        }
                    }
                }

                // Sınırlayıcıyı tepe koruması mı koydu (yoksa zincirde olmayacaktı: ses yükseltilmiyor ya da tepe aşmıyordu).
                var sinirlayici = Sinirlayici(duzeltme);
                var tepeZorla = sinirlayici && !ArgsBuilder.SinirlayiciVar(a.Ses, olcum, duzeltme.Telafi);
                if (rapor is null && (tepeZorla || duzeltme.TasmaPayi > 0))
                {
                    // Eşitleme kapalı: yalnızca tepe koruması çalıştı, satıra ve ipucuna yazılsın.
                    rapor = new EsitlemeRaporu(EsitlemeKarari.Kapali, kaynakOlcum, hedef, tavan);
                }

                if (rapor is not null)
                {
                    rapor = rapor with
                    {
                        Telafi = duzeltme.Telafi,
                        TelafiSiniri = sinir,
                        Kayipli = kayipli && denendi,
                        TasmaPayi = duzeltme.TasmaPayi,
                        Tasma = tasma,
                        TepeZorla = tepeZorla,
                        IlkTepe = ilkTepe,
                        SinirlayiciCalisti = sinirlayici,
                    };
                    onInfo?.Invoke(Describe(media, a, ses, rapor));
                }
            }
            finally
            {
                if (denemeDosyasi is not null)
                {
                    DeleteQuietly(denemeDosyasi);
                }
            }
        }

        Directory.CreateDirectory(outputFolder);
        var output = OutputPathFor(input, outputFolder, profile, ArgsBuilder.Uzanti(a));
        var temp = output + ".part" + ArgsBuilder.Uzanti(a);
        var gecisKlasoru = Path.Combine(Path.GetTempPath(), "Relios Export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gecisKlasoru);
        KomutPlani Plan() => ArgsBuilder.Olustur(a, media, input, ses, temp, Path.Combine(gecisKlasoru, "gecis"), olcum,
            duzeltme.Telafi, duzeltme.TasmaPayi, duzeltme.Sinirla);
        var plan = Plan();
        try
        {
            var n = plan.Gecisler.Count;
            var duzeltmeler = 0;
            for (var i = 0; i < n; i++)
            {
                var basla = (double)i / n;
                var ilerleme = progress is null ? null : new Progress<double>(p => progress.Report(basla + (p / n)));
                try
                {
                    await FfmpegRunner.RunAsync(plan.Gecisler[i], media.DurationSeconds, ilerleme, ct);
                }
                catch (FfmpegHatasi ex) when (duzeltmeler < EnCokX264Duzeltmesi)
                {
                    // x264 açılışta reddettiyse ve ayar düzeltilebiliyorsa (çok düşük 2 / 3 geçiş bit hızı, matrisle
                    // olanaksız QP sınırı): x264'ün söylediği değerle bu geçiş (ve sonrakiler) yeniden. Bellek yetmediyse
                    // (iş parçacığı / önden bakış azaltıldı) bütün geçişler baştan: geçişler aynı ayarlarla kodlanmalı.
                    if (ArgsBuilder.X264Duzelt(ex.Gunluk, a) is not { } duzeltmeNotu)
                    {
                        throw;
                    }

                    duzeltmeler++;
                    ekler.Add(duzeltmeNotu);
                    onNot?.Invoke(duzeltmeNotu);
                    onInfo?.Invoke(Describe(media, a, ses, rapor, ekler));
                    plan = Plan();
                    i = ArgsBuilder.BellekYetmedi(ex.Gunluk) ? -1 : i - 1;
                }
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

        if (rapor is { Uygulandi: true } or { TepeZorla: true } or { TasmaPayi: > 0 })
        {
            // Ses değiştirildiyse (eşitleme ya da tepe koruması) sonucu: çıktının sesi bir kez daha ölçülür (yalnızca ses
            // okunur). Ölçülemese de video hazırdır.
            onInfo?.Invoke(Describe(media, a, ses, rapor, ekler) + " · çıktının sesi ölçülüyor");
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

    /// <summary>x264'ün açılışta reddettiği ayar en çok bu kadar kez düzeltilip yeniden denenir.</summary>
    public const int EnCokX264Duzeltmesi = 8;

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

    /// <summary>Video okunamadığında (kodlamadan önce) günlük: okuma komutu ve ffprobe çıktısı.</summary>
    private static string OkumaGunlugu(FfmpegHatasi ex, string input, ExportProfile profile, ProfilAyarlari a)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Relios Export · hata günlüğü · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.AppendLine("Kaynak : " + input);
        sb.AppendLine("Profil : " + profile.DisplayName);
        sb.AppendLine("Açıklama: " + ex.Message);
        sb.AppendLine();
        sb.AppendLine($"Komut 1/1: \"{Tools.Ffprobe}\" " + ArgsBuilder.KomutMetni(MediaProbe.ProbeArgumanlari(input)));
        sb.AppendLine();
        sb.AppendLine("----- ffprobe çıktısı -----");
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

    /// <summary>
    /// Programın bu kaynakta ayarlarda yaptığı değişiklikler (kodlayıcının açamadığı ya da oynatıcıların çözemediği
    /// birleşimde en yakın çalışan ayar), görüntü önce: ör. "Main profilde kayıpsız yok: CRF 1", "geçmeli 4:2:0'da
    /// yükseklik 4'ün katı: 1350 → 1348", "AAC-LD yalnızca 22,05–48 kHz'te çalınabiliyor: 48 kHz".
    /// </summary>
    public static List<string> Notlar(MediaInfo media, ProfilAyarlari a, SesSecimi ses)
    {
        var notlar = new List<string>();
        if (ArgsBuilder.X264KayipsizNotu(a, media) is { } kayipsiz)
        {
            // Kayıpsız yalnızca High 4:4:4'te; profil her ayarın önüne geçer.
            notlar.Add(kayipsiz);
        }

        if (ArgsBuilder.YukseklikNotu(a, media) is { } yukseklik)
        {
            notlar.Add(yukseklik);
        }

        if (ArgsBuilder.FdkKarari(a.Ses, ses)?.Not is { } fdk)
        {
            // Ör. "FDK AAC LC 48 kHz stereo sesi en çok 576 kb/s kodlar", "HE-AAC v2 yalnızca stereo sesi kodlar: ...".
            notlar.Add(fdk);
        }

        return notlar;
    }

    /// <summary>
    /// Kaynağın kısa Türkçe özeti: çözünürlük, kare hızı, kırpma, hangi sesin kullanılacağı ve programın ayarlarda yaptığı
    /// değişiklikler (<see cref="Notlar"/>; <paramref name="ekler"/>: kodlarken yapılanlar, ör. x264'ün düzeltmesi).
    /// </summary>
    public static string Describe(
        MediaInfo media, ProfilAyarlari a, SesSecimi ses, EsitlemeRaporu? esitleme = null, IReadOnlyList<string>? ekler = null)
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

        if (ArgsBuilder.X264KayipsizNotu(a, media) is { } kayipsiz)
        {
            // Ör. "Main profilde kayıpsız yok: CRF 1" (kayıpsız yalnızca High 4:4:4'te; profil her ayarın önüne geçer).
            text += " · " + kayipsiz;
        }

        if (ArgsBuilder.YukseklikNotu(a, media) is { } yukseklik)
        {
            text += " · " + yukseklik;
        }

        foreach (var ek in ekler ?? [])
        {
            text += " · " + ek;
        }

        text += " · Ses: " + ses.Aciklama;
        if (ArgsBuilder.FdkKarari(a.Ses, ses)?.Not is { } fdk)
        {
            // Ör. "FDK AAC LC 48 kHz stereo sesi en çok 576 kb/s kodlar", "HE-AAC v2 yalnızca stereo sesi kodlar: ...".
            text += " · " + fdk;
        }

        if (esitleme is not null)
        {
            // Ör. "-21,3 → -14 LUFS (yalnızca yükseltilir)", "-9,2 LUFS · zaten yüksek, dokunulmuyor"
            text += " · " + esitleme.BilgiMetni;
        }

        return text;
    }
}
