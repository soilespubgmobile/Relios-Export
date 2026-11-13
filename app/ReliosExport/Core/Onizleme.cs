using System.Globalization;

namespace ReliosExport.Core;

/// <summary>Kırpma önizlemesi: kaynaktan ya da çıktı filtresinden tek kare (PNG).</summary>
public static class Onizleme
{
    /// <param name="vf">null: kaynağın kendisi (döndürülmüş, kare pikselli); değilse çıktıdaki -vf filtresi.</param>
    public static Task KareAsync(string input, double saniye, string? vf, string png, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-ss", Math.Max(0, saniye).ToString("0.###", CultureInfo.InvariantCulture),
            "-i", input,
            "-map", "0:v:0",
            "-frames:v", "1",
            "-vf", vf ?? "scale=trunc(iw*sar/2)*2:trunc(ih/2)*2,setsar=1",
            "-an",
            "-c:v", "png",
            "-f", "image2",
            png,
        };
        return FfmpegRunner.RunAsync(args, 0, null, ct);
    }
}
