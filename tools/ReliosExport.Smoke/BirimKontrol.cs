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
        HariciSes();
        SesYuksekligi();
        TepeKorumasi();
        Boyutlar();
        Kaplar();
        AyarGecisi();
        Klasorler();
        HataYazilari();
        FdkSinirlari();
        FdkUyumuKontrol();
        X264Profilleri();
        SesGecikmesi();
        Calinabilirlik();
        Sinirlar013();
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
        var a = temel ?? Varsayilanlar.TikTokNvenc();
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
        Console.WriteLine("--- TikTok NVENC ayarı (0.10.0'a kadar varsayılan: CBR 34375 kb/s) komutları");

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
        Iceriyor("TikTok NVENC ayarı: tampon boş, bit hızı kadar", k, " -b:v 34375k -bufsize 34375k ");

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
        Console.WriteLine("--- NVENC kayıpsız (tune lossless): sabit QP 0; kodlama modu, bit hızı, tampon, AQ ve profil verilmez");
        static void Kayipsiz(ProfilAyarlari a) => a.Video.Nvenc.Ayar = "lossless";

        // TikTok NVENC ayarında AQ açık: kart "Adaptive quantization is not supported with lossless encoding" verip açılmıyordu.
        k = NvencKomutu(hd, Kayipsiz);
        Iceriyor("Kayıpsız (TikTok NVENC ayarı)", k,
            " -preset p7 -tune lossless -level:v 4.0 -rc constqp -qp 0 -b:v 0 -multipass fullres -rc-lookahead 32 -spatial-aq 0 -temporal-aq 0 -bf 0 -refs 3 -dpb_size 3 -coder cabac ");
        Icermiyor("Kayıpsız", k, " -profile:v ");
        Icermiyor("Kayıpsız", k, " -aq-strength ");
        Icermiyor("Kayıpsız", k, " -bufsize ");
        Iceriyor("Kayıpsız (4:2:0)", k, ",format=yuv420p,");
        k = NvencKomutu(hd, a =>
        {
            Kayipsiz(a);
            a.Video.Nvenc.Mod = NvencModu.DegiskenBitHizi;
            a.Video.Nvenc.MaksBitHizi = 30000;
            a.Video.Nvenc.Tampon = 40000;
        });
        Iceriyor("Kayıpsız + VBR 30000 / 40000 seçili kalsa da", k, " -rc constqp -qp 0 -b:v 0 -multipass ");
        Icermiyor("Kayıpsız + VBR", k, " -maxrate ");
        Icermiyor("Kayıpsız + VBR", k, " -bufsize ");
        k = NvencKomutu(hd, a =>
        {
            Kayipsiz(a);
            a.Video.Nvenc.Mod = NvencModu.SabitKalite;
            a.Video.Nvenc.Cq = 30;
        });
        Icermiyor("Kayıpsız + sabit kalite 30 seçili kalsa da", k, " -cq ");
        k = NvencKomutu(hd, a =>
        {
            Kayipsiz(a);
            a.Video.Nvenc.Profil = "baseline";
            a.Video.Nvenc.BKare = 2;
        });
        Iceriyor("Kayıpsız + Baseline seçili (dosya High 4:4:4 Predictive)", k, " -bf 2 -b_ref_mode middle ");
        Iceriyor("Kayıpsız + Baseline seçili", k, " -coder cabac ");
        Icermiyor("Kayıpsız + Baseline seçili", k, " -profile:v ");
        k = NvencKomutu(hd, a =>
        {
            Kayipsiz(a);
            a.Video.Nvenc.Profil = "high444p";
        });
        Iceriyor("Kayıpsız + High 4:4:4", k, ",format=yuv444p,");
        Iceriyor("Kayıpsız + High 4:4:4", k, " -coder cavlc ");
        Icermiyor("Kayıpsız + High 4:4:4", k, " -profile:v ");

        // Seviye yalnızca boyuta, kare hızına ve DPB'ye göre (bit hızı sınırı yoktur): 1080×1920@60, kaynak 4.0 → 4.2.
        k = NvencKomutu(Kaynak(1080, 1920, 60, "h264", "High", 40), Kayipsiz);
        Iceriyor("Kayıpsız 1080×1920@60, kaynak High@4.0", k, " -level:v 4.2 ");

        var kayipsiz = Varsayilanlar.TikTokNvenc();
        Kayipsiz(kayipsiz);
        Kontrol("Kayıpsız: kodlama özeti", "Kayıpsız", kayipsiz.Video.Nvenc.ModOzeti);
        Kontrol("Kayıpsız: özet", "NVENC · Kayıpsız · p7 · High 4:4:4 (seviye kaynakla aynı)", kayipsiz.Video.Nvenc.Ozet);
        Kontrol("Kayıpsız: tahmini boyut yok", "yok", BoyutTahmini.Hesapla(kayipsiz, hd, SesSecimi.Yok)?.Metin ?? "yok");
        Kontrol("CBR (karşılaştırma): tahmini boyut var", "True", (BoyutTahmini.Hesapla(Varsayilanlar.TikTokNvenc(), hd, SesSecimi.Yok) is not null).ToString());
        Kontrol("Kayıpsız ayar dosyasına yazılmaz (yalnızca tune)", "False",
            AyarDosyasi.Yaz(kayipsiz).Contains("Kayipsiz", StringComparison.Ordinal).ToString());

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
        Console.WriteLine("--- YouTube varsayılanı (x264 CRF 18, Main, VBV 34375 / 55000): seviye kaynakla aynı, sınır aşılırsa yeten en düşük");

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

        // Profil Main (16 Kasım, kullanıcının örnek videosuyla birebir): kaynak High olsa da 8x8 DCT kapalı, dosya Main.
        var k = Yt(Kaynak(1920, 1080, 30, "h264", "High", 40));
        Iceriyor("High@4.0 1080p30 (34375 kb/s Main 4.0'ı aşar)", k, " -profile:v main -level:v 4.1 ");
        Iceriyor("High@4.0 1080p30", k, ":vbv-maxrate=34375:vbv-bufsize=55000:");
        Iceriyor("High kaynak, profil Main: 8x8 DCT kapalı", k, ":8x8dct=0:");
        Icermiyor("High kaynak, profil Main", k, " -profile:v high ");
        Iceriyor("High@4.2 1080p60: Main", Yt(Kaynak(1920, 1080, 60, "h264", "High", 42)), " -profile:v main -level:v 4.2 ");
        Iceriyor("Baseline kaynak: Main", Yt(Kaynak(720, 1280, 30, "h264", "Constrained Baseline", 31)), " -profile:v main -level:v 4.1 ");
        Iceriyor("HEVC kaynak: Main", Yt(Kaynak(1920, 1080, 30, "hevc", "Main 10", 120)), " -profile:v main ");
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

        Iceriyor("Profil kaynak (High: 8x8 DCT açılır)", Yt(hd, x =>
        {
            Vbv55(x);
            x.Profil = "kaynak";
        }), " -level:v 4.1 ");
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
            x.Profil = "kaynak";
            x.EkParametreler = "no-8x8dct=1";
        }), " -level:v 5.0 ");
        Iceriyor("YouTube varsayılanı (Main), VBV 55000 / 70000: Main 5.0", Yt(hd, Vbv55), " -profile:v main -level:v 5.0 ");
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
        x = KomutMetni(Kaynak(1920, 1080, 60, "h264", "High", 42), Varsayilanlar.TikTokNvenc());
        Iceriyor("TikTok NVENC ayarı 60 fps (30 kare, değişmez)", x, " -g 30 ");

        // TikTok varsayılanı (x264): en çok 10 saniyede bir anahtar kare; en az ve sahne eşiği x264'ün (40).
        x = KomutMetni(Kaynak(1920, 1080, 60, "h264", "High", 42), Varsayilanlar.TikTok());
        Iceriyor("TikTok varsayılanı 60 fps (en çok 10 sn: 600 kare)", x, ":keyint=600:");
        Icermiyor("TikTok varsayılanı: en az anahtar kare x264'ün", x, "min-keyint");
        Icermiyor("TikTok varsayılanı: sahne eşiği x264'ün (40)", x, "scenecut");
        x = KomutMetni(Kaynak(1080, 1920, 30000.0 / 1001, "h264", "High", 40), Varsayilanlar.TikTok());
        Iceriyor("TikTok varsayılanı 29,97 fps (en çok 10 sn: 300 kare)", x, ":keyint=300:");
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
        Iceriyor("Mono kaynak (TikTok)", k, " -af pan=stereo|c0=c0|c1=c0,aresample=44100:resampler=soxr:precision=33:dither_method=triangular ");
        Iceriyor("Mono kaynak (TikTok)", k, " -c:a libfdk_aac -profile:a aac_low -b:a 128k -ar 44100 -ac 2 ");
        k = Komut(2, Varsayilanlar.YouTube());
        Icermiyor("Stereo kaynak", k, "pan=");
        Iceriyor("Stereo kaynak", k, " -af aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac ");

        // YouTube varsayılanı: FDK AAC-LC 128 kb/s CBR, 48 kHz stereo; dönüşüm en yüksek kalitede (soxr 33 bit), 24 bit
        // kaynak 16 bite üçgen titreşimle iner; afterburner açık (ffmpeg'in varsayılanı: bayrak yazılmaz).
        Iceriyor("YouTube varsayılanı: FDK AAC 128 kb/s, 48 kHz, en yüksek dönüşüm, üçgen titreşim", k,
            " -af aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac -profile:a aac_low -b:a 128k -ar 48000 -ac 2 -metadata:s:a:0 language=eng ");
        Icermiyor("YouTube varsayılanı: afterburner açık", k, "-afterburner");
        Icermiyor("YouTube varsayılanı: PCM yok", k, "pcm_");

        // TikTok varsayılanı: FDK AAC-LC 128 kb/s CBR, 44,1 kHz stereo; aynı dönüşüm (en yüksek kalite, üçgen titreşim).
        k = Komut(2, Varsayilanlar.TikTok());
        Iceriyor("TikTok varsayılanı: FDK AAC 128 kb/s, 44,1 kHz, en yüksek dönüşüm, üçgen titreşim", k,
            " -af aresample=44100:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac -profile:a aac_low -b:a 128k -ar 44100 -ac 2 -metadata:s:a:0 language=eng ");
        Icermiyor("TikTok varsayılanı: afterburner açık", k, "-afterburner");

        // TikTok'ta WAV seçilirse de aynı dönüşüm, 16 bit 44,1 kHz.
        var wav = Varsayilanlar.TikTok();
        wav.Ses.Kodlayici = SesKodlayici.Wav;
        k = Komut(2, wav);
        Iceriyor("TikTok'ta WAV seçilirse (16 bit, 44,1 kHz)", k, " -af aresample=44100:resampler=soxr:precision=33:dither_method=triangular -c:a pcm_s16le -ar 44100 -ac 2 ");
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
            Iceriyor(ad + " varsayılanı: ses filtresi değişmedi", k, " -af aresample=" + (ad == "TikTok" ? "44100" : "48000")
                + ":resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac ");
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

        // Zincir denemeleri WAV 16 bit 48 kHz sesle (0.10.0'ın TikTok sesi): örnekler kayıpsız, hız belli.
        var esit = Varsayilanlar.TikTokNvenc();
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
        var esitKapali = Varsayilanlar.TikTokNvenc();
        esitKapali.Ses.Esitleme = true;
        esitKapali.Ses.Sinirlayici = false;
        Iceriyor("Eşitleme: sınırlayıcı kutusu kapalı olsa da tepe aşıyorsa tutulur", Komut(esitKapali, olcum), ",alimiter=limit=0.891251:");
        Iceriyor("Eşitleme + telafi 2,5 dB: kazanç 9,87 dB", " " + string.Join(' ', ArgsBuilder.Olustur(esit, Kaynak48(), "KAYNAK.mp4",
            ArgsBuilder.SesSec(esit.Ses, Kaynak48(), "KAYNAK.mp4"), "CIKTI.mov", "GECIS", olcum, 2.5).Gecisler[0]) + " ",
            " -af volume=9.87dB,aresample=192000:");
        Iceriyor("Telafi ölçümü: kodlamadaki ses zinciri + ölçüm", " " + string.Join(' ', ArgsBuilder.DenemeOlcumKomutu(esit.Ses, "KAYNAK.mp4",
            ArgsBuilder.SesSec(esit.Ses, Kaynak48(), "KAYNAK.mp4"), 10, olcum, 2.5)) + " ",
            " -map 0:a:0 -vn -sn -dn -t 10 -af volume=9.87dB,aresample=192000:resampler=soxr:precision=33,alimiter=limit=0.891251:level=0:latency=1,"
            + "aresample=48000:resampler=soxr:precision=33:dither_method=triangular,ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I,ametadata=mode=print:key=lavfi.r128.true_peak -f null - ");
        esit.Ses.OrnekHizi = null;
        Iceriyor("Eşitleme + örnekleme 'kaynakla aynı': 192 kHz'ten kaynağın hızına", Komut(esit, olcum), ",aresample=44100:resampler=soxr");
        Iceriyor("Eşitleme + örnekleme 'kaynakla aynı'", Komut(esit, olcum), " -ar 44100 ");
        Icermiyor("Eşitleme (sınırlayıcısız) + örnekleme 'kaynakla aynı': hız değişmez", Komut(esit, olcumAlcak), " -ar ");

        // TikTok varsayılanı (FDK AAC 128 kb/s 44,1 kHz): aynı sabit kazanç; 44,1 kHz'e dönüş (sınırlayıcı 192 kHz'te).
        var esitTt = Varsayilanlar.TikTok();
        Iceriyor("TikTok varsayılanı + eşitleme: sabit kazanç, 44,1 kHz, FDK AAC", Komut(esitTt, olcumAlcak),
            " -af volume=7.37dB,aresample=44100:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac -profile:a aac_low -b:a 128k -ar 44100 -ac 2 ");
        Iceriyor("TikTok varsayılanı + eşitleme, tepe aşıyor: 192 kHz sınırlayıcı, sonra 44,1 kHz", Komut(esitTt, olcum),
            " -af volume=7.37dB,aresample=192000:resampler=soxr:precision=33,alimiter=limit=0.891251:level=0:latency=1,"
            + "aresample=44100:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac ");

        // Mono çıktı: kanal indirme ölçümden ve eşitlemeden önce (stereo ölçülüp mono'ya inilince sonuç 3–6 dB düşük kalırdı).
        var mono = Varsayilanlar.YouTube();
        mono.Ses.Kanal = KanalModu.Mono;
        Iceriyor("Mono, eşitleme kapalı: kanal indirme yine zincirde (ölçümdeki gibi, 0.13.0)", Komut(mono),
            " -af pan=mono|c0=0.5*c0+0.5*c1,aresample=48000:");
        Iceriyor("Mono, eşitleme kapalı", Komut(mono), " -ac 1 ");
        mono.Ses.Esitleme = true;
        Iceriyor("Mono + eşitleme: önce mono'ya iner (yarım + yarım), sonra sabit kazanç", Komut(mono, olcum),
            " -af pan=mono|c0=0.5*c0+0.5*c1,volume=7.37dB,");
        Iceriyor("Mono + eşitleme: ölçüm de mono", " " + string.Join(' ', ArgsBuilder.OlcumKomutu(mono.Ses, "KAYNAK.mp4",
            ArgsBuilder.SesSec(mono.Ses, Kaynak48(), "KAYNAK.mp4"), 10)) + " ",
            " -af pan=mono|c0=0.5*c0+0.5*c1,aresample=48000:resampler=soxr:precision=33,ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I,ametadata=mode=print:key=lavfi.r128.true_peak ");
        Iceriyor("Mono + eşitleme, ses dokunulmadı (ölçüm yok): indirme yine zincirde", Komut(mono), " -af pan=mono|c0=0.5*c0+0.5*c1,");

        var olc = ArgsBuilder.OlcumKomutu(esit.Ses, "KAYNAK.mp4", ArgsBuilder.SesSec(esit.Ses, Kaynak48(), "KAYNAK.mp4"), 10);
        Iceriyor("Ölçüm komutu (BS.1770 ölçeri, loudnorm değil)", " " + string.Join(' ', olc) + " ",
            " -map 0:a:0 -vn -sn -dn -t 10 -af ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I,ametadata=mode=print:key=lavfi.r128.true_peak -f null - ");
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

        // Telafi adımı: deneme ölçümü hedefin altındaysa eksik kadar artar, üstündeyse azalır (en çok ± 6 dB); AAC'de de
        // WAV'da da 0,02 LU içindeyse durur (0.13.0'a kadar WAV'da 0,2 LU, yalnızca artardı).
        string Adim(double telafi, double olculen)
        {
            var (yeni, sinir) = Esitleme.TelafiAdimi(telafi, olculen, -14);
            return (yeni?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "dur") + (sinir ? "/sinir" : string.Empty);
        }

        Kontrol("Telafi: -17,79 ölçüldü → 3,79", "3.79", Adim(0, -17.79));
        Kontrol("Telafi: 3,79 ile -14,25 → 4,04", "4.04", Adim(3.79, -14.25));
        Kontrol("Telafi: -14,15 → 4,19 (WAV'da da 0,02 LU içine kadar)", "4.19", Adim(4.04, -14.15));
        Kontrol("Telafi: -14,015 (0,02 içinde) → dur", "dur", Adim(4.19, -14.015));
        Kontrol("Telafi: -24 ölçüldü → en çok 6", "6.00/sinir", Adim(0, -24));
        Kontrol("Telafi: sınırda, yine eksik → dur", "dur/sinir", Adim(6, -15));
        Kontrol("Telafi: hedefi geçti → azalır", "2.50", Adim(3, -13.5));

        // AAC çıktı: deneme ses AAC'ye çevrilip ölçülür (AAC tizleri keser, yükseklik biraz düşer); 0,02 LU içinde
        // durur, adım 0,01, AAC yüksekliği artırdıysa eksi olabilir. WAV'da (yalnızca sınırlayıcı) 0,2 LU.
        string AdimAac(double telafi, double olculen)
        {
            var (yeni, sinir) = Esitleme.TelafiAdimi(telafi, olculen, -14);
            return (yeni?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "dur") + (sinir ? "/sinir" : string.Empty);
        }

        Kontrol("AAC telafi: -14,16 ölçüldü (kullanıcının OBS kaydı, 128 kb/s) → 0,16", "0.16", AdimAac(0, -14.16));
        Kontrol("AAC telafi: 0,16 ile -14,003 → dur", "dur", AdimAac(0.16, -14.003));
        Kontrol("AAC telafi: 0,16 ile -14,03 → 0,19", "0.19", AdimAac(0.16, -14.03));
        Kontrol("AAC telafi: AAC yüksekliği artırdı (-13,9) → -0,10", "-0.10", AdimAac(0, -13.9));
        Kontrol("AAC telafi: en çok 6 dB", "6.00/sinir", AdimAac(0, -24));
        Kontrol("Sınırlayıcı telafisi (WAV): -14,16 → 0,16 (0.13.0'a kadar 0,2 LU içinde dururdu)", "0.16", Adim(0, -14.16));
        Kontrol("WAV, hedeften yüksek (çok kısık seste ölçer kayması): -13,62 → -0,38", "-0.38", Adim(0, -13.62));

        // Sekant: aynı sınırlayıcı ayarıyla iki deneme varsa adım eğime göre (sınırlayıcı kazancın bir kısmını yer).
        string Sekant(double t0, double o0, double t1, double o1)
        {
            var (yeni, sinir) = Esitleme.TelafiAdimi(t1, o1, -14, (t0, o0));
            return (yeni?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "dur") + (sinir ? "/sinir" : string.Empty);
        }

        Kontrol("Sekant: 0 → -15, 2 → -14,4 (eğim 0,3): 2 + 0,4 / 0,3 = 3,33", "3.33", Sekant(0, -15, 2, -14.4));
        Kontrol("Sekant: eğim 0,6 (0 → -17,79, 3,79 → -15,5): 3,79 + 1,5 / 0,604 = 6,27 → en çok 6", "6.00/sinir", Sekant(0, -17.79, 3.79, -15.5));
        Kontrol("Sekant: eğim 1 (sınırlayıcısız): adım eksik kadar", "2.50", Sekant(0, -16.5, 2, -14.5));
        Kontrol("Sekant: eğim eksi (ölçüm oynadı): adım eksik kadar", "2.60", Sekant(1, -14.5, 2, -14.6));
        Kontrol("Sekant: eğim çok küçük (0,05 → 0,25 sayılır): 1 + 0,2 × 4", "1.80", Sekant(0, -14.25, 1, -14.2));
        Kontrol("Sekant: aynı telafi (eğim yok): adım eksik kadar", "1.30", Sekant(1, -14.4, 1, -14.3));
        Kontrol("Kayıplı mı: TikTok sesi (FDK AAC 44,1 kHz)", "True", Esitleme.KayipliMi(Varsayilanlar.TikTok().Ses).ToString());
        Kontrol("Kayıplı mı: TikTok NVENC ayarının sesi (WAV)", "False", Esitleme.KayipliMi(Varsayilanlar.TikTokNvenc().Ses).ToString());
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
            " -i CIKTI.mov -map 0:a:0 -vn -sn -dn -af ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I,ametadata=mode=print:key=lavfi.r128.true_peak -f null - ");

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

    private static void TepeKorumasi()
    {
        Console.WriteLine();
        Console.WriteLine("--- Tepe koruması (0.12.0): çıktının gerçek tepesi (AAC'de çözülen sesinki) tavanı aşmaz; ölçülüp düzeltilir");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        static MediaInfo Kaynak() => new(1920, 1080, 1, 1, 0, 10, true, 60, "bt709", "bt709", "tv")
        {
            VideoCodec = "h264",
            VideoProfil = "High",
            VideoSeviye = 42,
            SesAkislari = [new SesAkisi(1, "pcm_s24le", 48000, 2, 24, null)],
        };

        string Komut(ProfilAyarlari a, SesOlcum? olcum = null, double telafi = 0, double tasmaPayi = 0, bool sinirla = false)
        {
            var m = Kaynak();
            var ses = ArgsBuilder.SesSec(a.Ses, m, "KAYNAK.mp4");
            return " " + string.Join(' ', ArgsBuilder.Olustur(a, m, "KAYNAK.mp4", ses, "CIKTI.mov", "GECIS", olcum, telafi, tasmaPayi, sinirla).Gecisler[0]) + " ";
        }

        // Komutlar: tepe koruması sınırlayıcıyı tavanın altına çeker ya da dokunulmayan sese koyar.
        var tt = Varsayilanlar.TikTok();
        Iceriyor("TikTok, dokunulmayan yüksek ses, sınırlayıcı zorla, pay 0,91: 192 kHz sınırlayıcı -1,91 dB, kazanç yok", Komut(tt, null, 0, 0.91, true),
            " -af aresample=192000:resampler=soxr:precision=33,alimiter=limit=0.802602:level=0:latency=1,"
            + "aresample=44100:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac ");
        Icermiyor("TikTok, dokunulmayan ses + sınırlayıcı: yükseklik değişmez (volume yok)", Komut(tt, null, 0, 0.91, true), "volume=");
        Iceriyor("TikTok, düzeltme yok: ses zinciri değişmedi (sınırlayıcı yok)", Komut(tt),
            " -af aresample=44100:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac ");
        var tepeli = new SesOlcum(-21.37, -3.2, 6.4);
        Iceriyor("TikTok + eşitleme, telafi 0,16, pay 0,18: kazanç 7,53 dB, sınırlayıcı -1,18 dB", Komut(tt, tepeli, 0.16, 0.18),
            " -af volume=7.53dB,aresample=192000:resampler=soxr:precision=33,alimiter=limit=0.872971:level=0:latency=1,aresample=44100:");
        var sinirda = new SesOlcum(-21.37, -8.5, 6);
        Icermiyor("Eşitleme: tepe -8,5 + 7,37 = -1,13 (tavanın altında): sınırlayıcı yok", Komut(tt, sinirda), "alimiter");
        Iceriyor("Eşitleme: aynı ses, pay 0,3 (tavan -1,3): sınırlayıcı girer", Komut(tt, sinirda, 0, 0.3), ",alimiter=limit=0.860994:level=0:latency=1,");
        var elle = Varsayilanlar.YouTube();
        elle.Ses.Seviye = 6;
        Iceriyor("Elle +6 dB, pay 0,18: sınırlayıcı -1,18 dB", Komut(elle, null, 0, 0.18), " -af volume=6dB,aresample=192000:resampler=soxr:precision=33,alimiter=limit=0.872971:");

        // Zincirde sınırlayıcı var mı (kodlama, denemeler ve rapor aynı kuralla).
        var s = Varsayilanlar.TikTok().Ses;
        Kontrol("Sınırlayıcı: varsayılan, ölçüm yok", "False", ArgsBuilder.SinirlayiciVar(s, null).ToString());
        Kontrol("Sınırlayıcı: tepe koruması zorladı", "True", ArgsBuilder.SinirlayiciVar(s, null, sinirla: true).ToString());
        Kontrol("Sınırlayıcı: eşitleme tepeyi aşırıyor (-3,2 + 7,37)", "True", ArgsBuilder.SinirlayiciVar(s, tepeli).ToString());
        Kontrol("Sınırlayıcı: eşitleme, tepe -1,13 (aşmıyor)", "False", ArgsBuilder.SinirlayiciVar(s, sinirda).ToString());
        Kontrol("Sınırlayıcı: aynı ses, pay 0,3", "True", ArgsBuilder.SinirlayiciVar(s, sinirda, tasmaPayi: 0.3).ToString());
        var kapali = Varsayilanlar.TikTok().Ses;
        kapali.Seviye = 6;
        Kontrol("Sınırlayıcı: elle +6 dB, kutu açık", "True", ArgsBuilder.SinirlayiciVar(kapali, null).ToString());
        kapali.Sinirlayici = false;
        Kontrol("Sınırlayıcı: elle +6 dB, kutu kapalı", "False", ArgsBuilder.SinirlayiciVar(kapali, null).ToString());
        kapali.Seviye = 0;
        Kontrol("Sınırlayıcı: kutu kapalı, eşitleme tepeyi aşırıyor (eskisi gibi tutulur)", "True", ArgsBuilder.SinirlayiciVar(kapali, tepeli).ToString());

        // Denemeler: dokunulmayan seste kazanç yok; WAV denemesinde kanal indirme zincirde (kodlamadaki -ac'nin aynısı).
        var tts = Varsayilanlar.TikTok().Ses;
        var ic = ArgsBuilder.SesSec(tts, Kaynak(), "KAYNAK.mp4");
        var aac = " " + string.Join(' ', ArgsBuilder.DenemeKodlamaKomutu(tts, "KAYNAK.mp4", ic, 10, null, 0, "DENEME.m4a", 0.91, true)) + " ";
        Iceriyor("AAC denemesi, dokunulmayan ses + sınırlayıcı: kodlamadaki zincir", aac,
            " -t 10 -af aresample=192000:resampler=soxr:precision=33,alimiter=limit=0.802602:level=0:latency=1,"
            + "aresample=44100:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac -profile:a aac_low -b:a 128k -ar 44100 -ac 2 ");
        var aacDuz = " " + string.Join(' ', ArgsBuilder.DenemeKodlamaKomutu(tts, "KAYNAK.mp4", ic, 10, null, 0, "DENEME.m4a")) + " ";
        Iceriyor("AAC denemesi, dokunulmayan ses: kodlamadaki zincirle aynı (yalnızca dönüşüm)", aacDuz,
            " -t 10 -af aresample=44100:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac ");
        var wav = Varsayilanlar.YouTube().Ses;
        wav.Kodlayici = SesKodlayici.Wav;
        wav.Kanal = KanalModu.Mono;
        var wavSes = ArgsBuilder.SesSec(wav, Kaynak(), "KAYNAK.mp4");
        var wavDeneme = " " + string.Join(' ', ArgsBuilder.DenemeOlcumKomutu(wav, "KAYNAK.mp4", wavSes, 10, null, 0)) + " ";
        Iceriyor("WAV denemesi, dokunulmayan mono çıktı: kanal indirme zincirde, kazanç yok, ölçer", wavDeneme,
            " -t 10 -af pan=mono|c0=0.5*c0+0.5*c1,aresample=48000:resampler=soxr:precision=33:dither_method=triangular,ebur128=peak=true:metadata=1,");
        Icermiyor("WAV denemesi, dokunulmayan ses: kazanç yok", wavDeneme, "volume=");
        Iceriyor("Ölçer: yükseklik ve gerçek tepe çerçeve bilgisinden", " " + Esitleme.OlcumFiltresi + " ",
            " ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I,ametadata=mode=print:key=lavfi.r128.true_peak ");

        // Ölçerin gerçek tepesi: çerçeve bilgisinden (0,001 doğrusal), sondaki 100 ms özette daha yüksekse özetten.
        const string olcer = "[Parsed_ametadata_1 @ 0x1] lavfi.r128.I=-14.020\n[Parsed_ametadata_2 @ 0x2] lavfi.r128.true_peak=0.884\n"
            + "[Parsed_ametadata_1 @ 0x1] lavfi.r128.I=-14.004\n[Parsed_ametadata_2 @ 0x2] lavfi.r128.true_peak=0.891\n"
            + "[Parsed_ebur128_0 @ 0x3] Summary:\n\n  Integrated loudness:\n    I:         -14.0 LUFS\n    Threshold: -24.0 LUFS\n\n"
            + "  Loudness range:\n    LRA:         5.1 LU\n    Threshold: -34.0 LUFS\n    LRA low:   -17.0 LUFS\n    LRA high:  -12.0 LUFS\n\n"
            + "  True peak:\n    Peak:       -1.0 dBFS";
        string Tepe(string g) => ArgsBuilder.TepeOku(g) is { } t
            ? (double.IsNegativeInfinity(t.Db) ? "-inf" : t.Db.ToString("0.0000", inv)) + (t.Hassas ? "/hassas" : "/ozet")
            : "yok";
        Kontrol("Gerçek tepe: son çerçeve bilgisi (0,891 → -1,0024 dB)", "-1.0024/hassas", Tepe(olcer));
        Kontrol("Gerçek tepe: sesin son 100 ms'si daha yüksek (özet -0,5) → özetten", "-0.5000/ozet", Tepe(olcer.Replace("Peak:       -1.0 dBFS", "Peak:       -0.5 dBFS")));
        Kontrol("Gerçek tepe: çerçeve bilgisi yok → özetten", "-1.0000/ozet", Tepe(olcer.Replace("lavfi.r128.true_peak", "lavfi.r128.sample_peak")));
        Kontrol("Gerçek tepe: sessiz ses", "-inf/hassas", Tepe(olcer.Replace("true_peak=0.884", "true_peak=0.000").Replace("true_peak=0.891", "true_peak=0.000")
            .Replace("Peak:       -1.0 dBFS", "Peak:       -inf dBFS")));
        Kontrol("Gerçek tepe: ölçer çıktısı yok", "yok", Tepe("ffmpeg: hata"));
        var oku = ArgsBuilder.OlcumOku(olcer);
        Kontrol("Ölçüm: yükseklik -14,004, tepe hassas", "-14.004/-1.0024/True",
            oku is null ? "yok" : $"{oku.Yukseklik.ToString(inv)}/{oku.Tepe.ToString("0.0000", inv)}/{oku.TepeHassas}");

        // Tepe tavanda mı (ölçerin yuvarlaması kadar pay: çerçeve bilgisinde 0,005 dB, özette 0,05 dB).
        Kontrol("Tavanda: -1,006 (hassas)", "True", Esitleme.TepeTavandaMi(-1.006, true, -1).ToString());
        Kontrol("Tavanda: -1,003 (hassas) → aşabilir", "False", Esitleme.TepeTavandaMi(-1.003, true, -1).ToString());
        Kontrol("Tavanda: -1,06 (özet)", "True", Esitleme.TepeTavandaMi(-1.06, false, -1).ToString());
        Kontrol("Tavanda: -1,04 (özet) → aşabilir", "False", Esitleme.TepeTavandaMi(-1.04, false, -1).ToString());
        Kontrol("Tavanda: sessiz ses", "True", Esitleme.TepeTavandaMi(double.NegativeInfinity, true, -1).ToString());

        // Kodlayıcıya giren sesin tahmini tepesi: kaynağın tepesi + kazanç, sınırlayıcı varsa en çok onun tavanı.
        string Giris(double? kaynak, double kazanc, bool sinir, double sinirTavani) =>
            Esitleme.GirisTepesi(kaynak, kazanc, sinir, sinirTavani)?.ToString("0.00", inv) ?? "yok";
        Kontrol("Giriş tepesi: kaynak bilinmiyor", "yok", Giris(null, 3, true, -1));
        Kontrol("Giriş tepesi: -3 + 2, sınırlayıcısız", "-1.00", Giris(-3, 2, false, -1));
        Kontrol("Giriş tepesi: -3 + 11, sınırlayıcı -1,18'de", "-1.18", Giris(-3, 11, true, -1.18));
        Kontrol("Giriş tepesi: -3 + 1, sınırlayıcı -1,18'de (tepe altında)", "-2.00", Giris(-3, 1, true, -1.18));

        // Deneme adımı (bilgisayarda FDK AAC 128 kb/s ile ölçülen dizilerle): tepe aşarsa sınırlayıcı görülen taşma +
        // 0,1 dB aşağı çekilir; ikisi de tamamsa bu denemenin ayarı son ayardır.
        string Adim(SesDuzeltme d, double tasma, double? yukseklik, double? tepe, double? giris)
        {
            var (yeni, sinir, t) = Esitleme.DenemeAdimi(d, tasma, yukseklik, -14, tepe, true, -1, giris);
            return (yeni is { } y ? string.Create(inv, $"telafi {y.Telafi:0.00} pay {y.TasmaPayi:0.00} {(y.Sinirla ? "sınırla" : "-")}") : "bitti")
                   + string.Create(inv, $" / taşma {t:0.00}") + (sinir ? " / sınır" : string.Empty);
        }

        var d0 = new SesDuzeltme();
        Kontrol("Tepeli ses (14_tepeli): -0,92 (giriş -1) → pay 0,18", "telafi 0.00 pay 0.18 sınırla / taşma 0.08", Adim(d0, 0, -14.01, -0.92, -1));
        Kontrol("Tepeli ses: pay 0,18 ile -1,03, -14,00 → bitti", "bitti / taşma 0.08", Adim(new SesDuzeltme(0, 0.18, true), 0.08, -14.00, -1.03, -1.18));
        Kontrol("Yoğun ses: -0,51 (giriş -1) → pay 0,59", "telafi 0.00 pay 0.59 sınırla / taşma 0.49", Adim(d0, 0, null, -0.51, -1));
        Kontrol("Yoğun ses: pay 0,59 ile -0,94 (giriş -1,59) → pay 0,75", "telafi 0.00 pay 0.75 sınırla / taşma 0.65",
            Adim(new SesDuzeltme(0, 0.59, true), 0.49, null, -0.94, -1.59));
        Kontrol("Yoğun ses: pay 0,75 ile -1,13 → bitti", "bitti / taşma 0.65", Adim(new SesDuzeltme(0, 0.75, true), 0.65, null, -1.13, -1.75));
        Kontrol("Sınırlayıcı yokken taşma (giriş -1,2, çıktı -0,7) → pay 0,6, sınırlayıcı girer", "telafi 0.00 pay 0.60 sınırla / taşma 0.50",
            Adim(d0, 0, null, -0.7, -1.2));
        Kontrol("Hazır yüksek ses (tepe -0,1, sınırlayıcı -1'de): -0,19 → pay 0,91", "telafi 0.00 pay 0.91 sınırla / taşma 0.81",
            Adim(new SesDuzeltme(0, 0, true), 0, null, -0.19, -1));
        Kontrol("Pay 6 dB ile yine aşıyor (AAC-LD, ezilmiş ses): 6,6", "telafi 0.00 pay 6.60 sınırla / taşma 6.50", Adim(new SesDuzeltme(0, 6, true), 0, null, -0.5, -7));
        Kontrol("Pay en çok 12 dB: sınırda, yine aşıyor → bitti", "bitti / taşma 12.50", Adim(new SesDuzeltme(0, 12, true), 0, null, -0.5, -13));
        Kontrol("Yalnızca yükseklik (AAC -14,16) → telafi 0,16", "telafi 0.16 pay 0.00 - / taşma 0.00", Adim(d0, 0, -14.16, -3, -3.1));
        Kontrol("İkisi de tamam → bitti", "bitti / taşma 0.00", Adim(d0, 0, -14.01, -1.2, -1.25));
        Kontrol("Tepe ve yükseklik birlikte: -0,92 ve -14,16 → telafi 0,16, pay 0,18", "telafi 0.16 pay 0.18 sınırla / taşma 0.08",
            Adim(d0, 0, -14.16, -0.92, -1));
        Kontrol("Tepe denemesi yok (tepe null): yalnızca yükseklik", "bitti / taşma 0.00", Adim(d0, 0, -14.0, null, null));
        Kontrol("Sessiz ses → bitti", "bitti / taşma 0.00", Adim(d0, 0, null, double.NegativeInfinity, null));
        Kontrol("Kaynağın tepesi bilinmiyor: aşım kadar (-0,8 → pay 0,3)", "telafi 0.00 pay 0.30 sınırla / taşma 0.20", Adim(d0, 0, null, -0.8, null));

        // Rapor: satır, bilgi ve ipucu yazıları.
        var ka = Varsayilanlar.TikTok().Ses;
        var yuksek = Esitleme.KararVer(ka, new SesOlcum(-9.24, -0.1, 4)) with
        {
            TepeZorla = true, SinirlayiciCalisti = true, TasmaPayi = 0.91, Tasma = 0.81, Kayipli = true, IlkTepe = -0.19, SonucYukseklik = -9.46,
        };
        Kontrol("Zaten yüksek + tepe koruması: satır", "-9,5 LUFS (yalnızca tepeler sınırlandı)", yuksek.SatirMetni ?? "yok");
        Kontrol("Zaten yüksek + tepe koruması: hedefe ulaşılamadı yazmaz", "False", yuksek.HedefeUlasilamadi.ToString());
        Kontrol("Zaten yüksek + tepe koruması: bilgi", "-9,2 LUFS · zaten yüksek, dokunulmuyor (tepeler -1 dB'de tutulur)", yuksek.BilgiMetni);
        Kontrol("Zaten yüksek + tepe koruması: ipucu (tepe tavanın üstündeydi)", "True", yuksek.Ayrinti
            .Contains("Tepe -0,1 dBTP, tavanın üstündeydi: sınırlayıcı yalnızca tepeleri tuttu; sesin geri kalanına dokunulmadı.", StringComparison.Ordinal).ToString());
        Kontrol("Zaten yüksek + tepe koruması: ipucu (AAC payı)", "True", yuksek.Ayrinti
            .Contains("AAC kodlaması tepeyi 0,81 dB yükseltiyordu (denendi, ölçüldü): sınırlayıcı -1,91 dB'de çalıştı; çıktının gerçek tepesi en çok -1 dB.", StringComparison.Ordinal).ToString());
        Kontrol("Zaten yüksek + tepe koruması: karar yazısı", "True", yuksek.Ayrinti
            .Contains("Karar: zaten hedeften yüksek, sesin yüksekliğine dokunulmadı", StringComparison.Ordinal).ToString());
        Kontrol("Zaten yüksek + tepe koruması: sınırlı", "True", yuksek.Sinirli.ToString());
        Kontrol("Zaten yüksek, koruma yok: eskisi gibi", "-9,2 LUFS (dokunulmadı)", Esitleme.KararVer(ka, new SesOlcum(-9.24, -3, 4)).SatirMetni ?? "yok");
        var ezik = Esitleme.KararVer(ka, new SesOlcum(1.1, 10, 20), kararYukseklik: -6.9)
                   with { SinirlayiciCalisti = true, SonucYukseklik = -12.81, TasmaPayi = 6 };
        Kontrol("Zaten yüksek, sınırlayıcı zincirde (elle +30 dB): satır sonuçla", "-12,8 LUFS (yalnızca tepeler sınırlandı)", ezik.SatirMetni ?? "yok");
        Iceriyor("Zaten yüksek, sınırlayıcı zincirde: bilgi satırı", ezik.BilgiMetni, "-6,9 LUFS · zaten yüksek, dokunulmuyor (tepeler -1 dB'de tutulur)");
        var kapaliRapor = new EsitlemeRaporu(EsitlemeKarari.Kapali, new SesOlcum(-12, -0.4, 5), -14, -1)
        {
            TepeZorla = true, SinirlayiciCalisti = true, TasmaPayi = 0.2, Tasma = 0.1, SonucYukseklik = -12.2, SonucTepe = -1.02,
        };
        Kontrol("Eşitleme kapalı, tepe koruması: satır", "tepeler -1 dB'de tutuldu", kapaliRapor.SatirMetni ?? "yok");
        Kontrol("Eşitleme kapalı, tepe koruması: bilgi", "tepeler -1 dB'de tutulur", kapaliRapor.BilgiMetni);
        Kontrol("Eşitleme kapalı, tepe koruması: ipucu", "True", kapaliRapor.Ayrinti
            .StartsWith("Tepe koruması (LUFS eşitleme kapalı; tepe en çok -1 dB)", StringComparison.Ordinal).ToString());
        Kontrol("Eşitleme kapalı, tepe koruması (WAV): dönüşüm payı", "True", kapaliRapor.Ayrinti
            .Contains("Örnekleme hızı dönüşümü tepeyi 0,1 dB yükseltiyordu", StringComparison.Ordinal).ToString());
        Kontrol("Eşitleme kapalı, tepe koruması: sonuç", "True", kapaliRapor.Ayrinti.Contains("Sonuç: -12,2 LUFS · tepe -1,0 dBTP", StringComparison.Ordinal).ToString());
        var dogrusal = Esitleme.KararVer(ka, new SesOlcum(-25.87, -1.47, 3)) with
        {
            Telafi = 0.05, Kayipli = true, TasmaPayi = 0.18, Tasma = 0.08, SinirlayiciCalisti = true, IlkTepe = -0.92, SonucYukseklik = -14.0,
        };
        Kontrol("Eşitleme + sınırlayıcı + AAC payı: satır", "-14,0 LUFS", dogrusal.SatirMetni ?? "yok");
        Kontrol("Eşitleme + sınırlayıcı + AAC payı: ipucu", "True", dogrusal.Ayrinti
            .Contains("AAC kodlaması tepeyi 0,08 dB yükseltiyordu (denendi, ölçüldü): sınırlayıcı -1,18 dB'de çalıştı", StringComparison.Ordinal).ToString());
        Kontrol("Eşitleme + sınırlayıcı + AAC payı: tepe cümlesi eskisi gibi", "True", dogrusal.Ayrinti
            .Contains("dB'ye çıkacaktı: sınırlayıcı -1 dB'de tuttu", StringComparison.Ordinal).ToString());
        Kontrol("Eşitleme + sınırlayıcı + AAC payı: \"yalnızca tepeleri\" yazmaz (ses yükseltildi)", "False", dogrusal.Ayrinti
            .Contains("yalnızca tepeleri", StringComparison.Ordinal).ToString());
    }

    private static void Boyutlar()
    {
        Console.WriteLine();
        Console.WriteLine("--- Tahmini dosya boyutu: (video + ses bit hızı) × süre");
        var m = new MediaInfo(1080, 1920, 1, 1, 0, 60, true, 30, "bt709", "bt709", "tv")
        {
            SesAkislari = [new SesAkisi(1, "aac", 44100, 2, null, 128)],
        };
        var tt = Varsayilanlar.TikTokNvenc();
        var ses = ArgsBuilder.SesSec(tt.Ses, m, "YOK.mp4");
        // (34375 + 48000 × 16 × 2 / 1000) kb/s × 60 s = 269 332 500 bayt = 256,9 MB
        Kontrol("TikTok NVENC ayarı 60 sn (CBR 34375 + WAV 16 bit 48 kHz stereo)", "tahmini 257 MB", BoyutTahmini.Hesapla(tt, m, ses)?.Metin ?? "yok");
        var ttVar = Varsayilanlar.TikTok();
        Kontrol("TikTok varsayılanı (x264 CRF 24, bit hızı üst sınırı yok): tahmin yapılmaz", "yok",
            BoyutTahmini.Hesapla(ttVar, m, ArgsBuilder.SesSec(ttVar.Ses, m, "YOK.mp4"))?.Metin ?? "yok");
        var yt = Varsayilanlar.YouTube();
        // (34375 + 128 FDK AAC) × 60 = 2 070 180 kbit + tampon payı: 55000 kbit × 0,9 (x264 vbv-init varsayılanı)
        // = 49 500 kbit → 2 119 680 kbit = 264 960 000 bayt = 252,7 MB
        Kontrol("YouTube 60 sn (CRF 18, en yüksek 34375, tampon 55000, AAC 128)", "en çok 253 MB", BoyutTahmini.Hesapla(yt, m, ses)?.Metin ?? "yok");
        var kisa = m with { DurationSeconds = 5 };
        // 5 sn: (34375 + 128) × 5 + 49500 = 222 015 kbit = 27 751 875 bayt = 26,5 MB
        Kontrol("YouTube 5 sn: tampon payı kısa videoda büyük", "en çok 26,5 MB", BoyutTahmini.Hesapla(yt, kisa, ses)?.Metin ?? "yok");
        yt.Video.X264.VbvMaks = null;
        Kontrol("CRF, üst sınır yok: tahmin yapılmaz", "yok", BoyutTahmini.Hesapla(yt, m, ses)?.Metin ?? "yok");
        var cq = Varsayilanlar.TikTokNvenc();
        cq.Video.Nvenc.Mod = NvencModu.SabitKalite;
        cq.Video.Nvenc.MaksBitHizi = 30000;
        cq.Video.Nvenc.Tampon = 40000;
        // NVENC sabit kalite: tampon yok sayılır, kart seviyenin tamponunu kullanır. Kaynak H.264 değil → Main,
        // seviye otomatik → yeten en düşük: 30000 kb/s 4.0'ı (20000) aşar → 4.1, tamponu 62500 kbit.
        // (30000 + 1536) × 60 + 62500 = 1 954 660 kbit = 244 332 500 bayt = 233,0 MB
        Kontrol("NVENC sabit kalite, en yüksek 30000 (tampon 40000 yok sayılır)", "en çok 233 MB", BoyutTahmini.Hesapla(cq, m, ses)?.Metin ?? "yok");
        cq.Video.Nvenc.MaksBitHizi = null;
        Kontrol("NVENC sabit kalite, üst sınır yok: tahmin yapılmaz", "yok", BoyutTahmini.Hesapla(cq, m, ses)?.Metin ?? "yok");
        var sessiz = Varsayilanlar.TikTokNvenc();
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

    private static void HariciSes()
    {
        Console.WriteLine();
        Console.WriteLine("--- Aynı adlı harici ses: videonun süresine tam getirilir (atrim + apad); ölçüm, telafi denemesi ve kodlama aynı uzunlukta");

        // Video izinin süresi: MP4 / MOV akış süresi, Matroska / WebM "DURATION" etiketi, yoksa 0 (dosyanın süresi kullanılır).
        static string Sure(string akis, string bicimSuresi = "25.5") =>
            MediaProbe.Parse("{\"streams\": [{\"codec_type\": \"video\", \"width\": 1280, \"height\": 720, " + akis
                             + "}], \"format\": {\"duration\": \"" + bicimSuresi + "\"}}")
                .VideoSuresi.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        Kontrol("Video izinin süresi: akış süresi (MP4 / MOV)", "6", Sure("\"duration\": \"6.000000\""));
        Kontrol("Video izinin süresi: Matroska DURATION etiketi", "25.042", Sure("\"tags\": {\"DURATION\": \"00:00:25.042000000\"}"));
        Kontrol("Video izinin süresi: dile göre etiket (DURATION-eng)", "62.5", Sure("\"tags\": {\"DURATION-eng\": \"00:01:02.500000000\"}"));
        Kontrol("Video izinin süresi: bilinmiyor", "0", Sure("\"tags\": {\"title\": \"x\"}"));

        static MediaInfo Video(double bicimSuresi, double videoSuresi) =>
            new(1280, 720, 1, 1, 0, bicimSuresi, false, 30, "bt709", "bt709", "tv")
            {
                VideoCodec = "h264",
                VideoProfil = "High",
                VideoSeviye = 31,
                VideoSuresi = videoSuresi,
            };
        var harici = new SesSecimi(SesSecimTuru.HariciDosya, "SES.wav", 2) { OrnekHizi = 48000 };
        var ic = new SesSecimi(SesSecimTuru.Video, null, 2) { OrnekHizi = 48000 };
        static string S(double d) => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        Kontrol("Ses süresi: harici, video izi 5,5 sn (dosya 6,2)", "5.5", S(ArgsBuilder.SesSuresi(Video(6.2, 5.5), harici)));
        Kontrol("Ses süresi: harici, video izinin süresi bilinmiyor", "6", S(ArgsBuilder.SesSuresi(Video(6, 0), harici)));
        Kontrol("Ses süresi: harici, video izi dosyadan uzun yazılmış", "6.2", S(ArgsBuilder.SesSuresi(Video(6.2, 6.5), harici)));
        Kontrol("Ses süresi: videonun kendi sesi (dosyanın süresi)", "6.2", S(ArgsBuilder.SesSuresi(Video(6.2, 5.5), ic)));

        static string Komut(ProfilAyarlari a, MediaInfo m, SesSecimi ses, SesOlcum? olcum = null) =>
            " " + string.Join(' ', ArgsBuilder.Olustur(a, m, "KAYNAK.mp4", ses, "CIKTI.mov", "GECIS", olcum).Gecisler[0]) + " ";
        var tt = Varsayilanlar.TikTok();
        var k = Komut(tt, Video(6, 6), harici);
        Iceriyor("TikTok + harici ses", k, " -i KAYNAK.mp4 -i SES.wav -map 0:v:0 -map 1:a:0 ");
        Iceriyor("TikTok + harici ses: videonun süresine tam (kısaysa sessizlik, uzunsa kesilir)", k,
            " -af aresample=44100:resampler=soxr:precision=33:dither_method=triangular,apad,atrim=end=6 -c:a libfdk_aac ");
        Icermiyor("TikTok + harici ses: süre belli, -shortest yok (son AAC karesini keserdi)", k, "-shortest");
        var olcum = new SesOlcum(-21.37, -10, 6.4);
        Iceriyor("TikTok + harici ses + eşitleme: kazanç, 44,1 kHz, sonra tam süre", Komut(tt, Video(6, 6), harici, olcum),
            " -af volume=7.37dB,aresample=44100:resampler=soxr:precision=33:dither_method=triangular,apad,atrim=end=6 -c:a libfdk_aac ");
        Iceriyor("Harici ses: video izinin süresiyle (25,042 sn)", Komut(tt, Video(25.5, 25.042), harici), ",apad,atrim=end=25.042 ");
        var bilinmez = Komut(tt, Video(0, 0), harici);
        Iceriyor("Harici ses, süre bilinmiyor: eski yol (sessizlikle doldur)", bilinmez, ":dither_method=triangular,apad -c:a ");
        Iceriyor("Harici ses, süre bilinmiyor: video bitince kesilir", bilinmez, " -shortest ");
        Icermiyor("Videonun kendi sesi: kesme / doldurma yok", Komut(tt, Video(6, 6), ic), "apad");

        // Ölçüm ve telafi denemesi kodlamadaki uzunlukla: harici seste giriş -t ile kısılmaz (gecikme / kesme kaydırmasın),
        // zincirin sonunda aynı atrim + apad.
        var tts = tt.Ses;
        var olc = " " + string.Join(' ', ArgsBuilder.OlcumKomutu(tts, "KAYNAK.mp4", harici, 6)) + " ";
        Iceriyor("Ölçüm, harici ses: WAV okunur", olc, " -i SES.wav -map 0:a:0 -vn -sn -dn ");
        Icermiyor("Ölçüm, harici ses: giriş -t ile kısılmaz", olc, " -t ");
        Iceriyor("Ölçüm, harici ses: çıktının hızında (44,1 kHz) ve kodlamadaki uzunlukla", olc,
            " -af aresample=44100:resampler=soxr:precision=33,apad,atrim=end=6,ebur128=peak=true:");
        var olcIc = " " + string.Join(' ', ArgsBuilder.OlcumKomutu(tts, "KAYNAK.mp4", ic, 6)) + " ";
        Iceriyor("Ölçüm, videonun kendi sesi: dosyanın süresiyle, çıktının hızında", olcIc, " -t 6 -af aresample=44100:resampler=soxr:precision=33,ebur128=");
        Icermiyor("Ölçüm, videonun kendi sesi: kesme / doldurma yok", olcIc, "apad");
        var olcBilinmez = " " + string.Join(' ', ArgsBuilder.OlcumKomutu(tts, "KAYNAK.mp4", harici, 0)) + " ";
        Icermiyor("Ölçüm, harici ses, süre bilinmiyor: sonsuz doldurma yok", olcBilinmez, "apad");

        var deneme = " " + string.Join(' ', ArgsBuilder.DenemeKodlamaKomutu(tts, "KAYNAK.mp4", harici, 6, olcum, 0.12, "DENEME.m4a")) + " ";
        Icermiyor("AAC telafi denemesi, harici ses: giriş -t ile kısılmaz", deneme, " -t ");
        Iceriyor("AAC telafi denemesi, harici ses: kodlamadaki zincir ve uzunluk", deneme,
            " -af volume=7.49dB,aresample=44100:resampler=soxr:precision=33:dither_method=triangular,apad,atrim=end=6 -c:a libfdk_aac -profile:a aac_low -b:a 128k -ar 44100 -ac 2 ");
        Icermiyor("AAC telafi denemesi, harici ses: -shortest yok", deneme, "-shortest");
        var denemeBilinmez = " " + string.Join(' ', ArgsBuilder.DenemeKodlamaKomutu(tts, "KAYNAK.mp4", harici, 0, olcum, 0, "DENEME.m4a")) + " ";
        Icermiyor("AAC telafi denemesi, harici ses, süre bilinmiyor: sonsuz doldurma yok", denemeBilinmez, "apad");
        var wav = Varsayilanlar.YouTube().Ses;
        wav.Kodlayici = SesKodlayici.Wav;
        var tepeli = new SesOlcum(-21.37, -3.2, 6.4);
        var sinirli = " " + string.Join(' ', ArgsBuilder.DenemeOlcumKomutu(wav, "KAYNAK.mp4", harici, 6, tepeli, 0)) + " ";
        Iceriyor("Sınırlayıcı telafi ölçümü, harici ses: kodlamadaki zincir ve uzunluk", sinirli,
            ",aresample=48000:resampler=soxr:precision=33:dither_method=triangular,apad,atrim=end=6,ebur128=");
        Icermiyor("Sınırlayıcı telafi ölçümü, harici ses: giriş -t ile kısılmaz", sinirli, " -t ");

        // x264 / NVENC penceresinin komut notundaki örnek profilin kendi anahtar karesinden (kare hızı kaynakla aynıyken).
        static string Ornek(ProfilAyarlari a, double fps, bool nvenc = false) => AnahtarKareHesabi.OrnekMetni(a.AnahtarKare, fps, nvenc) ?? "yok";
        Kontrol("Komut notu örneği: TikTok varsayılanı, 60 fps (10 sn)", "600", Ornek(Varsayilanlar.TikTok(), 60));
        Kontrol("Komut notu örneği: TikTok varsayılanı, 29,97 fps", "300", Ornek(Varsayilanlar.TikTok(), 30000.0 / 1001));
        Kontrol("Komut notu örneği: YouTube varsayılanı, 60 fps", "100 / 51", Ornek(Varsayilanlar.YouTube(), 60));
        Kontrol("Komut notu örneği: YouTube varsayılanı, NVENC (yalnızca en fazla)", "100", Ornek(Varsayilanlar.YouTube(), 60, nvenc: true));
        var bos = Varsayilanlar.TikTok();
        bos.AnahtarKare.EnBuyuk = null;
        Kontrol("Komut notu örneği: en fazla boş", "yok", Ornek(bos, 60));
    }

    private static void Kaplar()
    {
        Console.WriteLine();
        Console.WriteLine("--- Dosya biçimi: TikTok ve YouTube MOV, dikey 9:16 MP4, WAV ses her zaman MOV; kodlama tarihi");
        Kontrol("TikTok varsayılanı", ".mov", Varsayilanlar.TikTok().Uzanti);
        Kontrol("YouTube varsayılanı", ".mov", Varsayilanlar.YouTube().Uzanti);
        Kontrol("YouTube varsayılanı: x264 profili", "main / kaynak", $"{Varsayilanlar.YouTube().Video.X264.Profil} / {Varsayilanlar.YouTube().Video.X264.Seviye}");
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
        const string TikTokSes = "FdkAac Bit16 44100 EnYuksek Ucgen";

        var eskiYt = Varsayilanlar.YouTube();
        eskiYt.Surum = 4;
        EskiSes(eskiYt.Ses);
        eskiYt.Video.Kap = KapBicimi.Mp4;
        eskiYt.AnahtarKare.Birim = AralikBirimi.Kare;
        eskiYt.Video.X264.Crf = 20; // kullanıcının kendi değişikliği korunmalı
        eskiYt.Video.X264.VbvMaks = 14000; // 0.8.6'ya kadar YouTube VBV'si 14000 / 28000
        eskiYt.Video.X264.VbvTampon = 28000;
        var eskiTt = Varsayilanlar.TikTokNvenc(); // 0.10.0'a kadar TikTok varsayılanı (NVENC)
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
        Kontrol("TikTok 5 → 12: değiştirilmemiş video yeni varsayılana geçer (x264)", "X264", tt.Video.Kodlayici.ToString());
        Kontrol("TikTok 5 → 9: tampon boş (bit hızı kadar)", "boş", tt.Video.Nvenc.Tampon?.ToString() ?? "boş");
        Kontrol("TikTok 5 → 12: ses FDK AAC 128 kb/s 44,1 kHz, en yüksek kalite, üçgen titreşim", TikTokSes + " 128",
            SesOzeti(tt.Ses) + " " + tt.Ses.Fdk.BitHizi);
        Kontrol("YouTube 4 → 11: ses FDK AAC 128 kb/s, en yüksek kalite, üçgen titreşim", YouTubeSes + " 128",
            SesOzeti(yt.Ses) + " " + yt.Ses.Fdk.BitHizi);
        Kontrol("TikTok varsayılanı: ses FDK AAC 128 kb/s CBR 44,1 kHz, afterburner açık", TikTokSes + " aac_low Cbr 128 True",
            SesOzeti(Varsayilanlar.TikTok().Ses) + " " + FdkOzeti(Varsayilanlar.TikTok().Ses.Fdk));
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
        var tt10 = Varsayilanlar.TikTokNvenc();
        tt10.Surum = 10;
        var ayarTt10 = new AppSettings();
        ayarTt10.Profiller[ExportProfileId.TikTok] = tt10;
        Kontrol("TikTok 10 → 12: 0.10.0'ın sesi (WAV 16 bit 48 kHz) FDK AAC 128 kb/s 44,1 kHz olur", TikTokSes,
            SesOzeti(ayarTt10.ProfilAyari(ExportProfileId.TikTok).Ses));
        var onYt10 = Varsayilanlar.YouTube();
        onYt10.Surum = 10;
        Varsayilanlar.PlatformSesi(onYt10.Ses);
        Kontrol("YouTube ön ayarı (sürüm 10, WAV 16 bit) kaydedildiği gibi kalır", YeniSes,
            SesOzeti(new AppSettings().OnAyariYukle(ExportProfileId.YouTube, onYt10).Ses));

        // 0.8.6'nın (sürüm 7) kendiliğinden yazdığı 55000 kb geri alınır; kullanıcının kendi VBV'si ve
        // yeni sürümde elle girilen tampon korunur.
        var tt7 = Varsayilanlar.TikTokNvenc();
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
        var ttKendi = Varsayilanlar.TikTokNvenc();
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

        var tt8 = Varsayilanlar.TikTokNvenc();
        tt8.Video.Nvenc.Tampon = 55000;
        var ayarlar8 = new AppSettings();
        ayarlar8.Profiller[ExportProfileId.TikTok] = tt8;
        Kontrol("TikTok 8: elle girilen 55000 korunur", "55000", ayarlar8.ProfilAyari(ExportProfileId.TikTok).Video.Nvenc.Tampon?.ToString() ?? "boş");

        // Sürüm 12: TikTok varsayılanı x264 veryslow CRF 24 + FDK AAC 128 kb/s 44,1 kHz (kullanıcının bilgisayarında
        // denenen ayar). 0.10.0'ın değiştirilmemiş TikTok ayarı yenisine geçer; kullanıcının değiştirdiği kısım, LUFS
        // eşitleme seçimi ve ön ayar olduğu gibi kalır.
        static string Json<T>(T x) => System.Text.Json.JsonSerializer.Serialize(x, AyarDosyasi.Secenekler);
        static string Bos(object? x) => x?.ToString() ?? "boş";
        var yeniTt = Varsayilanlar.TikTok();
        var x264Tt = yeniTt.Video.X264;
        Kontrol("TikTok varsayılanı: x264", "X264 Crf 24 veryslow high kaynak · ref 16 · önden bakış 250 · iş parçacığı 8",
            $"{yeniTt.Video.Kodlayici} {x264Tt.Mod} {x264Tt.Crf} {x264Tt.OnAyar} {x264Tt.Profil} {x264Tt.Seviye} · ref {x264Tt.Ref} · önden bakış {x264Tt.IleriBakis} · iş parçacığı {x264Tt.IsParcacigi}");
        Kontrol("TikTok varsayılanı: diğer x264 ayarları hız ayarının (B-kare, me, VBV, qpmax, 8x8 DCT boş)", "boş boş boş boş boş",
            $"{Bos(x264Tt.BKare)} {Bos(x264Tt.Me)} {Bos(x264Tt.VbvMaks)} {Bos(x264Tt.QpMax)} {Bos(x264Tt.Dct8x8)}");
        Kontrol("TikTok varsayılanı: boyut ve kare hızı kaynakla aynı, MOV", "Kaynak Kaynak Mov",
            $"{yeniTt.Video.Cozunurluk} {yeniTt.Video.KareHiziModu} {yeniTt.Video.Kap}");
        Kontrol("TikTok varsayılanı: anahtar kare en çok 10 sn (en az ve sahne eşiği x264'ün)", "Saniye 10 boş boş",
            $"{yeniTt.AnahtarKare.Birim} {yeniTt.AnahtarKare.EnBuyuk} {Bos(yeniTt.AnahtarKare.EnKucuk)} {Bos(yeniTt.AnahtarKare.SahneDegisimi)}");
        Kontrol("TikTok varsayılanı: NVENC'e geçilirse 0.10.0'ın ayarı (CBR 34375)", "Cbr 34375", $"{yeniTt.Video.Nvenc.Mod} {yeniTt.Video.Nvenc.BitHizi}");
        Kontrol("TikTok varsayılanı: LUFS eşitleme açık, -14", "True -14",
            $"{yeniTt.Ses.Esitleme} {yeniTt.Ses.EsitlemeHedef.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        var tt11 = Varsayilanlar.TikTokNvenc();
        tt11.Surum = 11;
        tt11.Ses.EsitlemeHedef = -16; // kullanıcının seçimi korunmalı
        var ayar11 = new AppSettings();
        ayar11.Profiller[ExportProfileId.TikTok] = tt11;
        var g11 = ayar11.ProfilAyari(ExportProfileId.TikTok);
        Kontrol("TikTok 11 → 12: 0.10.0 varsayılanının videosu yeni varsayılan olur", Json(yeniTt.Video), Json(g11.Video));
        Kontrol("TikTok 11 → 12: anahtar kare yeni varsayılan olur", Json(yeniTt.AnahtarKare), Json(g11.AnahtarKare));
        Kontrol("TikTok 11 → 12: ses FDK AAC 128 kb/s 44,1 kHz", TikTokSes + " aac_low Cbr 128 True", SesOzeti(g11.Ses) + " " + FdkOzeti(g11.Ses.Fdk));
        Kontrol("TikTok 11 → 12: LUFS hedefi (-16) korunur", "-16", g11.Ses.EsitlemeHedef.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Kontrol("TikTok 11 → 12: sürüm", ProfilAyarlari.GuncelSurum.ToString(), g11.Surum.ToString());

        // Kullanıcının bilgisayarındaki ayar (17 Kasım): x264 CRF 24 elle kurulmuş, ses WAV 16 bit 48 kHz.
        var ttPc = Varsayilanlar.TikTok();
        ttPc.Surum = 11;
        Varsayilanlar.PlatformSesi(ttPc.Ses);
        var ayarPc = new AppSettings();
        ayarPc.Profiller[ExportProfileId.TikTok] = ttPc;
        var gPc = ayarPc.ProfilAyari(ExportProfileId.TikTok);
        Kontrol("TikTok 11 → 12 (elle kurulmuş x264 CRF 24): video olduğu gibi kalır (yeni varsayılanla aynı)", Json(yeniTt.Video), Json(gPc.Video));
        Kontrol("TikTok 11 → 12 (elle kurulmuş x264 CRF 24): ses FDK AAC 44,1 kHz olur", TikTokSes, SesOzeti(gPc.Ses));

        var ttKendiVideo = Varsayilanlar.TikTokNvenc();
        ttKendiVideo.Surum = 11;
        ttKendiVideo.Video.Nvenc.BitHizi = 20000;
        ttKendiVideo.AnahtarKare.EnBuyuk = 60;
        ttKendiVideo.Ses.WavBit = WavBitDerinligi.Bit24;
        var ayarKendiVideo = new AppSettings();
        ayarKendiVideo.Profiller[ExportProfileId.TikTok] = ttKendiVideo;
        var gKendi = ayarKendiVideo.ProfilAyari(ExportProfileId.TikTok);
        Kontrol("TikTok 11 → 12: kendi NVENC ayarı (CBR 20000) korunur", "Nvenc 20000", $"{gKendi.Video.Kodlayici} {gKendi.Video.Nvenc.BitHizi}");
        Kontrol("TikTok 11 → 12: kendi anahtar karesi (60 kare) korunur", "Kare 60", $"{gKendi.AnahtarKare.Birim} {gKendi.AnahtarKare.EnBuyuk}");
        Kontrol("TikTok 11 → 12: kendi sesi (WAV 24 bit) korunur", "Wav Bit24 48000 EnYuksek Ucgen", SesOzeti(gKendi.Ses));

        var onTt11 = Varsayilanlar.TikTokNvenc();
        onTt11.Surum = 11;
        var gOn = new AppSettings().OnAyariYukle(ExportProfileId.TikTok, onTt11);
        Kontrol("TikTok ön ayarı (sürüm 11) kaydedildiği gibi kalır", "Nvenc Kare 30 " + YeniSes,
            $"{gOn.Video.Kodlayici} {gOn.AnahtarKare.Birim} {gOn.AnahtarKare.EnBuyuk} {SesOzeti(gOn.Ses)}");
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

    /// <summary>
    /// FDK AAC'nin açtığı birleşimler (FdkUyumu; FDK AAC 3.0.0 ile her profil × hız × kanal × bit hızı ölçüldü,
    /// tools/fdk_sinirlari): hız, kanal düzeni, profil ve bit hızı kodlayıcıya hep açılan değerle verilir.
    /// </summary>
    private static void FdkUyumuKontrol()
    {
        Console.WriteLine();
        Console.WriteLine("--- FDK AAC uyumu: 12 hız, kanal düzeni, açılmayan profil, bit hızı sınırı (FDK AAC 3.0.0'da ölçüldü)");
        foreach (var (hz, bek) in new[] { (192000, 96000), (176400, 96000), (88200, 88200), (50000, 48000), (44100, 44100), (7350, 8000), (100, 8000), (384000, 96000), (46050, 48000), (46049, 44100), (46051, 48000) })
        {
            Kontrol($"FDK hızı: {hz} Hz", bek.ToString(), FdkUyumu.Hiz(hz).ToString());
        }

        foreach (var (duzen, kanal, bek) in new (string?, int, string)[]
                 {
                     ("mono", 1, "mono"), ("stereo", 2, "stereo"), ("2.1", 3, "stereo"), ("3.0", 3, "3.0"), ("3.1", 4, "3.0"),
                     ("quad", 4, "5.0"), ("4.0", 4, "4.0"), ("5.1(side)", 6, "5.1"), ("5.1", 6, "5.1"), ("6.0", 6, "7.1"),
                     ("6.1(back)", 7, "6.1(back)"), ("6.1", 7, "7.1"), ("7.1", 8, "7.1"), ("7.1(wide-side)", 8, "7.1(wide)"),
                     ("5.1.4", 10, "5.1.2(back)"), ("7.1.4", 12, "7.1"), ("22.2", 24, "7.1(wide)"), (null, 6, "5.1"), (null, 7, "6.1(back)"),
                     ("10 channels", 10, "7.1"), (null, 1, "mono"),
                 })
        {
            Kontrol($"FDK kanal düzeni: {duzen ?? "bilinmiyor"} ({kanal} kanal)", bek, FdkUyumu.Duzen(duzen, kanal));
        }

        static string K(string profil, int? hz, string? duzen, FdkModu mod = FdkModu.Cbr, int bit = 128)
        {
            var k = FdkUyumu.Hesapla(new FdkAyarlari { Profil = profil, Mod = mod, BitHizi = bit }, hz, duzen);
            return $"{k.Profil} {k.BitHizi?.ToString() ?? "-"}";
        }

        Kontrol("HE-AAC v2, stereo 48 kHz: olduğu gibi", "aac_he_v2 64", K("aac_he_v2", 48000, "stereo", bit: 64));
        Kontrol("HE-AAC v2, mono: HE-AAC (parametrik stereo yalnızca 2 kanal)", "aac_he 48", K("aac_he_v2", 48000, "mono", bit: 48));
        Kontrol("HE-AAC v2, 5.1: HE-AAC", "aac_he 128", K("aac_he_v2", 48000, "5.1"));
        Kontrol("HE-AAC, 11,025 kHz: AAC-LC", "aac_low 64", K("aac_he", 11025, "stereo", bit: 64));
        Kontrol("HE-AAC v2, mono 8 kHz: AAC-LC", "aac_low 32", K("aac_he_v2", 8000, "mono", bit: 32));
        Kontrol("HE-AAC, 16 kHz: olduğu gibi", "aac_he 32", K("aac_he", 16000, "stereo", bit: 32));
        Kontrol("AAC-LD, 7.1: AAC-LC", "aac_low 256", K("aac_ld", 48000, "7.1", bit: 256));
        Kontrol("AAC-ELD, 7.1(wide): AAC-LC", "aac_low 256", K("aac_eld", 48000, "7.1(wide)", bit: 256));
        Kontrol("AAC-LD, 5.1.2(back): çalınamıyor (FFmpeg çözemiyor), AAC-LC", "aac_low 256", K("aac_ld", 48000, "5.1.2(back)", bit: 256));
        Kontrol("AAC-LC 48 kHz stereo 800 kb/s: FDK en çok 576 kodlar", "aac_low 576", K("aac_low", 48000, "stereo", bit: 800));
        Kontrol("AAC-LC 44,1 kHz stereo 800 kb/s: 529", "aac_low 529", K("aac_low", 44100, "stereo", bit: 800));
        Kontrol("AAC-LC 44,1 kHz mono 512 kb/s: 264", "aac_low 264", K("aac_low", 44100, "mono", bit: 512));
        Kontrol("AAC-LC 48 kHz stereo 8 kb/s: FDK en az 14 kodlar", "aac_low 14", K("aac_low", 48000, "stereo", bit: 8));
        Kontrol("HE-AAC 96 kHz 5.1 64 kb/s: 88,2 kHz (96'da çözülemeyen SBR), en az 100", "aac_he 100", K("aac_he", 96000, "5.1", bit: 64));
        Kontrol("HE-AAC v2 48 kHz stereo 256 kb/s: en çok 144", "aac_he_v2 144", K("aac_he_v2", 48000, "stereo", bit: 256));
        Kontrol("AAC-LD mono 96 kHz 800 kb/s: 48 kHz (çalınabilen en yüksek), en çok 576", "aac_ld 576", K("aac_ld", 96000, "mono", bit: 800));
        Kontrol("AAC-ELD mono 64 kHz 700 kb/s: 48 kHz, en çok 576", "aac_eld 576", K("aac_eld", 64000, "mono", bit: 700));
        Kontrol("AAC-LC 128 kb/s: sınır içinde, olduğu gibi", "aac_low 128", K("aac_low", 44100, "stereo"));
        Kontrol("VBR: bit hızı yok", "aac_he -", K("aac_he", 48000, "stereo", FdkModu.Vbr3));
        Kontrol("Hız / düzen bilinmiyor: olduğu gibi", "aac_he_v2 800", K("aac_he_v2", null, null, bit: 800));
        Kontrol("Bilinmeyen profil adı: AAC-LC", "aac_low 128", K("aac_xyz", 48000, "stereo"));
        Kontrol("Not: bit hızı sınırı", "FDK AAC LC 48 kHz stereo sesi en çok 576 kb/s kodlar",
            FdkUyumu.Hesapla(new FdkAyarlari { BitHizi = 800 }, 48000, "stereo").Not ?? "-");
        Kontrol("Not: HE-AAC v2 mono", "HE-AAC v2 yalnızca stereo sesi kodlar: mono seste HE-AAC",
            FdkUyumu.Hesapla(new FdkAyarlari { Profil = "aac_he_v2", BitHizi = 32 }, 48000, "mono").Not ?? "-");

        // Komutlar: kaynağın hızı / kanal düzeni (ses izinin bilgisiyle).
        static MediaInfo Kaynak(int hz, int kanal, string? duzen) => new(1280, 720, 1, 1, 0, 6, true, 30, "bt709", "bt709", "tv")
        {
            VideoCodec = "h264",
            VideoProfil = "High",
            VideoSeviye = 31,
            VideoSuresi = 6,
            SesAkislari = [new SesAkisi(1, "pcm_s24le", hz, kanal, 24, null) { Duzen = duzen }],
        };

        static string Komut(ProfilAyarlari a, MediaInfo m, SesOlcum? olcum = null)
        {
            var ses = ArgsBuilder.SesSec(a.Ses, m, "KAYNAK.mov");
            return " " + string.Join(' ', ArgsBuilder.Olustur(a, m, "KAYNAK.mov", ses, "CIKTI.mov", "GECIS", olcum).Gecisler[0]) + " ";
        }

        var yt = Varsayilanlar.YouTube();
        yt.Ses.OrnekHizi = null;
        var k192 = Kaynak(192000, 2, "stereo");
        var c = Komut(yt, k192);
        Iceriyor("192 kHz kaynak, örnekleme kaynakla aynı, FDK: en yakın FDK hızına (96 kHz) seçilen kaliteyle", c,
            " -af aresample=96000:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac ");
        Iceriyor("192 kHz kaynak, FDK: -ar 96000", c, " -ar 96000 ");
        yt.Ses.Seviye = 6;
        c = Komut(yt, k192);
        Iceriyor("192 kHz kaynak + sınırlayıcı (+6 dB): sınırlayıcıdan sonra 96 kHz (eskiden -ar 192000: FDK açılmazdı)", c,
            ",alimiter=limit=0.891251:level=0:latency=1,aresample=96000:resampler=soxr:precision=33:dither_method=triangular ");
        Icermiyor("192 kHz kaynak + sınırlayıcı: -ar 192000 yok", c, "-ar 192000");
        var wav = Varsayilanlar.YouTube();
        wav.Ses.OrnekHizi = null;
        wav.Ses.Kodlayici = SesKodlayici.Wav;
        wav.Ses.Seviye = 6;
        Iceriyor("192 kHz kaynak + sınırlayıcı, WAV: kaynağın hızı kalır", Komut(wav, k192), " -ar 192000 ");
        var olcumYuksek = new SesOlcum(-30, -12, 5);
        var esit = Varsayilanlar.YouTube();
        esit.Ses.OrnekHizi = null;
        Iceriyor("192 kHz kaynak + eşitleme (sınırlayıcısız): yine 96 kHz", Komut(esit, k192, olcumYuksek), " -ar 96000 ");

        var kaynakKanal = Varsayilanlar.YouTube();
        kaynakKanal.Ses.Kanal = KanalModu.Kaynak;
        c = Komut(kaynakKanal, Kaynak(48000, 6, "5.1(side)"));
        Iceriyor("5.1(side) kaynak, kanal kaynakla aynı, FDK: FDK'nin 5.1 düzenine (açık, ölçekli)", c, " -af aresample=ochl=5.1:rematrix_maxval=1,aresample=48000:");
        Icermiyor("5.1(side) kaynak: -ac yok", c, " -ac ");
        c = Komut(kaynakKanal, Kaynak(48000, 2, "stereo"));
        Icermiyor("Stereo kaynak, kanal kaynakla aynı: düzen değişmez", c, "ochl=");
        c = Komut(kaynakKanal, Kaynak(48000, 8, "7.1"));
        Icermiyor("7.1 kaynak: düzen aynı (FDK'de var)", c, "ochl=");
        kaynakKanal.Ses.Fdk.Profil = "aac_ld";
        kaynakKanal.Ses.Fdk.BitHizi = 256;
        c = Komut(kaynakKanal, Kaynak(48000, 8, "7.1"));
        Iceriyor("7.1 kaynak + AAC-LD: FDK açmaz, AAC-LC", c, " -c:a libfdk_aac -profile:a aac_low -b:a 256k ");
        c = Komut(kaynakKanal, Kaynak(48000, 10, "5.1.4"));
        Iceriyor("5.1.4 kaynak + AAC-LD: 5.1.2(back) düzenine", c, "aresample=ochl=5.1.2(back):rematrix_maxval=1");
        Iceriyor("5.1.4 kaynak + AAC-LD: 5.1.2(back)'ta LD çalınamıyor, AAC-LC", c, " -profile:a aac_low -b:a 256k ");
        kaynakKanal.Ses.Fdk.Profil = "aac_he_v2";
        kaynakKanal.Ses.Fdk.BitHizi = 48;
        Iceriyor("Mono kaynak, kanal kaynakla aynı + HE-AAC v2: HE-AAC", Komut(kaynakKanal, Kaynak(48000, 1, "mono")), " -profile:a aac_he -b:a 48k ");
        var mono = Varsayilanlar.TikTok();
        mono.Ses.Kanal = KanalModu.Mono;
        mono.Ses.Fdk.Profil = "aac_he_v2";
        mono.Ses.Fdk.BitHizi = 40;
        Iceriyor("Kanal mono + HE-AAC v2: HE-AAC", Komut(mono, Kaynak(48000, 2, "stereo")), " -profile:a aac_he -b:a 40k -ar 44100 -ac 1 ");
        var ust = Varsayilanlar.TikTok();
        ust.Ses.Fdk.BitHizi = 800;
        Iceriyor("TikTok, 800 kb/s: komutta FDK'nin gerçek bit hızı (44,1 kHz stereo: 529)", Komut(ust, Kaynak(48000, 2, "stereo")), " -b:a 529k ");
        Kontrol("Tahmini boyutta da gerçek bit hızı (529 kb/s)", "529",
            BoyutTahmini.SesBitHizi(ust.Ses, ArgsBuilder.SesSec(ust.Ses, Kaynak(48000, 2, "stereo"), "KAYNAK.mov")).ToString("0"));
        var notlu = Exporter.Describe(Kaynak(48000, 2, "stereo"), ust, ArgsBuilder.SesSec(ust.Ses, Kaynak(48000, 2, "stereo"), "KAYNAK.mov"));
        Iceriyor("Satırdaki bilgi: bit hızı sınırı", " " + notlu + " ", " · FDK AAC LC 44,1 kHz stereo sesi en çok 529 kb/s kodlar ");
        Kontrol("MediaProbe: kanal düzeni okunur", "5.1(side)",
            MediaProbe.Parse("{\"streams\": [{\"codec_type\": \"video\", \"width\": 64, \"height\": 64}, {\"codec_type\": \"audio\", \"index\": 1, "
                             + "\"codec_name\": \"ac3\", \"sample_rate\": \"48000\", \"channels\": 6, \"channel_layout\": \"5.1(side)\"}], \"format\": {\"duration\": \"1\"}}")
                .SesAkislari[0].Duzen ?? "yok");
    }

    /// <summary>
    /// x264 profili her ayarın önüne geçer (x264 --profile gibi): FFmpeg profili -x264-params'tan önce uyguladığı için
    /// profilin izin vermediği değerler yazılmaz; kayıpsız yalnızca High 4:4:4'te (değilse QP 1 / CRF 1).
    /// </summary>
    private static void X264Profilleri()
    {
        Console.WriteLine();
        Console.WriteLine("--- x264 profili her ayarın önüne geçer (Baseline / Main sınırları, kayıpsız yalnızca High 4:4:4)");
        static MediaInfo M() => new(1280, 720, 1, 1, 0, 6, false, 30, "bt709", "bt709", "tv")
        {
            VideoCodec = "h264",
            VideoProfil = "Constrained Baseline",
            VideoSeviye = 31,
        };

        static string Params(Action<X264Ayarlari> d)
        {
            var a = Varsayilanlar.TikTok();
            d(a.Video.X264);
            var komut = string.Join(' ', ArgsBuilder.Olustur(a, M(), "KAYNAK.mp4", SesSecimi.Yok, "CIKTI.mov", "GECIS").Gecisler[0]);
            var i = komut.IndexOf("-x264-params ", StringComparison.Ordinal);
            return ":" + komut[(i + 13)..].Split(' ')[0] + ":";
        }

        var p = Params(x => { x.Profil = "baseline"; x.BKare = 3; x.AgirlikliP = 2; x.Dct8x8 = true; x.Cqm = "jvt"; x.Gecmeli = true; });
        Icermiyor("Baseline: B-kare yazılmaz", p, ":bframes=");
        Icermiyor("Baseline: ağırlıklı P yazılmaz", p, ":weightp=");
        Icermiyor("Baseline: 8x8 DCT yazılmaz", p, ":8x8dct=");
        Icermiyor("Baseline: özel niceleme matrisi yazılmaz", p, ":cqm=");
        Iceriyor("Baseline: geçmeli yazılmaz (interlaced=0)", p, ":interlaced=0:");
        p = Params(x => { x.Profil = "baseline"; x.BKare = 0; x.AgirlikliP = 0; x.Dct8x8 = false; x.Cqm = "flat"; });
        Iceriyor("Baseline: izin verilen değerler yazılır", p, ":bframes=0:weightp=0:");
        Iceriyor("Baseline: 8x8 DCT kapalı yazılır", p, ":8x8dct=0:cqm=flat:");
        p = Params(x => { x.Profil = "kaynak"; x.BKare = 3; });
        Icermiyor("Profil kaynakla aynı, kaynak Constrained Baseline: B-kare yazılmaz", p, ":bframes=");
        p = Params(x => { x.Profil = "main"; x.BKare = 3; x.Dct8x8 = true; x.Cqm = "jvt"; x.Gecmeli = true; });
        Iceriyor("Main: B-kare ve geçmeli yazılır", p, ":interlaced=1:");
        Iceriyor("Main: B-kare", p, ":bframes=3:");
        Icermiyor("Main: 8x8 DCT yazılmaz", p, ":8x8dct=");
        Icermiyor("Main: özel niceleme matrisi yazılmaz", p, ":cqm=");
        p = Params(x => { x.Profil = "high"; x.Dct8x8 = true; x.Cqm = "jvt"; });
        Iceriyor("High: 8x8 DCT ve matris yazılır", p, ":8x8dct=1:cqm=jvt:");

        p = Params(x => { x.Profil = "main"; x.Mod = X264Modu.SabitQp; x.Qp = 0; });
        Iceriyor("Main + QP 0: kayıpsız yok, QP 1", p, ":qp=1:");
        p = Params(x => { x.Profil = "high"; x.Mod = X264Modu.Crf; x.Crf = 0.5; });
        Iceriyor("High + CRF 0,5 (8 bitte kayıpsız): CRF 1", p, ":crf=1:");
        p = Params(x => { x.Profil = "high"; x.Mod = X264Modu.Crf; x.Crf = 1.5; });
        Iceriyor("High + CRF 1,5: olduğu gibi", p, ":crf=1.5:");
        p = Params(x => { x.Profil = "high10"; x.Mod = X264Modu.Crf; x.Crf = 0; });
        Iceriyor("High 10 + CRF 0 (10 bitte kayıpsız değil): olduğu gibi", p, ":crf=0:");
        p = Params(x => { x.Profil = "high10"; x.Mod = X264Modu.SabitQp; x.Qp = 0; });
        Iceriyor("High 10 + QP 0: QP 1", p, ":qp=1:");
        p = Params(x => { x.Profil = "high444"; x.Mod = X264Modu.SabitQp; x.Qp = 0; });
        Iceriyor("High 4:4:4 + QP 0: kayıpsız kalır", p, ":qp=0:");
        p = Params(x => { x.Profil = string.Empty; x.Mod = X264Modu.Crf; x.Crf = 0; });
        Iceriyor("Profil otomatik + CRF 0: kayıpsız kalır", p, ":crf=0:");
        var a = Varsayilanlar.TikTok();
        a.Video.X264.Profil = "main";
        a.Video.X264.Mod = X264Modu.SabitQp;
        a.Video.X264.Qp = 0;
        Iceriyor("Satırdaki bilgi: kayıpsız yok", " " + Exporter.Describe(M(), a, SesSecimi.Yok) + " ", " · Main profilde kayıpsız yok: QP 1 ");
        // CIF 7,5 fps High@1.1: seviye en çok 2 referans kare taşır. 2 referans + 2 B-kare (piramit: 4 kare) 1.2 ister;
        // Baseline'da B-kare olmadığı için 1.1 yeter.
        var cif = new MediaInfo(352, 288, 1, 1, 0, 2, false, 7.5, "bt709", "bt709", "tv")
        {
            VideoCodec = "h264",
            VideoProfil = "Constrained Baseline",
            VideoSeviye = 11,
        };
        a.Video.X264.Profil = "baseline";
        a.Video.X264.Seviye = "kaynak";
        a.Video.X264.Mod = X264Modu.Crf;
        a.Video.X264.BKare = 2;
        a.Video.X264.Ref = 2;
        Kontrol("Baseline + 2 B-kare, CIF 1.1: B-kare yok, seviye 1.1 yeter", "1.1", ArgsBuilder.X264Seviyesi(a, cif));
        a.Video.X264.Profil = "main";
        Kontrol("Main + 2 B-kare, CIF 1.1: piramit 4 kare, seviye 1.2", "1.2", ArgsBuilder.X264Seviyesi(a, cif));
    }

    /// <summary>
    /// Ses gecikmesi sesi zamanda kaydırır, dosyanın süresini değiştirmez: videonun kendi sesi gecikme verilince dosyanın
    /// süresine tam getirilir (artı gecikmede sonu kesilir, eksi gecikmede sonu sessizlikle dolar; ses tamamen dışarıda
    /// kalsa da ses izi sessiz olarak durur). Gecikme yoksa sese dokunulmaz.
    /// </summary>
    private static void SesGecikmesi()
    {
        Console.WriteLine();
        Console.WriteLine("--- Ses gecikmesi: dosyanın süresi değişmez");
        static MediaInfo M() => new(1280, 720, 1, 1, 0, 6, true, 30, "bt709", "bt709", "tv")
        {
            VideoCodec = "h264",
            VideoProfil = "High",
            VideoSeviye = 31,
            VideoSuresi = 6,
            SesAkislari = [new SesAkisi(1, "pcm_s16le", 48000, 2, 16, null) { Duzen = "stereo" }],
        };

        static string Komut(ProfilAyarlari a)
        {
            var ses = ArgsBuilder.SesSec(a.Ses, M(), "KAYNAK.mov");
            return " " + string.Join(' ', ArgsBuilder.Olustur(a, M(), "KAYNAK.mov", ses, "CIKTI.mov", "GECIS").Gecisler[0]) + " ";
        }

        var a = Varsayilanlar.YouTube();
        a.Ses.Kodlayici = SesKodlayici.Wav;
        a.Ses.Gecikme = 250;
        Iceriyor("+250 ms: sonu kesilir (dosya 6 sn kalır)", Komut(a), " -af adelay=250:all=1,aresample=48000:resampler=soxr:precision=33:dither_method=triangular,apad,atrim=end=6 ");
        a.Ses.Gecikme = -1500;
        Iceriyor("-1500 ms: sonu sessizlikle dolar", Komut(a), " -af atrim=start=1.5,asetpts=PTS-STARTPTS,aresample=48000:resampler=soxr:precision=33:dither_method=triangular,apad,atrim=end=6 ");
        a.Ses.Gecikme = 0;
        Icermiyor("Gecikme yok: sese dokunulmaz", Komut(a), "apad");
        a.Ses.Gecikme = 250;
        var ses = ArgsBuilder.SesSec(a.Ses, M(), "KAYNAK.mov");
        var olc = " " + string.Join(' ', ArgsBuilder.OlcumKomutu(a.Ses, "KAYNAK.mov", ses, 6)) + " ";
        Iceriyor("Ölçüm de aynı uzunlukla", olc, " -af adelay=250:all=1,apad,atrim=end=6,ebur128=");
        var tt = Varsayilanlar.TikTok();
        tt.Ses.Gecikme = -60000;
        var deneme = " " + string.Join(' ', ArgsBuilder.DenemeKodlamaKomutu(tt.Ses, "KAYNAK.mov", ArgsBuilder.SesSec(tt.Ses, M(), "KAYNAK.mov"), 6,
            new SesOlcum(-30, -12, 4), 0, "DENEME.m4a")) + " ";
        Iceriyor("-60 sn (ses tamamen dışarıda): AAC denemesi de dosyanın süresiyle", deneme, ",apad,atrim=end=6 -c:a libfdk_aac ");
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
        Kontrol("NVENC kayıpsız + AQ (ek parametrelerle)",
            "NVENC kayıpsız kodlamada uyarlamalı nicelemeyi (AQ) kabul etmiyor: ek parametrelerdeki -spatial-aq / -temporal-aq'yu kaldırın.",
            HataAciklamasi.Bul("[h264_nvenc @ 0000014df5ff7180] InitializeEncoder failed: invalid param (8): Adaptive quantization is not supported with lossless encoding") ?? "yok");
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

    /// <summary>
    /// 0.13.0: FDK'nin açtığı ama FFmpeg'in AAC çözücüsünün (oynatıcıların ve platformların çoğu) çözemediği birleşimler
    /// çözülen ayara çevrilir; kanal indirme ölçümde ve kodlamada aynı; titreşim yalnızca 16 bitte; ölçüm çıktının hızında.
    /// </summary>
    private static void Calinabilirlik()
    {
        Console.WriteLine();
        Console.WriteLine("--- Çalınabilirlik (0.13.0): FDK'nin her çıktısı FFmpeg'le çözülür; kanal indirme, titreşim, ölçüm hızı");
        static string KH(string profil, int? hz, string? duzen, FdkModu mod = FdkModu.Cbr, int bit = 128)
        {
            var k = FdkUyumu.Hesapla(new FdkAyarlari { Profil = profil, Mod = mod, BitHizi = bit }, hz, duzen);
            return $"{k.Profil} {k.BitHizi?.ToString() ?? "-"} {k.Mod} {k.OrnekHizi?.ToString() ?? "-"}";
        }

        static string N(string profil, int? hz, string? duzen, FdkModu mod = FdkModu.Cbr, int bit = 128) =>
            FdkUyumu.Hesapla(new FdkAyarlari { Profil = profil, Mod = mod, BitHizi = bit }, hz, duzen).Not ?? "-";

        Kontrol("AAC-LD 96 kHz stereo: 48 kHz (FFmpeg LD'yi 22,05–48 kHz'te çözer)", "aac_ld 256 Cbr 48000", KH("aac_ld", 96000, "stereo", bit: 256));
        Kontrol("AAC-LD 8 kHz mono: 22,05 kHz", "aac_ld 32 Cbr 22050", KH("aac_ld", 8000, "mono", bit: 32));
        Kontrol("AAC-LD 16 kHz 5.1: 22,05 kHz", "aac_ld 128 Cbr 22050", KH("aac_ld", 16000, "5.1"));
        Kontrol("AAC-LD 48 kHz stereo: olduğu gibi", "aac_ld 128 Cbr 48000", KH("aac_ld", 48000, "stereo"));
        Kontrol("AAC-LD 22,05 kHz: olduğu gibi (çözülen en düşük hız)", "aac_ld 128 Cbr 22050", KH("aac_ld", 22050, "stereo"));
        Kontrol("AAC-ELD 16 kHz stereo 64 kb/s: 22,05 kHz, SBR'siz en az 80", "aac_eld 80 Cbr 22050", KH("aac_eld", 16000, "stereo", bit: 64));
        Kontrol("AAC-ELD 48 kHz mono 32 kb/s: SBR'siz en az 64", "aac_eld 64 Cbr 48000", KH("aac_eld", 48000, "mono", bit: 32));
        Kontrol("AAC-ELD 48 kHz stereo 64 kb/s: SBR'siz en az 128", "aac_eld 128 Cbr 48000", KH("aac_eld", 48000, "stereo", bit: 64));
        Kontrol("AAC-ELD 32 kHz stereo 96 kb/s: SBR yok, olduğu gibi", "aac_eld 96 Cbr 32000", KH("aac_eld", 32000, "stereo", bit: 96));
        Kontrol("AAC-ELD 44,1 kHz mono VBR 2 (40 kb/s): SBR'siz en düşük kip VBR 4", "aac_eld - Vbr4 44100", KH("aac_eld", 44100, "mono", FdkModu.Vbr2));
        Kontrol("AAC-ELD 24 kHz stereo VBR 1 (40 kb/s): VBR 3 (96 kb/s)", "aac_eld - Vbr3 24000", KH("aac_eld", 24000, "stereo", FdkModu.Vbr1));
        Kontrol("AAC-ELD 48 kHz mono VBR 5: olduğu gibi", "aac_eld - Vbr5 48000", KH("aac_eld", 48000, "mono", FdkModu.Vbr5));
        Kontrol("AAC-ELD 48 kHz 5.1: çok kanalda SBR yok, olduğu gibi", "aac_eld 128 Cbr 48000", KH("aac_eld", 48000, "5.1"));
        Kontrol("AAC-ELD 96 kHz 5.1.2(back): önce AAC-LC (düzen), hız kalır", "aac_low 256 Cbr 96000", KH("aac_eld", 96000, "5.1.2(back)", bit: 256));
        Kontrol("AAC-LD, hız bilinmiyor: olduğu gibi", "aac_ld 128 Cbr -", KH("aac_ld", null, "stereo"));
        Kontrol("HE-AAC 96 kHz 3.0 110 kb/s: 88,2 kHz", "aac_he 110 Cbr 88200", KH("aac_he", 96000, "3.0", bit: 110));
        Kontrol("HE-AAC 96 kHz stereo: olduğu gibi (mono / stereo çözülüyor)", "aac_he 64 Cbr 96000", KH("aac_he", 96000, "stereo", bit: 64));
        Kontrol("HE-AAC 88,2 kHz 5.1: olduğu gibi", "aac_he 200 Cbr 88200", KH("aac_he", 88200, "5.1", bit: 200));
        Kontrol("HE-AAC v2 96 kHz stereo: olduğu gibi", "aac_he_v2 64 Cbr 96000", KH("aac_he_v2", 96000, "stereo", bit: 64));
        Kontrol("Not: AAC-LD hızı", "AAC-LD yalnızca 22,05–48 kHz'te çalınabiliyor: 48 kHz", N("aac_ld", 96000, "stereo"));
        Kontrol("Not: AAC-ELD hızı ve SBR", "AAC-ELD yalnızca 22,05–48 kHz'te çalınabiliyor: 22,05 kHz; AAC-ELD stereo seste 64 kb/s'de SBR kullanıyor (çalınamıyor): 80 kb/s",
            N("aac_eld", 16000, "stereo", bit: 64));
        Kontrol("Not: AAC-ELD VBR", "AAC-ELD mono seste VBR 2 SBR kullanıyor (çalınamıyor): VBR 4", N("aac_eld", 44100, "mono", FdkModu.Vbr2));
        Kontrol("Not: HE-AAC 96 kHz çok kanal", "HE-AAC 96 kHz'te çok kanallı seste standarda uymayan SBR üretebiliyor (çözülemiyor): 88,2 kHz",
            N("aac_he", 96000, "5.1", bit: 200));
        Kontrol("Not: AAC-LD 5.1.2(back)", "AAC-LD 5.1.2(back) sesi çalınamıyor (FFmpeg çözemiyor): AAC-LC", N("aac_ld", 48000, "5.1.2(back)", bit: 256));
        Kontrol("Not: AAC-LC 48 kHz stereo 128: yok", "-", N("aac_low", 48000, "stereo"));

        static string E(int hz, int kanal) => FdkUyumu.EldSbrsizEnAz(hz, kanal)?.ToString() ?? "yok";
        foreach (var (hz, kanal, bek) in new[]
                 {
                     (48000, 1, "64000"), (44100, 1, "64000"), (32000, 1, "56000"), (24000, 1, "40000"), (22050, 1, "40000"), (16000, 1, "28000"),
                     (48000, 2, "128000"), (44100, 2, "128000"), (32000, 2, "96000"), (24000, 2, "80000"), (22050, 2, "80000"), (16000, 2, "64000"),
                     (12000, 1, "yok"), (8000, 2, "yok"), (64000, 2, "yok"), (96000, 1, "yok"), (48000, 6, "yok"),
                 })
        {
            Kontrol($"AAC-ELD SBR'siz en az: {hz} Hz, {kanal} kanal (FDK eldSbrAutoConfigTab)", bek, E(hz, kanal));
        }

        foreach (var (mod, duzen, bek) in new[]
                 {
                     (FdkModu.Vbr1, "mono", 32000), (FdkModu.Vbr3, "mono", 56000), (FdkModu.Vbr5, "mono", 112000),
                     (FdkModu.Vbr1, "stereo", 40000), (FdkModu.Vbr2, "stereo", 64000), (FdkModu.Vbr4, "stereo", 128000), (FdkModu.Vbr5, "stereo", 192000),
                     (FdkModu.Vbr3, "5.1", 240000), (FdkModu.Vbr1, "7.1", 140000), (FdkModu.Vbr2, "6.1(back)", 192000), (FdkModu.Vbr4, "3.0", 192000),
                 })
        {
            Kontrol($"FDK VBR bit hızı: {mod} {duzen} (configTabVBR × etkin kanal)", bek.ToString(), FdkUyumu.VbrBitHizi(mod, duzen).ToString());
        }

        // Komutlar.
        static MediaInfo M(int hz, int kanal, string? duzen) => new(1280, 720, 1, 1, 0, 6, true, 30, "bt709", "bt709", "tv")
        {
            VideoCodec = "h264",
            VideoProfil = "High",
            VideoSeviye = 31,
            VideoSuresi = 6,
            SesAkislari = [new SesAkisi(1, "pcm_s24le", hz, kanal, 24, null) { Duzen = duzen }],
        };

        static string Komut(ProfilAyarlari a, MediaInfo m, SesOlcum? olcum = null)
        {
            var ses = ArgsBuilder.SesSec(a.Ses, m, "KAYNAK.mov");
            return " " + string.Join(' ', ArgsBuilder.Olustur(a, m, "KAYNAK.mov", ses, "CIKTI.mov", "GECIS", olcum).Gecisler[0]) + " ";
        }

        static string Olcum(ProfilAyarlari a, MediaInfo m)
        {
            var ses = ArgsBuilder.SesSec(a.Ses, m, "KAYNAK.mov");
            return " " + string.Join(' ', ArgsBuilder.OlcumKomutu(a.Ses, "KAYNAK.mov", ses, 6)) + " ";
        }

        var be = M(48000, 6, "5.1");
        var yt = Varsayilanlar.YouTube();
        Iceriyor("5.1 → stereo: açık ve ölçekli indirme (-ac 2'nin 16 bitteki hâli)", Komut(yt, be),
            " -af aresample=ochl=stereo:rematrix_maxval=1,aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac ");
        Iceriyor("5.1 → stereo: ölçüm de aynı indirmeyle", Olcum(yt, be), " -af aresample=ochl=stereo:rematrix_maxval=1,ebur128=");
        Iceriyor("5.1 → stereo + eşitleme: önce indirme, sonra kazanç", Komut(yt, be, new SesOlcum(-25, -20, 5)),
            " -af aresample=ochl=stereo:rematrix_maxval=1,volume=11.0dB,aresample=48000:");
        var ytMono = Varsayilanlar.YouTube();
        ytMono.Ses.Kanal = KanalModu.Mono;
        Iceriyor("5.1 → mono: açık ve ölçekli", Komut(ytMono, be), " -af aresample=ochl=mono:rematrix_maxval=1,aresample=48000:");
        Iceriyor("Stereo → mono: yarım + yarım (pan)", Komut(ytMono, M(48000, 2, "stereo")), " -af pan=mono|c0=0.5*c0+0.5*c1,aresample=48000:");
        var ytWav = Varsayilanlar.YouTube();
        ytWav.Ses.Kodlayici = SesKodlayici.Wav;
        ytWav.Ses.Kanal = KanalModu.Kaynak;
        Icermiyor("WAV, kanal kaynakla aynı (5.1): düzen değişmez", Komut(ytWav, be), "ochl=");

        var ld = Varsayilanlar.YouTube();
        ld.Ses.Fdk.Profil = "aac_ld";
        ld.Ses.OrnekHizi = null;
        var k96 = M(96000, 2, "stereo");
        Iceriyor("AAC-LD, 96 kHz kaynak, hız kaynakla aynı: 48 kHz'e", Komut(ld, k96),
            " -af aresample=48000:resampler=soxr:precision=33:dither_method=triangular -c:a libfdk_aac -profile:a aac_ld -b:a 128k ");
        Iceriyor("AAC-LD, 96 kHz kaynak: -ar 48000", Komut(ld, k96), " -ar 48000 ");
        Iceriyor("AAC-LD, 96 kHz kaynak: ölçüm çıktının hızında (48 kHz)", Olcum(ld, k96), " -af aresample=48000:resampler=soxr:precision=33,ebur128=");
        ld.Ses.OrnekHizi = 8000;
        Iceriyor("AAC-LD, 8 kHz seçili: 22,05 kHz'e", Komut(ld, M(48000, 2, "stereo")), " -ar 22050 ");
        ld.Ses.OrnekHizi = 48000;
        Iceriyor("AAC-LD, 48 kHz: olduğu gibi", Komut(ld, M(48000, 2, "stereo")), " -profile:a aac_ld -b:a 128k -ar 48000 ");
        var eld = Varsayilanlar.YouTube();
        eld.Ses.Fdk.Profil = "aac_eld";
        eld.Ses.Fdk.Mod = FdkModu.Vbr2;
        eld.Ses.Kanal = KanalModu.Mono;
        eld.Ses.OrnekHizi = 44100;
        Iceriyor("AAC-ELD mono 44,1 kHz VBR 2: komutta VBR 4", Komut(eld, M(48000, 2, "stereo")), " -profile:a aac_eld -vbr 4 ");
        eld.Ses.Fdk.Mod = FdkModu.Cbr;
        eld.Ses.Fdk.BitHizi = 24;
        Iceriyor("AAC-ELD mono 44,1 kHz 24 kb/s: 64 kb/s", Komut(eld, M(48000, 2, "stereo")), " -profile:a aac_eld -b:a 64k ");
        Kontrol("AAC-ELD: tahmini boyutta da 64 kb/s", "64",
            BoyutTahmini.SesBitHizi(eld.Ses, ArgsBuilder.SesSec(eld.Ses, M(48000, 2, "stereo"), "KAYNAK.mov")).ToString("0"));
        var he = Varsayilanlar.YouTube();
        he.Ses.Fdk.Profil = "aac_he";
        he.Ses.Fdk.BitHizi = 200;
        he.Ses.Kanal = KanalModu.Kaynak;
        he.Ses.OrnekHizi = null;
        Iceriyor("HE-AAC 96 kHz 5.1: 88,2 kHz", Komut(he, M(96000, 6, "5.1")), " -ar 88200 ");
        Iceriyor("HE-AAC 96 kHz stereo: 96 kHz kalır", Komut(he, k96), " -profile:a aac_he -b:a 200k ");
        Icermiyor("HE-AAC 96 kHz stereo: dönüşüm yok", Komut(he, k96), " -ar ");
        var notlu = Exporter.Describe(k96, ld, ArgsBuilder.SesSec(ld.Ses, k96, "KAYNAK.mov"));
        Icermiyor("Satırdaki bilgi: AAC-LD 48 kHz seçiliyken not yok", " " + notlu + " ", "çalınabiliyor");
        ld.Ses.OrnekHizi = null;
        notlu = Exporter.Describe(k96, ld, ArgsBuilder.SesSec(ld.Ses, k96, "KAYNAK.mov"));
        Iceriyor("Satırdaki bilgi: AAC-LD hız notu", " " + notlu + " ", " · AAC-LD yalnızca 22,05–48 kHz'te çalınabiliyor: 48 kHz");
        Kontrol("Notlar: AAC-LD hız notu (bitince ipucunda)", "AAC-LD yalnızca 22,05–48 kHz'te çalınabiliyor: 48 kHz",
            string.Join(" | ", Exporter.Notlar(k96, ld, ArgsBuilder.SesSec(ld.Ses, k96, "KAYNAK.mov"))));
        Kontrol("Çıktının hızı: AAC-LD, 96 kHz kaynak", "48000",
            ArgsBuilder.CiktiHizi(ld.Ses, ArgsBuilder.SesSec(ld.Ses, k96, "KAYNAK.mov"))?.ToString() ?? "yok");
        var wavKaynak = Varsayilanlar.YouTube();
        wavKaynak.Ses.Kodlayici = SesKodlayici.Wav;
        wavKaynak.Ses.OrnekHizi = null;
        Kontrol("Çıktının hızı: WAV, hız kaynakla aynı", "96000",
            ArgsBuilder.CiktiHizi(wavKaynak.Ses, ArgsBuilder.SesSec(wavKaynak.Ses, k96, "KAYNAK.mov"))?.ToString() ?? "yok");
        Kontrol("Çıktının hızı: WAV, 48 kHz seçili", "48000",
            ArgsBuilder.CiktiHizi(ytWav.Ses, ArgsBuilder.SesSec(ytWav.Ses, k96, "KAYNAK.mov"))?.ToString() ?? "yok");

        // Titreşim yalnızca 16 bit çıktıda (FFmpeg'in Shibata titreşimi 24 / 32 bitte sesi bozuyor).
        foreach (var (bit, titresim, codec, titresimli) in new[]
                 {
                     (WavBitDerinligi.Bit16, Titresim.Shibata, "pcm_s16le", true),
                     (WavBitDerinligi.Bit16, Titresim.Ucgen, "pcm_s16le", true),
                     (WavBitDerinligi.Bit24, Titresim.Shibata, "pcm_s24le", false),
                     (WavBitDerinligi.Bit24, Titresim.UcgenYuksekGecis, "pcm_s24le", false),
                     (WavBitDerinligi.Bit32, Titresim.Shibata, "pcm_s32le", false),
                     (WavBitDerinligi.Bit32, Titresim.Ucgen, "pcm_s32le", false),
                     (WavBitDerinligi.Float32, Titresim.Shibata, "pcm_f32le", false),
                 })
        {
            var w = Varsayilanlar.YouTube();
            w.Ses.Kodlayici = SesKodlayici.Wav;
            w.Ses.WavBit = bit;
            w.Ses.Titresim = titresim;
            var komut = Komut(w, M(44100, 2, "stereo"));
            Iceriyor($"WAV {bit} + {titresim}: {codec}", komut, " -c:a " + codec + " ");
            if (titresimli)
            {
                Iceriyor($"WAV {bit} + {titresim}: titreşim var", komut, ":dither_method=");
            }
            else
            {
                Icermiyor($"WAV {bit} + {titresim}: titreşim yok (24 / 32 bitte gereksiz; Shibata sesi bozuyordu)", komut, "dither_method");
                Iceriyor($"WAV {bit} + {titresim}: dönüşüm yine en yüksek kalitede", komut, " -af aresample=48000:resampler=soxr:precision=33 -c:a ");
            }

            Kontrol($"TitresimVar: WAV {bit} + {titresim}", titresimli.ToString(), ArgsBuilder.TitresimVar(w.Ses).ToString());
        }

        var fdkShibata = Varsayilanlar.YouTube();
        fdkShibata.Ses.Titresim = Titresim.Shibata;
        Iceriyor("FDK AAC (16 bit girer) + Shibata: titreşim var", Komut(fdkShibata, M(44100, 2, "stereo")), ":dither_method=shibata -c:a libfdk_aac ");
        Kontrol("TitresimVar: FDK + titreşim yok", "False",
            ArgsBuilder.TitresimVar(new SesAyarlari { Kodlayici = SesKodlayici.FdkAac, Titresim = Titresim.Yok }).ToString());

        // DRC: çözücü sesi değiştirir; karar ve tepe koruması çözülen sesle.
        var drc = Varsayilanlar.TikTok().Ses;
        Kontrol("DRC: varsayılanda yok", "False", Esitleme.DrcVar(drc).ToString());
        drc.Fdk.DrcProfili = 0;
        Kontrol("DRC: profil 0 (yok)", "False", Esitleme.DrcVar(drc).ToString());
        drc.Fdk.DrcProfili = 3;
        Kontrol("DRC: müzik · standart", "True", Esitleme.DrcVar(drc).ToString());
        drc.Kodlayici = SesKodlayici.Wav;
        Kontrol("DRC: WAV'da yok", "False", Esitleme.DrcVar(drc).ToString());

        var tts = Varsayilanlar.TikTok().Ses;
        // AAC (DRC) ve sınırlayıcısız deneme: kazanç çıktıdaki hâlinden.
        var r = Esitleme.KararVer(tts, new SesOlcum(-34.8, -14.2, 20), kararYukseklik: -34.8) with { KaynakYukseklik = -21.7 };
        Kontrol("Karar çıktıdaki hâliyle (-34,8): yükseltilir", "Dogrusal", r.Karar.ToString());
        Iceriyor("Ayrıntı: kaynağın ölçümü", r.Ayrinti, "Ölçülen: -21,7 LUFS · tepe -14,2 dBTP · ses aralığı 20,0 LU");
        Iceriyor("Ayrıntı: çıktıdaki hâli ve karar", r.Ayrinti, "Çıktıdaki hâli -34,8 LUFS (ses kodlamadaki zinciriyle denenip ölçüldü): karar bununla verildi.");
        Iceriyor("Bilgi satırı: kazanç çıktıdaki hâline göre (+20,8)", r.BilgiMetni, "-34,8 → -14 LUFS (bütün ses +20,8 dB");
        var r2 = Esitleme.KararVer(tts, new SesOlcum(-14.1, -3, 5)) with { KaynakYukseklik = -14.12 };
        Icermiyor("Ayrıntı: kaynak ile çıktı aynıysa (0,05'ten az) tek satır", r2.Ayrinti, "Çıktıdaki hâli");
        Kontrol("Karar: çıktıdaki hâli -14,1 → hedefte", "Hedefte", r2.Karar.ToString());
        // Sınırlayıcılı deneme: karar çıktıdaki hâliyle, kazanç kaynağın ölçümünden (sınırlayıcının kıstığı kazançla değişir).
        var r3 = Esitleme.KararVer(tts, new SesOlcum(9.8, 20.8, 20), kararYukseklik: -23.9) with { KaynakYukseklik = 9.8 };
        Kontrol("Karar: kaynak 9,8 LUFS ama çıktıda -23,9 (sınırlayıcı): eşitlenir", "Dogrusal", r3.Karar.ToString());
        Iceriyor("Bilgi satırı: kazanç kaynağın ölçümünden (9,8 → -14: -23,8 dB)", r3.BilgiMetni, "9,8 → -14 LUFS (bütün ses -23,8 dB");
        Iceriyor("Ayrıntı: karar çıktıdaki hâliyle", r3.Ayrinti, "Çıktıdaki hâli -23,9 LUFS (ses kodlamadaki zinciriyle denenip ölçüldü): karar bununla verildi.");
        Iceriyor("Ayrıntı: kısıldı", r3.Ayrinti, "Karar: bütün ses aynı miktarda -23,8 dB kısıldı");
        var wav40 = Varsayilanlar.YouTube().Ses;
        wav40.Kodlayici = SesKodlayici.Wav;
        wav40.EsitlemeHedef = -40;
        wav40.EsitlemeYonu = EsitlemeYonu.IkiYon;
        var r4 = Esitleme.KararVer(wav40, new SesOlcum(-20.9, -0.8, 20), kararYukseklik: -25.9) with { KaynakYukseklik = -20.9 };
        Kontrol("Kısma (hedef -40, sınırlayıcı -12'de denendi: -25,9): eşitlenir", "Dogrusal", r4.Karar.ToString());
        Iceriyor("Kısma: kazanç kaynağın ölçümünden (-19,1; eskiden -14,1 ve ses -35'te kalıyordu)", r4.BilgiMetni, "-20,9 → -40 LUFS (bütün ses -19,1 dB");
        var r5 = Esitleme.KararVer(tts, new SesOlcum(-13.0, -1, 5), kararYukseklik: -14.2);
        Kontrol("Kaynak -13 ama çıktıda -14,2: hedefte", "Hedefte", r5.Karar.ToString());
        Kontrol("Hedefte: bilgi satırı çıktıdaki hâliyle", "-14,2 LUFS · zaten hedefte, dokunulmuyor", r5.BilgiMetni);
        Kontrol("Hedefte: biten satır çıktıdaki hâliyle", "-14,2 LUFS (dokunulmadı)", r5.SatirMetni ?? "yok");
        var r6 = Esitleme.KararVer(tts, new SesOlcum(-15.5, -3, 5), kararYukseklik: -12.6);
        Kontrol("Kaynak -15,5 ama çıktıda -12,6: zaten yüksek", "ZatenYuksek", r6.Karar.ToString());
        Kontrol("Zaten yüksek: biten satır çıktıdaki hâliyle", "-12,6 LUFS (dokunulmadı)", r6.SatirMetni ?? "yok");
        var kisikRapor = Esitleme.KararVer(wav40, new SesOlcum(-68.28, -60, 0)) with { Telafi = -0.37, KisikOlcum = true };
        Iceriyor("Çok kısık ses: ölçerin kapısı için düzeltme yazılır", kisikRapor.Ayrinti,
            "Ses çok kısık olduğu için (ölçer -70 LUFS'un altındaki bölümleri saymaz) yükseklik çıktının seviyesinde yeniden ölçüldü: kazanç -0,4 dB düzeltildi.");
        Kontrol("Çok kısık sınırı: -45 LUFS", "-45", Esitleme.KisikSinir.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>0.13.0: x264'ün açılmadığı ayarlar (geçmeli yükseklik, matris QP, düşük 2 geçiş), kırpma, tahmini boyut.</summary>
    private static void Sinirlar013()
    {
        Console.WriteLine();
        Console.WriteLine("--- x264'ün açılmadığı ayarlar, çok dar boyut, tahmini boyut (0.13.0)");
        static MediaInfo V(int w, int h, double sure = 10, double fps = 30) => new(w, h, 1, 1, 0, sure, false, fps, "bt709", "bt709", "tv")
        {
            VideoCodec = "h264",
            VideoProfil = "High",
            VideoSeviye = 40,
            VideoSuresi = sure,
        };

        static string Komut(ProfilAyarlari a, MediaInfo m) =>
            " " + string.Join(' ', ArgsBuilder.Olustur(a, m, "KAYNAK.mov", SesSecimi.Yok, "CIKTI.mov", "GECIS").Gecisler[0]) + " ";

        static ProfilAyarlari Gecmeli(string profil, int w = 1080, int h = 1350)
        {
            var a = Varsayilanlar.TikTok();
            a.Video.Cozunurluk = CozunurlukModu.Hedef;
            a.Video.Genislik = w;
            a.Video.Yukseklik = h;
            a.Video.X264.Profil = profil;
            a.Video.X264.Gecmeli = true;
            return a;
        }

        var hd = V(1280, 720);
        Iceriyor("Geçmeli + High (4:2:0), 1080×1350: yükseklik 4'ün katı (1348)", Komut(Gecmeli("high"), hd), "scale=1080:1348:");
        Kontrol("Geçmeli + High: çıktı boyutu", "1080×1348", $"{ArgsBuilder.CiktiBoyutu(Gecmeli("high"), hd).Genislik}×{ArgsBuilder.CiktiBoyutu(Gecmeli("high"), hd).Yukseklik}");
        Kontrol("Geçmeli + High: not", "geçmeli 4:2:0'da yükseklik 4'ün katı: 1350 → 1348", ArgsBuilder.YukseklikNotu(Gecmeli("high"), hd) ?? "yok");
        Kontrol("Geçmeli + High: Notlar", "geçmeli 4:2:0'da yükseklik 4'ün katı: 1350 → 1348", string.Join(" | ", Exporter.Notlar(hd, Gecmeli("high"), SesSecimi.Yok)));
        Iceriyor("Geçmeli + High: satırdaki bilgi", " " + Exporter.Describe(hd, Gecmeli("high"), SesSecimi.Yok) + " ", " · geçmeli 4:2:0'da yükseklik 4'ün katı: 1350 → 1348 ");
        Iceriyor("Geçmeli + High 10 (4:2:0): 1348", Komut(Gecmeli("high10"), hd), "scale=1080:1348:");
        Iceriyor("Geçmeli + High 4:2:2: 1350 kalır (4:2:2'de 2'nin katı yeter)", Komut(Gecmeli("high422"), hd), "scale=1080:1350:");
        Kontrol("Geçmeli + High 4:2:2: not yok", "yok", ArgsBuilder.YukseklikNotu(Gecmeli("high422"), hd) ?? "yok");
        Iceriyor("Geçmeli + High 4:4:4: 1350 kalır", Komut(Gecmeli("high444"), hd), "scale=1080:1350:");
        Iceriyor("Geçmeli + Baseline: geçmeli yok, 1350 kalır", Komut(Gecmeli("baseline"), hd), "scale=1080:1350:");
        Iceriyor("Geçmeli + High, 1080×1920 (zaten 4'ün katı)", Komut(Gecmeli("high", 1080, 1920), hd), "scale=1080:1920:");
        Kontrol("Geçmeli + High, 1080×1920: not yok", "yok", ArgsBuilder.YukseklikNotu(Gecmeli("high", 1080, 1920), hd) ?? "yok");
        var ilerleyen = Gecmeli("high");
        ilerleyen.Video.X264.Gecmeli = false;
        Iceriyor("İlerleyen (geçmeli değil): 1350 kalır", Komut(ilerleyen, hd), "scale=1080:1350:");
        var kenar = Gecmeli("high");
        kenar.Video.Oran = OranUyumu.KenarEkle;
        Iceriyor("Geçmeli + kenar ekle: dolgu 1348", Komut(kenar, hd), ",pad=1080:1348:-1:-1:color=black,");
        var kaynakBoyut = Gecmeli("high");
        kaynakBoyut.Video.Cozunurluk = CozunurlukModu.Kaynak;
        Iceriyor("Geçmeli + boyut kaynakla aynı: 4'ün katına", Komut(kaynakBoyut, V(1280, 722)), "scale=trunc(iw*sar/2)*2:trunc(ih/4)*4:");
        Kontrol("Geçmeli + boyut kaynakla aynı (1280×722): 1280×720", "1280×720",
            $"{ArgsBuilder.CiktiBoyutu(kaynakBoyut, V(1280, 722)).Genislik}×{ArgsBuilder.CiktiBoyutu(kaynakBoyut, V(1280, 722)).Yukseklik}");
        Kontrol("Geçmeli + boyut kaynakla aynı (722): not", "geçmeli 4:2:0'da yükseklik 4'ün katı: 722 → 720", ArgsBuilder.YukseklikNotu(kaynakBoyut, V(1280, 722)) ?? "yok");
        var nvencGecmeli = Gecmeli("high");
        nvencGecmeli.Video.Kodlayici = VideoKodlayici.Nvenc;
        Kontrol("NVENC: x264'ün geçmeli ayarı geçerli değil (2'nin katı)", "2", ArgsBuilder.YukseklikKati(nvencGecmeli, hd).ToString());

        // Çok büyük küçültme: FFmpeg'in ölçekleyicisi bir adımda en çok 256 dokunuş (ör. 576 → 16 Spline'da açılmıyordu).
        static string Ara(int kw, int kh, int hw, int hh, string olcek, bool korunur) =>
            string.Join(" ", ArgsBuilder.AraOlcekler(kw, kh, hw, hh, olcek, korunur).Select(x => $"{x.W}x{x.H}")) is { Length: > 0 } t ? t : "yok";
        Kontrol("Ara ölçek: Spline 720×576 → 16×1080 (esnet): genişlik önce 128'e", "128x576", Ara(720, 576, 16, 1080, "spline", false));
        Kontrol("Ara ölçek: Area 8192 → 16 (oran 512): önce 128'e", "128x2160", Ara(8192, 2160, 16, 2160, "area", false));
        Kontrol("Ara ölçek: Bicubic 576 → 16 (145 dokunuş): gerekmez", "yok", Ara(720, 576, 720, 16, "bicubic", false));
        Kontrol("Ara ölçek: Bicubic 1080 → 16 (271 dokunuş): 128'e", "1920x128", Ara(1920, 1080, 1920, 16, "bicubic", false));
        Kontrol("Ara ölçek: Lanczos 1920×1080 → 1080×1920: gerekmez", "yok", Ara(1920, 1080, 1080, 1920, "lanczos", false));
        Kontrol("Ara ölçek: en yakın komşu: gerekmez", "yok", Ara(8192, 8192, 16, 16, "neighbor", false));
        Kontrol("Ara ölçek: oran korunur (kırpma 330×576 → 16×28): ikisi aynı oranla", "128x224", Ara(330, 576, 16, 28, "spline", true));
        Kontrol("Ara ölçek: Lanczos 480×270 → 16×8192 (30 kat küçültme, 30 kat büyütme): önce 128", "128x270", Ara(480, 270, 16, 8192, "lanczos", false));
        Kontrol("Ara ölçek: Lanczos 128 → 16 (8 kat): gerekmez", "yok", Ara(128, 270, 16, 8192, "lanczos", false));
        Kontrol("Ara ölçek: Spline 320×180 → 8192×16 (11,25 kat): yükseklik önce 128", "320x128", Ara(320, 180, 8192, 16, "spline", false));
        var dar = Varsayilanlar.TikTok();
        dar.Video.Cozunurluk = CozunurlukModu.Hedef;
        dar.Video.Genislik = 16;
        dar.Video.Yukseklik = 1080;
        dar.Video.Oran = OranUyumu.Esnet;
        dar.Video.Olcekleme = "spline";
        var dk = Komut(dar, V(720, 576));
        Iceriyor("Spline 16×1080 (esnet): önce Area ile 128×576", dk, "scale=128:576:flags=area");
        Iceriyor("Spline 16×1080: son adım Spline", dk, "scale=128:576:flags=area" + (dar.Video.HassasYuvarlama ? "+accurate_rnd+full_chroma_int" : string.Empty)
                                                         + ",scale=16:1080:flags=spline");
        dar.Video.Olcekleme = "bicubic";
        Icermiyor("Bicubic 16×1080 (720 → 16: 181 dokunuş): ara ölçek yok", Komut(dar, V(720, 576)), "flags=area");

        // Çok dar hedefte kırpma genişliği / yüksekliği en az 2 (eskiden 0 çıkıp ffmpeg duruyordu).
        static string P(CropPlan p) => $"{p.Width}x{p.Height}+{p.X}+{p.Y}";
        var kucuk = V(320, 180);
        Kontrol("Kırpma: 320×180 → 16:8192 (çok dar): genişlik 2", "2x180+159+0", P(ArgsBuilder.PlanCrop(kucuk, 16.0 / 8192)));
        Kontrol("Kırpma: 320×180 → 8192:16 (çok yassı): yükseklik 2", "320x2+0+89", P(ArgsBuilder.PlanCrop(kucuk, 8192.0 / 16)));
        Kontrol("Kırpma: 1920×1080 → 9:16 (eskisi gibi)", "608x1080+656+0", P(ArgsBuilder.PlanCrop(V(1920, 1080), 9.0 / 16)));

        // x264'ün açılışta reddettiği ama düzeltilebilen ayarlar (kodlarken, x264'ün iletisindeki değerle).
        var iki = Varsayilanlar.YouTube();
        iki.Video.X264.Mod = X264Modu.IkiGecis;
        iki.Video.X264.BitHizi = 1;
        var not = ArgsBuilder.X264Duzelt("[libx264 @ 0x1] requested bitrate is too low. estimated minimum is 43 kbps\n", iki);
        Kontrol("2 geçiş, bit hızı çok düşük: x264'ün en azı + %2 (43 → 45)", "45", iki.Video.X264.BitHizi.ToString());
        Kontrol("2 geçiş, bit hızı çok düşük: not", "1 kb/s bu videoda 2 geçişle kodlanamıyor (x264: en az 43 kb/s): 45 kb/s", not ?? "yok");
        var uc = Varsayilanlar.YouTube();
        uc.Video.X264.Mod = X264Modu.UcGecis;
        uc.Video.X264.BitHizi = 4000;
        not = ArgsBuilder.X264Duzelt("requested bitrate is too low. estimated minimum is 5012 kbps", uc);
        Kontrol("3 geçiş, 4 Mb/s, en az 5012: 5114", "5114", uc.Video.X264.BitHizi.ToString());
        Kontrol("3 geçiş: not", "4 Mb/s bu videoda 3 geçişle kodlanamıyor (x264: en az 5012 kb/s): 5114 kb/s", not ?? "yok");
        var crf = Varsayilanlar.YouTube();
        Kontrol("CRF'de bu ileti: düzeltilmez", "yok", ArgsBuilder.X264Duzelt("requested bitrate is too low. estimated minimum is 43 kbps", crf) ?? "yok");
        Kontrol("CRF'de: bit hızı değişmedi", Varsayilanlar.YouTube().Video.X264.BitHizi.ToString(), crf.Video.X264.BitHizi.ToString());
        var jvt = Varsayilanlar.YouTube();
        jvt.Video.X264.Cqm = "jvt";
        jvt.Video.X264.QpMax = 0;
        not = ArgsBuilder.X264Duzelt("[libx264 @ 0x1] Impossible QP constraints for CQM (min=19, max=0)", jvt);
        Kontrol("JVT + qpmax 0: qpmax x264'ün en azına (19)", "19", jvt.Video.X264.QpMax?.ToString() ?? "yok");
        Kontrol("JVT + qpmax 0: not", "JVT nicemleme matrisiyle en yüksek QP en az 19 olabilir (x264): qpmax 0 → 19", not ?? "yok");
        var jvtBos = Varsayilanlar.YouTube();
        jvtBos.Video.X264.Cqm = "jvt";
        jvtBos.Video.X264.QpMax = null;
        Kontrol("JVT, qpmax boş: düzeltilecek bir şey yok", "yok", ArgsBuilder.X264Duzelt("Impossible QP constraints for CQM (min=7, max=6)", jvtBos) ?? "yok");
        var nv = Varsayilanlar.YouTube();
        nv.Video.Kodlayici = VideoKodlayici.Nvenc;
        Kontrol("NVENC: x264 düzeltmesi yok", "yok", ArgsBuilder.X264Duzelt("requested bitrate is too low. estimated minimum is 43 kbps", nv) ?? "yok");

        // 2 / 3 geçiş, en yüksek bit hızı ortalamadan düşük: x264 CBR sayar, kodlanan bit hızı en yüksek bit hızıdır.
        var cbrSay = Varsayilanlar.YouTube();
        cbrSay.Video.X264.Mod = X264Modu.UcGecis;
        cbrSay.Video.X264.BitHizi = 4000;
        cbrSay.Video.X264.VbvMaks = 100;
        not = ArgsBuilder.X264Duzelt("[libx264 @ 0x1] max bitrate less than average bitrate, assuming CBR\n"
                                     + "[libx264 @ 0x1] requested bitrate is too low. estimated minimum is 213 kbps", cbrSay);
        Kontrol("CBR sayılan 3 geçiş (4 Mb/s, en yüksek 100): en yüksek bit hızı 219'a", "219/4000",
            $"{cbrSay.Video.X264.VbvMaks}/{cbrSay.Video.X264.BitHizi}");
        Kontrol("CBR sayılan 3 geçiş: not", "en yüksek bit hızı 100 kb/s bu videoda 3 geçişle kodlanamıyor (x264: en az 213 kb/s): 219 kb/s", not ?? "yok");
        var cbrSay2 = Varsayilanlar.YouTube();
        cbrSay2.Video.X264.Mod = X264Modu.IkiGecis;
        cbrSay2.Video.X264.BitHizi = 64;
        cbrSay2.Video.X264.VbvMaks = 32;
        ArgsBuilder.X264Duzelt("requested bitrate is too low. estimated minimum is 138 kbps", cbrSay2);
        Kontrol("CBR sayılan 2 geçiş (64, en yüksek 32): ikisi de 142 (tek denemede)", "142/142",
            $"{cbrSay2.Video.X264.VbvMaks}/{cbrSay2.Video.X264.BitHizi}");

        // Matris: QP aralığı boş (min > max): önce kullanıcının sınırı, sonra düz matris.
        var matris = Varsayilanlar.YouTube();
        matris.Video.X264.Cqm = "jvt";
        matris.Video.X264.QpMax = null;
        matris.Video.X264.QpMin = 10;
        const string Bos = "[libx264 @ 0x1] Impossible QP constraints for CQM (min=5, max=4)";
        Kontrol("Matris, qpmin 10 > en çok 4: qpmin 4", "JVT nicemleme matrisiyle en düşük QP en çok 4 olabilir (x264): qpmin 10 → 4",
            ArgsBuilder.X264Duzelt(Bos, matris) ?? "yok");
        Kontrol("Matris, yine boş (en az 5 > 4): düz matris", "JVT nicemleme matrisi bu ayarlarla kullanılamıyor (x264: QP en az 5, en çok 4): düz matris",
            ArgsBuilder.X264Duzelt(Bos, matris) ?? "yok");
        Kontrol("Matris düz oldu", "flat", matris.Video.X264.Cqm ?? "yok");
        Kontrol("Matris düzken yine aynı ileti: düzeltilecek bir şey yok", "yok", ArgsBuilder.X264Duzelt(Bos, matris) ?? "yok");

        // Geçmeli (MBAFF): kısıtlı intra kapalı, arama aralığı en az 16.
        var mbaff = Gecmeli("high");
        mbaff.Video.X264.KisitliIntra = true;
        mbaff.Video.X264.MeAraligi = 4;
        mbaff.Video.X264.IsParcacigi = 8;
        var mk = Komut(mbaff, hd);
        Iceriyor("Geçmeli + kısıtlı intra: kısıtlı intra kapalı", mk, ":constrained-intra=0:");
        Iceriyor("Geçmeli + kısıtlı intra: geçmeli kalır", mk, ":interlaced=1:");
        Iceriyor("Geçmeli + arama aralığı 4 + 8 iş parçacığı: 16", mk, ":merange=16:");
        Kontrol("Geçmeli: notlar", "geçmeli 4:2:0'da yükseklik 4'ün katı: 1350 → 1348 | geçmeli kodlamada kısıtlı intra tahmin kapatıldı (x264 ikisi birlikteyken çözülemeyen kare üretebiliyor) | geçmeli kodlamada arama aralığı en az 16 (x264 daha küçüğünde çok iş parçacığıyla düşüyor): 4 → 16",
            string.Join(" | ", Exporter.Notlar(hd, mbaff, SesSecimi.Yok)));
        mbaff.Video.X264.IsParcacigi = 1;
        Iceriyor("Geçmeli, tek iş parçacığı: arama aralığı olduğu gibi (4)", Komut(mbaff, hd), ":merange=4:");
        mbaff.Video.X264.IsParcacigi = null;
        Iceriyor("Geçmeli, iş parçacığı otomatik: 16", Komut(mbaff, hd), ":merange=16:");
        mbaff.Video.X264.MeAraligi = 24;
        Iceriyor("Geçmeli, arama aralığı 24: olduğu gibi", Komut(mbaff, hd), ":merange=24:");
        var tabanGecmeli = Gecmeli("baseline");
        tabanGecmeli.Video.X264.KisitliIntra = true;
        tabanGecmeli.Video.X264.MeAraligi = 4;
        var tk = Komut(tabanGecmeli, hd);
        Iceriyor("Baseline (geçmeli yok) + kısıtlı intra: açık kalır", tk, ":constrained-intra=1:");
        Iceriyor("Baseline (geçmeli yok): arama aralığı 4 kalır", tk, ":merange=4:");
        var ilerleyenKisitli = Varsayilanlar.TikTok();
        ilerleyenKisitli.Video.X264.KisitliIntra = true;
        Iceriyor("İlerleyen + kısıtlı intra: açık", Komut(ilerleyenKisitli, hd), ":constrained-intra=1:");
        Kontrol("İlerleyen: not yok", "yok", ArgsBuilder.KisitliIntraNotu(ilerleyenKisitli, hd) ?? "yok");

        // Tahmini boyut: VBV x264'ün en yüksek QP'de bile harcayacağının altındaysa "en çok" söylenmez.
        Kontrol("En az VBV bit hızı: 1920×1080 30 fps (120 × 68 makroblok × 30 × 6 bit)", "1469",
            BoyutTahmini.EnAzVbvBitHizi(Varsayilanlar.YouTube(), V(1920, 1080)).ToString("0"));
        var dusukVbv = Varsayilanlar.YouTube();
        dusukVbv.Video.X264.VbvMaks = 100;
        dusukVbv.Video.X264.VbvTampon = 100;
        Kontrol("4K 25 fps, VBV 100 kb/s: \"en çok\" yok (dosya tahminin 40 katı çıkıyordu)", "yok",
            BoyutTahmini.Hesapla(dusukVbv, V(3840, 2160, 3, 25), SesSecimi.Yok)?.Metin ?? "yok");

        // Bellek yetmedi (GitHub'da ölçüldü: 2160×3840'ta 128 iş parçacığı ~47 GB, 64: 23,8 GB, 32: 12,5 GB, 16: 7 GB).
        const string Bellek = "x264 [error]: malloc of size 42881664 failed\n[vost#0:0/libx264 @ 0x1] [enc:libx264 @ 0x2] Error while "
                              + "opening encoder - maybe incorrect parameters such as bit_rate, rate, width or height.\nCould not open encoder before EOF";
        Kontrol("Bellek yetmedi: x264'ün iletisi", "True", ArgsBuilder.BellekYetmedi(Bellek).ToString());
        Kontrol("Bellek yetmedi: FFmpeg'in iletisi", "True", ArgsBuilder.BellekYetmedi("[vf#0:0] Error while filtering: Cannot allocate memory").ToString());
        Kontrol("Bellek yetmedi değil: kodlayıcı açılamadı", "False", ArgsBuilder.BellekYetmedi("Could not open encoder before EOF").ToString());
        Kontrol("Bellek: Türkçe açıklama (\"ayarlar uymuyor\" değil)",
            "Bellek yetmedi: kodlayıcı bu ayarlarla bilgisayarın belleğinden fazlasını istiyor. Çözünürlüğü, iş parçacığı sayısını ya da önden bakışı düşürün; açık başka programları kapatın.",
            HataAciklamasi.Bul(Bellek) ?? "yok");
        Kontrol("x264'ün kendi iş parçacığı sayısı: 16 işlemcide 24", "24", ArgsBuilder.X264OtomatikIsParcacigi(16).ToString());
        var cok = Varsayilanlar.YouTube();
        cok.Video.X264.IsParcacigi = 128;
        var bnot = ArgsBuilder.X264Duzelt(Bellek, cok, islemci: 16);
        Kontrol("Bellek, 128 iş parçacığı (16 işlemci): 64", "64", cok.Video.X264.IsParcacigi?.ToString() ?? "yok");
        Kontrol("Bellek: not (41 MB yukarı yuvarlı)", "Bellek yetmedi (x264 41 MB daha ayıramadı): iş parçacığı 128 → 64", bnot ?? "yok");
        Kontrol("Bellek: 64 → 32", "Bellek yetmedi (x264 41 MB daha ayıramadı): iş parçacığı 64 → 32", ArgsBuilder.X264Duzelt(Bellek, cok, islemci: 16) ?? "yok");
        Kontrol("Bellek: 32 → 16 (x264'ün kendi seçtiği 24'ün üstünde)", "Bellek yetmedi (x264 41 MB daha ayıramadı): iş parçacığı 32 → 16",
            ArgsBuilder.X264Duzelt(Bellek, cok, islemci: 16) ?? "yok");
        Kontrol("Bellek: önden bakış 40 (60'tan kısa): sonra iş parçacığı 16 → 8", "Bellek yetmedi (x264 41 MB daha ayıramadı): iş parçacığı 16 → 8",
            ArgsBuilder.X264Duzelt(Bellek, cok, islemci: 16) ?? "yok");
        Kontrol("Bellek: önden bakış değişmedi", "40", cok.Video.X264.IleriBakis?.ToString() ?? "yok");
        var tt = Varsayilanlar.TikTok();
        Kontrol("Bellek, TikTok varsayılanı (8 iş parçacığı, 16 işlemci): önce önden bakış 250 → 125",
            "Bellek yetmedi (x264 301 MB daha ayıramadı): önden bakış 250 → 125 kare",
            ArgsBuilder.X264Duzelt("x264 [error]: malloc of size 315187200 failed", tt, islemci: 16) ?? "yok");
        Kontrol("Bellek, TikTok: 125 → 60 (en az 60)", "Bellek yetmedi: önden bakış 125 → 60 kare",
            ArgsBuilder.X264Duzelt("Cannot allocate memory", tt, islemci: 16) ?? "yok");
        Kontrol("Bellek, TikTok: sonra iş parçacığı 8 → 4", "Bellek yetmedi: iş parçacığı 8 → 4", ArgsBuilder.X264Duzelt("Cannot allocate memory", tt, islemci: 16) ?? "yok");
        Kontrol("Bellek, TikTok: 4 → 2", "Bellek yetmedi: iş parçacığı 4 → 2", ArgsBuilder.X264Duzelt("Cannot allocate memory", tt, islemci: 16) ?? "yok");
        Kontrol("Bellek, TikTok: 2 → 1", "Bellek yetmedi: iş parçacığı 2 → 1", ArgsBuilder.X264Duzelt("Cannot allocate memory", tt, islemci: 16) ?? "yok");
        Kontrol("Bellek, TikTok: sonra önden bakış 60 → 30", "Bellek yetmedi: önden bakış 60 → 30 kare", ArgsBuilder.X264Duzelt("Cannot allocate memory", tt, islemci: 16) ?? "yok");
        var tt4 = Varsayilanlar.TikTok();
        Kontrol("Bellek, TikTok, 4 işlemci (x264'ün kendi seçtiği 6): önce iş parçacığı 8 → 4", "Bellek yetmedi: iş parçacığı 8 → 4",
            ArgsBuilder.X264Duzelt("Cannot allocate memory", tt4, islemci: 4) ?? "yok");
        var oto = Varsayilanlar.YouTube();
        oto.Video.X264.IsParcacigi = null;
        Kontrol("Bellek, iş parçacığı boş (x264 seçer: 16 işlemcide 24): 24 → 12", "Bellek yetmedi: iş parçacığı 24 → 12",
            ArgsBuilder.X264Duzelt("Cannot allocate memory", oto, islemci: 16) ?? "yok");
        var ekli = Varsayilanlar.YouTube();
        ekli.Video.X264.IsParcacigi = 128;
        ekli.Video.X264.EkParametreler = "threads=128";
        Kontrol("Bellek, ek parametrede threads: iş parçacığına dokunulmaz, önden bakış 40 → 20", "Bellek yetmedi: önden bakış 40 → 20 kare",
            ArgsBuilder.X264Duzelt("Cannot allocate memory", ekli, islemci: 16) ?? "yok");
        Kontrol("Bellek, ek parametrede threads: ayar 128 kaldı", "128", ekli.Video.X264.IsParcacigi?.ToString() ?? "yok");
        ekli.Video.X264.EkParametreler = "threads=128:rc-lookahead=250";
        Kontrol("Bellek, ikisi de ek parametrede: düzeltilecek bir şey yok", "yok", ArgsBuilder.X264Duzelt("Cannot allocate memory", ekli, islemci: 16) ?? "yok");
        var hizli = Varsayilanlar.YouTube();
        hizli.Video.X264.IsParcacigi = 1;
        hizli.Video.X264.IleriBakis = null;
        hizli.Video.X264.OnAyar = "ultrafast";
        Kontrol("Bellek, 1 iş parçacığı + ultrafast (önden bakış 0): düzeltilecek bir şey yok", "yok",
            ArgsBuilder.X264Duzelt("Cannot allocate memory", hizli, islemci: 16) ?? "yok");
        hizli.Video.X264.OnAyar = "slow";
        Kontrol("Bellek, önden bakış boş + slow (x264: 50): 25", "Bellek yetmedi: önden bakış 50 → 25 kare",
            ArgsBuilder.X264Duzelt("Cannot allocate memory", hizli, islemci: 16) ?? "yok");
        Kontrol("Bellek, NVENC: x264 düzeltmesi yok", "yok", ArgsBuilder.X264Duzelt(Bellek, nv, islemci: 16) ?? "yok");
        Kontrol("Başka bir hata: düzeltilmez", "yok", ArgsBuilder.X264Duzelt("height not divisible by 4 (1080x1350)", Varsayilanlar.YouTube()) ?? "yok");

        // Kayıpsız ve tahmini boyut.
        var m = V(1920, 1080);
        var kp = Varsayilanlar.YouTube();
        kp.Video.X264.Crf = 0;
        kp.Video.X264.Profil = string.Empty;
        Kontrol("Kayıpsız mı: 8 bit CRF 0, otomatik profil", "True", ArgsBuilder.X264KayipsizMi(kp, m).ToString());
        Kontrol("CRF 0 (kayıpsız) + VBV: x264 VBV'yi yok sayar, tahmin yok", "yok", BoyutTahmini.Hesapla(kp, m, SesSecimi.Yok)?.Metin ?? "yok");
        kp.Video.X264.Profil = "high444";
        Kontrol("Kayıpsız mı: CRF 0, High 4:4:4", "True", ArgsBuilder.X264KayipsizMi(kp, m).ToString());
        kp.Video.X264.Profil = "main";
        Kontrol("Kayıpsız mı: CRF 0, Main (CRF 1'e çıkar)", "False", ArgsBuilder.X264KayipsizMi(kp, m).ToString());
        Kontrol("CRF 0 + Main + VBV: en çok", "True", (BoyutTahmini.Hesapla(kp, m, SesSecimi.Yok)?.Tur == TahminTuru.EnCok).ToString());
        kp.Video.X264.Profil = "high10";
        Kontrol("Kayıpsız mı: CRF 0, High 10 (10 bitte CRF 0 = QP 12)", "False", ArgsBuilder.X264KayipsizMi(kp, m).ToString());
        kp.Video.X264.Profil = string.Empty;
        kp.Video.X264.Crf = 0.5;
        Kontrol("Kayıpsız mı: CRF 0,5, otomatik profil", "True", ArgsBuilder.X264KayipsizMi(kp, m).ToString());
        kp.Video.X264.Crf = 1;
        Kontrol("Kayıpsız mı: CRF 1", "False", ArgsBuilder.X264KayipsizMi(kp, m).ToString());
        kp.Video.X264.Mod = X264Modu.SabitQp;
        kp.Video.X264.Qp = 0;
        Kontrol("Kayıpsız mı: QP 0, otomatik profil", "True", ArgsBuilder.X264KayipsizMi(kp, m).ToString());
        var dusukQp = Varsayilanlar.YouTube();
        dusukQp.Video.X264.QpMax = 10;
        Kontrol("CRF + VBV, qpmax 10: x264 VBV'ye uyamaz, \"en çok\" yok", "yok", BoyutTahmini.Hesapla(dusukQp, m, SesSecimi.Yok)?.Metin ?? "yok");
        dusukQp.Video.X264.QpMax = BoyutTahmini.EnCokIcinQpMax;
        Kontrol("CRF + VBV, qpmax 20: en çok", "True", (BoyutTahmini.Hesapla(dusukQp, m, SesSecimi.Yok)?.Tur == TahminTuru.EnCok).ToString());
        var kucukTampon = Varsayilanlar.YouTube();
        kucukTampon.Video.X264.VbvTampon = 100;
        Kontrol("Tampon bir kareden küçük (100 kb, 34375 / 30 fps): x264 bir kareye büyütür", "en çok 41,1 MB",
            BoyutTahmini.Hesapla(kucukTampon, m, SesSecimi.Yok)?.Metin ?? "yok");

        // FDK VBR: tahmini boyutta FDK'nin kipin hedeflediği bit hızı (stereo VBR 5: 192, eskiden 224 sayılıyordu).
        var vbr = Varsayilanlar.YouTube();
        vbr.Ses.Fdk.Mod = FdkModu.Vbr5;
        var sesStereo = new SesSecimi(SesSecimTuru.Video, null, 2) { OrnekHizi = 48000, Duzen = "stereo" };
        Kontrol("FDK VBR 5 stereo: 192 kb/s", "192", BoyutTahmini.SesBitHizi(vbr.Ses, sesStereo).ToString("0"));
        vbr.Ses.Fdk.Mod = FdkModu.Vbr1;
        vbr.Ses.Kanal = KanalModu.Mono;
        Kontrol("FDK VBR 1 mono: 32 kb/s", "32", BoyutTahmini.SesBitHizi(vbr.Ses, sesStereo).ToString("0"));
        vbr.Ses.Fdk.Mod = FdkModu.Vbr3;
        vbr.Ses.Kanal = KanalModu.Kaynak;
        var ses51 = new SesSecimi(SesSecimTuru.Video, null, 6) { OrnekHizi = 48000, Duzen = "5.1" };
        Kontrol("FDK VBR 3 5.1: 240 kb/s (48 × 5 etkin kanal)", "240", BoyutTahmini.SesBitHizi(vbr.Ses, ses51).ToString("0"));
    }
}
