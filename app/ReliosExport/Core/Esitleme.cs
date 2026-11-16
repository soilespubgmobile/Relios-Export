using System.Globalization;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Core;

/// <summary>LUFS eşitlemede ölçümden sonra verilen karar.</summary>
public enum EsitlemeKarari
{
    /// <summary>Ses sessiz ya da ölçülemedi: eşitlenmez.</summary>
    Olculemedi,

    /// <summary>Ses hedefe zaten yakın (± 0,5 LU): dokunulmaz.</summary>
    Hedefte,

    /// <summary>Ses hedeften yüksek ve "yalnızca kısık sesi yükselt" seçili: dokunulmaz.</summary>
    ZatenYuksek,

    /// <summary>
    /// Bütün ses aynı miktarda (sabit kazanç) yükseltilir / kısılır: sesin dengesi değişmez. Kazanç tepeyi tavanın
    /// üstüne çıkaracaksa tepeleri sınırlayıcı tutar ve kıstığı pay telafi edilir.
    /// </summary>
    Dogrusal,
}

/// <summary>
/// Bir videonun LUFS eşitleme raporu: ölçüm, karar ve (eşitlendiyse) çıktının ölçülen yüksekliği / tepesi.
/// Kodlarken bilgi satırına, bitince videonun satırına ve ipucuna yazılır.
/// </summary>
public sealed record EsitlemeRaporu(EsitlemeKarari Karar, SesOlcum? Olcum, double Hedef, double Tavan)
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Çıktının ölçülen tümleşik yüksekliği (LUFS); eşitlenmediyse ya da ölçülemediyse null.</summary>
    public double? SonucYukseklik { get; init; }

    /// <summary>Çıktının ölçülen gerçek tepesi (dBTP).</summary>
    public double? SonucTepe { get; init; }

    /// <summary>
    /// Sınırlayıcının kıstığı ve (AAC çıktıda) kodlamanın düşürdüğü payı karşılamak için sese eklenen kazanç (dB;
    /// en çok <see cref="Esitleme.EnCokTelafi"/>; yalnızca AAC'de eksi olabilir).
    /// </summary>
    public double Telafi { get; init; }

    /// <summary>Gereken telafi <see cref="Esitleme.EnCokTelafi"/> dB'yi aştı (çok tepeli ses; tepeler daha çok ezilmesin diye durdu).</summary>
    public bool TelafiSiniri { get; init; }

    /// <summary>
    /// Çıktının sesi kayıplı (FDK AAC): telafi denemesinde ses AAC'ye çevrilip ölçüldü (AAC tizleri kestiği için
    /// yükseklik biraz düşer; kayıp kadar telafi edildi).
    /// </summary>
    public bool Kayipli { get; init; }

    /// <summary>Ses değiştirildi mi.</summary>
    public bool Uygulandi => Karar == EsitlemeKarari.Dogrusal;

    /// <summary>Ölçülen yükseklikten hedefe kazanç (dB).</summary>
    public double Fark => Olcum is { } o ? Hedef - o.Yukseklik : 0;

    /// <summary>Sese uygulanan toplam kazanç (dB): hedefe kazanç + telafi.</summary>
    public double Kazanc => Fark + Telafi;

    /// <summary>Kazanç (telafi dahil) tepeyi tavanın üstüne çıkarır mı (tepeleri sınırlayıcı tutar).</summary>
    public bool Sinirli => Uygulandi && Olcum is { } o && Esitleme.SinirlanirMi(o, Hedef, Tavan, Telafi);

    /// <summary>Sonuç hedefe 0,5 LU'dan uzak mı (ör. çok tepeli seste telafi sınırına gelindi).</summary>
    public bool HedefeUlasilamadi => SonucYukseklik is { } s && Math.Abs(s - Hedef) > Esitleme.Tolerans;

    private static string Bir(double d) => Math.Round(d, 1).ToString("0.0", Tr);

    private static string Isaretli(double d) => (Math.Round(d, 1) >= 0 ? "+" : string.Empty) + Bir(d);

    private string HedefMetni => Bicim.Sayi(Hedef);

    /// <summary>Kodlarken bilgi satırı: "-25,5 → -14 LUFS (bütün ses +11,5 dB)".</summary>
    public string BilgiMetni => Karar switch
    {
        EsitlemeKarari.Olculemedi => "ses ölçülemedi, eşitlenmiyor",
        EsitlemeKarari.Hedefte => $"{Bir(Olcum!.Yukseklik)} LUFS · zaten hedefte, dokunulmuyor",
        EsitlemeKarari.ZatenYuksek => $"{Bir(Olcum!.Yukseklik)} LUFS · zaten yüksek, dokunulmuyor",
        _ => $"{Bir(Olcum!.Yukseklik)} → {HedefMetni} LUFS (bütün ses {Isaretli(Kazanc)} dB"
             + (Sinirli ? $", tepeler {Bicim.Sayi(Tavan)} dB'de tutulur" : string.Empty) + ")",
    };

    /// <summary>Biten satır: "-14,0 LUFS", "-16,0 LUFS (hedefe ulaşılamadı)", "-9,2 LUFS (dokunulmadı)".</summary>
    public string? SatirMetni => Karar switch
    {
        EsitlemeKarari.Olculemedi => "ses ölçülemedi, eşitlenmedi",
        EsitlemeKarari.Hedefte or EsitlemeKarari.ZatenYuksek => $"{Bir(Olcum!.Yukseklik)} LUFS (dokunulmadı)",
        _ when SonucYukseklik is { } s => $"{Bir(s)} LUFS" + (HedefeUlasilamadi ? " (hedefe ulaşılamadı)" : string.Empty),
        _ => null,
    };

    /// <summary>İpucu: ölçüm, karar ve sonuç ayrıntısı (birkaç satır).</summary>
    public string Ayrinti
    {
        get
        {
            if (Olcum is not { } o)
            {
                return "LUFS eşitleme: ses sessiz ya da ölçülemedi; sese dokunulmadı.";
            }

            var satirlar = new List<string>
            {
                $"LUFS eşitleme (hedef {HedefMetni} LUFS, tepe en çok {Bicim.Sayi(Tavan)} dB)",
                $"Ölçülen: {Bir(o.Yukseklik)} LUFS · tepe {Bir(o.Tepe)} dBTP · ses aralığı {Bir(o.Aralik)} LU",
            };
            satirlar.Add(Karar switch
            {
                EsitlemeKarari.Hedefte => "Karar: zaten hedefte (0,5 LU içinde), sese dokunulmadı.",
                EsitlemeKarari.ZatenYuksek => "Karar: zaten hedeften yüksek, sese dokunulmadı (yalnızca kısık ses yükseltilir).",
                _ => $"Karar: bütün ses aynı miktarda {Isaretli(Fark)} dB {(Fark >= 0 ? "yükseltildi" : "kısıldı")}; sesin dengesi değişmedi.",
            });

            if (Sinirli)
            {
                // AAC'de tepeyi tavana telafi de çıkarmış olabilir: o zaman uygulanan bütün kazançla.
                satirlar.Add($"Tepe {Bir(o.Tepe + (Kayipli ? Kazanc : Fark))} dB'ye çıkacaktı: sınırlayıcı {Bicim.Sayi(Tavan)} dB'de tuttu"
                             + (Telafi > 0 ? $"; kıstığı pay için ses {Isaretli(Telafi)} dB daha yükseltildi" : string.Empty)
                             + (Kayipli && Telafi != 0 ? " (AAC kodlamasının düşürdüğü pay da)" : string.Empty)
                             + (TelafiSiniri ? $" (telafi en çok {Bicim.Sayi(Esitleme.EnCokTelafi)} dB)" : string.Empty) + ".");
            }
            else if (Kayipli && Telafi > 0)
            {
                satirlar.Add($"AAC kodlaması tizleri kestiği için yükseklik {Bir(Telafi)} LU düşüyordu: ses {Bir(Telafi)} dB daha yükseltildi"
                             + " (AAC'ye çevrilip ölçülerek).");
            }
            else if (Kayipli && Telafi < 0)
            {
                satirlar.Add($"AAC kodlaması yüksekliği {Bir(-Telafi)} LU artırıyordu: kazanç {Bir(-Telafi)} dB azaltıldı"
                             + " (AAC'ye çevrilip ölçülerek).");
            }

            if (SonucYukseklik is { } s)
            {
                satirlar.Add($"Sonuç: {Bir(s)} LUFS" + (SonucTepe is { } t ? $" · tepe {Bir(t)} dBTP" : string.Empty)
                             + (HedefeUlasilamadi ? " · hedefe ulaşılamadı" : string.Empty));
            }

            return string.Join('\n', satirlar);
        }
    }
}

