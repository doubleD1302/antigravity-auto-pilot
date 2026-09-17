using System.Windows;

namespace AntigravityAutoPilot;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            try { System.IO.File.WriteAllText("crash.log", ev.ExceptionObject.ToString()); } catch { }
        };
        DispatcherUnhandledException += (s, ev) =>
        {
            try { System.IO.File.WriteAllText("crash.log", ev.Exception.ToString()); } catch { }
        };
        base.OnStartup(e);
    }
}
