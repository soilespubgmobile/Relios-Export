using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ReliosExport.Core;

namespace ReliosExport;

/// <summary>Beklenmeyen hataları dosyaya yazar ve kullanıcıya gösterir (ağ yok, yalnızca yerel dosya).</summary>
internal static class CrashReport
{
    private const string FileName = "Relios Export hata.txt";
    private static int _shown;

    /// <summary>Derleme kontrolünde true: hata penceresi açılmaz, program hata koduyla kapanır.</summary>
    public static bool Silent { get; set; }

    public static string? Write(Exception? ex, string source)
    {
        var text = new StringBuilder()
            .AppendLine($"Zaman: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Sürüm: {typeof(CrashReport).Assembly.GetName().Version}")
            .AppendLine($"Windows: {RuntimeInformation.OSDescription} ({Environment.OSVersion.Version})")
            .AppendLine($".NET: {RuntimeInformation.FrameworkDescription}")
            .AppendLine($"Klasör: {AppPaths.AppFolder}")
            .AppendLine($"Kaynak: {source}")
            .AppendLine()
            .AppendLine(ex?.ToString() ?? "(ayrıntı yok)")
            .AppendLine(new string('-', 60))
            .ToString();

        foreach (var dir in new[] { AppPaths.AppFolder, Path.GetTempPath() })
        {
            try
            {
                var path = Path.Combine(dir, FileName);
                File.AppendAllText(path, text, Encoding.UTF8);
                return path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // sıradaki klasörü dene
            }
        }

        return null;
    }

    public static void Show(Exception? ex, string source)
    {
        var path = Write(ex, source);
        if (Silent)
        {
            Environment.Exit(4);
        }

        if (Interlocked.Exchange(ref _shown, 1) == 1)
        {
            return;
        }

        var message = ex?.GetBaseException().Message ?? "Bilinmeyen hata";
        try
        {
            // WPF'e bağlı olmayan Windows mesaj kutusu: WPF açılamasa bile görünür.
            MessageBoxW(
                IntPtr.Zero,
                $"Relios Export beklenmeyen bir hatayla karşılaştı:\n\n{message}\n\nAyrıntılar şu dosyada:\n{path ?? "(dosya yazılamadı)"}",
                "Relios Export",
                MbOk | MbIconError | MbSetForeground);
        }
        catch (Exception)
        {
            // Ekran gösterilemiyorsa en azından dosya yazıldı.
        }
    }

    private const uint MbOk = 0x0;
    private const uint MbIconError = 0x10;
    private const uint MbSetForeground = 0x10000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
