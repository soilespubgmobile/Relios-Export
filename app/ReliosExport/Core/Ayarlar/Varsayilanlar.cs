using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReliosExport.Core.Ayarlar;

/// <summary>Profillerin fabrika ayarları ("Varsayılana dön").</summary>
public static class Varsayilanlar
{
    /// <summary>
    /// TikTok (0.11.0, kullanıcının isteği; bilgisayarında denendi): x264 veryslow, CRF 24, High profil, seviye kaynakla
    /// aynı (16 referans kare yetmezse yükselir: 1080p'de 5.1). Diğer x264 ayarları hız ayarının (8 B-kare, piramit,
    /// uyarlamalı B-kare 2, umh, subme 10, trellis 2, 8x8 DCT); 16 referans kare, önden bakış 250; bit hızı üst sınırı
    /// yok. Anahtar kare en çok 10 saniyede bir (en az: x264'ün kendisi), sahne değişiminde x264'ün eşiği (40).
    /// Çözünürlük ve kare hızı kaynakla aynı (60 fps korunur); MOV. Ses FDK AAC-LC 128 kb/s 44,1 kHz (bkz.
    /// <see cref="TikTokSesi"/>), LUFS eşitleme açık. TikTok yüklenen videoyu kendisi yeniden kodlar (1080p'yi H.265
    /// ile; dikey 60 fps'te ~2,3 Mb/s): CRF 24 bunun 4–5 katıdır, TikTok'un sonucunda kaynağı yüklemekten farkı
    /// ölçülemedi. NVENC'e geçilirse 0.10.0'ın NVENC ayarları (CBR 34375 kb/s) kullanılır.
    /// </summary>
    public static ProfilAyarlari TikTok()
    {
        var a = TikTokNvenc();
        var v = a.Video;
        v.Kodlayici = VideoKodlayici.X264;
        v.X264 = new X264Ayarlari
        {
            OnAyar = "veryslow",
            Ayar = string.Empty,
            Profil = "high",
            Seviye = "kaynak",
            Mod = X264Modu.Crf,
            Crf = 24,
            IsParcacigi = 8, // Ryzen 7 5700X: 8 çekirdek
            Ref = 16,
            IleriBakis = 250,
        };

        var k = a.AnahtarKare;
        k.Birim = AralikBirimi.Saniye;
        k.EnBuyuk = 10;
        k.EnKucuk = null;
        k.SahneDegisimi = null;

        TikTokSesi(a.Ses);
        a.Ses.Esitleme = true;
        return a;
    }

    /// <summary>
    /// 0.10.0'a kadar TikTok varsayılanı (ayar geçişi ve NVENC denemeleri için): ekran kartıyla (NVENC H.264) sabit bit
    /// hızı CBR 34375 kb/s (tampon boş: bit hızı kadar, 1 saniye); çözünürlük, kare hızı, H.264 profili ve seviyesi
    /// kaynakla aynı; B-kare yok, 3 referans kare, 30 karede bir anahtar kare; MOV. Ses: WAV 16 bit 48 kHz (bkz.
    /// <see cref="PlatformSesi"/>).
    /// </summary>
    public static ProfilAyarlari TikTokNvenc()
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
    /// 0.10.0'a kadar TikTok sesi (0.8.9'da YouTube'un da): WAV (PCM) 16 bit, 48 kHz, stereo (dosya MOV). Platform sesi zaten kendisi
    /// sıkıştırır; 16 bit PCM, FDK AAC'nin de girişte kullandığı 16 bitlik sesi hiç sıkıştırmadan verir. Dönüşüm en
    /// yüksek kalitede (soxr 33 bit). 24 / 32 bit ya da kayan noktalı kaynak 16 bite üçgen titreşimle iner: çok kısık
    /// yerlerde ve sönen seslerde yuvarlama bozulması olmaz. Kaynak zaten 16 bit 48 kHz ise ses bit bit aynı kalır.
    /// </summary>
    public static void PlatformSesi(SesAyarlari s)
    {
        s.Kodlayici = SesKodlayici.Wav;
        s.WavBit = WavBitDerinligi.Bit16;
        s.OrnekHizi = 48000;
        s.Kanal = KanalModu.Stereo;
        s.Kalite = DonusumKalitesi.EnYuksek;
        s.Titresim = Titresim.Ucgen;

        // LUFS eşitleme açık (-14, yalnızca kısık ses yükseltilir): TikTok ve YouTube'da varsayılan.
        s.Esitleme = true;
    }

