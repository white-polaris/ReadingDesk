using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DDLDesk;

// A critically damped spring keeps consecutive wheel events continuous, even
// when direction changes. No frame subscription remains once scrolling stops.
internal sealed class SmoothScroll : IDisposable
{
    private readonly ScrollViewer viewer;
    private bool running, disposed;
    private double position, target, velocity, lastTime;
    private static double Seconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public SmoothScroll(ScrollViewer viewer)
    {
        this.viewer = viewer;
        viewer.PreviewMouseWheel += Wheel;
        viewer.PreviewMouseDown += MouseDown;
        viewer.PreviewKeyDown += KeyDown;
        viewer.Unloaded += Unloaded;
        viewer.IsVisibleChanged += VisibilityChanged;
    }
    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0 || viewer.ScrollableHeight <= 0) return;
        WheelDelta(e.Delta); e.Handled = true;
    }
    public void WheelDelta(int delta)
    {
        double step = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 30;
        if (step > 0) ScrollBy(-delta / 120d * step);
    }
    public void ScrollBy(double amount)
    {
        if (disposed || !double.IsFinite(amount) || amount == 0) return;
        if (!running) { position = target = viewer.VerticalOffset; velocity = 0; }
        target = Math.Clamp(target + amount, 0, viewer.ScrollableHeight);
        if (Math.Abs(target - position) < .05 && Math.Abs(velocity) < 1) return;
        if (!running)
        {
            running = true; lastTime = Seconds;
            CompositionTarget.Rendering += Frame;
        }
    }
    private void Frame(object? sender, EventArgs e)
    {
        double now = Seconds, dt = now - lastTime;
        if (dt <= 0) return;
        lastTime = now;
        target = Math.Clamp(target, 0, viewer.ScrollableHeight);
        const double frequency = 26;
        double error = position - target;
        double decay = Math.Exp(-frequency * dt);
        double correction = (velocity + frequency * error) * dt;
        position = Math.Clamp(target + (error + correction) * decay, 0, viewer.ScrollableHeight);
        velocity = (velocity - frequency * correction) * decay;
        if (Math.Abs(position - target) < .15 && Math.Abs(velocity) < 2)
        { viewer.ScrollToVerticalOffset(target); Stop(); }
        else viewer.ScrollToVerticalOffset(position);
    }
    private void MouseDown(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? node = e.OriginalSource as DependencyObject; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : null)
            if (node is ScrollBar) { Stop(); return; }
    }
    private void KeyDown(object sender, KeyEventArgs e)
    { if (e.Key is Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Up or Key.Down) Stop(); }
    private void Unloaded(object sender, RoutedEventArgs e) => Stop();
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) { if (!viewer.IsVisible) Stop(); }
    public void Stop()
    {
        if (running) CompositionTarget.Rendering -= Frame;
        running = false; velocity = 0;
    }
    public void Dispose()
    {
        if (disposed) return;
        Stop(); disposed = true;
        viewer.PreviewMouseWheel -= Wheel; viewer.PreviewMouseDown -= MouseDown;
        viewer.PreviewKeyDown -= KeyDown; viewer.Unloaded -= Unloaded;
        viewer.IsVisibleChanged -= VisibilityChanged;
    }
}
