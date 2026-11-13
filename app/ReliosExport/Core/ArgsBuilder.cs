using System.Globalization;

namespace ReliosExport.Core;

/// <summary>9:16'ya kırpma hesabının sonucu.</summary>
public sealed record CropPlan(int Width, int Height, int X, int Y, bool IsNeeded);

/// <summary>Profil + kaynak bilgisinden ffmpeg komut satırını üretir.</summary>
public static class ArgsBuilder
{
    // TikTok hedefi (XviD4PSP "Relios 9x16" ile doğrulanan değerler)
    public const int TikTokWidth = 1216;
    public const int TikTokHeight = 2160;

    /// <summary>
    /// Kaynağı ortadan 9:16'ya kırpma planı. Kaynak zaten 9:16 ise kırpmaz.
    /// Örnek: 1920x1080 → 608x1080 (sol/sağ 656), 1080x1080 → 608x1080 (sol/sağ 236).
    /// </summary>
    public static CropPlan PlanCrop916(MediaInfo m)
    {
        // ffmpeg döndürmeyi otomatik uyguladığı için kırpma döndürülmüş görüntüye göre yapılır.
        var w = m.DisplayWidth;
        var h = m.DisplayHeight;
        var sar = m.Rotation is 90 or 270 ? 1.0 / m.Sar : m.Sar;
        const double target = 9.0 / 16.0;
        var displayAspect = w * sar / h;

        if (Math.Abs(displayAspect - target) < 0.002)
        {
            return new CropPlan(w, h, 0, 0, IsNeeded: false);
        }

        if (displayAspect > target)
        {
            // Kaynak daha geniş: yanlardan kırp.
            var cw = Even(h * target / sar);
            cw = Math.Min(cw, w - (w % 2));
            return new CropPlan(cw, h, (w - cw) / 2, 0, IsNeeded: true);
        }

        // Kaynak daha dar (ör. 9:19.5 telefon kaydı): üstten/alttan kırp.
        var ch = Even(w * sar / target);
        ch = Math.Min(ch, h - (h % 2));
        return new CropPlan(w, ch, 0, (h - ch) / 2, IsNeeded: true);
    }

    public static IReadOnlyList<string> Build(
        ExportProfile profile,
        MediaInfo media,
        string input,
        string output)
    {
        return profile.Id switch
        {
            ExportProfileId.TikTok => BuildTikTok(media, input, output),
            ExportProfileId.YouTube => throw new NotSupportedException("YouTube ayarları henüz belirlenmedi."),
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
    }

    private static List<string> BuildTikTok(MediaInfo media, string input, string output)
    {
        var crop = PlanCrop916(media);
        var filters = new List<string>
        {
            // 60 → 29.97: önce kare hızı (gereksiz ölçekleme yapılmasın)
            "fps=30000/1001",
        };
        if (crop.IsNeeded)
        {
            filters.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}"));
        }

        // Ölçekleme + renk: Limited (TV) aralık, BT.709 matris. Kare piksel, 8-bit 4:2:0.
        // Etiketsiz kaynakta ffmpeg BT.601 varsayar; HD görüntüde bu renkleri kaydırır.
        // Bu yüzden etiketsiz HD kaynak BT.709 kabul edilir (renk olduğu gibi kalır).
        filters.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"scale={TikTokWidth}:{TikTokHeight}:flags=lanczos:in_color_matrix={InputMatrix(media)}:out_range=tv:out_color_matrix=bt709"));
        filters.Add("setsar=1");
        filters.Add("format=yuv420p");
        // Renk primeri ve aktarım her zaman BT.709 etiketlenir (hedef video gibi).
        // FFmpeg 7.1 ve sonrası bu ikisini kareden alıyor; -color_primaries / -color_trc
        // tek başına etiketsiz kaynakta işe yaramıyor (doğrulamada "None" çıktı).
        filters.Add("setparams=color_primaries=bt709:color_trc=bt709");

        // x264: hedef meta verideki "Encoding settings" satırının birebir karşılığı.
        // threads=7 kullanıcının seçimi (işlemcinin tamamı kullanılmasın).
        var x264Params = string.Join(':',
            "threads=7",
            "ref=3",
            "deblock=-1,-1",
            "me=hex",
            "subme=7",
            "psy-rd=1.00,0.15",
            "mixed-refs=1",
            "merange=16",
            "chroma-me=1",
            "trellis=1",
            "8x8dct=0",
            "cqm=flat",
            "deadzone-inter=21",
            "deadzone-intra=11",
            "fast-pskip=1",
            "nr=0",
            "dct-decimate=1",
            "interlaced=0",
            "bluray-compat=0",
            "constrained-intra=0",
            "bframes=0",
            "weightp=2",
            "keyint=50",
            "min-keyint=26",
            "scenecut=0",
            "intra-refresh=0",
            "rc-lookahead=40",
            "mbtree=1",
            "crf=18",
            "qcomp=0.60",
            "qpmin=0",
            "qpmax=33",
            "qpstep=4",
            "vbv-maxrate=14000",
            "vbv-bufsize=28000",
            "nal-hrd=none",
            "filler=0",
            "ipratio=1.40",
            "aq-mode=1",
            "aq-strength=1.00");

        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-i", input,
            "-map", "0:v:0",
        };
        if (media.HasAudio)
        {
            args.AddRange(["-map", "0:a:0"]);
        }

        args.AddRange(
        [
            // Kaynaktaki genel etiketler (ör. Topaz, TikTok etiketleri) çıktıya taşınmasın.
            "-map_metadata", "-1",
            "-map_chapters", "-1",
            "-vf", string.Join(',', filters),
            "-c:v", "libx264",
            "-preset", "medium",
            "-profile:v", "main",
            "-level:v", "5.0",
            "-x264-params", x264Params,
            "-color_range", "tv",
            "-colorspace", "bt709",
            "-color_primaries", "bt709",
            "-color_trc", "bt709",
            "-metadata:s:v:0", "language=eng",
        ]);

        if (media.HasAudio)
        {
            args.AddRange(
            [
                "-af", "aresample=48000:resampler=soxr",
                "-c:a", "libfdk_aac",
                "-profile:a", "aac_low",
                "-b:a", "128k",
                "-ar", "48000",
                "-ac", "2",
                "-metadata:s:a:0", "language=eng",
            ]);
        }

        args.AddRange(
        [
            "-movflags", "+faststart",
            "-progress", "pipe:1",
            "-stats_period", "0.5",
            output,
        ]);

        return args;
    }

    /// <summary>Kaynak renk matrisi: etiket varsa ffmpeg okur (auto), yoksa çözünürlüğe göre BT.709 / BT.601.</summary>
    public static string InputMatrix(MediaInfo m)
    {
        if (!m.ColorSpaceUnknown)
        {
            return "auto";
        }

        var w = m.DisplayWidth;
        var h = m.DisplayHeight;
        return Math.Min(w, h) >= 720 || Math.Max(w, h) >= 1280 ? "bt709" : "bt601";
    }

    private static int Even(double v)
    {
        var r = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        return r % 2 == 0 ? r : r + 1;
    }
}
