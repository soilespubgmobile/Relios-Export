using System.Windows;
using System.Windows.Threading;

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

        // Yalnızca GitHub'daki derleme kontrolü için (pencere görünmeden sonucu dosyaya yazar, kapanır):
        //   --ffmpeg-kontrol <dosya>   tek exe'nin içindeki FFmpeg açılıyor mu
        //   --pencere-kontrol <dosya>  ana pencere hatasız açılıyor mu
        var ff = Array.IndexOf(e.Args, "--ffmpeg-kontrol");
        var win = Array.IndexOf(e.Args, "--pencere-kontrol");
        var k = ff >= 0 ? ff : win;
        if (k >= 0)
        {
            var path = k + 1 < e.Args.Length
                ? e.Args[k + 1]
                : System.IO.Path.Combine(Core.AppPaths.AppFolder, "kontrol.txt");
            RunCheck(path, window: ff < 0);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void RunCheck(string path, bool window)
    {
        try
        {
            var text = $"klasor={Core.Tools.Folder}\nffmpeg={Core.Tools.Ffmpeg}\n" +
                       $"ffprobe={Core.Tools.Ffprobe}\nbulundu={Core.Tools.Available}\n";
            if (!window)
            {
                System.IO.File.WriteAllText(path, text);
                Shutdown(Core.Tools.Available ? 0 : 1);
                return;
            }

            var w = new MainWindow();
            MainWindow = w;
            w.Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                System.IO.File.WriteAllText(path, text + "pencere=acildi\n");
                Shutdown(0);
            }));
            w.Show();
        }
        catch (Exception ex)
        {
            try
            {
                System.IO.File.WriteAllText(path, "hata=" + ex);
            }
            catch (Exception)
            {
                // yazılamadıysa yalnızca çıkış kodu kalır
            }

            Shutdown(2);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReport.Show(e.Exception, "Dispatcher");
        e.Handled = true;
        Shutdown(1);
    }
}
