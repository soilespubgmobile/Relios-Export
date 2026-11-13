using System.IO;

namespace ReliosExport.Core;

/// <summary>
/// Programın bulunduğu klasör ("Relios Export.exe"nin yanı).
/// Tek exe paketi çalışırken dosyalarını geçici klasöre açtığı için AppContext.BaseDirectory
/// orayı gösterir; ffmpeg klasörü, ayarlar ve hata kaydı ise exe'nin yanında durur.
/// </summary>
public static class AppPaths
{
    public static string AppFolder { get; } =
        Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } dir ? dir : AppContext.BaseDirectory;
}
