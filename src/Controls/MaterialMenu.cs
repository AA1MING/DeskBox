using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DeskBox.Interop;
using DeskBox.Services;
using DeskBox.Theme;

namespace DeskBox.Controls;

/// <summary>A menu in a non-layered HWND, so the same DWM materials work in menus.</summary>
public sealed class MaterialMenu : Menu
{
    private Window? _host;
    private bool _open;
    private bool _closing;
    public PlacementMode Placement { get; set; } = PlacementMode.MousePoint;
    public UIElement? PlacementTarget { get; set; }
    public double HorizontalOffset { get; set; }
    public double VerticalOffset { get; set; }
    public bool StaysOpen { get; set; }
    public event RoutedEventHandler? Closed;
    public bool IsOpen
    {
        get => _open;
        set { if (value) Open(); else CloseMenu(); }
    }
    public MaterialMenu()
    {
        IsMainMenu = false;
        AddHandler(MenuItem.ClickEvent, new RoutedEventHandler((_, _) => CloseMenu()));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CloseMenu(); e.Handled = true; } };
    }
    private void Open()
    {
        if (_open) return;
        _open = true;
        _closing = false;
        _host = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = false, ShowInTaskbar = false, Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.Manual,
            Background = Brushes.Transparent, Content = this,
            UseLayoutRounding = true, SnapsToDevicePixels = true
        };
        var owner = PlacementTarget == null ? null : Window.GetWindow(PlacementTarget);
        if (owner?.IsVisible == true) _host.Owner = owner;
        _host.SourceInitialized += (_, _) =>
        {
            BackdropService.Apply(_host, ConfigService.Current.Backdrop, ThemeBrushes.IsDarkNow);
            BackdropService.Cloak(_host, true);
        };
        _host.Loaded += (_, _) =>
        {
            Position();
            _host.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (_host == null || _closing) return;
                BackdropService.Cloak(_host, false);
                Motion.Enter(this, 4);
                _host.Activate();
                Items.OfType<MenuItem>().FirstOrDefault(i => i.IsEnabled)?.Focus();
            }));
        };
        _host.Deactivated += (_, _) => { if (!StaysOpen) CloseMenu(); };
        _host.Closed += (_, _) => { _open = false; _host = null; Closed?.Invoke(this, new RoutedEventArgs()); };
        Opacity = 0;
        _host.Show();
    }
    private void Position()
    {
        if (_host == null) return;
        Point location;
        if (Placement == PlacementMode.Absolute) location = new Point(HorizontalOffset, VerticalOffset);
        else if (Placement != PlacementMode.MousePoint && PlacementTarget is FrameworkElement target && PresentationSource.FromVisual(target) != null)
            location = target.PointToScreen(Placement == PlacementMode.Bottom ? new Point(0, target.ActualHeight + 4) : new Point(HorizontalOffset, VerticalOffset));
        else
        {
            Native.GetCursorPos(out var cursor);
            location = new Point(cursor.X, cursor.Y);
        }
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)location.X, (int)location.Y));
        var dpi = VisualTreeHelper.GetDpi(_host);
        double width = _host.ActualWidth * dpi.DpiScaleX, height = _host.ActualHeight * dpi.DpiScaleY;
        int x = (int)Math.Clamp(location.X, screen.WorkingArea.Left, Math.Max(screen.WorkingArea.Left, screen.WorkingArea.Right - width));
        int y = (int)Math.Clamp(location.Y, screen.WorkingArea.Top, Math.Max(screen.WorkingArea.Top, screen.WorkingArea.Bottom - height));
        Native.SetWindowPos(new WindowInteropHelper(_host).Handle, Native.HWND_TOPMOST, x, y, (int)Math.Ceiling(width), (int)Math.Ceiling(height), 16);
    }
    private void CloseMenu()
    {
        if (!_open || _closing || _host == null) return;
        _closing = true;
        IsHitTestVisible = false;
        var host = _host;
        Motion.Fade(this, 0, () => { host.Content = null; host.Close(); IsHitTestVisible = true; });
    }
}
