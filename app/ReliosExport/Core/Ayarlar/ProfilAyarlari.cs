using System.Globalization;
using System.Text.Json.Serialization;

namespace ReliosExport.Core.Ayarlar;

// ======================================================================================
// Bir profilin (TikTok / YouTube) tüm ayarları: Video, Ses ve Anahtar kare.
// Değeri null olan x264 / NVENC ayarları komuta yazılmaz; değeri seçilen ön ayar (preset) belirler.
// ======================================================================================

public enum CozunurlukModu { Kaynak, Hedef }

/// <summary>Kaynağın en-boy oranı hedeften farklıysa ne yapılacağı.</summary>
public enum OranUyumu { OrtadanKirp, KenarEkle, Esnet }

public enum KareHiziModu { Kaynak, Sabit }

public enum VideoKodlayici { X264, Nvenc }

public enum X264Modu { Crf, SabitQp, OrtalamaBitHizi, IkiGecis, UcGecis, Cbr }

public enum NvencModu { SabitKalite, SabitQp, DegiskenBitHizi, Cbr }

public enum SesKaynagi { AyniAdliDosyaOncelikli, VideonunSesi, Sessiz }

public enum SesKodlayici { FdkAac, Wav }

public enum KanalModu { Kaynak, Mono, Stereo }

public enum DonusumKalitesi { Yuksek, CokYuksek, EnYuksek }

public enum Titresim { Yok, Ucgen, UcgenYuksekGecis, Shibata }

public enum FdkModu { Cbr, Vbr1, Vbr2, Vbr3, Vbr4, Vbr5 }

public enum WavBitDerinligi { Bit16, Bit24, Bit32, Float32 }

public enum AralikBirimi { Kare, Saniye }

public sealed class ProfilAyarlari : Gozlenebilir
{
    private VideoAyarlari _video = new();
    private SesAyarlari _ses = new();
    private AnahtarKareAyarlari _anahtarKare = new();

    public ProfilAyarlari()
    {
        Izle(_video);
        Izle(_ses);
        Izle(_anahtarKare);
    }

    /// <summary>Ayar dosyasının biçim sürümü (ileride değişirse eski kayıtlar okunabilsin).</summary>
    public int Surum { get; set; } = 1;

    public VideoAyarlari Video { get => _video; set => Bagla(ref _video, value); }

    public SesAyarlari Ses { get => _ses; set => Bagla(ref _ses, value); }

    public AnahtarKareAyarlari AnahtarKare { get => _anahtarKare; set => Bagla(ref _anahtarKare, value); }

    public ProfilAyarlari Kopya() => AyarDosyasi.Kopyala(this);
}

// ------------------------------------------------------------------------------- Video

public sealed class VideoAyarlari : Gozlenebilir
{
    private CozunurlukModu _cozunurluk = CozunurlukModu.Hedef;
    private int _genislik = 1216;
    private int _yukseklik = 2160;
    private OranUyumu _oran = OranUyumu.OrtadanKirp;
    private string _olcekleme = "lanczos";
    private bool _hassasYuvarlama;
    private KareHiziModu _kareHiziModu = KareHiziModu.Sabit;
    private string _kareHizi = "30000/1001";
    private VideoKodlayici _kodlayici = VideoKodlayici.X264;
    private X264Ayarlari _x264 = new();
    private NvencAyarlari _nvenc = new();

    public VideoAyarlari()
    {
        Izle(_x264);
        Izle(_nvenc);
    }

    public CozunurlukModu Cozunurluk { get => _cozunurluk; set => Set(ref _cozunurluk, value); }

    public int Genislik { get => _genislik; set => Set(ref _genislik, Math.Clamp(value, 16, 8192)); }

    public int Yukseklik { get => _yukseklik; set => Set(ref _yukseklik, Math.Clamp(value, 16, 8192)); }

    public OranUyumu Oran { get => _oran; set => Set(ref _oran, value); }

    /// <summary>FFmpeg ölçekleme yöntemi: lanczos, spline, bicubic, bilinear, area, neighbor.</summary>
    public string Olcekleme { get => _olcekleme; set => Set(ref _olcekleme, value); }

