using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace UsageMonitor;

public partial class App : Application
{
    private Mutex _instance = new();
    private bool _ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var preview = e.Args.FirstOrDefault(argument => argument.StartsWith("--preview=", StringComparison.Ordinal)) ?? string.Empty;
        var instanceName = preview.Length > 0 ? $"UsageMonitor.Preview.{Environment.ProcessId}" : "UsageMonitor.Widget";
        _instance.Dispose();
        _instance = new Mutex(true, instanceName, out _ownsInstance);
        if (!_ownsInstance)
        {
            EventWaitHandle.TryOpenExisting("UsageMonitor.Show", out var showEvent);
            using (showEvent)
                showEvent?.Set();
            Shutdown();
            return;
        }

        var window = new MainWindow(preview.Replace("--preview=", string.Empty, StringComparison.Ordinal));
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstance)
            _instance.ReleaseMutex();
        _instance.Dispose();
        base.OnExit(e);
    }
}
