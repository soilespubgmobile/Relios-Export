using System.Windows;
using System.Windows.Threading;

namespace ReliosExport;

public partial class App : Application
{
    public App()
    {
        // Program açılırken ya da çalışırken beklenmeyen bir hata olursa sessizce kapanmasın:
        // hata program klasöründeki "Relios Export hata.txt" dosyasına yazılır ve ekranda gösterilir.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashReport.Show(e.ExceptionObject as Exception, "AppDomain");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashReport.Write(e.Exception, "Task");
            e.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReport.Show(e.Exception, "Dispatcher");
        e.Handled = true;
        Shutdown(1);
    }
}