    /// <summary>Ölçeklemede hassas yuvarlama (accurate_rnd).</summary>
    public bool HassasYuvarlama { get => _hassasYuvarlama; set => Set(ref _hassasYuvarlama, value); }

    public KareHiziModu KareHiziModu { get => _kareHiziModu; set => Set(ref _kareHiziModu, value); }

    /// <summary>Hedef kare hızı, kesir olarak (ör. 30000/1001).</summary>
    public string KareHizi { get => _kareHizi; set => Set(ref _kareHizi, value); }

    public VideoKodlayici Kodlayici { get => _kodlayici; set => Set(ref _kodlayici, value); }

    public X264Ayarlari X264 { get => _x264; set => Bagla(ref _x264, value); }

    public NvencAyarlari Nvenc { get => _nvenc; set => Bagla(ref _nvenc, value); }

    /// <summary>Panelde boyut listesi: "kaynak" ya da "GxY".</summary>
    [JsonIgnore]
    public string BoyutSecimi
    {
        get => Cozunurluk == CozunurlukModu.Kaynak ? "kaynak" : $"{Genislik}x{Yukseklik}";
        set
        {
            if (value == "kaynak")
            {
                Cozunurluk = CozunurlukModu.Kaynak;
                return;
            }

            if (value == "ozel")
            {
                // Listede olmayan boyut: genişlik ve yükseklik kutulardan girilir.
                Cozunurluk = CozunurlukModu.Hedef;
                return;
            }

            var p = (value ?? string.Empty).Split('x');
            if (p.Length == 2 && int.TryParse(p[0], out var w) && int.TryParse(p[1], out var h))
            {
                Genislik = w;
                Yukseklik = h;
                Cozunurluk = CozunurlukModu.Hedef;
            }
        }
    }

    /// <summary>Panelde kare hızı listesi: "kaynak" ya da kesir.</summary>
    [JsonIgnore]
    public string KareHiziSecimi
    {
        get => KareHiziModu == KareHiziModu.Kaynak ? "kaynak" : KareHizi;
        set
        {
            if (value == "kaynak")
            {
                KareHiziModu = KareHiziModu.Kaynak;
            }
            else if (Bicim.KesirCoz(value) is not null)
            {
                KareHizi = value!;
                KareHiziModu = KareHiziModu.Sabit;
            }
        }
    }

    [JsonIgnore]
    public string CozunurlukOzeti => Cozunurluk == CozunurlukModu.Kaynak
        ? "Kaynakla aynı"
        : $"{Genislik}×{Yukseklik} · {OranMetni(Oran)} · {OlceklemeAdi(Olcekleme)}";


    [JsonIgnore]
    public string KareHiziOzeti => KareHiziModu == KareHiziModu.Kaynak
        ? "Kaynakla aynı"
        : $"{Bicim.KareHiziMetni(KareHizi)} fps (sabit)";

    [JsonIgnore]
    public string KodlayiciOzeti => Kodlayici == VideoKodlayici.X264 ? X264.Ozet : Nvenc.Ozet;

    [JsonIgnore]
    public override string Ozet => $"{CozunurlukOzeti} · {KareHiziOzeti}";

    protected override IEnumerable<string> EkBildirimler(string? name) =>
        [nameof(CozunurlukOzeti), nameof(KareHiziOzeti), nameof(KodlayiciOzeti), nameof(BoyutSecimi), nameof(KareHiziSecimi)];

    public static string OranMetni(OranUyumu o) => o switch
    {
        OranUyumu.OrtadanKirp => "ortadan kırp",
        OranUyumu.KenarEkle => "siyah kenar",
        _ => "esnet",
    };

    public static string OlceklemeAdi(string s) => s switch
    {
        "lanczos" => "Lanczos",
        "spline" => "Spline",
        "bicubic" => "Bicubic",
        "bilinear" => "Bilinear",
        "area" => "Area",
        "neighbor" => "En yakın",
        _ => s,
    };
}

/// <summary>x264 ayarları; XviD4PSP'deki sekmelere göre gruplanmıştır.</summary>
public sealed class X264Ayarlari : Gozlenebilir
{
    // --- Ana
    private string _onAyar = "medium";
    private string _ayar = string.Empty;
    private string _profil = "main";
    private string _seviye = "5.0";
    private X264Modu _mod = X264Modu.Crf;
    private double _crf = 18;
    private int _qp = 20;
    private int _bitHizi = 8000;
    private bool _hizliIlkGecis = true;
    private int? _isParcacigi;
    private int? _ileriBakisIsParcacigi;

