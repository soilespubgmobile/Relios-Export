using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Arayuz;

/// <summary>Ayrıntılı ayar pencerelerinin ortak yardımcıları.</summary>
public static class PencereOrtak
{
    /// <summary>Kaynak bilinmeden komut önizlemesi için kare hızı: sabitse o, kaynakla aynıysa 30 varsayılır.</summary>
    public static double OnizlemeKareHizi(ProfilAyarlari a) =>
        a.Video.KareHiziModu == KareHiziModu.Sabit && Bicim.KesirCoz(a.Video.KareHizi) is { } f ? f : 30;

    /// <summary>
    /// Komut sekmesinin notu. Kare hızı kaynakla aynıysa örnek, profilin kendi anahtar kare ayarından hesaplanır
    /// (ör. TikTok varsayılanında 60 fps'te 600, YouTube'unkinde 100 / 51). <paramref name="nvenc"/>: NVENC yalnızca
    /// "en fazla"yı kullanır.
    /// </summary>
    public static string KomutNotu(ProfilAyarlari a, bool nvenc = false)
    {
        var not = "Anahtar kare ayarları (keyint, min-keyint, scenecut) ana paneldeki 'Anahtar kare' bölümünden gelir.";
        if (a.AnahtarKare.Birim != AralikBirimi.Kare && a.Video.KareHiziModu == KareHiziModu.Kaynak)
        {
            not += " Kare hızı kaynakla aynı olduğundan anahtar kare aralığı burada 30 fps varsayılarak gösterilir; kodlamada kaynağın kare hızı kullanılır"
                   + (AnahtarKareHesabi.OrnekMetni(a.AnahtarKare, 60, nvenc) is { } ornek ? $" (ör. 60 fps'te {ornek})." : ".");
        }

        return not;
    }
}
