using System.Globalization;
using System.IO;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Core;

/// <summary>Kaynağı hedef en-boy oranına ortadan kırpma hesabının sonucu.</summary>
public sealed record CropPlan(int Width, int Height, int X, int Y, bool IsNeeded);

public enum SesSecimTuru { Yok, Video, HariciDosya }

/// <summary>Çıktıya hangi sesin gireceği: videonun kendi sesi, aynı adlı harici dosya ya da hiç.</summary>
public sealed record SesSecimi(SesSecimTuru Tur, string? Dosya = null)
{
    public static readonly SesSecimi Yok = new(SesSecimTuru.Yok);
    public static readonly SesSecimi Video = new(SesSecimTuru.Video);

    public string Aciklama => Tur switch
    {
        SesSecimTuru.HariciDosya => Path.GetFileName(Dosya) ?? "harici ses",
        SesSecimTuru.Video => "videonun sesi",
        _ => "ses yok",
    };
}

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
                return media.HasAudio ? SesSecimi.Video : SesSecimi.Yok;
        }
    }

    public static KomutPlani Olustur(
        ProfilAyarlari a,
        MediaInfo media,
        string input,
        SesSecimi ses,
        string output,
        string? gecisDosyasi = null)
    {
        var v = a.Video;
        var gecisSayisi = v.Kodlayici == VideoKodlayici.X264
            ? v.X264.Mod switch { X264Modu.IkiGecis => 2, X264Modu.UcGecis => 3, _ => 1 }
            : 1;
        gecisDosyasi ??= Path.Combine(Path.GetTempPath(), "relios-export-gecis");

        var gecisler = new List<IReadOnlyList<string>>();
        if (gecisSayisi == 1)
        {
            gecisler.Add(Komut(a, media, input, ses, output, gecis: 0, gecisDosyasi));
        }
        else
        {
            // 2 geçiş: 1 → 2. 3 geçiş: 1 → 3 (okur ve yazar) → 2 (son).
            gecisler.Add(Komut(a, media, input, ses, "-", gecis: 1, gecisDosyasi));
            if (gecisSayisi == 3)
            {
                gecisler.Add(Komut(a, media, input, ses, "-", gecis: 3, gecisDosyasi));
            }

            gecisler.Add(Komut(a, media, input, ses, output, gecis: 2, gecisDosyasi));
        }

        return new KomutPlani(gecisler, Uzanti(a));
    }

    /// <summary>Ses WAV olarak yazılacaksa MP4 olmaz: MOV kabı kullanılır.</summary>
    public static string Uzanti(ProfilAyarlari a) => a.Ses.Kodlayici == SesKodlayici.Wav ? ".mov" : ".mp4";

    private static List<string> Komut(
        ProfilAyarlari a,
        MediaInfo media,
        string input,
        SesSecimi ses,
        string output,
        int gecis,
        string gecisDosyasi)
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
            args.AddRange(["-map", harici ? "1:a:0" : "0:a:0"]);
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
            Ses(args, a.Ses, harici);
        }

        args.AddRange(
        [
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

    private static int? Kare(AnahtarKareAyarlari k, double? deger, double fps)
    {
        if (deger is not { } d)
        {
            return null;
        }

        var kare = k.Birim == AralikBirimi.Saniye ? Math.Round(d * fps, MidpointRounding.AwayFromZero) : Math.Round(d);
        return Math.Max(1, (int)kare);
    }

    private static void X264(List<string> args, ProfilAyarlari a, MediaInfo media, int gecis, string gecisDosyasi)
    {
        var x = a.Video.X264;
        var p = X264Parametreleri(a, CiktiKareHizi(a, media), media);
        X264Bayraklari(args, x, gecis, gecisDosyasi, media);
        args.AddRange(["-x264-params", string.Join(':', p)]);
    }

    /// <summary>-x264-params değeri (ayar penceresindeki önizleme için de kullanılır).</summary>
    public static string X264ParametreMetni(ProfilAyarlari a, double fps) => string.Join(':', X264Parametreleri(a, fps));

    // --------------------------------------------------------------------------- Önizleme

    /// <summary>Ayar penceresindeki "Komut" sekmesi: x264 için ffmpeg'e verilecek video seçenekleri (tek geçiş).</summary>
    public static string X264KomutMetni(ProfilAyarlari a, double fps)
    {
        var args = new List<string>();
        X264Bayraklari(args, a.Video.X264, 0, string.Empty, null);
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
        Ekle("keyint", Kare(k, k.EnBuyuk, fps));
        Ekle("min-keyint", Kare(k, k.EnKucuk, fps));
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

        return m.VideoCodec == "h264" && m.VideoSeviye is { } l
            ? l == 9 ? "1b" : string.Create(Inv, $"{l / 10}.{l % 10}")
            : string.Empty;
    }

    private static void X264Bayraklari(List<string> args, X264Ayarlari x, int gecis, string gecisDosyasi, MediaInfo? media)
    {
        var profil = ProfilCoz(x.Profil, media);
        var seviye = SeviyeCoz(x.Seviye, media);
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
        var profil = ProfilCoz(n.Profil, media);
        var seviye = SeviyeCoz(n.Seviye, media);
        var k = a.AnahtarKare;
        args.AddRange(["-c:v", "h264_nvenc", "-preset", n.OnAyar]);
        if (!string.IsNullOrEmpty(n.Ayar))
        {
            args.AddRange(["-tune", n.Ayar]);
        }

        if (!string.IsNullOrEmpty(profil))
        {
            args.AddRange(["-profile:v", profil]);
        }

        if (!string.IsNullOrEmpty(seviye))
        {
            args.AddRange(["-level:v", seviye]);
        }

        static string Kb(int v) => v.ToString(Inv) + "k";
        switch (n.Mod)
        {
            case NvencModu.SabitKalite:
                args.AddRange(["-rc", "vbr", "-cq", n.Cq.ToString(Inv), "-b:v", "0"]);
                break;
            case NvencModu.SabitQp:
                args.AddRange(["-rc", "constqp", "-qp", n.Qp.ToString(Inv)]);
                break;
            case NvencModu.DegiskenBitHizi:
                args.AddRange(["-rc", "vbr", "-b:v", Kb(n.BitHizi)]);
                break;
            default:
                args.AddRange(["-rc", "cbr", "-b:v", Kb(n.BitHizi)]);
                break;
        }

        if (n.MaksBitHizi is { } mx && n.Mod != NvencModu.SabitQp)
        {
            args.AddRange(["-maxrate", Kb(mx)]);
        }

        if (n.Tampon is { } tb && n.Mod != NvencModu.SabitQp)
        {
            args.AddRange(["-bufsize", Kb(tb)]);
        }

        args.AddRange(["-multipass", n.CokGecis]);
        if (n.IleriBakis is { } la)
        {
            args.AddRange(["-rc-lookahead", la.ToString(Inv)]);
        }

        args.AddRange(["-spatial-aq", n.UzamsalAq ? "1" : "0"]);
        if (n.UzamsalAq)
        {
            args.AddRange(["-aq-strength", n.AqGucu.ToString(Inv)]);
        }

        args.AddRange(["-temporal-aq", n.ZamansalAq ? "1" : "0"]);
        if (n.BKare is { } bf)
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

        if (n.AgirlikliTahmin)
        {
            args.AddRange(["-weighted_pred", "1"]);
        }

        args.AddRange(["-coder", n.Kodlama]);

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

    private static void Ses(List<string> args, SesAyarlari s, bool harici)
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

        var kaliteVar = s.Kalite != DonusumKalitesi.Yuksek || s.Titresim != Titresim.Yok;
        if (s.OrnekHizi is not null || kaliteVar)
        {
            var r = new List<string>();
            if (s.OrnekHizi is { } hz)
            {
                r.Add(hz.ToString(Inv));
            }

            r.Add("resampler=soxr");
            if (s.Kalite != DonusumKalitesi.Yuksek)
            {
                r.Add("precision=" + (s.Kalite == DonusumKalitesi.CokYuksek ? "28" : "33"));
            }

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
                args.AddRange(["-cutoff", f.Kesim.ToString(Inv)]);
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

        if (s.OrnekHizi is { } ar)
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