    // --- Hareket
    private string? _me;
    private int? _meAraligi;
    private int? _altPiksel;
    private string? _dogrudanMv;
    private int? _agirlikliP;
    private bool _agirlikliB = true;
    private bool? _karisikRef;
    private bool? _kromaMe;

    // --- Kare
    private int? _ref;
    private int? _bKare;
    private int? _bSapma;
    private int? _bUyarlamali;
    private string? _bPiramit;
    private int? _ileriBakis;
    private int? _dilim;
    private bool _deblock = true;
    private int? _deblockAlfa;
    private int? _deblockBeta;
    private bool _cabac = true;
    private bool _gecmeli;

    // --- Analiz
    private string? _bolumler;
    private bool? _dct8x8;
    private int? _trellis;
    private double? _psyRd;
    private double? _psyTrellis;
    private bool _psy = true;
    private int? _oluBolgeInter;
    private int? _oluBolgeIntra;
    private int? _gurultuAzaltma;
    private bool? _mbAgaci;
    private bool? _hizliPAtlama;
    private bool? _dctSeyreltme;

    // --- Niceleyici
    private int? _qpMin;
    private int? _qpMax;
    private int? _qpAdim;
    private double? _ipOrani;
    private double? _pbOrani;
    private int? _kromaQpOfseti;
    private double? _qcomp;
    private double? _cplxBlur;
    private double? _qBlur;
    private string? _cqm;
    private int? _aqModu;
    private double? _aqGucu;

    // --- Gelişmiş
    private int? _vbvMaks;
    private int? _vbvTampon;
    private double? _vbvBaslangic;
    private string? _nalHrd;
    private bool _dolgu;
    private bool _aud;
    private bool _picStruct;
    private bool _blurayUyumlu;
    private bool _kisitliIntra;
    private string _ekParametreler = string.Empty;

    public string OnAyar { get => _onAyar; set => Set(ref _onAyar, value); }

    /// <summary>x264 --tune (boş: yok).</summary>
    public string Ayar { get => _ayar; set => Set(ref _ayar, value); }

    /// <summary>baseline, main, high, high10, high422, high444; boş: otomatik.</summary>
    public string Profil { get => _profil; set => Set(ref _profil, value); }

    /// <summary>Ör. 5.0; boş: otomatik.</summary>
    public string Seviye { get => _seviye; set => Set(ref _seviye, value); }

    public X264Modu Mod { get => _mod; set => Set(ref _mod, value); }

    public double Crf { get => _crf; set => Set(ref _crf, Math.Clamp(value, 0, 51)); }

    public int Qp { get => _qp; set => Set(ref _qp, Math.Clamp(value, 0, 69)); }

    /// <summary>kb/s (ortalama, 2/3 geçiş ve CBR modlarında).</summary>
    public int BitHizi { get => _bitHizi; set => Set(ref _bitHizi, Math.Clamp(value, 1, 1_000_000)); }

    public bool HizliIlkGecis { get => _hizliIlkGecis; set => Set(ref _hizliIlkGecis, value); }

    public int? IsParcacigi { get => _isParcacigi; set => Set(ref _isParcacigi, value); }

    public int? IleriBakisIsParcacigi { get => _ileriBakisIsParcacigi; set => Set(ref _ileriBakisIsParcacigi, value); }

    public string? Me { get => _me; set => Set(ref _me, Bos(value)); }

    public int? MeAraligi { get => _meAraligi; set => Set(ref _meAraligi, value); }

    public int? AltPiksel { get => _altPiksel; set => Set(ref _altPiksel, value); }

    public string? DogrudanMv { get => _dogrudanMv; set => Set(ref _dogrudanMv, Bos(value)); }

    public int? AgirlikliP { get => _agirlikliP; set => Set(ref _agirlikliP, value); }

    public bool AgirlikliB { get => _agirlikliB; set => Set(ref _agirlikliB, value); }

    public bool? KarisikRef { get => _karisikRef; set => Set(ref _karisikRef, value); }

