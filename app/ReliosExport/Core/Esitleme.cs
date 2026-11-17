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

    /// <summary>
    /// LUFS eşitleme kapalı: yalnızca tepe koruması çalıştı (sınırlayıcı açık; çıktının gerçek tepesi tavanı aşacaktı).
    /// </summary>
    Kapali,
}

/// <summary>
/// Bir videonun ses raporu: LUFS eşitlemenin ölçümü, kararı ve (eşitlendiyse) çıktının ölçülen yüksekliği / tepesi;
/// tepe koruması (çıktının gerçek tepesi tavanı aşmasın diye sınırlayıcının ne yaptığı). Kodlarken bilgi satırına,
/// bitince videonun satırına ve ipucuna yazılır.
/// </summary>
public sealed record EsitlemeRaporu(EsitlemeKarari Karar, SesOlcum? Olcum, double Hedef, double Tavan)
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Çıktının ölçülen tümleşik yüksekliği (LUFS); ses değiştirilmediyse ya da ölçülemediyse null.</summary>
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
    /// Çıktının sesi kayıplı (FDK AAC): deneme sesi AAC'ye çevrilip ölçüldü (AAC tizleri kestiği için yükseklik biraz
    /// düşer, kodlama tepeyi biraz yükseltir; ikisi de ölçülen kadar düzeltildi).
    /// </summary>
    public bool Kayipli { get; init; }

    /// <summary>
    /// Sınırlayıcı tavanın bu kadar (dB) altında çalıştı: deneme ölçümünde çıktının gerçek tepesi tavanı aştı (AAC
    /// kodlaması ve örnekleme hızı dönüşümü tepeyi biraz yükseltir). Görülen en büyük taşma + <see cref="Esitleme.TasmaEki"/>.
    /// </summary>
    public double TasmaPayi { get; init; }

    /// <summary>Denemelerde görülen en büyük taşma (dB): çıktının gerçek tepesi - kodlayıcıya giren sesin tepesi.</summary>
    public double Tasma { get; init; }

    /// <summary>
    /// Sınırlayıcıyı tepe koruması ekledi: ses yükseltilmediği (ya da tepe tavanı aşmayacak kadar yükseltildiği) hâlde
    /// çıktının gerçek tepesi tavanı aşacaktı.
    /// </summary>
    public bool TepeZorla { get; init; }

    /// <summary>İlk denemede çıktının ölçülen gerçek tepesi (dBTP). Deneme yapılmadıysa null.</summary>
    public double? IlkTepe { get; init; }

    /// <summary>Kodlamadaki ses zincirinde sınırlayıcı var (son deneme ve kodlama aynı zincirle).</summary>
    public bool SinirlayiciCalisti { get; init; }

    /// <summary>Sesin yüksekliği değiştirildi mi (bütün ses aynı miktarda).</summary>
    public bool Uygulandi => Karar == EsitlemeKarari.Dogrusal;

    /// <summary>Ölçülen yükseklikten hedefe kazanç (dB).</summary>
    public double Fark => Olcum is { } o ? Hedef - o.Yukseklik : 0;

    /// <summary>Sese uygulanan toplam kazanç (dB): hedefe kazanç + telafi.</summary>
    public double Kazanc => Fark + Telafi;

    /// <summary>Sınırlayıcının çalıştığı tavan (dB): tavan - taşma payı.</summary>
    public double SinirTavani => Tavan - TasmaPayi;

    /// <summary>
    /// Tepeleri sınırlayıcı tuttu mu: kodlamadaki zincirde sınırlayıcı var ya da (kararda, denemeden önce) kazanç
    /// (telafi dahil) tepeyi sınırlayıcının tavanının üstüne çıkarır.
    /// </summary>
    public bool Sinirli => SinirlayiciCalisti || TepeZorla
        || (Uygulandi && Olcum is { } o && Esitleme.SinirlanirMi(o, Hedef, SinirTavani, Telafi));

    /// <summary>Sonuç hedefe 0,5 LU'dan uzak mı (ör. çok tepeli seste telafi sınırına gelindi).</summary>
    public bool HedefeUlasilamadi => Uygulandi && SonucYukseklik is { } s && Math.Abs(s - Hedef) > Esitleme.Tolerans;

    private static string Bir(double d) => Math.Round(d, 1).ToString("0.0", Tr);

    private static string Isaretli(double d) => (Math.Round(d, 1) >= 0 ? "+" : string.Empty) + Bir(d);

    private string HedefMetni => Bicim.Sayi(Hedef);

    private string TavanMetni => Bicim.Sayi(Tavan);

    private string TepeEki => TepeZorla ? $" (tepeler {TavanMetni} dB'de tutulur)" : string.Empty;

    /// <summary>Kodlarken bilgi satırı: "-25,5 → -14 LUFS (bütün ses +11,5 dB)".</summary>
    public string BilgiMetni => Karar switch
    {
        EsitlemeKarari.Olculemedi => "ses ölçülemedi, eşitlenmiyor",
        EsitlemeKarari.Hedefte => $"{Bir(Olcum!.Yukseklik)} LUFS · zaten hedefte, dokunulmuyor" + TepeEki,
        EsitlemeKarari.ZatenYuksek => $"{Bir(Olcum!.Yukseklik)} LUFS · zaten yüksek, dokunulmuyor" + TepeEki,
        EsitlemeKarari.Kapali => $"tepeler {TavanMetni} dB'de tutulur",
        _ => $"{Bir(Olcum!.Yukseklik)} → {HedefMetni} LUFS (bütün ses {Isaretli(Kazanc)} dB"
             + (Sinirli ? $", tepeler {TavanMetni} dB'de tutulur" : string.Empty) + ")",
    };

    /// <summary>
    /// Biten satır: "-14,0 LUFS", "-16,0 LUFS (hedefe ulaşılamadı)", "-9,2 LUFS (dokunulmadı)", "-9,4 LUFS (yalnızca
    /// tepeler sınırlandı)", "tepeler -1 dB'de tutuldu" (eşitleme kapalı).
    /// </summary>
    public string? SatirMetni => Karar switch
    {
        EsitlemeKarari.Olculemedi => "ses ölçülemedi, eşitlenmedi",
        EsitlemeKarari.Hedefte or EsitlemeKarari.ZatenYuksek => TepeZorla
            ? $"{Bir(SonucYukseklik ?? Olcum!.Yukseklik)} LUFS (yalnızca tepeler sınırlandı)"
            : $"{Bir(Olcum!.Yukseklik)} LUFS (dokunulmadı)",
        EsitlemeKarari.Kapali => $"tepeler {TavanMetni} dB'de tutuldu",
        _ when SonucYukseklik is { } s => $"{Bir(s)} LUFS" + (HedefeUlasilamadi ? " (hedefe ulaşılamadı)" : string.Empty),
        _ => null,
    };

    /// <summary>İpucu: ölçüm, karar, tepe koruması ve sonuç ayrıntısı (birkaç satır).</summary>
    public string Ayrinti
    {
        get
        {
            var satirlar = new List<string>();
            if (Karar == EsitlemeKarari.Kapali)
            {
                satirlar.Add($"Tepe koruması (LUFS eşitleme kapalı; tepe en çok {TavanMetni} dB)");
                if (Olcum is { } ok)
                {
                    satirlar.Add($"Ölçülen: {Bir(ok.Yukseklik)} LUFS · tepe {Bir(ok.Tepe)} dBTP");
                }

                satirlar.Add("Karar: sesin yüksekliğine dokunulmadı; yalnızca tepeler sınırlandı.");
            }
            else if (Olcum is not { } o)
            {
                return "LUFS eşitleme: ses sessiz ya da ölçülemedi; sese dokunulmadı.";
            }
            else
            {
                satirlar.Add($"LUFS eşitleme (hedef {HedefMetni} LUFS, tepe en çok {TavanMetni} dB)");
                satirlar.Add($"Ölçülen: {Bir(o.Yukseklik)} LUFS · tepe {Bir(o.Tepe)} dBTP · ses aralığı {Bir(o.Aralik)} LU");
                var dokunma = TepeZorla ? "sesin yüksekliğine dokunulmadı" : "sese dokunulmadı";
                satirlar.Add(Karar switch
                {
                    EsitlemeKarari.Hedefte => $"Karar: zaten hedefte (0,5 LU içinde), {dokunma}.",
                    EsitlemeKarari.ZatenYuksek => $"Karar: zaten hedeften yüksek, {dokunma} (yalnızca kısık ses yükseltilir).",
                    _ => $"Karar: bütün ses aynı miktarda {Isaretli(Fark)} dB {(Fark >= 0 ? "yükseltildi" : "kısıldı")}; sesin dengesi değişmedi.",
                });

                // AAC'de tepeyi tavana telafi de çıkarmış olabilir: o zaman uygulanan bütün kazançla.
                var tahmin = o.Tepe + (Kayipli ? Kazanc : Fark);
                if (Uygulandi && Sinirli && tahmin + Esitleme.TepePayi > Tavan)
                {
                    satirlar.Add($"Tepe {Bir(tahmin)} dB'ye çıkacaktı: sınırlayıcı {TavanMetni} dB'de tuttu"
                                 + (Telafi > 0 ? $"; kıstığı pay için ses {Isaretli(Telafi)} dB daha yükseltildi" : string.Empty)
                                 + (Kayipli && Telafi != 0 ? " (AAC kodlamasının düşürdüğü pay da)" : string.Empty)
                                 + (TelafiSiniri ? $" (telafi en çok {Bicim.Sayi(Esitleme.EnCokTelafi)} dB)" : string.Empty) + ".");
                }
                else if (Uygulandi && Kayipli && Telafi > 0)
                {
                    satirlar.Add($"AAC kodlaması tizleri kestiği için yükseklik {Bir(Telafi)} LU düşüyordu: ses {Bir(Telafi)} dB daha yükseltildi"
                                 + " (AAC'ye çevrilip ölçülerek).");
                }
                else if (Uygulandi && Kayipli && Telafi < 0)
                {
                    satirlar.Add($"AAC kodlaması yüksekliği {Bir(-Telafi)} LU artırıyordu: kazanç {Bir(-Telafi)} dB azaltıldı"
                                 + " (AAC'ye çevrilip ölçülerek).");
                }
            }

            if (TepeZorla && !Uygulandi)
            {
                satirlar.Add(Olcum is { } ot && ot.Tepe + Esitleme.TepePayi > Tavan
                    ? $"Tepe {Bir(ot.Tepe)} dBTP, tavanın üstündeydi: sınırlayıcı yalnızca tepeleri tuttu; sesin geri kalanına dokunulmadı."
                    : $"Çıktının tepesi {Bir(IlkTepe ?? Tavan)} dBTP'ye çıkacaktı: sınırlayıcı yalnızca tepeleri tuttu; sesin geri kalanına dokunulmadı.");
            }

            if (TasmaPayi > 0)
            {
                var tasma = Math.Max(Tasma, TasmaPayi - Esitleme.TasmaEki);
                satirlar.Add($"{(Kayipli ? "AAC kodlaması" : "Örnekleme hızı dönüşümü")} tepeyi {Bicim.Sayi(Math.Round(tasma, 2))} dB yükseltiyordu"
                             + $" (denendi, ölçüldü): sınırlayıcı {Bicim.Sayi(Math.Round(SinirTavani, 2))} dB'de çalıştı; çıktının gerçek tepesi en çok {TavanMetni} dB.");
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
    /// Ses yüksekliği ölçeri: FFmpeg'in ebur128 filtresi (ITU-R BS.1770-4 / EBU R128 ölçerinin kendisi; gerçek tepe
    /// 192 kHz'te). Tümleşik yükseklik ve gerçek tepe her 100 ms'de çerçeve bilgisine yazılır, ametadata ikisini de basar:
    /// yükseklik 0,001 LU, gerçek tepe 0,001 (doğrusal; tavan çevresinde ~0,005 dB) hassasiyetle (sondaki özette yalnızca
    /// 0,1). loudnorm'un kendi ölçümü kullanılmaz: sesin son ~2,9 saniyesini iki kez sayar (FFmpeg af_loudnorm.c,
    /// flush_frame); sonu sesin geri kalanından kısık ya da yüksek olan kısa videoda sonuç kayar (25 saniyelik bir OBS
    /// kaydında 0,44 LU).
    /// </summary>
    public const string OlcumFiltresi =
        "ebur128=peak=true:metadata=1,ametadata=mode=print:key=lavfi.r128.I,ametadata=mode=print:key=lavfi.r128.true_peak";

    /// <summary>Ölçerin sessizlik eşiği (LUFS): sessiz ya da 0,4 saniyeden kısa seste tümleşik yükseklik bu kalır.</summary>
    public const double MutlakKapi = -70;

    /// <summary>Ölçerin özetindeki gerçek tepe 0,1 dB'ye yuvarlıdır: sınırlayıcı kararında bu kadar (dB) pay bırakılır.</summary>
    public const double TepePayi = 0.05;

    /// <summary>Sınırlayıcının kıstığı pay en çok bu kadar (dB) telafi edilir; daha fazlası tepeleri aşırı ezerdi.</summary>
    public const double EnCokTelafi = 6;

    /// <summary>
    /// Çerçeve bilgisindeki gerçek tepe 0,001'e (doğrusal) yuvarlıdır: tavan çevresinde en çok ~0,005 dB. Tepe koruması
    /// bu kadar (dB) pay bırakır (özetteki 0,1 dB'lik değerde <see cref="TepePayi"/>).
    /// </summary>
    public const double HassasTepePayi = 0.005;

    /// <summary>
    /// Tepe koruması: deneme ölçümünde çıktının gerçek tepesi tavanı aşarsa sınırlayıcı, görülen en büyük taşmanın bu kadar
    /// (dB) fazlası aşağı çekilir (taşma kodlamaya göre biraz değişir; aynı sesin AAC'si kullanıcının videolarında 0,1–1 dB).
    /// </summary>
    public const double TasmaEki = 0.1;

    /// <summary>Sınırlayıcı tavandan en çok bu kadar (dB) aşağı çekilir.</summary>
    public const double EnCokTasmaPayi = 6;

    /// <summary>Telafi ve tepe koruması için en çok bu kadar deneme yapılır (yalnızca ses).</summary>
    public const int EnCokDeneme = 6;

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
    /// düşer; kodlama gerçek tepeyi de biraz yükseltir. Deneme sesi o zaman AAC'ye çevrilip ölçülür. WAV kayıpsızdır:
    /// denemede kodlanmadan ölçülür.
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

    /// <summary>Ölçülen gerçek tepe tavanın altında mı (ölçerin yuvarlaması kadar pay bırakılarak).</summary>
    public static bool TepeTavandaMi(double tepe, bool hassas, double tavan) =>
        tepe + (hassas ? HassasTepePayi : TepePayi) <= tavan;

    /// <summary>
    /// Kodlayıcıya giren sesin tahmini gerçek tepesi (dBTP): kaynağın ölçülen tepesi + kazanç; zincirde sınırlayıcı
    /// varsa en çok sınırlayıcının tavanı. Kaynağın tepesi bilinmiyorsa null.
    /// </summary>
    public static double? GirisTepesi(double? kaynakTepe, double kazanc, bool sinirlayici, double sinirTavani) =>
        kaynakTepe is { } t ? (sinirlayici ? Math.Min(t + kazanc, sinirTavani) : t + kazanc) : null;

    /// <summary>
    /// Bir denemeden sonra sesin yeni düzeltmesi. Yükseklik (<paramref name="yukseklik"/>: LUFS eşitleme uygulanıyorsa
    /// ölçülen; yoksa null) <see cref="TelafiAdimi"/> ile. Tepe (<paramref name="tepe"/>: çıktının ölçülen gerçek tepesi;
    /// tepe koruması yoksa null): tavanı aşıyorsa sınırlayıcı zincire girer ve görülen en büyük taşma (çıktının tepesi -
    /// kodlayıcıya giren sesin tepesi, <paramref name="girisTepe"/>) + <see cref="TasmaEki"/> kadar, en az da önceki payın
    /// üstüne aşım + ek kadar aşağı çekilir (pay hiç azalmaz; 0,01 dB'ye yukarı yuvarlanır, en çok
    /// <see cref="EnCokTasmaPayi"/>). Değişecek bir şey yoksa (ikisi de tamam ya da sınıra gelindi) Yeni null: bu
    /// denemenin ayarı son ayardır. <paramref name="tasma"/>: şimdiye kadar görülen en büyük taşma (dB).
    /// </summary>
    public static (SesDuzeltme? Yeni, bool TelafiSiniri, double Tasma) DenemeAdimi(
        SesDuzeltme d, double tasma, double? yukseklik, double hedef, bool kayipli,
        double? tepe, bool hassas, double tavan, double? girisTepe)
    {
        double? yeniTelafi = null;
        var sinir = false;
        if (yukseklik is { } y)
        {
            (yeniTelafi, sinir) = TelafiAdimi(d.Telafi, y, hedef, kayipli);
        }

        var yeni = yeniTelafi is { } t ? d with { Telafi = t } : d;
        if (tepe is { } tp && !TepeTavandaMi(tp, hassas, tavan))
        {
            var asim = tp - tavan;
            tasma = Math.Max(tasma, Math.Max(0, girisTepe is { } g ? tp - g : asim));
            var pay = Math.Max(tasma + TasmaEki, d.TasmaPayi + asim + TasmaEki);
            pay = Math.Min(Math.Ceiling(Math.Round(pay * 100, 6)) / 100, EnCokTasmaPayi);
            yeni = yeni with { TasmaPayi = Math.Max(pay, d.TasmaPayi), Sinirla = true };
        }

        return (yeni == d ? null : yeni, sinir, tasma);
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
