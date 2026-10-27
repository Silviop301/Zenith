using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Zenith.App.Controls;

/// <summary>
/// Keeps a panel in view while the page scrolls, like CSS "position: sticky": it follows the scroll
/// <see cref="GapProperty"/> pixels below the top of the page and never leaves its parent. It is also kept no taller
/// than the visible area, so its own content scrolls instead (see <see cref="NestedScroll"/>).
/// </summary>
public static class Sticky
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.RegisterAttached(
        "Gap", typeof(double), typeof(Sticky), new PropertyMetadata(double.NaN, OnGapChanged));

    public static double GetGap(DependencyObject o) => (double)o.GetValue(GapProperty);
    public static void SetGap(DependencyObject o, double value) => o.SetValue(GapProperty, value);

    private static void OnGapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.OldValue is double old && !double.IsNaN(old)) return;
        element.RenderTransform = new TranslateTransform();
        // Hooked on the first layout rather than on Loaded, which never comes when the page is drawn off screen (screenshots).
        ScrollViewer? page = null;
        void Hook()
        {
            if (page is not null) return;
            page = FindAncestor<ScrollViewer>(element);
            if (page is not null) page.ScrollChanged += (_, _) => Update(element, page);
        }
        element.SizeChanged += (_, _) => { Hook(); Update(element, page); };
        element.IsVisibleChanged += (_, _) => { Hook(); Update(element, page); };
    }

    private static void Update(FrameworkElement element, ScrollViewer? page)
    {
        var shift = (TranslateTransform)element.RenderTransform;
        // Sizes, not IsVisible: IsVisible stays false when the page is drawn off screen (screenshots), and a hidden page has no size.
        if (page is null || page.ActualHeight <= 0 || element.ActualHeight <= 0 ||
            VisualTreeHelper.GetParent(element) is not FrameworkElement parent || parent.ActualHeight <= 0)
        {
            shift.Y = 0;
            return;
        }

        var gap = GetGap(element);
        element.MaxHeight = Math.Max(240, page.ViewportHeight - 2 * gap);

        // Positions in the page's visible area, without the shift already applied.
        var top = element.TransformToAncestor(page).Transform(new Point()).Y - shift.Y;
        var parentBottom = parent.TransformToAncestor(page).Transform(new Point(0, parent.ActualHeight)).Y;
        var room = Math.Max(0, parentBottom - top - element.ActualHeight);
        shift.Y = Math.Clamp(gap - top, 0, room);
    }

    internal static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var o = VisualTreeHelper.GetParent(child); o is not null; o = VisualTreeHelper.GetParent(o))
            if (o is T found) return found;
        return null;
    }
}

/// <summary>
/// A scroll area inside a scrolling page: the mouse wheel moves the page once this area reaches its top or bottom,
/// and it returns to the top when it shows something else (another fiche).
/// </summary>
public static class NestedScroll
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(NestedScroll), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scroll || e.NewValue is not true) return;
        scroll.PreviewMouseWheel += (_, args) =>
        {
            var atTop = scroll.VerticalOffset <= 0;
            var atBottom = scroll.VerticalOffset >= scroll.ScrollableHeight;
            if (!(args.Delta > 0 && atTop || args.Delta < 0 && atBottom)) return;
            args.Handled = true;
            if (Sticky.FindAncestor<ScrollViewer>(scroll) is { } page)
                page.RaiseEvent(new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta) { RoutedEvent = UIElement.MouseWheelEvent });
        };
        scroll.DataContextChanged += (_, _) => scroll.ScrollToTop();
    }
}
