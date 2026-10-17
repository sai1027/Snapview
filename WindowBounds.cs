using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Snapview;

// Borderless WindowChrome otherwise maximizes beyond the work area and clips the footer.
internal static class WindowBounds
{
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(WindowProc);
    }

    private static nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int WmGetMinMaxInfo = 0x0024;
        if (message != WmGetMinMaxInfo) return 0;
        var monitor = MonitorFromWindow(hwnd, 2); // nearest monitor, including secondary/negative origins
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return 0;
        var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        // This message uses physical pixels; do not convert these values to WPF DIPs.
        bounds.MaxPosition = new NativePoint(info.Work.Left - info.Monitor.Left, info.Work.Top - info.Monitor.Top);
        bounds.MaxSize = new NativePoint(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        Marshal.StructureToPtr(bounds, lParam, false);
        handled = true;
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
