using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Arayuz;

/// <summary>Ayar panelindeki ve pencerelerdeki açılır listelerin seçenekleri.</summary>
public static class Secenekler
{
    private static readonly object O = OnAyarDegeri.Ornek;

    private static Secenek[] L(params (object? Deger, string Ad)[] x) => x.Select(i => new Secenek(i.Deger, i.Ad)).ToArray();

    // ------------------------------------------------------------------ Video
    public static Secenek[] Boyutlar { get; } = L(
        ("kaynak", "Kaynakla aynı"),
        ("1216x2160", "1216×2160 · 9:16 · TikTok"),
        ("1080x1920", "1080×1920 · 9:16 · Full HD"),
        ("1440x2560", "1440×2560 · 9:16 · 2K"),
        ("2160x3840", "2160×3840 · 9:16 · 4K"),
        ("1080x1350", "1080×1350 · 4:5"),
        ("1080x1080", "1080×1080 · 1:1"),
        ("1920x1080", "1920×1080 · 16:9 · Full HD"),
        ("2560x1440", "2560×1440 · 16:9 · 2K"),
        ("3840x2160", "3840×2160 · 16:9 · 4K"),
        ("ozel", "Özel boyut"));

    public static Secenek[] OranUyumlari { get; } = L(
        (OranUyumu.OrtadanKirp, "Ortadan kırp"),
        (OranUyumu.KenarEkle, "Siyah kenar ekle"),
        (OranUyumu.Esnet, "Esnet (oran bozulur)"));

    public static Secenek[] Olceklemeler { get; } = L(
        ("lanczos", "Lanczos (keskin)"),
        ("spline", "Spline"),
        ("bicubic", "Bicubic"),
        ("bilinear", "Bilinear"),
        ("area", "Area (küçültmede iyi)"),
        ("neighbor", "En yakın komşu"));

    public static Secenek[] KareHizlari { get; } = L(
        ("kaynak", "Kaynakla aynı"),
        ("24000/1001", "23,976"),
        ("24", "24"),
        ("25", "25"),
        ("30000/1001", "29,97"),
        ("30", "30"),
        ("50", "50"),
        ("60000/1001", "59,94"),
        ("60", "60"));

    public static Secenek[] VideoKodlayicilar { get; } = L(
        (VideoKodlayici.X264, "x264 (işlemci)"),
        (VideoKodlayici.Nvenc, "NVENC H.264 (ekran kartı)"));

    // ------------------------------------------------------------------ x264
    public static Secenek[] X264OnAyarlar { get; } = L(
        ("ultrafast", "ultrafast (en hızlı)"),
        ("superfast", "superfast"),
        ("veryfast", "veryfast"),
        ("faster", "faster"),
        ("fast", "fast"),
        ("medium", "medium"),
        ("slow", "slow"),
        ("slower", "slower"),
        ("veryslow", "veryslow"),
        ("placebo", "placebo (en yavaş)"));

    public static Secenek[] X264Ayarlar { get; } = L(
        ("", "Yok"),
        ("film", "Film"),
        ("animation", "Animasyon"),
        ("grain", "Gren"),
        ("stillimage", "Durağan görüntü"),
        ("psnr", "PSNR"),
        ("ssim", "SSIM"),
        ("fastdecode", "Hızlı çözme"),
        ("zerolatency", "Sıfır gecikme"));

    public static Secenek[] X264Profiller { get; } = L(
        ("kaynak", "Kaynakla aynı"),
        ("", "Otomatik"),
        ("baseline", "Baseline"),
        ("main", "Main"),
        ("high", "High"),
        ("high10", "High 10 (10 bit)"),
        ("high422", "High 4:2:2"),
        ("high444", "High 4:4:4"));

    public static Secenek[] Seviyeler { get; } = L(
        [("kaynak", "Kaynakla aynı"), ("", "Otomatik"), .. new[] { "1.0", "1.1", "1.2", "1.3", "2.0", "2.1", "2.2", "3.0", "3.1", "3.2", "4.0", "4.1", "4.2", "5.0", "5.1", "5.2", "6.0", "6.1", "6.2" }
            .Select(x => ((object?)x, x.Replace('.', ',')))]);

    public static Secenek[] X264Modlari { get; } = L(
        (X264Modu.Crf, "Sabit kalite (CRF)"),
        (X264Modu.SabitQp, "Sabit niceleyici (QP)"),
        (X264Modu.OrtalamaBitHizi, "Ortalama bit hızı (1 geçiş)"),
        (X264Modu.IkiGecis, "Değişken bit hızı · 2 geçiş"),
        (X264Modu.UcGecis, "Değişken bit hızı · 3 geçiş"),
        (X264Modu.Cbr, "Sabit bit hızı (CBR)"));

