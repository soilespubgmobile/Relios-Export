using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

/// <summary>
/// Kodlama kodunun video gerektirmeyen kontrolleri (--birim-kontrol):
/// H.264 seviye hesabı (beklenenler x264'ün kendi seviye seçimiyle 552 durumda doğrulandı),
/// NVENC komutunda seviye / tampon / Baseline ve programın veri klasörü seçimi.
/// </summary>
internal static class BirimKontrol
{
    private static int _ok;
    private static int _fark;

    public static int Calistir()
    {
        Seviyeler();
        NvencKomutlari();
        AnahtarKareler();
        Klasorler();
        Console.WriteLine();
        Console.WriteLine($"Birim kontrol: {_ok + _fark} kontrol, {_fark} fark.");
        return _fark == 0 ? 0 : 1;
    }

    private static void Kontrol(string ad, string beklenen, string cikti)
    {
        var ok = beklenen == cikti;
        if (ok)
        {
            _ok++;
        }
        else
        {
            _fark++;
        }

        Console.WriteLine($"{(ok ? "OK  " : "FARK")}  {ad,-58} beklenen: {beklenen,-8} çıktı: {cikti}");
    }

    private static void Seviyeler()
    {
        Console.WriteLine("--- H.264 seviye hesabı (genişlik×yükseklik@fps, bit hızı / tampon kb, referans, profil)");
        (int W, int H, double Fps, int? Br, int? Buf, int Refs, bool High, string? Beklenen)[] durumlar =
        [
            (1920, 1080, 60, 34375, 34375, 3, true, "4.2"),
            (1920, 1080, 60, 34375, 34375, 3, false, "4.2"),
            (1080, 1920, 30, 34375, 34375, 3, false, "4.1"),
            (1080, 1920, 60, 34375, 68750, 3, false, "5.0"),
            (1080, 1920, 60, 34375, 68750, 3, true, "4.2"),
            (1280, 720, 60, 34375, 34375, 3, true, "4.1"),
            (720, 1280, 30, 34375, 34375, 3, false, "4.1"),
            (2160, 3840, 60, 34375, 34375, 3, true, "5.2"),
            (2160, 3840, 30, 34375, 34375, 3, true, "5.1"),
            (2560, 1440, 60, 34375, 34375, 3, false, "5.1"),
            (3840, 2160, 30, null, null, 3, false, "5.1"),
            (1920, 1080, 30, null, null, 3, true, "4.0"),
            (1920, 1080, 30, null, null, 5, true, "5.0"),
            (1920, 1080, 60, null, null, 5, true, "5.0"),
            (1216, 2160, 30000.0 / 1001, 14000, 28000, 3, false, "5.0"),
            (1216, 2160, 30000.0 / 1001, 14000, 28000, 3, true, "5.0"),
            (640, 360, 30, 14000, 28000, 3, true, "4.0"),
            (7680, 4320, 30, 34375, 34375, 3, true, "6.0"),
            (7680, 4320, 60, 34375, 34375, 3, true, "6.1"),
            (8192, 8192, 60, null, null, 3, true, null),
        ];

        foreach (var d in durumlar)
        {
            var c = H264Seviye.EnAz(d.W, d.H, d.Fps, d.Br, d.Buf, d.Refs, d.High) ?? "yok";
            Kontrol(
                $"{d.W}×{d.H}@{d.Fps:0.##} {d.Br?.ToString() ?? "-"}/{d.Buf?.ToString() ?? "-"} ref {d.Refs} {(d.High ? "High" : "Main")}",
                d.Beklenen ?? "yok",
                c);
        }

        Kontrol("Sıra: 4.2 > 4.1", "True", (H264Seviye.Sira("4.2") > H264Seviye.Sira("4.1")).ToString());
        Kontrol("Sıra: 1b, 1 ile 1.1 arasında", "True",
            (H264Seviye.Sira("1b") > H264Seviye.Sira("1") && H264Seviye.Sira("1b") < H264Seviye.Sira("1.1")).ToString());
        Kontrol("EnYuksek(4.2, 4.1)", "4.2", H264Seviye.EnYuksek("4.2", "4.1"));
        Kontrol("EnYuksek(3.2, 4.1)", "4.1", H264Seviye.EnYuksek("3.2", "4.1"));
    }

    private static MediaInfo Kaynak(int w, int h, double fps, string codec, string? profil, int? seviye) =>
        new(w, h, 1, 1, 0, 10, false, fps, "bt709", "bt709", "tv")
        {
            VideoCodec = codec,
            VideoProfil = profil,
            VideoSeviye = seviye,
        };

    private static string NvencKomutu(MediaInfo m, Action<ProfilAyarlari>? degistir = null, ProfilAyarlari? temel = null)
    {
        var a = temel ?? Varsayilanlar.TikTok();
        degistir?.Invoke(a);
        var plan = ArgsBuilder.Olustur(a, m, "KAYNAK.mp4", SesSecimi.Yok, "CIKTI.mp4", "GECIS");
        return " " + string.Join(' ', plan.Gecisler[0]) + " ";
    }

