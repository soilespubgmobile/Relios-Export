using ReliosExport.Core;

// Kullanım: ReliosExport.EskiKomut <ffprobe.exe> <kaynak video>
// 0.3.0'ın TikTok komutunu, yeni programın deneme aracındaki (Smoke --sadece-komut) biçimde yazar:
// kaynak yolu KAYNAK, çıktı CIKTI.mp4 olarak.
Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length < 2)
{
    Console.Error.WriteLine("Kullanım: ReliosExport.EskiKomut <ffprobe.exe> <kaynak>");
    return 2;
}

var media = await MediaProbe.ProbeAsync(args[0], args[1], CancellationToken.None);
var komut = ArgsBuilder.Build(Profiles.TikTok, media, args[1], "CIKTI.mp4");
Console.WriteLine("ffmpeg " + string.Join(' ', komut.Select(a => a == args[1] ? "KAYNAK" : a.Contains(' ') ? $"\"{a}\"" : a)));
return 0;
