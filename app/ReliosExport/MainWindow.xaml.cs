using System.Runtime.InteropServices;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using ReliosExport.Arayuz;
using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport;

/// <summary>Listedeki bir videonun durumu.</summary>
public enum ItemState
{
    Waiting,
    Running,
    Done,
    Failed,
    Canceled,
}

/// <summary>Listedeki tek bir video.</summary>
public sealed class FileItem : INotifyPropertyChanged
{
    private ItemState _state = ItemState.Waiting;
    private string _status = "Bekliyor";
    private string? _detail;
    private double _progress;
    private string? _gunlukYolu;
    private DateTime? _baslangic;

    public FileItem(string path) => Path = path;

    /// <summary>Başarısız olduysa ffmpeg günlüğünün yazıldığı dosya.</summary>
    public string? GunlukYolu
    {
        get => _gunlukYolu;
        set
        {
            if (Set(ref _gunlukYolu, value))
            {
                OnChanged(nameof(GunlukGorunur));
            }
        }
    }

    public Visibility GunlukGorunur => _gunlukYolu is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Kodlamanın başladığı an (kalan süre ve toplam süre için).</summary>
    public void Basla() => _baslangic = DateTime.UtcNow;

    /// <summary>Başlangıçtan bu yana geçen süre.</summary>
    public TimeSpan? Gecen => _baslangic is { } b ? DateTime.UtcNow - b : null;

    /// <summary>Tahmini kalan süre (ilerleme hızından); başta güvenilir olmadığı için bir süre gösterilmez.</summary>
    public TimeSpan? Kalan
    {
        get
        {
            if (Gecen is not { } g || _progress < 0.02 || _progress >= 1 || g.TotalSeconds < 3)
            {
                return null;
            }

            return TimeSpan.FromSeconds(g.TotalSeconds * (1 - _progress) / _progress);
        }
    }

    /// <summary>"45 sn", "2 dk 05 sn", "1 sa 12 dk".</summary>
    public static string SureMetni(TimeSpan t)
    {
        var s = (int)Math.Ceiling(t.TotalSeconds);
        return s < 60 ? $"{s} sn" : s < 3600 ? $"{s / 60} dk {s % 60:00} sn" : $"{s / 3600} sa {s % 3600 / 60} dk";
    }

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Videonun yanındaki aynı adlı ses dosyası (yoksa null).</summary>
    public string? HariciSes { get; set; }

    /// <summary>Bittiğinde oluşan dosya.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Kaynağın ffprobe bilgisi (eklenince arka planda okunur; tahmini dosya boyutu için).</summary>
    public MediaInfo? Medya { get; set; }

    /// <summary>Aynı adlı ses dosyasının bilgisi (örnekleme hızı, kanal) ve hangi dosyaya ait olduğu.</summary>
    public SesAkisi? HariciAkis { get; set; }

    public string? HariciAkisYolu { get; set; }

