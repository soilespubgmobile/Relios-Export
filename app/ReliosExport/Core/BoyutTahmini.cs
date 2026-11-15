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
        if (m.DurationSeconds <= 0 || VideoBitHizi(a) is not { } video)
        {
            return null;
        }

        var sesKbps = SesBitHizi(a.Ses, ses);
        var bayt = (video.Kbps + sesKbps) * 1000.0 / 8 * m.DurationSeconds;
        return new BoyutTahmin((long)Math.Round(bayt), video.Tur);
    }

    /// <summary>Videonun bit hızı (kb/s) ve tahminin türü; ayarlardan belli değilse null.</summary>
    public static (double Kbps, TahminTuru Tur)? VideoBitHizi(ProfilAyarlari a)
    {
        var v = a.Video;
        if (v.Kodlayici == VideoKodlayici.Nvenc)
        {
            var n = v.Nvenc;
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
                return (x.VbvMaks.Value, TahminTuru.EnCok);
            default:
                return null;
        }
    }

    /// <summary>Sesin bit hızı (kb/s). WAV kesin; FDK AAC CBR kesin, VBR kipin üst değeri (kanal başına).</summary>
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
            var hz = s.OrnekHizi ?? ses.OrnekHizi ?? 48000;
            var bit = s.WavBit == WavBitDerinligi.Bit16 ? 16 : s.WavBit == WavBitDerinligi.Bit24 ? 24 : 32;
            return hz * bit * kanal / 1000.0;
        }

        var f = s.Fdk;
        return f.Mod switch
        {
            FdkModu.Cbr => f.BitHizi,
            FdkModu.Vbr1 => 32 * kanal,
            FdkModu.Vbr2 => 40 * kanal,
            FdkModu.Vbr3 => 56 * kanal,
            FdkModu.Vbr4 => 72 * kanal,
            _ => 112 * kanal,
        };
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
