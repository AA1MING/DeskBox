using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DeskBox;
using DeskBox.Interop;
using DeskBox.Services;

internal static class MotionChecks
{
    internal static async Task Run(MainWindow main, string output, Action<bool, string> check)
    {
        VerifyMotionContract(check);

        main.HidePanel(); await Task.Delay(250);
        var settings = new SettingsWindow();
        var surface = (FrameworkElement)settings.FindName("SettingsSurface");
        var viewer = (ScrollViewer)settings.FindName("SettingsContent");
        bool cloakedBeforeRender = false, rendered = false;
        var firstRender = new TaskCompletionSource();
        int visibleFrames = 0;
        bool blankFrame = false;
        Size firstSize = default;
        bool sizeChanged = false;
        settings.Loaded += (_, _) =>
        {
            Native.DwmGetWindowAttribute(new WindowInteropHelper(settings).Handle, 14, out int cloak, 4);
            cloakedBeforeRender = cloak != 0;
        };
        settings.ContentRendered += (_, _) =>
        {
            if (rendered) return;
            rendered = true;
            firstSize = settings.RenderSize;
            firstRender.TrySetResult();
        };
        EventHandler frame = (_, _) =>
        {
            if (!settings.IsVisible || !rendered) return;
            Native.DwmGetWindowAttribute(new WindowInteropHelper(settings).Handle, 14, out int cloak, 4);
            if (cloak != 0) return;
            visibleFrames++;
            blankFrame |= surface.Opacity < .85 || viewer.Opacity < .99;
            sizeChanged |= settings.RenderSize != firstSize;
        };
        CompositionTarget.Rendering += frame;
        try
        {
            settings.Show(); await firstRender.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(30);
            Diagnostics.CaptureWindow(settings, Path.Combine(output, "settings-entry.png"));
            await Task.Delay(350);
            check(cloakedBeforeRender && rendered && visibleFrames > 0, "settings waits for the first rendered frame before revealing");
            check(!blankFrame && !sizeChanged, "settings entry never shows an empty surface or changes window size");
            var scale = ((TransformGroup)surface.RenderTransform).Children.OfType<ScaleTransform>().Single();
            check(Math.Abs(scale.ScaleX - 1) < .001 && Math.Abs(scale.ScaleY - 1) < .001, "window spring settles at native text size");
            Diagnostics.CaptureWindow(settings, Path.Combine(output, "settings-settled.png"));

            viewer.ScrollToVerticalOffset(viewer.ScrollableHeight / 2); await Task.Delay(100);
            double before = viewer.VerticalOffset;
            Wheel(viewer, -120); await Task.Delay(70);
            double turning = viewer.VerticalOffset;
            check(turning > before, "smooth scrolling responds without a slow start");
            Wheel(viewer, 120); await Task.Delay(70);
            check(viewer.VerticalOffset < turning, "reversing the wheel immediately reverses movement");
            for (int i = 0; i < 8; i++) Wheel(viewer, -120);
            await Task.Delay(300);
            check(viewer.VerticalOffset >= 0 && viewer.VerticalOffset <= viewer.ScrollableHeight, "repeated wheel input remains within content bounds");
            var scrollBar = (System.Windows.Controls.Primitives.ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
            var viewportWidth = viewer.ViewportWidth;
            var barSize = scrollBar.RenderSize;
            await Task.Delay(950);
            check(scrollBar.Opacity < .01, "scrollbar hides after a short idle interval");
            Wheel(viewer, 120); await Task.Delay(300);
            check(scrollBar.Opacity > .99, "scrolling reveals the scrollbar again");
            scrollBar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
            await Task.Delay(950);
            check(scrollBar.Opacity > .99, "hover keeps the scrollbar available for dragging");
            Mouse.Capture(scrollBar, CaptureMode.SubTree);
            scrollBar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
            await Task.Delay(950);
            check(scrollBar.IsMouseCaptureWithin && scrollBar.Opacity > .99, "mouse capture keeps scrollbar visible outside its bounds");
            Mouse.Capture(null);
            await Task.Delay(950);
            check(scrollBar.Opacity < .01 && viewer.ViewportWidth == viewportWidth && scrollBar.RenderSize == barSize,
                "scrollbar hides after release without shifting the content layout");
            settings.Close(); await Task.Delay(65);
            check(settings.IsVisible && surface.Opacity > .85 && viewer.Opacity == 1, "closing keeps readable content until native dismissal");
            await Task.Delay(250);
            check(!settings.IsVisible && SettingsWindow.Current == null, "settings exit completes without an empty lingering window");
        }
        finally
        {
            CompositionTarget.Rendering -= frame;
            if (settings.IsVisible) { settings.Close(); await Task.Delay(250); }
        }

        var immediate = new SettingsWindow();
        immediate.Loaded += (_, _) => immediate.Close();
        immediate.Show(); await Task.Delay(350);
        check(!immediate.IsVisible && SettingsWindow.Current == null, "closing during window preparation cannot reveal it later");

        var earlyDialog = (InputDialog)Activator.CreateInstance(typeof(InputDialog), nonPublic: true)!;
        earlyDialog.Loaded += (_, _) => earlyDialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(earlyDialog), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        bool timedOut = false;
        var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timeout.Tick += (_, _) => { timedOut = true; timeout.Stop(); earlyDialog.Close(); };
        timeout.Start();
        var earlyResult = earlyDialog.ShowDialog();
        timeout.Stop();
        check(earlyResult != true && !timedOut, "Escape during dialog preparation completes dismissal without restarting entry");

        main.ShowPanel(); await Task.Delay(300);
        var button = (Button)main.FindName("SettingsButton");
        var buttonScale = ((TransformGroup)button.RenderTransform).Children.OfType<ScaleTransform>().Single();
        var originalSize = button.RenderSize;
        button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
        await Task.Delay(250);
        check(buttonScale.ScaleX > 1.02 && button.RenderSize == originalSize && button.Opacity == 1, "button hover springs without dimming text or moving layout");
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        await Task.Delay(120);
        check(buttonScale.ScaleX < 1 && button.Opacity == 1, "button press gives immediate compression feedback");
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
        button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
        await Task.Delay(250);
        check(Math.Abs(buttonScale.ScaleX - 1) < .001, "interrupted button feedback returns smoothly to rest");
    }

    private static void VerifyMotionContract(Action<bool, string> check)
    {
        // Panel entry is a deliberate, decelerating arrival.
        check(Motion.EnterDuration.TotalMilliseconds is >= 200 and <= 260, "panel entry stays in the 200-260 ms band");
        check(Motion.ExitDuration < Motion.EnterDuration, "panel exit is quicker than entry");
        check(Motion.ExitDuration.TotalMilliseconds is >= 120 and <= 200, "panel exit stays in the 120-200 ms band");
        check(Motion.EnterScale < 1 && Motion.ExitScale < 1, "panel scales from a slightly smaller surface");
        check(Motion.ExitOpacity == 0, "panel content fades fully out before the window is hidden");

        var enter = Motion.Tween(0, 1);
        var exit = Motion.TweenOut(0, 1);
        check(Math.Abs(enter.Duration.TimeSpan.TotalMilliseconds - Motion.EnterDuration.TotalMilliseconds) < 1, "entry tween uses the shared enter duration");
        check(Math.Abs(exit.Duration.TimeSpan.TotalMilliseconds - Motion.ExitDuration.TotalMilliseconds) < 1, "exit tween uses the shared exit duration");

        var enterEase = (EasingFunctionBase)enter.EasingFunction;
        var exitEase = (EasingFunctionBase)exit.EasingFunction;
        check(enterEase.EasingMode == EasingMode.EaseIn && exitEase.EasingMode == EasingMode.EaseIn,
            "custom bezier curves run in EaseIn mode so the shape is not mirrored");

        // Enter should already be well past halfway at 25% of the timeline (strong deceleration).
        check(enterEase.Ease(.25) > .7, "entry curve decelerates hard into place");
        // Exit should barely have moved at 25%, then leave quickly.
        check(exitEase.Ease(.25) < .1 && exitEase.Ease(1) > .99, "exit curve holds briefly then leaves quickly");
        check(Math.Abs(enterEase.Ease(1) - 1) < 1e-6 && Math.Abs(exitEase.Ease(0)) < 1e-6, "both curves hit their endpoints exactly");
    }

    private static void Wheel(ScrollViewer viewer, int delta) => viewer.RaiseEvent(
        new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
}
