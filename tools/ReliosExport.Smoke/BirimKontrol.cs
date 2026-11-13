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

    private static string NvencKomutu(MediaInfo m, Action<ProfilAyarlari>? degistir = null)
    {
        var a = Varsayilanlar.TikTok();
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
