using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport;

/// <summary>
/// Kırpma önizlemesi: soldaki kaynak karede çıktıya alınacak alan çerçeveli görünür, sağda aynı karenin
/// programın kendi filtresiyle (kırpma, ölçekleme, renk) üretilmiş çıktısı. Konum değiştikçe güncellenir;
/// değişiklikler doğrudan profilin ayarlarına yazılır.
/// </summary>
public partial class KirpmaPenceresi : Window
{
    private static readonly double[] KareYerleri = [0.5, 0.25, 0.75, 0.1, 0.9];

    private readonly IReadOnlyList<string> _videolar;
    private readonly ProfilAyarlari _ayarlar;
    private readonly DispatcherTimer _gecikme;
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "Relios Export", "onizleme-" + Guid.NewGuid().ToString("N"));
    private MediaInfo? _media;
    private string? _video;
    private int _kareSirasi;
    private CancellationTokenSource? _cts;
    private int _surum;
    private int _kaynakSurum;

    public KirpmaPenceresi(IReadOnlyList<string> videolar, ProfilAyarlari ayarlar)
    {
        InitializeComponent();
        _videolar = videolar;
        _ayarlar = ayarlar;
        DataContext = ayarlar.Video;
        _gecikme = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(350) };
        _gecikme.Tick += async (_, _) =>
        {
            _gecikme.Stop();
            await CiktiyiYenileAsync();
        };

        _ayarlar.Degisti += OnAyarDegisti;
        Closed += (_, _) =>
        {
            _ayarlar.Degisti -= OnAyarDegisti;
            _cts?.Cancel();
            try
            {
                Directory.Delete(_klasor, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // geçici dosya sonra silinir
            }
        };

        VideoSecimi.ItemsSource = videolar.Select(v => Path.GetFileName(v)).ToList();
        VideoSecimi.SelectedIndex = 0;
    }

    /// <summary>Pencere kontrolü için: iki kare de hazır mı.</summary>
    internal int CiktiSayisi { get; private set; }

    internal bool Hazir => KaynakResim.Source is not null && CiktiResim.Source is not null && Durum.Text.Length == 0;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.Apply(this);
    }

    private double Saniye => _media is { DurationSeconds: > 0 } m ? m.DurationSeconds * KareYerleri[_kareSirasi % KareYerleri.Length] : 0;

    private async void OnVideoDegisti(object sender, SelectionChangedEventArgs e)
    {
        if (VideoSecimi.SelectedIndex < 0)
        {
            return;
        }

        _video = _videolar[VideoSecimi.SelectedIndex];
        _kareSirasi = 0;
        _media = null;
        try
        {
            _media = await MediaProbe.ProbeAsync(Tools.Ffprobe, _video, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Durum.Text = "Video okunamadı: " + ex.Message.Split('\n')[0];
            return;
        }

        await HepsiniYenileAsync();
    }

    private async void OnBaskaKare(object sender, RoutedEventArgs e)
    {
        _kareSirasi++;
        await HepsiniYenileAsync();
    }

    private void OnAyarDegisti(object? sender, EventArgs e)
    {
        CerceveyiCiz();
        _gecikme.Stop();
        _gecikme.Start();
    }

    private async Task HepsiniYenileAsync()
    {
        if (_video is null || _media is null)
        {
            return;
        }

        var surum = ++_kaynakSurum;
        Directory.CreateDirectory(_klasor);
        var png = Path.Combine(_klasor, $"kaynak-{surum}.png");
        Durum.Text = "Kare hazırlanıyor…";
        try
        {
            await Onizleme.KareAsync(_video, Saniye, null, png, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Durum.Text = "Kare alınamadı: " + ex.Message.Split('\n')[0];
            return;
        }

        if (surum != _kaynakSurum || !File.Exists(png))
        {
            return;
        }

        var resim = Yukle(png);
        KaynakResim.Source = resim;
        KaynakAlan.Width = resim.PixelWidth;
        KaynakAlan.Height = resim.PixelHeight;
        KaynakBaslik.Text = $"KAYNAK · {_media.DisplayWidth}×{_media.DisplayHeight} · {Bicim.Sayi(Saniye)}. saniye";
        CerceveyiCiz();
        await CiktiyiYenileAsync();
    }

    /// <summary>Kaynak karede çıktıya giren alanı çerçeveler, dışını karartır (ffmpeg gerektirmez, anında).</summary>
    private void CerceveyiCiz()
    {
        if (_media is null || KaynakResim.Source is not BitmapSource resim)
        {
            return;
        }

        var v = _ayarlar.Video;
        var crop = v.Cozunurluk == CozunurlukModu.Hedef && v.Oran == OranUyumu.OrtadanKirp
            ? ArgsBuilder.PlanCrop(_ayarlar, _media)
            : null;
        if (crop is not { IsNeeded: true })
        {
            Cerceve.Visibility = Visibility.Collapsed;
            Karartma.Data = null;
            Bilgi.Text = v.Cozunurluk == CozunurlukModu.Kaynak
                ? "Boyut kaynakla aynı: kırpma yok."
                : v.Oran == OranUyumu.OrtadanKirp ? "Kaynağın oranı hedefle aynı: kırpma yok." : "Kırpma kapalı (oran farklıysa: " + VideoAyarlari.OranMetni(v.Oran) + ").";
            return;
        }

        // Kırpma kaynağın (döndürülmüş) piksellerinde; kare piksele açılmış resme oranla taşınır.
        double sx = resim.PixelWidth / (double)_media.DisplayWidth, sy = resim.PixelHeight / (double)_media.DisplayHeight;
        var alan = new Rect(crop.X * sx, crop.Y * sy, crop.Width * sx, crop.Height * sy);
        Cerceve.Margin = new Thickness(alan.X, alan.Y, 0, 0);
        Cerceve.Width = alan.Width;
        Cerceve.Height = alan.Height;
        Cerceve.StrokeThickness = Math.Max(2, resim.PixelWidth / 240.0);
        Cerceve.Visibility = Visibility.Visible;
        Karartma.Data = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, resim.PixelWidth, resim.PixelHeight)),
            new RectangleGeometry(alan));
        Bilgi.Text = $"Alınan alan: {crop.Width}×{crop.Height}, sol üst köşe {crop.X}, {crop.Y} · çıktı {v.Genislik}×{v.Yukseklik}";
    }

    private async Task CiktiyiYenileAsync()
    {
        if (_video is null || _media is null)
        {
            return;
        }

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var surum = ++_surum;
        var png = Path.Combine(_klasor, $"cikti-{surum}.png");
        Durum.Text = "Çıktı karesi hazırlanıyor…";
        try
        {
            Directory.CreateDirectory(_klasor);
            var vf = ArgsBuilder.VideoFiltreMetni(_ayarlar, _media);
            await Onizleme.KareAsync(_video, Saniye, vf, png, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                Durum.Text = "Çıktı karesi üretilemedi: " + ex.Message.Split('\n')[0];
            }

            return;
        }

        if (cts.IsCancellationRequested || !File.Exists(png))
        {
            return;
        }

        var resim = Yukle(png);
        CiktiResim.Source = resim;
        CiktiSayisi++;
        CiktiBaslik.Text = $"ÇIKTI · {resim.PixelWidth}×{resim.PixelHeight}";
        Durum.Text = string.Empty;
    }

    private static BitmapImage Yukle(string png)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        b.UriSource = new Uri(png);
        b.EndInit();
        b.Freeze();
        return b;
    }
}
