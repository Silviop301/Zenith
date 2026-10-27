using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using Zenith.Core.Presentation;
using Zenith.Core.Resources;
using Forms = System.Windows.Forms;

namespace Zenith.App.Tray;

/// <summary>
/// The icon next to the clock. Click opens the app; right click shows a small menu: open, check now,
/// start with Windows, quit. The top dot follows <see cref="TrayState"/>.
/// </summary>
public sealed partial class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon = new();
    private readonly ContextMenu _menu;
    private readonly MenuItem _startup;
    private readonly MenuItem _update;
    private readonly Separator _updateSeparator;
    private Action? _installUpdate;
    private System.Drawing.Icon? _current;
    private TrayStatus _status = TrayStatus.Rest;

    public TrayIcon(Action open, Action scan, Action exit)
    {
        _menu = new ContextMenu { Style = Style("TrayMenu"), Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        _update = Item("", () => _installUpdate?.Invoke());
        _updateSeparator = new Separator { Style = Style("TrayMenuSeparator") };
        _menu.Items.Add(_update);
        _menu.Items.Add(_updateSeparator);
        SetUpdate(null, null);
        _menu.Items.Add(Item(Strings.TrayMenuOpen, open));
        _menu.Items.Add(Item(Strings.TrayMenuScan, scan));
        _menu.Items.Add(new Separator { Style = Style("TrayMenuSeparator") });
        _startup = Item(Strings.TrayMenuStartup, () => AppSettings.StartWithWindows = !AppSettings.StartWithWindows);
        _menu.Items.Add(_startup);
        _menu.Items.Add(new Separator { Style = Style("TrayMenuSeparator") });
        _menu.Items.Add(Item(Strings.TrayMenuExit, exit));
        _menu.Opened += (_, _) =>
        {
            // Without this the menu stays open after a click elsewhere.
            if (PresentationSource.FromVisual(_menu) is HwndSource source) SetForegroundWindow(source.Handle);
        };

        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) open();
            else if (e.Button == Forms.MouseButtons.Right)
            {
                _startup.IsChecked = AppSettings.StartWithWindows;
                _menu.IsOpen = true;
            }
        };
        _icon.BalloonTipClicked += (_, _) => open();
        SystemEvents.UserPreferenceChanged += OnThemeChanged;

        Render();
        _icon.Text = Strings.TrayScanning;
        _icon.Visible = true;
    }

    public void Update(TrayState state)
    {
        // NotifyIcon.Text is limited to 127 characters.
        _icon.Text = state.Tooltip.Length > 127 ? state.Tooltip[..127] : state.Tooltip;
        if (state.Status == _status) return;
        _status = state.Status;
        Render();
    }

    /// <summary>Shows "Instalar a versão x" at the top of the menu while an update is ready.</summary>
    public void SetUpdate(string? version, Action? install)
    {
        _installUpdate = install;
        _update.Header = version is null ? "" : string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.TrayMenuUpdate, version);
        _update.Visibility = _updateSeparator.Visibility = version is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A Windows notification from the tray icon. Clicking it opens the app.</summary>
    public void Notify(string title, string text, bool warning = false) =>
        _icon.ShowBalloonTip(10_000, title, text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);

    private void Render()
    {
        var size = Forms.SystemInformation.SmallIconSize.Width;
        var previous = _current;
        _current = TrayIconRenderer.CreateIcon(_status, TrayIconRenderer.LightTaskbar(), size);
        _icon.Icon = _current;
        if (previous is not null) TrayIconRenderer.Destroy(previous);
    }

    private void OnThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
            Application.Current?.Dispatcher.BeginInvoke(Render);
    }

    private static MenuItem Item(string header, Action click)
    {
        var item = new MenuItem { Header = header, Style = Style("TrayMenuItem") };
        item.Click += (_, _) => click();
        return item;
    }

    private static Style Style(string key) => (Style)Application.Current.FindResource(key);

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnThemeChanged;
        _icon.Visible = false;
        _icon.Dispose();
        if (_current is not null) TrayIconRenderer.Destroy(_current);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);
}