    private static void Iceriyor(string ad, string komut, string parca) =>
        Kontrol(ad + ": " + parca.Trim(), "var", komut.Contains(parca, StringComparison.Ordinal) ? "var" : "yok");

    private static void Icermiyor(string ad, string komut, string parca) =>
        Kontrol(ad + ": " + parca.Trim() + " olmamalı", "yok", komut.Contains(parca, StringComparison.Ordinal) ? "var" : "yok");

    private static void NvencKomutlari()
    {
        Console.WriteLine();
        Console.WriteLine("--- TikTok varsayılanı (NVENC CBR 34375 kb/s) komutları");

        var k = NvencKomutu(Kaynak(1080, 1920, 60, "h264", "Main", 42));
        Iceriyor("Main@4.2 1080×1920@60", k, " -profile:v main -level:v 4.2 -rc cbr -b:v 34375k -bufsize 34375k ");
        // ffmpeg'de -refs yalnızca kare başına referans; DPB ayrıca verilmezse kart 6 kare seçer ve 4.2'yi reddeder
        // (kullanıcının kartında denendi: -dpb_size 3 ile High@4.2, max_num_ref_frames 3).
        Iceriyor("Main@4.2 1080×1920@60", k, " -bf 0 -refs 3 -dpb_size 3 ");

        k = NvencKomutu(Kaynak(1920, 1080, 60, "h264", "High", 42));
        Iceriyor("High@4.2 1920×1080@60", k, " -profile:v high -level:v 4.2 -rc cbr -b:v 34375k -bufsize 34375k ");

        k = NvencKomutu(Kaynak(1920, 1080, 30, "h264", "High", 40));
        Iceriyor("High@4.0 1080p30 (bit hızı 4.0'ı aşar)", k, " -profile:v high -level:v 4.1 ");

        k = NvencKomutu(Kaynak(1280, 720, 60, "h264", "High", 32));
        Iceriyor("High@3.2 720p60", k, " -level:v 4.1 ");

        k = NvencKomutu(Kaynak(2160, 3840, 60, "h264", "High", 51));
        Iceriyor("High@5.1 2160×3840@60 (5.1 60 fps'e yetmez)", k, " -level:v 5.2 ");

        k = NvencKomutu(Kaynak(1080, 1920, 60, "h264", "High", 52));
        Iceriyor("High@5.2 1080×1920@60 (kaynak daha yüksek: aynı kalır)", k, " -level:v 5.2 ");

        k = NvencKomutu(Kaynak(720, 1280, 30, "h264", "Constrained Baseline", 31));
        Iceriyor("Baseline kaynak", k, " -profile:v baseline -level:v 4.1 ");
        Iceriyor("Baseline kaynak", k, " -bf 0 ");
        Iceriyor("Baseline kaynak", k, " -coder cavlc ");
        Icermiyor("Baseline kaynak", k, " -coder cabac ");

        k = NvencKomutu(Kaynak(3840, 2160, 60, "hevc", "Main 10", 153));
        Iceriyor("HEVC kaynak", k, " -profile:v main -rc cbr ");
        Icermiyor("HEVC kaynak", k, " -level:v ");

        k = NvencKomutu(Kaynak(1920, 1080, 30, "h264", "High", 0));
        Icermiyor("Seviye bilgisi bozuk (0)", k, " -level:v ");

        k = NvencKomutu(Kaynak(1920, 1080, 30, "h264", "High", 40), a => a.Video.Nvenc.Seviye = "4.0");
        Iceriyor("Elle seçilen seviye değişmez", k, " -level:v 4.0 ");

        k = NvencKomutu(Kaynak(1920, 1080, 30, "h264", "High", 40), a => a.Video.Nvenc.Mod = NvencModu.DegiskenBitHizi);
        Icermiyor("VBR'de tampon ffmpeg'e bırakılır", k, " -bufsize ");

        k = NvencKomutu(Kaynak(1920, 1080, 30, "h264", "High", 40), a => a.Video.Nvenc.Tampon = 20000);
        Iceriyor("Elle girilen tampon", k, " -bufsize 20000k ");

        Console.WriteLine();
        Console.WriteLine("--- YouTube'da NVENC seçilirse (referans kare boş, 3 B-kare, sabit kalite)");
        static void Nvenc(ProfilAyarlari a) => a.Video.Kodlayici = VideoKodlayici.Nvenc;

        k = NvencKomutu(Kaynak(1920, 1080, 60, "h264", "High", 42), Nvenc, Varsayilanlar.YouTube());
        Iceriyor("High@4.2 1080p60", k, " -profile:v high -level:v 4.2 ");
        Iceriyor("High@4.2 1080p60 (4.2, 1080p'de en fazla 4 kare)", k, " -dpb_size 4 ");
        Icermiyor("High@4.2 1080p60", k, " -refs ");

        k = NvencKomutu(Kaynak(2160, 3840, 30, "h264", "High", 51), Nvenc, Varsayilanlar.YouTube());
        Iceriyor("High@5.1 2160×3840@30 (5.1'de en fazla 5 kare)", k, " -level:v 5.1 ");
        Iceriyor("High@5.1 2160×3840@30", k, " -dpb_size 5 ");

        k = NvencKomutu(Kaynak(1920, 1080, 60, "h264", "High", 42), a => { Nvenc(a); a.Video.Nvenc.Seviye = string.Empty; }, Varsayilanlar.YouTube());
        Icermiyor("Seviye otomatik: DPB'yi kart seçer", k, " -dpb_size ");
        Icermiyor("Seviye otomatik", k, " -level:v ");
    }

