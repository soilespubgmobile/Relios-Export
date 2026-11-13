using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Arayuz;

/// <summary>Ayrıntılı ayar pencerelerinin ortak yardımcıları.</summary>
public static class PencereOrtak
{
    /// <summary>Kaynak bilinmeden komut önizlemesi için kare hızı: sabitse o, kaynakla aynıysa 30 varsayılır.</summary>
    public static double OnizlemeKareHizi(ProfilAyarlari a) =>
        a.Video.KareHiziModu == KareHiziModu.Sabit && Bicim.KesirCoz(a.Video.KareHizi) is { } f ? f : 30;

    public static string KomutNotu(ProfilAyarlari a)
    {
        var not = "Anahtar kare ayarları (keyint, min-keyint, scenecut) ana paneldeki 'Anahtar kare' bölümünden gelir.";
        if (a.AnahtarKare.Birim == AralikBirimi.Saniye && a.Video.KareHiziModu == KareHiziModu.Kaynak)
        {
            not += " Kare hızı kaynakla aynı olduğundan saniye → kare çevrimi burada 30 fps varsayılarak gösterilir; kodlamada kaynağın kare hızı kullanılır.";
        }

        return not;
    }
}
