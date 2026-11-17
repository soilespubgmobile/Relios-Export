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

    private static string FilePath => Path.Combine(AppPaths.VeriKlasoru, "ayarlar.json");

    /// <summary>Profilin ayarları; kayıt yoksa ya da bozuksa fabrika ayarları.</summary>
    public ProfilAyarlari ProfilAyari(ExportProfileId id)
    {
        Profiller.TryGetValue(id, out var kayit);
        var a = Yukselt(id, kayit, onAyar: false);
        if (!ReferenceEquals(a, kayit))
        {
            Profiller[id] = a;
        }

        return a;
    }

    /// <summary>
    /// Kaydedilmiş bir ön ayarı profilin ayarı yapar. Ön ayar kullanıcının adını verip kaydettiği ayardır:
    /// eski sürümde kaydedildiyse de sesi kaydedildiği gibi kalır (ses varsayılanının değişmesi ona yansımaz).
    /// </summary>
    public ProfilAyarlari OnAyariYukle(ExportProfileId id, ProfilAyarlari kayit) =>
        Profiller[id] = Yukselt(id, kayit, onAyar: true);

    private static ProfilAyarlari Yukselt(ExportProfileId id, ProfilAyarlari? a, bool onAyar)
    {
        // Eski sürümlerde kaydedilmiş YouTube ayarı eski varsayılandır: yenisiyle değişir.
        if (a is null
            || a.Surum is < 1 or > ProfilAyarlari.GuncelSurum
            || (id == ExportProfileId.YouTube && a.Surum < 3)
            || (id == ExportProfileId.TikTok && a.Surum < 4))
        {
            a = Varsayilanlar.For(id);
        }
        else
        {
            if (a.Surum < 3 && a.Video.X264.IsParcacigi == 7)
            {
                // 7 iş parçacığı eski varsayılandı; artık 8 (işlemcinin çekirdek sayısı).
                a.Video.X264.IsParcacigi = 8;
            }

            if (id == ExportProfileId.YouTube && a.Surum < 5
                && a.AnahtarKare is { Birim: AralikBirimi.Kare, EnBuyuk: 50.0, EnKucuk: 26.0 })
            {
                // Eski YouTube varsayılanı (her kare hızında 50 / 26): artık 30 fps'e göre (60 fps'te 100 / 51).
                a.AnahtarKare.Birim = AralikBirimi.Kare30;
            }

            if (a.Surum < 6)
            {
                // Dosya biçimi ayarı yoktu (hep MP4); TikTok ve YouTube'da varsayılan artık MOV.
                a.Video.Kap = KapBicimi.Mov;
            }

            if (id == ExportProfileId.TikTok && a.Surum == 7
                && a.Video.Nvenc is { Mod: NvencModu.Cbr, BitHizi: 34375, Tampon: 55000 })
            {
                // 0.8.6 TikTok tamponunu kendiliğinden 55000 kb yapmıştı; varsayılan yine boş (bit hızı kadar).
                a.Video.Nvenc.Tampon = null;
            }

            if (!onAyar && a.Surum < 9
                && a.Ses is { Kodlayici: SesKodlayici.FdkAac, OrnekHizi: 48000, Kanal: KanalModu.Stereo, Kalite: DonusumKalitesi.Yuksek, Titresim: Titresim.Yok }
                && a.Ses.Fdk is
                {
                    Profil: "aac_low", Mod: FdkModu.Cbr, BitHizi: 128, Kesim: 0, Afterburner: true, Sinyal: "default",
                    DrcProfili: null, SikistirmaProfili: null, DrcHedef: null, SikistirmaHedef: null, ProgramSeviyesi: null,
                })
            {
                // Eski ses varsayılanı (FDK AAC 128 kb/s 48 kHz): artık WAV 16 bit, en yüksek dönüşüm kalitesi,
                // üçgen titreşim. Kullanıcının değiştirdiği ses ayarına dokunulmaz.
                Varsayilanlar.PlatformSesi(a.Ses);
            }

            if (!onAyar && a.Surum < 10)
            {
                // 0.8.9'da LUFS eşitleme varsayılan kapalıydı; artık TikTok ve YouTube'da açık. Ön ayarlar
                // kaydedildiği gibi kalır.
                a.Ses.Esitleme = true;
            }

            if (!onAyar && id == ExportProfileId.YouTube && a.Surum < 11
                && a.Ses is
                {
                    Kodlayici: SesKodlayici.Wav, WavBit: WavBitDerinligi.Bit16, OrnekHizi: 48000, Kanal: KanalModu.Stereo,
                    Kalite: DonusumKalitesi.EnYuksek, Titresim: Titresim.Ucgen,
                })
            {
                // 0.8.9'un YouTube sesi (WAV 16 bit 48 kHz): artık FDK AAC 128 kb/s 48 kHz, en yüksek dönüşüm kalitesi,
                // üçgen titreşim. LUFS eşitleme seçimi, kullanıcının değiştirdiği ses ayarı, TikTok ve ön ayarlar
                // olduğu gibi kalır.
                Varsayilanlar.YouTubeSesi(a.Ses);
            }

            if (id == ExportProfileId.YouTube && a.Surum < 8
                && a.Video.X264 is { VbvMaks: 14000, VbvTampon: 28000 })
            {
                // Eski YouTube varsayılanı (en yüksek bit hızı 14000 kb/s, tampon 28000 kb): artık 34375 / 55000.
                a.Video.X264.VbvMaks = 34375;
                a.Video.X264.VbvTampon = 55000;
            }

            if (!onAyar && id == ExportProfileId.TikTok && a.Surum < 12)
            {
                // 0.10.0'a kadarki TikTok varsayılanı (NVENC CBR 34375 kb/s, 30 karede bir anahtar kare, WAV 16 bit
                // 48 kHz): değiştirilmemiş kısımlar yeni varsayılana geçer (x264 veryslow CRF 24, en çok 10 saniyede bir
                // anahtar kare, FDK AAC 128 kb/s 44,1 kHz). Kullanıcının değiştirdiği kısım, LUFS eşitleme seçimi ve ön
                // ayarlar olduğu gibi kalır.
                var eski = Varsayilanlar.TikTokNvenc();
                var yeni = Varsayilanlar.TikTok();
                if (AyniAyar(a.Video, eski.Video))
                {
                    a.Video = yeni.Video;
                }

                if (AyniAyar(a.AnahtarKare, eski.AnahtarKare))
                {
                    a.AnahtarKare = yeni.AnahtarKare;
                }

                if (a.Ses is
                    {
                        Kodlayici: SesKodlayici.Wav, WavBit: WavBitDerinligi.Bit16, OrnekHizi: 48000, Kanal: KanalModu.Stereo,
                        Kalite: DonusumKalitesi.EnYuksek, Titresim: Titresim.Ucgen,
                    })
                {
                    Varsayilanlar.TikTokSesi(a.Ses);
                }
            }
        }

        a.Surum = ProfilAyarlari.GuncelSurum;
        return a;
    }

    /// <summary>İki ayar nesnesi (ör. kayıtlı video ayarı ve eski varsayılan) ayar dosyasında birebir aynı mı.</summary>
    private static bool AyniAyar<T>(T x, T y) =>
        JsonSerializer.Serialize(x, AyarDosyasi.Secenekler) == JsonSerializer.Serialize(y, AyarDosyasi.Secenekler);

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
            Directory.CreateDirectory(AppPaths.VeriKlasoru);
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
