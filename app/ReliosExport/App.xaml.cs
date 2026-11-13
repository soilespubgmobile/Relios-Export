using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ReliosExport.Arayuz;
using ReliosExport.Core;
using ReliosExport.Core.Ayarlar;

namespace ReliosExport;

public partial class App : Application
{
    static App()
    {
        // Derleme kontrolünde (--ffmpeg-kontrol / --pencere-kontrol) hata penceresi açılmaz:
        // hata yalnızca dosyaya yazılır ve program hata koduyla kapanır.
        CrashReport.Silent = Array.Exists(
            Environment.GetCommandLineArgs(), a => a is "--ffmpeg-kontrol" or "--pencere-kontrol");

        // WPF'in kendisi başlatılmadan önce bağlanır: başlangıçta bir şey çökerse bile
        // hata "Relios Export hata.txt" dosyasına yazılır ve ekranda gösterilir.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashReport.Show(e.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashReport.Write(e.Exception, "Task");
            e.SetObserved();
        };
    }

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        GirisKutulari.Etkinlestir();

        // Yalnızca GitHub'daki derleme kontrolü için (sonucu dosyaya yazar, kapanır):
        //   --ffmpeg-kontrol <dosya>   tek exe'nin içindeki FFmpeg açılıyor mu
        //   --pencere-kontrol <dosya>  ana pencere ve ayar pencereleri hatasız açılıyor mu
        //   --ekran <klasör>           (pencere kontrolüyle) pencerelerin görüntüsünü PNG olarak kaydeder
        var ff = Array.IndexOf(e.Args, "--ffmpeg-kontrol");
        var win = Array.IndexOf(e.Args, "--pencere-kontrol");
        var k = ff >= 0 ? ff : win;
        if (k >= 0)
        {
            var path = k + 1 < e.Args.Length
                ? e.Args[k + 1]
                : Path.Combine(AppPaths.AppFolder, "kontrol.txt");
            var ek = Array.IndexOf(e.Args, "--ekran");
            var ekran = ek >= 0 && ek + 1 < e.Args.Length ? e.Args[ek + 1] : null;
            var ov = Array.IndexOf(e.Args, "--ornek-video");
            _ornekVideo = ov >= 0 && ov + 1 < e.Args.Length ? e.Args[ov + 1] : null;
            RunCheck(path, window: ff < 0, ekran);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private static string? _ornekVideo;

    private void RunCheck(string path, bool window, string? ekran)
    {
        try
        {
            var text = $"klasor={Tools.Folder}\nffmpeg={Tools.Ffmpeg}\n" +
                       $"ffprobe={Tools.Ffprobe}\nbulundu={Tools.Available}\n";
            if (!window)
            {
                File.WriteAllText(path, text);
                Shutdown(Tools.Available ? 0 : 1);
                return;
            }

            var w = new MainWindow();
            MainWindow = w;
            w.Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                text += "pencere=acildi\n";
                try
                {
                    text += PencereleriDene(w, ekran);
                    File.WriteAllText(path, text);
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(path, text + "hata=" + ex);
                    Shutdown(3);
                }
            }));
            w.Show();
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(path, "hata=" + ex);
            }
            catch (Exception)
            {
                // yazılamadıysa yalnızca çıkış kodu kalır
            }

            Shutdown(2);
        }
    }

    /// <summary>
    /// Ayar pencerelerinin hepsini açıp kapatır (XAML ve bağlama hataları burada çıkar);
    /// istenirse ana pencerenin ve her sekmenin görüntüsünü kaydeder.
    /// </summary>
    private static string PencereleriDene(MainWindow w, string? ekran)
    {
        var rapor = new StringBuilder();
        var arka = (Brush)w.FindResource("BgSurface");
        w.KontrolIcinDoldur();
        EkranGoruntusu.Bekle(w.Dispatcher);
        if (ekran is not null)
        {
            EkranGoruntusu.Pencere(w, Path.Combine(ekran, "01-ana-pencere-tiktok.png"));
            w.KontrolIcinTumSatirlariAc();
            EkranGoruntusu.Bekle(w.Dispatcher);
            EkranGoruntusu.Oge(w.AyarPaneli, arka, Path.Combine(ekran, "02-ayar-paneli-tiktok.png"));
            w.RbYouTube.IsChecked = true;
            EkranGoruntusu.Bekle(w.Dispatcher);
            EkranGoruntusu.Oge(w.AyarPaneli, arka, Path.Combine(ekran, "03-ayar-paneli-youtube.png"));
            EkranGoruntusu.Pencere(w, Path.Combine(ekran, "04-ana-pencere-youtube.png"));
            w.RbTikTok.IsChecked = true;
            EkranGoruntusu.Bekle(w.Dispatcher);
        }

        // Ön ayar penceresi boş görünmesin: bellekte iki örnek (diske yazılmaz).
        var ayarlar = w.KontrolAyarlari;
        var dosya = w.KontrolAyarDosyasi;
        dosya.OnAyarlar.Add(new KayitliOnAyar { Ad = "TikTok dans", Profil = ExportProfileId.TikTok, Ayarlar = Varsayilanlar.TikTok() });
        dosya.OnAyarlar.Add(new KayitliOnAyar { Ad = "YouTube 4K", Profil = ExportProfileId.YouTube, Ayarlar = Varsayilanlar.YouTube() });

        var pencereler = new (string Ad, Func<Window> Yap)[]
        {
            ("x264", () => new X264Penceresi(ayarlar)),
            ("nvenc", () => new NvencPenceresi(ayarlar)),
            ("fdk", () => new FdkPenceresi(ayarlar)),
            ("onayar", () => new OnAyarPenceresi(dosya, Profiles.TikTok, ayarlar)),
        };

        var sira = 5;
        foreach (var (ad, yap) in pencereler)
        {
            var d = yap();
            d.Owner = w;
            d.Show();
            EkranGoruntusu.Bekle(d.Dispatcher);
            var sekme = ekran is null ? 0 : EkranGoruntusu.Sekmeler(d, ekran, $"{sira:00}-{ad}");
            d.Close();
            rapor.Append($"{ad}=acildi");
            rapor.Append(ekran is null ? "\n" : $" ({sekme} görüntü)\n");
            sira++;
        }

        // Kırpma önizlemesi gerçek bir videoyla (ffmpeg ile iki kare üretir): ortadan ve soldan.
        if (_ornekVideo is not null)
        {
            var onizlemeAyari = Varsayilanlar.TikTok();
            var k = new KirpmaPenceresi([_ornekVideo], onizlemeAyari) { Owner = w };
            k.Show();
            var hazir = Bekle(k, () => k.Hazir);
            if (ekran is not null && hazir)
            {
                EkranGoruntusu.Pencere(k, Path.Combine(ekran, $"{sira:00}-kirpma-orta.png"));
            }

            var onceki = k.CiktiSayisi;
            onizlemeAyari.Video.KirpmaYatay = 0;
            var hazir2 = Bekle(k, () => k.CiktiSayisi > onceki && k.Hazir);
            if (ekran is not null && hazir2)
            {
                EkranGoruntusu.Pencere(k, Path.Combine(ekran, $"{sira:00}-kirpma-sol.png"));
            }

            k.Close();
            rapor.Append($"kirpma=acildi (kareler {(hazir && hazir2 ? "hazir" : "HAZIR DEGIL")})\n");
            if (!(hazir && hazir2))
            {
                throw new InvalidOperationException("Kırpma önizlemesinde kareler 30 saniyede hazırlanmadı.");
            }
        }

        // TikTok varsayılanının x264 komutu (önizlemedeki ile aynı fonksiyon) — kontrol çıktısında görünsün.
        rapor.Append("x264_tiktok=").Append(ArgsBuilder.X264KomutMetni(Varsayilanlar.TikTok(), 30000.0 / 1001)).Append('\n');
        return rapor.ToString();
    }

    /// <summary>Koşul sağlanana kadar (en çok 30 sn) pencerenin işlerini yürütür.</summary>
    private static bool Bekle(Window w, Func<bool> kosul)
    {
        for (var i = 0; i < 300; i++)
        {
            EkranGoruntusu.Bekle(w.Dispatcher);
            if (kosul())
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReport.Show(e.Exception, "Dispatcher");
        e.Handled = true;
        Shutdown(1);
    }
}
