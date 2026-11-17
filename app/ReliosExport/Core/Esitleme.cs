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
    /// Kaynağın ölçülen yüksekliği (LUFS; kanal sayısı ve hız çıktıdaki gibi). Karar sesin çıktıdaki hâlinin ölçümüyle
    /// verildiyse (<see cref="Olcum"/>: AAC'de kodlanıp çözülen, sınırlayıcıdan geçen ses) ondan farklı olabilir.
    /// </summary>
    public double? KaynakYukseklik { get; init; }

    /// <summary>
    /// Kararın verildiği yükseklik (LUFS): sesin çıktıdaki hâli, bir kez kazançsız denenip ölçüldüyse (AAC'de kodlanıp
    /// çözülen, sınırlayıcıdan geçen ses); denenmediyse null (karar <see cref="Olcum"/> ile). Kazanç yine
    /// <see cref="Olcum"/>'dan hesaplanır: sınırlayıcı kazançla birlikte değişir (ses kısılınca hiç çalışmayabilir).
    /// </summary>
    public double? KararYukseklik { get; init; }

    /// <summary>
    /// Ses ya da hedef çok kısık (<see cref="Esitleme.KisikSinir"/>): yükseklik çıktının seviyesinde de denenip ölçüldü
    /// (ölçerin mutlak kapısı kısık seste bazı blokları saymaz).
    /// </summary>
    public bool KisikOlcum { get; init; }

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

    /// <summary>
    /// Denemeler bitti (<see cref="Esitleme.EnCokDeneme"/>) ama hedefe gelinemedi: kodlama, tepesi tavanın altında ölçülen
    /// denemelerden hedefe en yakınının ayarıyla yapıldı.
    /// </summary>
    public bool DenemelerBitti { get; init; }

    /// <summary>
    /// Sonuç hedeften <see cref="Esitleme.SonucPayi"/> LU'dan uzak mı (ör. çok tepeli seste telafi sınırına gelindi,
    /// sınırlayıcı sesi daha fazla yükseltemiyor).
    /// </summary>
    public bool HedefeUlasilamadi => Uygulandi && SonucYukseklik is { } s && Math.Abs(s - Hedef) > Esitleme.SonucPayi;

    /// <summary>Kararın yüksekliği: sesin çıktıdaki hâli denendiyse o, değilse ölçülen.</summary>
    private double KararDegeri => KararYukseklik ?? Olcum?.Yukseklik ?? 0;

    private static string Bir(double d) => Math.Round(d, 1).ToString("0.0", Tr);

    private static string Isaretli(double d) => (Math.Round(d, 1) >= 0 ? "+" : string.Empty) + Bir(d);

    private string HedefMetni => Bicim.Sayi(Hedef);

    private string TavanMetni => Bicim.Sayi(Tavan);

    private string TepeEki => TepeZorla || SinirlayiciCalisti ? $" (tepeler {TavanMetni} dB'de tutulur)" : string.Empty;

    /// <summary>Kodlarken bilgi satırı: "-25,5 → -14 LUFS (bütün ses +11,5 dB)".</summary>
    public string BilgiMetni => Karar switch
    {
        EsitlemeKarari.Olculemedi => "ses ölçülemedi, eşitlenmiyor",
        EsitlemeKarari.Hedefte => $"{Bir(KararDegeri)} LUFS · zaten hedefte, dokunulmuyor" + TepeEki,
        EsitlemeKarari.ZatenYuksek => $"{Bir(KararDegeri)} LUFS · zaten yüksek, dokunulmuyor" + TepeEki,
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
        EsitlemeKarari.Hedefte or EsitlemeKarari.ZatenYuksek => TepeZorla || SinirlayiciCalisti
            ? $"{Bir(SonucYukseklik ?? KararDegeri)} LUFS (yalnızca tepeler sınırlandı)"
            : $"{Bir(SonucYukseklik ?? KararDegeri)} LUFS (dokunulmadı)",
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
                var kaynak = KaynakYukseklik ?? o.Yukseklik;
                satirlar.Add($"Ölçülen: {Bir(kaynak)} LUFS · tepe {Bir(o.Tepe)} dBTP · ses aralığı {Bir(o.Aralik)} LU");
                if (KararYukseklik is { } ky && Math.Abs(ky - kaynak) >= 0.05)
                {
                    // Karar çıktıdaki hâlin ölçümüyle (ör. AAC'nin DRC'si sesi kısıyor, sınırlayıcı yüksek sesi eziyor).
                    satirlar.Add($"Çıktıdaki hâli {Bir(ky)} LUFS (ses kodlamadaki zinciriyle denenip ölçüldü): karar bununla verildi.");
                }

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
                else if (Uygulandi && KisikOlcum && Telafi != 0)
                {
                    satirlar.Add($"Ses çok kısık olduğu için (ölçer -70 LUFS'un altındaki bölümleri saymaz) yükseklik çıktının seviyesinde"
                                 + $" yeniden ölçüldü: kazanç {Isaretli(Telafi)} dB düzeltildi.");
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
                if (HedefeUlasilamadi && !TelafiSiniri)
                {
                    satirlar.Add(DenemelerBitti
                        ? $"Denemeler bitti (en çok {Esitleme.EnCokDeneme}): tepesi tavanın altında kalan denemelerden hedefe en yakını kullanıldı."
                        : Sinirli
                            ? "Sınırlayıcı tepeleri tavanda tutarken ses daha fazla yükselmiyor: hedefe en yakın sonuç bu."
                            : "Ses kodlamadan sonra hedefe getirilemedi: hedefe en yakın sonuç bu.");
                }
            }

            return string.Join('\n', satirlar);
        }
    }
}

