using System.Threading;
using System.Windows;
namespace Wideska;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        instance = new Mutex(true, @"Local\Wideska.CalendarWidget", out bool created);
        if (!created) { MessageBox.Show("Wideska is already running. Open it from the system tray.", "Wideska"); Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
