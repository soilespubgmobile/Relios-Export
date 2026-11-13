using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using ReliosExport.Core;

namespace ReliosExport;

public sealed class FileItem : INotifyPropertyChanged
{
    private string _status = "Bekliyor";
    private double _progress;

    public FileItem(string path) => Path = path;

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public double Progress
    {
        get => _progress;
        set => Set(ref _progress, value);
    }

    public bool Done { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public partial class MainWindow : Window
{
    private static readonly string[] VideoExtensions =
        [".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".ts", ".mts", ".m2ts", ".flv", ".wmv"];

    private readonly ObservableCollection<FileItem> _files = [];
    private CancellationTokenSource? _cts;
    private string _outputFolder =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Relios Export");

    public MainWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _files;
        _files.CollectionChanged += (_, _) => DropHint.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OutputFolderText.Text = _outputFolder;
        UpdateProfileInfo();

        var missing = Tools.MissingMessage();
        if (missing is not null)
        {
            StatusText.Text = "FFmpeg bulunamadı";
            MessageBox.Show(this, missing, "Relios Export", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private ExportProfile SelectedProfile => RbYouTube.IsChecked == true ? Profiles.YouTube : Profiles.TikTok;

    private void OnProfileChanged(object sender, RoutedEventArgs e) => UpdateProfileInfo();

    private void UpdateProfileInfo()
    {
        if (ProfileInfo is null)
        {
            return;
        }

        var p = SelectedProfile;
        ProfileInfo.Text = p.IsReady ? p.Description : $"{p.DisplayName}: {p.Description}";
        if (BtnStart is not null && _cts is null)
        {
            BtnStart.IsEnabled = p.IsReady;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && _cts is null
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_cts is not null || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        AddPaths(paths);
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
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
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                AddPaths(Directory.EnumerateFiles(path));
                continue;
            }

            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (!VideoExtensions.Contains(ext) || _files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _files.Add(new FileItem(path));
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_cts is null)
        {
            _files.Clear();
            StatusText.Text = string.Empty;
        }
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Kayıt yerini seç", InitialDirectory = _outputFolder };
        if (dlg.ShowDialog(this) == true)
        {
            _outputFolder = dlg.FolderName;
            OutputFolderText.Text = _outputFolder;
        }
    }

    private async void OnStartOrCancel(object sender, RoutedEventArgs e)
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            return;
        }

        var missing = Tools.MissingMessage();
        if (missing is not null)
        {
            MessageBox.Show(this, missing, "Relios Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profile = SelectedProfile;
        var queue = _files.Where(f => !f.Done).ToList();
        if (queue.Count == 0)
        {
            StatusText.Text = "Listede işlenecek video yok";
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        var ok = 0;
        var failed = 0;
        try
        {
            foreach (var item in queue)
            {
                _cts.Token.ThrowIfCancellationRequested();
                item.Status = "İşleniyor…";
                item.Progress = 0;
                StatusText.Text = $"{ok + failed + 1}/{queue.Count} işleniyor";
                var progress = new Progress<double>(v => item.Progress = v);
                try
                {
                    var output = await Exporter.ExportAsync(item.Path, _outputFolder, profile, progress, _cts.Token);
                    item.Status = "Bitti → " + System.IO.Path.GetFileName(output);
                    item.Done = true;
                    ok++;
                }
                catch (OperationCanceledException)
                {
                    item.Status = "İptal edildi";
                    item.Progress = 0;
                    throw;
                }
                catch (Exception ex)
                {
                    item.Status = "Hata: " + ex.Message;
                    item.Progress = 0;
                    failed++;
                }
            }

            StatusText.Text = failed == 0 ? $"{ok} video hazır" : $"{ok} hazır, {failed} hatalı";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "İptal edildi";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        BtnStart.Content = busy ? "İptal" : "Başlat";
        BtnStart.IsEnabled = busy || SelectedProfile.IsReady;
        BtnAdd.IsEnabled = !busy;
        BtnClear.IsEnabled = !busy;
        RbTikTok.IsEnabled = !busy;
        RbYouTube.IsEnabled = !busy;
    }
}