/// <summary>
/// Bir yükseklik denemesi: sese eklenen telafi (dB), sınırlayıcının tavandan payı (dB) ve çıktının ölçülen yüksekliği
/// (LUFS).
/// </summary>
public readonly record struct YukseklikDenemesi(double Telafi, double Pay, double Olculen);

/// <summary>LUFS eşitlemenin kararı, sınırlayıcı telafisi ve sonucun ölçülmesi.</summary>
public static class Esitleme
{
    /// <summary>Hedefe bu kadar (LU) yakın ses olduğu gibi kalır.</summary>
    public const double Tolerans = 0.5;

    /// <summary>
    /// Yüksekliği değiştirilen sesin sonucu hedeften bu kadar (LU) uzaksa "hedefe ulaşılamadı": denemeler hedefe
    /// <see cref="KayipliPayi"/> içinde gelir, ölçülen kazanç (WAV) tam uygulanır; daha uzak sonuç, hedefe varılamadığını
    /// gösterir (telafi sınırı, sınırlayıcının sesi daha fazla yükseltemediği ses, denemelerin bittiği durum). 0.13.0'a
    /// kadar 0,5 LU idi: -5,45 LUFS'luk sonuç (hedef -5) etiketsiz kalıyordu.
    /// </summary>
    public const double SonucPayi = 0.1;

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

    /// <summary>
    /// Ölçerin üst sınırı (LUFS): FFmpeg'in ebur128'i blokları -70 ile +10 LUFS arasındaki bir çizelgede sayar
    /// (f_ebur128.c, ABS_UP_THRES); +10'un üstündeki blok +10 sayılır, tümleşik yükseklik olduğundan kısık çıkar
    /// (ölçüldü: 10 kanallı ses +30 dB'de 12,4 LUFS iken 9,9). Kayan noktalı ses tam ölçeği aşabilir, çok kanallı
    /// sesin yüksekliği kanalların toplamıdır.
    /// </summary>
    public const double OlcerUstSiniri = 10;

    /// <summary>
    /// K ağırlığının (BS.1770 ön süzgeci + RLB) en büyük kazancı (dB, Nyquist'te): bir bloğun ağırlıklı gücü en çok
    /// tepenin karesi × bu kazanç.
    /// </summary>
    public const double KAgirligiKazanci = 4.04;

    /// <summary>
    /// Ölçülen ses ölçerin üst sınırını (<see cref="OlcerUstSiniri"/>) aşabilir mi: 400 ms'lik bir bloğun yüksekliği
    /// en çok gerçek tepe + K ağırlığının kazancı - 0,691 + 10·log10(kanal ağırlıklarının toplamı; bir kanalınki en çok
    /// 1,41). Aşabilecekse ölçüm bu kadar (dB; 1 dB pay, tam dB) kısılarak yinelenir; aşamazsa 0. Olağan seste (stereo,
    /// tepe tam ölçeğin altında) hiç gerekmez.
    /// </summary>
    public static double OlcumKisilmasi(double tepe, int kanal)
    {
        if (!double.IsFinite(tepe))
        {
            return 0;
        }

        var enYuksekBlok = tepe + KAgirligiKazanci - 0.691 + (10 * Math.Log10(1.41 * Math.Max(1, kanal)));
        var sinir = OlcerUstSiniri - 1;
        return enYuksekBlok > sinir ? Math.Ceiling(enYuksekBlok - sinir) : 0;
    }

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

