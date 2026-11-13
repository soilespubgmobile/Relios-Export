using ReliosExport.Core;

// Kullanım: ReliosExport.Smoke.exe <kaynak video> <çıktı klasörü>
// ffmpeg.exe ve ffprobe.exe bu exe'nin yanındaki "ffmpeg" klasöründe olmalı (programdaki gibi).
if (args.Length < 2)
{
    Console.Error.WriteLine("Kullanım: ReliosExport.Smoke <kaynak> <çıktı klasörü>");
    return 2;
}

var input = args[0];
var outDir = args[1];
Console.WriteLine($"ffmpeg: {Tools.Ffmpeg} (bulundu: {Tools.Available})");
if (!Tools.Available)
{
    return 3;
}

var media = await MediaProbe.ProbeAsync(Tools.Ffprobe, input, CancellationToken.None);
Console.WriteLine($"Kaynak: {Exporter.Describe(media)}");
var preview = ArgsBuilder.Build(Profiles.TikTok, media, input, "CIKTI.mp4");
Console.WriteLine("Komut: ffmpeg " + string.Join(' ', preview.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));

var last = -1;
var progress = new Progress<double>(p =>
{
    var pc = (int)(p * 100);
    if (pc / 20 != last / 20)
    {
        last = pc;
        Console.WriteLine($"  %{pc}");
    }
});

var output = await Exporter.ExportAsync(
    input, outDir, Profiles.TikTok, progress, info => Console.WriteLine("Bilgi: " + info), CancellationToken.None);
Console.WriteLine("Çıktı: " + output);
return 0;
