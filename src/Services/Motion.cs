using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DeskBox.Interop;

namespace DeskBox.Services;

/// <summary>
/// Shared motion language for DeskBox surfaces.
///
/// Easing uses Fluent-style cubic-bezier curves instead of the built-in
/// CubicEase. The fixed CubicEase shape reads mechanical at these very short
/// durations, which is what made the panel feel stiff:
///   * Enter cubic-bezier(0.1, 0.9, 0.2, 1.0) - decelerate hard into place.
///   * Exit  cubic-bezier(0.7, 0.0, 1.0, 0.5) - hold, then leave quickly.
/// Enter (220 ms) and exit (160 ms) also differ on purpose: the panel arrives
/// deliberately and leaves without making the user wait for it.
/// </summary>
public static class Motion
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(160);
    public static System.Windows.Duration FeedbackDuration => new(TimeSpan.FromMilliseconds(180));
    public static System.Windows.Duration SettleDuration => new(TimeSpan.FromMilliseconds(220));

    // Surface geometry: a small lift and scale, never a large swoop.
    public const double EnterScale = .98;
    public const double ExitScale = .98;
    public const double EnterOffset = 6;
    public const double ExitOffset = 5;
    public const double ExitOpacity = 0;

    // Shared across every tween, so keep them frozen (immutable + safe to reuse).
    private static readonly IEasingFunction EnterEase = Frozen(new CubicBezierEase(.1, .9, .2, 1));
    private static readonly IEasingFunction ExitEase = Frozen(new CubicBezierEase(.7, 0, 1, .5));

    private static T Frozen<T>(T value) where T : Freezable
    {
        if (value.CanFreeze) value.Freeze();
        return value;
    }

    public static DoubleAnimation Tween(double from, double to, Action? completed = null, int milliseconds = 220, bool spring = false)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = spring ? new BackEase { Amplitude = .35, EasingMode = EasingMode.EaseOut } : EnterEase
        };
        if (completed != null) animation.Completed += (_, _) => completed();
        return animation;
    }

    /// <summary>Exit curve: starts gently, then accelerates away.</summary>
    public static DoubleAnimation TweenOut(double from, double to, Action? completed = null, int milliseconds = 160)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = ExitEase
        };
        if (completed != null) animation.Completed += (_, _) => completed();
        return animation;
    }

    /// <summary>Cubic-bezier easing with the same parameterisation as CSS.</summary>
    private sealed class CubicBezierEase : EasingFunctionBase
    {
        private readonly double _x1, _y1, _x2, _y2;
        public CubicBezierEase(double x1, double y1, double x2, double y2)
        {
            _x1 = x1; _y1 = y1; _x2 = x2; _y2 = y2;
            // EasingFunctionBase defaults to EaseOut, which would mirror the curve.
            EasingMode = EasingMode.EaseIn;
        }
        protected override Freezable CreateInstanceCore() => new CubicBezierEase(_x1, _y1, _x2, _y2);
        protected override double EaseInCore(double normalizedTime)
        {
            // Solve x(t) = normalizedTime for t, then evaluate y(t).
            double low = 0, high = 1, t = normalizedTime;
            for (int i = 0; i < 24; i++)
            {
                t = (low + high) / 2;
                if (Bezier(t, _x1, _x2) < normalizedTime) low = t; else high = t;
            }
            return Bezier(t, _y1, _y2);
        }
        private static double Bezier(double t, double p1, double p2)
        {
            double u = 1 - t;
            return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
        }
    }

    public static void Fade(UIElement element, double to, Action? completed = null) =>
        element.BeginAnimation(UIElement.OpacityProperty, Tween(element.Opacity, to, completed));

    private sealed class SurfaceState
    {
        public bool Exiting;
        public readonly ScaleTransform Scale = new();
        public readonly TranslateTransform Slide = new();
        public SurfaceState(FrameworkElement element)
        {
            var transforms = new TransformGroup();
            if (!element.RenderTransform.Value.IsIdentity) transforms.Children.Add(element.RenderTransform);
            transforms.Children.Add(Scale); transforms.Children.Add(Slide);
            element.RenderTransform = transforms;
            element.RenderTransformOrigin = new Point(.5, .5);
        }
    }
    private static readonly ConditionalWeakTable<FrameworkElement, SurfaceState> Surfaces = new();

    public static void PrepareEnter(FrameworkElement element, double offset = EnterOffset)
    {
        var state = Surfaces.GetValue(element, e => new SurfaceState(e));
        state.Exiting = false;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1; // The first visible native frame must already contain readable content.
        state.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        state.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        state.Slide.BeginAnimation(TranslateTransform.YProperty, null);
        state.Scale.ScaleX = state.Scale.ScaleY = EnterScale;
        state.Slide.Y = offset;
    }

    public static void Enter(FrameworkElement element)
    {
        var state = Surfaces.GetValue(element, e => new SurfaceState(e));
        if (state.Exiting) return;
        Fade(element, 1);
        int ms = (int)EnterDuration.TotalMilliseconds;
        state.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Tween(state.Scale.ScaleX, 1, milliseconds: ms));
        state.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Tween(state.Scale.ScaleY, 1, milliseconds: ms));
        state.Slide.BeginAnimation(TranslateTransform.YProperty, Tween(state.Slide.Y, 0, milliseconds: ms));
    }

    public static void Exit(FrameworkElement element, Action completed)
    {
        var state = Surfaces.GetValue(element, e => new SurfaceState(e));
        state.Exiting = true;
        int ms = (int)ExitDuration.TotalMilliseconds;
        state.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, TweenOut(state.Scale.ScaleX, ExitScale, milliseconds: ms));
        state.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, TweenOut(state.Scale.ScaleY, ExitScale, milliseconds: ms));
        state.Slide.BeginAnimation(TranslateTransform.YProperty, TweenOut(state.Slide.Y, ExitOffset, milliseconds: ms));
        element.BeginAnimation(UIElement.OpacityProperty, TweenOut(element.Opacity, ExitOpacity, completed, ms));
    }
    /// <summary>Prepare native material before showing the first frame; animate only content.</summary>
    public static void AttachWindow(Window window, FrameworkElement content, bool animateClose = true)
    {
        PrepareEnter(content);
        bool closing = false;
        bool completed = false;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.SourceInitialized += (_, _) =>
        {
            BackdropService.Apply(window, ConfigService.Current.Backdrop, DeskBox.Theme.ThemeBrushes.IsDarkNow);
            BackdropService.Cloak(window, true);
        };
        // Showing the HWND can clear its initial cloak. Reapply before WPF's first render.
        window.Loaded += (_, _) => BackdropService.Cloak(window, true);
        EventHandler? rendered = null;
        rendered = (_, _) =>
        {
            window.ContentRendered -= rendered;
            if (closed || closing || Surfaces.GetValue(content, e => new SurfaceState(e)).Exiting || !window.IsVisible) return;
            Native.DwmFlush();
            BackdropService.Cloak(window, false);
            Enter(content);
            window.Activate();
        };
        window.ContentRendered += rendered;
        if (!animateClose)
        {
            window.Closing += (_, _) => closing = true;
            return;
        }
        window.Closing += (_, e) =>
        {
            if (completed || !window.IsVisible || Application.Current.Dispatcher.HasShutdownStarted) return;
            e.Cancel = true;
            if (closing) return;
            closing = true;
            window.IsHitTestVisible = false;
            Exit(content, () => { completed = true; window.Close(); });
        };
    }

    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.RegisterAttached(
        "Interactive", typeof(bool), typeof(Motion), new PropertyMetadata(false, InteractiveChanged));
    public static bool GetInteractive(DependencyObject target) => (bool)target.GetValue(InteractiveProperty);
    public static void SetInteractive(DependencyObject target, bool value) => target.SetValue(InteractiveProperty, value);
    private static void InteractiveChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element || !(bool)e.NewValue || element is not (ButtonBase or MenuItem)) return;
        var scale = new ScaleTransform();
        var transforms = new TransformGroup();
        if (!element.RenderTransform.Value.IsIdentity) transforms.Children.Add(element.RenderTransform);
        transforms.Children.Add(scale);
        element.RenderTransform = transforms;
        element.RenderTransformOrigin = new Point(.5, .5);
        double hover = element is MenuItem or CheckBox ? 1.015 : 1.035;
        void Animate(double to, bool pressed = false)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Tween(scale.ScaleX, to, milliseconds: pressed ? 100 : 200, spring: !pressed));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Tween(scale.ScaleY, to, milliseconds: pressed ? 100 : 200, spring: !pressed));
        }
        element.MouseEnter += (_, _) => Animate(Mouse.LeftButton == MouseButtonState.Pressed ? .97 : hover);
        element.MouseLeave += (_, _) => Animate(1);
        element.PreviewMouseLeftButtonDown += (_, _) => Animate(.97, true);
        element.PreviewMouseLeftButtonUp += (_, _) => Animate(element.IsMouseOver ? hover : 1);
        element.IsEnabledChanged += (_, _) => { if (!element.IsEnabled) Animate(1); };
        element.Unloaded += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = 1;
        };
    }

    private sealed class ScrollState { public double Target; public bool Animating; public int Direction; public int Version; }
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
        state.Direction = 0;
        ++state.Version;
        viewer.ScrollToTop();
    }
    private static void ScrollChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ScrollViewer viewer || !(bool)e.NewValue) return;
        viewer.PreviewMouseWheel += (_, args) =>
        {
            if (viewer.ScrollableHeight <= 0 || Keyboard.Modifiers != ModifierKeys.None) return;
            var state = ScrollStates.GetOrCreateValue(viewer);
            int direction = Math.Sign(args.Delta);
            double start = state.Animating && state.Direction == direction ? state.Target : viewer.VerticalOffset;
            double step = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : Math.Max(1, SystemParameters.WheelScrollLines) * 24;
            state.Target = Math.Clamp(start - args.Delta / 120d * step, 0, viewer.ScrollableHeight);
            int version = ++state.Version;
            state.Direction = direction;
            viewer.BeginAnimation(OffsetProperty, Tween(viewer.VerticalOffset, state.Target, () =>
            {
                if (version == state.Version) state.Animating = false;
            }));
            state.Animating = true;
            args.Handled = true;
        };
        viewer.PreviewMouseLeftButtonDown += (_, _) =>
        {
            double offset = viewer.VerticalOffset;
            viewer.BeginAnimation(OffsetProperty, null);
            viewer.SetValue(OffsetProperty, offset);
            var state = ScrollStates.GetOrCreateValue(viewer);
            state.Animating = false;
            state.Direction = 0;
            ++state.Version;
        };
    }
}
