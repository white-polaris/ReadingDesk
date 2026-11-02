using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DDLDesk;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct PixelPoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] public struct PixelRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public PixelRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PixelPoint point);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out PixelRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    public static Point Cursor() { GetCursorPos(out var p); return new Point(p.X, p.Y); }
    public static PixelRect Rect(Window window) { GetWindowRect(new WindowInteropHelper(window).Handle, out var r); return r; }
    public static void Move(Window window, double x, double y) => SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0, 0x15);
    public static void MakeGhost(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x80 | 0x20);
    }
    public static Rect WorkArea(Window window)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(window).Handle, 2), ref info)) return SystemParameters.WorkArea;
        var a = window.PointFromScreen(new Point(info.Work.Left, info.Work.Top));
        var b = window.PointFromScreen(new Point(info.Work.Right, info.Work.Bottom));
        return new Rect(new Point(window.Left + a.X, window.Top + a.Y), new Point(window.Left + b.X, window.Top + b.Y));
    }
}
