using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Core;

/// <summary>Kullanıcının adını verip kaydettiği ayarlar.</summary>
public sealed class KayitliOnAyar
{
    [JsonPropertyName("ad")]
    public string Ad { get; set; } = string.Empty;

    [JsonPropertyName("profil")]
    public ExportProfileId Profil { get; set; }

    [JsonPropertyName("ayarlar")]
    public ProfilAyarlari Ayarlar { get; set; } = new();
}

/// <summary>
/// Programın hatırladığı ayarlar: program klasöründeki "ayarlar.json" (taşınabilir, kurulum yok).
/// Klasör yazılamıyorsa ayarlar sessizce kaydedilmez; program yine çalışır.
/// </summary>
public sealed class AppSettings
{
    [JsonPropertyName("kayit_yeri")]
    public string? OutputFolder { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    /// <summary>Her profilin son kullanılan ayarları.</summary>
    [JsonPropertyName("profiller")]
    public Dictionary<ExportProfileId, ProfilAyarlari> Profiller { get; set; } = [];

    [JsonPropertyName("on_ayarlar")]
    public List<KayitliOnAyar> OnAyarlar { get; set; } = [];

    private static string FilePath => Path.Combine(AppPaths.AppFolder, "ayarlar.json");

    /// <summary>Profilin ayarları; kayıt yoksa ya da bozuksa fabrika ayarları.</summary>
    public ProfilAyarlari ProfilAyari(ExportProfileId id)
    {
        // Eski sürümlerde kaydedilmiş YouTube ayarı eski varsayılandır: yenisiyle değişir.
        if (!Profiller.TryGetValue(id, out var a) || a is null
            || a.Surum is < 1 or > ProfilAyarlari.GuncelSurum
            || (id == ExportProfileId.YouTube && a.Surum < 3))
        {
            a = Varsayilanlar.For(id);
            Profiller[id] = a;
        }
        else if (a.Surum < 3 && a.Video.X264.IsParcacigi == 7)
        {
            // 7 iş parçacığı eski varsayılandı; artık 8 (işlemcinin çekirdek sayısı).
            a.Video.X264.IsParcacigi = 8;
        }

        a.Surum = ProfilAyarlari.GuncelSurum;
        return a;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), AyarDosyasi.Secenekler)
                       ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Bozuk ya da okunamayan dosya: varsayılanlarla devam.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, AyarDosyasi.Secenekler));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Salt okunur klasör: kaydetmeden devam.
        }
    }
}
