namespace ReliosExport.Core;

/// <summary>
/// H.264 seviye sınırları (ITU-T H.264, Tablo A-1) ve bir çıktının en az hangi seviyeyi gerektirdiği.
/// <para>NVENC seviyeyi katı denetler: istenen seviye çıktının boyutuna, kare hızına, bit hızına, tamponuna ya da
/// referans kare sayısına yetmezse "InitializeEncoder failed: invalid param (8): Invalid Level." verip hiç
/// başlamaz. x264 ise yalnızca uyarır.</para>
/// </summary>
public static class H264Seviye
{
    /// <param name="Ad">ffmpeg'in -level değeri.</param>
    /// <param name="MaksMbSn">Saniyedeki en çok makroblok.</param>
    /// <param name="MaksKare">Bir karedeki en çok makroblok.</param>
    /// <param name="MaksDpb">Referans kareler için en çok makroblok (DPB).</param>
    /// <param name="MaksBitHizi">kb/s, Baseline / Main (High'da 1,25 katı).</param>
    /// <param name="MaksTampon">kbit, Baseline / Main (High'da 1,25 katı).</param>
    private sealed record Sinir(string Ad, long MaksMbSn, long MaksKare, long MaksDpb, long MaksBitHizi, long MaksTampon);

    private static readonly Sinir[] Tablo =
    [
        new("1", 1485, 99, 396, 64, 175),
        new("1b", 1485, 99, 396, 128, 350),
        new("1.1", 3000, 396, 900, 192, 500),
        new("1.2", 6000, 396, 2376, 384, 1000),
        new("1.3", 11880, 396, 2376, 768, 2000),
        new("2.0", 11880, 396, 2376, 2000, 2000),
        new("2.1", 19800, 792, 4752, 4000, 4000),
        new("2.2", 20250, 1620, 8100, 4000, 4000),
        new("3.0", 40500, 1620, 8100, 10000, 10000),
        new("3.1", 108000, 3600, 18000, 14000, 14000),
        new("3.2", 216000, 5120, 20480, 20000, 20000),
        new("4.0", 245760, 8192, 32768, 20000, 25000),
        new("4.1", 245760, 8192, 32768, 50000, 62500),
        new("4.2", 522240, 8704, 34816, 50000, 62500),
        new("5.0", 589824, 22080, 110400, 135000, 135000),
        new("5.1", 983040, 36864, 184320, 240000, 240000),
        new("5.2", 2073600, 36864, 184320, 240000, 240000),
        new("6.0", 4177920, 139264, 696320, 240000, 240000),
        new("6.1", 8355840, 139264, 696320, 480000, 480000),
        new("6.2", 16711680, 139264, 696320, 800000, 800000),
    ];

    /// <summary>Seviyenin tablodaki sırası ("4.2", "42", "4" gibi yazımlar kabul edilir); bilinmiyorsa -1.</summary>
    public static int Sira(string? seviye)
    {
        if (string.IsNullOrWhiteSpace(seviye))
        {
            return -1;
        }

        var s = seviye.Trim();
        if (s is "1b" or "1.0b" or "9")
        {
            return 1;
        }

        if (!s.Contains('.') && int.TryParse(s, out var n))
        {
            // 42 → 4.2, 4 → 4.0
            s = n >= 10 ? $"{n / 10}.{n % 10}" : $"{n}.0";
        }

        if (s == "1.0")
        {
            s = "1";
        }

        for (var i = 0; i < Tablo.Length; i++)
        {
            if (Tablo[i].Ad == s)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Çıktının gerektirdiği en düşük seviye; 6.2 de yetmiyorsa null.
    /// </summary>
    /// <param name="genislik">Çıktı genişliği (piksel).</param>
    /// <param name="yukseklik">Çıktı yüksekliği (piksel).</param>
    /// <param name="kareHizi">Çıktının kare hızı.</param>
    /// <param name="bitHizi">En yüksek bit hızı (kb/s); sınırsızsa null.</param>
    /// <param name="tampon">VBV tamponu (kbit); yoksa null.</param>
    /// <param name="referansKare">Çözücünün saklaması gereken referans kare sayısı.</param>
    /// <param name="high">High (ve üstü) profil: bit hızı ve tampon sınırları 1,25 kat.</param>
    public static string? EnAz(int genislik, int yukseklik, double kareHizi, int? bitHizi, int? tampon, int referansKare, bool high)
    {
        if (genislik <= 0 || yukseklik <= 0)
        {
            return null;
        }

        long mbG = (genislik + 15) / 16;
        long mbY = (yukseklik + 15) / 16;
        var kare = mbG * mbY;
        var mbSn = kare * (kareHizi > 0 ? kareHizi : 30);
        var refs = Math.Max(1, referansKare);
        var kat = high ? 1.25 : 1.0;

        foreach (var s in Tablo)
        {
            // Kenar sınırı: genişlik ve yükseklik (makroblok) en fazla √(8 × MaksKare).
            var kenar = Math.Sqrt(8.0 * s.MaksKare);
            if (kare > s.MaksKare || mbG > kenar || mbY > kenar || mbSn > s.MaksMbSn)
            {
                continue;
            }

            // DPB: referans kareler sığmalı (en çok 16 kare).
            if (Math.Min(s.MaksDpb / kare, 16) < refs)
            {
                continue;
            }

            if (bitHizi is { } b && b > s.MaksBitHizi * kat)
            {
                continue;
            }

            if (tampon is { } t && t > s.MaksTampon * kat)
            {
                continue;
            }

            return s.Ad;
        }

        return null;
    }

    /// <summary>
    /// Seviyenin bu kare boyutunda taşıyabileceği en çok referans kare (DPB, en fazla 16);
    /// seviye ya da boyut bilinmiyorsa null.
    /// </summary>
    public static int? EnFazlaDpb(string? seviye, int genislik, int yukseklik)
    {
        var i = Sira(seviye);
        if (i < 0 || genislik <= 0 || yukseklik <= 0)
        {
            return null;
        }

        long kare = ((genislik + 15) / 16) * (long)((yukseklik + 15) / 16);
        return (int)Math.Max(1, Math.Min(Tablo[i].MaksDpb / kare, 16));
    }

    /// <summary>İki seviyeden yüksek olanı (biri bilinmiyorsa diğeri).</summary>
    public static string EnYuksek(string a, string? b)
    {
        if (string.IsNullOrEmpty(b))
        {
            return a;
        }

        return Sira(b) > Sira(a) ? b : a;
    }
}
