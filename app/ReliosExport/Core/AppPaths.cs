using System.IO;

namespace ReliosExport.Core;

/// <summary>
/// Programın klasörleri.
/// <para><see cref="AppFolder"/>: "Relios Export.exe"nin bulunduğu klasör. Tek exe paketi çalışırken dosyalarını
/// geçici klasöre açtığı için AppContext.BaseDirectory orayı gösterir; gömülü ffmpeg yoksa exe'nin yanındaki
/// "ffmpeg" klasörüne bakılır.</para>
/// <para><see cref="VeriKlasoru"/>: ayarlar, hata günlükleri ve hata kaydı. Normalde exe'nin yanıdır. Exe bir
/// arşivin (rar, 7z, zip) içinden çalıştırılınca arşiv programı onu geçici bir klasöre açar ve arşiv kapanınca
/// o klasörü siler; ayarlar ve günlükler kaybolmasın diye o durumda %LOCALAPPDATA%\Relios Export kullanılır.</para>
/// </summary>
public static class AppPaths
{
    public static string AppFolder { get; } =
        Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } dir ? dir : AppContext.BaseDirectory;

    /// <summary>Exe geçici klasörden (çoğunlukla bir arşivin içinden) mi çalışıyor.</summary>
    public static bool GeciciKlasorden { get; } = GeciciKlasordeMi(AppFolder);

    public static string VeriKlasoru { get; } = GeciciKlasorden ? YerelKlasor() ?? AppFolder : AppFolder;

    private static string? YerelKlasor()
    {
        var yerel = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(yerel) ? null : Path.Combine(yerel, "Relios Export");
    }

    /// <summary>Klasör kullanıcının geçici klasörünün içinde mi (arşiv programlarının açtığı klasörler dahil).</summary>
    public static bool GeciciKlasordeMi(string klasor)
    {
        try
        {
            var tam = Normal(klasor);

            // Arşiv programlarının açtığı klasör adları: rar "Rar$EX...", 7z "7zO...", zip "Temp1_...".
            foreach (var parca in tam.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                if (parca.StartsWith("Rar$EX", StringComparison.OrdinalIgnoreCase)
                    || parca.StartsWith("7zO", StringComparison.Ordinal)
                    || parca.StartsWith("Temp1_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var yerel = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var geciciler = new List<string> { Path.GetTempPath() };
            if (!string.IsNullOrEmpty(yerel))
            {
                geciciler.Add(Path.Combine(yerel, "Temp"));
            }

            return geciciler.Any(g => !string.IsNullOrEmpty(g) && tam.StartsWith(Normal(g), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Normal(string yol) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(yol)) + Path.DirectorySeparatorChar;
}
