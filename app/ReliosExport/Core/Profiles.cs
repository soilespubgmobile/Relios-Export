namespace ReliosExport.Core;

/// <summary>Programdaki dışa aktarma seçenekleri.</summary>
public enum ExportProfileId
{
    TikTok,
    YouTube,
}

/// <param name="Tags">Üst çubuktaki profil özeti etiketleri.</param>
/// <param name="Description">Tek satırlık açıklama; hazır olmayan profilde uyarı olarak görünür.</param>
public sealed record ExportProfile(
    ExportProfileId Id,
    string DisplayName,
    string FileSuffix,
    bool IsReady,
    IReadOnlyList<string> Tags,
    string Description);

public static class Profiles
{
    /// <summary>
    /// TikTok: XviD4PSP'de birlikte doğrulanan "Relios 9x16" mantığının birebir aynısı.
    /// Hedef: 1216x2160 9:16 kare piksel, 29.97 fps, H.264 Main@L5.0, FDK AAC LC 48 kHz 128 kb/s.
    /// </summary>
    public static readonly ExportProfile TikTok = new(
        ExportProfileId.TikTok,
        "TikTok",
        "_TikTok",
        IsReady: true,
        ["1216×2160 · 9:16", "29,97 fps", "H.264 Main@L5.0", "CRF 18", "FDK AAC · 128 kb/s · 48 kHz"],
        "1216x2160 (9:16) · 29.97 fps · H.264 Main@L5.0 CRF 18 · FDK AAC 128 kb/s");

    /// <summary>YouTube: ayrıntıları kullanıcı belirleyecek; o zamana kadar kapalı.</summary>
    public static readonly ExportProfile YouTube = new(
        ExportProfileId.YouTube,
        "YouTube",
        "_YouTube",
        IsReady: false,
        [],
        "YouTube ayarları henüz belirlenmedi");

    public static IReadOnlyList<ExportProfile> All { get; } = [TikTok, YouTube];
}