    /// <summary>
    /// Sınırlayıcı tavandan en çok bu kadar (dB) aşağı çekilir. AAC kodlaması ezilmiş seste tepeyi 6 dB'den fazla da
    /// yükseltebiliyor (ölçüldü: 8 kHz 8 bit ses +30 dB, AAC-LD'de 6,1 dB): tepe yine de tavanın altında kalsın diye.
    /// </summary>
    public const double EnCokTasmaPayi = 12;

    /// <summary>
    /// Tepe denemesi kaynağın tepesi (kazançla) tavanın bu kadar (dB) altına kadar yapılır: kodlama tepeyi olağan seste
    /// bundan fazla yükseltmez (kullanıcının videolarında AAC 128 kb/s 0,1–1 dB).
    /// </summary>
    public const double TepeDenemesiPayi = 6;

    /// <summary>Telafi ve tepe koruması için en çok bu kadar deneme yapılır (yalnızca ses).</summary>
    public const int EnCokDeneme = 6;

    /// <summary>
    /// Deneme ölçümü hedefe bu kadar (LU) yakınsa telafi tamamdır (AAC ve WAV). AAC'nin kaybı küçüktür (kullanıcının
    /// OBS kaydında 128 kb/s'de 0,16 LU) ve kazançla doğrusal değişir: tek düzeltmede hedefe 0,01 içinde gelinir.
    /// </summary>
    public const double KayipliPayi = 0.02;

    /// <summary>Telafinin en küçük adımı (dB).</summary>
    public const double KayipliAdim = 0.01;

    /// <summary>
    /// Ses ya da hedef bundan (LUFS) kısıksa yükseklik çıktının seviyesinde de denenip ölçülür: ölçer -70 LUFS'un altındaki
    /// blokları saymaz (mutlak kapı), -68 LUFS'ta ölçülüp +28 dB yükseltilen ses -40 yerine -40,4 çıkıyordu (ölçüldü).
    /// 20 LU daha yüksekte kapının etkisi 0,05 LU'nun altındadır.
    /// </summary>
    public const double KisikSinir = -45;

    /// <summary>
    /// Çıktının sesi kayıplı mı (FDK AAC): AAC tizleri (128 kb/s'de ~17 kHz üstünü) keser, ölçülen yükseklik biraz
    /// düşer; kodlama gerçek tepeyi de biraz yükseltir. Deneme sesi o zaman AAC'ye çevrilip ölçülür. WAV kayıpsızdır:
    /// denemede kodlanmadan ölçülür.
    /// </summary>
    public static bool KayipliMi(SesAyarlari s) => s.Kodlayici == SesKodlayici.FdkAac;

    /// <summary>
    /// FDK AAC'nin DRC'si açık mı (DRC profili seçili): FFmpeg'in AAC çözücüsü (oynatıcıların ve platformların çoğu)
    /// DRC kazancını uygular, çözülen sesin yüksekliği ve tepesi kodlanan sesinkinden çok farklı olabilir (ölçüldü:
    /// müzik · hafif, hedef -20'de -21,7 LUFS'luk ses -34,8 LUFS çözülüyor). Karar ve tepe koruması çözülen sesle.
    /// </summary>
    public static bool DrcVar(SesAyarlari s) => s.Kodlayici == SesKodlayici.FdkAac && s.Fdk.DrcProfili is > 0;

    /// <summary>
    /// Hedefe kazanç (+ telafi), ölçülen gerçek tepeyi tavanın üstüne çıkarır mı (tepe 0,1 dB'ye yuvarlı ölçüldüğü
    /// için <see cref="TepePayi"/> kadar pay bırakılır: tavanı geçebilecek tepe her zaman sınırlanır).
    /// </summary>
    public static bool SinirlanirMi(SesOlcum o, double hedef, double tavan, double telafi = 0) =>
        o.Tepe + TepePayi + (hedef - o.Yukseklik) + telafi > tavan;