    /// <summary>
    /// YouTube sesinin biçimi (kullanıcının isteği, sürüm 11): FDK AAC-LC 128 kb/s CBR, 48 kHz, stereo; afterburner
    /// açık (en iyi kalite). Dönüşüm en yüksek kalitede (soxr 33 bit); FDK AAC 16 bit ses alır, 24 / 32 bit ya da
    /// kayan noktalı kaynak 16 bite üçgen titreşimle iner. WAV seçilirse 16 bit. LUFS eşitlemeye dokunmaz (ayar
    /// geçişinde kullanıcının seçimi kalsın; varsayılan <see cref="YouTube"/>'da açık).
    /// </summary>
    public static void YouTubeSesi(SesAyarlari s) => FdkAac128(s, 48000);

    /// <summary>
    /// TikTok sesinin biçimi (kullanıcının isteği, sürüm 12): FDK AAC-LC 128 kb/s CBR, 44,1 kHz, stereo; afterburner
    /// açık. Dönüşüm en yüksek kalitede (soxr 33 bit), 16 bite üçgen titreşimle; WAV seçilirse 16 bit. LUFS eşitlemeye
    /// dokunmaz (ayar geçişinde kullanıcının seçimi kalsın; varsayılan <see cref="TikTok"/>'ta açık).
    /// </summary>
    public static void TikTokSesi(SesAyarlari s) => FdkAac128(s, 44100);

