using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CirDeck.Core;
using CirDeck.Interop;

namespace CirDeck.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private LibrarySession? _session;
    private LaunchpadWindow? _launchpad;
    private DockWindow? _dock;
    private IconBroker? _icons;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write(args.Exception);
            args.Handled = true;
        };

        if (e.Args.Any(arg => arg == "--scan"))
        {
            ConsoleAttach.Parent();
            try
            {
                var apps = StartMenuScanner.Scan();
                var lines = new List<string> { apps.Count.ToString() };
                foreach (var app in apps.Take(30))
                {
                    lines.Add(app.Name + "\t" + app.TargetPath);
                }

                Directory.CreateDirectory(AppPaths.LocalRoot);
                File.WriteAllLines(Path.Combine(AppPaths.LocalRoot, "scan.txt"), lines);
                foreach (var line in lines)
                {
                    Console.WriteLine(line);
                }
            }
            catch (Exception ex)
            {
                Log.Write(ex);
            }

            Shutdown(0);
            return;
        }

        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CirDeck.Activate");
        _mutex = new Mutex(true, @"Local\CirDeck.SingleInstance", out var created);
        if (!created)
        {
            _activate.Set();
            Shutdown(0);
            return;
        }

        _icons = new IconBroker();
        try
        {
            _session = LibrarySession.Load(Dispatcher);
            _launchpad = new LaunchpadWindow(_session, _icons);
            _dock = new DockWindow(_session, _icons);
        }
        catch (Exception ex)
        {
            Log.Write(ex);
            Shutdown(1);
            return;
        }
        _launchpad.ToggleRequested += ToggleLaunchpad;
        _launchpad.LaunchRequested += Launch;
        _launchpad.IsVisibleChanged += (_, _) => SyncDock();
        _dock.LaunchRequested += Launch;
        _dock.ExitRequested += ExitApplication;
        _dock.RescanRequested += () => _session.Rescan();
        _session.Changed += () =>
        {
            if (_launchpad.IsVisible)
            {
                return;
            }

            SyncDock();
        };

        if (_session.Settings.OpenLaunchpadAtStartup)
        {
            _launchpad.Present();
        }
        else
        {
            SyncDock();
        }

        _session.Rescan();
        StartRunningWatch();
        StartHotCorner();
        ListenForActivate();
    }

    private static bool IsWeChat(AppEntry entry)
    {
        var file = Path.GetFileName(entry.TargetPath);
        return entry.Name.Contains("微信", StringComparison.Ordinal)
            || file.Equals("Weixin.exe", StringComparison.OrdinalIgnoreCase)
            || file.Equals("WeChat.exe", StringComparison.OrdinalIgnoreCase);
    }

    private void StartHotCorner()
    {
        var armed = true;
        var insideSince = DateTime.MinValue;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) =>
        {
            if (_session == null || _launchpad == null || !_session.Settings.EnableHotCorner || _launchpad.IsVisible)
            {
                armed = true;
                return;
            }

            if (!MonitorLayout.TryGetCursor(out var x, out var y))
            {
                return;
            }

            var area = MonitorLayout.WorkAreaFromCursor();
            var inside = HotCorner.Contains(_session.Settings.HotCorner, x, y, area.Left, area.Top, area.Right, area.Bottom);
            if (!inside)
            {
                armed = true;
                return;
            }

            if (armed)
            {
                insideSince = DateTime.UtcNow;
                armed = false;
            }

            if ((DateTime.UtcNow - insideSince).TotalMilliseconds >= 280)
            {
                armed = false;
                _launchpad.Present();
            }
        };
        timer.Start();
    }

    private void ToggleLaunchpad()
    {
        if (_launchpad == null)
        {
            return;
        }

        if (_launchpad.IsVisible)
        {
            _launchpad.Dismiss();
        }
        else
        {
            _launchpad.Present();
        }
    }

    private void SyncDock()
    {
        if (_dock == null || _session == null || _launchpad == null)
        {
            return;
        }

        if (_session.Settings.ShowDock && !_launchpad.IsVisible)
        {
            _dock.ShowDock();
        }
        else
        {
            _dock.Hide();
        }
    }

    private void Launch(AppEntry entry)
    {
        try
        {
            var directory = entry.WorkingDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Path.GetDirectoryName(entry.TargetPath) ?? string.Empty;
            }

            if (IsWeChat(entry) && WeChatWake.TryActivate())
            {
                if (_session?.Settings.CloseAfterLaunch == true)
                {
                    _launchpad?.Dismiss();
                }

                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = entry.TargetPath,
                Arguments = entry.Arguments,
                WorkingDirectory = directory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Write(ex);
        }

        if (_session?.Settings.CloseAfterLaunch == true)
        {
            _launchpad?.Dismiss();
        }
    }

    private void StartRunningWatch()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            if (_dock == null || !_dock.IsVisible || _session?.Settings.ShowRunningIndicators != true)
            {
                return;
            }

            Task.Run(() =>
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        names.Add(process.ProcessName);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                return names;
            }).ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    Dispatcher.BeginInvoke(new Action(() => _dock?.SetRunning(task.Result)));
                }
            });
        };
        timer.Start();
    }

    private void ListenForActivate()
    {
        var handle = _activate;
        if (handle == null)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            while (handle.WaitOne())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_exiting)
                    {
                        _launchpad?.Present();
                    }
                }));
            }
        })
        {
            IsBackground = true,
            Name = "CirDeck.Activate"
        };
        thread.Start();
    }

    private void ExitApplication()
    {
        _exiting = true;
        _launchpad?.AllowClose();
        _dock?.AllowClose();
        _icons?.Dispose();
        Shutdown();
    }
}