    public ItemState State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    /// <summary>Durum satırının ipucu (hata ayrıntısı ya da kullanılacak ses dosyasının yolu).</summary>
    public string? Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (Set(ref _progress, value))
            {
                OnChanged(nameof(PercentText));
            }
        }
    }

    /// <summary>Türkçe yüzde yazımı ve kalan süre: "%45 · kalan 1 dk 10 sn".</summary>
    public string PercentText =>
        "%" + ((int)Math.Floor(Math.Clamp(_progress, 0, 1) * 100)).ToString(CultureInfo.InvariantCulture)
        + (Kalan is { } k ? " · kalan " + SureMetni(k) : string.Empty);

    public void Reset()
    {
        State = ItemState.Waiting;
        Status = "Bekliyor";
        Detail = null;
        Progress = 0;
        OutputPath = null;
        GunlukYolu = null;
        _baslangic = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnChanged(name!);
        return true;
    }
}

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public static readonly DependencyProperty IsDragOverProperty =
        DependencyProperty.Register(nameof(IsDragOver), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private static readonly string[] VideoExtensions =
        [".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".ts", ".mts", ".m2ts", ".flv", ".wmv"];

    /// <summary>Varsayılan kayıt yeri: kullanıcının İndirilenler klasörü (Windows'ta taşınmışsa yeni yeri).</summary>
    private static readonly string DefaultOutputFolder = IndirilenlerKlasoru();

    private static string IndirilenlerKlasoru()
    {
        try
        {
            // FOLDERID_Downloads
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var yolPtr) == 0)
            {
                try
                {
                    if (Marshal.PtrToStringUni(yolPtr) is { Length: > 0 } yol)
                    {
                        return yol;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(yolPtr);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Aşağıdaki yedek yol kullanılır.
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    private readonly ObservableCollection<FileItem> _files = [];
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _kayit;
    private readonly bool _ready;
    private ProfilAyarlari _ayarlar = null!;
    private CancellationTokenSource? _cts;
    private ExportProfileId _lastProfile;
    private string _outputFolder;
    private bool _ffmpegOk;
    private bool _closeWhenDone;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _outputFolder = string.IsNullOrWhiteSpace(_settings.OutputFolder) ? DefaultOutputFolder : _settings.OutputFolder;
        OutputFolderBox.Text = _outputFolder;
        if (string.Equals(_settings.Platform, nameof(ExportProfileId.YouTube), StringComparison.OrdinalIgnoreCase))
        {
            RbYouTube.IsChecked = true;
        }

        // Ayar değişikliği kısa bir beklemeyle kaydedilir (her tuşta diske yazılmasın).
        _kayit = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(600) };
        _kayit.Tick += (_, _) =>
        {
            _kayit.Stop();
            _settings.Save();
        };

        _lastProfile = SelectedProfile.Id;
        FileList.ItemsSource = _files;
        _files.CollectionChanged += (_, _) => OnListChanged();
        VersionText.Text = "Relios Export v" + AppVersion;
        Activated += (_, _) =>
        {
            if (!IsBusy)
            {
                CheckTools();
                HariciSesleriYenile();
            }
        };

        _ready = true;
        AyarlariBagla();
        CheckTools();
        OnListChanged();
    }

    /// <summary>Dışa aktarma sürüyor mu (satırlardaki "çıkar" düğmesi buna bakar).</summary>
    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        private set => SetValue(IsBusyProperty, value);
    }

    /// <summary>Pencerenin üstünde dosya sürükleniyor mu (bırakma alanı vurgusu).</summary>
    public bool IsDragOver
    {
        get => (bool)GetValue(IsDragOverProperty);
        private set => SetValue(IsDragOverProperty, value);
    }

    private static string AppVersion
    {
        get
        {
            var v = typeof(MainWindow).Assembly.GetName().Version;
            return v is null ? "0" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }

    private ExportProfile SelectedProfile => RbYouTube.IsChecked == true ? Profiles.YouTube : Profiles.TikTok;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.Apply(this);
    }

    // ---------------------------------------------------------------- durum

    private void CheckTools()
    {
        _ffmpegOk = Tools.Available;
        FfmpegBanner.Visibility = _ffmpegOk ? Visibility.Collapsed : Visibility.Visible;
        FfmpegBannerText.Text = $"ffmpeg.exe ve ffprobe.exe şu klasörde olmalı: {Tools.Folder}";
        UpdateStartState();
    }

    private void OnProfileChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        var id = SelectedProfile.Id;
        if (id != _lastProfile)
        {
            // Liste seçili platform için dışa aktarılır: önceki platformdaki sonuçlar sıfırlanır.
            foreach (var item in _files)
            {
                item.Reset();
            }

            _lastProfile = id;
        }

        AyarlariBagla();
        OnListChanged();
        _settings.Platform = id.ToString();
        _settings.Save();
    }

    private void OnListChanged()
    {
        var any = _files.Count > 0;
        DropZone.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        ListScroller.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = _files.Count.ToString(CultureInfo.InvariantCulture);
        BtnClear.IsEnabled = any && !IsBusy;
        if (!IsBusy)
        {
            StatusText.Text = IdleStatus();
        }

        UpdateStartState();
    }

    private string IdleStatus()
    {
        var waiting = _files.Count(f => f.State != ItemState.Done);
        var done = _files.Count - waiting;
        if (waiting > 0)
        {
            return $"{waiting} video sırada";
        }

        return done > 0 ? $"{done} video hazır" : string.Empty;
    }

    private void UpdateStartState()
    {
        if (!_ready)
        {
            return;
        }

        BtnStart.IsEnabled = IsBusy
            ? _cts is { IsCancellationRequested: false }
            : _ffmpegOk && _files.Count > 0;
        if (!IsBusy)
        {
            // Bütün videolar bittiyse düğme "Tekrar işle" olur: hepsi paneldeki o anki ayarlarla yeniden kodlanır.
            var tekrar = TekrarIslenecekMi;
            BtnStart.Content = tekrar ? "Tekrar işle" : "Başlat";
            BtnStart.ToolTip = tekrar
                ? "Bütün videoları şu anki ayarlarla yeniden işler. Önceki dosyalar silinmez; yenileri ayrı adla kaydedilir."
                : null;
        }
    }

    /// <summary>Listede video var ve hepsi bitmiş: Başlat düğmesi hepsini yeniden işler.</summary>
    private bool TekrarIslenecekMi => _files.Count > 0 && _files.All(f => f.State == ItemState.Done);

    // ---------------------------------------------------------------- ayarlar

    /// <summary>Seçili profilin ayarlarını panele bağlar.</summary>
    private void AyarlariBagla()
    {
        if (_ayarlar is not null)
        {
            _ayarlar.Degisti -= OnAyarDegisti;
        }

        _ayarlar = _settings.ProfilAyari(SelectedProfile.Id);
        _ayarlar.Degisti += OnAyarDegisti;
        AyarPaneli.DataContext = _ayarlar;
        ProfilAdiniYaz();
        EtiketleriYaz();
        BekleyenleriYaz();
    }

    private void OnAyarDegisti(object? sender, EventArgs e)
    {
        _kayit.Stop();
        _kayit.Start();
        EtiketleriYaz();
        BekleyenleriYaz();
    }

    private void KaydetHemen()
    {
        _kayit.Stop();
        _settings.Save();
    }

    private void ProfilAdiniYaz()
    {
        var ad = SelectedProfile.DisplayName;
        AyarProfilAdi.Text = IsBusy ? ad + " · değişiklikler sonraki başlatmada geçerli" : ad;
        AyarProfilAdi.ToolTip = IsBusy ? "Kodlanan videolar başlatıldıkları ayarlarla biter." : null;
    }

    /// <summary>Üst çubuktaki kısa özet: seçili profilin ayarlarından (ayrıntısı ipucunda).</summary>
    private void EtiketleriYaz() => ProfileTags.ItemsSource = Etiketler(_ayarlar);

    /// <summary>Üst çubuktaki bir özet: başlık, kısa değer ve üstüne gelince görünen ayrıntı.</summary>
    public sealed record OzetParcasi(string Baslik, string Deger, string Ayrinti);

    private static List<OzetParcasi> Etiketler(ProfilAyarlari a)
    {
        var v = a.Video;
        var boyut = v.Cozunurluk == CozunurlukModu.Kaynak ? "Kaynak boyut" : $"{v.Genislik}×{v.Yukseklik}";
        var fps = v.KareHiziModu == KareHiziModu.Kaynak ? "kaynak fps" : Bicim.KareHiziMetni(v.KareHizi) + " fps";
        var video = v.Cozunurluk == CozunurlukModu.Kaynak && v.KareHiziModu == KareHiziModu.Kaynak
            ? "Kaynakla aynı"
            : $"{boyut} · {fps}";

        var kodlayici = v.Kodlayici == VideoKodlayici.X264 ? "x264 · " + v.X264.ModOzeti : "NVENC · " + v.Nvenc.ModOzeti;

        var s = a.Ses;
        string ses;
        string sesAyrinti;
        if (s.Kaynak == SesKaynagi.Sessiz)
        {
            ses = "Sessiz";
            sesAyrinti = "Çıktıda ses yok.";
        }
        else
        {
            var hz = s.OrnekHizi is { } h ? Bicim.Khz(h) : "kaynak kHz";
            ses = s.Kodlayici == SesKodlayici.FdkAac
                ? (s.Fdk.Profil == "aac_low" ? "AAC" : FdkAyarlari.ProfilAdi(s.Fdk.Profil)) + " · "
                  + (s.Fdk.Mod == FdkModu.Cbr ? s.Fdk.BitHizi + " kb/s" : "VBR " + (int)s.Fdk.Mod) + " · " + hz
                : "WAV " + SesAyarlari.WavMetni(s.WavBit) + " · " + hz;
            sesAyrinti = $"{s.KaynakOzeti}\n{s.KodlayiciOzeti}\n{s.OrnekHiziOzeti}";
        }

        return
        [
            new("VİDEO", video, $"Boyut: {v.CozunurlukOzeti}\nKare hızı: {v.KareHiziOzeti}"),
            new("KODLAYICI", kodlayici, $"{v.KodlayiciOzeti}\nAnahtar kare: {a.AnahtarKare.AralikOzeti}"),
            new("SES", ses, sesAyrinti),
            new("DOSYA", a.Uzanti == ".mov" ? "MOV" : "MP4", a.KapOzeti),
        ];
    }

    private void OnVarsayilan(object sender, RoutedEventArgs e)
    {
        var p = SelectedProfile;
        var not = p.Id == ExportProfileId.TikTok
            ? " TikTok: ekran kartıyla (NVENC) CBR 34375 kb/s; boyut, kare hızı, profil ve seviye kaynakla aynı; ses WAV 16 bit 48 kHz (en yüksek dönüşüm kalitesi, üçgen titreşim); MOV."
            : " YouTube: x264 CRF 18, en yüksek bit hızı 34375 kb/s, tampon 55000 kb; çözünürlük, kare hızı, profil ve seviye kaynakla aynı; anahtar kare 30 fps'e göre 50 / 26; ses WAV 16 bit 48 kHz (en yüksek dönüşüm kalitesi, üçgen titreşim); MOV.";
        if (!ConfirmDialog.Ask(
                this,
                $"{p.DisplayName} ayarları varsayılana dönsün mü?",
                "Video, ses ve anahtar kare ayarlarının hepsi fabrika ayarına döner." + not,
                "Varsayılana dön",
                "Vazgeç"))
        {
            return;
        }

        _settings.Profiller[p.Id] = Varsayilanlar.For(p.Id);
        AyarlariBagla();
        KaydetHemen();
    }

    private void OnOnAyarlar(object sender, RoutedEventArgs e)
    {
        GirisKutulari.Uygula(this);
        var dlg = new OnAyarPenceresi(_settings, SelectedProfile, _ayarlar) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Yuklenen is { } yeni)
        {
            _settings.OnAyariYukle(SelectedProfile.Id, yeni);
            AyarlariBagla();
            KaydetHemen();
        }
    }

    private void OnX264Ayrintili(object sender, RoutedEventArgs e)
    {
        GirisKutulari.Uygula(this);
        var dlg = new X264Penceresi(_ayarlar) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _ayarlar.Video.X264 = dlg.Sonuc;
        }
    }

    private void OnNvencAyrintili(object sender, RoutedEventArgs e)
    {
        GirisKutulari.Uygula(this);
        var dlg = new NvencPenceresi(_ayarlar) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _ayarlar.Video.Nvenc = dlg.Sonuc;
        }
    }

    private void OnFdkAyrintili(object sender, RoutedEventArgs e)
    {
        GirisKutulari.Uygula(this);
        var dlg = new FdkPenceresi(_ayarlar) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _ayarlar.Ses.Fdk = dlg.Sonuc;
        }
    }

    // ---------------------------------------------------------------- aynı adlı ses

    /// <summary>Bekleyen videoların durum satırı: hangi sesin kullanılacağı.</summary>
    private void BekleyenleriYaz()
    {
        foreach (var item in _files)
        {
            if (item.State == ItemState.Waiting)
            {
                BekliyorYaz(item);
            }
        }
    }

    private void BekliyorYaz(FileItem item)
    {
        var tahmin = TahminMetni(item, _ayarlar) is { } t ? " · " + t : string.Empty;
        switch (_ayarlar.Ses.Kaynak)
        {
            case SesKaynagi.Sessiz:
                item.Status = "Bekliyor · Ses yok (sessiz)" + tahmin;
                item.Detail = null;
                break;
            case SesKaynagi.AyniAdliDosyaOncelikli when item.HariciSes is { } ses:
                item.Status = "Bekliyor · Ses: " + Path.GetFileName(ses) + tahmin;
                item.Detail = "Videonun kendi sesi yerine kullanılacak ses:\n" + ses;
                break;
            default:
                item.Status = "Bekliyor" + tahmin;
                item.Detail = null;
                break;
        }

        if (tahmin.Length > 0)
        {
            item.Detail = (item.Detail is null ? string.Empty : item.Detail + "\n\n")
                          + "Tahmini dosya boyutu: (video + ses bit hızı) × süre. "
                          + "\"En çok\": bit hızının yalnızca üst sınırı belli (ör. CRF + en yüksek bit hızı); gerçek dosya genelde daha küçüktür.";
        }
    }

    /// <summary>Bekleyen videonun tahmini çıktı boyutu ("tahmini 245 MB" / "en çok 1,2 GB"); bilinmiyorsa null.</summary>
    private static string? TahminMetni(FileItem item, ProfilAyarlari a)
    {
        if (item.Medya is not { } m)
        {
            return null;
        }

        var ses = a.Ses.Kaynak == SesKaynagi.AyniAdliDosyaOncelikli && item.HariciSes is { } dosya
            ? new SesSecimi(SesSecimTuru.HariciDosya, dosya, AyniYol(item.HariciAkisYolu, dosya) ? item.HariciAkis?.Kanal : null)
            {
                OrnekHizi = AyniYol(item.HariciAkisYolu, dosya) ? item.HariciAkis?.OrnekHizi : null,
            }
            : ArgsBuilder.SesSec(a.Ses, m, item.Path);
        return BoyutTahmini.Hesapla(a, m, ses)?.Metin;
    }

    // Aynı anda en fazla iki ffprobe (çok video birden eklenince bilgisayar yorulmasın).
    private readonly SemaphoreSlim _inceleme = new(2);

    /// <summary>Videoyu (ve aynı adlı sesi) arka planda inceler; bitince bekleyen satırın tahmini boyutu yazılır.</summary>
    private async Task InceleAsync(FileItem item)
    {
        if (!Tools.Available)
        {
            return;
        }

        await _inceleme.WaitAsync();
        try
        {
            item.Medya ??= await MediaProbe.ProbeAsync(Tools.Ffprobe, item.Path, CancellationToken.None);
            if (item.HariciSes is { } dosya && !AyniYol(item.HariciAkisYolu, dosya))
            {
                item.HariciAkis = await MediaProbe.ProbeAudioAsync(Tools.Ffprobe, dosya, CancellationToken.None);
                item.HariciAkisYolu = dosya;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Okunamayan dosya: tahmin yazılmaz; kodlarken ffmpeg asıl hatayı söyler.
        }
        finally
        {
            _inceleme.Release();
        }

        if (item.State == ItemState.Waiting && _files.Contains(item))
        {
            BekliyorYaz(item);
        }
    }

    /// <summary>Program dışında ses dosyası eklenmiş / silinmiş olabilir: bekleyenler yeniden aranır.</summary>
    private void HariciSesleriYenile()
    {
        foreach (var item in _files)
        {
            if (item.State == ItemState.Waiting)
            {
                item.HariciSes = HariciSes.Bul(item.Path);
                BekliyorYaz(item);
                if (item.HariciSes is { } dosya && !AyniYol(item.HariciAkisYolu, dosya))
                {
                    _ = InceleAsync(item);
                }
            }
        }
    }

    // ---------------------------------------------------------------- dosya ekleme

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var ok = !IsBusy && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        IsDragOver = ok;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => IsDragOver = false;

    private void OnDrop(object sender, DragEventArgs e)
    {
        IsDragOver = false;
        if (IsBusy || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        AddPaths(paths);
        Activate();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control && !IsBusy)
        {
            OnAddFiles(sender, e);
            e.Handled = true;
        }
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
        {
            return;
        }

        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Video seç",
            Filter = "Videolar|" + string.Join(';', VideoExtensions.Select(x => "*" + x)) + "|Tüm dosyalar|*.*",
        };
        if (dlg.ShowDialog(this) == true)
        {
            AddPaths(dlg.FileNames);
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var skipped = 0;
        var sesler = new List<string>();
        foreach (var path in Expand(paths))
        {
            // Ses dosyası listeye eklenmez: aynı adlı videoyla birlikte otomatik kullanılır.
            if (HariciSes.SesDosyasiMi(path))
            {
                sesler.Add(path);
                continue;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!VideoExtensions.Contains(ext))
            {
                skipped++;
                continue;
            }

            if (_files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var item = new FileItem(path) { HariciSes = HariciSes.Bul(path) };
            BekliyorYaz(item);
            _files.Add(item);
            _ = InceleAsync(item);
        }

        var eslesmeyen = sesler.Count(s => !_files.Any(f => AyniYol(f.HariciSes, s)));
        var notlar = new List<string>();
        if (skipped > 0)
        {
            notlar.Add($"{skipped} dosya video olmadığı için eklenmedi");
        }

        if (eslesmeyen > 0)
        {
            notlar.Add($"{eslesmeyen} ses dosyasının aynı adlı videosu listede yok");
        }

        if (notlar.Count > 0)
        {
            StatusText.Text = string.Join(" · ", notlar);
        }
    }

    private static bool AyniYol(string? a, string b)
    {
        if (a is null)
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static IEnumerable<string> Expand(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                IEnumerable<string> inner;
                try
                {
                    inner = Directory.GetFiles(path).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var f in inner)
                {
                    yield return f;
                }
            }
            else if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private void OnRemoveItem(object sender, RoutedEventArgs e)
    {
        if (!IsBusy && (sender as FrameworkElement)?.DataContext is FileItem item)
        {
            _files.Remove(item);
        }
    }

    private void OnShowItem(object sender, RoutedEventArgs e)
    {
        switch ((sender as FrameworkElement)?.DataContext)
        {
            case FileItem { State: ItemState.Failed, GunlukYolu: { } gunluk } when File.Exists(gunluk):
                // Günlük, .txt dosyalarını açan programla açılır.
                OpenExplorer($"\"{gunluk}\"");
                break;
            case FileItem { OutputPath: { } output } when File.Exists(output):
                OpenExplorer($"/select,\"{output}\"");
                break;
        }
    }


    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (!IsBusy)
        {
            _files.Clear();
        }
    }

    // ---------------------------------------------------------------- kayıt yeri

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Kayıt yerini seç",
            InitialDirectory = Directory.Exists(_outputFolder) ? _outputFolder : string.Empty,
        };
        if (dlg.ShowDialog(this) == true)
        {
            _outputFolder = dlg.FolderName;
            OutputFolderBox.Text = _outputFolder;
            _settings.OutputFolder = _outputFolder;
            KaydetHemen();
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_outputFolder);
            OpenExplorer($"\"{_outputFolder}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Kayıt klasörü açılamadı: " + ex.Message;
        }
    }

    private static void OpenExplorer(string arguments)
    {
        // Yalnızca Windows Gezgini açılır; ağ ya da dış program yok.
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = false })?.Dispose();
    }

    // ---------------------------------------------------------------- dışa aktarma

    private async void OnStartOrCancel(object sender, RoutedEventArgs e)
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            StatusText.Text = "Durduruluyor…";
            UpdateStartState();
            return;
        }

        CheckTools();
        GirisKutulari.Uygula(this);
        var profile = SelectedProfile;
        // Bekleyen / hatalı / iptal edilen varsa yalnızca onlar; hepsi bittiyse (Tekrar işle) hepsi yeniden.
        var queue = TekrarIslenecekMi ? _files.ToList() : _files.Where(f => f.State != ItemState.Done).ToList();
        if (!_ffmpegOk || queue.Count == 0)
        {
            return;
        }

        // Kuyruktaki her video aynı ayarla kodlanır; kodlama sürerken paneldeki değişiklik sonraki başlatmaya kalır.
        var ayarlar = _ayarlar.Kopya();
        KaydetHemen();
        foreach (var item in queue)
        {
            item.Reset();
            item.HariciSes = HariciSes.Bul(item.Path);
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        var ok = 0;
        var failed = 0;
        var canceled = false;
        try
        {
            for (var i = 0; i < queue.Count; i++)
            {
                if (token.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                var item = queue[i];
                var index = i;
                item.State = ItemState.Running;
                item.Status = "Hazırlanıyor…";
                item.Basla();
                ShowOverall(index, queue.Count, 0, null);
                var progress = new Progress<double>(v =>
                {
                    item.Progress = v;
                    ShowOverall(index, queue.Count, v, item.Kalan);
                });

                try
                {
                    EsitlemeRaporu? esitleme = null;
                    var output = await Exporter.ExportAsync(
                        item.Path,
                        _outputFolder,
                        profile,
                        ayarlar,
                        progress,
                        info => item.Status = "Kodlanıyor · " + info,
                        token,
                        r => esitleme = r);
                    item.OutputPath = output;
                    item.Progress = 1;
                    item.State = ItemState.Done;
                    // LUFS eşitleme açıksa sonucu da yazılır: "-14,0 LUFS", "-16,0 LUFS (hedefe ulaşılamadı)", "-9,2 LUFS (dokunulmadı)".
                    item.Status = "Hazır · " + Path.GetFileName(output)
                                  + (item.Gecen is { } sure ? " · " + FileItem.SureMetni(sure) : string.Empty)
                                  + DosyaBoyutu(output)
                                  + (esitleme?.SatirMetni is { } sesSonucu ? " · " + sesSonucu : string.Empty);
                    item.Detail = output + (esitleme is not null ? "\n\n" + esitleme.Ayrinti : string.Empty);
                    ok++;
                }
                catch (OperationCanceledException)
                {
                    item.State = ItemState.Canceled;
                    item.Status = "İptal edildi";
                    item.Progress = 0;
                    canceled = true;
                    break;
                }
                catch (Exception ex)
                {
                    item.State = ItemState.Failed;
                    item.Status = "Hata: " + FirstLine(ex.Message);
                    item.GunlukYolu = (ex as FfmpegHatasi)?.GunlukDosyasi;
                    item.Detail = ex.Message + (item.GunlukYolu is { } g ? "\n\nAyrıntılı günlük: " + g : string.Empty);
                    item.Progress = 0;
                    failed++;
                }
            }
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }

        StatusText.Text = Summary(ok, failed, canceled);
        if (_closeWhenDone)
        {
            Close();
        }
    }

    /// <summary>Oluşan dosyanın gerçek boyutu (" · 245 MB"); okunamazsa boş.</summary>
    private static string DosyaBoyutu(string yol)
    {
        try
        {
            return " · " + BoyutTahmini.Yaz(new FileInfo(yol).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private void ShowOverall(int index, int count, double current, TimeSpan? kalan)
    {
        var overall = count == 0 ? 0 : Math.Clamp((index + current) / count, 0, 1);
        Taskbar.ProgressValue = overall;
        // İlerleme yalnızca videonun satırında (çizgi, yüzde, kalan süre); pencerede ikinci bir çizgi yok.
        if (_cts is { IsCancellationRequested: false })
        {
            StatusText.Text = string.Empty;
        }
    }

    private static string Summary(int ok, int failed, bool canceled)
    {
        var parts = new List<string>();
        if (canceled)
        {
            parts.Add("İptal edildi");
        }

        if (ok > 0)
        {
            parts.Add($"{ok} video hazır");
        }

        if (failed > 0)
        {
            parts.Add(ok > 0 ? $"{failed} hatalı" : $"{failed} video hatalı");
        }

        return string.Join(" · ", parts);
    }

    private static string FirstLine(string message)
    {
        var line = message.Split('\n', 2)[0].Trim();
        return line.Length > 160 ? line[..160] + "…" : line;
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        BtnStart.Content = busy ? "İptal" : TekrarIslenecekMi ? "Tekrar işle" : "Başlat";
        if (busy)
        {
            BtnStart.ToolTip = null;
        }
        BtnStart.Style = (Style)FindResource(busy ? "DangerButton" : "PrimaryButton");
        BtnAdd.IsEnabled = !busy;
        BtnFolder.IsEnabled = !busy;
        RbTikTok.IsEnabled = !busy;
        RbYouTube.IsEnabled = !busy;
        BtnClear.IsEnabled = !busy && _files.Count > 0;
        Taskbar.ProgressValue = 0;
        Taskbar.ProgressState = busy ? TaskbarItemProgressState.Normal : TaskbarItemProgressState.None;
        ProfilAdiniYaz();
        if (!busy)
        {
            BekleyenleriYaz();
        }

        UpdateStartState();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!IsBusy)
        {
            GirisKutulari.Uygula(this);
            KaydetHemen();
            return;
        }

        // Kodlama sürerken pencere hemen kapanmaz: önce ffmpeg durdurulur, yarım dosya silinir.
        e.Cancel = true;
        if (_closeWhenDone)
        {
            return;
        }

        if (ConfirmDialog.Ask(
                this,
                "Dışa aktarma sürüyor",
                "Durdurup kapatılsın mı? Yarım kalan dosya silinir.",
                "Durdur ve kapat",
                "Devam et"))
        {
            _closeWhenDone = true;
            _cts?.Cancel();
        }
    }

    // ---------------------------------------------------------------- derleme kontrolü

    /// <summary>
    /// Yalnızca GitHub'daki pencere kontrolü için: listeye örnek satırlar konur ve ayar satırları açılır,
    /// böylece ekran görüntüsünde arayüzün dolu hâli görünür. Dosyalar diskte yoktur, kodlanmaz.
    /// </summary>
    internal void KontrolIcinDoldur()
    {
        var klasor = Path.Combine(Path.GetTempPath(), "Relios Export örnek");
        var bekleyen = new FileItem(Path.Combine(klasor, "edit1.mp4"))
        {
            HariciSes = Path.Combine(klasor, "edit1.wav"),
            // 60 saniyelik 1080×1920 video: tahmini boyut satırda görünsün (TikTok CBR 34375 kb/s + WAV 16 bit 48 kHz).
            Medya = new MediaInfo(1080, 1920, 1, 1, 0, 60, true, 30, "bt709", "bt709", "tv") { VideoCodec = "h264", VideoProfil = "High", VideoSeviye = 41 },
        };
        BekliyorYaz(bekleyen);
        var calisan = new FileItem(Path.Combine(klasor, "Türkçe ğüşıöç İsim.mov"))
        {
            State = ItemState.Running,
            Status = "Kodlanıyor · 1920×1080 · 60 fps · kırpılıyor · Ses: Türkçe ğüşıöç İsim.wav",
            Progress = 0.42,
        };
        var biten = new FileItem(Path.Combine(klasor, "klip.mp4"))
        {
            State = ItemState.Done,
            Status = "Hazır · klip_TikTok.mp4 · 1 dk 05 sn · 128 MB · -14,0 LUFS",
            Progress = 1,
        };
        var hatali = new FileItem(Path.Combine(klasor, "bozuk.mkv"))
        {
            State = ItemState.Failed,
            Status = "Hata: ffmpeg çalışmadı (kod 1)",
        };
        _files.Add(bekleyen);
        _files.Add(calisan);
        _files.Add(biten);
        _files.Add(hatali);

        foreach (var exp in AyarPaneli.Children.OfType<Expander>().Take(3))
        {
            exp.IsExpanded = true;
        }
    }

    /// <summary>
    /// Yalnızca pencere kontrolü için: listede yalnızca bitmiş videolar kalır; Başlat düğmesi "Tekrar işle"
    /// olmalı. Düğmenin yazısını döndürür.
    /// </summary>
    internal string KontrolIcinHepsiBitti()
    {
        foreach (var item in _files.Where(f => f.State != ItemState.Done).ToList())
        {
            _files.Remove(item);
        }

        if (_files.Count == 0)
        {
            _files.Add(new FileItem(Path.Combine(Path.GetTempPath(), "Relios Export örnek", "klip.mp4")));
        }

        foreach (var item in _files)
        {
            item.State = ItemState.Done;
            item.Status = "Hazır · " + Path.GetFileNameWithoutExtension(item.Path) + "_TikTok.mov";
            item.Progress = 1;
        }

        OnListChanged();
        return BtnStart.Content as string ?? string.Empty;
    }

    /// <summary>Yalnızca pencere kontrolü için: panelin bütün satırları açılır (tam görüntü).</summary>
    internal void KontrolIcinTumSatirlariAc()
    {
        foreach (var exp in AyarPaneli.Children.OfType<Expander>())
        {
            exp.IsExpanded = true;
        }
    }

    /// <summary>Yalnızca pencere kontrolü için: ilk satırın durum yazısı (tahmini boyut görünüyor mu).</summary>
    internal string KontrolIlkSatir => _files.Count > 0 ? _files[0].Status : string.Empty;

    internal ProfilAyarlari KontrolAyarlari => _ayarlar;

    internal AppSettings KontrolAyarDosyasi => _settings;

    // ---------------------------------------------------------------- uçtan uca deneme (yalnızca GitHub, --uctan-uca)

    /// <summary>Kayıt yeri (ayar dosyasına yazılmaz).</summary>
    internal void KontrolCiktiKlasoru(string yol)
    {
        _outputFolder = yol;
        OutputFolderBox.Text = yol;
    }

    /// <summary>Dosya ekleme: sürükle-bırak ve "Video ekle" ile aynı yol.</summary>
    internal void KontrolEkle(params string[] yollar) => AddPaths(yollar);

    internal IReadOnlyList<FileItem> KontrolSatirlar => _files;

    internal bool KontrolMesgul => IsBusy;

    internal string KontrolDurum => StatusText.Text;

    /// <summary>Satırdaki "çıkar" düğmesi.</summary>
    internal void KontrolCikar(FileItem item) => OnRemoveItem(new FrameworkElement { DataContext = item }, new RoutedEventArgs());

    /// <summary>Bir düğmeye tıklama (yalnızca düğme etkinse; değilse false).</summary>
    internal static bool KontrolTikla(System.Windows.Controls.Primitives.ButtonBase dugme)
    {
        if (!dugme.IsEnabled)
        {
            return false;
        }

        dugme.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        return true;
    }
}