    public bool? KromaMe { get => _kromaMe; set => Set(ref _kromaMe, value); }

    public int? Ref { get => _ref; set => Set(ref _ref, value); }

    public int? BKare { get => _bKare; set => Set(ref _bKare, value); }

    public int? BSapma { get => _bSapma; set => Set(ref _bSapma, value); }

    public int? BUyarlamali { get => _bUyarlamali; set => Set(ref _bUyarlamali, value); }

    public string? BPiramit { get => _bPiramit; set => Set(ref _bPiramit, Bos(value)); }

    public int? IleriBakis { get => _ileriBakis; set => Set(ref _ileriBakis, value); }

    public int? Dilim { get => _dilim; set => Set(ref _dilim, value); }

    public bool Deblock { get => _deblock; set => Set(ref _deblock, value); }

    public int? DeblockAlfa { get => _deblockAlfa; set => Set(ref _deblockAlfa, value); }

    public int? DeblockBeta { get => _deblockBeta; set => Set(ref _deblockBeta, value); }

    public bool Cabac { get => _cabac; set => Set(ref _cabac, value); }

    /// <summary>Geçmeli (interlaced) kodlama.</summary>
    public bool Gecmeli { get => _gecmeli; set => Set(ref _gecmeli, value); }

    public string? Bolumler { get => _bolumler; set => Set(ref _bolumler, Bos(value)); }

    public bool? Dct8x8 { get => _dct8x8; set => Set(ref _dct8x8, value); }

    public int? Trellis { get => _trellis; set => Set(ref _trellis, value); }

    public double? PsyRd { get => _psyRd; set => Set(ref _psyRd, value); }

    public double? PsyTrellis { get => _psyTrellis; set => Set(ref _psyTrellis, value); }

    public bool Psy { get => _psy; set => Set(ref _psy, value); }

    public int? OluBolgeInter { get => _oluBolgeInter; set => Set(ref _oluBolgeInter, value); }

    public int? OluBolgeIntra { get => _oluBolgeIntra; set => Set(ref _oluBolgeIntra, value); }

    public int? GurultuAzaltma { get => _gurultuAzaltma; set => Set(ref _gurultuAzaltma, value); }

    public bool? MbAgaci { get => _mbAgaci; set => Set(ref _mbAgaci, value); }

    public bool? HizliPAtlama { get => _hizliPAtlama; set => Set(ref _hizliPAtlama, value); }

    public bool? DctSeyreltme { get => _dctSeyreltme; set => Set(ref _dctSeyreltme, value); }

    public int? QpMin { get => _qpMin; set => Set(ref _qpMin, value); }

    public int? QpMax { get => _qpMax; set => Set(ref _qpMax, value); }

    public int? QpAdim { get => _qpAdim; set => Set(ref _qpAdim, value); }

    public double? IpOrani { get => _ipOrani; set => Set(ref _ipOrani, value); }

    public double? PbOrani { get => _pbOrani; set => Set(ref _pbOrani, value); }

    public int? KromaQpOfseti { get => _kromaQpOfseti; set => Set(ref _kromaQpOfseti, value); }

    public double? Qcomp { get => _qcomp; set => Set(ref _qcomp, value); }

    public double? CplxBlur { get => _cplxBlur; set => Set(ref _cplxBlur, value); }

    public double? QBlur { get => _qBlur; set => Set(ref _qBlur, value); }

    public string? Cqm { get => _cqm; set => Set(ref _cqm, Bos(value)); }

    public int? AqModu { get => _aqModu; set => Set(ref _aqModu, value); }

    public double? AqGucu { get => _aqGucu; set => Set(ref _aqGucu, value); }

    public int? VbvMaks { get => _vbvMaks; set => Set(ref _vbvMaks, value); }

    public int? VbvTampon { get => _vbvTampon; set => Set(ref _vbvTampon, value); }

    public double? VbvBaslangic { get => _vbvBaslangic; set => Set(ref _vbvBaslangic, value); }

    public string? NalHrd { get => _nalHrd; set => Set(ref _nalHrd, Bos(value)); }

    public bool Dolgu { get => _dolgu; set => Set(ref _dolgu, value); }

    public bool Aud { get => _aud; set => Set(ref _aud, value); }

    public bool PicStruct { get => _picStruct; set => Set(ref _picStruct, value); }