    public static Secenek[] HareketArama { get; } = L(
        (O, "Ön ayar"),
        ("dia", "Elmas (dia)"),
        ("hex", "Altıgen (hex)"),
        ("umh", "Düzensiz çoklu altıgen (umh)"),
        ("esa", "Tam arama (esa)"),
        ("tesa", "Dönüşümlü tam arama (tesa)"));

    public static Secenek[] AltPikseller { get; } = L(
        (O, "Ön ayar"),
        (0, "0: Yalnızca tam piksel"),
        (1, "1: SAD, 1 yineleme"),
        (2, "2: SATD, 2 yineleme"),
        (3, "3: Yarım piksel, MB kararından sonra"),
        (4, "4: Her zaman çeyrek piksel"),
        (5, "5: Çoklu çeyrek piksel + çift yönlü B"),
        (6, "6: RD, I/P kareler"),
        (7, "7: RD, tüm kareler"),
        (8, "8: RD iyileştirme, I/P kareler"),
        (9, "9: RD iyileştirme, tüm kareler"),
        (10, "10: QP-RD (trellis 2 ve AQ gerekir)"),
        (11, "11: Tam RD"));

    public static Secenek[] DogrudanMvler { get; } = L(
        (O, "Ön ayar"),
        ("none", "Yok"),
        ("spatial", "Uzamsal"),
        ("temporal", "Zamansal"),
        ("auto", "Otomatik"));

    public static Secenek[] AgirlikliPler { get; } = L(
        (O, "Ön ayar"),
        (0, "Kapalı"),
        (1, "Basit (ağırlıklı referans)"),
        (2, "Akıllı (ağırlıklı referans + kopya)"));

    public static Secenek[] BUyarlamalilar { get; } = L(
        (O, "Ön ayar"),
        (0, "Kapalı"),
        (1, "Hızlı"),
        (2, "En iyi (Optimal)"));

    public static Secenek[] BPiramitler { get; } = L(
        (O, "Ön ayar"),
        ("none", "Yok"),
        ("strict", "Katı (Strict)"),
        ("normal", "Normal"));

    public static Secenek[] Bolumler { get; } = L(
        (O, "Ön ayar"),
        ("none", "Yok"),
        ("i4x4,i8x8", "Yalnızca intra"),
        ("p8x8,b8x8,i8x8,i4x4", "Standart"),
        ("all", "Hepsi"));

    public static Secenek[] Trellisler { get; } = L(
        (O, "Ön ayar"),
        (0, "Kapalı"),
        (1, "Yalnızca son MB"),
        (2, "Tüm kararlarda"));

    public static Secenek[] Cqmler { get; } = L(
        (O, "Ön ayar"),
        ("flat", "Düz (flat)"),
        ("jvt", "JVT"));

    public static Secenek[] AqModlari { get; } = L(
        (O, "Ön ayar"),
        (0, "Kapalı"),
        (1, "Varyans AQ"),
        (2, "Otomatik varyans AQ"),
        (3, "Otomatik varyans AQ (koyu sahnelere ağırlık)"));

    public static Secenek[] NalHrdler { get; } = L(
        (O, "Ön ayar"),
        ("none", "Yok"),
        ("vbr", "VBR"),
        ("cbr", "CBR"));

    // ------------------------------------------------------------------ NVENC
    public static Secenek[] NvencOnAyarlar { get; } = L(
        ("p1", "p1 (en hızlı)"),
        ("p2", "p2"),
        ("p3", "p3"),
        ("p4", "p4 (orta)"),
        ("p5", "p5"),
        ("p6", "p6"),
        ("p7", "p7 (en kaliteli)"));

    public static Secenek[] NvencAyarlar { get; } = L(
        ("hq", "Yüksek kalite"),
        ("ll", "Düşük gecikme"),
        ("ull", "Çok düşük gecikme"),
        ("lossless", "Kayıpsız"));

    public static Secenek[] NvencProfiller { get; } = L(
        ("kaynak", "Kaynakla aynı"),
        ("", "Otomatik"),
        ("baseline", "Baseline"),
        ("main", "Main"),
        ("high", "High"),
        ("high444p", "High 4:4:4"));

