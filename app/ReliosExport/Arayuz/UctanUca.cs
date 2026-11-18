using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Arayuz;

/// <summary>
/// Yalnızca GitHub'daki uçtan uca deneme (--uctan-uca). Gerçek ana pencereyi bir kullanıcı gibi kullanır: video ekler
/// (sürükle-bırakla aynı yol), Başlat'a basar, bitmesini bekler, tekrar işler, iptal eder, platform değiştirir,
/// listeyi temizler; her adımda satırları, durum yazısını, düğmeleri ve oluşan dosyaları (ffprobe ile) denetler.
/// Deneme videoları iş akışında ffmpeg ile üretilir (medya klasörü). Kodlama YouTube ve TikTok varsayılanlarıyla
/// (x264) yapılır; GitHub'da NVIDIA ekran kartı olmadığı için TikTok'ta NVENC seçilince anlaşılır hata mesajı ve
/// günlük denetlenir.
/// </summary>
internal static class UctanUca
{
    public static async Task<int> CalistirAsync(MainWindow w, string medya, string cikti, StringBuilder rapor)
    {
        var fark = 0;
        var sira = 0;

        void Kontrol(string ad, object? beklenen, object? sonuc, bool ok)
        {
            sira++;
            rapor.AppendLine($"{(ok ? "OK  " : "FARK")} | {sira,2}. {ad} | beklenen: {beklenen} | sonuç: {sonuc}");
            if (!ok)
            {
                fark++;
            }
        }

        void Esit(string ad, object? beklenen, object? sonuc) =>
            Kontrol(ad, beklenen, sonuc, Equals(beklenen?.ToString(), sonuc?.ToString()));

        void Bolum(string ad) => rapor.AppendLine().AppendLine("--- " + ad);

        string M(string ad) => Path.Combine(medya, ad);
        FileItem? Satir(string ad) => w.KontrolSatirlar.FirstOrDefault(f => f.FileName == ad);
        string Durum(string ad) => Satir(ad)?.Status ?? "(satır yok)";
        string Dugme() => w.BtnStart.Content as string ?? string.Empty;
        string[] Parcalar() => Directory.GetFiles(cikti, "*.part*");

        async Task<bool> Tikla(System.Windows.Controls.Primitives.ButtonBase dugme, string ad)
        {
            var ok = MainWindow.KontrolTikla(dugme);
            if (!ok)
            {
                Kontrol(ad + " düğmesine tıklanabiliyor", "etkin", "etkin değil", false);
            }

            await Task.Delay(150);
            return ok;
        }

        async Task<bool> Bitir(string ad, double saniye)
        {
            var ok = await Bekle(() => !w.KontrolMesgul, saniye);
            Kontrol(ad + " bitti (" + saniye + " sn içinde)", "bitti", ok ? "bitti" : "bitmedi", ok);
            return ok;
        }

        Directory.CreateDirectory(cikti);
        rapor.AppendLine("Uçtan uca deneme · kayıt yeri: " + cikti);

        // ------------------------------------------------------------------ 1. Açılış ve video ekleme
        Bolum("1. Açılış, YouTube profili, video ekleme");
        Esit("FFmpeg bulundu (program içinde)", true, Tools.Available);
        w.RbYouTube.IsChecked = true;
        await Task.Delay(200);
        w.KontrolCiktiKlasoru(cikti);
        Esit("YouTube kodlayıcısı (varsayılan)", VideoKodlayici.X264, w.KontrolAyarlari.Video.Kodlayici);
        Esit("YouTube: LUFS eşitleme varsayılan açık", true, w.KontrolAyarlari.Ses.Esitleme);
        Esit("Liste boşken Başlat etkin değil", false, w.BtnStart.IsEnabled);

        const string Tr = "Türkçe ğüşıöç İsim (1)";
        w.KontrolEkle(M("ses_var.mp4"), M("sessiz.mov"), M("harici.mp4"), M("harici.wav"), M(Tr + ".mp4"), M(Tr + ".wav"),
            M("bozuk.mp4"), M("notlar.txt"), M("yalniz.wav"));
        await Task.Delay(300);
        Esit("Eklenen satırlar (ses ve metin dosyası listeye girmez)", $"ses_var.mp4, sessiz.mov, harici.mp4, {Tr}.mp4, bozuk.mp4",
            string.Join(", ", w.KontrolSatirlar.Select(f => f.FileName)));
        Kontrol("Video olmayan dosya bildirildi", "1 dosya video olmadığı için eklenmedi", w.KontrolDurum,
            w.KontrolDurum.Contains("1 dosya video olmadığı için eklenmedi", StringComparison.Ordinal));
        Kontrol("Eşi olmayan ses dosyası bildirildi", "1 ses dosyasının aynı adlı videosu listede yok", w.KontrolDurum,
            w.KontrolDurum.Contains("1 ses dosyasının aynı adlı videosu listede yok", StringComparison.Ordinal));
        Esit("Aynı adlı ses bulundu", "harici.wav", Path.GetFileName(Satir("harici.mp4")?.HariciSes));
        Esit("Türkçe / boşluklu adda aynı adlı ses bulundu", Tr + ".wav", Path.GetFileName(Satir(Tr + ".mp4")?.HariciSes));
        Kontrol("Aynı adlı ses satırda yazıyor", "Bekliyor · Ses: harici.wav", Durum("harici.mp4"),
            Durum("harici.mp4").StartsWith("Bekliyor · Ses: harici.wav", StringComparison.Ordinal));

        w.KontrolEkle(M("ses_var.mp4"));
        await Task.Delay(200);
        Esit("Aynı video ikinci kez eklenmez", 5, w.KontrolSatirlar.Count);

        var tahmin = await Bekle(() => Durum("ses_var.mp4").Contains("en çok", StringComparison.Ordinal), 60);
        Kontrol("Tahmini boyut yazıldı (YouTube: CRF + en yüksek bit hızı → \"en çok\")", "… en çok … MB", Durum("ses_var.mp4"), tahmin);
        await Bekle(() => Durum("sessiz.mov").Contains("en çok", StringComparison.Ordinal), 30);
        Kontrol("Sessiz videonun tahmini boyutu", "… en çok … KB/MB", Durum("sessiz.mov"),
            Durum("sessiz.mov").Contains("en çok", StringComparison.Ordinal));
        Esit("Bozuk dosyanın tahmini yok", "Bekliyor", Durum("bozuk.mp4"));
        Esit("Düğme", "Başlat", Dugme());
        Esit("Düğme etkin", true, w.BtnStart.IsEnabled);

        // ------------------------------------------------------------------ 2. Başlat
        Bolum("2. Başlat: 4 video kodlanır, bozuk dosya hata verir");
        if (await Tikla(w.BtnStart, "Başlat"))
        {
            Esit("Kodlarken meşgul", true, w.KontrolMesgul);
            Esit("Kodlarken düğme", "İptal", Dugme());
            Esit("Kodlarken \"Video ekle\" kapalı", false, w.BtnAdd.IsEnabled);
            Esit("Kodlarken platform değiştirilemez", false, w.RbTikTok.IsEnabled);
            Esit("Kodlarken \"Temizle\" kapalı", false, w.BtnClear.IsEnabled);
            await Bitir("Dışa aktarma", 600);
        }

        foreach (var ad in new[] { "ses_var.mp4", "sessiz.mov", "harici.mp4", Tr + ".mp4" })
        {
            var s = Satir(ad);
            Esit(ad + ": durum", ItemState.Done, s?.State);
            var beklenenAd = Path.GetFileNameWithoutExtension(ad) + "_YouTube.mov";
            Esit(ad + ": çıktı adı", beklenenAd, Path.GetFileName(s?.OutputPath));
            Kontrol(ad + ": çıktı dosyası var", "var", s?.OutputPath, s?.OutputPath is { } o && File.Exists(o));
            Kontrol(ad + ": satır (ad · süre · boyut)", "Hazır · " + beklenenAd + " · … sn · … MB", s?.Status,
                s?.Status is { } st && st.StartsWith("Hazır · " + beklenenAd + " · ", StringComparison.Ordinal)
                && Regex.IsMatch(st, @" · [\d,]+ (KB|MB|GB)( |$)"));
        }

        var sesVar = await Incele(Satir("ses_var.mp4")?.OutputPath);
        Esit("ses_var: video codec", "h264", sesVar?.VideoCodec);
        Esit("ses_var: ses (YouTube: FDK AAC, 48 kHz, stereo)", "aac 48000 2",
            sesVar?.SesAkislari is [var a1] ? $"{a1.Codec} {a1.OrnekHizi} {a1.Kanal}" : "ses izi sayısı " + sesVar?.SesAkislari.Count);
        Kontrol("ses_var: süre (12 sn)", "12 ± 0,2", sesVar?.DurationSeconds, sesVar is { } sv && Math.Abs(sv.DurationSeconds - 12) <= 0.2);
        var sessiz = await Incele(Satir("sessiz.mov")?.OutputPath);
        Esit("sessiz: çıktıda ses yok", 0, sessiz?.SesAkislari.Count);
        var harici = await Incele(Satir("harici.mp4")?.OutputPath);
        Esit("harici: sesi aynı adlı WAV'dan (video sessizdi)", "aac 48000 2",
            harici?.SesAkislari is [var a2] ? $"{a2.Codec} {a2.OrnekHizi} {a2.Kanal}" : "ses izi sayısı " + harici?.SesAkislari.Count);

        var turkce = await Incele(Satir(Tr + ".mp4")?.OutputPath);
        Esit("Türkçe ad: dikey boyut ve aynı adlı WAV'ın sesi", "1080×1920 aac 48000 2",
            turkce is null ? "çıktı okunamadı" : $"{turkce.DisplayWidth}×{turkce.DisplayHeight} "
            + (turkce.SesAkislari is [var a4] ? $"{a4.Codec} {a4.OrnekHizi} {a4.Kanal}" : "ses izi sayısı " + turkce.SesAkislari.Count));

        var bozuk = Satir("bozuk.mp4");
        Esit("bozuk: durum", ItemState.Failed, bozuk?.State);
        Esit("bozuk: hata yazısı Türkçe ve anlaşılır", "Hata: Video dosyası okunamadı: bozuk, yarım kopyalanmış ya da desteklenmeyen bir dosya.", bozuk?.Status);
        Kontrol("bozuk: satırda program adı / bellek adresi yok", "ffprobe ve @ 0x yok", bozuk?.Status,
            bozuk?.Status is { } bs && !bs.Contains("ffprobe", StringComparison.OrdinalIgnoreCase) && !bs.Contains(" @ ", StringComparison.Ordinal));
        var bozukGunluk = bozuk?.GunlukYolu;
        Kontrol("bozuk: hata günlüğü yazıldı (satırdaki günlük düğmesi)", "dosya var", bozukGunluk, bozukGunluk is not null && File.Exists(bozukGunluk));
        Esit("bozuk: günlük düğmesi görünür", Visibility.Visible, bozuk?.GunlukGorunur);
        Kontrol("bozuk: günlükte okuma komutu ve çıktısı", "Komut 1/1 … ----- ffprobe çıktısı ----- … Invalid data", bozukGunluk,
            bozukGunluk is not null && File.Exists(bozukGunluk) && File.ReadAllText(bozukGunluk) is var bg
            && bg.Contains("Komut 1/1:", StringComparison.Ordinal) && bg.Contains("----- ffprobe çıktısı -----", StringComparison.Ordinal)
            && bg.Contains("Invalid data found", StringComparison.Ordinal));
        Kontrol("bozuk: ipucunda ayrıntı ve günlüğün yeri", "… Ayrıntılı günlük: …", bozuk?.Detail,
            bozuk?.Detail?.Contains("Ayrıntılı günlük: ", StringComparison.Ordinal) == true);
        Esit("Yarım dosya kalmadı (.part)", 0, Parcalar().Length);
        Esit("Durum yazısı", "4 video hazır · 1 hatalı", w.KontrolDurum);
        Esit("Hatalı satır varken düğme", "Başlat", Dugme());
        Esit("Bitince \"Video ekle\" açık", true, w.BtnAdd.IsEnabled);

        // ------------------------------------------------------------------ 3. Yalnızca hatalı yeniden
        Bolum("3. Başlat yeniden: yalnızca hatalı video denenir, bitenler yeniden kodlanmaz");
        var ilkCikti = Satir("ses_var.mp4")?.OutputPath;
        if (await Tikla(w.BtnStart, "Başlat"))
        {
            await Bitir("Yeniden deneme", 120);
        }

        Esit("bozuk: yine hata", ItemState.Failed, Satir("bozuk.mp4")?.State);
        Esit("ses_var: dokunulmadı (aynı çıktı)", ilkCikti, Satir("ses_var.mp4")?.OutputPath);
        Esit("Yeni (_2) dosya oluşmadı", 0, Directory.GetFiles(cikti, "*_2.*").Length);
        Esit("Durum yazısı", "1 video hatalı", w.KontrolDurum);

        // ------------------------------------------------------------------ 4. Çıkar → Tekrar işle (LUFS eşitleme açık)
        Bolum("4. Hatalıyı çıkar: düğme \"Tekrar işle\"; LUFS eşitleme açıkken hepsi yeniden");
        if (bozuk is not null)
        {
            w.KontrolCikar(bozuk);
        }

        await Task.Delay(200);
        Esit("Satır sayısı", 4, w.KontrolSatirlar.Count);
        Esit("Hepsi bitince düğme", "Tekrar işle", Dugme());
        w.KontrolAyarlari.Ses.Esitleme = true;
        await Task.Delay(200);
        if (await Tikla(w.BtnStart, "Tekrar işle"))
        {
            Esit("Tekrar işlerken düğme", "İptal", Dugme());
            await Bitir("Tekrar işleme", 600);
        }

        foreach (var ad in new[] { "ses_var.mp4", "sessiz.mov", "harici.mp4", Tr + ".mp4" })
        {
            var s = Satir(ad);
            Esit(ad + ": durum", ItemState.Done, s?.State);
            Esit(ad + ": yeni çıktı ayrı adla", Path.GetFileNameWithoutExtension(ad) + "_YouTube_2.mov", Path.GetFileName(s?.OutputPath));
        }

        Kontrol("Önceki çıktı silinmedi", "var", ilkCikti, ilkCikti is not null && File.Exists(ilkCikti));
        Kontrol("ses_var: eşitleme sonucu satırda (kısık ses → -14 ± 0,2)", "… · -14,0 LUFS", Durum("ses_var.mp4"),
            Regex.IsMatch(Durum("ses_var.mp4"), @" · (-14,[0-2]|-13,[89]) LUFS$"));
        Kontrol("ses_var: ipucunda karar (bütün ses aynı miktarda)", "Karar: bütün ses aynı miktarda", Satir("ses_var.mp4")?.Detail,
            Satir("ses_var.mp4")?.Detail?.Contains("Karar: bütün ses aynı miktarda", StringComparison.Ordinal) == true);
        Kontrol("harici: yüksek ses (yalnızca kısık yükseltilir) → dokunulmadı", "… LUFS (dokunulmadı)", Durum("harici.mp4"),
            Durum("harici.mp4").EndsWith("LUFS (dokunulmadı)", StringComparison.Ordinal));
        Kontrol("Türkçe ad: aynı adlı kısık WAV -14'e getirildi (± 0,2)", "… · -14,0 LUFS", Durum(Tr + ".mp4"),
            Regex.IsMatch(Durum(Tr + ".mp4"), @" · (-14,[0-2]|-13,[89]) LUFS$"));
        Kontrol("sessiz: sesi olmayan videoda eşitleme yazısı yok", "LUFS yok", Durum("sessiz.mov"),
            !Durum("sessiz.mov").Contains("LUFS", StringComparison.Ordinal));
        var esitlenmis = await Incele(Satir("ses_var.mp4")?.OutputPath);
        Esit("ses_var (_2): ses yine FDK AAC 48 kHz stereo", "aac 48000 2",
            esitlenmis?.SesAkislari is [var a3] ? $"{a3.Codec} {a3.OrnekHizi} {a3.Kanal}" : "ses izi sayısı " + esitlenmis?.SesAkislari.Count);
        Esit("Durum yazısı", "4 video hazır", w.KontrolDurum);
        w.KontrolAyarlari.Ses.Esitleme = false;
        await Task.Delay(200);

        // ------------------------------------------------------------------ 5. İptal
        Bolum("5. İptal: uzun video kodlanırken durdurulur; yarım dosya silinir");
        w.KontrolEkle(M("uzun.mp4"));
        await Task.Delay(300);
        Esit("Bekleyen video eklenince düğme", "Başlat", Dugme());
        if (await Tikla(w.BtnStart, "Başlat"))
        {
            var basladi = await Bekle(() => Satir("uzun.mp4")?.Progress > 0.03, 180);
            Kontrol("uzun: kodlama ilerliyor", "> %3", Satir("uzun.mp4")?.PercentText, basladi);
            await Tikla(w.BtnStart, "İptal");
            await Bitir("İptal", 60);
        }

        Esit("uzun: durum", ItemState.Canceled, Satir("uzun.mp4")?.State);
        Esit("uzun: satır", "İptal edildi", Durum("uzun.mp4"));
        Esit("Durum yazısı", "İptal edildi", w.KontrolDurum);
        Esit("Yarım dosya silindi (.part)", 0, Parcalar().Length);
        Esit("uzun: çıktı oluşmadı", 0, Directory.GetFiles(cikti, "uzun_*").Length);
        Esit("İptalden sonra düğme", "Başlat", Dugme());
        Esit("Bitenlere dokunulmadı", "Done, Done, Done, Done",
            string.Join(", ", new[] { "ses_var.mp4", "sessiz.mov", "harici.mp4", Tr + ".mp4" }.Select(a => Satir(a)?.State.ToString())));

        // ------------------------------------------------------------------ 6. TikTok: varsayılan x264; NVENC seçilince ekran kartı yok
        Bolum("6. TikTok'a geç: satırlar sıfırlanır; varsayılan x264 CRF 24 (en çok 8000 kb/s) + FDK AAC 44,1 kHz; NVENC seçilince (ekran kartı yok) anlaşılır hata + günlük");
        w.RbTikTok.IsChecked = true;
        await Task.Delay(300);
        Esit("Platform değişince satırlar bekliyor", "Waiting, Waiting, Waiting, Waiting, Waiting",
            string.Join(", ", w.KontrolSatirlar.Select(f => f.State.ToString())));
        Kontrol("Satır yazısı yeniden \"Bekliyor\"", "Bekliyor …", Durum("ses_var.mp4"),
            Durum("ses_var.mp4").StartsWith("Bekliyor", StringComparison.Ordinal));
        var ttAyar = w.KontrolAyarlari;
        Esit("TikTok kodlayıcısı (varsayılan)", VideoKodlayici.X264, ttAyar.Video.Kodlayici);
        Esit("TikTok x264 (varsayılan)", "Crf 24 veryslow high kaynak · ref 16 · önden bakış 250 · en yüksek 8000 · tampon 12000",
            $"{ttAyar.Video.X264.Mod} {ttAyar.Video.X264.Crf} {ttAyar.Video.X264.OnAyar} {ttAyar.Video.X264.Profil} {ttAyar.Video.X264.Seviye} · ref {ttAyar.Video.X264.Ref} · önden bakış {ttAyar.Video.X264.IleriBakis} · en yüksek {ttAyar.Video.X264.VbvMaks} · tampon {ttAyar.Video.X264.VbvTampon}");
        Esit("TikTok anahtar kare (varsayılan)", "Saniye 10", $"{ttAyar.AnahtarKare.Birim} {ttAyar.AnahtarKare.EnBuyuk}");
        Esit("TikTok sesi (varsayılan)", "FdkAac 44100 128", $"{ttAyar.Ses.Kodlayici} {ttAyar.Ses.OrnekHizi} {ttAyar.Ses.Fdk.BitHizi}");
        Esit("TikTok: LUFS eşitleme varsayılan açık", true, ttAyar.Ses.Esitleme);

        // Kullanıcı NVENC'i seçer: GitHub'da NVIDIA ekran kartı yok.
        ttAyar.Video.Kodlayici = VideoKodlayici.Nvenc;
        await Task.Delay(300);
        Esit("Düğme", "Başlat", Dugme());
        if (await Tikla(w.BtnStart, "Başlat"))
        {
            await Bitir("TikTok dışa aktarma (NVENC)", 300);
        }

        Esit("Hepsi hatalı (ekran kartı yok)", "Failed, Failed, Failed, Failed, Failed",
            string.Join(", ", w.KontrolSatirlar.Select(f => f.State.ToString())));
        Kontrol("Hata yazısı anlaşılır (NVENC / ekran kartı)", "Hata: NVENC kullanılamadı …", Durum("ses_var.mp4"),
            Durum("ses_var.mp4").StartsWith("Hata: NVENC", StringComparison.Ordinal));
        var gunluk = Satir("ses_var.mp4")?.GunlukYolu;
        Kontrol("Hata günlüğü yazıldı", "dosya var", gunluk, gunluk is not null && File.Exists(gunluk));
        Kontrol("Günlükte komut ve ffmpeg çıktısı", "Komut 1/1 … ----- ffmpeg çıktısı -----", gunluk,
            gunluk is not null && File.Exists(gunluk) && File.ReadAllText(gunluk) is var g
            && g.Contains("Komut 1/1:", StringComparison.Ordinal) && g.Contains("----- ffmpeg çıktısı -----", StringComparison.Ordinal));
        Esit("Durum yazısı", "5 video hatalı", w.KontrolDurum);
        Esit("Yarım dosya kalmadı (.part)", 0, Parcalar().Length);

        // ------------------------------------------------------------------ 6b. TikTok varsayılanıyla kodlama
        Bolum("6b. TikTok varsayılanı (x264 veryslow CRF 24, en çok 8000 kb/s, FDK AAC 128 kb/s 44,1 kHz) ile kodlama");
        ttAyar.Video.Kodlayici = VideoKodlayici.X264;
        await Task.Delay(300);
        if (await Tikla(w.BtnClear, "Temizle"))
        {
            Esit("Liste boş", 0, w.KontrolSatirlar.Count);
        }

        w.KontrolEkle(M("ses_var.mp4"));
        await Task.Delay(2000);
        var ttTahmin = Durum("ses_var.mp4");
        Kontrol("TikTok: bekleyen satırda en çok boyut (CRF + en yüksek 8000 kb/s)", "Bekliyor · en çok … MB", ttTahmin,
            ttTahmin.StartsWith("Bekliyor · en çok ", StringComparison.Ordinal) && ttTahmin.EndsWith(" MB", StringComparison.Ordinal));
        var ttEnCok = Satir("ses_var.mp4") is { Medya: { } ttMedya } ttBekleyen
            ? BoyutTahmini.Hesapla(ttAyar, ttMedya, ArgsBuilder.SesSec(ttAyar.Ses, ttMedya, ttBekleyen.Path))?.Bayt
            : null;
        if (await Tikla(w.BtnStart, "Başlat"))
        {
            await Bitir("TikTok dışa aktarma (x264)", 600);
        }

        var ttSatir = Satir("ses_var.mp4");
        Esit("TikTok: durum", ItemState.Done, ttSatir?.State);
        Esit("TikTok: çıktı adı", "ses_var_TikTok.mov", Path.GetFileName(ttSatir?.OutputPath));
        Kontrol("TikTok: satırda -14,0 LUFS (kısık ses yükseltildi; AAC payı telafi edildi)", "… · -14,0 LUFS", ttSatir?.Status,
            ttSatir?.Status?.Contains("-14,0 LUFS", StringComparison.Ordinal) == true);
        var ttCikti = await Incele(ttSatir?.OutputPath);
        Esit("TikTok: video (H.264 High)", "h264 High", $"{ttCikti?.VideoCodec} {ttCikti?.VideoProfil}");
        Esit("TikTok: seviye (16 referans kare, 720p → 5.0)", 50, ttCikti?.VideoSeviye);
        Esit("TikTok: ses (FDK AAC, 44,1 kHz, stereo)", "aac 44100 2",
            ttCikti?.SesAkislari is [var t1] ? $"{t1.Codec} {t1.OrnekHizi} {t1.Kanal}" : "ses izi sayısı " + ttCikti?.SesAkislari.Count);
        Kontrol("TikTok: süre (12 sn)", "12 ± 0,2", ttCikti?.DurationSeconds, ttCikti is { } tc && Math.Abs(tc.DurationSeconds - 12) <= 0.2);
        var ttBoyut = ttSatir?.OutputPath is { } ttYol && File.Exists(ttYol) ? new FileInfo(ttYol).Length : (long?)null;
        Kontrol("TikTok: dosya \"en çok\" tahmininin altında", $"<= {ttEnCok?.ToString() ?? "?"} bayt", ttBoyut,
            ttBoyut is { } tb && ttEnCok is { } te && tb <= te);

        // ------------------------------------------------------------------ 7. Temizle, ayar dosyası
        Bolum("7. Temizle; ayarlar diske yazıldı");
        if (await Tikla(w.BtnClear, "Temizle"))
        {
            Esit("Liste boş", 0, w.KontrolSatirlar.Count);
        }

        Esit("Bırakma alanı görünüyor", Visibility.Visible, w.DropZone.Visibility);
        Esit("Liste boşken Başlat etkin değil", false, w.BtnStart.IsEnabled);
        var ayarDosyasi = Path.Combine(AppPaths.VeriKlasoru, "ayarlar.json");
        string? platform = null;
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(ayarDosyasi));
            platform = doc.RootElement.GetProperty("platform").GetString();
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            platform = "okunamadı: " + ex.Message;
        }

        Esit("ayarlar.json: son seçilen platform", "TikTok", platform);

        // ------------------------------------------------------------------ 8. Kayıt yeri yok (çıkarılmış disk)
        Bolum("8. Kayıt yeri bulunamıyor (olmayan sürücü): anlaşılır Türkçe hata");
        var surucu = "QRSTUVWXYZ".Select(c => c + @":\").FirstOrDefault(d => !Directory.Exists(d));
        if (surucu is null)
        {
            rapor.AppendLine("     | boş sürücü harfi yok; bu adım atlandı");
        }
        else
        {
            w.KontrolCiktiKlasoru(Path.Combine(surucu, "Relios Export deneme"));
            w.KontrolEkle(M("sessiz.mov"));
            await Task.Delay(300);
            if (await Tikla(w.BtnStart, "Başlat"))
            {
                await Bitir("Olmayan kayıt yerine dışa aktarma", 120);
            }

            Esit("Olmayan kayıt yeri: durum", ItemState.Failed, Satir("sessiz.mov")?.State);
            Kontrol("Olmayan kayıt yeri: Türkçe açıklama", "Hata: Kayıt yeri bulunamadı: …", Durum("sessiz.mov"),
                Durum("sessiz.mov").StartsWith("Hata: Kayıt yeri bulunamadı:", StringComparison.Ordinal));
            w.KontrolCiktiKlasoru(cikti);
            if (await Tikla(w.BtnClear, "Temizle"))
            {
                Esit("Liste boş", 0, w.KontrolSatirlar.Count);
            }
        }

        rapor.AppendLine().AppendLine($"Uçtan uca: {sira} kontrol, {fark} fark.");
        return fark;
    }

    /// <summary>Deneme hata verse de program meşgul kapanmasın (kapanışta onay penceresi açılırdı).</summary>
    public static async Task DurdurAsync(MainWindow w)
    {
        if (w.KontrolMesgul)
        {
            MainWindow.KontrolTikla(w.BtnStart);
            await Bekle(() => !w.KontrolMesgul, 60);
        }
    }

    private static async Task<bool> Bekle(Func<bool> kosul, double saniye)
    {
        var sw = Stopwatch.StartNew();
        while (!kosul())
        {
            if (sw.Elapsed.TotalSeconds > saniye)
            {
                return false;
            }

            await Task.Delay(100);
        }

        return true;
    }

    private static async Task<MediaInfo?> Incele(string? yol)
    {
        if (yol is null || !File.Exists(yol))
        {
            return null;
        }

        try
        {
            return await MediaProbe.ProbeAsync(Tools.Ffprobe, yol, CancellationToken.None);
        }
        catch (Exception ex) when (ex is FfmpegHatasi or InvalidOperationException)
        {
            return null;
        }
    }
}
