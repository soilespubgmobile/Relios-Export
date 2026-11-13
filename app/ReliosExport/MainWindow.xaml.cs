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

    private static readonly string DefaultOutputFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Relios Export");

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
            : _ffmpegOk && _files.Any(f => f.State != ItemState.Done);
    }

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

    /// <summary>Üst çubuktaki kısa özet: seçili profilin ayarlarından.</summary>
    private void EtiketleriYaz() => ProfileTags.ItemsSource = Etiketler(_ayarlar);

    private static List<string> Etiketler(ProfilAyarlari a)
    {
        var v = a.Video;
        var list = new List<string>
        {
            v.Cozunurluk == CozunurlukModu.Kaynak ? "Kaynak çözünürlük" : $"{v.Genislik}×{v.Yukseklik}",
            v.KareHiziModu == KareHiziModu.Kaynak ? "Kaynak kare hızı" : Bicim.KareHiziMetni(v.KareHizi) + " fps",
        };

        if (v.Kodlayici == VideoKodlayici.X264)
        {
            list.Add("x264 · " + v.X264.ModOzeti);
            list.Add(Bicim.ProfilSeviye(v.X264.Profil, v.X264.Seviye));
        }
        else
        {
            list.Add("NVENC · " + v.Nvenc.ModOzeti);
            list.Add(Bicim.ProfilSeviye(v.Nvenc.Profil, v.Nvenc.Seviye));
        }

        var k = a.AnahtarKare;
        if (k.EnBuyuk is { } enBuyuk)
        {
            var birim = k.Birim == AralikBirimi.Kare ? string.Empty : " sn";
            list.Add("GOP " + Bicim.Sayi(enBuyuk) + (k.EnKucuk is { } enKucuk ? " / " + Bicim.Sayi(enKucuk) : string.Empty) + birim);
        }

        var s = a.Ses;
        if (s.Kaynak == SesKaynagi.Sessiz)
        {
            list.Add("Sessiz");
        }
        else
        {
            list.Add(s.Kodlayici == SesKodlayici.FdkAac ? s.Fdk.Ozet : "WAV · " + SesAyarlari.WavMetni(s.WavBit));
            list.Add(s.OrnekHizi is { } hz ? Bicim.Khz(hz) : "Kaynak örnekleme");
            if (s.Kaynak == SesKaynagi.AyniAdliDosyaOncelikli)
            {
                list.Add("Aynı adlı WAV");
            }
        }

        list.Add(ArgsBuilder.Uzanti(a) == ".mov" ? "MOV" : "MP4");
        return list;
    }

    private void OnVarsayilan(object sender, RoutedEventArgs e)
    {
        var p = SelectedProfile;
        var not = p.Id == ExportProfileId.TikTok
            ? " TikTok'un fabrika ayarı XviD4PSP'deki hedefle aynıdır (1216×2160, 29,97 fps, CRF 18)."
            : " YouTube'un fabrika ayarı XviD4PSP ayarlarının aynısıdır; yalnızca çözünürlük ve kare hızı kaynakla aynı kalır.";
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
            _settings.Profiller[SelectedProfile.Id] = yeni;
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
        switch (_ayarlar.Ses.Kaynak)
        {
            case SesKaynagi.Sessiz:
                item.Status = "Bekliyor · Ses yok (sessiz)";
                item.Detail = null;
                break;
            case SesKaynagi.AyniAdliDosyaOncelikli when item.HariciSes is { } ses:
                item.Status = "Bekliyor · Ses: " + Path.GetFileName(ses);
                item.Detail = "Videonun kendi sesi yerine kullanılacak ses:\n" + ses;
                break;
            default:
                item.Status = "Bekliyor";
                item.Detail = null;
                break;
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
                // Günlük, .txt dosyalarını açan programla (Not Defteri) açılır.
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
        var queue = _files.Where(f => f.State != ItemState.Done).ToList();
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
                    var output = await Exporter.ExportAsync(
                        item.Path,
                        _outputFolder,
                        profile,
                        ayarlar,
                        progress,
                        info => item.Status = "Kodlanıyor · " + info,
                        token);
                    item.OutputPath = output;
                    item.Progress = 1;
                    item.State = ItemState.Done;
                    item.Status = "Hazır · " + Path.GetFileName(output)
                                  + (item.Gecen is { } sure ? " · " + FileItem.SureMetni(sure) : string.Empty);
                    item.Detail = output;
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

    private void ShowOverall(int index, int count, double current, TimeSpan? kalan)
    {
        var overall = count == 0 ? 0 : Math.Clamp((index + current) / count, 0, 1);
        OverallBar.Value = overall;
        Taskbar.ProgressValue = overall;
        if (_cts is { IsCancellationRequested: false })
        {
            var pct = ((int)Math.Floor(current * 100)).ToString(CultureInfo.InvariantCulture);
            StatusText.Text = $"{index + 1}/{count} kodlanıyor · %{pct}" + (kalan is { } k ? " · bu video için kalan " + FileItem.SureMetni(k) : string.Empty);
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
        BtnStart.Content = busy ? "İptal" : "Başlat";
        BtnStart.Style = (Style)FindResource(busy ? "DangerButton" : "PrimaryButton");
        BtnAdd.IsEnabled = !busy;
        BtnFolder.IsEnabled = !busy;
        RbTikTok.IsEnabled = !busy;
        RbYouTube.IsEnabled = !busy;
        BtnClear.IsEnabled = !busy && _files.Count > 0;
        OverallBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        OverallBar.Value = 0;
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
        var bekleyen = new FileItem(Path.Combine(klasor, "edit1.mp4")) { HariciSes = Path.Combine(klasor, "edit1.wav") };
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
            Status = "Hazır · klip_TikTok.mp4",
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

    /// <summary>Yalnızca pencere kontrolü için: panelin bütün satırları açılır (tam görüntü).</summary>
    internal void KontrolIcinTumSatirlariAc()
    {
        foreach (var exp in AyarPaneli.Children.OfType<Expander>())
        {
            exp.IsExpanded = true;
        }
    }

    internal ProfilAyarlari KontrolAyarlari => _ayarlar;

    internal AppSettings KontrolAyarDosyasi => _settings;
}
