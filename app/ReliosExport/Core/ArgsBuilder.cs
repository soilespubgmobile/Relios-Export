using System.Globalization;
using System.IO;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Core;

/// <summary>Kaynağı hedef en-boy oranına ortadan kırpma hesabının sonucu.</summary>
public sealed record CropPlan(int Width, int Height, int X, int Y, bool IsNeeded);

public enum SesSecimTuru { Yok, Video, HariciDosya }

/// <summary>
/// Çıktıya hangi sesin gireceği: videonun kendi sesi, aynı adlı harici dosya ya da hiç.
/// <paramref name="Kanal"/>: seçilen sesin kanal sayısı (bilinmiyorsa null); mono ses stereoya tam seviyede açılsın diye.
/// <paramref name="Iz"/>: videonun kaçıncı ses izi (0'dan; ffmpeg -map 0:a:N). <paramref name="IzOzeti"/>: videoda
/// birden fazla ses izi varsa hangisinin seçildiği (ör. "2. iz · PCM 24 bit · 48 kHz · stereo").
/// </summary>
public sealed record SesSecimi(SesSecimTuru Tur, string? Dosya = null, int? Kanal = null, int Iz = 0, string? IzOzeti = null)
{
    /// <summary>Seçilen sesin örnekleme hızı (Hz; bilinmiyorsa null). LUFS eşitlemeden sonra "kaynakla aynı" hız için.</summary>
    public int? OrnekHizi { get; init; }

    public static readonly SesSecimi Yok = new(SesSecimTuru.Yok);

    public string Aciklama => Tur switch
    {
        SesSecimTuru.HariciDosya => Path.GetFileName(Dosya) ?? "harici ses",
        SesSecimTuru.Video => IzOzeti is null ? "videonun sesi" : $"videonun sesi ({IzOzeti})",
        _ => "ses yok",
    };
}

/// <summary>
/// LUFS eşitlemede sesin ölçülen değerleri (ITU-R BS.1770-4 / EBU R128; FFmpeg'in ebur128 ölçeri, bkz.
/// <see cref="Esitleme.OlcumFiltresi"/>): tümleşik yükseklik (LUFS, 0,001 hassasiyetle), gerçek tepe (dBTP) ve
/// yükseklik aralığı (LU) (bu ikisi ölçerin özetinden, 0,1 hassasiyetle).
/// </summary>
public sealed record SesOlcum(double Yukseklik, double Tepe, double Aralik);

/// <summary>Bir dışa aktarmanın ffmpeg komutları (2 / 3 geçişte birden fazla) ve çıktı uzantısı.</summary>
public sealed record KomutPlani(IReadOnlyList<IReadOnlyList<string>> Gecisler, string Uzanti);