/// <summary>LUFS eşitlemenin kararı, sınırlayıcı telafisi ve sonucun ölçülmesi.</summary>
public static class Esitleme
{
    /// <summary>Hedefe bu kadar (LU) yakın ses olduğu gibi kalır; sonuç bu kadar uzaksa "hedefe ulaşılamadı".</summary>
    public const double Tolerans = 0.5;

    /// <summary>
    /// Ses yüksekliği ölçeri: FFmpeg'in ebur128 filtresi (ITU-R BS.1770-4 / EBU R128 ölçerinin kendisi; gerçek tepe 4 kat
    /// örneklemede). Tümleşik yükseklik her 100 ms'de çerçeve bilgisine yazılır, ametadata onu 0,001 hassasiyetle basar
    /// (sondaki özette yalnızca 0,1). loudnorm'un kendi ölçümü kullanılmaz: sesin son ~2,9 saniyesini iki kez sayar
    /// (FFmpeg af_loudnorm.c, flush_frame); sonu sesin geri kalanından kısık ya da yüksek olan kısa videoda sonuç kayar
    /// (25 saniyelik bir OBS kaydında 0,44 LU).
    /// </summary>
    public const string OlcumFiltresi = "ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I";

    /// <summary>Ölçerin sessizlik eşiği (LUFS): sessiz ya da 0,4 saniyeden kısa seste tümleşik yükseklik bu kalır.</summary>
    public const double MutlakKapi = -70;

