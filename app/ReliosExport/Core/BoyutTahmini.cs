using System.Globalization;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Core;

/// <summary>Tahminin türü: bit hızı belli (CBR / ortalama) ya da yalnızca üst sınır belli (CRF + VBV gibi).</summary>
public enum TahminTuru { Yaklasik, EnCok }

/// <summary>Çıktı dosyasının tahmini boyutu (bayt) ve türü.</summary>
public sealed record BoyutTahmin(long Bayt, TahminTuru Tur)
{
    /// <summary>"tahmini 245 MB" ya da "en çok 1,2 GB".</summary>
    public string Metin => (Tur == TahminTuru.EnCok ? "en çok " : "tahmini ") + BoyutTahmini.Yaz(Bayt);
}

/// <summary>
/// Çıktı boyutunun tahmini: (video bit hızı + ses bit hızı) × süre. Video bit hızı ayarlardan belli değilse
/// (CRF / sabit kalite / sabit QP ve üst sınır yok) tahmin yapılmaz. Kap ek yükü (MOV / MP4 başlığı) çok küçüktür.
/// </summary>
public static class BoyutTahmini
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static BoyutTahmin? Hesapla(ProfilAyarlari a, MediaInfo m, SesSecimi ses)
    {
        if (m.DurationSeconds <= 0 || VideoBitHizi(a, m) is not { } video)
        {
            return null;
        }

        var sesKbps = SesBitHizi(a.Ses, ses);
        var kbit = (video.Kbps + sesKbps) * m.DurationSeconds;
        if (video.Tur == TahminTuru.EnCok && TamponPayi(a, m) is { } pay)
        {
            // Üst sınırda kodlayıcının tamponu da sayılır: video başında tampondaki bitleri harcayabilir, kısa
            // videoda ortalama en yüksek bit hızının üstüne çıkabilir (ör. 5 sn'de 55000 kb'lık tampon ≈ +%29).
            kbit += pay;
        }

        return new BoyutTahmin((long)Math.Round(kbit * 1000.0 / 8), video.Tur);
    }

    /// <summary>
    /// x264'te "en çok" tahmini için en yüksek QP sınırının (qpmax) en az değeri: daha düşük sınırda x264 bit hızını
    /// VBV'nin altına indiremez (QP yükseltilemiyor; qpmax 0'da her kare QP 0), "en çok" söylenemez.
    /// </summary>
    public const int EnCokIcinQpMax = 20;

    /// <summary>
    /// Videonun bit hızı (kb/s) ve tahminin türü; ayarlardan belli değilse null. <paramref name="m"/>: "kaynakla aynı"
    /// profil için (kayıpsız olup olmadığı profile bağlı).
    /// </summary>
    public static (double Kbps, TahminTuru Tur)? VideoBitHizi(ProfilAyarlari a, MediaInfo? m = null)
    {
        var v = a.Video;
        if (v.Kodlayici == VideoKodlayici.Nvenc)
        {
            var n = v.Nvenc;
            if (n.Kayipsiz)
            {
                // Kayıpsızda bit hızı görüntünün kendisine bağlıdır (sınırı yoktur): tahmin yapılmaz.
                return null;
            }

            return n.Mod switch
            {
                NvencModu.Cbr => (n.BitHizi, TahminTuru.Yaklasik),
                NvencModu.DegiskenBitHizi => (n.BitHizi, TahminTuru.Yaklasik),
                NvencModu.SabitKalite when n.MaksBitHizi is > 0 => (n.MaksBitHizi.Value, TahminTuru.EnCok),
                _ => null,
            };
        }

        var x = v.X264;
        switch (x.Mod)
        {
            case X264Modu.Cbr:
            case X264Modu.OrtalamaBitHizi:
            case X264Modu.IkiGecis:
            case X264Modu.UcGecis:
                return (x.BitHizi, TahminTuru.Yaklasik);
            case X264Modu.Crf when x.VbvMaks is > 0 && x.VbvTampon is > 0:
                // x264 CRF'de VBV'yi yalnızca ikisi birlikte verilince kullanır (ArgsBuilder.X264Vbv ile aynı kural).
                // Kayıpsızda (8 bit CRF 0) x264 VBV'yi yok sayar; qpmax çok düşükse VBV'ye uyamaz: üst sınır yok.
                if (ArgsBuilder.X264KayipsizMi(a, m) || x.QpMax is < EnCokIcinQpMax)
                {
                    return null;
                }

                return (x.VbvMaks.Value, TahminTuru.EnCok);
            default:
                return null;
        }
    }

    /// <summary>
    /// "En çok" tahmininde tamponun payı (kbit): x264'te tampon × başlangıç doluluğu (vbv-init, boşsa x264'ün
    /// varsayılanı 0,9); NVENC'te ("en çok" yalnızca sabit kalitede) seviyenin en büyük tamponu, çünkü ffmpeg
    /// sabit kalitede verilen tamponu yok sayar ve kart seviyenin tamponunu kullanır. Bilinmiyorsa null.
    /// </summary>
    private static double? TamponPayi(ProfilAyarlari a, MediaInfo m)
    {
        var v = a.Video;
        if (v.Kodlayici == VideoKodlayici.Nvenc)
        {
            return v.Nvenc.Mod == NvencModu.SabitKalite ? ArgsBuilder.NvencSabitKaliteTamponu(a, m) : null;
        }

        var x = v.X264;
        if (x.VbvTampon is not > 0)
        {
            return null;
        }

        // x264 tamponu bir kareden küçük olamaz: en yüksek bit hızı / kare hızı ("VBV buffer size cannot be smaller
        // than one frame"); daha küçük verilince kendisi büyütür.
        var tampon = Math.Max(x.VbvTampon.Value, x.VbvMaks is > 0 ? x.VbvMaks.Value / ArgsBuilder.CiktiKareHizi(a, m) : 0);
        return tampon * Math.Clamp(x.VbvBaslangic ?? 0.9, 0, 1);
    }

    /// <summary>
    /// Sesin bit hızı (kb/s). WAV kesin; FDK AAC CBR kesin (FDK'nin kodlayacağı değer), VBR'de FDK'nin kipin hedeflediği
    /// bit hızı (<see cref="FdkUyumu.VbrBitHizi"/>).
    /// </summary>
    public static double SesBitHizi(SesAyarlari s, SesSecimi ses)
    {
        if (ses.Tur == SesSecimTuru.Yok)
        {
            return 0;
        }

        var kanal = s.Kanal switch
        {
            KanalModu.Mono => 1,
            KanalModu.Stereo => 2,
            _ => ses.Kanal is > 0 ? ses.Kanal.Value : 2,
        };

        if (s.Kodlayici == SesKodlayici.Wav)
        {
            var hz = ArgsBuilder.CiktiHizi(s, ses) ?? 48000;
            var bit = s.WavBit == WavBitDerinligi.Bit16 ? 16 : s.WavBit == WavBitDerinligi.Bit24 ? 24 : 32;
            return hz * bit * kanal / 1000.0;
        }

        // CBR: FDK'nin bu hız ve kanal düzeninde kodlayacağı bit hızı (sınırın dışındakini FDK sınıra çeker); VBR:
        // FDK'nin kipe göre hedefi (AAC-ELD'de kip yükselebilir).
        var f = s.Fdk;
        var karar = ArgsBuilder.FdkKarari(s, ses);
        var duzen = karar?.Duzen ?? (kanal == 1 ? "mono" : kanal == 2 ? "stereo" : FdkUyumu.Duzen(null, kanal));
        var mod = karar?.Mod ?? f.Mod;
        return mod == FdkModu.Cbr
            ? karar?.BitHizi ?? f.BitHizi
            : FdkUyumu.VbrBitHizi(mod, duzen) / 1000.0;
    }

    /// <summary>Windows Gezgini gibi (1 MB = 1024 × 1024 bayt): "245 MB", "1,2 GB", "850 KB".</summary>
    public static string Yaz(long bayt)
    {
        const double Kb = 1024, Mb = Kb * 1024, Gb = Mb * 1024;
        return bayt >= Gb ? (bayt / Gb).ToString("0.0", Tr) + " GB"
            : bayt >= Mb ? (bayt / Mb).ToString(bayt >= 100 * Mb ? "0" : "0.0", Tr) + " MB"
            : Math.Max(1, Math.Round(bayt / Kb)).ToString("0", Tr) + " KB";
    }
}
