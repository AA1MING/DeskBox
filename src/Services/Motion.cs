using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DeskBox.Services;

public static class Motion
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(300);
    public static DoubleAnimation Tween(double from, double to, Action? completed = null)
    {
        var animation = new DoubleAnimation(from, to, Duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        if (completed != null) animation.Completed += (_, _) => completed();
        return animation;
    }

    public static void Fade(UIElement element, double to, Action? completed = null) =>
        element.BeginAnimation(UIElement.OpacityProperty, Tween(element.Opacity, to, completed));

    public static void Enter(FrameworkElement element, double offset = 8)
    {
        var slide = new TranslateTransform(0, offset);
        element.RenderTransform = slide;
        element.Opacity = 0;
        Fade(element, 1);
        slide.BeginAnimation(TranslateTransform.YProperty, Tween(offset, 0));
    }

    /// <summary>Prepare native material before showing the first frame; animate only content.</summary>
    public static void AttachWindow(Window window, FrameworkElement content, bool animateClose = true)
    {
        content.Opacity = 0;
        window.SourceInitialized += (_, _) =>
        {
            BackdropService.Apply(window, ConfigService.Current.Backdrop, DeskBox.Theme.ThemeBrushes.IsDarkNow);
            BackdropService.Cloak(window, true);
        };
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            BackdropService.Cloak(window, false);
            Enter(content);
            window.Activate();
        }));
        if (!animateClose) return;
        bool closing = false;
        bool completed = false;
        window.Closing += (_, e) =>
        {
            if (completed || !window.IsVisible || Application.Current.Dispatcher.HasShutdownStarted) return;
            e.Cancel = true;
            if (closing) return;
            closing = true;
            window.IsHitTestVisible = false;
            Fade(content, 0, () => { completed = true; window.Close(); });
        };
    }

    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.RegisterAttached(
        "Interactive", typeof(bool), typeof(Motion), new PropertyMetadata(false, InteractiveChanged));
    public static bool GetInteractive(DependencyObject target) => (bool)target.GetValue(InteractiveProperty);
    public static void SetInteractive(DependencyObject target, bool value) => target.SetValue(InteractiveProperty, value);
    private static void InteractiveChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element || !(bool)e.NewValue) return;
        element.MouseEnter += (_, _) => Fade(element, 0.94);
        element.MouseLeave += (_, _) => Fade(element, 1);
        element.PreviewMouseLeftButtonDown += (_, _) => Fade(element, 0.78);
        element.PreviewMouseLeftButtonUp += (_, _) => Fade(element, element.IsMouseOver ? 0.94 : 1);
    }

    private sealed class ScrollState { public double Target; public bool Animating; }
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> ScrollStates = new();
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(Motion), new PropertyMetadata(0d, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));
    public static readonly DependencyProperty SmoothScrollProperty = DependencyProperty.RegisterAttached(
        "SmoothScroll", typeof(bool), typeof(Motion), new PropertyMetadata(false, ScrollChanged));
    public static bool GetSmoothScroll(DependencyObject target) => (bool)target.GetValue(SmoothScrollProperty);
    public static void SetSmoothScroll(DependencyObject target, bool value) => target.SetValue(SmoothScrollProperty, value);
    public static void ScrollToTop(ScrollViewer viewer)
    {
        viewer.BeginAnimation(OffsetProperty, null);
        viewer.SetValue(OffsetProperty, 0d);
        var state = ScrollStates.GetOrCreateValue(viewer);
        state.Target = 0;
        state.Animating = false;
        viewer.ScrollToTop();
    }
    private static void ScrollChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ScrollViewer viewer || !(bool)e.NewValue) return;
        viewer.PreviewMouseWheel += (_, args) =>
        {
            if (viewer.ScrollableHeight <= 0 || Keyboard.Modifiers != ModifierKeys.None) return;
            var state = ScrollStates.GetOrCreateValue(viewer);
            double start = state.Animating ? state.Target : viewer.VerticalOffset;
            double step = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : Math.Max(1, SystemParameters.WheelScrollLines) * 24;
            state.Target = Math.Clamp(start - args.Delta / 120d * step, 0, viewer.ScrollableHeight);
            viewer.BeginAnimation(OffsetProperty, Tween(viewer.VerticalOffset, state.Target, () => state.Animating = false));
            state.Animating = true;
            args.Handled = true;
        };
        viewer.PreviewMouseLeftButtonDown += (_, _) =>
        {
            double offset = viewer.VerticalOffset;
            viewer.BeginAnimation(OffsetProperty, null);
            viewer.SetValue(OffsetProperty, offset);
            ScrollStates.GetOrCreateValue(viewer).Animating = false;
        };
    }
}
