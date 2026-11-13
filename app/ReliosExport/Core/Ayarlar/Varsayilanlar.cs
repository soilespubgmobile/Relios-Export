using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReliosExport.Core.Ayarlar;

/// <summary>Profillerin fabrika ayarları ("Varsayılana dön").</summary>
public static class Varsayilanlar
{
    /// <summary>
    /// TikTok: XviD4PSP 8.2.14 "Relios 9x16" ile doğrulanan hedef (79 kontrolde fark yok).
    /// 1216x2160 9:16, 29.97 fps, x264 Main@L5.0 CRF 18, FDK AAC LC 48 kHz 128 kb/s.
    /// </summary>
    public static ProfilAyarlari TikTok()
    {
        var a = new ProfilAyarlari();

        var v = a.Video;
        v.Cozunurluk = CozunurlukModu.Hedef;
        v.Genislik = 1216;
        v.Yukseklik = 2160;
        v.Oran = OranUyumu.OrtadanKirp;
        v.Olcekleme = "lanczos";
        v.HassasYuvarlama = false;
        v.KareHiziModu = KareHiziModu.Sabit;
        v.KareHizi = "30000/1001";
        v.Kodlayici = VideoKodlayici.X264;

        var x = v.X264;
        x.OnAyar = "medium";
        x.Ayar = string.Empty;
        x.Profil = "main";
        x.Seviye = "5.0";
        x.Mod = X264Modu.Crf;
        x.Crf = 18;
        x.IsParcacigi = 7;
        x.Ref = 3;
        x.Deblock = true;
        x.DeblockAlfa = -1;
        x.DeblockBeta = -1;
        x.Me = "hex";
        x.AltPiksel = 7;
        x.PsyRd = 1.00;
        x.PsyTrellis = 0.15;
        x.KarisikRef = true;
        x.MeAraligi = 16;
        x.KromaMe = true;
        x.Trellis = 1;
        x.Dct8x8 = false;
        x.Cqm = "flat";
        x.OluBolgeInter = 21;
        x.OluBolgeIntra = 11;
        x.HizliPAtlama = true;
        x.GurultuAzaltma = 0;
        x.DctSeyreltme = true;
        x.Gecmeli = false;
        x.BlurayUyumlu = false;
        x.KisitliIntra = false;
        x.BKare = 0;
        x.AgirlikliP = 2;
        x.IleriBakis = 40;
        x.MbAgaci = true;
        x.Qcomp = 0.60;
        x.QpMin = 0;
        x.QpMax = 33;
        x.QpAdim = 4;
        x.VbvMaks = 14000;
        x.VbvTampon = 28000;
        x.NalHrd = "none";
        x.Dolgu = false;
        x.IpOrani = 1.40;
        x.AqModu = 1;
        x.AqGucu = 1.00;

        var k = a.AnahtarKare;
        k.Birim = AralikBirimi.Kare;
        k.EnBuyuk = 50;
        k.EnKucuk = 26;
        k.SahneDegisimi = 0;
        k.AcikGop = false;
        k.IntraYenileme = false;

        var s = a.Ses;
        s.Kaynak = SesKaynagi.AyniAdliDosyaOncelikli;
        s.Gecikme = 0;
        s.Seviye = 0;
        s.OrnekHizi = 48000;
        s.Kalite = DonusumKalitesi.Yuksek;
        s.Titresim = Titresim.Yok;
        s.Kanal = KanalModu.Stereo;
        s.Kodlayici = SesKodlayici.FdkAac;
        s.Fdk.Profil = "aac_low";
        s.Fdk.Mod = FdkModu.Cbr;
        s.Fdk.BitHizi = 128;
        s.Fdk.Kesim = 0;
        s.Fdk.Afterburner = true;
        s.Fdk.Sinyal = "default";
        return a;
    }

    /// <summary>
    /// YouTube: kaynakla aynı çözünürlük ve kare hızı, yüksek kalite. YouTube'un yükleme önerileri:
    /// High profil, 2 ardışık B-kare, kapalı GOP (kare hızının yarısı), AAC-LC 48 kHz 384 kb/s.
    /// </summary>
    /// <summary>
    /// YouTube: TikTok'la (XviD4PSP "Relios 9x16") aynı kodlama ayarları — x264, anahtar kare, FDK AAC —
    /// tek farkla: çözünürlük, en-boy oranı ve kare hızı kaynakla aynı (kırpma, ölçekleme, fps çevirme yok).
    /// </summary>
    public static ProfilAyarlari YouTube()
    {
        var a = TikTok();
        a.Video.Cozunurluk = CozunurlukModu.Kaynak;
        a.Video.KareHiziModu = KareHiziModu.Kaynak;

        // Kullanıcı sonradan "hedef boyut"a geçerse başlangıç değerleri
        a.Video.Genislik = 1920;
        a.Video.Yukseklik = 1080;
        return a;
    }

    public static ProfilAyarlari For(ExportProfileId id) => id == ExportProfileId.TikTok ? TikTok() : YouTube();
}

/// <summary>Ayarların JSON biçimi (ayarlar.json ve kayıtlı ön ayarlar).</summary>
public static class AyarDosyasi
{
    public static readonly JsonSerializerOptions Secenekler = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Yaz(ProfilAyarlari a) => JsonSerializer.Serialize(a, Secenekler);

    public static ProfilAyarlari? Oku(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ProfilAyarlari>(json, Secenekler);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ProfilAyarlari Kopyala(ProfilAyarlari a) => Oku(Yaz(a)) ?? new ProfilAyarlari();

    /// <summary>Herhangi bir ayar nesnesinin bağımsız kopyası (ayrıntılı ayar pencereleri kopya üzerinde çalışır).</summary>
    public static T Kopyala<T>(T a)
        where T : Gozlenebilir, new() =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(a, Secenekler), Secenekler) ?? new T();
}