    /// <summary>Ölçerin özetindeki gerçek tepe 0,1 dB'ye yuvarlıdır: sınırlayıcı kararında bu kadar (dB) pay bırakılır.</summary>
    public const double TepePayi = 0.05;

    /// <summary>Sınırlayıcının kıstığı pay en çok bu kadar (dB) telafi edilir; daha fazlası tepeleri aşırı ezerdi.</summary>
    public const double EnCokTelafi = 6;

    /// <summary>Telafi için en çok bu kadar deneme ölçümü yapılır (yalnızca ses okunur).</summary>
    public const int EnCokDeneme = 3;

    /// <summary>Deneme ölçümü hedefe bu kadar (LU) yakınsa telafi tamamdır.</summary>
    public const double TelafiPayi = 0.2;

    /// <summary>
    /// AAC çıktıda deneme ölçümü hedefe bu kadar (LU) yakınsa telafi tamamdır. AAC'nin kaybı küçüktür (kullanıcının
    /// OBS kaydında 128 kb/s'de 0,16 LU) ve kazançla doğrusal değişir: tek düzeltmede hedefe 0,01 içinde gelinir.
    /// </summary>
    public const double KayipliPayi = 0.02;

    /// <summary>AAC çıktıda telafinin en küçük adımı (dB).</summary>
    public const double KayipliAdim = 0.01;

    /// <summary>
    /// Çıktının sesi kayıplı mı (FDK AAC): AAC tizleri (128 kb/s'de ~17 kHz üstünü) keser, ölçülen yükseklik biraz
    /// düşer. Eşitlemede ses o zaman AAC'ye çevrilip denenir, kayıp telafi edilir. WAV kayıpsızdır, denenmez.
    /// </summary>
    public static bool KayipliMi(SesAyarlari s) => s.Kodlayici == SesKodlayici.FdkAac;