    private static void FdkAac128(SesAyarlari s, int ornekHizi)
    {
        s.Kodlayici = SesKodlayici.FdkAac;
        s.WavBit = WavBitDerinligi.Bit16;
        s.OrnekHizi = ornekHizi;
        s.Kanal = KanalModu.Stereo;
        s.Kalite = DonusumKalitesi.EnYuksek;
        s.Titresim = Titresim.Ucgen;

        var f = s.Fdk;
        f.Profil = "aac_low";
        f.Mod = FdkModu.Cbr;
        f.BitHizi = 128;
        f.Kesim = 0;
        f.Afterburner = true;
        f.Sinyal = "default";
        f.DrcProfili = null;
        f.SikistirmaProfili = null;
        f.DrcHedef = null;
        f.SikistirmaHedef = null;
        f.ProgramSeviyesi = null;
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
    /// orantılı; MOV; ses FDK AAC 128 kb/s 48 kHz (bkz. <see cref="YouTubeSesi"/>), LUFS eşitleme açık.
    /// </summary>
    public static ProfilAyarlari YouTube()
    {
        var a = Dikey916();
        a.Video.Cozunurluk = CozunurlukModu.Kaynak;
        a.Video.KareHiziModu = KareHiziModu.Kaynak;

        // Profil Main (8x8 DCT kapalı; 16 Kasım, kullanıcı isteği): x264 ayarları kullanıcının örnek videosuyla birebir,
        // yalnızca anahtar kare aralığı, VBV ve iş parçacığı programın. Seviye kaynakla aynı (yetmezse yükselir).
        a.Video.X264.Profil = "main";
        a.Video.X264.Seviye = "kaynak";
        a.Video.Nvenc.Profil = "kaynak";
        a.Video.Nvenc.Seviye = "kaynak";

        // Kullanıcı sonradan "hedef boyut"a geçerse başlangıç değerleri
        a.Video.Genislik = 1920;
        a.Video.Yukseklik = 1080;

        // Kalite yine CRF 18'le belirlenir; bit hızı en çok 34375 kb/s'e çıkar (9:16 ayarında 14000 / 28000).
        // Kaynağın seviyesi buna yetmezse (ör. 1080p30 Main@4.0'ın sınırı 20000 / 25000) yeten en düşük seviye
        // yazılır (4.1); bkz. ArgsBuilder.X264Seviyesi.
        a.Video.X264.VbvMaks = 34375;
        a.Video.X264.VbvTampon = 55000;

        // Anahtar kare aralığı aynı süre kalsın: 30 fps'te 50 / 26, 60 fps'te 100 / 51 (kare hızı kaynakla aynı).
        a.AnahtarKare.Birim = AralikBirimi.Kare30;
        a.Video.Kap = KapBicimi.Mov;
        YouTubeSesi(a.Ses);

        // LUFS eşitleme açık (-14, yalnızca kısık ses yükseltilir): TikTok ve YouTube'da varsayılan.
        a.Ses.Esitleme = true;
        return a;
    }

    public static ProfilAyarlari For(ExportProfileId id) => id == ExportProfileId.TikTok ? TikTok() : YouTube();

    /// <summary>
    /// Ön ayar penceresindeki "Önerilen ayarlar" (0.13.0, kullanıcının isteği: iki örnek videonun kodlaması; 0.13.1'den
    /// beri ekran kartıyla, örnekler de donanımla kodlanmış). Programın kendi listesidir: ayar dosyasına yazılmaz,
    /// kullanıcının kaydettiği ön ayarlara karışmaz; "Yükle" ile şu anki profile uygulanır. Görüntü kaynakla aynı boyut ve
    /// kare hızında, ses yüksekliği TikTok varsayılanındaki gibi (-14 LUFS, yalnızca kısık ses yükseltilir; tepe sınırlayıcı
    /// -1 dB). Kodlayıcı x264'e çevrilirse aynı yapı x264'le kodlanır (ekran kartı olmayan bilgisayar için).
    /// </summary>
    public static IReadOnlyList<OnerilenAyar> Onerilenler() =>
    [
        new("38m videosu gibi",
            "Ekran kartı (NVENC) · 12 Mb/s ortalama, en çok 16 Mb/s (değişken) · High · P-kare tek referans · 1 B-kare (IPBPB, "
            + "sabit; referans değil) · ağırlıklı tahmin yok · uzamsal AQ · 8x8 DCT · CABAC · her saniye anahtar kare (sahne "
            + "değişiminde ek anahtar kare yok) · MOV · FDK AAC-LC 128 kb/s 44,1 kHz stereo",
            Onerilen38m()),
        new("1717504691091 videosu gibi",
            "Ekran kartı (NVENC) · 52 Mb/s ortalama, en çok 62 Mb/s (değişken) · Main · 3 referans kare · B-kare yok · ağırlıklı "
            + "tahmin yok · AQ yok · CABAC · 30 karede bir anahtar kare (sahne değişiminde ek anahtar kare yok) · MP4 · FDK "
            + "AAC-LC 512 kb/s 48 kHz stereo",
            Onerilen1717504691091()),
    ];

    /// <summary>
    /// "38m" örnek videosunun kodlaması (TikTok'un düzenleyicisiyle telefonda donanımla kodlanmış 1080x1920 30 fps; ölçüldü):
    /// H.264 High, DPB 2 kare, P-kareler tek referansa bakar, IPBPB (her P'den sonra bir referans olmayan B-kare, hep
    /// aynı dizilir), B-karede uzamsal doğrudan tahmin, ağırlıklı tahmin yok (P ve B), 8x8 DCT, CABAC, kare başına tek
    /// dilim; makroblok QP'si kare içinde değişir (uyarlamalı niceleme, I-karede 15–30); anahtar kare her saniye (IDR),
    /// sahne değişiminde ek anahtar kare yok; değişken bit hızı ortalama 12,2 Mb/s, en yüksek 1 saniyesi 15,9 Mb/s;
    /// BT.709 sınırlı aralık; ses AAC-LC 44,1 kHz stereo (~138 kb/s değişken).
    /// Ekran kartında (kullanıcının kartında denendi, 0.13.1): p7 hq, VBR 12000 / en çok 16000 / tampon 16000, çok
    /// geçiş fullres, B-kare 1 referans değil, uyarlamalı B-kare kapalı (kart yoksa hızlı yerlerde B yerine P koyuyordu),
    /// referans 1 (DPB 2), uzamsal AQ 8, zamansal AQ yok, ağırlıklı tahmin yok, CABAC, NVDEC; başlıkları örnekle aynı
    /// (yalnızca B-karede doğrudan tahmin zamansal: ffmpeg o ayarı vermiyor). Önden bakış kartın ön ayarınınki.
    /// Kodlayıcı x264'e çevrilirse: veryslow, 2 geçiş 12000 kb/s, VBV 16000 / 16000, ref 2, B-kare 1 (uyarlamalı B-kare
    /// ve piramit kapalı), weightp 0, weightb 0. Anahtar kare 1 sn, sahne eşiği 0. Seviye kaynakla aynı (yetmezse
    /// yükselir). Ses TikTok varsayılanınınki (FDK AAC-LC 128 kb/s 44,1 kHz).
    /// </summary>
    public static ProfilAyarlari Onerilen38m()
    {
        var a = TikTok();
        a.Video.Kodlayici = VideoKodlayici.Nvenc;
        a.Video.Nvenc = new NvencAyarlari
        {
            OnAyar = "p7",
            Ayar = "hq",
            Profil = "high",
            Seviye = "kaynak",
            Mod = NvencModu.DegiskenBitHizi,
            BitHizi = 12000,
            MaksBitHizi = 16000,
            Tampon = 16000,
            CokGecis = "fullres",
            IleriBakis = null,
            UzamsalAq = true,
            AqGucu = 8,
            ZamansalAq = false,
            BKare = 1,
            BRefModu = "disabled",
            UyarlamaliB = false,
            Ref = 1,
            AgirlikliTahmin = false,
            Kodlama = "cabac",
            GpuCozme = true,
        };

        a.Video.X264 = new X264Ayarlari
        {
            OnAyar = "veryslow",
            Ayar = string.Empty,
            Profil = "high",
            Seviye = "kaynak",
            Mod = X264Modu.IkiGecis,
            BitHizi = 12000,
            HizliIlkGecis = true,
            IsParcacigi = 8,
            Ref = 2,
            BKare = 1,
            BUyarlamali = 0,
            BPiramit = "none",
            AgirlikliP = 0,
            AgirlikliB = false,
            Dct8x8 = true,
            Cabac = true,
            VbvMaks = 16000,
            VbvTampon = 16000,
        };

        var k = a.AnahtarKare;
        k.Birim = AralikBirimi.Saniye;
        k.EnBuyuk = 1;
        k.EnKucuk = null;
        k.SahneDegisimi = 0;
        k.AcikGop = false;
        k.IntraYenileme = false;
        return a;
    }

    /// <summary>
    /// "1717504691091" örnek videosunun kodlaması (bilgisayarda ekran kartıyla kodlanmış 1920x1920 60 fps; ölçüldü): H.264
    /// Main, 3 referans kare, B-kare yok, ağırlıklı tahmin yok, CABAC, kare başına tek dilim; kare içinde makroblok QP'si
    /// değişmiyor (uyarlamalı niceleme yok); tam 30 karede bir anahtar kare (IDR), sahne değişiminde ek anahtar kare yok;
    /// değişken bit hızı ortalama 52,3 Mb/s, saniyeler 41,8–61,5 Mb/s; ses AAC-LC 48 kHz stereo ~509 kb/s; MP4.
    /// Ekran kartında (kullanıcının kartında denendi, 0.13.1): p7 hq, VBR 52000 / en çok 62000 / tampon 62000, çok geçiş
    /// fullres, B-kare 0, referans 3 (DPB 3), AQ yok (uzamsal da zamansal da), ağırlıklı tahmin yok, CABAC, NVDEC; dosyanın
    /// başlıkları örneğinkiyle birebir (POC türü 2, kare numarası alanı, constraint_set1, zamanlama, P-karelerin referans
    /// sayısı IDR'den sonra 1, 2, sonra 3), yani örnek de NVENC'le kodlanmış. Önden bakış kartın ön ayarınınki.
    /// Kodlayıcı x264'e çevrilirse: veryslow, Main, 2 geçiş 52000 kb/s, VBV 62000 / 62000, ref 3, B-kare 0, weightp 0.
    /// Anahtar kare 30 kare, sahne eşiği 0. Seviye kaynakla aynı (yetmezse yükselir: 62 Mb/s Main 4.2'nin sınırını aşar,
    /// 5.0 / 5.1 olur). Ses FDK AAC-LC 512 kb/s 48 kHz.
    /// </summary>
    public static ProfilAyarlari Onerilen1717504691091()
    {
        var a = TikTok();
        a.Video.Kap = KapBicimi.Mp4;
        a.Video.Kodlayici = VideoKodlayici.Nvenc;
        a.Video.Nvenc = new NvencAyarlari
        {
            OnAyar = "p7",
            Ayar = "hq",
            Profil = "main",
            Seviye = "kaynak",
            Mod = NvencModu.DegiskenBitHizi,
            BitHizi = 52000,
            MaksBitHizi = 62000,
            Tampon = 62000,
            CokGecis = "fullres",
            IleriBakis = null,
            UzamsalAq = false,
            AqGucu = 8,
            ZamansalAq = false,
            BKare = 0,
            BRefModu = "disabled",
            UyarlamaliB = true,
            Ref = 3,
            AgirlikliTahmin = false,
            Kodlama = "cabac",
            GpuCozme = true,
        };

        a.Video.X264 = new X264Ayarlari
        {
            OnAyar = "veryslow",
            Ayar = string.Empty,
            Profil = "main",
            Seviye = "kaynak",
            Mod = X264Modu.IkiGecis,
            BitHizi = 52000,
            HizliIlkGecis = true,
            IsParcacigi = 8,
            Ref = 3,
            BKare = 0,
            AgirlikliP = 0,
            Dct8x8 = false,
            Cabac = true,
            VbvMaks = 62000,
            VbvTampon = 62000,
        };

        var k = a.AnahtarKare;
        k.Birim = AralikBirimi.Kare;
        k.EnBuyuk = 30;
        k.EnKucuk = null;
        k.SahneDegisimi = 0;
        k.AcikGop = false;
        k.IntraYenileme = false;

        FdkAac128(a.Ses, 48000);
        a.Ses.Fdk.BitHizi = 512;
        return a;
    }
}

/// <summary>Ön ayar penceresindeki önerilen ayar: adı, ne olduğu (kısa) ve ayarları.</summary>
public sealed record OnerilenAyar(string Ad, string Aciklama, ProfilAyarlari Ayarlar);

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
