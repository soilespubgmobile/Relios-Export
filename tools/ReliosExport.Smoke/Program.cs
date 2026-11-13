using System.Globalization;
using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

// Relios Export'un kodlama kodunu (app/ReliosExport/Core) arayüz olmadan çalıştıran deneme aracı.
// Kullanım: ReliosExport.Smoke.exe <kaynak video> <çıktı klasörü> [--profil tiktok|tiktok-916|youtube] [--ayar ayar.json]
//           [--ayarla Yol=Deger ...] [--sadece-komut]
//           ReliosExport.Smoke.exe --birim-kontrol   (video gerektirmeyen kontroller)
//   --ayar          profil ayarlarını JSON dosyasından alır (yoksa profilin fabrika ayarları)
//   --ayarla        tek bir ayarı değiştirir, ör. --ayarla Video.X264.Mod=IkiGecis --ayarla Ses.Kodlayici=Wav
//   --sadece-komut  kodlamaz; komutları yer tutucularla (KAYNAK, SES, CIKTI) yazar
// ffmpeg.exe ve ffprobe.exe bu exe'nin yanındaki "ffmpeg" klasöründe olmalı (programdaki gibi).

// Türkçe karakterler günlükte bozulmasın.
Console.OutputEncoding = System.Text.Encoding.UTF8;

// Video gerektirmeyen kontroller: seviye hesabı, NVENC komutu, veri klasörü.
if (args.Length >= 1 && args[0] == "--birim-kontrol")
{
    return BirimKontrol.Calistir();
}

if (args.Length < 2)
{
    Console.Error.WriteLine("Kullanım: ReliosExport.Smoke <kaynak> <çıktı klasörü> [--profil tiktok|tiktok-916|youtube] [--ayar dosya.json] [--sadece-komut]");
    return 2;
}

var input = args[0];
var outDir = args[1];
var profil = Profiles.TikTok;
ProfilAyarlari? ayarlar = null;
var degisiklikler = new List<string>();
var sadeceKomut = false;
for (var i = 2; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--profil" when i + 1 < args.Length:
            var ad = args[++i].ToLowerInvariant();
            profil = ad == "youtube" ? Profiles.YouTube : Profiles.TikTok;
            if (ad == "tiktok-916")
            {
                // Hazır ön ayar: dikey 9:16 (x264, 1216x2160, 29,97 fps) — doğrulama bunu kullanır.
                ayarlar = Varsayilanlar.Dikey916();
            }
            break;
        case "--ayar" when i + 1 < args.Length:
            ayarlar = AyarDosyasi.Oku(File.ReadAllText(args[++i]))
                      ?? throw new InvalidOperationException("Ayar dosyası okunamadı: " + args[i]);
            break;
        case "--ayarla" when i + 1 < args.Length:
            degisiklikler.Add(args[++i]);
            break;
        case "--sadece-komut":
            sadeceKomut = true;
            break;
    }
}

ayarlar ??= Varsayilanlar.For(profil.Id);
foreach (var d in degisiklikler)
{
    Ayarla(ayarlar, d);
    Console.WriteLine("Ayar: " + d);
}

Console.WriteLine($"ffmpeg: {Tools.Ffmpeg} (bulundu: {Tools.Available})");
if (!Tools.Available)
{
    return 3;
}

var media = await MediaProbe.ProbeAsync(Tools.Ffprobe, input, CancellationToken.None);
var ses = await ArgsBuilder.SesSecAsync(Tools.Ffprobe, ayarlar.Ses, media, input, CancellationToken.None);
Console.WriteLine($"Profil: {profil.DisplayName}");
Console.WriteLine($"Kaynak: {Exporter.Describe(media, ayarlar, ses)}");
Console.WriteLine($"Video: {ayarlar.Video.Ozet} · {ayarlar.Video.KodlayiciOzeti}");
Console.WriteLine($"Ses ayarı: {ayarlar.Ses.KaynakOzeti} · {ayarlar.Ses.KodlayiciOzeti}");

var plan = ArgsBuilder.Olustur(ayarlar, media, input, ses, "CIKTI" + ArgsBuilder.Uzanti(ayarlar), "GECIS");
for (var g = 0; g < plan.Gecisler.Count; g++)
{
    var komut = string.Join(' ', plan.Gecisler[g].Select(a =>
        a == input ? "KAYNAK" : ses.Dosya is { } d && a == d ? "SES" : a.Contains(' ') ? $"\"{a}\"" : a));
    Console.WriteLine($"Komut {g + 1}/{plan.Gecisler.Count}: ffmpeg {komut}");
}

if (sadeceKomut)
{
    return 0;
}

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

try
{
    var output = await Exporter.ExportAsync(
        input, outDir, profil, ayarlar, progress, info => Console.WriteLine("Bilgi: " + info), CancellationToken.None);
    Console.WriteLine("Çıktı: " + output);
    return 0;
}
catch (FfmpegHatasi ex)
{
    Console.WriteLine("HATA: " + ex.Message);
    Console.WriteLine("Günlük: " + (ex.GunlukDosyasi ?? "(yazılamadı)"));
    return 5;
}

// "Video.X264.BitHizi=4000" gibi bir ifadeyle ayar nesnesindeki tek bir özelliği değiştirir ("null": boş).
static void Ayarla(object kok, string ifade)
{
    var esit = ifade.IndexOf('=');
    if (esit <= 0)
    {
        throw new ArgumentException("--ayarla Yol=Deger biçiminde olmalı: " + ifade);
    }

    var yol = ifade[..esit].Split('.');
    var metin = ifade[(esit + 1)..];
    var hedef = kok;
    for (var i = 0; i < yol.Length - 1; i++)
    {
        hedef = hedef.GetType().GetProperty(yol[i])?.GetValue(hedef)
                ?? throw new ArgumentException("Ayar bulunamadı: " + string.Join('.', yol[..(i + 1)]));
    }

    var p = hedef.GetType().GetProperty(yol[^1]) ?? throw new ArgumentException("Ayar bulunamadı: " + ifade[..esit]);
    var tip = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
    object? deger = metin == "null" ? null
        : tip.IsEnum ? Enum.Parse(tip, metin)
        : tip == typeof(string) ? metin
        : Convert.ChangeType(metin, tip, CultureInfo.InvariantCulture);
    p.SetValue(hedef, deger);
}
