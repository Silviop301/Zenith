using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zenith.App.Backend;
using Zenith.App.Controls;
using Zenith.App.Tray;
using Zenith.App.ViewModels;
using Zenith.App.Views;
using Zenith.Core.Presentation;
using Zenith.Core.Resources;

namespace Zenith.App;

/// <summary>
/// zenith.exe              the app, on the real PC, with its tray icon
/// zenith.exe --bandeja    starts in the tray only (what Windows runs at sign-in)
/// zenith.exe --demo       the app with a made-up PC (nothing is read or changed, no tray icon)
/// zenith.exe --screenshot &lt;folder&gt; [--demo]   renders each screen to PNG and exits (used by CI)
/// </summary>
public partial class App : Application
{
    private MainViewModel? _vm;
    private TrayIcon? _tray;
    private SingleInstance? _instance;
    private MainWindow? _window;
    private bool _exiting;
    private readonly DispatcherTimer _checkTimer = new() { Interval = TimeSpan.FromHours(1) };

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        var demo = args.Contains("--demo");
        var shotIndex = Array.IndexOf(args, "--screenshot");
        IBackend backend = demo ? new DemoBackend() : new LiveBackend();
        _vm = new MainViewModel(backend);

        DispatcherUnhandledException += (_, ex) =>
        {
            ex.Handled = true;
            if (_vm is not null) _vm.Toast = string.Format(Strings.MsgError, ex.Exception.Message);
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (shotIndex >= 0)
        {
            var folder = shotIndex + 1 < args.Length && !args[shotIndex + 1].StartsWith("--") ? args[shotIndex + 1] : "screenshots";
            var code = 0;
            try
            {
                await CaptureAsync(_vm, folder, demo);
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(Path.Combine(folder, "erro.txt"), ex.ToString());
                code = 1;
            }
            Shutdown(code);
            return;
        }

        if (!demo)
        {
            _instance = SingleInstance.TryClaim(() => Dispatcher.BeginInvoke(ShowWindow));
            if (_instance is null)
            {
                Shutdown();
                return;
            }
            try
            {
                AppSettings.ApplyStartupDefault();
            }
            catch (Exception)
            {
                // A locked-down registry only costs the start with Windows.
            }
            _tray = new TrayIcon(ShowWindow, ScanNow, Quit);
            _vm.PropertyChanged += (_, p) =>
            {
                if (p.PropertyName == nameof(MainViewModel.Tray)) _tray.Update(_vm.Tray);
            };
            SessionEnding += (_, _) => _exiting = true;
        }
        _checkTimer.Tick += async (_, _) => await CheckInBackgroundAsync();

        if (args.Contains(AppSettings.TrayArgument) && _tray is not null)
        {
            // Signed in to Windows: stay in the tray, check quietly, speak up only for a critical alert.
            await _vm.StartAsync();
            await AfterStartAsync();
            return;
        }

        // Opening: the symbol plays while the first scan runs. The main window shows when both are done,
        // or after a few seconds at most; a longer scan goes on inside the main window.
        var splash = new SplashWindow();
        splash.Show();
        var start = _vm.StartAsync();
        await Task.WhenAll(Task.Delay(Motion.Enabled ? SplashWindow.Duration : TimeSpan.FromMilliseconds(600)),
            Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(4))));

        ShowWindow();
        await splash.CloseAsync();
        await start;
        await AfterStartAsync();
    }

    private async Task AfterStartAsync()
    {
        _checkTimer.Start();
        NotifyAlerts();
        // At sign-in the app can start before the service; look again in a minute instead of staying amber for an hour.
        if (_vm is { IsServiceReady: false })
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            await CheckInBackgroundAsync();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow { DataContext = _vm };
            MainWindow = _window;
            _window.Closing += (_, e) =>
            {
                if (_exiting) return;
                if (_tray is null)
                {
                    // Demo: no tray, closing the window quits.
                    Shutdown();
                    return;
                }
                // Closing the window keeps Zenith in the tray; "Sair do Zenith" in the tray menu quits.
                e.Cancel = true;
                _window.Hide();
                if (!AppSettings.TrayHintShown)
                {
                    _tray.Notify(Strings.TrayStillRunningTitle, Strings.TrayStillRunning);
                    AppSettings.TrayHintShown = true;
                }
            };
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ScanNow()
    {
        ShowWindow();
        if (_vm is { IsBusy: false, IsScanning: false }) _ = _vm.RescanAsync(showScanning: true);
    }

    private void Quit()
    {
        _exiting = true;
        Shutdown();
    }

    /// <summary>Every hour: with the window hidden, a full quiet check; with it open, only the alerts, so the cards do not move under the user.</summary>
    private async Task CheckInBackgroundAsync()
    {
        if (_vm is null) return;
        if (_window is { IsVisible: true }) await _vm.RefreshAlertsAsync();
        else await _vm.BackgroundCheckAsync();
        NotifyAlerts();
    }

    /// <summary>Shows a critical alert once per value: the same disk with the same error count is not repeated.</summary>
    private void NotifyAlerts()
    {
        if (_tray is null || _vm is null) return;
        var next = _vm.Alerts.FirstOrDefault(a => !AppSettings.AlertShown(a.Key, a.Value));
        if (next is null) return;
        _tray.Notify(next.Title, next.Text, warning: true);
        AppSettings.MarkAlertShown(next.Key, next.Value);
    }

    /// <summary>Renders the main states off screen, without animations, so CI can show what the screens look like.</summary>
    private static async Task CaptureAsync(MainViewModel vm, string folder, bool demo)
    {
        Directory.CreateDirectory(folder);
        Motion.Enabled = false;
        var view = new ShellView { DataContext = vm };

        try
        {
            // The last frame of the opening.
            var splash = new SplashWindow();
            Save((FrameworkElement)splash.Content, folder, "00-abertura.png", 480, 300);
            splash.Close();
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "abertura-erro.txt"), ex.ToString());
        }

        Save(view, folder, "00-varrendo.png");
        await vm.StartAsync();
        Save(view, folder, "01-achados.png");

        vm.Page = AppPage.Journal;
        Save(view, folder, "02-resgatados.png");
        vm.Page = AppPage.Findings;

        vm.OpenGuide(GuideKind.Bios);
        Save(view, folder, "03-guia-bios.png");
        vm.CloseOverlays();

        SaveTrayIcons(folder);

        vm.ShowHdrTrialPreview();
        Save(view, folder, "04-teste-hdr.png");
        vm.CloseOverlays();

        if (demo)
        {
            // After "Ativar" on the refresh rate and the power plan.
            await vm.ApplyForScreenshotAsync([Core.Findings.FindingIds.RefreshRate, Core.Findings.FindingIds.PowerPlan]);
            Save(view, folder, "05-depois-de-ativar.png");
            vm.Page = AppPage.Journal;
            Save(view, folder, "06-resgatados-depois.png");
        }
    }

    /// <summary>The tray icon in each state, on a dark and a light taskbar, at 48 px (a 16 px icon at 300 %).</summary>
    private static void SaveTrayIcons(string folder)
    {
        const int tile = 72, size = 48;
        TrayStatus[] states = [TrayStatus.Rest, TrayStatus.Active, TrayStatus.Alert];
        using var sheet = new System.Drawing.Bitmap(tile * states.Length, tile * 2);
        using var g = System.Drawing.Graphics.FromImage(sheet);
        using var darkTaskbar = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x20, 0x20, 0x20));
        using var lightTaskbar = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xF3, 0xF3, 0xF3));
        foreach (var (light, row) in new[] { (false, 0), (true, 1) })
        {
            g.FillRectangle(light ? lightTaskbar : darkTaskbar, 0, row * tile, sheet.Width, tile);
            for (var i = 0; i < states.Length; i++)
            {
                using var icon = TrayIconRenderer.Draw(states[i], light, size);
                g.DrawImageUnscaled(icon, i * tile + (tile - size) / 2, row * tile + (tile - size) / 2);
            }
        }
        sheet.Save(Path.Combine(folder, "07-bandeja.png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void Save(FrameworkElement view, string folder, string name, int width = 1280, int height = 1240)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name));
        png.Save(file);
    }
}