    /// <summary>
    /// Ölçümden sonra karar: hedefe 0,5 LU yakınsa dokunulmaz; yüksek ses "yalnızca kısık sesi yükselt" seçiliyse
    /// dokunulmaz; değilse bütün ses aynı miktarda (sabit kazanç) hedefe getirilir. Ses aralığı (LRA) karara girmez:
    /// kısık ve yüksek yerler arasındaki fark olduğu gibi kalır. <paramref name="o"/>: kazancın hesaplandığı ölçüm;
    /// <paramref name="kararYukseklik"/>: sesin çıktıdaki hâlinin yüksekliği (kazançsız denendiyse; karar bununla).
    /// </summary>
    public static EsitlemeRaporu KararVer(SesAyarlari s, SesOlcum? o, double? kararYukseklik = null)
    {
        EsitlemeRaporu R(EsitlemeKarari k) => new(k, o, s.EsitlemeHedef, s.SinirlayiciTavan) { KararYukseklik = kararYukseklik };

        if (o is null)
        {
            return R(EsitlemeKarari.Olculemedi);
        }

        var fark = s.EsitlemeHedef - (kararYukseklik ?? o.Yukseklik);
        if (Math.Abs(fark) <= Tolerans)
        {
            return R(EsitlemeKarari.Hedefte);
        }

        return fark < 0 && s.EsitlemeYonu == EsitlemeYonu.YalnizcaYukselt
            ? R(EsitlemeKarari.ZatenYuksek)
            : R(EsitlemeKarari.Dogrusal);
    }

    /// <summary>
    /// İki denemenin eğimi en az / en çok bu kadar sayılır: sınırlayıcı kazancın çoğunu yerse 1 dB kazanç yüksekliği
    /// 0,1 LU'ya kadar az artırır (adım en çok eksiğin 10 katı); çok kısık seste ölçerin mutlak kapısı eğimi 1'in
    /// biraz üstüne çıkarabilir.
    /// </summary>
    public const double EnKucukEgim = 0.1;

    /// <inheritdoc cref="EnKucukEgim"/>
    public const double EnBuyukEgim = 1.5;

    /// <summary>
    /// Deneme ölçümünden sonra yeni telafi (dB), sınırlayıcının payı değişmiyorsa (yalnızca AAC, çok kısık ses, DRC).
    /// <paramref name="onceki"/>: önceki denemenin telafisi ve ölçülen yüksekliği.
    /// </summary>
    public static (double? Yeni, bool Sinir) TelafiAdimi(
        double telafi, double olculen, double hedef, (double Telafi, double Olculen)? onceki = null)
    {
        var liste = new List<YukseklikDenemesi>();
        if (onceki is { } o)
        {
            liste.Add(new YukseklikDenemesi(o.Telafi, 0, o.Olculen));
        }

        return TelafiAdimi(new YukseklikDenemesi(telafi, 0, olculen), hedef, 0, liste);
    }