    public static Secenek[] NvencModlari { get; } = L(
        (NvencModu.SabitKalite, "Sabit kalite (CQ)"),
        (NvencModu.SabitQp, "Sabit niceleyici (QP)"),
        (NvencModu.DegiskenBitHizi, "Değişken bit hızı (VBR)"),
        (NvencModu.Cbr, "Sabit bit hızı (CBR)"));

    public static Secenek[] CokGecisler { get; } = L(
        ("disabled", "Kapalı"),
        ("qres", "Çeyrek çözünürlük"),
        ("fullres", "Tam çözünürlük"));

    public static Secenek[] BRefModlari { get; } = L(
        ("disabled", "Kapalı"),
        ("each", "Her B-kare"),
        ("middle", "Ortadaki B-kare"));

    public static Secenek[] Kodlamalar { get; } = L(
        ("cabac", "CABAC"),
        ("cavlc", "CAVLC"));

    // ------------------------------------------------------------------ Ses
    public static Secenek[] SesKaynaklari { get; } = L(
        (SesKaynagi.AyniAdliDosyaOncelikli, "Aynı adlı ses dosyası (yoksa videonun sesi)"),
        (SesKaynagi.VideonunSesi, "Videonun kendi sesi"),
        (SesKaynagi.Sessiz, "Sessiz (ses yok)"));

    public static Secenek[] OrnekHizlari { get; } = L(
        (O, "Kaynakla aynı"),
        (44100, "44,1 kHz"),
        (48000, "48 kHz"),
        (96000, "96 kHz"));

    public static Secenek[] Kaliteler { get; } = L(
        (DonusumKalitesi.Yuksek, "Yüksek (soxr 20 bit)"),
        (DonusumKalitesi.CokYuksek, "Çok yüksek (soxr 28 bit)"),
        (DonusumKalitesi.EnYuksek, "En yüksek (soxr 33 bit)"));

    public static Secenek[] Titresimler { get; } = L(
        (Titresim.Yok, "Yok"),
        (Titresim.Ucgen, "Üçgen (triangular)"),
        (Titresim.UcgenYuksekGecis, "Üçgen, yüksek geçişli"),
        (Titresim.Shibata, "Shibata (gürültü şekillendirme)"));

    public static Secenek[] Kanallar { get; } = L(
        (KanalModu.Kaynak, "Kaynakla aynı"),
        (KanalModu.Stereo, "Stereo"),
        (KanalModu.Mono, "Mono"));

    public static Secenek[] SesKodlayicilar { get; } = L(
        (SesKodlayici.FdkAac, "FDK AAC"),
        (SesKodlayici.Wav, "WAV (PCM, kayıpsız)"));

    public static Secenek[] WavBitleri { get; } = L(
        (WavBitDerinligi.Bit16, "16 bit"),
        (WavBitDerinligi.Bit24, "24 bit"),
        (WavBitDerinligi.Bit32, "32 bit"),
        (WavBitDerinligi.Float32, "32 bit float"));

    public static Secenek[] FdkProfiller { get; } = L(
        ("aac_low", "AAC-LC"),
        ("aac_he", "HE-AAC"),
        ("aac_he_v2", "HE-AAC v2"),
        ("aac_ld", "AAC-LD"),
        ("aac_eld", "AAC-ELD"));

    public static Secenek[] FdkModlari { get; } = L(
        (FdkModu.Cbr, "Sabit bit hızı (CBR)"),
        (FdkModu.Vbr1, "VBR 1 (en düşük)"),
        (FdkModu.Vbr2, "VBR 2"),
        (FdkModu.Vbr3, "VBR 3"),
        (FdkModu.Vbr4, "VBR 4"),
        (FdkModu.Vbr5, "VBR 5 (en yüksek)"));

    public static Secenek[] FdkSinyaller { get; } = L(
        ("default", "Varsayılan"),
        ("implicit", "Örtük (implicit)"),
        ("explicit_sbr", "Açık SBR"),
        ("explicit_hierarchical", "Açık hiyerarşik"));

    /// <summary>FDK AAC dinamik aralık (DRC) profilleri.</summary>
    public static Secenek[] DrcProfilleri { get; } = L(
        (O, "Kullanılmaz"),
        (0, "Yok"),
        (1, "Film · standart"),
        (2, "Film · hafif"),
        (3, "Müzik · standart"),
        (4, "Müzik · hafif"),
        (5, "Konuşma"));

    // ------------------------------------------------------------------ Anahtar kare
    public static Secenek[] AralikBirimleri { get; } = L(
        (AralikBirimi.Kare, "kare"),
        (AralikBirimi.Saniye, "saniye"));
}
