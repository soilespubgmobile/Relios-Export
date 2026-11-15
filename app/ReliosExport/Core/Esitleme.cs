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

    /// <summary>Bütün ses aynı miktarda yükseltilir / kısılır (sesin başka hiçbir yeri değişmez).</summary>
    Dogrusal,

    /// <summary>Doğrusal olamıyor: ses yer yer farklı miktarda yükseltilir (loudnorm'un dinamik kipi).</summary>
    Dinamik,
}

/// <summary>Dinamik kipin nedeni.</summary>
public enum DinamikNedeni
{
    Yok,

    /// <summary>Bütün sesi aynı miktarda yükseltmek tepeyi tavanın üstüne çıkarırdı.</summary>
    AniTepeler,

    /// <summary>Kısık ve yüksek yerler arasındaki fark (LRA) 11 LU'dan büyük.</summary>
    GenisAralik,

    /// <summary>Ses aralığı (LRA) 0 ölçüldü (hiç değişmeyen ses, ör. sabit ton).</summary>
    SabitSes,
}

/// <summary>
/// Bir videonun LUFS eşitleme raporu: ölçüm, karar ve (eşitlendiyse) çıktının ölçülen yüksekliği / tepesi.
/// Kodlarken bilgi satırına, bitince videonun satırına ve ipucuna yazılır.
/// </summary>
public sealed record EsitlemeRaporu(EsitlemeKarari Karar, DinamikNedeni Neden, LoudnormOlcum? Olcum, double Hedef, double Tavan)
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Çıktının ölçülen tümleşik yüksekliği (LUFS); eşitlenmediyse ya da ölçülemediyse null.</summary>
    public double? SonucYukseklik { get; init; }

    /// <summary>Çıktının ölçülen gerçek tepesi (dBTP).</summary>
    public double? SonucTepe { get; init; }

    /// <summary>Ses değiştirildi mi (doğrusal ya da dinamik).</summary>
    public bool Uygulandi => Karar is EsitlemeKarari.Dogrusal or EsitlemeKarari.Dinamik;

    /// <summary>Sonuç hedefe 0,5 LU'dan uzak mı (ör. ani tepeler yüzünden -16'da kaldı).</summary>
    public bool HedefeUlasilamadi => SonucYukseklik is { } s && Math.Abs(s - Hedef) > Esitleme.Tolerans;

    private static string Bir(double d) => Math.Round(d, 1).ToString("0.0", Tr);

    private string HedefMetni => Bicim.Sayi(Hedef);

    private string NedenMetni => Neden switch
    {
        DinamikNedeni.AniTepeler => "ani tepeler yüzünden",
        DinamikNedeni.GenisAralik => "ses aralığı geniş",
        DinamikNedeni.SabitSes => "ses hiç değişmiyor",
        _ => string.Empty,
    };

    /// <summary>Kodlarken bilgi satırı: "-25,5 → -14 LUFS (yalnızca yükseltilir)".</summary>
    public string BilgiMetni => Karar switch
    {
        EsitlemeKarari.Olculemedi => "ses ölçülemedi, eşitlenmiyor",
        EsitlemeKarari.Hedefte => $"{Bir(Olcum!.Yukseklik)} LUFS · zaten hedefte, dokunulmuyor",
        EsitlemeKarari.ZatenYuksek => $"{Bir(Olcum!.Yukseklik)} LUFS · zaten yüksek, dokunulmuyor",
        EsitlemeKarari.Dogrusal => $"{Bir(Olcum!.Yukseklik)} → {HedefMetni} LUFS ("
                                   + (Hedef > Olcum!.Yukseklik ? "yalnızca yükseltilir" : "yalnızca kısılır") + ")",
        _ => $"{Bir(Olcum!.Yukseklik)} → {HedefMetni} LUFS (dinamik" + (NedenMetni.Length > 0 ? ": " + NedenMetni : string.Empty) + ")",
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
            var fark = Hedef - o.Yukseklik;
            satirlar.Add(Karar switch
            {
                EsitlemeKarari.Hedefte => "Karar: zaten hedefte (0,5 LU içinde), sese dokunulmadı.",
                EsitlemeKarari.ZatenYuksek => "Karar: zaten hedeften yüksek, sese dokunulmadı (yalnızca kısık ses yükseltilir).",
                EsitlemeKarari.Dogrusal => $"Karar: doğrusal; bütün ses {(fark >= 0 ? "+" : string.Empty)}{Bir(fark)} dB, sesin başka hiçbir yeri değişmedi.",
                _ => Neden == DinamikNedeni.AniTepeler
                    ? $"Karar: dinamik; {(fark >= 0 ? "+" : string.Empty)}{Bir(fark)} dB tepeyi {Bir(o.Tepe + fark)} dB'ye çıkarırdı, ses yer yer farklı yükseltildi."
                    : $"Karar: dinamik ({NedenMetni}); ses yer yer farklı yükseltildi.",
            });

            if (SonucYukseklik is { } s)
            {
                satirlar.Add($"Sonuç: {Bir(s)} LUFS" + (SonucTepe is { } t ? $" · tepe {Bir(t)} dBTP" : string.Empty)
                             + (HedefeUlasilamadi ? " · hedefe ulaşılamadı" : string.Empty));
            }

            return string.Join('\n', satirlar);
        }
    }
}

