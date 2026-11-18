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
    /// ffmpeg'i çalıştırır. <paramref name="onProgress"/> 0..1 arası ilerleme bildirir: <paramref name="toplamKare"/>
    /// biliniyorsa kodlanan kare sayısından, değilse ffmpeg'in bildirdiği zamandan (geri gitmez). ffmpeg'in zamanı
    /// okunan yeri gösterir: x264 önden bakış ve B-kareler yüzünden kareleri geriden kodlar (TikTok ayarında ~266 kare),
    /// okuma bitince zaman bildirilmez; zamana göre ilerleme 30 saniyelik videoda %85'te duruyor, kalan kareler
    /// kodlanınca birden bitiyordu (GitHub'da ölçüldü). Başarısız olursa ffmpeg'in son hata satırlarıyla birlikte
    /// istisna fırlatır.
    /// </summary>
    public static async Task RunAsync(
        IReadOnlyList<string> args,
        double durationSeconds,
        IProgress<double>? onProgress,
        CancellationToken ct,
        List<string>? tumCikti = null,
        double toplamKare = 0)
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
        var enCok = 0.0;
        void Bildir(double oran)
        {
            // Kodlama bitmeden %100 gösterilmez; ilerleme geri gitmez.
            oran = Math.Clamp(oran, 0, 0.995);
            if (oran > enCok)
            {
                enCok = oran;
                onProgress?.Report(oran);
            }
        }

        p.OutputDataReceived += (_, e) =>
        {
            // -progress pipe:1 → "frame=123" (kodlanan görüntü karesi), "out_time_us=12345678" (okunan yer).
            if (e.Data is not { } line)
            {
                return;
            }

            if (toplamKare > 0)
            {
                if (line.StartsWith("frame=", StringComparison.Ordinal)
                    && long.TryParse(line.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var kare))
                {
                    Bildir(kare / toplamKare);
                }
            }
            else if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
                     && durationSeconds > 0
                     && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
            {
                Bildir(us / 1_000_000.0 / durationSeconds);
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

            var aciklama = HataAciklamasi.Bul(gunluk)
                           ?? (HataAciklamasi.CokmeMi(p.ExitCode) ? HataAciklamasi.CokmeAciklamasi(p.ExitCode) : $"ffmpeg hata verdi (kod {p.ExitCode}).");
            throw new FfmpegHatasi(aciklama + Environment.NewLine + string.Join(Environment.NewLine, son), gunluk, p.ExitCode);
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

    /// <summary>
    /// Kodlama sürerken yazılan yarım dosyanın adı: "&lt;çıktı&gt;.&lt;işlem numarası&gt;.part&lt;uzantı&gt;". Ad, programın
    /// çalışan kopyasına özeldir (işlem numarası çalışan işlemler arasında tektir; bir kopya aynı anda tek video işler):
    /// başka bir kopya aynı anda aynı adlı videoyu aynı klasöre aktarsa da iki FFmpeg aynı dosyaya yazamaz. 0.16.0'a
    /// kadar ad "&lt;çıktı&gt;.part&lt;uzantı&gt;" idi: iki kopya aynı yarım dosyaya yazıp çıktıyı bozabiliyordu.
    /// </summary>
    public static string YarimDosyaAdi(string output, string uzanti) =>
        string.Create(CultureInfo.InvariantCulture, $"{output}.{Environment.ProcessId:x}.part{uzanti}");

    /// <summary>
    /// Biten yarım dosyayı çıktının adına taşır ve taşındığı adı döndürür. Çıktının adı dışa aktarmanın başında seçilir;
    /// kodlama sürerken o adla bir dosya oluştuysa (ör. programın başka bir kopyası aynı adlı videoyu önce bitirdi)
    /// üzerine yazılmaz, emek de boşa gitmez: sıradaki boş ad (<paramref name="sonrakiAd"/>: "_2", "_3" …) denenir.
    /// Dosya başka bir nedenle taşınamıyorsa hata olduğu gibi çıkar.
    /// </summary>
    public static string Yerlestir(string temp, string output, Func<string> sonrakiAd)
    {
        for (var deneme = 0; ; deneme++)
        {
            try
            {
                File.Move(temp, output, overwrite: false);
                return output;
            }
            catch (IOException) when (deneme < 100 && File.Exists(output) && File.Exists(temp))
            {
                output = sonrakiAd();
            }
        }
    }

    /// <summary>
    /// Tanı için (programın kendisi kullanmaz; deneme aracı yazar): sesin her denemesinin ayarı ve ölçümü, ölçümün
    /// yinelenmesi.
    /// </summary>
    public static Action<string>? DenemeIzi { get; set; }

    /// <summary>
    /// Ölçer komutunu (<see cref="Esitleme.OlcumFiltresi"/> ile biten) çalıştırır, yüksekliği ve gerçek tepeyi okur.
    /// FFmpeg'in ölçeri +10 LUFS'un üstündeki blokları +10 sayar (<see cref="Esitleme.OlcerUstSiniri"/>): ses o kadar
    /// yüksek olabilecekse (gerçek tepe ve kanal sayısıyla, <see cref="Esitleme.OlcumKisilmasi"/>) ölçüm ses kısılarak
    /// yinelenir; yükseklik ve aralık ondan (kısılan kadar eklenir), gerçek tepe ilk ölçümden (0,001 hassasiyetle).
    /// <paramref name="kanal"/>: ölçülen sesin kanal sayısı (en çok).
    /// </summary>
    private static async Task<(SesOlcum? Olcum, (double Db, bool Hassas)? Tepe)> Olc(
        IReadOnlyList<string> komut, int kanal, CancellationToken ct)
    {
        var cikti = new List<string>();
        await FfmpegRunner.RunAsync(komut, 0, null, ct, cikti);
        var gunluk = string.Join('\n', cikti);
        var olcum = ArgsBuilder.OlcumOku(gunluk);
        var tepe = ArgsBuilder.TepeOku(gunluk);
        if (tepe is { } t && Esitleme.OlcumKisilmasi(t.Db, kanal) is var kis && kis > 0)
        {
            var ikinci = new List<string>();
            await FfmpegRunner.RunAsync(ArgsBuilder.OlcumuKis(komut, kis), 0, null, ct, ikinci);
            var kisik = ArgsBuilder.OlcumOku(string.Join('\n', ikinci));
            DenemeIzi?.Invoke(string.Create(CultureInfo.InvariantCulture,
                $"OLCUM_KISILDI={kis} ILK={olcum?.Yukseklik.ToString("0.000", CultureInfo.InvariantCulture) ?? "-"} KISIK={kisik?.Yukseklik.ToString("0.000", CultureInfo.InvariantCulture) ?? "-"}"));
            olcum = kisik is null ? olcum : kisik with { Yukseklik = kisik.Yukseklik + kis, Tepe = t.Db, TepeHassas = t.Hassas };
        }

        return (olcum, tepe);
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
            // Ölçüm, denemeler ve kodlama aynı süreyle (aynı adlı harici ses videonun süresine tam getirilir).
            var sesSuresi = ArgsBuilder.SesSuresi(media, ses);
            var olcumKomutu = ArgsBuilder.OlcumKomutu(a.Ses, input, ses, sesSuresi);

            // Ölçülen sesin kanal sayısı (en çok): ölçerin üst sınırı için.
            var olcumKanali = ArgsBuilder.OlcumKanali(a.Ses, ses);
            (SesOlcum? Olcum, (double Db, bool Hassas)? Tepe) kaynak;
            try
            {
                kaynak = await Olc(olcumKomutu, olcumKanali, ct);
            }
            catch (FfmpegHatasi ex)
            {
                // Ölçüm de hata günlüğüne yazılır (satırdaki "Günlük" düğmesi açar).
                ex.GunlukDosyasi = HataAciklamasi.GunlukYaz(input, GunlukMetni(ex, input, profile, media, a, ses, null, [olcumKomutu]));
                throw;
            }

            // Seviye (elle kazanç) ölçümün zincirine girmez, ölçülene eklenir: kayan noktalı zincirde kazanç doğrusaldır;
            // ölçer +10 LUFS'un üstünü saymaz, -70'in altını sessiz sayar (+30 dB'deki 10 kanallı ses 2,5 LU kısık,
            // -60 dB'deki ses sessiz ölçülüyordu).
            var seviye = a.Ses.Seviye;
            var kaynakOlcum = kaynak.Olcum is { } ko ? ko with { Yukseklik = ko.Yukseklik + seviye, Tepe = ko.Tepe + seviye } : null;
            var kaynakTepe = kaynak.Tepe?.Db + seviye;
            var hedef = a.Ses.EsitlemeHedef;
            var tavan = a.Ses.SinirlayiciTavan;
            var kayipli = Esitleme.KayipliMi(a.Ses);
            var denemeDosyasi = kayipli
                ? Path.Combine(Path.GetTempPath(), "Relios Export", "deneme-" + Guid.NewGuid().ToString("N") + ".m4a")
                : null;

            // Deneme (yalnızca ses): ses kodlamadaki zincirle ölçülür; çıktının sesi AAC ise önce AAC'ye çevrilir
            // (ölçülen, çözülen sesinki: oynatıcıların ve platformların duyduğu). Ölçülen yüksekliği ve gerçek tepeyi
            // döndürür.
            async Task<(SesOlcum? Olcum, (double Db, bool Hassas)? Tepe)> Dene(SesOlcum? o, SesDuzeltme d)
            {
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

                    var sonuc = await Olc(denemeler[^1], olcumKanali, ct);
                    DenemeIzi?.Invoke(string.Create(CultureInfo.InvariantCulture,
                        $"DENEME telafi={d.Telafi:0.00} pay={d.TasmaPayi:0.00} sinirla={d.Sinirla} olcum={(o is null ? "yok" : "var")}"
                        + $" yukseklik={sonuc.Olcum?.Yukseklik.ToString("0.000", CultureInfo.InvariantCulture) ?? "-"}"
                        + $" tepe={sonuc.Tepe?.Db.ToString("0.000", CultureInfo.InvariantCulture) ?? "-"}"));
                    return sonuc;
                }
                catch (FfmpegHatasi ex)
                {
                    ex.GunlukDosyasi = HataAciklamasi.GunlukYaz(input, GunlukMetni(ex, input, profile, media, a, ses, rapor, denemeler));
                    throw;
                }
            }

            try
            {
                // Kararın ölçümü: sesin çıktıdaki hâli. Kaynağın ölçümü kodlamadan önceki sesinkidir; AAC'de çözülen ses
                // ondan kısık olabilir (AAC tizleri keser; FDK'nin DRC ayarlarıyla çözücü sesi kısıp sıkıştırır; çok
                // düşük kesim frekansı sesin çoğunu siler), sınırlayıcı (elle yükseltmede ya da tavanı aşan tepede)
                // yüksek sesi kısar. Ses o zaman bir kez kazançsız denenir (AAC'de kodlanıp çözülür), "zaten yüksek" /
                // "hedefte" kararı çıktının gerçek yüksekliğiyle verilir. Kazanç: zincirde sınırlayıcı yoksa çıktıdaki
                // hâlinden (AAC'nin kaybı, DRC, kesim kazançla değişmez); varsa kaynağın ölçümünden (sınırlayıcının
                // kıstığı kazançla değişir, ses kısılınca hiç çalışmayabilir: kısılan ses hedefin üstünde kalıyordu),
                // gerisi denemelerle telafi edilir; kodlayıcıya giren ses tam ölçeği aşıyorsa da (elle yükseltme, kayan
                // noktalı kaynak) kaynağın ölçümünden: kazançsız denemede kodlayıcı tepeleri kırpar, kazanç değişince
                // kırpılan pay da değişir. WAV'da sınırlayıcı yoksa ölçüm zaten çıktının sesidir (aynı kanallar, aynı
                // hız). Tepe: kaynağınki (kodlayıcıya giren sesin tahmini).
                var ilk = new SesDuzeltme(Sinirla: a.Ses.Sinirlayici && kaynakTepe is { } t0 && t0 + Esitleme.TepePayi > tavan);
                (SesOlcum? Olcum, (double Db, bool Hassas)? Tepe)? ilkSonuc = null;
                if (a.Ses.Esitleme)
                {
                    var kazancOlcum = kaynakOlcum;
                    double? kararYukseklik = null;
                    var ilkSinirlayici = ArgsBuilder.SinirlayiciVar(a.Ses, null, 0, 0, ilk.Sinirla);
                    if (kaynakOlcum is not null && (kayipli || ilkSinirlayici))
                    {
                        onInfo?.Invoke(Describe(media, a, ses) + " · sesin çıktıdaki yüksekliği ölçülüyor");
                        ilkSonuc = await Dene(null, ilk);
                        if (ilkSonuc.Value.Olcum is { } cikis)
                        {
                            kararYukseklik = cikis.Yukseklik;
                            if (!ilkSinirlayici && kaynakOlcum.Tepe + Esitleme.TepePayi <= 0)
                            {
                                kazancOlcum = kaynakOlcum with { Yukseklik = cikis.Yukseklik };
                            }
                        }
                    }

                    rapor = Esitleme.KararVer(a.Ses, kazancOlcum, kararYukseklik) with { KaynakYukseklik = kaynakOlcum?.Yukseklik };
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

                // Çok kısık ses ya da hedef: ölçerin mutlak kapısı (-70 LUFS) bazı blokları saymaz, kazanç çıktının
                // seviyesinde denenip düzeltilir.
                var kisik = uygulandi && Math.Min(olcum!.Yukseklik, hedef) < Esitleme.KisikSinir;

                // Yükseklik denemesi: sınırlayıcı tepeleri kıstıkça yükseklik hedefin altında kalır; AAC tizleri kestiği
                // için yükseklik biraz düşer (kullanıcının OBS kaydında 128 kb/s'de 0,16 LU); çok kısık seste ölçer
                // sonucu kaydırır: ölçülen fark kadar telafi edilir (iki yönlü, 0,02 LU içine kadar).
                bool YukseklikDenemesi(SesDuzeltme d) => uygulandi && (kayipli || Sinirlayici(d) || kisik);

                // Kaynağın tepesi (kazançla) tavanı zaten aşıyorsa sınırlayıcı baştan zincirde (ses yükseltilmese de).
                if (koruma && kaynakTepe is { } kt && !Sinirlayici(duzeltme) && kt + Kazanc(duzeltme) + Esitleme.TepePayi > tavan)
                {
                    duzeltme = duzeltme with { Sinirla = true };
                }

                // Tepe denemesi gerekmez: ses sessiz ya da tavandan çok kısık (kodlama tepeyi olağan seste TepeDenemesiPayi kadar
                // yükseltse de tavanı aşmaz). FDK'nin DRC'si açıksa çözücü sesi yükseltebilir: her zaman denenir. Denemeye
                // girilince tepe (koruma varken) her denemede okunur: telafi kazancı artırabilir.
                var tepeDenemesi = koruma && kaynakTepe is { } kt2
                    && (kt2 + Kazanc(duzeltme) + Esitleme.TepeDenemesiPayi > tavan || Esitleme.DrcVar(a.Ses));
                double? ilkTepe = null;
                var tasma = 0.0;
                var sinir = false;
                var denendi = false;
                var denemelerBitti = false;
                var ilerlemeYok = false;
                if (tepeDenemesi || YukseklikDenemesi(duzeltme))
                {
                    // Gerekirse telafi ve sınırlayıcının tavanı düzeltilip yeniden denenir; kodlama denenmiş bir ayarla
                    // yapılır (denemeler biterse tepesi tavanın altında ölçülen ayarla).
                    var denenenler = new List<(SesDuzeltme D, double? Yukseklik, bool TepeTamam)>();
                    var noktalar = new List<YukseklikDenemesi>();
                    for (var d = 0; d < Esitleme.EnCokDeneme; d++)
                    {
                        var yukseklikDenemesi = YukseklikDenemesi(duzeltme);
                        onInfo?.Invoke(Describe(media, a, ses, rapor) + (yukseklikDenemesi
                            ? (kayipli
                                ? (Sinirlayici(duzeltme) ? " · sınırlayıcının ve AAC'nin payı ölçülüyor" : " · AAC'nin payı ölçülüyor")
                                : (Sinirlayici(duzeltme) ? " · sınırlayıcının payı ölçülüyor" : " · çıktının yüksekliği ölçülüyor"))
                            : (kayipli ? " · AAC'nin tepeye etkisi ölçülüyor" : " · tepe ölçülüyor")));

                        // Kazançsız ilk deneme kararın ölçümüyle aynı zincirse yeniden yapılmaz.
                        var denemeSonucu = ilkSonuc is { } ilkS && olcum is null && duzeltme == ilk
                            ? ilkS
                            : await Dene(olcum, duzeltme);
                        ilkSonuc = null;
                        denendi = true;
                        var tepe = koruma ? denemeSonucu.Tepe : null;
                        double? yukseklik = null;
                        if (yukseklikDenemesi)
                        {
                            if (denemeSonucu.Olcum is not { } olculen)
                            {
                                break;
                            }

                            yukseklik = olculen.Yukseklik;
                        }

                        if (koruma && tepe is null)
                        {
                            break;
                        }

                        ilkTepe ??= tepe?.Db;
                        denenenler.Add((duzeltme, yukseklik, tepe is not { } tp || Esitleme.TepeTavandaMi(tp.Db, tp.Hassas, tavan)));
                        var giris = Esitleme.GirisTepesi(kaynakTepe, Kazanc(duzeltme), Sinirlayici(duzeltme), tavan - duzeltme.TasmaPayi);

                        // Adım bütün önceki yükseklik denemeleriyle (sınırlayıcının payı değişse de: Esitleme.TelafiAdimi).
                        var (yeni, sinirda, gorulen) = Esitleme.DenemeAdimi(
                            duzeltme, tasma, yukseklik, hedef, tepe?.Db, tepe?.Hassas ?? false, tavan, giris, noktalar);
                        tasma = gorulen;
                        if (yukseklikDenemesi)
                        {
                            // Telafi sınırına bir kez gelindiyse (gereken telafi 6 dB'yi aştı) sonra hedefe yakın bir ayar
                            // seçilse de sınır görüldü: hedefe ulaşılamadıysa nedeni budur.
                            sinir |= sinirda;
                        }

                        if (yukseklik is { } yk)
                        {
                            noktalar.Add(new YukseklikDenemesi(duzeltme.Telafi, duzeltme.TasmaPayi, yk));
                        }

                        if (yeni is null)
                        {
                            // Hedefe gelinmeden durduysa (telafi sınırında değil): hedefin iki yanındaki denemeler arasında
                            // 0,01 dB kaldı, daha yakın ayar yok.
                            ilerlemeYok = yukseklik is { } yd && Math.Abs(yd - hedef) > Esitleme.KayipliPayi && !sinirda;
                            break;
                        }

                        duzeltme = yeni;
                        if (rapor is not null)
                        {
                            rapor = rapor with { Telafi = duzeltme.Telafi, TelafiSiniri = sinir, Kayipli = kayipli, TasmaPayi = duzeltme.TasmaPayi };
                        }
                    }

                    // Kodlama denenmiş bir ayarla: son deneme tamamsa (tepe tavanın altında, yükseklik hedefte) o; değilse
                    // (denemeler bitti, hedefe gelinemedi) tepesi tavanın altında ölçülen denemelerden hedefe en yakını
                    // (yükseklik denenmediyse sınırlayıcıyı en az kısanı); hiçbiri değilse telafi sabit, yalnızca
                    // sınırlayıcı aşağı çekilerek denenir.
                    if (koruma && denenenler.Count > 0)
                    {
                        var son = denenenler[^1];
                        var sonuDenendi = son.D == duzeltme;
                        denemelerBitti = !sonuDenendi;
                        // Hedefe en yakını; yükseklikleri 0,02 LU içinde eşit olanlardan sese en az kazanç ekleyeni (sınırlayıcı
                        // doyduysa daha fazla kazanç yüksekliği artırmaz, tepeleri daha çok ezer).
                        var uygun = denenenler.Where(x => x.TepeTamam)
                            .OrderBy(x => x.Yukseklik is { } y ? Math.Round(Math.Abs(y - hedef) / Esitleme.KayipliPayi) : 0)
                            .ThenBy(x => x.Yukseklik is null ? 0 : Math.Abs(x.D.Telafi))
                            .ThenBy(x => x.D.TasmaPayi)
                            .ToList();

                        // Son deneme hedefte ve tepesi tavanın altındaysa kodlama onunla.
                        var sonTamam = sonuDenendi && son.TepeTamam
                            && (son.Yukseklik is not { } sy || Math.Abs(sy - hedef) <= Esitleme.KayipliPayi);
                        if (sonTamam)
                        {
                            sinir = false;
                        }
                        else if (uygun.Count > 0)
                        {
                            duzeltme = uygun[0].D;
                        }
                        else if (!sonuDenendi)
                        {
                            for (var d = 0; d < Esitleme.EnCokDeneme; d++)
                            {
                                onInfo?.Invoke(Describe(media, a, ses, rapor) + (kayipli ? " · AAC'nin tepeye etkisi ölçülüyor" : " · tepe ölçülüyor"));
                                if ((await Dene(olcum, duzeltme)).Tepe is not { } tepe)
                                {
                                    break;
                                }

                                var giris = Esitleme.GirisTepesi(kaynakTepe, Kazanc(duzeltme), Sinirlayici(duzeltme), tavan - duzeltme.TasmaPayi);
                                var (yeni, _, gorulen) = Esitleme.DenemeAdimi(duzeltme, tasma, null, hedef, tepe.Db, tepe.Hassas, tavan, giris);
                                tasma = gorulen;
                                if (yeni is null)
                                {
                                    break;
                                }

                                duzeltme = yeni;
                            }
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
                        DenemelerBitti = denemelerBitti,
                        YakinAyarYok = ilerlemeYok,
                        Kayipli = kayipli && denendi,
                        TasmaPayi = duzeltme.TasmaPayi,
                        Tasma = tasma,
                        TepeZorla = tepeZorla,
                        IlkTepe = ilkTepe,
                        SinirlayiciCalisti = sinirlayici,
                        KisikOlcum = kisik && denendi,
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
        var uzanti = ArgsBuilder.Uzanti(a);
        var output = OutputPathFor(input, outputFolder, profile, uzanti);
        var temp = YarimDosyaAdi(output, uzanti);
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
                    await FfmpegRunner.RunAsync(plan.Gecisler[i], media.DurationSeconds, ilerleme, ct, toplamKare: ToplamKare(media, a));
                }
                catch (FfmpegHatasi ex) when (duzeltmeler < EnCokX264Duzeltmesi)
                {
                    // x264 açılışta reddettiyse ve ayar düzeltilebiliyorsa (çok düşük 2 / 3 geçiş bit hızı, matrisle
                    // olanaksız QP sınırı): x264'ün söylediği değerle bu geçiş (ve sonrakiler) yeniden. Bellek yetmediyse
                    // (iş parçacığı / önden bakış azaltıldı) bütün geçişler baştan: geçişler aynı ayarlarla kodlanmalı.
                    if (ArgsBuilder.X264Duzelt(ex.Gunluk, a, coktu: ex.Coktu) is not { } duzeltmeNotu)
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

            output = Yerlestir(temp, output, () => OutputPathFor(input, outputFolder, profile, uzanti));
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
                if ((await Olc(Esitleme.SonucOlcumKomutu(output), ArgsBuilder.OlcumKanali(a.Ses, ses), ct)).Olcum is { } sonuc)
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

    /// <summary>
    /// Çıktının tahmini görüntü karesi sayısı (ilerleme çubuğu için): görüntünün süresi × çıktının kare hızı; görüntü izinin
    /// süresi bilinmiyorsa dosyanınki. Bilinmiyorsa 0 (ilerleme ffmpeg'in zamanından).
    /// </summary>
    public static double ToplamKare(MediaInfo media, ProfilAyarlari a)
    {
        var sure = media.VideoSuresi > 0 ? media.VideoSuresi : media.DurationSeconds;
        return sure > 0 ? Math.Max(1, Math.Round(sure * ArgsBuilder.CiktiKareHizi(a, media))) : 0;
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

        if (ArgsBuilder.KisitliIntraNotu(a, media) is { } kisitli)
        {
            notlar.Add(kisitli);
        }

        if (ArgsBuilder.AramaAraligiNotu(a, media) is { } arama)
        {
            notlar.Add(arama);
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

        if (ArgsBuilder.KisitliIntraNotu(a, media) is { } kisitli)
        {
            text += " · " + kisitli;
        }

        if (ArgsBuilder.AramaAraligiNotu(a, media) is { } arama)
        {
            text += " · " + arama;
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
