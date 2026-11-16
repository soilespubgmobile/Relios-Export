using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

/// <summary>
/// Kodlama kodunun video gerektirmeyen kontrolleri (--birim-kontrol):
/// H.264 seviye hesabı (beklenenler x264'ün kendi seviye seçimiyle 552 durumda doğrulandı),
/// NVENC ve x264 komutunda seviye / tampon / Baseline ve programın veri klasörü seçimi.
/// </summary>
internal static class BirimKontrol
{
    private static int _ok;
    private static int _fark;

    public static int Calistir()
    {
        Seviyeler();
        NvencKomutlari();
        X264Seviyeleri();
        AnahtarKareler();
        SesKanallari();
        SesIzleri();
        SesYuksekligi();
        Boyutlar();
        Kaplar();
        AyarGecisi();
        Klasorler();
        HataYazilari();
        FdkSinirlari();
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

        k = NvencKomutu(Kaynak(1920, 1080, 30, "h264", "High", 40), a =>
        {
            a.Video.Nvenc.Mod = NvencModu.DegiskenBitHizi;
            a.Video.Nvenc.Tampon = null;
        });
        Icermiyor("VBR'de tampon boşsa ffmpeg'e bırakılır", k, " -bufsize ");

        k = NvencKomutu(Kaynak(1920, 1080, 30, "h264", "High", 40));
        Iceriyor("TikTok varsayılanı: tampon boş, bit hızı kadar", k, " -b:v 34375k -bufsize 34375k ");

        k = NvencKomutu(Kaynak(1920, 1080, 30, "h264", "High", 40), a => a.Video.Nvenc.Tampon = 20000);
        Iceriyor("Elle girilen tampon", k, " -bufsize 20000k ");

        // Elle 55000 kb girilirse 4.2 yine yeter (Main 62500, High 78125 kbit).
        k = NvencKomutu(Kaynak(1080, 1920, 60, "h264", "Main", 42), a => a.Video.Nvenc.Tampon = 55000);
        Iceriyor("Main@4.2 1080×1920@60, elle tampon 55000", k, " -level:v 4.2 -rc cbr -b:v 34375k -bufsize 55000k ");

        Console.WriteLine();
        Console.WriteLine("--- NVENC kipleri ve kartın kabul etmediği birleşimler (kullanıcının kartında denendi)");
        var hd = Kaynak(1080, 1920, 30, "h264", "High", 40);

        // CBR'de en yüksek bit hızını kart yok sayar (dosyanın HRD'sinde bit hızı yazar): komuta konmaz.
        k = NvencKomutu(hd, a => a.Video.Nvenc.MaksBitHizi = 30000);
        Icermiyor("CBR + en yüksek bit hızı 30000", k, " -maxrate ");
        Iceriyor("CBR + en yüksek bit hızı 30000", k, " -rc cbr -b:v 34375k -bufsize 34375k ");

        k = NvencKomutu(hd, a =>
        {
            a.Video.Nvenc.Mod = NvencModu.DegiskenBitHizi;
            a.Video.Nvenc.BitHizi = 20000;
            a.Video.Nvenc.MaksBitHizi = 30000;
            a.Video.Nvenc.Tampon = 40000;
        });
        Iceriyor("VBR 20000 / 30000 / 40000", k, " -rc vbr -b:v 20000k -maxrate 30000k -bufsize 40000k ");
        Iceriyor("VBR 20000 / 30000 / 40000 (4.0'ın 25000 / 31250'si aşılır)", k, " -level:v 4.1 ");

        // Sabit kalitede ffmpeg tamponu yok sayar ("CQ mode shall discard ... vbv buffer size"): komuta konmaz.
        k = NvencKomutu(hd, a =>
        {
            a.Video.Nvenc.Mod = NvencModu.SabitKalite;
            a.Video.Nvenc.MaksBitHizi = 30000;
            a.Video.Nvenc.Tampon = 40000;
        });
        Iceriyor("Sabit kalite + en yüksek 30000", k, " -rc vbr -cq 19 -b:v 0 -maxrate 30000k -multipass ");
        Icermiyor("Sabit kalite + tampon 40000", k, " -bufsize ");

        // Sabit QP'de -b:v 0: yoksa ffmpeg'in 2 Mb/s varsayılanı HRD'ye yazılırdı.
        k = NvencKomutu(hd, a => a.Video.Nvenc.Mod = NvencModu.SabitQp);
        Iceriyor("Sabit QP 20", k, " -rc constqp -qp 20 -b:v 0 -multipass ");
        Icermiyor("Sabit QP", k, " -maxrate ");
        Icermiyor("Sabit QP", k, " -bufsize ");

        // B-kare referans olunca DPB en az 3 ("DPB size should be greater than or equal to 3 for B as reference").
        k = NvencKomutu(hd, a =>
        {
            a.Video.Nvenc.BKare = 2;
            a.Video.Nvenc.BRefModu = "middle";
            a.Video.Nvenc.Ref = 1;
        });
        Iceriyor("2 B-kare (ortadaki referans) + referans 1", k, " -bf 2 -b_ref_mode middle -refs 1 -dpb_size 3 ");
        k = NvencKomutu(hd, a =>
        {
            a.Video.Nvenc.BKare = 2;
            a.Video.Nvenc.BRefModu = "each";
            a.Video.Nvenc.Ref = 2;
        });
        Iceriyor("2 B-kare (her biri referans) + referans 2", k, " -bf 2 -b_ref_mode each -refs 2 -dpb_size 3 ");
        k = NvencKomutu(hd, a =>
        {
            a.Video.Nvenc.BKare = 2;
            a.Video.Nvenc.BRefModu = "disabled";
            a.Video.Nvenc.Ref = 1;
        });
        Iceriyor("2 B-kare (referans değil) + referans 1", k, " -bf 2 -b_ref_mode disabled -refs 1 -dpb_size 2 ");
        k = NvencKomutu(hd, a =>
        {
            a.Video.Nvenc.BKare = 2;
            a.Video.Nvenc.Ref = 4;
        });
        Iceriyor("2 B-kare + referans 4 (yeter)", k, " -refs 4 -dpb_size 4 ");

        // Ağırlıklı tahmin B-karelerle kullanılamaz ("Weighted Prediction not supported with B-frames"): B-kare 0.
        k = NvencKomutu(hd, a =>
        {
            a.Video.Nvenc.BKare = 2;
            a.Video.Nvenc.AgirlikliTahmin = true;
        });
        Iceriyor("Ağırlıklı tahmin + 2 B-kare", k, " -bf 0 -refs 3 -dpb_size 3 -weighted_pred 1 ");
        Icermiyor("Ağırlıklı tahmin + 2 B-kare", k, " -b_ref_mode ");
        k = NvencKomutu(hd, a =>
        {
            a.Video.Nvenc.BKare = null;
            a.Video.Nvenc.AgirlikliTahmin = true;
        });
        Iceriyor("Ağırlıklı tahmin + B-kare boş (kartın varsayılanı)", k, " -bf 0 ");
        k = NvencKomutu(hd, a =>
        {
            a.Video.Nvenc.Profil = "baseline";
            a.Video.Nvenc.AgirlikliTahmin = true;
        });
        Icermiyor("Baseline + ağırlıklı tahmin (Baseline'da yok)", k, " -weighted_pred ");

        // High 4:4:4'te kart yalnızca CAVLC kullanır.
        k = NvencKomutu(hd, a => a.Video.Nvenc.Profil = "high444p");
        Iceriyor("High 4:4:4 + CABAC seçili", k, " -profile:v high444p ");
        Iceriyor("High 4:4:4 + CABAC seçili", k, " -coder cavlc ");
        Iceriyor("High 4:4:4", k, ",format=yuv444p,");

        Console.WriteLine();
        Console.WriteLine("--- NVDEC: kareler önce kaynağın piksel biçimine çevrilir (işlemciyle çözmeyle bit bit aynı sonuç)");
        static void Nvdec(ProfilAyarlari a) => a.Video.Nvenc.GpuCozme = true;
        k = NvencKomutu(hd with { PikselBicimi = "yuv420p" }, Nvdec);
        Iceriyor("NVDEC, 8 bit kaynak", k, " -hwaccel cuda -i KAYNAK.mp4 ");
        Iceriyor("NVDEC, 8 bit kaynak", k, " -vf format=yuv420p,scale=");
        k = NvencKomutu(Kaynak(3840, 2160, 60, "hevc", "Main 10", 153) with { PikselBicimi = "yuv420p10le" }, Nvdec);
        Iceriyor("NVDEC, HEVC 10 bit kaynak", k, " -vf format=yuv420p10le,scale=");
        k = NvencKomutu(hd with { PikselBicimi = "yuvj420p" }, Nvdec);
        Iceriyor("NVDEC, tam aralık (yuvj420p) kaynak", k, " -vf format=yuv420p,scale=");
        k = NvencKomutu(hd with { PikselBicimi = "yuv420p" }, a =>
        {
            Nvdec(a);
            a.Video.KareHiziModu = KareHiziModu.Sabit;
            a.Video.KareHizi = "30";
        });
        Iceriyor("NVDEC + sabit kare hızı", k, " -vf format=yuv420p,fps=30,scale=");
        k = NvencKomutu(hd with { PikselBicimi = "yuv420p" });
        Icermiyor("NVDEC kapalı", k, " -hwaccel ");
        Icermiyor("NVDEC kapalı", k, "format=yuv420p,scale=");
        k = NvencKomutu(hd with { PikselBicimi = "yuv420p" }, a =>
        {
            Nvdec(a);
            a.Video.Kodlayici = VideoKodlayici.X264;
        });
        Icermiyor("x264 seçili (NVDEC kutusu açık kalsa da)", k, " -hwaccel ");
        Icermiyor("x264 seçili (NVDEC kutusu açık kalsa da)", k, "format=yuv420p,scale=");
        k = NvencKomutu(hd, Nvdec);
        Icermiyor("NVDEC, piksel biçimi bilinmiyor", k, " -vf format=");

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

    private static void X264Seviyeleri()
    {
        Console.WriteLine();
        Console.WriteLine("--- YouTube varsayılanı (x264 CRF 18, VBV 34375 / 55000): seviye kaynakla aynı, sınır aşılırsa yeten en düşük");

        static string Yt(MediaInfo m, Action<X264Ayarlari>? degistir = null)
        {
            var a = Varsayilanlar.YouTube();
            degistir?.Invoke(a.Video.X264);
            return KomutMetni(m, a);
        }

        static void VbvYok(X264Ayarlari x)
        {
            x.VbvMaks = null;
            x.VbvTampon = null;
        }

        var k = Yt(Kaynak(1920, 1080, 30, "h264", "High", 40));
        Iceriyor("High@4.0 1080p30 (34375 kb/s 4.0'ı aşar)", k, " -profile:v high -level:v 4.1 ");
        Iceriyor("High@4.0 1080p30", k, ":vbv-maxrate=34375:vbv-bufsize=55000:");
        Iceriyor("High@4.0 1080×1920@29.97", Yt(Kaynak(1080, 1920, 30000.0 / 1001, "h264", "High", 40)), " -level:v 4.1 ");
        Iceriyor("High@4.2 1080p60 (aynı kalır)", Yt(Kaynak(1920, 1080, 60, "h264", "High", 42)), " -level:v 4.2 ");
        Iceriyor("High@3.2 1080×1080@30", Yt(Kaynak(1080, 1080, 30, "h264", "High", 32)), " -level:v 4.1 ");
        Iceriyor("Main@4.0 1080p30", Yt(Kaynak(1920, 1080, 30, "h264", "Main", 40)), " -profile:v main -level:v 4.1 ");
        Iceriyor("Main@3.1 720p30", Yt(Kaynak(1280, 720, 30, "h264", "Main", 31)), " -level:v 4.1 ");
        Iceriyor("High@5.1 4K30 (aynı kalır)", Yt(Kaynak(3840, 2160, 30, "h264", "High", 51)), " -level:v 5.1 ");
        Iceriyor("High@5.1 4K60 (5.1 60 fps'e yetmez)", Yt(Kaynak(3840, 2160, 60, "h264", "High", 51)), " -level:v 5.2 ");
        Icermiyor("HEVC kaynak: seviyeyi x264 seçer", Yt(Kaynak(1920, 1080, 30, "hevc", "Main", 120)), " -level:v ");
        Iceriyor("Elle seçilen seviye değişmez", Yt(Kaynak(1920, 1080, 30, "h264", "High", 40), x => x.Seviye = "4.0"), " -level:v 4.0 ");
        Iceriyor("VBV boş: 4.0 yeter", Yt(Kaynak(1920, 1080, 30, "h264", "High", 40), VbvYok), " -level:v 4.0 ");
        Iceriyor("CRF'de yalnızca tampon: x264 yok sayar", Yt(Kaynak(1920, 1080, 30, "h264", "High", 40), x => x.VbvMaks = null), " -level:v 4.0 ");
        Iceriyor("Sabit QP: VBV yok", Yt(Kaynak(1920, 1080, 30, "h264", "High", 40), x => x.Mod = X264Modu.SabitQp), " -level:v 4.0 ");
        Iceriyor("CBR 40000, tampon boş (2 katı: 80000)", Yt(Kaynak(1920, 1080, 30, "h264", "High", 40), x =>
        {
            x.Mod = X264Modu.Cbr;
            x.BitHizi = 40000;
            x.VbvTampon = null;
        }), " -level:v 5.0 ");
        Iceriyor("Ortalama 30000, en yüksek boş (ortalamaya eşit)", Yt(Kaynak(1920, 1080, 30, "h264", "High", 40), x =>
        {
            x.Mod = X264Modu.OrtalamaBitHizi;
            x.BitHizi = 30000;
            x.VbvMaks = null;
            x.VbvTampon = 40000;
        }), " -level:v 4.1 ");
        Iceriyor("Ortalama 30000, VBV yok", Yt(Kaynak(1920, 1080, 30, "h264", "High", 40), x =>
        {
            x.Mod = X264Modu.OrtalamaBitHizi;
            x.BitHizi = 30000;
            VbvYok(x);
        }), " -level:v 4.0 ");

        Console.WriteLine();
        Console.WriteLine("--- x264 referans kareleri (DPB): açık referans; B-kare varsa en az 2, B-piramitte en az 4");
        // Referans boşsa ffmpeg hız ayarının referansını seviyenin taşıyabileceğine kendisi kısar (yerelde ve
        // GitHub'da x264'ün kendi denetimiyle denendi): seviyeyi yalnızca açık referans ve B-kare yapısı zorlar.
        var hd = Kaynak(1920, 1080, 30, "h264", "High", 40);
        Iceriyor("Referans 5 (1080p'de 4.x en çok 4 kare)", Yt(hd, x => x.Ref = 5), " -level:v 5.0 ");
        Iceriyor("Ek parametre ref=5", Yt(hd, x => x.EkParametreler = "ref=5"), " -level:v 5.0 ");
        Iceriyor("Referans 5, VBV boş", Yt(hd, x =>
        {
            x.Ref = 5;
            VbvYok(x);
        }), " -level:v 5.0 ");
        Iceriyor("slow, referans boş (ffmpeg kısar)", Yt(hd, x =>
        {
            x.OnAyar = "slow";
            x.Ref = null;
        }), " -level:v 4.1 ");
        Iceriyor("slow, referans boş, VBV boş (kaynak 4.0 yeter)", Yt(hd, x =>
        {
            x.OnAyar = "slow";
            x.Ref = null;
            VbvYok(x);
        }), " -level:v 4.0 ");
        Iceriyor("veryslow, referans ve B-kare boş", Yt(hd, x =>
        {
            x.OnAyar = "veryslow";
            x.Ref = null;
            x.BKare = null;
        }), " -level:v 4.1 ");
        Iceriyor("animation, referans ve B-kare boş", Yt(hd, x =>
        {
            x.Ayar = "animation";
            x.Ref = null;
            x.BKare = null;
        }), " -level:v 4.1 ");
        Iceriyor("Blu-ray uyumlu, referans 8 (en çok 6)", Yt(hd, x =>
        {
            x.Ref = 8;
            x.BlurayUyumlu = true;
        }), " -level:v 5.0 ");

        // 352×288 (396 makroblok) seviye 1.1: en çok 900 makroblok, yani 2 kare. B-piramit 2 ve üstü B-karede 4 kare ister.
        var cif = Kaynak(352, 288, 7.5, "h264", "High", 11);
        void Cif(string ad, string beklenen, Action<X264Ayarlari> degistir) => Iceriyor("CIF 1.1: " + ad, Yt(cif, x =>
        {
            VbvYok(x);
            degistir(x);
        }), " -level:v " + beklenen + " ");

        Cif("3 referans", "1.2", _ => { });
        Cif("2 referans", "1.1", x => x.Ref = 2);
        Cif("2 referans, 1 B-kare (piramit olmaz)", "1.1", x =>
        {
            x.Ref = 2;
            x.BKare = 1;
        });
        Cif("2 referans, 2 B-kare (piramit: 4 kare)", "1.2", x =>
        {
            x.Ref = 2;
            x.BKare = 2;
        });
        Cif("2 referans, 2 B-kare, piramit yok", "1.1", x =>
        {
            x.Ref = 2;
            x.BKare = 2;
            x.BPiramit = "none";
        });
        Cif("referans boş, B-kare 0", "1.1", x => x.Ref = null);
        Cif("referans boş, B-kare 2", "1.2", x =>
        {
            x.Ref = null;
            x.BKare = 2;
        });
        Cif("referans ve B-kare boş (medium: 3 B-kare)", "1.2", x =>
        {
            x.Ref = null;
            x.BKare = null;
        });
        Cif("referans ve B-kare boş, ultrafast (B-kare yok)", "1.1", x =>
        {
            x.OnAyar = "ultrafast";
            x.Ref = null;
            x.BKare = null;
        });
        Cif("referans ve B-kare boş, zerolatency", "1.1", x =>
        {
            x.Ayar = "zerolatency";
            x.Ref = null;
            x.BKare = null;
        });
        Cif("referans ve B-kare boş, ultrafast + animation (2 B-kare)", "1.2", x =>
        {
            x.OnAyar = "ultrafast";
            x.Ayar = "animation";
            x.Ref = null;
            x.BKare = null;
        });
        Cif("ek parametre b-pyramid=none, 2 B-kare", "1.1", x =>
        {
            x.Ref = 2;
            x.BKare = 2;
            x.EkParametreler = "b-pyramid=none";
        });

        Console.WriteLine();
        Console.WriteLine("--- High'ın 1,25 kat sınırı yalnızca dosya gerçekten High ise (VBV 55000 / 70000: High 4.1'e, Main 5.0'a sığar)");
        static void Vbv55(X264Ayarlari x)
        {
            x.VbvMaks = 55000;
            x.VbvTampon = 70000;
        }

        Iceriyor("Profil kaynak (High: 8x8 DCT açılır)", Yt(hd, Vbv55), " -level:v 4.1 ");
        Iceriyor("Profil high, 8x8 DCT kapalı (x264 Main yazar)", Yt(hd, x =>
        {
            Vbv55(x);
            x.Profil = "high";
            x.Dct8x8 = false;
        }), " -level:v 5.0 ");
        Iceriyor("Profil high, 8x8 DCT açık", Yt(hd, x =>
        {
            Vbv55(x);
            x.Profil = "high";
            x.Dct8x8 = true;
        }), " -level:v 4.1 ");
        Iceriyor("Profil high, 8x8 DCT hız ayarından (medium: açık)", Yt(hd, x =>
        {
            Vbv55(x);
            x.Profil = "high";
            x.Dct8x8 = null;
        }), " -level:v 4.1 ");
        Iceriyor("Profil main", Yt(hd, x =>
        {
            Vbv55(x);
            x.Profil = "main";
        }), " -profile:v main -level:v 5.0 ");
        Iceriyor("Profil kaynak + ek parametre no-8x8dct", Yt(hd, x =>
        {
            Vbv55(x);
            x.EkParametreler = "no-8x8dct=1";
        }), " -level:v 5.0 ");
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

    private static void SesKanallari()
    {
        Console.WriteLine();
        Console.WriteLine("--- Ses kanalları: mono kaynak stereoya tam seviyede (pan), stereo kaynak olduğu gibi");
        static MediaInfo SesliKaynak(int kanal) => new(1920, 1080, 1, 1, 0, 10, true, 30, "bt709", "bt709", "tv")
        {
            VideoCodec = "h264",
            VideoProfil = "High",
            VideoSeviye = 40,
            SesAkislari = [new SesAkisi(1, "pcm_s24le", 44100, kanal, 24, null)],
        };

        string Komut(int kanal, ProfilAyarlari a)
        {
            var m = SesliKaynak(kanal);
            var ses = ArgsBuilder.SesSec(a.Ses, m, "KAYNAK.mp4");
            var plan = ArgsBuilder.Olustur(a, m, "KAYNAK.mp4", ses, "CIKTI.mov", "GECIS");
            return " " + string.Join(' ', plan.Gecisler[0]) + " ";
        }

        var k = Komut(1, Varsayilanlar.YouTube());
        Iceriyor("Mono kaynak (YouTube)", k, " -af pan=stereo|c0=c0|c1=c0,aresample=48000:resampler=soxr:precision=33:dither_method=triangular ");
        Iceriyor("Mono kaynak (YouTube)", k, " -c:a libfdk_aac -profile:a aac_low -b:a 128k -ar 48000 -ac 2 ");
        k = Komut(1, Varsayilanlar.TikTok());
        Iceriyor("Mono kaynak (TikTok)", k, " -af pan=stereo|c0=c0|c1=c0,aresample=48000:resampler=soxr:precision=33:dither_method=triangular ");
        Iceriyor("Mono kaynak (TikTok)", k, " -c:a pcm_s16le -ar 48000 -ac 2 ");
        k = Komut(2, Varsayilanlar.YouTube());
        Icermiyor("Stereo kaynak", k, "pan=");
        Iceriyor("Stereo kaynak", k, " -af aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac ");

        // YouTube varsayılanı: FDK AAC-LC 128 kb/s CBR, 48 kHz stereo; dönüşüm en yüksek kalitede (soxr 33 bit), 24 bit
        // kaynak 16 bite üçgen titreşimle iner; afterburner açık (ffmpeg'in varsayılanı: bayrak yazılmaz).
        Iceriyor("YouTube varsayılanı: FDK AAC 128 kb/s, 48 kHz, en yüksek dönüşüm, üçgen titreşim", k,
            " -af aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac -profile:a aac_low -b:a 128k -ar 48000 -ac 2 -metadata:s:a:0 language=eng ");
        Icermiyor("YouTube varsayılanı: afterburner açık", k, "-afterburner");
        Icermiyor("YouTube varsayılanı: PCM yok", k, "pcm_");

        // TikTok'ta FDK AAC seçilirse de aynı dönüşüm (en yüksek kalite, üçgen titreşim) kullanılır.
        var fdk = Varsayilanlar.TikTok();
        fdk.Ses.Kodlayici = SesKodlayici.FdkAac;
        k = Komut(2, fdk);
        Iceriyor("TikTok'ta FDK AAC seçilirse", k, " -af aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac -profile:a aac_low -b:a 128k ");
        var kaynakKanal = Varsayilanlar.YouTube();
        kaynakKanal.Ses.Kanal = KanalModu.Kaynak;
        k = Komut(1, kaynakKanal);
        Icermiyor("Kanal 'kaynakla aynı' (mono kalır)", k, "pan=");
        Icermiyor("Kanal 'kaynakla aynı' (mono kalır)", k, " -ac ");
    }

    private static void SesYuksekligi()
    {
        Console.WriteLine();
        Console.WriteLine("--- Tepe sınırlayıcı ve LUFS eşitleme (varsayılan: sınırlayıcı açık -1 dB, eşitleme açık -14 LUFS; ölçer BS.1770 ebur128)");
        static MediaInfo Kaynak48() => new(1920, 1080, 1, 1, 0, 10, true, 30, "bt709", "bt709", "tv")
        {
            VideoCodec = "h264",
            VideoProfil = "High",
            VideoSeviye = 40,
            SesAkislari = [new SesAkisi(1, "pcm_s16le", 44100, 2, 16, null)],
        };

        string Komut(ProfilAyarlari a, SesOlcum? olcum = null)
        {
            var m = Kaynak48();
            var ses = ArgsBuilder.SesSec(a.Ses, m, "KAYNAK.mp4");
            var plan = ArgsBuilder.Olustur(a, m, "KAYNAK.mp4", ses, "CIKTI.mov", "GECIS", olcum);
            return " " + string.Join(' ', plan.Gecisler[0]) + " ";
        }

        foreach (var (ad, a) in new[] { ("TikTok", Varsayilanlar.TikTok()), ("YouTube", Varsayilanlar.YouTube()) })
        {
            Kontrol(ad + ": sınırlayıcı açık", "True", a.Ses.Sinirlayici.ToString());
            Kontrol(ad + ": sınırlayıcı tavanı", "-1", a.Ses.SinirlayiciTavan.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Kontrol(ad + ": LUFS eşitleme açık (varsayılan)", "True", a.Ses.Esitleme.ToString());
            Kontrol(ad + ": LUFS hedefi", "-14", a.Ses.EsitlemeHedef.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var k = Komut(a);
            Icermiyor(ad + " varsayılanı (ses yükseltilmiyor)", k, "alimiter");
            Icermiyor(ad + " varsayılanı", k, "loudnorm");
            Icermiyor(ad + " varsayılanı", k, "ebur128");
            Iceriyor(ad + " varsayılanı: ses filtresi değişmedi", k, " -af aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a "
                + (ad == "TikTok" ? "pcm_s16le " : "libfdk_aac "));
        }

        var arti = Varsayilanlar.YouTube();
        arti.Ses.Seviye = 6;
        Iceriyor("+6 dB: seviye, 192 kHz, sınırlayıcı (-1 dB = 0,891251), 48 kHz'e dönüş", Komut(arti),
            " -af volume=6dB,aresample=192000:resampler=soxr:precision=33,alimiter=limit=0.891251:level=0:latency=1,"
            + "aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac ");
        var artiKaynak = Varsayilanlar.YouTube();
        artiKaynak.Ses.Seviye = 6;
        artiKaynak.Ses.OrnekHizi = null;
        Iceriyor("+6 dB, örnekleme 'kaynakla aynı': 192 kHz'ten kaynağın hızına (44,1 kHz)", Komut(artiKaynak),
            ",alimiter=limit=0.891251:level=0:latency=1,aresample=44100:resampler=soxr");
        Iceriyor("+6 dB, örnekleme 'kaynakla aynı'", Komut(artiKaynak), " -ar 44100 ");
        var artiYuksek = Varsayilanlar.YouTube();
        artiYuksek.Ses.Seviye = 6;
        artiYuksek.Ses.Kalite = DonusumKalitesi.Yuksek;
        artiYuksek.Ses.Titresim = Titresim.Yok;
        Iceriyor("+6 dB, dönüşüm kalitesi Yüksek: iki dönüşüm de soxr varsayılanıyla", Komut(artiYuksek),
            " -af volume=6dB,aresample=192000:resampler=soxr,alimiter=limit=0.891251:level=0:latency=1,aresample=48000:resampler=soxr -c:a ");
        arti.Ses.Sinirlayici = false;
        Icermiyor("+6 dB, sınırlayıcı kapalı", Komut(arti), "alimiter");
        var eksi = Varsayilanlar.YouTube();
        eksi.Ses.Seviye = -6;
        Icermiyor("-6 dB (kısılıyor)", Komut(eksi), "alimiter");

        var esit = Varsayilanlar.TikTok();
        esit.Ses.Esitleme = true;
        var olcum = new SesOlcum(-21.37, -3.2, 6.4);
        // Eşitleme: bütün ses aynı miktarda (sabit kazanç, -14 - (-21,37) = +7,37 dB). Tepe -3,2 + 7,37 = +4,17 > -1:
        // sınırlayıcı 192 kHz'te tepeleri tutar.
        Iceriyor("Eşitleme: sabit kazanç +7,37 dB, tepe aşıyor: 192 kHz sınırlayıcı", Komut(esit, olcum),
            " -af volume=7.37dB,aresample=192000:resampler=soxr:precision=33,alimiter=limit=0.891251:level=0:latency=1,"
            + "aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a pcm_s16le ");
        Icermiyor("Eşitleme: ikinci geçişte loudnorm yok (yalnızca sabit kazanç)", Komut(esit, olcum), "loudnorm");
        Icermiyor("Eşitleme açık ama ölçüm yok (sessiz ses)", Komut(esit), "volume=");
        var olcumAlcak = new SesOlcum(-21.37, -10, 25);
        Iceriyor("Eşitleme: tepe aşmıyor (-10 + 7,37 = -2,63), ses aralığı 25 LU: yalnızca sabit kazanç", Komut(esit, olcumAlcak),
            " -af volume=7.37dB,aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a pcm_s16le ");
        Icermiyor("Eşitleme: tepe aşmıyorsa sınırlayıcı yok", Komut(esit, olcumAlcak), "alimiter");
        var esitKapali = Varsayilanlar.TikTok();
        esitKapali.Ses.Esitleme = true;
        esitKapali.Ses.Sinirlayici = false;
        Iceriyor("Eşitleme: sınırlayıcı kutusu kapalı olsa da tepe aşıyorsa tutulur", Komut(esitKapali, olcum), ",alimiter=limit=0.891251:");
        Iceriyor("Eşitleme + telafi 2,5 dB: kazanç 9,87 dB", " " + string.Join(' ', ArgsBuilder.Olustur(esit, Kaynak48(), "KAYNAK.mp4",
            ArgsBuilder.SesSec(esit.Ses, Kaynak48(), "KAYNAK.mp4"), "CIKTI.mov", "GECIS", olcum, 2.5).Gecisler[0]) + " ",
            " -af volume=9.87dB,aresample=192000:");
        Iceriyor("Telafi ölçümü: kodlamadaki ses zinciri + ölçüm", " " + string.Join(' ', ArgsBuilder.DenemeOlcumKomutu(esit.Ses, "KAYNAK.mp4",
            ArgsBuilder.SesSec(esit.Ses, Kaynak48(), "KAYNAK.mp4"), 10, olcum, 2.5)) + " ",
            " -map 0:a:0 -vn -sn -dn -t 10 -af volume=9.87dB,aresample=192000:resampler=soxr:precision=33,alimiter=limit=0.891251:level=0:latency=1,"
            + "aresample=48000:resampler=soxr:precision=33:dither_method=triangular,ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I -f null - ");
        esit.Ses.OrnekHizi = null;
        Iceriyor("Eşitleme + örnekleme 'kaynakla aynı': 192 kHz'ten kaynağın hızına", Komut(esit, olcum), ",aresample=44100:resampler=soxr");
        Iceriyor("Eşitleme + örnekleme 'kaynakla aynı'", Komut(esit, olcum), " -ar 44100 ");
        Icermiyor("Eşitleme (sınırlayıcısız) + örnekleme 'kaynakla aynı': hız değişmez", Komut(esit, olcumAlcak), " -ar ");

        // Mono çıktı: kanal indirme ölçümden ve eşitlemeden önce (stereo ölçülüp mono'ya inilince sonuç 3–6 dB düşük kalırdı).
        var mono = Varsayilanlar.YouTube();
        mono.Ses.Kanal = KanalModu.Mono;
        Icermiyor("Mono, eşitleme kapalı: kanal indirme yine -ac 1 ile (eskisi gibi)", Komut(mono), "pan=mono");
        Iceriyor("Mono, eşitleme kapalı", Komut(mono), " -ac 1 ");
        mono.Ses.Esitleme = true;
        Iceriyor("Mono + eşitleme: önce mono'ya iner (yarım + yarım), sonra sabit kazanç", Komut(mono, olcum),
            " -af pan=mono|c0=0.5*c0+0.5*c1,volume=7.37dB,");
        Iceriyor("Mono + eşitleme: ölçüm de mono", " " + string.Join(' ', ArgsBuilder.OlcumKomutu(mono.Ses, "KAYNAK.mp4",
            ArgsBuilder.SesSec(mono.Ses, Kaynak48(), "KAYNAK.mp4"), 10)) + " ",
            " -af pan=mono|c0=0.5*c0+0.5*c1,ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I ");
        Icermiyor("Mono + eşitleme, ses dokunulmadı (ölçüm yok): indirme yine -ac 1 ile", Komut(mono), "pan=mono");

        var olc = ArgsBuilder.OlcumKomutu(esit.Ses, "KAYNAK.mp4", ArgsBuilder.SesSec(esit.Ses, Kaynak48(), "KAYNAK.mp4"), 10);
        Iceriyor("Ölçüm komutu (BS.1770 ölçeri, loudnorm değil)", " " + string.Join(' ', olc) + " ",
            " -map 0:a:0 -vn -sn -dn -t 10 -af ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I -f null - ");
        Icermiyor("Ölçüm komutunda loudnorm yok (sesin son ~2,9 sn'sini iki kez sayar)", " " + string.Join(' ', olc) + " ", "loudnorm");

        foreach (var (ad, a) in new[] { ("TikTok", Varsayilanlar.TikTok()), ("YouTube", Varsayilanlar.YouTube()) })
        {
            Kontrol(ad + ": eşitleme yönü", "YalnizcaYukselt", a.Ses.EsitlemeYonu.ToString());
        }

        // Karar (hedef -14, tavan -1): kısık ses her zaman bütünüyle aynı miktarda yükseltilir; tepe + kazanç tavanı
        // aşıyorsa tepeleri sınırlayıcı tutar. Ses aralığı (LRA) karara girmez.
        var ka = Varsayilanlar.YouTube().Ses;
        ka.Esitleme = true;
        string Karar(SesOlcum? o)
        {
            var r = Esitleme.KararVer(ka, o);
            return r.Karar + (r.Sinirli ? "/Sinirli" : string.Empty);
        }

        Kontrol("Kısık, tepe -12,1 (+11 dB → -1,1): sabit kazanç", "Dogrusal", Karar(new SesOlcum(-25, -12.1, 5)));
        Kontrol("Kısık, tepe -12 (+11 dB → -1; tepe 0,1 dB'ye yuvarlı, pay 0,05): sabit kazanç + sınırlayıcı", "Dogrusal/Sinirli", Karar(new SesOlcum(-25, -12, 5)));
        Kontrol("Kısık, tepe -11,9 (+11 dB → -0,9): sabit kazanç + sınırlayıcı", "Dogrusal/Sinirli", Karar(new SesOlcum(-25, -11.9, 5)));
        Kontrol("Kısık, ses aralığı 20,6 LU (kullanıcının OBS kaydı): sabit kazanç", "Dogrusal", Karar(new SesOlcum(-20.363, -11.2, 20.6)));
        Kontrol("Kısık, ses aralığı 0 (sabit ton): sabit kazanç", "Dogrusal", Karar(new SesOlcum(-25, -20, 0)));
        Kontrol("Hedefe 0,4 LU yakın: dokunulmaz", "Hedefte", Karar(new SesOlcum(-14.4, -3, 5)));
        Kontrol("Hedefe 0,6 LU uzak: doğrusal", "Dogrusal", Karar(new SesOlcum(-14.6, -3, 5)));
        Kontrol("Yüksek (-9), yalnızca yükselt: dokunulmaz", "ZatenYuksek", Karar(new SesOlcum(-9, -3, 5)));
        Kontrol("Ölçülemedi (sessiz)", "Olculemedi", Karar(null));
        ka.EsitlemeYonu = EsitlemeYonu.IkiYon;
        Kontrol("Yüksek (-9), iki yön: doğrusal (kısılır)", "Dogrusal", Karar(new SesOlcum(-9, -3, 5)));
        ka.EsitlemeYonu = EsitlemeYonu.YalnizcaYukselt;

        var rd = Esitleme.KararVer(ka, new SesOlcum(-25, -12.1, 5));
        Kontrol("Bilgi satırı (sabit kazanç)", "-25,0 → -14 LUFS (bütün ses +11,0 dB)", rd.BilgiMetni);
        Kontrol("Biten satır (sonuç -14,02)", "-14,0 LUFS", (rd with { SonucYukseklik = -14.02 }).SatirMetni ?? "yok");
        var rt = Esitleme.KararVer(ka, new SesOlcum(-25.52, -1.59, 2.8));
        Kontrol("Bilgi satırı (tepe aşıyor)", "-25,5 → -14 LUFS (bütün ses +11,5 dB, tepeler -1 dB'de tutulur)", rt.BilgiMetni);
        Kontrol("Bilgi satırı (telafi 2,3 dB)", "-25,5 → -14 LUFS (bütün ses +13,8 dB, tepeler -1 dB'de tutulur)", (rt with { Telafi = 2.3 }).BilgiMetni);
        Kontrol("Ayrıntı (telafi)", "True", (rt with { Telafi = 2.3 }).Ayrinti.Contains("kıstığı pay için ses +2,3 dB daha yükseltildi", StringComparison.Ordinal).ToString());
        Kontrol("Ayrıntı (telafi sınırı)", "True", (rt with { Telafi = 6, TelafiSiniri = true }).Ayrinti.Contains("(telafi en çok 6 dB)", StringComparison.Ordinal).ToString());
        Kontrol("Biten satır (sonuç -16,04)", "-16,0 LUFS (hedefe ulaşılamadı)", (rt with { SonucYukseklik = -16.04 }).SatirMetni ?? "yok");

        // Telafi adımı: deneme ölçümü hedefin altındaysa eksik kadar artar (en çok 6 dB), 0,2 LU içindeyse durur.
        string Adim(double telafi, double olculen)
        {
            var (yeni, sinir) = Esitleme.TelafiAdimi(telafi, olculen, -14);
            return (yeni?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "dur") + (sinir ? "/sinir" : string.Empty);
        }

        Kontrol("Telafi: -17,79 ölçüldü → 3,79", "3.79", Adim(0, -17.79));
        Kontrol("Telafi: 3,79 ile -14,25 → 4,04", "4.04", Adim(3.79, -14.25));
        Kontrol("Telafi: -14,15 (0,2 içinde) → dur", "dur", Adim(4.04, -14.15));
        Kontrol("Telafi: -24 ölçüldü → en çok 6", "6.00/sinir", Adim(0, -24));
        Kontrol("Telafi: sınırda, yine eksik → dur", "dur/sinir", Adim(6, -15));
        Kontrol("Telafi: hedefi geçti → azalır", "2.50", Adim(3, -13.5));

        // AAC çıktı: deneme ses AAC'ye çevrilip ölçülür (AAC tizleri keser, yükseklik biraz düşer); 0,02 LU içinde
        // durur, adım 0,01, AAC yüksekliği artırdıysa eksi olabilir. WAV'da (yalnızca sınırlayıcı) 0,2 LU.
        string AdimAac(double telafi, double olculen)
        {
            var (yeni, sinir) = Esitleme.TelafiAdimi(telafi, olculen, -14, kayipli: true);
            return (yeni?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "dur") + (sinir ? "/sinir" : string.Empty);
        }

        Kontrol("AAC telafi: -14,16 ölçüldü (kullanıcının OBS kaydı, 128 kb/s) → 0,16", "0.16", AdimAac(0, -14.16));
        Kontrol("AAC telafi: 0,16 ile -14,003 → dur", "dur", AdimAac(0.16, -14.003));
        Kontrol("AAC telafi: 0,16 ile -14,03 → 0,19", "0.19", AdimAac(0.16, -14.03));
        Kontrol("AAC telafi: AAC yüksekliği artırdı (-13,9) → -0,10", "-0.10", AdimAac(0, -13.9));
        Kontrol("AAC telafi: en çok 6 dB", "6.00/sinir", AdimAac(0, -24));
        Kontrol("Sınırlayıcı telafisi (WAV): -14,16 (0,2 içinde) → dur", "dur", Adim(0, -14.16));
        Kontrol("Kayıplı mı: TikTok sesi (WAV)", "False", Esitleme.KayipliMi(Varsayilanlar.TikTok().Ses).ToString());
        Kontrol("Kayıplı mı: YouTube sesi (FDK AAC)", "True", Esitleme.KayipliMi(Varsayilanlar.YouTube().Ses).ToString());
        Kontrol("Telafi tepeyi tavana çıkarırsa sınırlayıcı (tepe -12,1 + 11 + 0,2 > -1)", "True", (rd with { Telafi = 0.2 }).Sinirli.ToString());
        var rAac = Esitleme.KararVer(ka, new SesOlcum(-25, -20, 5)) with { Telafi = 0.16, Kayipli = true };
        Kontrol("Bilgi satırı (AAC telafisi 0,16; tepe -20 + 11,16: sınırlayıcı yok)", "-25,0 → -14 LUFS (bütün ses +11,2 dB)", rAac.BilgiMetni);
        Kontrol("Ayrıntı (AAC telafisi)", "True", rAac.Ayrinti
            .Contains("AAC kodlaması tizleri kestiği için yükseklik 0,2 LU düşüyordu: ses 0,2 dB daha yükseltildi", StringComparison.Ordinal).ToString());
        Kontrol("Ayrıntı (AAC telafisi tepeyi tavana çıkardı: -12,1 + 11,16)", "True", (rd with { Telafi = 0.16, Kayipli = true }).Ayrinti
            .Contains("Tepe -0,9 dB'ye çıkacaktı: sınırlayıcı -1 dB'de tuttu; kıstığı pay için ses +0,2 dB daha yükseltildi (AAC kodlamasının düşürdüğü pay da).",
                StringComparison.Ordinal).ToString());

        var ytAac = Varsayilanlar.YouTube();
        var ytAacSes = ArgsBuilder.SesSec(ytAac.Ses, Kaynak48(), "KAYNAK.mp4");
        var aacKomut = " " + string.Join(' ', ArgsBuilder.Olustur(ytAac, Kaynak48(), "KAYNAK.mp4", ytAacSes, "CIKTI.mov", "GECIS",
            new SesOlcum(-21.37, -10, 25), 0.16).Gecisler[0]) + " ";
        Iceriyor("YouTube (AAC) + eşitleme, AAC telafisi 0,16: kazanç 7,53 dB, sınırlayıcı yok", aacKomut,
            " -af volume=7.53dB,aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac ");
        Icermiyor("YouTube (AAC) + eşitleme, AAC telafisi: tepe aşmıyorsa sınırlayıcı yok", aacKomut, "alimiter");
        var aacDeneme = " " + string.Join(' ', ArgsBuilder.DenemeKodlamaKomutu(ytAac.Ses, "KAYNAK.mp4", ytAacSes, 10,
            new SesOlcum(-21.37, -10, 25), 0.16, "DENEME.m4a")) + " ";
        Iceriyor("AAC denemesi: kodlamadaki ses zinciri ve aynı FDK AAC ayarı, yalnızca ses", aacDeneme,
            " -map 0:a:0 -vn -sn -dn -t 10 -af volume=7.53dB,aresample=48000:resampler=soxr:precision=33:dither_method=triangular"
            + " -c:a libfdk_aac -profile:a aac_low -b:a 128k ");
        Iceriyor("AAC denemesi: geçici dosyaya", aacDeneme, " -f mp4 DENEME.m4a ");
        Icermiyor("AAC denemesi: görüntü yok", aacDeneme, " -c:v ");
        Kontrol("Biten satır (zaten yüksek)", "-9,2 LUFS (dokunulmadı)",
            Esitleme.KararVer(ka, new SesOlcum(-9.2, -3, 5)).SatirMetni ?? "yok");
        Iceriyor("Sonuç ölçüm komutu (ölçümle aynı ölçer)", " " + string.Join(' ', Esitleme.SonucOlcumKomutu("CIKTI.mov")) + " ",
            " -i CIKTI.mov -map 0:a:0 -vn -sn -dn -af ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I -f null - ");

        // Ölçerin gerçek çıktısı (FFmpeg ebur128 + ametadata): yükseklik son "lavfi.r128.I=" satırından (0,001),
        // gerçek tepe ve aralık en sondaki özetten.
        const string olcer = "[Parsed_ametadata_1 @ 0x1] frame:248  pts:1190400 pts_time:24.8\n"
            + "[Parsed_ametadata_1 @ 0x1] lavfi.r128.I=-20.401\n"
            + "[Parsed_ametadata_1 @ 0x1] frame:249  pts:1195200 pts_time:24.9\n"
            + "[Parsed_ametadata_1 @ 0x1] lavfi.r128.I=-20.363\n"
            + "[out#0/null @ 0x2] video:0kB audio:9375kB subtitle:0kB other streams:0kB global headers:0kB muxing overhead: unknown\n"
            + "size=N/A time=00:00:25.04 bitrate=N/A speed= 120x\n"
            + "[Parsed_ebur128_0 @ 0x3] Summary:\n\n  Integrated loudness:\n    I:         -20.4 LUFS\n    Threshold: -30.4 LUFS\n\n"
            + "  Loudness range:\n    LRA:        20.6 LU\n    Threshold: -40.4 LUFS\n    LRA low:   -41.2 LUFS\n    LRA high:  -20.6 LUFS\n\n"
            + "  True peak:\n    Peak:      -11.2 dBFS";
        var oku = ArgsBuilder.OlcumOku(olcer);
        Kontrol("Ölçüm okunur (yükseklik 0,001, tepe ve aralık özetten)", "-20.363/-11.2/20.6",
            oku is null ? "yok" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{oku.Yukseklik}/{oku.Tepe}/{oku.Aralik}"));
        Kontrol("Sessiz ses (-70, tepe -inf): eşitleme yapılmaz", "yok",
            ArgsBuilder.OlcumOku(olcer.Replace("I=-20.363", "I=-70.000").Replace("-11.2 dBFS", " -inf dBFS")) is null ? "yok" : "var");
        Kontrol("0,4 sn'den kısa ses (ölçer -70 yazar): eşitleme yapılmaz", "yok",
            ArgsBuilder.OlcumOku(olcer.Replace("I=-20.401", "I=-70.000").Replace("I=-20.363", "I=-70.000")) is null ? "yok" : "var");
        Kontrol("Özet yoksa (ölçüm yarıda kaldı) okunmaz", "yok",
            ArgsBuilder.OlcumOku(olcer[..olcer.IndexOf("[Parsed_ebur128_0", StringComparison.Ordinal)]) is null ? "yok" : "var");
        Kontrol("Yükseklik satırı yoksa okunmaz", "yok", ArgsBuilder.OlcumOku(olcer.Replace("lavfi.r128.I=", "lavfi.r128.M=")) is null ? "yok" : "var");
    }

    private static void Boyutlar()
    {
        Console.WriteLine();
        Console.WriteLine("--- Tahmini dosya boyutu: (video + ses bit hızı) × süre");
        var m = new MediaInfo(1080, 1920, 1, 1, 0, 60, true, 30, "bt709", "bt709", "tv")
        {
            SesAkislari = [new SesAkisi(1, "aac", 44100, 2, null, 128)],
        };
        var tt = Varsayilanlar.TikTok();
        var ses = ArgsBuilder.SesSec(tt.Ses, m, "YOK.mp4");
        // (34375 + 48000 × 16 × 2 / 1000) kb/s × 60 s = 269 332 500 bayt = 256,9 MB
        Kontrol("TikTok 60 sn (CBR 34375 + WAV 16 bit 48 kHz stereo)", "tahmini 257 MB", BoyutTahmini.Hesapla(tt, m, ses)?.Metin ?? "yok");
        var yt = Varsayilanlar.YouTube();
        // (34375 + 128 FDK AAC) × 60 = 2 070 180 kbit + tampon payı: 55000 kbit × 0,9 (x264 vbv-init varsayılanı)
        // = 49 500 kbit → 2 119 680 kbit = 264 960 000 bayt = 252,7 MB
        Kontrol("YouTube 60 sn (CRF 18, en yüksek 34375, tampon 55000, AAC 128)", "en çok 253 MB", BoyutTahmini.Hesapla(yt, m, ses)?.Metin ?? "yok");
        var kisa = m with { DurationSeconds = 5 };
        // 5 sn: (34375 + 128) × 5 + 49500 = 222 015 kbit = 27 751 875 bayt = 26,5 MB
        Kontrol("YouTube 5 sn: tampon payı kısa videoda büyük", "en çok 26,5 MB", BoyutTahmini.Hesapla(yt, kisa, ses)?.Metin ?? "yok");
        yt.Video.X264.VbvMaks = null;
        Kontrol("CRF, üst sınır yok: tahmin yapılmaz", "yok", BoyutTahmini.Hesapla(yt, m, ses)?.Metin ?? "yok");
        var cq = Varsayilanlar.TikTok();
        cq.Video.Nvenc.Mod = NvencModu.SabitKalite;
        cq.Video.Nvenc.MaksBitHizi = 30000;
        cq.Video.Nvenc.Tampon = 40000;
        // NVENC sabit kalite: tampon yok sayılır, kart seviyenin tamponunu kullanır. Kaynak H.264 değil → Main,
        // seviye otomatik → yeten en düşük: 30000 kb/s 4.0'ı (20000) aşar → 4.1, tamponu 62500 kbit.
        // (30000 + 1536) × 60 + 62500 = 1 954 660 kbit = 244 332 500 bayt = 233,0 MB
        Kontrol("NVENC sabit kalite, en yüksek 30000 (tampon 40000 yok sayılır)", "en çok 233 MB", BoyutTahmini.Hesapla(cq, m, ses)?.Metin ?? "yok");
        cq.Video.Nvenc.MaksBitHizi = null;
        Kontrol("NVENC sabit kalite, üst sınır yok: tahmin yapılmaz", "yok", BoyutTahmini.Hesapla(cq, m, ses)?.Metin ?? "yok");
        var sessiz = Varsayilanlar.TikTok();
        sessiz.Ses.Kaynak = SesKaynagi.Sessiz;
        Kontrol("Sessiz (yalnızca video)", "tahmini 246 MB",
            BoyutTahmini.Hesapla(sessiz, m, ArgsBuilder.SesSec(sessiz.Ses, m, "YOK.mp4"))?.Metin ?? "yok");
        Kontrol("Biçim: 1,5 GB", "1,5 GB", BoyutTahmini.Yaz(1610612736));
        Kontrol("Biçim: 12,3 MB", "12,3 MB", BoyutTahmini.Yaz(12897484));
        Kontrol("Biçim: 850 KB", "850 KB", BoyutTahmini.Yaz(870400));
    }

    private static void SesIzleri()
    {
        Console.WriteLine();
        Console.WriteLine("--- Videoda birden fazla ses izi: WAV (PCM) / kayıpsız iz varsa o, yoksa ilki");
        static MediaInfo Izli(params string[] codecler) => new(1920, 1080, 1, 1, 0, 10, true, 30, "bt709", "bt709", "tv")
        {
            VideoCodec = "h264",
            VideoProfil = "High",
            VideoSeviye = 40,
            SesAkislari = codecler.Select((c, i) => new SesAkisi(i + 1, c, 48000, 2, c.StartsWith("pcm_") ? 24 : null, null)).ToList(),
        };

        void Dene(string ad, MediaInfo m, string map, string? aciklamaParca)
        {
            var a = Varsayilanlar.YouTube();
            var ses = ArgsBuilder.SesSec(a.Ses, m, "KAYNAK.mp4");
            var plan = ArgsBuilder.Olustur(a, m, "KAYNAK.mp4", ses, "CIKTI.mov", "GECIS");
            var k = " " + string.Join(' ', plan.Gecisler[0]) + " ";
            Iceriyor(ad, k, $" -map {map} ");
            if (aciklamaParca is not null)
            {
                Kontrol(ad + ": bilgi satırı", "var", ses.Aciklama.Contains(aciklamaParca, StringComparison.Ordinal) ? "var" : ses.Aciklama);
            }
        }

        Dene("AAC + WAV (PCM)", Izli("aac", "pcm_s24le"), "0:a:1", "2. iz · PCM · 24 bit");
        Dene("WAV (PCM) + AAC", Izli("pcm_s16le", "aac"), "0:a:0", "1. iz · PCM");
        Dene("AAC + FLAC", Izli("aac", "flac"), "0:a:1", "2. iz");
        Dene("AAC + AAC (ilki)", Izli("aac", "aac"), "0:a:0", null);
        Dene("Tek AAC", Izli("aac"), "0:a:0", null);
        var tek = ArgsBuilder.SesSec(Varsayilanlar.YouTube().Ses, Izli("aac"), "KAYNAK.mp4");
        Kontrol("Tek iz: bilgi satırı", "videonun sesi", tek.Aciklama);
    }

    private static void Kaplar()
    {
        Console.WriteLine();
        Console.WriteLine("--- Dosya biçimi: TikTok ve YouTube MOV, dikey 9:16 MP4, WAV ses her zaman MOV; kodlama tarihi");
        Kontrol("TikTok varsayılanı", ".mov", Varsayilanlar.TikTok().Uzanti);
        Kontrol("YouTube varsayılanı", ".mov", Varsayilanlar.YouTube().Uzanti);
        Kontrol("Dikey 9:16 ön ayarı", ".mp4", Varsayilanlar.Dikey916().Uzanti);
        var wav = Varsayilanlar.Dikey916();
        wav.Ses.Kodlayici = SesKodlayici.Wav;
        Kontrol("MP4 + WAV ses", ".mov", wav.Uzanti);
        Kontrol("MP4 + WAV ses özeti", "MOV (WAV ses MP4'e girmez)", wav.KapOzeti);

        // Encoded date / Tagged date: son geçişin komutunda kodlama anı; 2 geçişte ilk geçiş (çıktısız) yazmaz.
        var m = Kaynak(1920, 1080, 30, "h264", "High", 40);
        Iceriyor("TikTok: kodlama tarihi", NvencKomutu(m), " -metadata creation_time=now -movflags +faststart ");
        Iceriyor("YouTube: kodlama tarihi", KomutMetni(m, Varsayilanlar.YouTube()), " -metadata creation_time=now -movflags +faststart ");
        Iceriyor("Dikey 9:16 (MP4): kodlama tarihi", KomutMetni(m, Varsayilanlar.Dikey916()), " -metadata creation_time=now ");
        var iki = Varsayilanlar.YouTube();
        iki.Video.X264.Mod = X264Modu.IkiGecis;
        var plan = ArgsBuilder.Olustur(iki, m, "KAYNAK.mp4", SesSecimi.Yok, "CIKTI.mov", "GECIS");
        Kontrol("2 geçiş: geçiş sayısı", "2", plan.Gecisler.Count.ToString());
        Icermiyor("2 geçiş: ilk geçiş (çıktısız)", " " + string.Join(' ', plan.Gecisler[0]) + " ", "creation_time");
        Iceriyor("2 geçiş: son geçiş", " " + string.Join(' ', plan.Gecisler[^1]) + " ", " -metadata creation_time=now ");
        Iceriyor("Kaynağın etiketleri taşınmaz", KomutMetni(m, Varsayilanlar.YouTube()), " -map_metadata -1 ");
    }

    private static void AyarGecisi()
    {
        Console.WriteLine();
        Console.WriteLine("--- Kayıtlı ayarların yeni sürüme geçişi (eski ayar dosyası)");
        // 0.8.8'e kadar TikTok ve YouTube sesi: FDK AAC 128 kb/s 48 kHz, yüksek kalite (soxr 20 bit), titreşim yok.
        static void EskiSes(SesAyarlari s)
        {
            s.Kodlayici = SesKodlayici.FdkAac;
            s.WavBit = WavBitDerinligi.Bit24;
            s.Kalite = DonusumKalitesi.Yuksek;
            s.Titresim = Titresim.Yok;
        }

        static string SesOzeti(SesAyarlari s) => $"{s.Kodlayici} {s.WavBit} {s.OrnekHizi} {s.Kalite} {s.Titresim}";
        static string FdkOzeti(FdkAyarlari f) => $"{f.Profil} {f.Mod} {f.BitHizi} {f.Afterburner}";
        const string YeniSes = "Wav Bit16 48000 EnYuksek Ucgen";
        const string YouTubeSes = "FdkAac Bit16 48000 EnYuksek Ucgen";

        var eskiYt = Varsayilanlar.YouTube();
        eskiYt.Surum = 4;
        EskiSes(eskiYt.Ses);
        eskiYt.Video.Kap = KapBicimi.Mp4;
        eskiYt.AnahtarKare.Birim = AralikBirimi.Kare;
        eskiYt.Video.X264.Crf = 20; // kullanıcının kendi değişikliği korunmalı
        eskiYt.Video.X264.VbvMaks = 14000; // 0.8.6'ya kadar YouTube VBV'si 14000 / 28000
        eskiYt.Video.X264.VbvTampon = 28000;
        var eskiTt = Varsayilanlar.TikTok();
        eskiTt.Surum = 5;
        eskiTt.Video.Kap = KapBicimi.Mp4;
        EskiSes(eskiTt.Ses);
        var ayarlar = new AppSettings();
        ayarlar.Profiller[ExportProfileId.YouTube] = eskiYt;
        ayarlar.Profiller[ExportProfileId.TikTok] = eskiTt;

        var yt = ayarlar.ProfilAyari(ExportProfileId.YouTube);
        Kontrol("YouTube 4 → 9: dosya biçimi", "Mov", yt.Video.Kap.ToString());
        Kontrol("YouTube 4 → 9: anahtar kare birimi", "Kare30", yt.AnahtarKare.Birim.ToString());
        Kontrol("YouTube 4 → 9: kendi CRF değeri korunur", "20", yt.Video.X264.Crf.ToString());
        Kontrol("YouTube 4 → 9: VBV 34375 / 55000", "34375/55000", $"{yt.Video.X264.VbvMaks}/{yt.Video.X264.VbvTampon}");
        Kontrol("YouTube sürüm", ProfilAyarlari.GuncelSurum.ToString(), yt.Surum.ToString());
        var tt = ayarlar.ProfilAyari(ExportProfileId.TikTok);
        Kontrol("TikTok 5 → 9: dosya biçimi", "Mov", tt.Video.Kap.ToString());
        Kontrol("TikTok 5 → 9: NVENC kalır", "Nvenc", tt.Video.Kodlayici.ToString());
        Kontrol("TikTok 5 → 9: tampon boş (bit hızı kadar)", "boş", tt.Video.Nvenc.Tampon?.ToString() ?? "boş");
        Kontrol("TikTok 5 → 11: ses WAV 16 bit, en yüksek kalite, üçgen titreşim", YeniSes, SesOzeti(tt.Ses));
        Kontrol("YouTube 4 → 11: ses FDK AAC 128 kb/s, en yüksek kalite, üçgen titreşim", YouTubeSes + " 128",
            SesOzeti(yt.Ses) + " " + yt.Ses.Fdk.BitHizi);
        Kontrol("TikTok varsayılanı: ses", YeniSes, SesOzeti(Varsayilanlar.TikTok().Ses));
        Kontrol("YouTube varsayılanı: ses FDK AAC 128 kb/s CBR, afterburner açık", YouTubeSes + " aac_low Cbr 128 True",
            SesOzeti(Varsayilanlar.YouTube().Ses) + " " + FdkOzeti(Varsayilanlar.YouTube().Ses.Fdk));

        // Sürüm 11: YouTube sesi FDK AAC 128 kb/s. 0.8.9'un YouTube sesi (WAV 16 bit) yeni varsayılana geçer; LUFS
        // eşitleme seçimi kalır; kullanıcının değiştirdiği ses, TikTok ve ön ayar olduğu gibi kalır.
        var yt10 = Varsayilanlar.YouTube();
        yt10.Surum = 10;
        Varsayilanlar.PlatformSesi(yt10.Ses);
        yt10.Ses.Esitleme = false;
        var ayar10 = new AppSettings();
        ayar10.Profiller[ExportProfileId.YouTube] = yt10;
        var ses10 = ayar10.ProfilAyari(ExportProfileId.YouTube).Ses;
        Kontrol("YouTube 10 → 11: 0.8.9'un sesi (WAV 16 bit) FDK AAC 128 kb/s olur", YouTubeSes + " aac_low Cbr 128 True",
            SesOzeti(ses10) + " " + FdkOzeti(ses10.Fdk));
        Kontrol("YouTube 10 → 11: kapatılmış LUFS eşitleme kapalı kalır", "False", ses10.Esitleme.ToString());
        var yt10Kendi = Varsayilanlar.YouTube();
        yt10Kendi.Surum = 10;
        Varsayilanlar.PlatformSesi(yt10Kendi.Ses);
        yt10Kendi.Ses.WavBit = WavBitDerinligi.Bit24;
        var ayar10Kendi = new AppSettings();
        ayar10Kendi.Profiller[ExportProfileId.YouTube] = yt10Kendi;
        Kontrol("YouTube 10 → 11: kendi ses ayarı (WAV 24 bit) korunur", "Wav Bit24 48000 EnYuksek Ucgen",
            SesOzeti(ayar10Kendi.ProfilAyari(ExportProfileId.YouTube).Ses));
        var tt10 = Varsayilanlar.TikTok();
        tt10.Surum = 10;
        var ayarTt10 = new AppSettings();
        ayarTt10.Profiller[ExportProfileId.TikTok] = tt10;
        Kontrol("TikTok 10 → 11: ses WAV 16 bit kalır", YeniSes, SesOzeti(ayarTt10.ProfilAyari(ExportProfileId.TikTok).Ses));
        var onYt10 = Varsayilanlar.YouTube();
        onYt10.Surum = 10;
        Varsayilanlar.PlatformSesi(onYt10.Ses);
        Kontrol("YouTube ön ayarı (sürüm 10, WAV 16 bit) kaydedildiği gibi kalır", YeniSes,
            SesOzeti(new AppSettings().OnAyariYukle(ExportProfileId.YouTube, onYt10).Ses));

        // 0.8.6'nın (sürüm 7) kendiliğinden yazdığı 55000 kb geri alınır; kullanıcının kendi VBV'si ve
        // yeni sürümde elle girilen tampon korunur.
        var tt7 = Varsayilanlar.TikTok();
        tt7.Surum = 7;
        tt7.Video.Nvenc.Tampon = 55000;
        var yt7 = Varsayilanlar.YouTube();
        yt7.Surum = 7;
        yt7.Video.X264.VbvMaks = 20000;
        yt7.Video.X264.VbvTampon = 40000;
        var ayarlar7 = new AppSettings();
        ayarlar7.Profiller[ExportProfileId.TikTok] = tt7;
        ayarlar7.Profiller[ExportProfileId.YouTube] = yt7;
        Kontrol("TikTok 7 → 9: 0.8.6'nın tamponu geri alınır", "boş", ayarlar7.ProfilAyari(ExportProfileId.TikTok).Video.Nvenc.Tampon?.ToString() ?? "boş");
        var yt8 = ayarlar7.ProfilAyari(ExportProfileId.YouTube).Video.X264;
        Kontrol("YouTube 7 → 9: kendi VBV'si korunur", "20000/40000", $"{yt8.VbvMaks}/{yt8.VbvTampon}");

        // Kullanıcının kendi ses ayarı (ör. FDK AAC 256 kb/s) yeni sürümde de korunur.
        var ytKendi = Varsayilanlar.YouTube();
        ytKendi.Surum = 8;
        EskiSes(ytKendi.Ses);
        ytKendi.Ses.Fdk.BitHizi = 256;
        var ayarlarKendi = new AppSettings();
        ayarlarKendi.Profiller[ExportProfileId.YouTube] = ytKendi;
        var sesKendi = ayarlarKendi.ProfilAyari(ExportProfileId.YouTube).Ses;
        Kontrol("YouTube 8 → 9: kendi ses ayarı (FDK AAC 256) korunur", "FdkAac 256", $"{sesKendi.Kodlayici} {sesKendi.Fdk.BitHizi}");
        var ttKendi = Varsayilanlar.TikTok();
        ttKendi.Surum = 8;
        EskiSes(ttKendi.Ses);
        ttKendi.Ses.Fdk.Afterburner = false;
        var ayarlarKendi2 = new AppSettings();
        ayarlarKendi2.Profiller[ExportProfileId.TikTok] = ttKendi;
        var sesKendi2 = ayarlarKendi2.ProfilAyari(ExportProfileId.TikTok).Ses;
        Kontrol("TikTok 8 → 9: FDK ince ayarı değişmişse (afterburner kapalı) korunur", "FdkAac False Yuksek Yok",
            $"{sesKendi2.Kodlayici} {sesKendi2.Fdk.Afterburner} {sesKendi2.Kalite} {sesKendi2.Titresim}");

        // 0.8.8'de kaydedilmiş ön ayar (ses FDK AAC 128) yüklenince sesi kaydedildiği gibi kalır; yeniden açılınca da.
        var onAyar = Varsayilanlar.YouTube();
        onAyar.Surum = 8;
        EskiSes(onAyar.Ses);
        var ayarlarOn = new AppSettings();
        var yuklenen = ayarlarOn.OnAyariYukle(ExportProfileId.YouTube, onAyar);
        Kontrol("Ön ayar (sürüm 8) yüklenince sesi korunur", "FdkAac 128 Yuksek Yok " + ProfilAyarlari.GuncelSurum,
            $"{yuklenen.Ses.Kodlayici} {yuklenen.Ses.Fdk.BitHizi} {yuklenen.Ses.Kalite} {yuklenen.Ses.Titresim} {yuklenen.Surum}");
        var sonra = ayarlarOn.ProfilAyari(ExportProfileId.YouTube).Ses;
        Kontrol("Ön ayardan sonra yeniden açılınca da korunur", "FdkAac 128", $"{sonra.Kodlayici} {sonra.Fdk.BitHizi}");

        // Ön ayarın diğer eski varsayılanları yine güncellenir (ör. 0.8.6'ya kadar YouTube VBV 14000 / 28000).
        var onAyar7 = Varsayilanlar.YouTube();
        onAyar7.Surum = 7;
        EskiSes(onAyar7.Ses);
        onAyar7.Video.X264.VbvMaks = 14000;
        onAyar7.Video.X264.VbvTampon = 28000;
        var yuklenen7 = new AppSettings().OnAyariYukle(ExportProfileId.YouTube, onAyar7);
        Kontrol("Ön ayar (sürüm 7): VBV 34375 / 55000 olur, ses FDK AAC kalır", "34375/55000 FdkAac",
            $"{yuklenen7.Video.X264.VbvMaks}/{yuklenen7.Video.X264.VbvTampon} {yuklenen7.Ses.Kodlayici}");

        // Sürüm 10: LUFS eşitleme varsayılan açık. Kayıtlı profil ayarında (sürüm 9, kapalı) açılır; ön ayar
        // kaydedildiği gibi kalır; eşitlemeden önceki ön ayarda (alan yok = kapalı) eşitleme yapılmaz.
        Kontrol("Ayar sınıfının kendi başlangıcı (eski ön ayarda alan yok)", "False", new SesAyarlari().Esitleme.ToString());
        Kontrol("Dikey 9:16 (GitHub hedef ayarı) eşitlemesiz", "False", Varsayilanlar.Dikey916().Ses.Esitleme.ToString());
        foreach (var pid in new[] { ExportProfileId.TikTok, ExportProfileId.YouTube })
        {
            var eski9 = Varsayilanlar.For(pid);
            eski9.Surum = 9;
            eski9.Ses.Esitleme = false;
            var ayar9 = new AppSettings();
            ayar9.Profiller[pid] = eski9;
            Kontrol(pid + " 9 → 10: LUFS eşitleme açılır", "True", ayar9.ProfilAyari(pid).Ses.Esitleme.ToString());
            var on9 = Varsayilanlar.For(pid);
            on9.Surum = 9;
            on9.Ses.Esitleme = false;
            Kontrol(pid + " ön ayar (sürüm 9, eşitleme kapalı) kaydedildiği gibi", "False",
                new AppSettings().OnAyariYukle(pid, on9).Ses.Esitleme.ToString());
            var on8 = Varsayilanlar.For(pid);
            on8.Surum = 8;
            on8.Ses.Esitleme = false;
            Kontrol(pid + " ön ayar (sürüm 8, eşitleme yoktu) eşitlemesiz", "False",
                new AppSettings().OnAyariYukle(pid, on8).Ses.Esitleme.ToString());
        }

        var tt8 = Varsayilanlar.TikTok();
        tt8.Video.Nvenc.Tampon = 55000;
        var ayarlar8 = new AppSettings();
        ayarlar8.Profiller[ExportProfileId.TikTok] = tt8;
        Kontrol("TikTok 8: elle girilen 55000 korunur", "55000", ayarlar8.ProfilAyari(ExportProfileId.TikTok).Video.Nvenc.Tampon?.ToString() ?? "boş");
    }

    private static string KomutMetni(MediaInfo m, ProfilAyarlari a)
    {
        var plan = ArgsBuilder.Olustur(a, m, "KAYNAK.mp4", SesSecimi.Yok, "CIKTI.mp4", "GECIS");
        return " " + string.Join(' ', plan.Gecisler[0]) + " ";
    }

    /// <summary>
    /// FDK AAC'de FFmpeg'in kabul ettiği sınırlar (kesim frekansı, DRC / sıkıştırma hedefi, program seviyesi; dışı
    /// kodlayıcıyı açmaz) ve panelde "Özel boyut" seçimi (liste başka boyuta atlamaz).
    /// </summary>
    private static void FdkSinirlari()
    {
        Console.WriteLine();
        Console.WriteLine("--- FDK AAC sınırları ve \"Özel boyut\" seçimi");
        static MediaInfo Kaynak(int hz) => new(1920, 1080, 1, 1, 0, 10, true, 30, "bt709", "bt709", "tv")
        {
            VideoCodec = "h264",
            VideoProfil = "High",
            VideoSeviye = 40,
            SesAkislari = [new SesAkisi(1, "aac", hz, 2, null, null)],
        };

        static string Komut(ProfilAyarlari a, int hz)
        {
            var m = Kaynak(hz);
            var ses = ArgsBuilder.SesSec(a.Ses, m, "KAYNAK.mp4");
            var plan = ArgsBuilder.Olustur(a, m, "KAYNAK.mp4", ses, "CIKTI.mov", "GECIS");
            return " " + string.Join(' ', plan.Gecisler[0]) + " ";
        }

        var f = new FdkAyarlari { Kesim = 99999, DrcHedef = 5, SikistirmaHedef = -50, ProgramSeviyesi = -10 };
        Kontrol("Kesim en çok 20000 Hz", "20000", f.Kesim.ToString());
        f.Kesim = -5;
        Kontrol("Kesim eksi → 0 (otomatik)", "0", f.Kesim.ToString());
        Kontrol("DRC hedefi en çok 0 dB", "0", f.DrcHedef?.ToString() ?? "boş");
        Kontrol("Sıkıştırma hedefi en az -31 dB", "-31", f.SikistirmaHedef?.ToString() ?? "boş");
        Kontrol("Program seviyesi aralıktaysa olduğu gibi", "-10", f.ProgramSeviyesi?.ToString() ?? "boş");
        f.DrcHedef = null;
        Kontrol("DRC hedefi boş (kullanılmaz)", "boş", f.DrcHedef?.ToString() ?? "boş");
        var y = Varsayilanlar.YouTube();
        y.Ses.Fdk.Kesim = 100;
        Iceriyor("Kesim 100 Hz, 48 kHz: FFmpeg'in en azına (188) yükseltilir", Komut(y, 48000), " -cutoff 188 ");
        y.Ses.OrnekHizi = 44100;
        Iceriyor("Kesim 100 Hz, 44,1 kHz: 173", Komut(y, 48000), " -cutoff 173 ");
        y.Ses.OrnekHizi = null;
        Iceriyor("Kesim 100 Hz, örnekleme kaynakla aynı (96 kHz): 375", Komut(y, 96000), " -cutoff 375 ");
        y.Ses.OrnekHizi = 48000;
        y.Ses.Fdk.Kesim = 16000;
        Iceriyor("Kesim 16000 Hz olduğu gibi", Komut(y, 48000), " -cutoff 16000 ");
        y.Ses.Fdk.Kesim = 0;
        Icermiyor("Kesim 0: otomatik (-cutoff yok)", Komut(y, 48000), " -cutoff ");

        var v = new VideoAyarlari { Cozunurluk = CozunurlukModu.Kaynak };
        v.BoyutSecimi = "ozel";
        Kontrol("Kaynakla aynı → Özel boyut: liste Özel'de kalır", "ozel Hedef", $"{v.BoyutSecimi} {v.Cozunurluk}");
        v.BoyutSecimi = "1080x1920";
        Kontrol("Özel → 1080×1920", "1080x1920", v.BoyutSecimi);
        v.BoyutSecimi = "ozel";
        Kontrol("1080×1920 → Özel boyut: liste Özel'de, kutularda 1080×1920 kalır", "ozel 1080x1920", $"{v.BoyutSecimi} {v.Genislik}x{v.Yukseklik}");
        v.Genislik = 1000;
        Kontrol("Özel boyutta kutuya yazılan genişlik", "ozel 1000", $"{v.BoyutSecimi} {v.Genislik}");
        v.BoyutSecimi = "kaynak";
        Kontrol("Özel → Kaynakla aynı", "kaynak Kaynak", $"{v.BoyutSecimi} {v.Cozunurluk}");
        var kayit = new ProfilAyarlari();
        kayit.Video.BoyutSecimi = "ozel";
        kayit.Video.Genislik = 1080;
        kayit.Video.Yukseklik = 1920;
        Kontrol("Özel seçimi ayar dosyasına yazılmaz (açılışta listedeki boyut görünür)", "1080x1920", AyarDosyasi.Kopyala(kayit).Video.BoyutSecimi);
    }

    /// <summary>Satırda görünen hata yazıları Türkçe ve anlaşılır (okunamayan video, kayıt yeri, disk).</summary>
    private static void HataYazilari()
    {
        Console.WriteLine("--- Hata yazıları (okunamayan video, kayıt yeri, disk dolu)");
        static string Ilk(string s) => s.Split('\n', 2)[0].Trim();
        var bozuk = MediaProbe.OkumaHatasi("[mov,mp4,m4a,3gp,3g2,mj2 @ 000001a17943f340] moov atom not found\nx.mp4: Invalid data found when processing input\n", 1);
        Kontrol("Bozuk video: Türkçe açıklama", "Video dosyası okunamadı: bozuk, yarım kopyalanmış ya da desteklenmeyen bir dosya.", Ilk(bozuk.Message));
        Kontrol("Bozuk video: ffprobe'un satırları ayrıntıda", "True", bozuk.Message.Contains("moov atom not found", StringComparison.Ordinal).ToString());
        Kontrol("Bozuk video: günlüğe tam çıktı", "True", bozuk.Gunluk.Contains("Invalid data found", StringComparison.Ordinal).ToString());
        var yok = MediaProbe.OkumaHatasi("C:\\yok.mp4: No such file or directory", 1);
        Kontrol("Silinmiş video: Türkçe açıklama", "Dosya ya da klasör bulunamadı: silinmiş, taşınmış ya da adı değişmiş olabilir.", Ilk(yok.Message));
        Kontrol("Bilinmeyen okuma hatası", "Video dosyası okunamadı (kod 5).", Ilk(MediaProbe.OkumaHatasi("garip bir hata", 5).Message));
        Kontrol("Yazma izni yok", "Kayıt yerine yazılamadı: yazma izni yok. Başka bir kayıt yeri seçin.",
            Ilk(HataAciklamasi.Metin(new UnauthorizedAccessException("Access to the path 'C:\\x' is denied."))));
        Kontrol("Kayıt yeri yok", "Kayıt yeri bulunamadı: klasör ya da sürücü yok (disk çıkarılmış olabilir). Başka bir kayıt yeri seçin.",
            Ilk(HataAciklamasi.Metin(new DirectoryNotFoundException("Could not find a part of the path 'Q:\\x'."))));
        Kontrol("Disk dolu (ERROR_DISK_FULL)", "Diskte yer kalmadı. Kayıt yerinde yer açın ya da başka bir kayıt yeri seçin.",
            Ilk(HataAciklamasi.Metin(new IOException("Diskte yeterli alan yok.", unchecked((int)0x80070070)))));
        Kontrol("Başka dosya hatası: ileti olduğu gibi", "kilitli", HataAciklamasi.Metin(new IOException("kilitli")));
        Kontrol("ffmpeg hatası: açıklaması olduğu gibi", "NVENC kullanılamadı", HataAciklamasi.Metin(new FfmpegHatasi("NVENC kullanılamadı", "günlük")));
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
