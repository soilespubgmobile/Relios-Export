using System.IO;

namespace ReliosExport.Core;

/// <summary>
/// Videonun yanındaki aynı adlı ses dosyası (XviD4PSP'deki "External" ses gibi):
/// "edit1.mp4" için aynı klasörde "edit1.wav" (ya da başka bir ses biçimi).
/// </summary>
public static class HariciSes
{
    /// <summary>Aranan uzantılar, öncelik sırasıyla (WAV önce).</summary>
    public static readonly string[] Uzantilar =
        [".wav", ".w64", ".flac", ".aif", ".aiff", ".m4a", ".aac", ".mp3", ".opus", ".ogg", ".ac3", ".eac3", ".mka"];

    public static string? Bul(string videoYolu)
    {
        try
        {
            var klasor = Path.GetDirectoryName(videoYolu);
            var ad = Path.GetFileNameWithoutExtension(videoYolu);
            if (string.IsNullOrEmpty(klasor) || string.IsNullOrEmpty(ad))
            {
                return null;
            }

            foreach (var uzanti in Uzantilar)
            {
                var yol = Path.Combine(klasor, ad + uzanti);
                if (File.Exists(yol)
                    && !string.Equals(Path.GetFullPath(yol), Path.GetFullPath(videoYolu), StringComparison.OrdinalIgnoreCase))
                {
                    return yol;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Okunamayan yol: harici ses yok sayılır.
        }

        return null;
    }

    /// <summary>Dosya bir ses dosyası mı (listeye video olarak eklenmesin).</summary>
    public static bool SesDosyasiMi(string yol) =>
        Array.Exists(Uzantilar, u => string.Equals(Path.GetExtension(yol), u, StringComparison.OrdinalIgnoreCase));
}
