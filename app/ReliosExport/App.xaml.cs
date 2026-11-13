using System.Windows;
using System.Windows.Threading;

namespace ReliosExport;

public partial class App : Application
{
    static App()
    {
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
        // Yalnızca GitHub'daki derleme kontrolü için: tek exe'nin içindeki FFmpeg açılıyor mu?
        // "Relios Export.exe --ffmpeg-kontrol <dosya>" pencere açmadan sonucu dosyaya yazar ve kapanır.
        var i = Array.IndexOf(e.Args, "--ffmpeg-kontrol");
        if (i >= 0)
        {
            StartupUri = null;
            var path = i + 1 < e.Args.Length
                ? e.Args[i + 1]
                : System.IO.Path.Combine(Core.AppPaths.AppFolder, "ffmpeg-kontrol.txt");
            System.IO.File.WriteAllText(
                path,
                $"klasor={Core.Tools.Folder}\nffmpeg={Core.Tools.Ffmpeg}\nffprobe={Core.Tools.Ffprobe}\nbulundu={Core.Tools.Available}\n");
            Shutdown(Core.Tools.Available ? 0 : 1);
            return;
        }

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReport.Show(e.Exception, "Dispatcher");
        e.Handled = true;
        Shutdown(1);
    }
}
