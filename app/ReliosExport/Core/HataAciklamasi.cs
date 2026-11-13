using System.IO;
using System.Text;

namespace ReliosExport.Core;

/// <summary>ffmpeg başarısız olduğunda fırlatılır: Türkçe açıklama + tam ffmpeg günlüğü.</summary>
public sealed class FfmpegHatasi(string mesaj, string gunluk) : Exception(mesaj)
{
    /// <summary>ffmpeg'in hata çıktısı (uzunsa başı ve sonu).</summary>
    public string Gunluk { get; } = gunluk;

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
            "NVENC oturumu açılamadı: ekran kartı başka bir kodlamayla (OBS vb.) meşgul olabilir. Onu kapatıp yeniden deneyin."),
        (["InitializeEncoder failed", "Provided device doesn't support required NVENC features", "Lossless encoding not supported",
          "B frames as references are not supported", "Weighted prediction"],
            "NVENC bu ayarları kabul etmedi (profil, seviye, B-kare ya da ağırlıklı tahmin kartta desteklenmiyor). NVENC ayarlarını değiştirin."),
        (["No space left on device", "There is not enough space on the disk"],
            "Diskte yer kalmadı. Kayıt yerinde yer açın ya da başka bir kayıt yeri seçin."),
        (["Permission denied"],
            "Dosyaya yazılamadı: kayıt yerine yazma izni yok ya da dosya başka bir programda açık."),
        (["Unable to set the bitrate", "Unable to set the AOT", "Unable to initialize the encoder", "Invalid config"],
            "FDK AAC bu birleşimi kabul etmedi: profil, bit hızı, örnekleme hızı ve kanal sayısı birbirine uymuyor (ör. HE-AAC için bit hızı çok yüksek)."),
        (["Incomplete MB-tree stats file", "stats file", "ratecontrol_init: can't open stats file"],
            "Çok geçişli x264'te geçiş dosyası okunamadı. Bir kez daha deneyin; sürerse tek geçişli (CRF) kullanın."),
        (["moov atom not found", "Invalid data found when processing input", "could not find codec parameters"],
            "Video dosyası okunamadı: bozuk, yarım kopyalanmış ya da desteklenmeyen bir dosya."),
        (["Error while opening encoder", "Error initializing output stream", "Could not open encoder"],
            "Kodlayıcı bu ayarlarla açılamadı (ayarlar birbirine uymuyor). Günlükte hangi ayarın reddedildiği yazar."),
        (["height not divisible by 2", "width not divisible by 2"],
            "Çözünürlük çift sayı olmalı (genişlik ve yükseklik)."),
    ];

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
    /// Hata günlüğünü program klasöründeki "Günlük" klasörüne yazar (yazılamazsa geçici klasöre).
    /// Dosya yolunu döndürür; hiçbir yere yazılamazsa null.
    /// </summary>
    public static string? GunlukYaz(string kaynak, string icerik)
    {
        var ad = $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} {Temizle(Path.GetFileNameWithoutExtension(kaynak))}.txt";
        foreach (var klasor in new[]
                 {
                     Path.Combine(AppPaths.AppFolder, "Günlük"),
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