/// <summary>LUFS eşitlemenin kararı (loudnorm'un kendi kuralıyla) ve sonucun ölçülmesi.</summary>
public static class Esitleme
{
    /// <summary>Hedefe bu kadar (LU) yakın ses olduğu gibi kalır; sonuç bu kadar uzaksa "hedefe ulaşılamadı".</summary>
    public const double Tolerans = 0.5;

    /// <summary>loudnorm'a verilen yükseklik aralığı hedefi (LU).</summary>
    public const double AralikHedefi = 11;

    /// <summary>
    /// Ölçümden sonra karar. Doğrusal / dinamik ayrımı loudnorm'un kendi kuralıdır (libavfilter/af_loudnorm.c):
    /// ölçülen tepe + kazanç tavanı aşmıyorsa ve ses aralığı 0'dan büyük, aralık hedefinden (11) küçükse doğrusal
    /// (her örnek aynı sabitle çarpılır); değilse dinamik.
    /// </summary>
    public static EsitlemeRaporu KararVer(SesAyarlari s, LoudnormOlcum? o)
    {
        EsitlemeRaporu R(EsitlemeKarari k, DinamikNedeni n = DinamikNedeni.Yok) => new(k, n, o, s.EsitlemeHedef, s.SinirlayiciTavan);

        if (o is null)
        {
            return R(EsitlemeKarari.Olculemedi);
        }

        var fark = s.EsitlemeHedef - o.Yukseklik;
        if (Math.Abs(fark) <= Tolerans)
        {
            return R(EsitlemeKarari.Hedefte);
        }

        if (fark < 0 && s.EsitlemeYonu == EsitlemeYonu.YalnizcaYukselt)
        {
            return R(EsitlemeKarari.ZatenYuksek);
        }

        // Ölçülen değerler loudnorm'a 2 basamakla verilir (ArgsBuilder): karar da aynı değerlerle verilir.
        var neden = o.Tepe + fark > s.SinirlayiciTavan ? DinamikNedeni.AniTepeler
            : o.Aralik > AralikHedefi ? DinamikNedeni.GenisAralik
            : o.Aralik == 0 ? DinamikNedeni.SabitSes
            : DinamikNedeni.Yok;
        return R(neden == DinamikNedeni.Yok ? EsitlemeKarari.Dogrusal : EsitlemeKarari.Dinamik, neden);
    }

    /// <summary>
    /// Çıktının sesini ölçen komut (yalnızca ses okunur): loudnorm ölçtüğü tümleşik yüksekliği ve gerçek tepeyi
    /// JSON olarak yazar (ölçüm geçişiyle aynı yöntem; okumak için <see cref="ArgsBuilder.LoudnormOlcumOku"/>).
    /// </summary>
    public static List<string> SonucOlcumKomutu(string dosya, SesAyarlari s) =>
    [
        "-hide_banner", "-nostdin", "-nostats", "-i", dosya, "-map", "0:a:0", "-vn", "-sn", "-dn",
        "-af", string.Create(CultureInfo.InvariantCulture,
            $"loudnorm=I={s.EsitlemeHedef:0.0#}:TP={s.SinirlayiciTavan:0.0#}:LRA={AralikHedefi:0}:print_format=json"),
        "-f", "null", "-",
    ];
}
