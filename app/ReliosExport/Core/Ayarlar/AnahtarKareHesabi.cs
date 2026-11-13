namespace ReliosExport.Core.Ayarlar;

/// <summary>Anahtar kare aralığının çıktının kare hızına göre kare sayısı (keyint / min-keyint, NVENC -g).</summary>
public static class AnahtarKareHesabi
{
    /// <summary>"30 fps'e göre" birimindeki değerlerin yazıldığı kare hızı (29,97).</summary>
    public const double TemelKareHizi = 30000.0 / 1001;

    /// <summary>Bir değerin bu kare hızında kaç kare olduğu; değer yoksa null.</summary>
    public static int? Kare(AnahtarKareAyarlari k, double? deger, double fps)
    {
        if (deger is not { } d)
        {
            return null;
        }

        var kare = k.Birim switch
        {
            AralikBirimi.Saniye => d * fps,
            AralikBirimi.Kare30 => d * fps / TemelKareHizi,
            _ => d,
        };
        return Math.Max(1, (int)Math.Round(kare, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// En fazla / en az aralık (kare). En az, en fazlanın yarısı + 1'i geçemez (x264 de böyle kırpar):
    /// 30 fps'te 50 / 26, 60 fps'te 100 / 51.
    /// </summary>
    public static (int? EnBuyuk, int? EnKucuk) Aralik(AnahtarKareAyarlari k, double fps)
    {
        var enBuyuk = Kare(k, k.EnBuyuk, fps);
        var enKucuk = Kare(k, k.EnKucuk, fps);
        if (enBuyuk is { } g && enKucuk is { } m && m > (g / 2) + 1)
        {
            enKucuk = (g / 2) + 1;
        }

        return (enBuyuk, enKucuk);
    }
}
