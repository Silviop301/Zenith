using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zenith.App.Backend;
using Zenith.App.Controls;
using Zenith.App.ViewModels;
using Zenith.App.Views;
using Zenith.Core.Presentation;
using Zenith.Core.Resources;

namespace Zenith.App;

/// <summary>
/// zenith.exe              the app, on the real PC
/// zenith.exe --demo       the app with a made-up PC (nothing is read or changed)
/// zenith.exe --screenshot &lt;folder&gt; [--demo]   renders each screen to PNG and exits (used by CI)
/// </summary>
public partial class App : Application
{
    private MainViewModel? _vm;

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

        if (shotIndex >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
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

        var window = new MainWindow { DataContext = _vm };
        MainWindow = window;
        window.Show();
        await _vm.StartAsync();
    }

    /// <summary>Renders the main states off screen, without animations, so CI can show what the screens look like.</summary>
    private static async Task CaptureAsync(MainViewModel vm, string folder, bool demo)
    {
        Directory.CreateDirectory(folder);
        Motion.Enabled = false;
        var view = new ShellView { DataContext = vm };

        Save(view, folder, "00-varrendo.png");
        await vm.StartAsync();
        Save(view, folder, "01-achados.png");

        vm.Page = AppPage.Journal;
        Save(view, folder, "02-resgatados.png");
        vm.Page = AppPage.Findings;

        vm.OpenGuide(GuideKind.Bios);
        Save(view, folder, "03-guia-bios.png");
        vm.CloseOverlays();

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

    private static void Save(FrameworkElement view, string folder, string name)
    {
        const int width = 1280, height = 1240;
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
