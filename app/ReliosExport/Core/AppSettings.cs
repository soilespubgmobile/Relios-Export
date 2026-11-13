using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReliosExport.Core;

/// <summary>
/// Programın hatırladığı ayarlar: program klasöründeki "ayarlar.json" (taşınabilir, kurulum yok).
/// Klasör yazılamıyorsa ayarlar sessizce kaydedilmez; program yine çalışır.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [JsonPropertyName("kayit_yeri")]
    public string? OutputFolder { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    private static string FilePath => Path.Combine(AppPaths.AppFolder, "ayarlar.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Bozuk ya da okunamayan dosya: varsayılanlarla devam.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Salt okunur klasör: kaydetmeden devam.
        }
    }
}
