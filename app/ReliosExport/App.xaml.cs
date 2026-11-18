using System.IO;
using System.Runtime.InteropServices;
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
            Environment.GetCommandLineArgs(), a => a is "--ffmpeg-kontrol" or "--pencere-kontrol" or "--uctan-uca" or "--arayuz-taramasi");

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
        //   --arayuz-taramasi <rapor> --medya <klasör> --cikti <klasör>  her kutuyu, listeyi, düğmeyi tek tek dener
        var uu = Array.IndexOf(e.Args, "--uctan-uca");
        var at = Array.IndexOf(e.Args, "--arayuz-taramasi");
        if (uu >= 0 || at >= 0)
        {
            string Arg(string ad)
            {
                var i = Array.IndexOf(e.Args, ad);
                return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : string.Empty;
            }

            if (at >= 0)
            {
                ArayuzTaramasiCalistir(Arg("--arayuz-taramasi"), Arg("--medya"), Arg("--cikti"));
            }
            else
            {
                UctanUcaCalistir(Arg("--uctan-uca"), Arg("--medya"), Arg("--cikti"));
            }

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

        // Tek kopya (0.16.1): aynı exe ikinci kez açılırsa yeni pencere açılmaz, açık olan öne gelir.
        if (BaskaKopyaAcik())
        {
            Shutdown(0);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TekKopyayiBirak();
        base.OnExit(e);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Tek kopya (0.16.1). Aynı exe iki kez açık olursa iki pencere aynı ayarlar.json'ı birbirinin üstüne yazar (son
    // kapanan kazanır) ve aynı videoyu iki kez işleyebilir. Bu yüzden aynı exe'nin ikinci açılışı yeni pencere açmaz:
    // açık kopyaya "öne gel" işareti gönderir, yanıtını alınca kapanır. Yalnızca Windows'un adlandırılmış nesneleri
    // kullanılır (oturuma özel "Local\" adları: kilit + iki işaret); ağ, dosya ya da kayıt defteri yok.
    // Güvenli taraf hep "program açılsın": açık kopya 5 saniyede yanıt vermezse (takıldıysa) ya da bu denetimde
    // beklenmeyen bir hata olursa program her zamanki gibi açılır. Ad exe'nin tam yolundan ve derlemesinden türetilir
    // (AppPaths.TekKopyaAdi): başka klasördeki kopya ya da yeni indirilen başka sürüm, eskisi açıkken de açılır. O
    // durumda da çıktılar çakışmaz (yarım dosyanın adı kopyaya özel, biten dosya boş ada taşınır: Exporter).
    // Derleme denetimleri (--ffmpeg-kontrol, --pencere-kontrol, --uctan-uca, --arayuz-taramasi) bu denetime girmez.
    // ------------------------------------------------------------------------------------------------------------
    private static readonly TimeSpan TekKopyaYanitSuresi = TimeSpan.FromSeconds(5);
    private Mutex? _tekKopyaKilidi;
    private bool _tekKopyaKilidiBende;
    private EventWaitHandle? _oneGelIstegi;
    private EventWaitHandle? _oneGeldi;
    private RegisteredWaitHandle? _oneGelBeklemesi;

    /// <summary>
    /// true: aynı exe'nin başka bir kopyası açık ve "öne gel" işaretine yanıt verdi (bu kopya pencere açmadan
    /// kapanmalı). false: bu kopya açılmalı (tek kopya bu; öbürü bu sırada kapandı; öbürü yanıt vermiyor; ya da denetim
    /// yapılamadı).
    /// </summary>
    private bool BaskaKopyaAcik()
    {
        try
        {
            var derleme = typeof(App).Assembly;
            var surum = (Attribute.GetCustomAttribute(derleme, typeof(System.Reflection.AssemblyInformationalVersionAttribute))
                            as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion
                        ?? derleme.GetName().Version?.ToString()
                        ?? string.Empty;
            var exe = Environment.ProcessPath is { Length: > 0 } yol ? yol : Path.Combine(AppPaths.AppFolder, "Relios Export.exe");
            var ad = @"Local\" + AppPaths.TekKopyaAdi(exe, surum);
            _tekKopyaKilidi = new Mutex(false, ad + "-kilit");
            _oneGelIstegi = new EventWaitHandle(false, EventResetMode.AutoReset, ad + "-one-gel");
            _oneGeldi = new EventWaitHandle(false, EventResetMode.ManualReset, ad + "-one-geldi");

            _tekKopyaKilidiBende = TekKopyaKilidiniAl();
            if (!_tekKopyaKilidiBende)
            {
                // Kilit başka bir kopyada. Öne gelebilsin diye izin verilir (Windows, kullanıcının başlatmadığı bir
                // programın kendini öne almasına izin vermez; kullanıcının az önce başlattığı bu kopya izin verebilir).
                try
                {
                    AllowSetForegroundWindow(AsfwAny);
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                {
                    // İzin verilemedi: açık kopya yine yanıt verir, Windows görev çubuğunda yanıp söndürür.
                }

                _oneGeldi.Reset();
                _oneGelIstegi.Set();
                var bitis = Environment.TickCount64 + (long)TekKopyaYanitSuresi.TotalMilliseconds;
                while (true)
                {
                    if (_oneGeldi.WaitOne(100))
                    {
                        return true;
                    }

                    // Açık kopya bu sırada kapandıysa (ör. kapatılıp hemen yeniden açıldı) kilit boşalır: tek kopya bu.
                    // Gönderilen işareti alan olmadı: geri alınır (yoksa bu kopya kendi işaretine yanıt verirdi).
                    if (TekKopyaKilidiniAl())
                    {
                        _tekKopyaKilidiBende = true;
                        _oneGelIstegi.Reset();
                        break;
                    }

                    if (Environment.TickCount64 >= bitis)
                    {
                        // Açık kopya yanıt vermiyor (takılmış olabilir): program yine de açılır.
                        break;
                    }
                }
            }

            if (_tekKopyaKilidiBende)
            {
                // Sonraki açılışların "öne gel" işareti: iş parçacığı havuzunda beklenir, pencere işi arayüzün iş
                // parçacığında yapılır (yanıt da oradan: yanıt veren kopyanın arayüzü gerçekten çalışıyordur).
                _oneGelBeklemesi = ThreadPool.RegisterWaitForSingleObject(
                    _oneGelIstegi,
                    (_, _) =>
                    {
                        try
                        {
                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(OneGel));
                        }
                        catch (Exception)
                        {
                            // Program kapanıyor: yanıt verilmez, yeni kopya kilit boşalınca kendisi açılır.
                        }
                    },
                    null,
                    Timeout.Infinite,
                    executeOnlyOnce: false);
            }

            return false;
        }
        catch (Exception)
        {
            // Denetim yapılamadı (ör. nesneler başka yetkiyle oluşturulmuş): program her zamanki gibi açılır.
            return false;
        }
    }

    private bool TekKopyaKilidiniAl()
    {
        try
        {
            return _tekKopyaKilidi is { } kilit && kilit.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // Kilidin sahibi bırakmadan kapandı (çöktü ya da sonlandırıldı): kilit artık bu kopyanın.
            return true;
        }
    }

    /// <summary>Açık kopyada: pencereyi öne getirir (simge durumundaysa geri açar) ve ikinci kopyaya yanıt verir.</summary>
    private void OneGel()
    {
        if (MainWindow is not { } w)
        {
            // Ana pencere kapandı, program kapanıyor: yanıt verilmez (verilseydi yeni kopya kapanır, sonra bu da
            // kapanır, ortada açık program kalmazdı). Yeni kopya kilit boşalınca kendisi açılır.
            return;
        }

        try
        {
            var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (h != IntPtr.Zero)
            {
                if (IsIconic(h))
                {
                    ShowWindow(h, SwRestore);
                }

                // Açık bir ayar penceresi, onay ya da dosya seçme penceresi varsa o öne gelir (ana pencere o sırada
                // tıklanamaz); yoksa ana pencerenin kendisi.
                var hedef = GetLastActivePopup(h);
                SetForegroundWindow(hedef != IntPtr.Zero ? hedef : h);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            // Pencere öne alınamadı: yanıt yine verilir (program açık ve çalışıyor).
        }
        finally
        {
            try
            {
                _oneGeldi?.Set();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or IOException or UnauthorizedAccessException)
            {
                // Program kapanıyor.
            }
        }
    }

    private void TekKopyayiBirak()
    {
        try
        {
            _oneGelBeklemesi?.Unregister(null);
            _oneGelBeklemesi = null;
            if (_tekKopyaKilidiBende)
            {
                _tekKopyaKilidiBende = false;
                _tekKopyaKilidi?.ReleaseMutex();
            }

            _tekKopyaKilidi?.Dispose();
            _oneGelIstegi?.Dispose();
            _oneGeldi?.Dispose();
        }
        catch (Exception)
        {
            // Program zaten kapanıyor: Windows nesneleri işlem bitince kendiliğinden bırakılır.
        }
    }

    private const int SwRestore = 9;
    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetLastActivePopup(IntPtr hWnd);

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

    /// <summary>Arayüz taraması: sonuç rapora yazılır; fark varsa çıkış kodu 1, beklenmeyen hata 2.</summary>
    private void ArayuzTaramasiCalistir(string raporYolu, string medya, string cikti)
    {
        // Bağlama iletileri pencereler oluşmadan önce dinlenir (açılıştaki hatalar da yakalansın).
        var baglama = ArayuzTaramasi.BaglamaIletileriniDinle();
        var w = new MainWindow();
        MainWindow = w;
        w.Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Func<Task>(async () =>
        {
            var rapor = new StringBuilder();
            var kod = 0;
            try
            {
                kod = await ArayuzTaramasi.CalistirAsync(w, medya, cikti, baglama, rapor) == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                rapor.AppendLine("BEKLENMEYEN HATA | " + ex);
                kod = 2;
            }

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
        // Bekleyen örnek: 60 sn 1080×1920, yanında aynı adlı WAV. TikTok varsayılanı x264 CRF 24 + en yüksek 8000 kb/s
        // (tampon 12000 kb; 0.16.0), ses FDK AAC 128 kb/s: (8000 + 128) × 60 + 12000 × 0,9 = 498 480 kbit
        // = 62 310 000 bayt = 59,4 MB → "en çok 59,4 MB".
        var ilk = w.KontrolIlkSatir;
        rapor.AppendLine("Bekleyen satır (TikTok): " + ilk);
        if (ilk != "Bekliyor · Ses: edit1.wav · en çok 59,4 MB")
        {
            throw new InvalidOperationException("TikTok varsayılanında bekleyen satır beklenen gibi değil (en çok 59,4 MB olmalı): " + ilk);
        }

        if (ekran is not null)
        {
            EkranGoruntusu.Pencere(w, Path.Combine(ekran, "01-ana-pencere-tiktok.png"));
            w.KontrolIcinTumSatirlariAc();
            EkranGoruntusu.Bekle(w.Dispatcher);
            EkranGoruntusu.Oge(w.AyarPaneli, arka, Path.Combine(ekran, "02-ayar-paneli-tiktok.png"));
        }

        // YouTube: CRF 18 + en yüksek 34375 kb/s (tampon 55000 kb), ses FDK AAC 128 kb/s:
        // (34375 + 128) × 60 + 55000 × 0,9 = 2 119 680 kbit = 264 960 000 bayt = 252,7 MB → "en çok 253 MB".
        w.RbYouTube.IsChecked = true;
        EkranGoruntusu.Bekle(w.Dispatcher);
        var ytIlk = w.KontrolIlkSatir;
        rapor.AppendLine("Bekleyen satır (YouTube): " + ytIlk);
        if (!ytIlk.Contains("en çok 253 MB", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("YouTube'da bekleyen satırda tahmini boyut yok: " + ytIlk);
        }

        if (ekran is not null)
        {
            EkranGoruntusu.Oge(w.AyarPaneli, arka, Path.Combine(ekran, "03-ayar-paneli-youtube.png"));
            EkranGoruntusu.Pencere(w, Path.Combine(ekran, "04-ana-pencere-youtube.png"));
        }

        w.RbTikTok.IsChecked = true;
        EkranGoruntusu.Bekle(w.Dispatcher);

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
            int sekme;
            if (d is OnAyarPenceresi oa)
            {
                // İki seçenek (Önerilen / Kayıtlı ayarlarım): ikisinin de görüntüsü. Kayıtlı ön ayar var: açılınca o seçili.
                rapor.AppendLine("Ön ayar penceresi açılınca: " + (oa.OnerilenGorunuyor ? "Önerilen" : "Kayıtlı ayarlarım"));
                if (oa.OnerilenGorunuyor)
                {
                    throw new InvalidOperationException("Kayıtlı ön ayar varken pencere Önerilen seçili açıldı.");
                }

                oa.SekmeOnerilen.IsChecked = true;
                EkranGoruntusu.Bekle(d.Dispatcher);
                if (ekran is not null)
                {
                    EkranGoruntusu.Pencere(d, Path.Combine(ekran, $"{sira:00}-{ad}-1-onerilen.png"));
                }

                oa.SekmeKayitli.IsChecked = true;
                EkranGoruntusu.Bekle(d.Dispatcher);
                if (ekran is not null)
                {
                    EkranGoruntusu.Pencere(d, Path.Combine(ekran, $"{sira:00}-{ad}-2-kayitli.png"));
                }

                sekme = ekran is null ? 0 : 2;
            }
            else
            {
                sekme = ekran is null ? 0 : EkranGoruntusu.Sekmeler(d, ekran, $"{sira:00}-{ad}");
            }

            d.Close();
            rapor.Append($"{ad}=acildi");
            rapor.Append(ekran is null ? "\n" : $" ({sekme} görüntü)\n");
            sira++;
        }

        // x264 / NVENC penceresinin komut notu: örnek, profilin kendi anahtar karesinden (TikTok 10 sn → 60 fps'te 600).
        foreach (var (ad, not, beklenen) in new[]
                 {
                     ("TikTok x264", PencereOrtak.KomutNotu(Varsayilanlar.TikTok()), "(ör. 60 fps'te 600)."),
                     ("TikTok NVENC", PencereOrtak.KomutNotu(Varsayilanlar.TikTok(), nvenc: true), "(ör. 60 fps'te 600)."),
                     ("YouTube x264", PencereOrtak.KomutNotu(Varsayilanlar.YouTube()), "(ör. 60 fps'te 100 / 51)."),
                     ("YouTube NVENC", PencereOrtak.KomutNotu(Varsayilanlar.YouTube(), nvenc: true), "(ör. 60 fps'te 100)."),
                 })
        {
            rapor.AppendLine($"Komut notu ({ad}): {not}");
            if (!not.EndsWith(beklenen, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Komut notu ({ad}) beklenen gibi değil: {not}");
            }
        }

        // Varsayılanların kodlayıcı komutları (önizlemedeki ile aynı fonksiyon) — kontrol çıktısında görünsün.
        rapor.Append("x264_tiktok=").Append(ArgsBuilder.X264KomutMetni(Varsayilanlar.TikTok(), 60)).Append('\n');
        rapor.Append("x264_dikey916=").Append(ArgsBuilder.X264KomutMetni(Varsayilanlar.Dikey916(), 30000.0 / 1001)).Append('\n');
        rapor.Append("nvenc_tiktok=").Append(ArgsBuilder.NvencKomutMetni(Varsayilanlar.TikTokNvenc(), 60)).Append('\n');
        return rapor.ToString();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReport.Show(e.Exception, "Dispatcher");
        e.Handled = true;
        Shutdown(1);
    }
}