    /// <summary>
    /// Hedefe kazanç (+ telafi), ölçülen gerçek tepeyi tavanın üstüne çıkarır mı (tepe 0,1 dB'ye yuvarlı ölçüldüğü
    /// için <see cref="TepePayi"/> kadar pay bırakılır: tavanı geçebilecek tepe her zaman sınırlanır).
    /// </summary>
    public static bool SinirlanirMi(SesOlcum o, double hedef, double tavan, double telafi = 0) =>
        o.Tepe + TepePayi + (hedef - o.Yukseklik) + telafi > tavan;

    /// <summary>
    /// Ölçümden sonra karar: hedefe 0,5 LU yakınsa dokunulmaz; yüksek ses "yalnızca kısık sesi yükselt" seçiliyse
    /// dokunulmaz; değilse bütün ses aynı miktarda (sabit kazanç) hedefe getirilir. Ses aralığı (LRA) karara girmez:
    /// kısık ve yüksek yerler arasındaki fark olduğu gibi kalır.
    /// </summary>
    public static EsitlemeRaporu KararVer(SesAyarlari s, SesOlcum? o)
    {
        EsitlemeRaporu R(EsitlemeKarari k) => new(k, o, s.EsitlemeHedef, s.SinirlayiciTavan);

        if (o is null)
        {
            return R(EsitlemeKarari.Olculemedi);
        }

        var fark = s.EsitlemeHedef - o.Yukseklik;
        if (Math.Abs(fark) <= Tolerans)
        {
            return R(EsitlemeKarari.Hedefte);
        }

        return fark < 0 && s.EsitlemeYonu == EsitlemeYonu.YalnizcaYukselt
            ? R(EsitlemeKarari.ZatenYuksek)
            : R(EsitlemeKarari.Dogrusal);
    }

    /// <summary>
    /// Deneme ölçümünden sonra yeni telafi (dB): ölçülen yükseklik hedefin altındaysa eksik kadar artar, üstündeyse azalır;
    /// 0 ile <see cref="EnCokTelafi"/> arasında kalır. Değişmesi gerekmiyorsa (hedefte ya da sınırda) null.
    /// <paramref name="kayipli"/> (AAC çıktı, deneme AAC'ye çevrilip ölçüldü): hedefe <see cref="KayipliPayi"/> içinde
    /// gelinene kadar <see cref="KayipliAdim"/> adımla; AAC yüksekliği artırdıysa telafi eksi olabilir.
    /// </summary>
    public static (double? Yeni, bool Sinir) TelafiAdimi(double telafi, double olculen, double hedef, bool kayipli = false)
    {
        var eksik = hedef - olculen;
        if (Math.Abs(eksik) <= (kayipli ? KayipliPayi : TelafiPayi))
        {
            return (null, false);
        }

        var istenen = kayipli ? telafi + eksik : Math.Max(0, telafi + eksik);
        var sinir = Math.Abs(istenen) > EnCokTelafi;
        var yeni = Math.Round(Math.Clamp(istenen, -EnCokTelafi, EnCokTelafi), 2);
        return Math.Abs(yeni - telafi) < (kayipli ? KayipliAdim : 0.05) ? (null, sinir) : (yeni, sinir);
    }

    /// <summary>
    /// Çıktının sesini ölçen komut (yalnızca ses okunur): ölçüm geçişiyle aynı ölçer (<see cref="OlcumFiltresi"/>;
    /// okumak için <see cref="ArgsBuilder.OlcumOku"/>).
    /// </summary>
    public static List<string> SonucOlcumKomutu(string dosya) =>
    [
        "-hide_banner", "-nostdin", "-nostats", "-i", dosya, "-map", "0:a:0", "-vn", "-sn", "-dn",
        "-af", OlcumFiltresi, "-f", "null", "-",
    ];
}