    private static void AnahtarKareler()
    {
        Console.WriteLine();
        Console.WriteLine("--- Anahtar kare aralığı: YouTube 30 fps'e göre 50 / 26 (aynı süre)");
        var k = Varsayilanlar.YouTube().AnahtarKare;
        (double Fps, string Beklenen)[] durumlar =
        [
            (30000.0 / 1001, "50/26"), (30, "50/26"), (25, "42/22"), (24, "40/21"), (24000.0 / 1001, "40/21"),
            (50, "83/42"), (60000.0 / 1001, "100/51"), (60, "100/51"), (120, "200/101"),
        ];
        foreach (var (fps, beklenen) in durumlar)
        {
            var (g, m) = AnahtarKareHesabi.Aralik(k, fps);
            Kontrol($"YouTube {fps:0.###} fps", beklenen, $"{g}/{m}");
        }

        var tiktok916 = Varsayilanlar.Dikey916().AnahtarKare;
        var (g9, m9) = AnahtarKareHesabi.Aralik(tiktok916, 30000.0 / 1001);
        Kontrol("Dikey 9:16 (sabit 29,97 fps, kare)", "50/26", $"{g9}/{m9}");

        var elle = new AnahtarKareAyarlari { Birim = AralikBirimi.Kare, EnBuyuk = 50, EnKucuk = 40 };
        var (ge, me) = AnahtarKareHesabi.Aralik(elle, 30);
        Kontrol("En az, en fazlanın yarısı + 1'i geçemez (50 / 40)", "50/26", $"{ge}/{me}");

        var x = KomutMetni(Kaynak(1920, 1080, 60, "h264", "High", 42), Varsayilanlar.YouTube());
        Iceriyor("YouTube x264 60 fps", x, ":keyint=100:min-keyint=51:");
        x = KomutMetni(Kaynak(1920, 1080, 30, "h264", "High", 40), Varsayilanlar.YouTube());
        Iceriyor("YouTube x264 30 fps", x, ":keyint=50:min-keyint=26:");
        x = KomutMetni(Kaynak(1920, 1080, 60, "h264", "High", 42), Varsayilanlar.TikTok());
        Iceriyor("TikTok NVENC 60 fps (30 kare, değişmez)", x, " -g 30 ");
    }

    private static string KomutMetni(MediaInfo m, ProfilAyarlari a)
    {
        var plan = ArgsBuilder.Olustur(a, m, "KAYNAK.mp4", SesSecimi.Yok, "CIKTI.mp4", "GECIS");
        return " " + string.Join(' ', plan.Gecisler[0]) + " ";
    }

    private static void Klasorler()
    {
        Console.WriteLine();
        Console.WriteLine("--- Veri klasörü (ayarlar, hata günlüğü): arşivin içinden açılınca kalıcı klasör");
        var gecici = Path.GetTempPath();
        Kontrol("WinRAR geçici klasörü", "True", AppPaths.GeciciKlasordeMi(Path.Combine(gecici, "Rar$EXa6772.2514.rartemp")).ToString());
        Kontrol("7-Zip geçici klasörü", "True", AppPaths.GeciciKlasordeMi(Path.Combine(gecici, "7zO8A1B2C3D")).ToString());
        Kontrol("Windows zip açıcısı", "True", AppPaths.GeciciKlasordeMi(Path.Combine(gecici, "Temp1_relios-export-win64.zip")).ToString());
        Kontrol("Geçici klasörün kendisi", "True", AppPaths.GeciciKlasordeMi(gecici).ToString());

        var kok = Path.GetPathRoot(Path.GetFullPath(gecici)) ?? "C:\\";
        Kontrol("Normal klasör", "False", AppPaths.GeciciKlasordeMi(Path.Combine(kok, "Programlar", "Relios Export")).ToString());
        Kontrol("Bu aracın klasörü (geçici değil)", "False", AppPaths.GeciciKlasorden.ToString());
        Kontrol("Veri klasörü exe'nin yanı", AppPaths.AppFolder, AppPaths.VeriKlasoru);
    }
}
