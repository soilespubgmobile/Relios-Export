using System.IO;
using System.Text;

namespace ReliosExport.Core;

/// <summary>ffmpeg başarısız olduğunda fırlatılır: Türkçe açıklama + tam ffmpeg günlüğü.</summary>
public sealed class FfmpegHatasi(string mesaj, string gunluk, int? cikisKodu = null) : Exception(mesaj)
{
    /// <summary>ffmpeg'in hata çıktısı (uzunsa başı ve sonu).</summary>
    public string Gunluk { get; } = gunluk;

    /// <summary>ffmpeg'in çıkış kodu (biliniyorsa). Windows'ta çökmede NTSTATUS: ör. 0xC0000005 erişim ihlali.</summary>
    public int? CikisKodu { get; } = cikisKodu;

    /// <summary>ffmpeg çöktü mü (kendi hatasıyla kapanmadı): erişim ihlali, yığın taşması, x264'ün iç denetimi (abort).</summary>
    public bool Coktu => CikisKodu is { } k && HataAciklamasi.CokmeMi(k);

    /// <summary>Günlüğün yazıldığı dosya (yazılabildiyse).</summary>
    public string? GunlukDosyasi { get; set; }
}

/// <summary>ffmpeg'in İngilizce hata çıktısından kullanıcının anlayacağı Türkçe açıklama.</summary>
public static class HataAciklamasi
{
    private static readonly (string[] Desenler, string Aciklama)[] Bilinenler =
    [
        (["Cannot load nvcuda.dll", "Cannot load libcuda", "No NVENC capable devices found", "CUDA_ERROR_NO_DEVICE",
          "cuInit(0) failed", "Could not dynamically load CUDA"],
            "NVENC kullanılamadı: NVIDIA ekran kartı ya da sürücüsü bulunamadı. Sürücüyü kurun / güncelleyin ya da kodlayıcıyı x264 yapın."),
        (["Driver does not support the required nvenc API version", "minimum required Nvidia driver"],
            "NVENC kullanılamadı: NVIDIA sürücüsü bu FFmpeg için çok eski. Sürücüyü güncelleyin ya da kodlayıcıyı x264 yapın."),
        (["OpenEncodeSessionEx failed", "out of memory (10)", "incompatible client key"],
            "NVENC oturumu açılamadı: ekran kartı aynı anda başka bir kodlamayla meşgul olabilir. Onu bitirip yeniden deneyin."),
        (["Invalid Level", "Unsupported Level"],
            "NVENC bu H.264 seviyesini kabul etmedi: seviye, videonun boyutu, kare hızı, bit hızı ya da referans kare sayısı için düşük. Seviyeyi \"Kaynakla aynı\" ya da \"Otomatik\" yapın."),
        (["Frame Dimension less than the minimum"],
            "NVENC bu kadar küçük görüntüyü kodlayamıyor. Çözünürlüğü büyütün ya da kodlayıcıyı x264 yapın."),
        (["No capable devices found"],
            "NVENC bu videoyu kodlayamıyor: ekran kartı bu boyutu ya da istenen özelliği desteklemiyor (H.264'te en fazla 4096 piksel). Çözünürlüğü düşürün ya da kodlayıcıyı x264 yapın."),
        (["Weighted Prediction not supported with B-frames"],
            "NVENC ağırlıklı tahmini B-karelerle birlikte kabul etmiyor: ağırlıklı tahmini kapatın ya da B-kare sayısını 0 yapın (ek parametrelerdeki -bf de)."),
        (["DPB size should be greater than or equal to"],
            "NVENC: B-kareler referans olarak kullanılınca referans kare (DPB) en az 3 olmalı. Referans kare sayısını artırın ya da B-kare referans modunu kapatın (ek parametrelerdeki -dpb_size de)."),
        (["Adaptive quantization is not supported with lossless encoding"],
            "NVENC kayıpsız kodlamada uyarlamalı nicelemeyi (AQ) kabul etmiyor: ek parametrelerdeki -spatial-aq / -temporal-aq'yu kaldırın."),
        (["InitializeEncoder failed", "Provided device doesn't support required NVENC features", "Lossless encoding not supported",
          "B frames as references are not supported", "Weighted prediction"],
            "NVENC bu ayarları kabul etmedi (profil, seviye, B-kare ya da ağırlıklı tahmin kartta desteklenmiyor). NVENC ayarlarını değiştirin."),
        (["No space left on device", "There is not enough space on the disk"],
            "Diskte yer kalmadı. Kayıt yerinde yer açın ya da başka bir kayıt yeri seçin."),
        (["Permission denied"],
            "Dosyaya yazılamadı: kayıt yerine yazma izni yok ya da dosya başka bir programda açık."),
        (["malloc of size", "Cannot allocate memory"],
            "Bellek yetmedi: kodlayıcı bu ayarlarla bilgisayarın belleğinden fazlasını istiyor. Çözünürlüğü, iş parçacığı sayısını ya da önden bakışı düşürün; açık başka programları kapatın."),
        (["Unable to set the bitrate", "Unable to set the AOT", "Unable to initialize the encoder", "Invalid config"],
            "FDK AAC bu birleşimi kabul etmedi: profil, bit hızı, örnekleme hızı ve kanal sayısı birbirine uymuyor (ör. HE-AAC için bit hızı çok yüksek)."),
        (["Incomplete MB-tree stats file", "stats file", "ratecontrol_init: can't open stats file"],
            "Çok geçişli x264'te geçiş dosyası okunamadı. Bir kez daha deneyin; sürerse tek geçişli (CRF) kullanın."),
        (["moov atom not found", "Invalid data found when processing input", "could not find codec parameters"],
            "Video dosyası okunamadı: bozuk, yarım kopyalanmış ya da desteklenmeyen bir dosya."),
        (["No such file or directory"],
            "Dosya ya da klasör bulunamadı: silinmiş, taşınmış ya da adı değişmiş olabilir."),
        (["Error while opening encoder", "Error initializing output stream", "Could not open encoder"],
            "Kodlayıcı bu ayarlarla açılamadı (ayarlar birbirine uymuyor). Günlükte hangi ayarın reddedildiği yazar."),
        (["height not divisible by 2", "width not divisible by 2"],
            "Çözünürlük çift sayı olmalı (genişlik ve yükseklik)."),
    ];