    public bool BlurayUyumlu { get => _blurayUyumlu; set => Set(ref _blurayUyumlu, value); }

    public bool KisitliIntra { get => _kisitliIntra; set => Set(ref _kisitliIntra, value); }

    /// <summary>x264-params sonuna eklenecek serbest ayarlar (anahtar=değer:anahtar=değer).</summary>
    public string EkParametreler { get => _ekParametreler; set => Set(ref _ekParametreler, value ?? string.Empty); }

    [JsonIgnore]
    public string ModOzeti => Mod switch
    {
        X264Modu.Crf => "CRF " + Bicim.Sayi(Crf),
        X264Modu.SabitQp => "Sabit QP " + Qp,
        X264Modu.OrtalamaBitHizi => Bicim.BitHizi(BitHizi) + " ortalama",
        X264Modu.IkiGecis => Bicim.BitHizi(BitHizi) + " · 2 geçiş",
        X264Modu.UcGecis => Bicim.BitHizi(BitHizi) + " · 3 geçiş",
        _ => Bicim.BitHizi(BitHizi) + " CBR",
    };

    [JsonIgnore]
    public override string Ozet =>
        $"x264 · {ModOzeti} · {OnAyar} · {Bicim.ProfilSeviye(Profil, Seviye)}";

    protected override IEnumerable<string> EkBildirimler(string? name) => [nameof(ModOzeti)];

