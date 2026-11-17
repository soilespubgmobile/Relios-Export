using ReliosExport.Core.Ayarlar;

namespace ReliosExport.Core;

/// <summary>
/// FDK AAC'nin kodlayabildiği ayarlar. Programdaki FFmpeg 9.0.2 (libfdk-aacenc.c) ve FDK AAC 3.0.0 (kodlayıcı kütüphanesi
/// 4.0.1) ile ölçüldü: her profil × FFmpeg'in FDK'ye verdiği 12 örnekleme hızı × 1–8 kanal × 8–800 kb/s'nin her değeri
/// (tools/fdk_sinirlari). Ölçülenler:
/// <list type="bullet">
/// <item>FDK yalnızca 12 hızı kodlar (8–96 kHz); başka hızdaki kaynak (ör. 192 kHz) en yakınına çevrilir.</item>
/// <item>Sınırın dışındaki bit hızını FDK sessizce sınıra çeker (48 kHz stereo AAC-LC'de 800 kb/s istenir, 576 kodlanır).
/// Program aynısını önceden yapar: komut, satırdaki bilgi ve tahmini boyut gerçek değeri gösterir.</item>
/// <item>Bazı birleşimleri FDK hiç açmaz ("Unable to initialize the encoder"), program en yakın açılan profile geçer:
/// HE-AAC v2 yalnızca stereo kodlar (mono / çok kanallı seste HE-AAC; parametrik stereo zaten iki kanal içindir);
/// HE-AAC ve HE-AAC v2 16 kHz'in altında yok (AAC-LC); AAC-LD / ELD 7.1 ve 7.1(wide) düzenini kodlamaz (AAC-LC;
/// 5.1.2(back) ve 6.1(back) olur); AAC-LD / ELD mono 64–96 kHz'te 614 kb/s'nin üstünde açılmaz (614'e çekilir).</item>
/// <item>FDK yalnızca şu kanal düzenlerini kodlar: mono, stereo, 3.0, 4.0, 5.0, 5.1, 6.1(back), 7.1, 7.1(wide),
/// 5.1.2(back). Kanal "kaynakla aynı" iken başka düzendeki ses (ör. 5.1(side), quad, 2.1) FFmpeg'in de seçtiği düzene
/// çevrilir (<see cref="Duzen"/>); program bunu kendisi yazar ki kodlayıcının göreceği kanal sayısı önceden bilinsin.</item>
/// </list>
/// </summary>
public static class FdkUyumu
{
    /// <summary>FFmpeg'in FDK AAC'ye verdiği örnekleme hızları (libfdk-aacenc.c; sıra FFmpeg'inki: en yakını seçerken önemli).</summary>
    public static readonly int[] OrnekHizlari = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000];

    public static bool HizDestekli(int hz) => Array.IndexOf(OrnekHizlari, hz) >= 0;

    /// <summary>
    /// FDK'nin kodlayacağı hız: desteklenen hız olduğu gibi, değilse en yakını (eşitlikte listede önce gelen; FFmpeg de
    /// kendi seçtiğinde böyle yapar). 192 kHz → 96 kHz, 7350 Hz → 8 kHz.
    /// </summary>
    public static int Hiz(int hz)
    {
        var enIyi = OrnekHizlari[0];
        var fark = long.MaxValue;
        foreach (var h in OrnekHizlari)
        {
            var d = Math.Abs((long)hz - h);
            if (d < fark)
            {
                fark = d;
                enIyi = h;
            }
        }

        return enIyi;
    }

    /// <summary>
    /// Kanal "kaynakla aynı" iken FDK'nin kodlayacağı düzen (FFmpeg adı): kaynağın düzeni FDK'de varsa aynısı, yoksa
    /// FFmpeg 9.0.2'nin kendi seçtiği düzen (bu derlemeyle her düzen için ölçüldü: sesin bütün kanallarını taşıyan en
    /// küçük düzen; LFE'si olup FDK'de karşılığı olmayan 2.1 / 3.1'de LFE düşer). Düzen bilinmiyorsa kanal sayısına göre.
    /// </summary>
    public static string Duzen(string? kaynakDuzen, int kanal)
    {
        var d = kaynakDuzen?.Trim().ToLowerInvariant();
        switch (d)
        {
            case "mono":
                return "mono";
            case "stereo" or "2.1" or "downmix" or "binaural":
                return "stereo";
            case "3.0" or "3.1":
                return "3.0";
            case "4.0":
                return "4.0";
            case "3.0(back)" or "quad" or "quad(side)" or "5.0" or "5.0(side)":
                return "5.0";
            case "4.1" or "5.1" or "5.1(side)":
                return "5.1";
            case "6.1(back)":
                return "6.1(back)";
            case "6.0" or "hexagonal" or "6.1" or "7.0" or "7.1" or "octagonal" or "7.1.2" or "7.1.4" or "7.2.3":
                return "7.1";
            case "6.0(front)" or "6.1(front)" or "7.0(front)" or "7.1(wide)" or "7.1(wide-side)" or "9.1.4" or "9.1.6"
                or "hexadecagonal" or "22.2":
                return "7.1(wide)";
            case "3.1.2" or "5.1.2" or "5.1.2(back)" or "cube" or "5.1.4":
                return "5.1.2(back)";
        }

        return kanal switch
        {
            <= 1 => "mono",
            2 => "stereo",
            3 => "3.0",
            4 => "4.0",
            5 => "5.0",
            6 => "5.1",
            7 => "6.1(back)",
            _ => "7.1",
        };
    }

    /// <summary>FDK düzeninin kanal sayısı.</summary>
    public static int KanalSayisi(string duzen) => duzen switch
    {
        "mono" => 1,
        "stereo" => 2,
        "3.0" => 3,
        "4.0" => 4,
        "5.0" => 5,
        "5.1" => 6,
        "6.1(back)" => 7,
        _ => 8,
    };

    /// <summary>
    /// FDK'ye verilecek profil ve (CBR'de) bit hızı. <paramref name="hz"/>: kodlayıcının göreceği örnekleme hızı
    /// (<see cref="Hiz"/>; bilinmiyorsa null: bit hızı olduğu gibi). <paramref name="duzen"/>: kodlayıcının göreceği kanal
    /// düzeni (<see cref="Duzen"/>; mono / stereo seçiliyse o; bilinmiyorsa null). <c>Not</c>: ayar değiştiyse kısa
    /// Türkçe açıklama (satırdaki bilgide görünür).
    /// </summary>
    public static Karar Hesapla(FdkAyarlari f, int? hz, string? duzen)
    {
        var profil = f.Profil is "aac_low" or "aac_he" or "aac_he_v2" or "aac_ld" or "aac_eld" ? f.Profil : "aac_low";
        var notlar = new List<string>();
        var kanal = duzen is null ? (int?)null : KanalSayisi(duzen);
        if (profil == "aac_he_v2" && kanal is { } k && k != 2)
        {
            profil = "aac_he";
            notlar.Add($"HE-AAC v2 yalnızca stereo sesi kodlar: {(k == 1 ? "mono" : k + " kanallı")} seste HE-AAC");
        }

        if (profil is "aac_he" or "aac_he_v2" && hz is < 16000)
        {
            notlar.Add($"{FdkAyarlari.ProfilAdi(profil)} 16 kHz'in altındaki sesi kodlayamaz: AAC-LC");
            profil = "aac_low";
        }

        if (profil is "aac_ld" or "aac_eld" && duzen is "7.1" or "7.1(wide)")
        {
            notlar.Add($"AAC-{(profil == "aac_ld" ? "LD" : "ELD")} {duzen} sesi kodlayamaz: AAC-LC");
            profil = "aac_low";
        }

        int? bitHizi = null;
        if (f.Mod == FdkModu.Cbr)
        {
            bitHizi = f.BitHizi;
            if (hz is { } h && duzen is not null && Sinir(profil, h, duzen) is { } s)
            {
                var enAz = (int)Math.Ceiling(s.EnAz / 1000.0);
                var enCok = s.EnCok / 1000;
                var neresi = $"{FdkAyarlari.ProfilAdi(profil)} {Bicim.Khz(h)} {KanalMetni(duzen)}";
                if (f.BitHizi > enCok)
                {
                    bitHizi = enCok;
                    notlar.Add($"FDK AAC {neresi} sesi en çok {enCok} kb/s kodlar");
                }
                else if (f.BitHizi < enAz)
                {
                    bitHizi = enAz;
                    notlar.Add($"FDK AAC {neresi} sesi en az {enAz} kb/s kodlar");
                }
            }
        }

        return new Karar(profil, bitHizi, notlar.Count > 0 ? string.Join("; ", notlar) : null);
    }

    /// <summary>FDK'nin bu profil, hız ve düzende kullandığı bit hızı aralığı (bit/s); açılmıyorsa ya da bilinmiyorsa null.</summary>
    public static (int EnAz, int EnCok)? Sinir(string profil, int hz, string duzen)
    {
        if (!Tablo.TryGetValue((profil, hz), out var satir))
        {
            return null;
        }

        // 1–7 kanal, 8: 7.1 / 7.1(wide), 9: 5.1.2(back)
        var i = duzen == "5.1.2(back)" ? 8 : KanalSayisi(duzen) - 1;
        var (enAz, enCok) = satir[i];
        return enCok > 0 ? (enAz, enCok) : null;
    }

    private static string KanalMetni(string duzen) => duzen switch
    {
        "mono" => "mono",
        "stereo" => "stereo",
        _ => duzen,
    };

    /// <summary>FDK'ye verilecek profil ve bit hızı (CBR'de kb/s; VBR'de null) ve değiştiyse açıklaması.</summary>
    public sealed record Karar(string Profil, int? BitHizi, string? Not);

    /// <summary>
    /// [(profil, hız)] = 1–8 kanal ve 5.1.2(back) için (en az, en çok) gerçek bit hızı (bit/s); (0, 0): açılmaz. En çok
    /// 800000: programın sınırı (FDK'ninki daha yüksek). tools/fdk_sinirlari/tablo.py ile üretildi.
    /// </summary>
    private static readonly Dictionary<(string Profil, int Hz), (int EnAz, int EnCok)[]> Tablo = new()
    {
        [("aac_low", 8000)] = [(8000, 48000), (8000, 96000), (8000, 144000), (8000, 192000), (8000, 240000), (8000, 240000), (8000, 288000), (8000, 336000), (8000, 336000)],
        [("aac_low", 11025)] = [(8000, 66150), (8000, 132300), (8000, 198450), (8000, 264600), (8000, 330750), (8000, 330750), (8000, 396900), (8000, 463050), (8000, 463050)],
        [("aac_low", 12000)] = [(8000, 72000), (8000, 144000), (8000, 216000), (8000, 288000), (8000, 360000), (8000, 360000), (8000, 432000), (8000, 504000), (8000, 504000)],
        [("aac_low", 16000)] = [(8000, 96000), (8000, 192000), (8000, 288000), (8000, 384000), (8000, 480000), (8000, 480000), (8000, 576000), (8250, 672000), (8250, 672000)],
        [("aac_low", 22050)] = [(8000, 132300), (8000, 264600), (8000, 396900), (8000, 529200), (8785, 661500), (9646, 661500), (10508, 793800), (11369, 800000), (11369, 800000)],
        [("aac_low", 24000)] = [(8000, 144000), (8000, 288000), (8000, 432000), (8625, 576000), (9562, 720000), (10500, 720000), (11437, 800000), (12375, 800000), (12375, 800000)],
        [("aac_low", 32000)] = [(8000, 192000), (9000, 384000), (10250, 576000), (11500, 768000), (12750, 800000), (14000, 800000), (15250, 800000), (16500, 800000), (16500, 800000)],
        [("aac_low", 44100)] = [(10680, 264600), (12403, 529200), (14125, 793800), (15848, 800000), (17571, 800000), (19293, 800000), (21016, 800000), (22739, 800000), (22739, 800000)],
        [("aac_low", 48000)] = [(11625, 288000), (13500, 576000), (15375, 800000), (17250, 800000), (19125, 800000), (21000, 800000), (22875, 800000), (24750, 800000), (24750, 800000)],
        [("aac_low", 64000)] = [(15500, 384000), (18000, 768000), (20500, 800000), (23000, 800000), (25500, 800000), (28000, 800000), (30500, 800000), (33000, 800000), (33000, 800000)],
        [("aac_low", 88200)] = [(21360, 529200), (24806, 800000), (28251, 800000), (31696, 800000), (35142, 800000), (38587, 800000), (42032, 800000), (45478, 800000), (45478, 800000)],
        [("aac_low", 96000)] = [(23250, 576000), (27000, 800000), (30750, 800000), (34500, 800000), (38250, 800000), (42000, 800000), (45750, 800000), (49500, 800000), (49500, 800000)],
        [("aac_he", 8000)] = [(0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he", 11025)] = [(0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he", 12000)] = [(0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he", 16000)] = [(8000, 48000), (16000, 96000), (26679, 144000), (40000, 192000), (43264, 240000), (45737, 240000), (58210, 288000), (61569, 336000), (61569, 336000)],
        [("aac_he", 22050)] = [(8000, 66150), (16000, 132300), (26679, 198450), (40000, 264600), (43264, 330750), (45737, 330750), (58210, 396900), (61569, 463050), (61569, 463050)],
        [("aac_he", 24000)] = [(8000, 72000), (16000, 144000), (26679, 216000), (40000, 288000), (43264, 360000), (45737, 360000), (58210, 432000), (61569, 504000), (61569, 504000)],
        [("aac_he", 32000)] = [(8000, 96000), (16000, 192000), (26679, 288000), (40000, 384000), (43264, 480000), (45737, 480000), (58210, 576000), (61569, 672000), (61569, 672000)],
        [("aac_he", 44100)] = [(11377, 132300), (16000, 264600), (28442, 396900), (40000, 529200), (43757, 661500), (47404, 661500), (58210, 793800), (63205, 800000), (63205, 800000)],
        [("aac_he", 48000)] = [(12000, 144000), (16000, 288000), (30000, 432000), (40000, 576000), (46184, 720000), (50000, 720000), (60000, 800000), (66711, 800000), (66711, 800000)],
        [("aac_he", 64000)] = [(24000, 192000), (32000, 384000), (60000, 576000), (80000, 768000), (92338, 800000), (100000, 800000), (120000, 800000), (133377, 800000), (133377, 800000)],
        [("aac_he", 88200)] = [(24000, 264600), (32000, 529200), (60000, 793800), (80000, 800000), (92338, 800000), (100000, 800000), (120000, 800000), (133377, 800000), (133377, 800000)],
        [("aac_he", 96000)] = [(32000, 288000), (36000, 576000), (80000, 800000), (106693, 800000), (123107, 800000), (133366, 800000), (160000, 800000), (177822, 800000), (177822, 800000)],
        [("aac_he_v2", 8000)] = [(0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he_v2", 11025)] = [(0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he_v2", 12000)] = [(0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he_v2", 16000)] = [(0, 0), (8000, 48000), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he_v2", 22050)] = [(0, 0), (8000, 66150), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he_v2", 24000)] = [(0, 0), (8000, 72000), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he_v2", 32000)] = [(0, 0), (8000, 96000), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he_v2", 44100)] = [(0, 0), (11377, 132300), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he_v2", 48000)] = [(0, 0), (12000, 144000), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he_v2", 64000)] = [(0, 0), (24000, 192000), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he_v2", 88200)] = [(0, 0), (24000, 264600), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_he_v2", 96000)] = [(0, 0), (32000, 288000), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0), (0, 0)],
        [("aac_ld", 8000)] = [(8000, 96000), (16000, 192000), (24000, 288000), (32000, 384000), (40000, 480000), (40000, 480000), (48000, 576000), (0, 0), (56000, 672000)],
        [("aac_ld", 11025)] = [(8000, 132300), (16000, 264600), (24000, 396900), (32000, 529200), (40000, 661500), (40000, 661500), (48000, 793800), (0, 0), (56000, 800000)],
        [("aac_ld", 12000)] = [(8000, 144000), (16000, 288000), (24000, 432000), (32000, 576000), (40000, 720000), (40000, 720000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_ld", 16000)] = [(8000, 192000), (16000, 384000), (24000, 576000), (32000, 768000), (40000, 800000), (40000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_ld", 22050)] = [(10680, 264600), (16000, 529200), (24000, 793800), (32000, 800000), (40000, 800000), (40000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_ld", 24000)] = [(11625, 288000), (16000, 576000), (24000, 800000), (32000, 800000), (40000, 800000), (40000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_ld", 32000)] = [(15500, 384000), (18000, 768000), (24000, 800000), (32000, 800000), (40000, 800000), (40000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_ld", 44100)] = [(21360, 529200), (24806, 800000), (28251, 800000), (32000, 800000), (40000, 800000), (40000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_ld", 48000)] = [(23250, 576000), (27000, 800000), (30750, 800000), (34500, 800000), (40000, 800000), (42000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_ld", 64000)] = [(31000, 614000), (36000, 800000), (41000, 800000), (46000, 800000), (51000, 800000), (56000, 800000), (61000, 800000), (0, 0), (66000, 800000)],
        [("aac_ld", 88200)] = [(42721, 614000), (49612, 800000), (56503, 800000), (63393, 800000), (70284, 800000), (77175, 800000), (84065, 800000), (0, 0), (90956, 800000)],
        [("aac_ld", 96000)] = [(46500, 614000), (54000, 800000), (61500, 800000), (69000, 800000), (76500, 800000), (84000, 800000), (91500, 800000), (0, 0), (99000, 800000)],
        [("aac_eld", 8000)] = [(8000, 96000), (16000, 192000), (24000, 288000), (32000, 384000), (40000, 480000), (40000, 480000), (48000, 576000), (0, 0), (56000, 672000)],
        [("aac_eld", 11025)] = [(8000, 132300), (16000, 264600), (24000, 396900), (32000, 529200), (40000, 661500), (40000, 661500), (48000, 793800), (0, 0), (56000, 800000)],
        [("aac_eld", 12000)] = [(8000, 144000), (16000, 288000), (24000, 432000), (32000, 576000), (40000, 720000), (40000, 720000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_eld", 16000)] = [(16000, 192000), (32000, 384000), (24000, 576000), (32000, 768000), (40000, 800000), (40000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_eld", 22050)] = [(18000, 264600), (32000, 529200), (24000, 793800), (32000, 800000), (40000, 800000), (40000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_eld", 24000)] = [(20000, 288000), (32000, 576000), (24000, 800000), (32000, 800000), (40000, 800000), (40000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_eld", 32000)] = [(16000, 384000), (32000, 768000), (24000, 800000), (32000, 800000), (40000, 800000), (40000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_eld", 44100)] = [(18000, 529200), (32000, 800000), (28251, 800000), (32000, 800000), (40000, 800000), (40000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_eld", 48000)] = [(20000, 576000), (32000, 800000), (30750, 800000), (34500, 800000), (40000, 800000), (42000, 800000), (48000, 800000), (0, 0), (56000, 800000)],
        [("aac_eld", 64000)] = [(31000, 614000), (36000, 800000), (41000, 800000), (46000, 800000), (51000, 800000), (56000, 800000), (61000, 800000), (0, 0), (66000, 800000)],
        [("aac_eld", 88200)] = [(42721, 614000), (49612, 800000), (56503, 800000), (63393, 800000), (70284, 800000), (77175, 800000), (84065, 800000), (0, 0), (90956, 800000)],
        [("aac_eld", 96000)] = [(46500, 614000), (54000, 800000), (61500, 800000), (69000, 800000), (76500, 800000), (84000, 800000), (91500, 800000), (0, 0), (99000, 800000)],
    };
}
