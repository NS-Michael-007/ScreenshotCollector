using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenshotCollector.Services;

/// <summary>
/// 窗口唤起工具：找到指定进程的主窗口并恢复到前台（分享到微信/QQ 用）。
/// </summary>
public static class WindowActivator
{
    /// <summary>找到指定进程名的可见主窗口，恢复显示并置前；返回是否成功</summary>
    public static bool Activate(string processName)
    {
        var hwnd = FindMainWindow(processName);
        if (hwnd == IntPtr.Zero) return false;

        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
        return true;
    }

    private static IntPtr FindMainWindow(string processName)
    {
        IntPtr result = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true; // 跳过隐藏窗口

            GetWindowThreadProcessId(hwnd, out var pid);
            try
            {
                var name = Process.GetProcessById((int)pid).ProcessName;
                if (name.Equals(processName, StringComparison.OrdinalIgnoreCase))
                {
                    result = hwnd;
                    return false; // 找到即停止
                }
            }
            catch
            {
                // 进程可能已退出，继续找下一个
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