    private static string? Bos(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>NVENC H.264 (ekran kartı) ayarları.</summary>
public sealed class NvencAyarlari : Gozlenebilir
{
    private string _onAyar = "p7";
    private string _ayar = "hq";
    private string _profil = "high";
    private string _seviye = string.Empty;
    private NvencModu _mod = NvencModu.SabitKalite;
    private int _cq = 19;
    private int _qp = 20;
    private int _bitHizi = 12000;
    private int? _maksBitHizi;
    private int? _tampon;
    private string _cokGecis = "fullres";
    private int? _ileriBakis = 32;
    private bool _uzamsalAq = true;
    private int _aqGucu = 8;
    private bool _zamansalAq = true;
    private int? _bKare = 3;
    private string _bRefModu = "middle";
    private int? _ref;
    private bool _agirlikliTahmin;
    private string _kodlama = "cabac";
    private bool _gpuCozme;
    private string _ekParametreler = string.Empty;

    /// <summary>p1 (en hızlı) … p7 (en kaliteli).</summary>
    public string OnAyar { get => _onAyar; set => Set(ref _onAyar, value); }

    /// <summary>hq, ll, ull, lossless.</summary>
    public string Ayar { get => _ayar; set => Set(ref _ayar, value); }

    /// <summary>baseline, main, high, high444p; boş: otomatik.</summary>
    public string Profil { get => _profil; set => Set(ref _profil, value); }

    public string Seviye { get => _seviye; set => Set(ref _seviye, value); }

    public NvencModu Mod { get => _mod; set => Set(ref _mod, value); }

    public int Cq { get => _cq; set => Set(ref _cq, Math.Clamp(value, 0, 51)); }

    public int Qp { get => _qp; set => Set(ref _qp, Math.Clamp(value, 0, 51)); }

    public int BitHizi { get => _bitHizi; set => Set(ref _bitHizi, Math.Clamp(value, 1, 1_000_000)); }

    public int? MaksBitHizi { get => _maksBitHizi; set => Set(ref _maksBitHizi, value); }

    public int? Tampon { get => _tampon; set => Set(ref _tampon, value); }

    /// <summary>disabled, qres, fullres.</summary>
    public string CokGecis { get => _cokGecis; set => Set(ref _cokGecis, value); }

    public int? IleriBakis { get => _ileriBakis; set => Set(ref _ileriBakis, value); }

    public bool UzamsalAq { get => _uzamsalAq; set => Set(ref _uzamsalAq, value); }

    public int AqGucu { get => _aqGucu; set => Set(ref _aqGucu, Math.Clamp(value, 1, 15)); }

    public bool ZamansalAq { get => _zamansalAq; set => Set(ref _zamansalAq, value); }

    public int? BKare { get => _bKare; set => Set(ref _bKare, value); }

    /// <summary>disabled, each, middle.</summary>
    public string BRefModu { get => _bRefModu; set => Set(ref _bRefModu, value); }

    public int? Ref { get => _ref; set => Set(ref _ref, value); }

    public bool AgirlikliTahmin { get => _agirlikliTahmin; set => Set(ref _agirlikliTahmin, value); }

    /// <summary>cabac, cavlc.</summary>
    public string Kodlama { get => _kodlama; set => Set(ref _kodlama, value); }

    /// <summary>Kaynağı da ekran kartıyla çöz (NVDEC).</summary>
    public bool GpuCozme { get => _gpuCozme; set => Set(ref _gpuCozme, value); }

    /// <summary>Komuta eklenecek serbest ffmpeg seçenekleri (ör. -surfaces 32).</summary>
    public string EkParametreler { get => _ekParametreler; set => Set(ref _ekParametreler, value ?? string.Empty); }

    [JsonIgnore]
    public string ModOzeti => Mod switch
    {
        NvencModu.SabitKalite => "Sabit kalite " + Cq,
        NvencModu.SabitQp => "Sabit QP " + Qp,
        NvencModu.DegiskenBitHizi => Bicim.BitHizi(BitHizi) + " VBR",
        _ => Bicim.BitHizi(BitHizi) + " CBR",
    };

    [JsonIgnore]
    public override string Ozet =>
        $"NVENC · {ModOzeti} · {OnAyar} · {Bicim.ProfilSeviye(Profil, Seviye)}";

    protected override IEnumerable<string> EkBildirimler(string? name) => [nameof(ModOzeti)];
}

// ------------------------------------------------------------------------------- Ses

public sealed class SesAyarlari : Gozlenebilir
{
    private SesKaynagi _kaynak = SesKaynagi.AyniAdliDosyaOncelikli;
    private int _gecikme;
    private double _seviye;
    private int? _ornekHizi = 48000;
    private DonusumKalitesi _kalite = DonusumKalitesi.Yuksek;
    private Titresim _titresim = Titresim.Yok;
    private KanalModu _kanal = KanalModu.Stereo;
    private SesKodlayici _kodlayici = SesKodlayici.FdkAac;
    private FdkAyarlari _fdk = new();
    private WavBitDerinligi _wavBit = WavBitDerinligi.Bit24;

    public SesAyarlari()
    {
        Izle(_fdk);
    }

    public SesKaynagi Kaynak { get => _kaynak; set => Set(ref _kaynak, value); }

    /// <summary>Milisaniye; eksi değer sesi öne alır (baştan keser).</summary>
    public int Gecikme { get => _gecikme; set => Set(ref _gecikme, Math.Clamp(value, -60_000, 60_000)); }

    /// <summary>Desibel; 0 değiştirmez.</summary>
    public double Seviye { get => _seviye; set => Set(ref _seviye, Math.Clamp(Math.Round(value, 1), -60, 30)); }

    /// <summary>Hz; null: kaynakla aynı.</summary>
    public int? OrnekHizi { get => _ornekHizi; set => Set(ref _ornekHizi, value); }

    public DonusumKalitesi Kalite { get => _kalite; set => Set(ref _kalite, value); }

    public Titresim Titresim { get => _titresim; set => Set(ref _titresim, value); }

    public KanalModu Kanal { get => _kanal; set => Set(ref _kanal, value); }

    public SesKodlayici Kodlayici { get => _kodlayici; set => Set(ref _kodlayici, value); }

    public FdkAyarlari Fdk { get => _fdk; set => Bagla(ref _fdk, value); }

    public WavBitDerinligi WavBit { get => _wavBit; set => Set(ref _wavBit, value); }

    [JsonIgnore]
    public string KaynakOzeti => Kaynak switch
    {
        SesKaynagi.AyniAdliDosyaOncelikli => "Aynı adlı ses dosyası (yoksa videonun sesi)",
        SesKaynagi.VideonunSesi => "Videonun kendi sesi",
        _ => "Sessiz",
    };

    [JsonIgnore]
    public string GecikmeOzeti => Gecikme == 0 ? "Yok" : $"{Gecikme} ms";

    [JsonIgnore]
    public string SeviyeOzeti => Seviye == 0 ? "Değişmez (0 dB)" : $"{(Seviye > 0 ? "+" : string.Empty)}{Bicim.Sayi(Seviye)} dB";

    [JsonIgnore]
    public string OrnekHiziOzeti => (OrnekHizi is { } hz ? Bicim.Khz(hz) : "Kaynakla aynı") + " · " + KaliteMetni(Kalite)
        + (Titresim == Titresim.Yok ? string.Empty : " · titreşim");

    [JsonIgnore]
    public string KanalOzeti => Kanal switch
    {
        KanalModu.Stereo => "Stereo",
        KanalModu.Mono => "Mono",
        _ => "Kaynakla aynı",
    };

    [JsonIgnore]
    public string KodlayiciOzeti => Kodlayici == SesKodlayici.FdkAac
        ? Fdk.Ozet
        : "WAV · " + WavMetni(WavBit) + " · MOV kabı";

    [JsonIgnore]
    public override string Ozet => KaynakOzeti;

    protected override IEnumerable<string> EkBildirimler(string? name) =>
        [nameof(KaynakOzeti), nameof(GecikmeOzeti), nameof(SeviyeOzeti), nameof(OrnekHiziOzeti), nameof(KanalOzeti), nameof(KodlayiciOzeti)];

    public static string KaliteMetni(DonusumKalitesi k) => k switch
    {
        DonusumKalitesi.Yuksek => "yüksek kalite",
        DonusumKalitesi.CokYuksek => "çok yüksek kalite",
        _ => "en yüksek kalite",
    };

    public static string WavMetni(WavBitDerinligi b) => b switch
    {
        WavBitDerinligi.Bit16 => "16 bit",
        WavBitDerinligi.Bit24 => "24 bit",
        WavBitDerinligi.Bit32 => "32 bit",
        _ => "32 bit float",
    };
}

public sealed class FdkAyarlari : Gozlenebilir
{
    private string _profil = "aac_low";
    private FdkModu _mod = FdkModu.Cbr;
    private int _bitHizi = 128;
    private int _kesim;
    private bool _afterburner = true;
    private string _sinyal = "default";
    private int? _drcProfili;
    private int? _sikistirmaProfili;
    private int? _drcHedef;
    private int? _sikistirmaHedef;
    private int? _programSeviyesi;

    /// <summary>aac_low (LC), aac_he (HE-AAC), aac_he_v2 (HE-AAC v2), aac_ld, aac_eld.</summary>
    public string Profil { get => _profil; set => Set(ref _profil, value); }

    public FdkModu Mod { get => _mod; set => Set(ref _mod, value); }

    /// <summary>kb/s (CBR).</summary>
    public int BitHizi { get => _bitHizi; set => Set(ref _bitHizi, Math.Clamp(value, 8, 800)); }

    /// <summary>Kesim frekansı (Hz); 0: otomatik.</summary>
    public int Kesim { get => _kesim; set => Set(ref _kesim, Math.Clamp(value, 0, 24000)); }

    public bool Afterburner { get => _afterburner; set => Set(ref _afterburner, value); }

    /// <summary>default, implicit, explicit_sbr, explicit_hierarchical.</summary>
    public string Sinyal { get => _sinyal; set => Set(ref _sinyal, value); }

    public int? DrcProfili { get => _drcProfili; set => Set(ref _drcProfili, value); }

    public int? SikistirmaProfili { get => _sikistirmaProfili; set => Set(ref _sikistirmaProfili, value); }

    public int? DrcHedef { get => _drcHedef; set => Set(ref _drcHedef, value); }

    public int? SikistirmaHedef { get => _sikistirmaHedef; set => Set(ref _sikistirmaHedef, value); }

    public int? ProgramSeviyesi { get => _programSeviyesi; set => Set(ref _programSeviyesi, value); }

    [JsonIgnore]
    public override string Ozet =>
        "FDK AAC · " + ProfilAdi(Profil) + " · " + (Mod == FdkModu.Cbr ? $"CBR {BitHizi} kb/s" : "VBR " + ((int)Mod));

    public static string ProfilAdi(string p) => p switch
    {
        "aac_low" => "LC",
        "aac_he" => "HE-AAC",
        "aac_he_v2" => "HE-AAC v2",
        "aac_ld" => "LD",
        "aac_eld" => "ELD",
        _ => p,
    };
}

// ------------------------------------------------------------------------------- Anahtar kare

public sealed class AnahtarKareAyarlari : Gozlenebilir
{
    private AralikBirimi _birim = AralikBirimi.Kare;
    private double? _enBuyuk = 50;
    private double? _enKucuk = 26;
    private int? _sahneDegisimi = 0;
    private bool _acikGop;
    private bool _intraYenileme;

    public AralikBirimi Birim { get => _birim; set => Set(ref _birim, value); }

    /// <summary>İki anahtar kare arası en fazla (kare ya da saniye); null: kodlayıcı varsayılanı.</summary>
    public double? EnBuyuk { get => _enBuyuk; set => Set(ref _enBuyuk, value); }

    /// <summary>En az (kare ya da saniye); null: otomatik.</summary>
    public double? EnKucuk { get => _enKucuk; set => Set(ref _enKucuk, value); }

    /// <summary>Sahne değişiminde anahtar kare eşiği (x264 scenecut); 0: kapalı, null: varsayılan (40).</summary>
    public int? SahneDegisimi { get => _sahneDegisimi; set => Set(ref _sahneDegisimi, value); }

    public bool AcikGop { get => _acikGop; set => Set(ref _acikGop, value); }

    public bool IntraYenileme { get => _intraYenileme; set => Set(ref _intraYenileme, value); }

    [JsonIgnore]
    public string AralikOzeti
    {
        get
        {
            var b = Birim == AralikBirimi.Kare ? "kare" : "sn";
            var buyuk = EnBuyuk is { } x ? $"en fazla {Bicim.Sayi(x)} {b}" : "en fazla varsayılan";
            var kucuk = EnKucuk is { } y ? $"en az {Bicim.Sayi(y)} {b}" : "en az otomatik";
            return $"{buyuk} · {kucuk}";
        }
    }

    [JsonIgnore]
    public string SahneOzeti => SahneDegisimi switch
    {
        null => "Varsayılan (40)",
        0 => "Kapalı",
        { } v => "Eşik " + v,
    };

    [JsonIgnore]
    public override string Ozet => AralikOzeti;

    [JsonIgnore]
    public string DigerOzeti => (AcikGop ? "Açık GOP" : "Kapalı GOP") + (IntraYenileme ? " · intra yenileme" : string.Empty);

    protected override IEnumerable<string> EkBildirimler(string? name) =>
        [nameof(AralikOzeti), nameof(SahneOzeti), nameof(DigerOzeti)];
}

// ------------------------------------------------------------------------------- Biçim

/// <summary>Türkçe sayı / birim biçimleri (arayüz) ve komut satırı için değişmez biçim.</summary>
public static class Bicim
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string Sayi(double v) => v.ToString("0.##", Tr);

    public static string BitHizi(int kbps) => kbps >= 1000 && kbps % 100 == 0
        ? (kbps / 1000.0).ToString("0.#", Tr) + " Mb/s"
        : kbps.ToString(Tr) + " kb/s";

    public static string Khz(int hz) => (hz / 1000.0).ToString("0.###", Tr) + " kHz";

    public static string ProfilSeviye(string profil, string seviye)
    {
        var p = string.IsNullOrEmpty(profil) ? "Otomatik profil" : ProfilAdi(profil);
        return string.IsNullOrEmpty(seviye) ? p + " (seviye otomatik)" : $"{p}@L{seviye}";
    }

    public static string ProfilAdi(string p) => p switch
    {
        "baseline" => "Baseline",
        "main" => "Main",
        "high" => "High",
        "high10" => "High 10",
        "high422" => "High 4:2:2",
        "high444" => "High 4:4:4",
        "high444p" => "High 4:4:4",
        _ => p,
    };

    /// <summary>"30000/1001" → "29,97".</summary>
    public static string KareHiziMetni(string kesir) =>
        KesirCoz(kesir) is { } f ? f.ToString("0.###", Tr) : kesir;

    public static double? KesirCoz(string? kesir)
    {
        if (string.IsNullOrWhiteSpace(kesir))
        {
            return null;
        }

        var parts = kesir.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
            && a > 0 && b > 0)
        {
            return a / b;
        }

        return double.TryParse(kesir, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    }
}
