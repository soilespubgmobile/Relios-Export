using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using Microsoft.Win32;
using ReliosExport.Core;

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

    public FileItem(string path) => Path = path;

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

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

    /// <summary>Hata ayrıntısı (durum satırının ipucunda görünür).</summary>
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

    /// <summary>Türkçe yüzde yazımı: %45.</summary>
    public string PercentText =>
        "%" + ((int)Math.Floor(Math.Clamp(_progress, 0, 1) * 100)).ToString(CultureInfo.InvariantCulture);

    public void Reset()
    {
        State = ItemState.Waiting;
        Status = "Bekliyor";
        Detail = null;
        Progress = 0;
        OutputPath = null;
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
    private readonly bool _ready;
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

        _lastProfile = SelectedProfile.Id;
        FileList.ItemsSource = _files;
        _files.CollectionChanged += (_, _) => OnListChanged();
        VersionText.Text = "Relios Export v" + AppVersion;
        Activated += (_, _) =>
        {
            if (!IsBusy)
            {
                CheckTools();
            }
        };

        _ready = true;
        CheckTools();
        UpdateProfileInfo();
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

        UpdateProfileInfo();
        OnListChanged();
        _settings.Platform = id.ToString();
        _settings.Save();
    }

    private void UpdateProfileInfo()
    {
        var p = SelectedProfile;
        ProfileTags.ItemsSource = p.Tags;
        ProfileTags.Visibility = p.IsReady ? Visibility.Visible : Visibility.Collapsed;
        ProfileNote.Visibility = p.IsReady ? Visibility.Collapsed : Visibility.Visible;
        ProfileNoteText.Text = p.Description;
        UpdateStartState();
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
            : _ffmpegOk && SelectedProfile.IsReady && _files.Any(f => f.State != ItemState.Done);
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
        foreach (var path in Expand(paths))
        {
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

            _files.Add(new FileItem(path));
        }

        if (skipped > 0)
        {
            StatusText.Text = $"{skipped} dosya video olmadığı için eklenmedi";
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
        if ((sender as FrameworkElement)?.DataContext is FileItem { OutputPath: { } output } && File.Exists(output))
        {
            OpenExplorer($"/select,\"{output}\"");
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
            _settings.Save();
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
        var profile = SelectedProfile;
        var queue = _files.Where(f => f.State != ItemState.Done).ToList();
        if (!_ffmpegOk || !profile.IsReady || queue.Count == 0)
        {
            return;
        }

        foreach (var item in queue)
        {
            item.Reset();
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
                ShowOverall(index, queue.Count, 0);
                var progress = new Progress<double>(v =>
                {
                    item.Progress = v;
                    ShowOverall(index, queue.Count, v);
                });

                try
                {
                    var output = await Exporter.ExportAsync(
                        item.Path,
                        _outputFolder,
                        profile,
                        progress,
                        info => item.Status = "Kodlanıyor · " + info,
                        token);
                    item.OutputPath = output;
                    item.Progress = 1;
                    item.State = ItemState.Done;
                    item.Status = "Hazır · " + Path.GetFileName(output);
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
                    item.Detail = ex.Message;
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

    private void ShowOverall(int index, int count, double current)
    {
        var overall = count == 0 ? 0 : Math.Clamp((index + current) / count, 0, 1);
        OverallBar.Value = overall;
        Taskbar.ProgressValue = overall;
        if (_cts is { IsCancellationRequested: false })
        {
            var pct = ((int)Math.Floor(current * 100)).ToString(CultureInfo.InvariantCulture);
            StatusText.Text = $"{index + 1}/{count} kodlanıyor · %{pct}";
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
        UpdateStartState();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!IsBusy)
        {
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
}