    /// <summary>
    /// Deneme ölçümünden sonra yeni telafi (dB): ölçülen yükseklik hedefin altındaysa artar, üstündeyse azalır (eksi
    /// olabilir: AAC yüksekliği artırabilir, çok kısık seste ölçerin mutlak kapısı sonucu kaydırır); hedefe
    /// <see cref="KayipliPayi"/> içinde gelinene kadar, en küçük adım <see cref="KayipliAdim"/>, en çok ±
    /// <see cref="EnCokTelafi"/>. Sınırlayıcının payı (<paramref name="yeniPay"/>; tepe aştıysa bu denemeyle büyür)
    /// da hesaba girer.
    /// <para>
    /// Sınırlayıcı ölçekten bağımsızdır: tavanı pay kadar aşağı çekilince çıktısı, sesin pay kadar daha az sürülmüş
    /// hâlinin pay kadar kısığıdır (alimiter kararını sesin tavana oranıyla verir; tepeye dokunmadığı seste de doğru).
    /// Bütün denemeler bu yüzden (telafi + pay, ölçülen + pay) düzleminde aynı eğrinin üstündedir: pay değişse de önceki
    /// denemeler (<paramref name="onceki"/>) adımda kullanılır (0.13.0'a kadar pay değişince eğim atılıyordu: hedef -5,
    /// tavan -0,1'de 6 deneme yetmiyordu). Adım: hedefin öbür yanındaki en yakın denemeyle aradeğerleme (yoksa hedefe en
    /// yakın denemeyle eğim); tek deneme varsa eğim 1 (sınırlayıcısız ses). Eğim <see cref="EnKucukEgim"/>–
    /// <see cref="EnBuyukEgim"/> arasında sayılır. Değişmesi gerekmiyorsa (hedefte ve pay aynı, ya da sınırda) null.
    /// </para>
    /// </summary>
    public static (double? Yeni, bool Sinir) TelafiAdimi(
        YukseklikDenemesi son, double hedef, double yeniPay, IReadOnlyList<YukseklikDenemesi> onceki)
    {
        var payAyni = Math.Abs(yeniPay - son.Pay) < 1e-9;
        if (Math.Abs(hedef - son.Olculen) <= KayipliPayi && payAyni)
        {
            return (null, false);
        }

        var x0 = son.Telafi + son.Pay;
        var y0 = son.Olculen + son.Pay;
        var hedefY = hedef + yeniPay;
        var egim = 1.0;
        var adaylar = onceki.Where(x => Math.Abs(x.Telafi + x.Pay - x0) >= KayipliAdim).ToList();
        if (adaylar.Count > 0)
        {
            // Hedefin öbür yanında deneme varsa aradeğerleme (eğri tekdüze: hedef arada kalır), yoksa en yakınla.
            var karsi = adaylar.Where(x => (x.Olculen + x.Pay - hedefY) * (y0 - hedefY) < 0).ToList();
            var secilen = (karsi.Count > 0 ? karsi : adaylar).MinBy(x => Math.Abs(x.Olculen + x.Pay - hedefY));
            var e = (y0 - (secilen.Olculen + secilen.Pay)) / (x0 - (secilen.Telafi + secilen.Pay));
            if (double.IsFinite(e) && e > 0)
            {
                egim = Math.Clamp(e, EnKucukEgim, EnBuyukEgim);
            }
        }

        // Yükseklik hedefteyse (yalnızca pay büyüyor) hedefte sayılır: adım yalnızca payın sınırlayıcıda kıstığı kadar.
        var simdiki = Math.Abs(hedef - son.Olculen) <= KayipliPayi ? hedef + son.Pay : y0;
        var istenen = x0 + ((hedefY - simdiki) / egim) - yeniPay;
        var sinir = Math.Abs(istenen) > EnCokTelafi;
        var yeni = Math.Round(Math.Clamp(istenen, -EnCokTelafi, EnCokTelafi), 2);
        if (yeni == 0)
        {
            // -0 (yuvarlanan küçük eksi sayı) yazıda "-0,00" görünmesin.
            yeni = 0;
        }

        return Math.Abs(yeni - son.Telafi) < KayipliAdim && payAyni ? (null, sinir) : (yeni, sinir);
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
    /// Bir denemeden sonra sesin yeni düzeltmesi. Tepe (<paramref name="tepe"/>: çıktının ölçülen gerçek tepesi; tepe
    /// koruması yoksa null): tavanı aşıyorsa sınırlayıcı zincire girer ve görülen en büyük taşma (çıktının tepesi -
    /// kodlayıcıya giren sesin tepesi, <paramref name="girisTepe"/>) + <see cref="TasmaEki"/> kadar, en az da önceki payın
    /// üstüne aşım + ek kadar aşağı çekilir (pay hiç azalmaz; 0,01 dB'ye yukarı yuvarlanır, en çok
    /// <see cref="EnCokTasmaPayi"/>). Yükseklik (<paramref name="yukseklik"/>: LUFS eşitleme uygulanıyorsa ölçülen; yoksa
    /// null) yeni payla <see cref="TelafiAdimi(YukseklikDenemesi, double, double, IReadOnlyList{YukseklikDenemesi})"/>
    /// ile (pay büyüyünce sınırlayıcının kıstığı da telafi edilir). Değişecek bir şey yoksa (ikisi de tamam ya da sınıra
    /// gelindi) Yeni null: bu denemenin ayarı son ayardır. <paramref name="tasma"/>: şimdiye kadar görülen en büyük
    /// taşma (dB). <paramref name="onceki"/>: önceki yükseklik denemeleri.
    /// </summary>
    public static (SesDuzeltme? Yeni, bool TelafiSiniri, double Tasma) DenemeAdimi(
        SesDuzeltme d, double tasma, double? yukseklik, double hedef,
        double? tepe, bool hassas, double tavan, double? girisTepe, IReadOnlyList<YukseklikDenemesi>? onceki = null)
    {
        var yeni = d;
        if (tepe is { } tp && !TepeTavandaMi(tp, hassas, tavan))
        {
            var asim = tp - tavan;
            tasma = Math.Max(tasma, Math.Max(0, girisTepe is { } g ? tp - g : asim));
            var pay = Math.Max(tasma + TasmaEki, d.TasmaPayi + asim + TasmaEki);
            pay = Math.Min(Math.Ceiling(Math.Round(pay * 100, 6)) / 100, EnCokTasmaPayi);
            yeni = yeni with { TasmaPayi = Math.Max(pay, d.TasmaPayi), Sinirla = true };
        }

        var sinir = false;
        if (yukseklik is { } y)
        {
            var (yeniTelafi, s) = TelafiAdimi(new YukseklikDenemesi(d.Telafi, d.TasmaPayi, y), hedef, yeni.TasmaPayi, onceki ?? []);
            sinir = s;
            if (yeniTelafi is { } t)
            {
                yeni = yeni with { Telafi = t };
            }
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
