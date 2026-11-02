using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DDLDesk;

internal sealed class DragGhost : Window
{
    private readonly Border frame;
    private readonly Vector contentInset;
    public DragGhost(Window owner, CardView card)
    {
        var source = card.CreateDragVisual();
        Owner = owner;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; IsHitTestVisible = false;
        var dpi = VisualTreeHelper.GetDpi(card.Root);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(source.ActualWidth * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(source.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(source); bitmap.Freeze();
        double cardWidth = source.ActualWidth + 4, cardHeight = source.ActualHeight + 4;
        // Any rotated rectangle fits inside a square of its diagonal length.
        // The extra transparent canvas belongs to this independent top-level
        // window, so neither rotation nor flight is clipped by the main view.
        Width = Height = Math.Ceiling(Math.Sqrt(cardWidth * cardWidth + cardHeight * cardHeight)) + 8;
        contentInset = new Vector((Width - cardWidth) / 2, (Height - cardHeight) / 2);
        frame = new Border { Width = cardWidth, Height = cardHeight, Child = new Image { Source = bitmap, Stretch = Stretch.Fill }, BorderBrush = Ui.Accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11), Opacity = .94 };
        var canvas = new Canvas { ClipToBounds = false };
        Canvas.SetLeft(frame, contentInset.X); Canvas.SetTop(frame, contentInset.Y);
        canvas.Children.Add(frame); Content = canvas;
        SourceInitialized += (_, _) => Native.MakeGhost(this);
        Show();
    }
    public void MoveCardTo(double x, double y)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Native.Move(this, x - contentInset.X * dpi.DpiScaleX, y - contentInset.Y * dpi.DpiScaleY);
    }
    public void SetDanger(bool danger) => frame.BorderBrush = danger ? Ui.Red : Ui.Accent;
    public void Fall(double vx, double vy)
    {
        SetDanger(true);
        var start = Native.Rect(this);
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        double gravity = 1900 * VisualTreeHelper.GetDpi(this).DpiScaleY;
        var rotation = new RotateTransform(); frame.RenderTransformOrigin = new Point(.5, .5); frame.RenderTransform = rotation;
        double spin = Math.Clamp(vx * .035, -95, 95);
        var clock = Stopwatch.StartNew();
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            double t = clock.Elapsed.TotalSeconds;
            var p = ThrowMath.Position(start.Left, start.Top, vx, vy, gravity, t);
            Native.Move(this, p.X, p.Y); rotation.Angle = spin * t;
            double margin = Math.Max(start.Right - start.Left, start.Bottom - start.Top);
            if (p.Y > bounds.Bottom + margin || p.X > bounds.Right + margin || p.X + margin < bounds.Left || t > 5)
            { CompositionTarget.Rendering -= tick; Close(); }
        };
        Closed += (_, _) => CompositionTarget.Rendering -= tick;
        CompositionTarget.Rendering += tick;
    }
}
