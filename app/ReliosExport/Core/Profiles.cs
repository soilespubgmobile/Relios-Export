namespace ReliosExport.Core;

/// <summary>Programdaki dışa aktarma profilleri; her birinin kendi ayarları vardır.</summary>
public enum ExportProfileId
{
    TikTok,
    YouTube,
}

public sealed record ExportProfile(ExportProfileId Id, string DisplayName, string FileSuffix);

public static class Profiles
{
    /// <summary>TikTok: varsayılanı XviD4PSP'de doğrulanan "Relios 9x16" (1216×2160, 29,97 fps, CRF 18).</summary>
    public static readonly ExportProfile TikTok = new(ExportProfileId.TikTok, "TikTok", "_TikTok");

    /// <summary>YouTube: varsayılanı kaynakla aynı çözünürlük ve kare hızı, yüksek kalite.</summary>
    public static readonly ExportProfile YouTube = new(ExportProfileId.YouTube, "YouTube", "_YouTube");

    public static IReadOnlyList<ExportProfile> All { get; } = [TikTok, YouTube];

    public static ExportProfile Get(ExportProfileId id) => id == ExportProfileId.TikTok ? TikTok : YouTube;
}
