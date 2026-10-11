using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace DeskBox.Services;

public static class ScrollBarActivity
{
    public static readonly DependencyProperty AutoHideProperty = DependencyProperty.RegisterAttached(
        "AutoHide", typeof(bool), typeof(ScrollBarActivity), new PropertyMetadata(false, Changed));
    public static bool GetAutoHide(DependencyObject target) => (bool)target.GetValue(AutoHideProperty);
    public static void SetAutoHide(DependencyObject target, bool value) => target.SetValue(AutoHideProperty, value);
    private static readonly ConditionalWeakTable<ScrollBar, Activity> States = new();

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollBar bar) return;
        if ((bool)args.NewValue) States.GetValue(bar, b => new Activity(b));
        else if (States.TryGetValue(bar, out var state)) { state.Dispose(); States.Remove(bar); }
    }

    private sealed class Activity : IDisposable
    {
        private readonly ScrollBar _bar;
        private readonly DispatcherTimer _idle;
        private bool _hover;

        public Activity(ScrollBar bar)
        {
            _bar = bar;
            _idle = new DispatcherTimer(DispatcherPriority.Background, bar.Dispatcher) { Interval = TimeSpan.FromMilliseconds(600) };
            _idle.Tick += OnIdle;
            bar.Loaded += OnLoaded;
            bar.Unloaded += OnUnloaded;
            bar.ValueChanged += OnValueChanged;
            bar.MouseEnter += OnEnter;
            bar.MouseLeave += OnLeave;
            bar.GotMouseCapture += OnCapture;
            bar.LostMouseCapture += OnCapture;
            Reset();
            if (bar.IsLoaded) OnLoaded(bar, new RoutedEventArgs());
        }

        private void Reset()
        {
            _idle.Stop();
            _bar.BeginAnimation(UIElement.OpacityProperty, null);
            _bar.SetCurrentValue(UIElement.OpacityProperty, 0d);
        }
        private void Show()
        {
            if (!_bar.IsLoaded) return;
            _bar.BeginAnimation(UIElement.OpacityProperty, Motion.Tween(_bar.Opacity, 1, milliseconds: 120));
            ScheduleHide();
        }
        private void ScheduleHide()
        {
            _idle.Stop();
            if (_bar.IsLoaded && !_hover && !_bar.IsMouseCaptureWithin) _idle.Start();
        }
        private void OnIdle(object? sender, EventArgs args)
        {
            _idle.Stop();
            if (!_hover && !_bar.IsMouseCaptureWithin)
                _bar.BeginAnimation(UIElement.OpacityProperty, Motion.Tween(_bar.Opacity, 0, milliseconds: 180));
        }
        private void OnLoaded(object sender, RoutedEventArgs args) { _hover = _bar.IsMouseOver; Reset(); if (_hover) Show(); }
        private void OnUnloaded(object sender, RoutedEventArgs args) => Reset();
        private void OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args) => Show();
        private void OnEnter(object sender, MouseEventArgs args) { _hover = true; Show(); }
        private void OnLeave(object sender, MouseEventArgs args) { _hover = false; ScheduleHide(); }
        private void OnCapture(object sender, MouseEventArgs args) => Show();

        public void Dispose()
        {
            Reset();
            _bar.SetCurrentValue(UIElement.OpacityProperty, 1d);
            _idle.Tick -= OnIdle;
            _bar.Loaded -= OnLoaded;
            _bar.Unloaded -= OnUnloaded;
            _bar.ValueChanged -= OnValueChanged;
            _bar.MouseEnter -= OnEnter;
            _bar.MouseLeave -= OnLeave;
            _bar.GotMouseCapture -= OnCapture;
            _bar.LostMouseCapture -= OnCapture;
        }
    }
}