/// <summary>Profil ayarları + kaynak bilgisinden ffmpeg komut satırını üretir.</summary>
public static class ArgsBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Kaynak videoyu hedef en-boy oranına ortadan kırpma planı (oran zaten aynıysa kırpmaz).</summary>
    public static CropPlan PlanCrop(MediaInfo m, double target)
    {
        // ffmpeg döndürmeyi otomatik uyguladığı için kırpma döndürülmüş görüntüye göre yapılır.
        var w = m.DisplayWidth;
        var h = m.DisplayHeight;
        var sar = m.Rotation is 90 or 270 ? 1.0 / m.Sar : m.Sar;
        var displayAspect = w * sar / h;

        if (Math.Abs(displayAspect - target) < 0.002)
        {
            return new CropPlan(w, h, 0, 0, IsNeeded: false);
        }

        if (displayAspect > target)
        {
            // Kaynak daha geniş: yanlardan kırp.
            var cw = Even(h * target / sar);
            cw = Math.Min(cw, w - (w % 2));
            return new CropPlan(cw, h, (w - cw) / 2, 0, IsNeeded: true);
        }

        // Kaynak daha dar (ör. 9:19.5 telefon kaydı): üstten/alttan kırp.
        var ch = Even(w * sar / target);
        ch = Math.Min(ch, h - (h % 2));
        return new CropPlan(w, ch, 0, (h - ch) / 2, IsNeeded: true);
    }

    /// <summary>Kaynak videoyu 9:16'ya ortadan kırpma planı.</summary>
    public static CropPlan PlanCrop916(MediaInfo m) => PlanCrop(m, 9.0 / 16.0);

    private static readonly double[] YayginOranlar =
        [9.0 / 16, 16.0 / 9, 1, 4.0 / 5, 5.0 / 4, 3.0 / 4, 4.0 / 3, 2.0 / 3, 3.0 / 2, 9.0 / 21, 21.0 / 9];

    /// <summary>
    /// Hedef boyutun en-boy oranı. Yaygın bir orana çok yakınsa (%0,5) o oran kullanılır:
    /// 1216×2160 tam 9:16 değildir (0,563); hedefteki gibi önce 9:16'ya kırpılıp sonra ölçeklenir.
    /// </summary>
    public static double HedefOran(int w, int h)
    {
        var r = (double)w / h;
        foreach (var c in YayginOranlar)
        {
            if (Math.Abs(r - c) / c < 0.005)
            {
                return c;
            }
        }

        return r;
    }

    /// <summary>Bu ayarlarla kaynak kırpılacak mı (ekranda bilgi için).</summary>
    public static bool KirpilacakMi(ProfilAyarlari a, MediaInfo m) =>
        a.Video.Cozunurluk == CozunurlukModu.Hedef
        && a.Video.Oran == OranUyumu.OrtadanKirp
        && PlanCrop(m, HedefOran(a.Video.Genislik, a.Video.Yukseklik)).IsNeeded;

    /// <summary>Ayarlara göre kullanılacak ses: aynı adlı dosya, videonun sesi ya da hiç.</summary>
    public static SesSecimi SesSec(SesAyarlari s, MediaInfo media, string input)
    {
        switch (s.Kaynak)
        {
            case SesKaynagi.Sessiz:
                return SesSecimi.Yok;
            case SesKaynagi.AyniAdliDosyaOncelikli when HariciSes.Bul(input) is { } dosya:
                return new SesSecimi(SesSecimTuru.HariciDosya, dosya);
            default:
                if (!media.HasAudio)
                {
                    return SesSecimi.Yok;
                }

                var izler = media.SesAkislari;
                var iz = VideoSesIzi(izler);
                var akis = iz < izler.Count ? izler[iz] : null;
                return new SesSecimi(
                    SesSecimTuru.Video,
                    null,
                    akis is { Kanal: > 0 } ? akis.Kanal : null,
                    iz,
                    izler.Count > 1 && akis is not null ? $"{iz + 1}. iz · {akis.Ozet}" : null)
                {
                    OrnekHizi = akis is { OrnekHizi: > 0 } ? akis.OrnekHizi : null,
                };
        }
    }

    /// <summary>
    /// Videonun hangi ses izi kullanılır: kayıpsız ses (WAV / PCM, FLAC, ALAC...) varsa o, yoksa ilk iz.
    /// Aynı videoda hem WAV hem AAC varsa FDK AAC'ye WAV'dan çevrilir (AAC'yi yeniden sıkıştırmak kalite kaybettirir).
    /// </summary>
    public static int VideoSesIzi(IReadOnlyList<SesAkisi> izler)
    {
        for (var i = 0; i < izler.Count; i++)
        {
            if (KayipsizMi(izler[i].Codec))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Ses codec'i kayıpsız mı (ffprobe adı).</summary>
    public static bool KayipsizMi(string? codec) =>
        codec is not null
        && (codec.StartsWith("pcm_", StringComparison.Ordinal)
            || codec is "flac" or "alac" or "wavpack" or "tta" or "truehd" or "mlp" or "ape" or "s302m");

    /// <summary>
    /// <see cref="SesSec"/> + aynı adlı harici dosyanın kanal sayısı (ffprobe ile). Kanal sayısı bilinmezse
    /// ses yine çalışır, yalnızca mono → stereo açılışı ffmpeg'in varsayılanıyla (-3 dB) olur.
    /// </summary>
    public static async Task<SesSecimi> SesSecAsync(string ffprobe, SesAyarlari s, MediaInfo media, string input, CancellationToken ct)
    {
        var ses = SesSec(s, media, input);
        if (ses is { Tur: SesSecimTuru.HariciDosya, Dosya: { } dosya })
        {
            try
            {
                if (await MediaProbe.ProbeAudioAsync(ffprobe, dosya, ct) is { } akis)
                {
                    ses = ses with
                    {
                        Kanal = akis.Kanal > 0 ? akis.Kanal : null,
                        OrnekHizi = akis.OrnekHizi > 0 ? akis.OrnekHizi : null,
                    };
                }
            }
            catch (Exception ex) when (ex is FfmpegHatasi or InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                // Okunamazsa ffmpeg kodlarken asıl hatayı söyler.
            }
        }

        return ses;
    }

    public static KomutPlani Olustur(
        ProfilAyarlari a,
        MediaInfo media,
        string input,
        SesSecimi ses,
        string output,
        string? gecisDosyasi = null,
        SesOlcum? olcum = null,
        double telafi = 0)
    {
        var v = a.Video;
        var gecisSayisi = v.Kodlayici == VideoKodlayici.X264
            ? v.X264.Mod switch { X264Modu.IkiGecis => 2, X264Modu.UcGecis => 3, _ => 1 }
            : 1;
        gecisDosyasi ??= Path.Combine(Path.GetTempPath(), "relios-export-gecis");

        var gecisler = new List<IReadOnlyList<string>>();
        if (gecisSayisi == 1)
        {
            gecisler.Add(Komut(a, media, input, ses, output, gecis: 0, gecisDosyasi, olcum, telafi));
        }
        else
        {
            // 2 geçiş: 1 → 2. 3 geçiş: 1 → 3 (okur ve yazar) → 2 (son).
            gecisler.Add(Komut(a, media, input, ses, "-", gecis: 1, gecisDosyasi));
            if (gecisSayisi == 3)
            {
                gecisler.Add(Komut(a, media, input, ses, "-", gecis: 3, gecisDosyasi));
            }

            gecisler.Add(Komut(a, media, input, ses, output, gecis: 2, gecisDosyasi, olcum, telafi));
        }

        return new KomutPlani(gecisler, Uzanti(a));
    }

    /// <summary>Çıktı uzantısı: MOV seçiliyse ya da ses WAV ise .mov (MP4'e PCM ses girmez), değilse .mp4.</summary>
    public static string Uzanti(ProfilAyarlari a) => a.Uzanti;

    private static List<string> Komut(
        ProfilAyarlari a,
        MediaInfo media,
        string input,
        SesSecimi ses,
        string output,
        int gecis,
        string gecisDosyasi,
        SesOlcum? olcum = null,
        double telafi = 0)
    {
        var v = a.Video;
        var sonGecis = gecis is 0 or 2;
        var sesVar = sonGecis && ses.Tur != SesSecimTuru.Yok;
        var harici = sesVar && ses.Tur == SesSecimTuru.HariciDosya;

        var cokGecisli = gecis != 0;
        var args = new List<string> { "-hide_banner", "-nostdin", "-y" };
        if (v.Kodlayici == VideoKodlayici.Nvenc && v.Nvenc.GpuCozme)
        {
            args.AddRange(["-hwaccel", "cuda"]);
        }

        args.AddRange(["-i", input]);
        if (harici)
        {
            args.AddRange(["-i", ses.Dosya!]);
        }

        args.AddRange(["-map", "0:v:0"]);
        if (sesVar)
        {
            args.AddRange(["-map", harici ? "1:a:0" : "0:a:" + ses.Iz.ToString(Inv)]);
        }

        args.AddRange(
        [
            // Kaynaktaki genel etiketler (ör. Topaz, TikTok etiketleri) çıktıya taşınmasın.
            "-map_metadata", "-1",
            "-map_chapters", "-1",
        ]);

        if (cokGecisli)
        {
            // Geçişlerin hepsi aynı kareleri kodlamalı: ilk geçişin çıktısı (null) varsayılan olarak kare
            // eklemez, MP4 ise sabit kare hızı için sonda kare ekleyebilir; x264 ikinci geçişte
            // "Incomplete MB-tree stats file" hatası verir. Hepsinde sabit kare hızı kipi açıkça seçilir.
            args.AddRange(["-fps_mode", "cfr"]);
        }

        args.AddRange(["-vf", string.Join(',', VideoFiltreleri(a, media))]);

        if (v.Kodlayici == VideoKodlayici.X264)
        {
            X264(args, a, media, gecis, gecisDosyasi);
        }
        else
        {
            Nvenc(args, a, CiktiKareHizi(a, media), media);
        }

        args.AddRange(
        [
            "-color_range", "tv",
            "-colorspace", "bt709",
            "-color_primaries", "bt709",
            "-color_trc", "bt709",
            "-metadata:s:v:0", "language=eng",
        ]);

        if (!sonGecis)
        {
            args.AddRange(["-an", "-progress", "pipe:1", "-stats_period", "0.5", "-f", "null", output]);
            return args;
        }

        if (sesVar)
        {
            Ses(args, a.Ses, harici, ses.Kanal, olcum, ses.OrnekHizi, telafi);
        }

        args.AddRange(
        [
            // Encoded date / Tagged date: dosyanın oluşturma ve değiştirme zamanı = kodlamanın başladığı an.
            // ffmpeg "now"ı bilgisayarın saatinden alır ve MP4 / MOV kuralı gereği UTC olarak dosyanın (mvhd) ve
            // her izin (tkhd, mdhd) başlığına yazar. Kaynağın tarihi yukarıda -map_metadata -1 ile taşınmaz.
            "-metadata", "creation_time=now",
            "-movflags", "+faststart",
            "-progress", "pipe:1",
            "-stats_period", "0.5",
            output,
        ]);

        return args;
    }

    // --------------------------------------------------------------------------- Video

    private static List<string> VideoFiltreleri(ProfilAyarlari a, MediaInfo media)
    {
        var v = a.Video;
        var filters = new List<string>();
        if (NvdecBicimi(a, media) is { } nvdec)
        {
            filters.Add("format=" + nvdec);
        }

        if (v.KareHiziModu == KareHiziModu.Sabit)
        {
            // Önce kare hızı (gereksiz ölçekleme yapılmasın), ör. 60 → 29.97.
            filters.Add("fps=" + v.KareHizi);
        }

        // Ölçekleme + renk: Limited (TV) aralık, BT.709 matris. Etiketsiz kaynakta ffmpeg BT.601 varsayar;
        // HD görüntüde bu renkleri kaydırır. Bu yüzden etiketsiz HD kaynak BT.709 kabul edilir.
        var flags = "flags=" + v.Olcekleme + (v.HassasYuvarlama ? "+accurate_rnd+full_chroma_int" : string.Empty);
        var renk = $"in_color_matrix={InputMatrix(media)}:out_range=tv:out_color_matrix=bt709";

        if (v.Cozunurluk == CozunurlukModu.Kaynak)
        {
            // Boyut aynı kalır (kare piksele açılır, çift sayıya yuvarlanır); yalnızca renk dönüşür.
            filters.Add($"scale=trunc(iw*sar/2)*2:trunc(ih/2)*2:{flags}:{renk}");
        }
        else
        {
            var w = v.Genislik - (v.Genislik % 2);
            var h = v.Yukseklik - (v.Yukseklik % 2);
            switch (v.Oran)
            {
                case OranUyumu.OrtadanKirp:
                    var crop = PlanCrop(media, HedefOran(w, h));
                    if (crop.IsNeeded)
                    {
                        filters.Add(string.Create(Inv, $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}"));
                    }

                    filters.Add(string.Create(Inv, $"scale={w}:{h}:{flags}:{renk}"));
                    break;
                case OranUyumu.KenarEkle:
                    filters.Add(string.Create(Inv,
                        $"scale={w}:{h}:force_original_aspect_ratio=decrease:force_divisible_by=2:{flags}:{renk}"));
                    filters.Add(string.Create(Inv, $"pad={w}:{h}:-1:-1:color=black"));
                    break;
                default:
                    filters.Add(string.Create(Inv, $"scale={w}:{h}:{flags}:{renk}"));
                    break;
            }
        }

        filters.Add("setsar=1");
        filters.Add("format=" + PikselBicimi(a, media));
        // Renk primeri ve aktarım her zaman BT.709 etiketlenir (hedef video gibi).
        // FFmpeg 7.1 ve sonrası bu ikisini kareden alıyor; -color_primaries / -color_trc
        // tek başına etiketsiz kaynakta işe yaramıyor.
        filters.Add("setparams=color_primaries=bt709:color_trc=bt709");
        return filters;
    }

    /// <summary>
    /// Kaynak ekran kartıyla çözülüyorsa (NVDEC) karelerin önce çevrileceği piksel biçimi: kaynağın işlemcide
    /// çözülünce geldiği biçim. NVDEC kareleri NV12 / P010 olarak verir; ölçekleme ve renk dönüşümü bu biçimleri
    /// işlemcide çözülen karelerden (yuv420p / yuv420p10le) farklı işler, sonuç bit bit aynı olmaz (kullanıcının
    /// kartında ölçüldü: VP9 480p'de 0,37 dB düşük). NV12 → yuv420p ve P010 → yuv420p10le kayıpsızdır (yalnızca
    /// düzlemlerin yeri değişir); bu çevirmeyle ekran kartıyla çözmenin sonucu işlemciyle çözmeyle aynı olur.
    /// "yuvj" biçimlerinin yerine aynı biçimin normal adı (aralık karenin bilgisinde kalır; ffmpeg de aynısını yapar).
    /// Ekran kartı kaynağı çözemezse (ör. ProRes) ffmpeg işlemcide çözer; çevirme o zaman etkisizdir.
    /// </summary>
    public static string? NvdecBicimi(ProfilAyarlari a, MediaInfo? media)
    {
        if (a.Video.Kodlayici != VideoKodlayici.Nvenc || !a.Video.Nvenc.GpuCozme
            || media?.PikselBicimi?.Trim() is not { Length: > 0 } p)
        {
            return null;
        }

        return p.StartsWith("yuvj", StringComparison.Ordinal) ? "yuv" + p[4..] : p;
    }

    private static string PikselBicimi(ProfilAyarlari a, MediaInfo? media)
    {
        var profil = ProfilCoz(a.Video.Kodlayici == VideoKodlayici.X264 ? a.Video.X264.Profil : a.Video.Nvenc.Profil, media);
        return profil switch
        {
            "high10" => "yuv420p10le",
            "high422" => "yuv422p",
            "high444" or "high444p" => "yuv444p",
            _ => "yuv420p",
        };
    }

    /// <summary>Çıktının kare hızı (anahtar kare aralığı saniye olarak verildiğinde gerekir).</summary>
    public static double CiktiKareHizi(ProfilAyarlari a, MediaInfo media)
    {
        var v = a.Video;
        if (v.KareHiziModu == KareHiziModu.Sabit && Bicim.KesirCoz(v.KareHizi) is { } f)
        {
            return f;
        }

        return media.FrameRate > 0 ? media.FrameRate : 30;
    }

    private static int? Kare(AnahtarKareAyarlari k, double? deger, double fps) => AnahtarKareHesabi.Kare(k, deger, fps);

    private static void X264(List<string> args, ProfilAyarlari a, MediaInfo media, int gecis, string gecisDosyasi)
    {
        var p = X264Parametreleri(a, CiktiKareHizi(a, media), media);
        X264Bayraklari(args, a, gecis, gecisDosyasi, media);
        args.AddRange(["-x264-params", string.Join(':', p)]);
    }

    /// <summary>-x264-params değeri (ayar penceresindeki önizleme için de kullanılır).</summary>
    public static string X264ParametreMetni(ProfilAyarlari a, double fps) => string.Join(':', X264Parametreleri(a, fps));

    // --------------------------------------------------------------------------- Önizleme

    /// <summary>Ayar penceresindeki "Komut" sekmesi: x264 için ffmpeg'e verilecek video seçenekleri (tek geçiş).</summary>
    public static string X264KomutMetni(ProfilAyarlari a, double fps)
    {
        var args = new List<string>();
        X264Bayraklari(args, a, 0, string.Empty, null);
        args.AddRange(["-x264-params", X264ParametreMetni(a, fps)]);
        return KomutMetni(args);
    }

    /// <summary>Ayar penceresindeki "Komut" sekmesi: NVENC için ffmpeg'e verilecek video seçenekleri.</summary>
    public static string NvencKomutMetni(ProfilAyarlari a, double fps)
    {
        var args = new List<string>();
        Nvenc(args, a, fps, null);
        return KomutMetni(args);
    }

    /// <summary>Ayar penceresindeki "Komut" sekmesi: ses seçenekleri (videonun kendi sesi için).</summary>
    public static string SesKomutMetni(SesAyarlari s)
    {
        var args = new List<string>();
        Ses(args, s, harici: false);
        return KomutMetni(args);
    }

    /// <summary>Komut satırı görünümü: boşluk içeren değerler tırnak içinde.</summary>
    public static string KomutMetni(IEnumerable<string> args) =>
        string.Join(' ', args.Select(x => x.Length == 0 || x.Contains(' ') || x.Contains('"') ? "\"" + x.Replace("\"", "\\\"") + "\"" : x));

    private static List<string> X264Parametreleri(ProfilAyarlari a, double fps, MediaInfo? media = null)
    {
        var x = a.Video.X264;
        var k = a.AnahtarKare;
        var cbr = x.Mod == X264Modu.Cbr;
        var p = new List<string>();

        void Ekle(string key, object? value)
        {
            switch (value)
            {
                case null:
                    return;
                case bool b:
                    p.Add($"{key}={(b ? 1 : 0)}");
                    return;
                default:
                    p.Add(string.Create(Inv, $"{key}={value}"));
                    return;
            }
        }

        static string Iki(double d) => d.ToString("0.00", Inv);

        // Sıra, hedef videodaki "Encoding settings" satırıyla doğrulanan komutla aynıdır.
        Ekle("threads", x.IsParcacigi);
        Ekle("ref", x.Ref);
        if (!x.Deblock)
        {
            Ekle("no-deblock", true);
        }
        else if (x.DeblockAlfa is { } da && x.DeblockBeta is { } db)
        {
            p.Add(string.Create(Inv, $"deblock={da},{db}"));
        }

        Ekle("me", x.Me);
        Ekle("subme", x.AltPiksel);
        if (x.PsyRd is { } rd && x.PsyTrellis is { } pt)
        {
            p.Add($"psy-rd={Iki(rd)},{Iki(pt)}");
        }

        Ekle("mixed-refs", x.KarisikRef);
        Ekle("merange", x.MeAraligi);
        Ekle("chroma-me", x.KromaMe);
        Ekle("trellis", x.Trellis);
        // x264 dosyaya kullanılan özelliklerin gerektirdiği en düşük profili yazar: 8x8 DCT kapalıysa High
        // istense de "Main" yazılır. Profil "kaynakla aynı" iken kaynak High ise High'ın özelliği açılır.
        var kaynakHigh = x.Profil == Kaynak && media is not null && ProfilCoz(x.Profil, media) == "high";
        Ekle("8x8dct", kaynakHigh ? true : x.Dct8x8);
        Ekle("cqm", x.Cqm);
        Ekle("deadzone-inter", x.OluBolgeInter);
        Ekle("deadzone-intra", x.OluBolgeIntra);
        Ekle("fast-pskip", x.HizliPAtlama);
        Ekle("nr", x.GurultuAzaltma);
        Ekle("dct-decimate", x.DctSeyreltme);
        Ekle("interlaced", x.Gecmeli);
        Ekle("bluray-compat", x.BlurayUyumlu);
        Ekle("constrained-intra", x.KisitliIntra);
        Ekle("bframes", x.BKare);
        Ekle("weightp", x.AgirlikliP);
        var aralik = AnahtarKareHesabi.Aralik(k, fps);
        Ekle("keyint", aralik.EnBuyuk);
        Ekle("min-keyint", aralik.EnKucuk);
        Ekle("scenecut", k.SahneDegisimi);
        Ekle("intra-refresh", k.IntraYenileme);
        Ekle("rc-lookahead", x.IleriBakis);
        Ekle("mbtree", x.MbAgaci);
        switch (x.Mod)
        {
            case X264Modu.Crf:
                Ekle("crf", x.Crf.ToString("0.##", Inv));
                break;
            case X264Modu.SabitQp:
                Ekle("qp", x.Qp);
                break;
        }

        if (x.Qcomp is { } qc)
        {
            p.Add("qcomp=" + Iki(qc));
        }

        Ekle("qpmin", x.QpMin);
        Ekle("qpmax", x.QpMax);
        Ekle("qpstep", x.QpAdim);
        Ekle("vbv-maxrate", cbr ? x.BitHizi : x.VbvMaks);
        Ekle("vbv-bufsize", cbr ? x.VbvTampon ?? x.BitHizi * 2 : x.VbvTampon);
        Ekle("nal-hrd", cbr ? "cbr" : x.NalHrd);
        Ekle("filler", cbr || x.Dolgu);
        if (x.IpOrani is { } ip)
        {
            p.Add("ipratio=" + Iki(ip));
        }

        Ekle("aq-mode", x.AqModu);
        if (x.AqGucu is { } aq)
        {
            p.Add("aq-strength=" + Iki(aq));
        }

        // Hedefte olmayan, yalnızca değiştirildiğinde yazılan ayarlar.
        Ekle("lookahead-threads", x.IleriBakisIsParcacigi);
        Ekle("b-bias", x.BSapma);
        Ekle("b-adapt", x.BUyarlamali);
        Ekle("b-pyramid", x.BPiramit);
        Ekle("direct", x.DogrudanMv);
        if (!x.AgirlikliB)
        {
            Ekle("weightb", false);
        }

        Ekle("slices", x.Dilim);
        if (!x.Cabac)
        {
            Ekle("cabac", false);
        }

        Ekle("partitions", x.Bolumler);
        if (!x.Psy)
        {
            Ekle("psy", false);
        }

        if (x.PbOrani is { } pb)
        {
            p.Add("pbratio=" + Iki(pb));
        }

        Ekle("chroma-qp-offset", x.KromaQpOfseti);
        if (x.CplxBlur is { } cb)
        {
            p.Add("cplxblur=" + Iki(cb));
        }

        if (x.QBlur is { } qb)
        {
            p.Add("qblur=" + Iki(qb));
        }

        if (x.VbvBaslangic is { } vi)
        {
            p.Add("vbv-init=" + Iki(vi));
        }

        if (x.Aud)
        {
            Ekle("aud", true);
        }

        if (x.PicStruct)
        {
            Ekle("pic-struct", true);
        }

        if (k.AcikGop)
        {
            Ekle("open-gop", true);
        }

        if (!string.IsNullOrWhiteSpace(x.EkParametreler))
        {
            p.Add(x.EkParametreler.Trim().Trim(':'));
        }

        return p;
    }

    // --------------------------------------------------------------------------- Kaynakla aynı profil / seviye

    /// <summary>Ayarlarda profil ve seviye için "kaynakla aynı" değeri.</summary>
    public const string Kaynak = "kaynak";

    /// <summary>
    /// "kaynak" profilini çözer: H.264 kaynakta aynı profil (Baseline / Main / High; çıktı 8 bit 4:2:0 olduğu
    /// için High 10 / 4:2:2 / 4:4:4 → High). Başka codec'lerde (HEVC, AV1, ProRes...) H.264 karşılığı
    /// olmadığından Main. Kaynak bilinmiyorsa (ayar penceresi önizlemesi) "(kaynak)".
    /// </summary>
    public static string ProfilCoz(string profil, MediaInfo? m)
    {
        if (profil != Kaynak)
        {
            return profil;
        }

        if (m is null)
        {
            return "(kaynak)";
        }

        if (m.VideoCodec != "h264" || string.IsNullOrEmpty(m.VideoProfil))
        {
            return "main";
        }

        return m.VideoProfil.Contains("Baseline", StringComparison.OrdinalIgnoreCase) ? "baseline"
            : m.VideoProfil.Contains("High", StringComparison.OrdinalIgnoreCase) ? "high"
            : "main";
    }

    /// <summary>
    /// "kaynak" seviyesini çözer: H.264 kaynakta aynı seviye (42 → "4.2", 9 → "1b"); başka codec'lerde
    /// seviyeler H.264'le aynı anlamda olmadığından otomatik (boş: x264 / NVENC kendisi seçer).
    /// </summary>
    public static string SeviyeCoz(string seviye, MediaInfo? m)
    {
        if (seviye != Kaynak)
        {
            return seviye;
        }

        if (m is null)
        {
            return "(kaynak)";
        }

        if (m.VideoCodec != "h264" || m.VideoSeviye is not { } l)
        {
            return string.Empty;
        }

        // Tanınmayan değer (bozuk ya da boş seviye bilgisi) ffmpeg'e verilirse kodlayıcı açılmaz: otomatik.
        var s = l == 9 ? "1b" : string.Create(Inv, $"{l / 10}.{l % 10}");
        return H264Seviye.Sira(s) >= 0 ? s : string.Empty;
    }

    /// <summary>Çıktı görüntüsünün boyutu (ölçekleme ve kırpmadan sonra).</summary>
    public static (int Genislik, int Yukseklik) CiktiBoyutu(ProfilAyarlari a, MediaInfo m)
    {
        var v = a.Video;
        if (v.Cozunurluk == CozunurlukModu.Kaynak)
        {
            // Filtredeki gibi: scale=trunc(iw*sar/2)*2:trunc(ih/2)*2 (döndürme ffmpeg'de önce uygulanır).
            var sar = m.Rotation is 90 or 270 ? 1.0 / m.Sar : m.Sar;
            return ((int)Math.Floor(m.DisplayWidth * sar / 2) * 2, m.DisplayHeight / 2 * 2);
        }

        return (v.Genislik - (v.Genislik % 2), v.Yukseklik - (v.Yukseklik % 2));
    }

    /// <summary>NVENC'in sınırlı en yüksek bit hızı (kb/s); sınırsızsa (kayıpsızda da) null.</summary>
    private static int? NvencBitHizi(NvencAyarlari n) => n.Kayipsiz ? null : n.Mod switch
    {
        NvencModu.Cbr => n.BitHizi,
        NvencModu.DegiskenBitHizi => n.MaksBitHizi ?? n.BitHizi,
        NvencModu.SabitKalite => n.MaksBitHizi,
        _ => null,
    };

    /// <summary>
    /// NVENC'in VBV tamponu (kbit). Boşsa: CBR'de bit hızı kadar (1 saniye; seviye sınırına sığar), VBR'de
    /// ffmpeg'in varsayılanı olan bit hızının 2 katı. Sabit kalite / QP'de yok: ffmpeg sabit kalitede tamponu
    /// yok sayar (nvenc.c: "CQ mode shall discard avg bitrate/vbv buffer size"), kart seviyenin tamponunu kullanır.
    /// Kayıpsızda da yok (bit hızı sınırı yoktur).
    /// </summary>
    private static int? NvencTampon(NvencAyarlari n) => n.Kayipsiz ? null : n.Mod switch
    {
        NvencModu.Cbr => n.Tampon ?? n.BitHizi,
        NvencModu.DegiskenBitHizi => n.Tampon ?? n.BitHizi * 2,
        _ => null,
    };

    /// <summary>
    /// Sabit kalitede kartın kullanacağı tampon (kbit): seviyenin en büyük tamponu (ffmpeg verilen tamponu yok
    /// sayar). "En çok" boyut tahmini için; seviye bilinmiyorsa (otomatik) çıktıya yeten en düşük seviyeninki.
    /// </summary>
    public static int? NvencSabitKaliteTamponu(ProfilAyarlari a, MediaInfo media)
    {
        var n = a.Video.Nvenc;
        var profil = NvencProfili(n, media);
        var high = profil.StartsWith("high", StringComparison.Ordinal);
        var seviye = NvencSeviyesi(a, media);
        if (H264Seviye.Sira(seviye) < 0)
        {
            var (w, h) = CiktiBoyutu(a, media);
            seviye = H264Seviye.EnAz(w, h, CiktiKareHizi(a, media), NvencBitHizi(n), null, NvencEnAzDpb(n, profil), high)
                     ?? string.Empty;
        }

        return H264Seviye.EnFazlaTampon(seviye, high);
    }

    /// <summary>
    /// NVENC'e verilecek seviye. "kaynak" ise kaynağın seviyesi; ama çıktının boyutu, kare hızı, bit hızı,
    /// tamponu ya da referans kare sayısı o seviyenin sınırını aşıyorsa yeten en düşük seviye. NVENC aşan
    /// seviyeyi kabul etmez ("Invalid Level"), kodlamayı hiç başlatmaz.
    /// </summary>
    public static string NvencSeviyesi(ProfilAyarlari a, MediaInfo? media)
    {
        var n = a.Video.Nvenc;
        var seviye = SeviyeCoz(n.Seviye, media);
        if (n.Seviye != Kaynak || media is null || string.IsNullOrEmpty(seviye))
        {
            return seviye;
        }

        var profil = NvencProfili(n, media);
        var (w, h) = CiktiBoyutu(a, media);

        // DPB, referans kare sayısı kadar verilir (NvencDpb; B-kare varsa en az 2 ya da 3); boşsa seviyeye göre
        // seçildiği için B-karenin gerektirdiği kadar yeter.
        var gerekli = H264Seviye.EnAz(
            w, h, CiktiKareHizi(a, media), NvencBitHizi(n), NvencTampon(n),
            Math.Max(n.Ref ?? 1, NvencEnAzDpb(n, profil)),
            high: profil.StartsWith("high", StringComparison.Ordinal));
        return H264Seviye.EnYuksek(seviye, gerekli);
    }

    /// <summary>
    /// NVENC'in DPB'si (dosyadaki referans kare tamponu, max_num_ref_frames). ffmpeg'de -refs yalnızca bir karenin
    /// kaç kareye baktığıdır; DPB ayrı ayardır (-dpb_size) ve boşsa kart kendisi seçer (6 kare). 1080p'de 4.x
    /// seviyeleri en fazla 4 kare taşır: kartın seçtiği 6 kare seviyeyi aşar ve NVENC "Invalid Level" verir.
    /// Bu yüzden referans kare sayısı verilmişse DPB de o kadar (B-karenin gerektirdiğinden az değil, bkz.
    /// NvencEnAzDpb); verilmemişse ve seviye belliyse seviyenin taşıyabildiği kadar (en fazla kartın seçeceği 6
    /// kare). Seviye otomatikse kart seçer.
    /// </summary>
    private static int? NvencDpb(NvencAyarlari n, string seviye, string profil, ProfilAyarlari a, MediaInfo? media)
    {
        if (n.Ref is { } r)
        {
            return Math.Max(r, NvencEnAzDpb(n, profil));
        }

        if (media is null || H264Seviye.Sira(seviye) < 0)
        {
            return null;
        }

        // Elle seçilen seviye B-karenin gerektirdiğini taşıyamıyorsa kart "Invalid Level" verir (açıklaması: seviye düşük).
        var (w, h) = CiktiBoyutu(a, media);
        return H264Seviye.EnFazlaDpb(seviye, w, h) is { } enFazla
            ? Math.Max(Math.Min(6, enFazla), NvencEnAzDpb(n, profil))
            : null;
    }

    /// <summary>
    /// NVENC'in profili. Kayıpsızda ffmpeg profili yok sayar, dosya her zaman High 4:4:4 Predictive olur; seçilen
    /// profil yalnızca renk örneklemesini belirler (PikselBicimi: High 4:4:4 → 4:4:4, ötekiler → 4:2:0). Baseline'ın
    /// sınırları (B-kare, CABAC, ağırlıklı tahmin yok) kayıpsızda geçerli değildir: 4:2:0'da "high" sayılır.
    /// </summary>
    private static string NvencProfili(NvencAyarlari n, MediaInfo? media) =>
        n.Kayipsiz
            ? (ProfilCoz(n.Profil, media) == "high444p" ? "high444p" : "high")
            : ProfilCoz(n.Profil, media);

    /// <summary>
    /// B-kare sayısı. Baseline profilde B-kare yoktur; ağırlıklı tahmin de B-karelerle birlikte kullanılamaz
    /// (NVENC "Weighted Prediction not supported with B-frames" verip hiç açılmaz). İkisinde de 0.
    /// </summary>
    private static int? NvencBKare(NvencAyarlari n, string profil) =>
        profil == "baseline" || NvencAgirlikliTahmin(n, profil) ? 0 : n.BKare;

    /// <summary>Ağırlıklı tahmin (Baseline profilde yoktur).</summary>
    private static bool NvencAgirlikliTahmin(NvencAyarlari n, string profil) => n.AgirlikliTahmin && profil != "baseline";

    /// <summary>
    /// B-karenin gerektirdiği en az DPB: B-kare referans olarak kullanılıyorsa 3 (NVENC "DPB size should be greater
    /// than or equal to 3 for B as reference" verip açılmaz; kullanıcının kartında denendi), yalnızca B-kare varsa
    /// 2 (önceki ve sonraki kare), B-kare yoksa 1. B-kare sayısı boşsa kartın varsayılanı B-kareli sayılır.
    /// </summary>
    private static int NvencEnAzDpb(NvencAyarlari n, string profil) => NvencBKare(n, profil) switch
    {
        0 => 1,
        null => 3,
        _ => n.BRefModu == "disabled" ? 2 : 3,
    };

    /// <summary>
    /// Entropi kodlaması: Baseline ve High 4:4:4 profilde kart yalnızca CAVLC kullanır (Baseline'da CABAC yoktur;
    /// NVENC 4:4:4'ü yalnızca CAVLC ile kodlar, CABAC seçilse de; kullanıcının kartında denendi).
    /// </summary>
    private static string NvencEntropi(NvencAyarlari n, string profil) =>
        profil is "baseline" or "high444p" ? "cavlc" : n.Kodlama;

    /// <summary>
    /// x264'e verilecek seviye. "kaynak" ise kaynağın seviyesi; ama çıktının boyutu, kare hızı, VBV'si (en yüksek
    /// bit hızı / tampon) ya da referans kareleri (DPB) o seviyenin sınırını aşıyorsa yeten en düşük seviye
    /// (NVENC'teki gibi). x264 aşan seviyeyle de kodlar, yalnızca uyarır; ama dosya kendi yazdığı seviyeye uymazdı.
    /// </summary>
    public static string X264Seviyesi(ProfilAyarlari a, MediaInfo? media)
    {
        var x = a.Video.X264;
        var seviye = SeviyeCoz(x.Seviye, media);
        if (x.Seviye != Kaynak || media is null || string.IsNullOrEmpty(seviye))
        {
            return seviye;
        }

        var (w, h) = CiktiBoyutu(a, media);
        var fps = CiktiKareHizi(a, media);
        var (vbvMaks, vbvTampon) = X264Vbv(x);
        var gerekli = H264Seviye.EnAz(
            w, h, fps, vbvMaks, vbvTampon, X264Dpb(x, AnahtarKareHesabi.Aralik(a.AnahtarKare, fps).EnBuyuk),
            high: X264HighYazilirMi(x, media));
        return H264Seviye.EnYuksek(seviye, gerekli);
    }

    /// <summary>
    /// x264'ün kullanacağı VBV (kb/s, kbit); kullanmayacaksa (null, null). x264 VBV'yi yalnızca tampon verilince
    /// kullanır: CRF'de en yüksek bit hızı da gerekir (yoksa tamponu yok sayar), ortalama bit hızı ve 2 / 3 geçişte
    /// en yüksek bit hızı boşsa ortalamaya eşitlenir; sabit QP'de VBV yoktur. Ek parametrelerdeki değer önce gelir.
    /// </summary>
    private static (int? Maks, int? Tampon) X264Vbv(X264Ayarlari x)
    {
        if (x.Mod == X264Modu.SabitQp)
        {
            return (null, null);
        }

        var cbr = x.Mod == X264Modu.Cbr;
        var maks = EkSayi(x, "vbv-maxrate") ?? (cbr ? x.BitHizi : x.VbvMaks);
        var tampon = EkSayi(x, "vbv-bufsize") ?? (cbr ? x.VbvTampon ?? x.BitHizi * 2 : x.VbvTampon);
        if (tampon is not > 0)
        {
            return (null, null);
        }

        if (maks is > 0)
        {
            return (maks, tampon);
        }

        // En yüksek bit hızı yok: CRF'de x264 tamponu yok sayar; ortalama bit hızında "assuming CBR" der.
        if (x.Mod == X264Modu.Crf)
        {
            return (null, null);
        }

        return (x.BitHizi, tampon);
    }

    /// <summary>
    /// Seviyenin taşıması gereken DPB (çözücünün saklayacağı kare sayısı), x264'ün hesabıyla: B-kare varsa en az 2,
    /// B-piramitte (x264 piramidi 2'den az B-karede kapatır) en az 4; referans kare sayısı açıkça verildiyse en az o
    /// kadar. Referans boşsa ffmpeg hız ayarının referansını seviyenin taşıyabileceğine kendisi kısar (seviyeyi
    /// zorlamaz). Boş bırakılan B-kare hız ayarından (preset / tune); ek parametrelerdeki değer önce gelir.
    /// </summary>
    private static int X264Dpb(X264Ayarlari x, int? anahtarKareAraligi)
    {
        var refs = EkSayi(x, "ref") ?? x.Ref;
        var bKare = EkSayi(x, "bframes") ?? x.BKare ?? X264HizAyarininBKaresi(x.OnAyar, x.Ayar);
        if (x.BlurayUyumlu || EkSayi(x, "bluray-compat") is > 0)
        {
            // x264 Blu-ray uyumunda en çok 6 referans, 3 B-kare kullanır.
            refs = refs is { } r ? Math.Min(r, 6) : (int?)null;
            bKare = Math.Min(bKare, 3);
        }

        bKare = Math.Clamp(bKare, 0, anahtarKareAraligi is { } g && g > 1 ? Math.Min(16, g - 1) : 16);
        var piramit = EkDeger(x, "b-pyramid") ?? x.BPiramit ?? "normal";
        var bKareIcin = bKare >= 2 && piramit is not ("none" or "0") ? 4 : bKare > 0 ? 2 : 1;
        return Math.Max(refs is { } n ? Math.Clamp(n, 1, 16) : 1, bKareIcin);
    }

    /// <summary>x264 hız ayarının (preset) ve ayarının (tune) B-kare sayısı (x264'teki gibi).</summary>
    private static int X264HizAyarininBKaresi(string? onAyar, string? ayar)
    {
        var bKare = onAyar?.Trim().ToLowerInvariant() switch
        {
            "ultrafast" => 0,
            "veryslow" => 8,
            "placebo" => 16,
            _ => 3, // x264'ün varsayılanı
        };

        // x264 tune'ları hız ayarından sonra ve yazıldıkları sırayla uygular.
        foreach (var t in (ayar ?? string.Empty).Split(new[] { ',', '.', '/', '-', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (t.ToLowerInvariant())
            {
                case "animation":
                    bKare += 2;
                    break;
                case "zerolatency":
                    bKare = 0;
                    break;
            }
        }

        return bKare;
    }

    /// <summary>
    /// x264 dosyaya özelliklerin gerektirdiği en düşük profili yazar; High'ın 1,25 kat bit hızı / tampon sınırı
    /// yalnızca dosya gerçekten High (8x8 DCT ya da düz olmayan niceleme matrisi) olunca geçerli. Profil "kaynakla
    /// aynı" ve kaynak High ise program 8x8 DCT'yi açar. Emin olunamayan durumda Main sınırı (daha yüksek seviye).
    /// </summary>
    private static bool X264HighYazilirMi(X264Ayarlari x, MediaInfo? media)
    {
        var profil = ProfilCoz(x.Profil, media);
        if (!profil.StartsWith("high", StringComparison.Ordinal))
        {
            return false;
        }

        // Ek parametreler komutun sonunda (x264 son değeri kullanır); sonra programın açtığı 8x8 DCT; sonra ayar /
        // hız ayarı (yalnızca ultrafast 8x8 DCT'yi kapatır).
        var ek = EkSayi(x, "8x8dct");
        var dct = ek is not null
            ? ek > 0
            : (x.Profil == Kaynak && profil == "high")
              || (x.Dct8x8 ?? !string.Equals(x.OnAyar, "ultrafast", StringComparison.OrdinalIgnoreCase));
        var cqm = x.Cqm is { } c && !string.Equals(c, "flat", StringComparison.OrdinalIgnoreCase);
        return dct || cqm;
    }

    /// <summary>
    /// Ek x264 parametrelerinde (anahtar=değer:anahtar=değer) anahtarın son değeri (x264 sonuncuyu kullanır);
    /// "no-anahtar=1" 0 sayılır (x264'teki gibi); yoksa null.
    /// </summary>
    private static string? EkDeger(X264Ayarlari x, string anahtar)
    {
        string? deger = null;
        foreach (var parca in x.EkParametreler.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var esit = parca.IndexOf('=');
            var ad = (esit < 0 ? parca : parca[..esit]).Trim().Replace('_', '-').ToLowerInvariant();
            if (ad == anahtar)
            {
                deger = esit < 0 ? "1" : parca[(esit + 1)..].Trim();
            }
            else if (ad == "no-" + anahtar)
            {
                var v = esit < 0 ? "1" : parca[(esit + 1)..].Trim().ToLowerInvariant();
                deger = v is "0" or "false" or "no" ? "1" : "0";
            }
        }

        return deger;
    }

    /// <summary>Ek parametredeki sayı; açık / kapalı değerleri (true / yes, false / no) 1 / 0 sayılır.</summary>
    private static int? EkSayi(X264Ayarlari x, string anahtar) => EkDeger(x, anahtar)?.ToLowerInvariant() switch
    {
        null => null,
        "true" or "yes" => 1,
        "false" or "no" => 0,
        var d => int.TryParse(d, NumberStyles.Integer, Inv, out var n) ? n : (int?)null,
    };

    private static void X264Bayraklari(List<string> args, ProfilAyarlari a, int gecis, string gecisDosyasi, MediaInfo? media)
    {
        var x = a.Video.X264;
        var profil = ProfilCoz(x.Profil, media);
        var seviye = X264Seviyesi(a, media);
        args.AddRange(["-c:v", "libx264", "-preset", x.OnAyar]);
        if (!string.IsNullOrEmpty(x.Ayar))
        {
            args.AddRange(["-tune", x.Ayar]);
        }

        if (!string.IsNullOrEmpty(profil))
        {
            args.AddRange(["-profile:v", profil]);
        }

        if (!string.IsNullOrEmpty(seviye))
        {
            args.AddRange(["-level:v", seviye]);
        }

        if (x.Mod is X264Modu.OrtalamaBitHizi or X264Modu.IkiGecis or X264Modu.UcGecis or X264Modu.Cbr)
        {
            args.AddRange(["-b:v", x.BitHizi.ToString(Inv) + "k"]);
        }

        if (gecis != 0)
        {
            args.AddRange(["-pass", gecis.ToString(Inv), "-passlogfile", gecisDosyasi]);
            if (gecis == 1)
            {
                args.AddRange(["-fastfirstpass", x.HizliIlkGecis ? "1" : "0"]);
            }
        }
    }

    private static void Nvenc(List<string> args, ProfilAyarlari a, double fps, MediaInfo? media)
    {
        var n = a.Video.Nvenc;
        var profil = NvencProfili(n, media);
        var seviye = NvencSeviyesi(a, media);
        var k = a.AnahtarKare;
        args.AddRange(["-c:v", "h264_nvenc", "-preset", n.OnAyar]);
        if (!string.IsNullOrEmpty(n.Ayar))
        {
            args.AddRange(["-tune", n.Ayar]);
        }

        // Kayıpsızda ffmpeg profili yok sayar (nvenc.c yalnızca kayıpsız değilken yazar); kart High 4:4:4 Predictive kullanır.
        if (!string.IsNullOrEmpty(profil) && !n.Kayipsiz)
        {
            args.AddRange(["-profile:v", profil]);
        }

        if (!string.IsNullOrEmpty(seviye))
        {
            args.AddRange(["-level:v", seviye]);
        }

        static string Kb(int v) => v.ToString(Inv) + "k";
        if (n.Kayipsiz)
        {
            // Kayıpsızda ffmpeg kodlama modunu ve kaliteyi yok sayar, sabit QP 0 kullanır (nvenc.c set_lossless);
            // komut da bunu söylesin. -b:v 0: sabit QP'deki gibi (verilmezse ffmpeg'in 2 Mb/s'i HRD'ye yazılır).
            args.AddRange(["-rc", "constqp", "-qp", "0", "-b:v", "0"]);
        }
        else
        {
            switch (n.Mod)
            {
                case NvencModu.SabitKalite:
                    args.AddRange(["-rc", "vbr", "-cq", n.Cq.ToString(Inv), "-b:v", "0"]);
                    break;
                case NvencModu.SabitQp:
                    // -b:v 0: verilmezse ffmpeg'in varsayılanı (2 Mb/s, tampon 4000 kbit) dosyanın HRD başlığına yazılır,
                    // oysa sabit QP'de bit hızı sınırı yoktur (kullanıcının kartında denendi: 12,6 Mb/s'lik dosyada
                    // "2000 kb/s"). 0 ile kart kendi varsayılanını yazar; görüntü bit bit aynı kalır.
                    args.AddRange(["-rc", "constqp", "-qp", n.Qp.ToString(Inv), "-b:v", "0"]);
                    break;
                case NvencModu.DegiskenBitHizi:
                    args.AddRange(["-rc", "vbr", "-b:v", Kb(n.BitHizi)]);
                    break;
                default:
                    args.AddRange(["-rc", "cbr", "-b:v", Kb(n.BitHizi)]);
                    break;
            }
        }

        // En yüksek bit hızı yalnızca VBR ve sabit kalitede: CBR'de kart onu yok sayar (bit hızı sabittir), kayıpsızda
        // bit hızı sınırı yoktur.
        if (n.MaksBitHizi is { } mx && !n.Kayipsiz && n.Mod is NvencModu.DegiskenBitHizi or NvencModu.SabitKalite)
        {
            args.AddRange(["-maxrate", Kb(mx)]);
        }

        // Tampon yalnızca CBR ve VBR'de (sabit kalitede ffmpeg yok sayar, QP'de yoktur). CBR'de boşsa bit hızı
        // kadar (1 saniye): ffmpeg'in varsayılanı (2 katı) Main@4.1 / 4.2'nin tampon sınırını (62500 kbit) aşar ve
        // NVENC seviyeyi reddeder. VBR'de boşsa ffmpeg'e bırakılır (2 katı).
        if ((n.Mod == NvencModu.Cbr || (n.Mod == NvencModu.DegiskenBitHizi && n.Tampon is not null))
            && NvencTampon(n) is { } tb)
        {
            args.AddRange(["-bufsize", Kb(tb)]);
        }

        args.AddRange(["-multipass", n.CokGecis]);
        if (n.IleriBakis is { } la)
        {
            args.AddRange(["-rc-lookahead", la.ToString(Inv)]);
        }

        // Kayıpsızda uyarlamalı niceleme yoktur: kart açılmaz ("Adaptive quantization is not supported with lossless
        // encoding"; kullanıcının kartında denendi). QP her yerde 0 olduğundan AQ'nun değiştireceği bir şey de yoktur.
        var uzamsalAq = n.UzamsalAq && !n.Kayipsiz;
        args.AddRange(["-spatial-aq", uzamsalAq ? "1" : "0"]);
        if (uzamsalAq)
        {
            args.AddRange(["-aq-strength", n.AqGucu.ToString(Inv)]);
        }

        args.AddRange(["-temporal-aq", n.ZamansalAq && !n.Kayipsiz ? "1" : "0"]);
        if (NvencBKare(n, profil) is { } bf)
        {
            args.AddRange(["-bf", bf.ToString(Inv)]);
            if (bf > 0)
            {
                args.AddRange(["-b_ref_mode", n.BRefModu]);
            }
        }

        if (n.Ref is { } refs)
        {
            args.AddRange(["-refs", refs.ToString(Inv)]);
        }

        if (NvencDpb(n, seviye, profil, a, media) is { } dpb)
        {
            args.AddRange(["-dpb_size", dpb.ToString(Inv)]);
        }

        if (NvencAgirlikliTahmin(n, profil))
        {
            args.AddRange(["-weighted_pred", "1"]);
        }

        // Baseline'da CABAC yoktur (kaynak Baseline ise "kaynakla aynı" profil buna düşer); 4:4:4'te kart CAVLC kullanır.
        args.AddRange(["-coder", NvencEntropi(n, profil)]);

        if (Kare(k, k.EnBuyuk, fps) is { } g)
        {
            args.AddRange(["-g", g.ToString(Inv)]);
        }

        if (k.SahneDegisimi == 0)
        {
            args.AddRange(["-no-scenecut", "1"]);
        }

        if (k.IntraYenileme)
        {
            args.AddRange(["-intra-refresh", "1"]);
        }

        if (!string.IsNullOrWhiteSpace(n.EkParametreler))
        {
            args.AddRange(n.EkParametreler.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }

    // --------------------------------------------------------------------------- Ses

    /// <summary>
    /// Sesin yüksekliğini değiştirmeden önceki filtreler: gecikme, seviye, mono → stereo. LUFS eşitlemede ölçüm de
    /// bunlardan sonraki sese yapılır (kodlamada sabit kazanç aynı yerde durur).
    /// <paramref name="kanalIndir"/>: LUFS eşitlemede çıktının kanal sayısına (mono / stereo) burada inilir; yükseklik
    /// çıktıdaki kanallarla ölçülmeli (stereo ölçülüp sonra mono'ya inilince sonuç hedefin 3–6 dB altında kalır).
    /// </summary>
    private static List<string> OnSesFiltreleri(SesAyarlari s, int? kaynakKanal, bool kanalIndir = false)
    {
        var af = new List<string>();
        if (s.Gecikme > 0)
        {
            af.Add(string.Create(Inv, $"adelay={s.Gecikme}:all=1"));
        }
        else if (s.Gecikme < 0)
        {
            af.Add(string.Create(Inv, $"atrim=start={(-s.Gecikme / 1000.0):0.###},asetpts=PTS-STARTPTS"));
        }

        if (s.Seviye != 0)
        {
            af.Add(string.Create(Inv, $"volume={s.Seviye:0.#}dB"));
        }

        if (s.Kanal == KanalModu.Stereo && kaynakKanal == 1)
        {
            // Mono ses iki kanala aynen (tam seviyede) yazılır: ffmpeg'in "-ac 2" açılışı her kanalı -3 dB kısar,
            // video stereo çalınınca kaynaktan sessiz kalırdı. Oynatıcılar da mono sesi iki hoparlöre aynen verir.
            af.Add("pan=stereo|c0=c0|c1=c0");
        }
        else if (kanalIndir && s.Kanal == KanalModu.Mono && kaynakKanal != 1)
        {
            // Stereo → mono: ffmpeg'in "-ac 1" ile yaptığının aynısı (iki kanalın yarısı); başka kanal sayısında
            // ffmpeg'in kendi indirmesi.
            af.Add(kaynakKanal == 2 ? "pan=mono|c0=0.5*c0+0.5*c1" : "aformat=channel_layouts=mono");
        }
        else if (kanalIndir && s.Kanal == KanalModu.Stereo && kaynakKanal is > 2)
        {
            af.Add("aformat=channel_layouts=stereo");
        }

        return af;
    }

    private static string Db(double d) => d.ToString("0.0#", Inv);

    /// <summary>soxr ve seçilen dönüşüm kalitesi: Yüksek soxr'un varsayılanı (20 bit), Çok yüksek 28, En yüksek 33 bit.</summary>
    private static string SoxrAyari(SesAyarlari s) => s.Kalite switch
    {
        DonusumKalitesi.Yuksek => "resampler=soxr",
        DonusumKalitesi.CokYuksek => "resampler=soxr:precision=28",
        _ => "resampler=soxr:precision=33",
    };

    /// <summary>
    /// LUFS eşitlemenin ölçüm geçişi: sesi yalnızca okur (görüntü yok); ölçer değerleri ffmpeg çıktısına yazar
    /// (okumak için <see cref="OlcumOku"/>). Süre videonunkiyle sınırlanır (harici ses videodan uzunsa sonu çıktıda kesilir).
    /// </summary>
    public static List<string> OlcumKomutu(SesAyarlari s, string input, SesSecimi ses, double sureSaniye)
    {
        var harici = ses.Tur == SesSecimTuru.HariciDosya;
        var args = new List<string> { "-hide_banner", "-nostdin", "-nostats", "-i", harici ? ses.Dosya! : input };
        args.AddRange(["-map", harici ? "0:a:0" : "0:a:" + ses.Iz.ToString(Inv), "-vn", "-sn", "-dn"]);
        if (sureSaniye > 0)
        {
            args.AddRange(["-t", sureSaniye.ToString("0.###", Inv)]);
        }

        var af = OnSesFiltreleri(s, ses.Kanal, kanalIndir: true);
        af.Add(Esitleme.OlcumFiltresi);
        args.AddRange(["-af", string.Join(',', af), "-f", "null", "-"]);
        return args;
    }

    /// <summary>
    /// Ölçerin ffmpeg çıktısından değerleri okur: tümleşik yükseklik son "lavfi.r128.I=" satırından (her 100 ms'de
    /// yazılır; sonuncusu bütün sesin değeri), gerçek tepe ve yükseklik aralığı en sondaki özetten ("Summary:").
    /// Ses sessizse ya da 0,4 saniyeden kısaysa ölçer -70 LUFS yazar; o zaman ya da okunamazsa null: eşitleme
    /// yapılmaz (sessiz sesi yükseltmeye çalışmak hata verir).
    /// </summary>
    public static SesOlcum? OlcumOku(string gunluk)
    {
        static double? Sayi(string metin) =>
            double.TryParse(metin, NumberStyles.Float, Inv, out var d) && double.IsFinite(d) ? d : null;

        var iler = System.Text.RegularExpressions.Regex.Matches(gunluk, @"lavfi\.r128\.I=(\S+)");
        var ozet = gunluk.LastIndexOf("Summary:", StringComparison.Ordinal);
        if (iler.Count == 0 || ozet < 0)
        {
            return null;
        }

        var aralik = System.Text.RegularExpressions.Regex.Match(gunluk[ozet..], @"LRA:\s+(\S+) LU");
        var tepe = System.Text.RegularExpressions.Regex.Match(gunluk[ozet..], @"True peak:\s+Peak:\s+(\S+) dBFS");
        if (Sayi(iler[^1].Groups[1].Value) is not { } i || i <= Esitleme.MutlakKapi
            || !tepe.Success || Sayi(tepe.Groups[1].Value) is not { } tp
            || !aralik.Success || Sayi(aralik.Groups[1].Value) is not { } lra)
        {
            return null;
        }

        return new SesOlcum(i, tp, lra);
    }

    /// <summary>
    /// Sesin filtre zinciri (kodlamada ve telafi ölçümünde aynı). LUFS eşitlemede bütün ses aynı miktarda (sabit
    /// kazanç: hedef - ölçülen + telafi) hedefe getirilir; sesin dengesi değişmez. Kazanç tepeyi tavanın üstüne
    /// çıkaracaksa sınırlayıcı (kutu kapalı olsa da) tepeleri tavanda tutar. <paramref name="hedefHz"/>: çıktının
    /// örnekleme hızı (null: kaynakla aynı ve dönüşüm yok).
    /// </summary>
    private static List<string> SesFiltreleri(
        SesAyarlari s, bool harici, int? kaynakKanal, SesOlcum? olcum, double telafi, int? kaynakHz, out int? hedefHz)
    {
        var esitleme = s.Esitleme && olcum is not null;
        var af = OnSesFiltreleri(s, kaynakKanal, kanalIndir: esitleme);
        var esitlemeSinirli = false;
        if (esitleme)
        {
            var o = olcum!;
            // Telafi de kazançtır: AAC'nin payı da tepeyi tavanın üstüne çıkarabilir (tepe tavana çok yakınsa).
            esitlemeSinirli = Esitleme.SinirlanirMi(o, s.EsitlemeHedef, s.SinirlayiciTavan, telafi);
            af.Add($"volume={Db(s.EsitlemeHedef - o.Yukseklik + telafi)}dB");
        }

        // Sınırlayıcı 192 kHz'te (4 kat örneklemede) çalışır: örneklerin arasındaki gerçek tepeler de tavanda kalır.
        // Kaynağın hızında çalışsaydı yalnızca örnekleri sınırlardı; tiz ya da sıkıştırılmış seste gerçek tepe tavanı
        // birkaç dB aşabilir, 44,1 kHz kaynak 48 kHz'e çevrilirken tam ölçekte kırpılabilirdi.
        var sinirla = (s.Sinirlayici && s.Seviye > 0) || esitlemeSinirli;
        if (sinirla)
        {
            af.Add("aresample=192000:" + SoxrAyari(s));

            // Tavan doğrusal değere çevrilir (-1 dB = 0,891); otomatik seviye kapalı (sesi kendisi yükseltmez),
            // gecikmesi telafi edilir (senkron kaymaz).
            var tavan = Math.Pow(10, s.SinirlayiciTavan / 20);
            af.Add(string.Create(Inv, $"alimiter=limit={tavan:0.######}:level=0:latency=1"));
        }

        // 192 kHz'ten dönüş: örnekleme hızı "kaynakla aynı" ise kaynağın hızına.
        hedefHz = s.OrnekHizi ?? (sinirla ? kaynakHz ?? 48000 : null);
        var kaliteVar = s.Kalite != DonusumKalitesi.Yuksek || s.Titresim != Titresim.Yok;
        if (hedefHz is not null || kaliteVar)
        {
            var r = new List<string>();
            if (hedefHz is { } hz)
            {
                r.Add(hz.ToString(Inv));
            }

            r.Add(SoxrAyari(s));

            if (s.Titresim != Titresim.Yok)
            {
                r.Add("dither_method=" + s.Titresim switch
                {
                    Titresim.Ucgen => "triangular",
                    Titresim.UcgenYuksekGecis => "triangular_hp",
                    _ => "shibata",
                });
            }

            af.Add("aresample=" + string.Join(':', r));
        }

        if (harici)
        {
            // Harici ses videodan kısaysa sonu sessizlikle doldurulur, uzunsa video bitince kesilir.
            af.Add("apad");
        }

        return af;
    }

    /// <summary>
    /// LUFS eşitlemede telafi ölçümü: sesin kodlamadaki filtre zinciri (sabit kazanç + sınırlayıcı) uygulanıp yüksekliği
    /// ölçülür (yalnızca ses okunur). Sınırlayıcı tepeleri kıstıkça yükseklik hedefin altında kalır; eksik kadar telafi
    /// eklenip yeniden denenir.
    /// </summary>
    public static List<string> DenemeOlcumKomutu(
        SesAyarlari s, string input, SesSecimi ses, double sureSaniye, SesOlcum olcum, double telafi)
    {
        var harici = ses.Tur == SesSecimTuru.HariciDosya;
        var args = new List<string> { "-hide_banner", "-nostdin", "-nostats", "-i", harici ? ses.Dosya! : input };
        args.AddRange(["-map", harici ? "0:a:0" : "0:a:" + ses.Iz.ToString(Inv), "-vn", "-sn", "-dn"]);
        if (sureSaniye > 0)
        {
            args.AddRange(["-t", sureSaniye.ToString("0.###", Inv)]);
        }

        var af = SesFiltreleri(s, harici: false, ses.Kanal, olcum, telafi, ses.OrnekHizi, out _);
        af.Add(Esitleme.OlcumFiltresi);
        args.AddRange(["-af", string.Join(',', af), "-f", "null", "-"]);
        return args;
    }

    /// <summary>
    /// AAC çıktıda telafi denemesi, 1. adım: ses kodlamadaki zincir (sabit kazanç + telafi, gerekirse sınırlayıcı) ve
    /// aynı kodlayıcı ayarlarıyla (FDK AAC) geçici dosyaya kodlanır (yalnızca ses). 2. adımda dosya
    /// <see cref="Esitleme.SonucOlcumKomutu"/> ile ölçülür. FDK AAC aynı sesten aynı sonucu verdiği için ölçülen
    /// değer, asıl kodlamadaki sesin yüksekliğidir.
    /// </summary>
    public static List<string> DenemeKodlamaKomutu(
        SesAyarlari s, string input, SesSecimi ses, double sureSaniye, SesOlcum olcum, double telafi, string cikti)
    {
        var harici = ses.Tur == SesSecimTuru.HariciDosya;
        var args = new List<string> { "-hide_banner", "-nostdin", "-nostats", "-y", "-i", harici ? ses.Dosya! : input };
        args.AddRange(["-map", harici ? "0:a:0" : "0:a:" + ses.Iz.ToString(Inv), "-vn", "-sn", "-dn"]);
        if (sureSaniye > 0)
        {
            args.AddRange(["-t", sureSaniye.ToString("0.###", Inv)]);
        }

        Ses(args, s, harici: false, ses.Kanal, olcum, ses.OrnekHizi, telafi);
        args.AddRange(["-f", "mp4", cikti]);
        return args;
    }

    private static void Ses(
        List<string> args, SesAyarlari s, bool harici, int? kaynakKanal = null, SesOlcum? olcum = null,
        int? kaynakHz = null, double telafi = 0)
    {
        var af = SesFiltreleri(s, harici, kaynakKanal, olcum, telafi, kaynakHz, out var hedefHz);
        if (af.Count > 0)
        {
            args.AddRange(["-af", string.Join(',', af)]);
        }

        if (s.Kodlayici == SesKodlayici.FdkAac)
        {
            var f = s.Fdk;
            args.AddRange(["-c:a", "libfdk_aac", "-profile:a", f.Profil]);
            if (f.Mod == FdkModu.Cbr)
            {
                args.AddRange(["-b:a", f.BitHizi.ToString(Inv) + "k"]);
            }
            else
            {
                args.AddRange(["-vbr", ((int)f.Mod).ToString(Inv)]);
            }

            if (f.Kesim > 0)
            {
                // FFmpeg'in FDK AAC sınırı: (örnekleme hızı + 255) / 256 ile 20000 Hz arası (48 kHz'te en az 188).
                var hz = hedefHz ?? kaynakHz ?? 48000;
                args.AddRange(["-cutoff", Math.Clamp(f.Kesim, (hz + 255) >> 8, FdkAyarlari.EnCokKesim).ToString(Inv)]);
            }

            if (!f.Afterburner)
            {
                args.AddRange(["-afterburner", "0"]);
            }

            if (f.Sinyal != "default")
            {
                args.AddRange(["-signaling", f.Sinyal]);
            }

            void Opt(string name, int? value)
            {
                if (value is { } x)
                {
                    args.AddRange([name, x.ToString(Inv)]);
                }
            }

            Opt("-drc_profile", f.DrcProfili);
            Opt("-comp_profile", f.SikistirmaProfili);
            Opt("-drc_target_ref", f.DrcHedef);
            Opt("-comp_target_ref", f.SikistirmaHedef);
            Opt("-prog_ref", f.ProgramSeviyesi);
        }
        else
        {
            args.AddRange(["-c:a", s.WavBit switch
            {
                WavBitDerinligi.Bit16 => "pcm_s16le",
                WavBitDerinligi.Bit24 => "pcm_s24le",
                WavBitDerinligi.Bit32 => "pcm_s32le",
                _ => "pcm_f32le",
            }]);
        }

        if (hedefHz is { } ar)
        {
            args.AddRange(["-ar", ar.ToString(Inv)]);
        }

        if (s.Kanal != KanalModu.Kaynak)
        {
            args.AddRange(["-ac", s.Kanal == KanalModu.Mono ? "1" : "2"]);
        }

        args.AddRange(["-metadata:s:a:0", "language=eng"]);
        if (harici)
        {
            args.Add("-shortest");
        }
    }

    /// <summary>Kaynak renk matrisi: etiket varsa ffmpeg okur (auto), yoksa çözünürlüğe göre BT.709 / BT.601.</summary>
    public static string InputMatrix(MediaInfo m)
    {
        if (!m.ColorSpaceUnknown)
        {
            return "auto";
        }

        var w = m.DisplayWidth;
        var h = m.DisplayHeight;
        return Math.Min(w, h) >= 720 || Math.Max(w, h) >= 1280 ? "bt709" : "bt601";
    }

    private static int Even(double v)
    {
        var r = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        return r % 2 == 0 ? r : r + 1;
    }
}
