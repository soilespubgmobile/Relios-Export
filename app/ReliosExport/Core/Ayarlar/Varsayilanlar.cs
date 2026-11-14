using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReliosExport.Core.Ayarlar;

/// <summary>Profillerin fabrika ayarları ("Varsayılana dön").</summary>
public static class Varsayilanlar
{
    /// <summary>
    /// TikTok: ekran kartıyla (NVENC H.264) sabit bit hızı CBR 34375 kb/s (tampon boş: bit hızı kadar, 1 saniye);
    /// çözünürlük, kare hızı, H.264 profili ve seviyesi kaynakla aynı; B-kare yok, 3 referans kare, 30 karede bir
    /// anahtar kare; MOV. Ses: WAV 16 bit 48 kHz (bkz. <see cref="PlatformSesi"/>).
    /// </summary>
    public static ProfilAyarlari TikTok()
    {
        var a = Dikey916();
        var v = a.Video;
        v.Cozunurluk = CozunurlukModu.Kaynak;
        v.KareHiziModu = KareHiziModu.Kaynak;
        v.Genislik = 1920;
        v.Yukseklik = 1080;
        v.Kodlayici = VideoKodlayici.Nvenc;
        v.Kap = KapBicimi.Mov;

        var n = v.Nvenc;
        n.Mod = NvencModu.Cbr;
        n.BitHizi = 34375;
        n.Profil = "kaynak";
        n.Seviye = "kaynak";
        n.BKare = 0;
        n.Ref = 3;
        n.Kodlama = "cabac";

        // x264'e geçilirse de profil / seviye kaynakla aynı kalsın
        v.X264.Profil = "kaynak";
        v.X264.Seviye = "kaynak";

        var k = a.AnahtarKare;
        k.Birim = AralikBirimi.Kare;
        k.EnBuyuk = 30;
        k.EnKucuk = null;
        k.SahneDegisimi = 0;
        PlatformSesi(a.Ses);
        return a;
    }

    /// <summary>
    /// TikTok ve YouTube sesi: WAV (PCM) 16 bit, 48 kHz, stereo (dosya MOV). Platform sesi zaten kendisi sıkıştırır;
    /// 16 bit PCM, FDK AAC'nin de girişte kullandığı 16 bitlik sesi hiç sıkıştırmadan verir (YouTube'a yükleyip
    /// indirerek denendi: 128 kb/s AAC yüklemeye göre 15-17 kHz'te 1,7-3,6 dB daha çok ayrıntı kalıyor, altı aynı).
    /// Dönüşüm en yüksek kalitede (soxr 33 bit). 24 / 32 bit ya da kayan noktalı kaynak 16 bite üçgen titreşimle
    /// iner: çok kısık yerlerde ve sönen seslerde yuvarlama bozulması olmaz. Kaynak zaten 16 bit 48 kHz ise ses
    /// bit bit aynı kalır.
    /// </summary>
    public static void PlatformSesi(SesAyarlari s)
    {
        s.Kodlayici = SesKodlayici.Wav;
        s.WavBit = WavBitDerinligi.Bit16;
        s.OrnekHizi = 48000;
        s.Kanal = KanalModu.Stereo;
        s.Kalite = DonusumKalitesi.EnYuksek;
        s.Titresim = Titresim.Ucgen;
    }

    /// <summary>
    /// Dikey 9:16 (0.7'ye kadar TikTok varsayılanıydı, 79 kontrolde hedefle fark yok; ön ayar listesinde yok,
    /// YouTube varsayılanının temeli ve GitHub doğrulamasının hedef ayarı):
    /// 1216x2160 9:16, 29.97 fps, x264 Main@L5.0 CRF 18, FDK AAC LC 48 kHz 128 kb/s.
    /// </summary>
    public static ProfilAyarlari Dikey916()
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
        v.Kap = KapBicimi.Mp4; // hedef videoyla doğrulanan dosya MP4

        var x = v.X264;
        x.OnAyar = "medium";
        x.Ayar = string.Empty;
        x.Profil = "main";
        x.Seviye = "5.0";
        x.Mod = X264Modu.Crf;
        x.Crf = 18;
        x.IsParcacigi = 8; // Ryzen 7 5700X: 8 çekirdek
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
    /// YouTube: dikey 9:16 ayarının kodlama ayarları (x264 CRF 18, anahtar kare). Farkları: çözünürlük,
    /// en-boy oranı, kare hızı, H.264 profili ve seviyesi kaynakla aynı (kırpma, ölçekleme, fps çevirme yok);
    /// VBV en yüksek bit hızı 34375 kb/s, tampon 55000 kb; anahtar kare aralığı 30 fps'e göre, kare hızıyla
    /// orantılı; MOV; ses WAV 16 bit 48 kHz (bkz. <see cref="PlatformSesi"/>).
    /// </summary>
    public static ProfilAyarlari YouTube()
    {
        var a = Dikey916();
        a.Video.Cozunurluk = CozunurlukModu.Kaynak;
        a.Video.KareHiziModu = KareHiziModu.Kaynak;
        a.Video.X264.Profil = "kaynak";
        a.Video.X264.Seviye = "kaynak";
        a.Video.Nvenc.Profil = "kaynak";
        a.Video.Nvenc.Seviye = "kaynak";

        // Kullanıcı sonradan "hedef boyut"a geçerse başlangıç değerleri
        a.Video.Genislik = 1920;
        a.Video.Yukseklik = 1080;

        // Kalite yine CRF 18'le belirlenir; bit hızı en çok 34375 kb/s'e çıkar (9:16 ayarında 14000 / 28000).
        // Kaynağın seviyesi buna yetmezse (ör. 1080p30 High@4.0'ın sınırı 25000 / 31250) yeten en düşük seviye
        // yazılır (4.1); bkz. ArgsBuilder.X264Seviyesi.
        a.Video.X264.VbvMaks = 34375;
        a.Video.X264.VbvTampon = 55000;

        // Anahtar kare aralığı aynı süre kalsın: 30 fps'te 50 / 26, 60 fps'te 100 / 51 (kare hızı kaynakla aynı).
        a.AnahtarKare.Birim = AralikBirimi.Kare30;
        a.Video.Kap = KapBicimi.Mov;
        PlatformSesi(a.Ses);
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