    /// <summary>
    /// Satırda gösterilecek hata yazısı. ffmpeg ve okuma hataları zaten Türkçe açıklamalıdır; dosya sistemi hataları
    /// (kayıt yerine yazma izni yok, klasör / sürücü yok, disk dolu) burada Türkçeleştirilir, asıl ileti alt satırda kalır.
    /// </summary>
    public static string Metin(Exception ex) => ex switch
    {
        FfmpegHatasi => ex.Message,
        UnauthorizedAccessException =>
            "Kayıt yerine yazılamadı: yazma izni yok. Başka bir kayıt yeri seçin." + Environment.NewLine + ex.Message,
        DirectoryNotFoundException or DriveNotFoundException =>
            "Kayıt yeri bulunamadı: klasör ya da sürücü yok (disk çıkarılmış olabilir). Başka bir kayıt yeri seçin."
            + Environment.NewLine + ex.Message,
        IOException io when (io.HResult & 0xFFFF) is 0x70 or 0x27 =>
            "Diskte yer kalmadı. Kayıt yerinde yer açın ya da başka bir kayıt yeri seçin." + Environment.NewLine + ex.Message,
        _ => ex.Message,
    };

    /// <summary>
    /// Çıkış kodu bir çökmeyi mi gösteriyor (Windows NTSTATUS hata kodları: 0xC0000005 erişim ihlali, 0xC0000409 güvenlik
    /// denetimi / abort, 0xC00000FD yığın taşması, 0xC000001D geçersiz komut, 0xC0000094 sıfıra bölme...).
    /// </summary>
    public static bool CokmeMi(int kod) => (uint)kod is >= 0xC0000000u and <= 0xC0FFFFFFu;

    /// <summary>Çöken ffmpeg için Türkçe açıklama.</summary>
    public static string CokmeAciklamasi(int kod) =>
        $"FFmpeg beklenmedik biçimde kapandı (kod 0x{(uint)kod:X8}{((uint)kod == 0xC0000005u ? ", erişim ihlali" : (uint)kod == 0xC0000409u ? ", iç denetim" : string.Empty)}): "
        + "bu ayar birleşimi kodlayıcıda bir hataya yol açıyor. Ayarları değiştirip (ör. nicemleme matrisi, geçmeli kodlama, iş parçacığı) yeniden deneyin.";

    /// <summary>Bilinen bir hata ise Türkçe açıklaması, değilse null.</summary>
    public static string? Bul(string ffmpegCiktisi)
    {
        foreach (var (desenler, aciklama) in Bilinenler)
        {
            if (desenler.Any(d => ffmpegCiktisi.Contains(d, StringComparison.OrdinalIgnoreCase)))
            {
                return aciklama;
            }
        }

        return null;
    }

    /// <summary>
    /// Hata günlüğünü veri klasöründeki "Günlük" klasörüne yazar (yazılamazsa geçici klasöre). Veri klasörü exe'nin
    /// yanıdır; exe bir arşivin içinden çalışıyorsa %LOCALAPPDATA%\Relios Export (arşiv kapanınca silinmesin diye).
    /// Dosya yolunu döndürür; hiçbir yere yazılamazsa null.
    /// </summary>
    public static string? GunlukYaz(string kaynak, string icerik)
    {
        var ad = $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} {Temizle(Path.GetFileNameWithoutExtension(kaynak))}.txt";
        foreach (var klasor in new[]
                 {
                     Path.Combine(AppPaths.VeriKlasoru, "Günlük"),
                     Path.Combine(Path.GetTempPath(), "Relios Export", "Günlük"),
                 })
        {
            try
            {
                Directory.CreateDirectory(klasor);
                var yol = Path.Combine(klasor, ad);
                File.WriteAllText(yol, icerik, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                return yol;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // sonraki klasörü dene
            }
        }

        return null;
    }

    private static string Temizle(string ad)
    {
        var gecersiz = Path.GetInvalidFileNameChars();
        var s = new string(ad.Select(c => gecersiz.Contains(c) ? '_' : c).ToArray());
        return s.Length > 80 ? s[..80] : s;
    }
}
