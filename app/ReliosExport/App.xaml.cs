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
            Environment.GetCommandLineArgs(), a => a is "--ffmpeg-kontrol" or "--pencere-kontrol" or "--uctan-uca");

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
        //   --uctan-uca <rapor> --medya <klasör> --cikti <klasör>  arayüzü bir kullanıcı gibi kullanıp denetler
        var uu = Array.IndexOf(e.Args, "--uctan-uca");
        if (uu >= 0)
        {
            string Arg(string ad)
            {
                var i = Array.IndexOf(e.Args, ad);
                return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : string.Empty;
            }

            UctanUcaCalistir(Arg("--uctan-uca"), Arg("--medya"), Arg("--cikti"));
            return;
        }

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
            RunCheck(path, window: ff < 0, ekran);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

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
            w.Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Func<Task>(async () =>
            {
                text += "pencere=acildi\n";
                try
                {
                    text += await PencereleriDeneAsync(w, ekran);
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

    /// <summary>Uçtan uca deneme: sonuç rapora yazılır; fark varsa çıkış kodu 1, beklenmeyen hata 2.</summary>
    private void UctanUcaCalistir(string raporYolu, string medya, string cikti)
    {
        var w = new MainWindow();
        MainWindow = w;
        w.Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Func<Task>(async () =>
        {
            var rapor = new StringBuilder();
            var kod = 0;
            try
            {
                kod = await UctanUca.CalistirAsync(w, medya, cikti, rapor) == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                rapor.AppendLine("BEKLENMEYEN HATA | " + ex);
                kod = 2;
            }

            await UctanUca.DurdurAsync(w);
            File.WriteAllText(raporYolu, rapor.ToString());
            Shutdown(kod);
        }));
        w.Show();
    }

    /// <summary>
    /// Ayar pencerelerinin hepsini açıp kapatır (XAML ve bağlama hataları burada çıkar);
    /// istenirse ana pencerenin ve her sekmenin görüntüsünü kaydeder.
    /// </summary>
    private static async Task<string> PencereleriDeneAsync(MainWindow w, string? ekran)
    {
        var rapor = new StringBuilder();
        var arka = (Brush)w.FindResource("BgSurface");
        w.KontrolIcinDoldur();
        EkranGoruntusu.Bekle(w.Dispatcher);
        // Bekleyen örnek: 60 sn, TikTok CBR 34375 kb/s + WAV 16 bit 48 kHz stereo = 257 MB.
        var ilk = w.KontrolIlkSatir;
        rapor.AppendLine("Tahmini boyut: " + ilk);
        if (!ilk.Contains("tahmini 257 MB", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Bekleyen satırda tahmini boyut yok: " + ilk);
        }

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

        // Hepsi bitince Başlat düğmesi "Tekrar işle" olmalı.
        var dugme = w.KontrolIcinHepsiBitti();
        EkranGoruntusu.Bekle(w.Dispatcher);
        rapor.AppendLine("Tekrar işle düğmesi: " + (dugme == "Tekrar işle" ? "tamam" : "HATA (" + dugme + ")"));
        if (dugme != "Tekrar işle")
        {
            throw new InvalidOperationException("Hepsi bitince Başlat düğmesi 'Tekrar işle' olmadı: " + dugme);
        }

        if (ekran is not null)
        {
            EkranGoruntusu.Pencere(w, Path.Combine(ekran, "09-ana-pencere-tekrar-isle.png"));
        }

        // Ses seviyesi bölümü, LUFS eşitleme açıkken (hedef, yön seçimi görünür); sonra varsayılana (kapalı) döner.
        w.KontrolAyarlari.Ses.Esitleme = true;
        EkranGoruntusu.Bekle(w.Dispatcher);
        if (ekran is not null)
        {
            EkranGoruntusu.Oge(w.AyarPaneli, arka, Path.Combine(ekran, "10-ayar-paneli-esitleme.png"));
        }

        rapor.AppendLine("Eşitleme yönü: " + w.KontrolAyarlari.Ses.EsitlemeYonu);
        w.KontrolAyarlari.Ses.Esitleme = false;
        EkranGoruntusu.Bekle(w.Dispatcher);

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

        // TikTok varsayılanının x264 komutu (önizlemedeki ile aynı fonksiyon) — kontrol çıktısında görünsün.
        rapor.Append("x264_dikey916=").Append(ArgsBuilder.X264KomutMetni(Varsayilanlar.Dikey916(), 30000.0 / 1001)).Append('\n');
        rapor.Append("nvenc_tiktok=").Append(ArgsBuilder.NvencKomutMetni(Varsayilanlar.TikTok(), 60)).Append('\n');
        return rapor.ToString();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReport.Show(e.Exception, "Dispatcher");
        e.Handled = true;
        Shutdown(1);
    }
}
