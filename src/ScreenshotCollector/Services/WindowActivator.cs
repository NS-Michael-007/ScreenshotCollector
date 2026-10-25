using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScreenshotCollector.Services;

/// <summary>
/// 窗口唤起工具：把微信/QQ 窗口恢复到前台；若程序未运行则先启动再置前。
/// 处理了 Windows「前台锁」（非前台进程直接 SetForegroundWindow 会被拒绝）的问题，
/// 通过 AttachThreadInput + BringWindowToTop + SetForegroundWindow 强制置前。
/// </summary>
public static class WindowActivator
{
    /// <summary>
    /// 确保指定应用在前台：已运行则直接置前；未运行则自动启动并等待窗口出现。
    /// <paramref name="appName"/> 传「微信」或「QQ」。
    /// </summary>
    public static bool EnsureForeground(string appName)
    {
        var processNames = appName switch
        {
            "微信" or "WeChat" or "Weixin" => new[] { "Weixin", "WeChat" },
            "QQ" or "QQNT" => new[] { "QQNT", "QQ" },
            _ => new[] { appName }
        };

        // 1. 已运行：直接唤起主窗口
        foreach (var p in processNames)
        {
            if (Activate(p)) return true;
        }

        // 2. 未运行：尝试启动应用
        var exe = FindExecutable(appName);
        if (exe == null) return false;

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch
        {
            return false;
        }

        // 3. 等待主窗口出现（最多约 2.5 秒）并置前
        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(250);
            foreach (var p in processNames)
            {
                if (Activate(p)) return true;
            }
        }

        return false;
    }

    /// <summary>找到指定进程名的可见主窗口，恢复显示并置前；返回是否成功</summary>
    public static bool Activate(string processName)
    {
        var hwnd = FindMainWindow(processName);
        if (hwnd == IntPtr.Zero) return false;

        return ForceForeground(hwnd);
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

    /// <summary>把窗口恢复并强制置前（绕过 Windows 前台锁）</summary>
    private static bool ForceForeground(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

        // 前台锁绕过：把本进程线程输入附加到目标窗口线程，再 BringWindowToTop + SetForegroundWindow
        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(hwnd, out _);

        var attached = false;
        if (currentThread != targetThread)
        {
            AttachThreadInput(currentThread, targetThread, true);
            attached = true;
        }

        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);

        if (attached)
        {
            AttachThreadInput(currentThread, targetThread, false);
        }

        return true;
    }

    /// <summary>在常见安装路径与注册表 App Paths 中查找微信/QQ 主程序，找不到返回 null</summary>
    private static string? FindExecutable(string appName)
    {
        string[] candidates = appName switch
        {
            "微信" or "WeChat" or "Weixin" => new[]
            {
                @"C:\Program Files (x86)\Tencent\Weixin\Weixin.exe",
                @"C:\Program Files\Tencent\Weixin\Weixin.exe",
                @"C:\Program Files (x86)\Tencent\WeChat\WeChat.exe",
                @"C:\Program Files\Tencent\WeChat\WeChat.exe",
                @"C:\Program Files (x86)\Tencent\WeChat\WeChatApp.exe",
                @"C:\Program Files\Tencent\WeChat\WeChatApp.exe"
            },
            "QQ" or "QQNT" => new[]
            {
                @"C:\Program Files (x86)\Tencent\QQ\Bin\QQ.exe",
                @"C:\Program Files\Tencent\QQ\Bin\QQ.exe",
                @"C:\Program Files (x86)\Tencent\QQNT\QQ.exe",
                @"C:\Program Files\Tencent\QQNT\QQ.exe",
                @"C:\Program Files (x86)\Tencent\QQ\QQ.exe",
                @"C:\Program Files\Tencent\QQ\QQ.exe"
            },
            _ => Array.Empty<string>()
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        // 注册表 App Paths（HKLM 优先，其次 HKCU）
        var exeName = appName switch
        {
            "微信" or "WeChat" or "Weixin" => "Weixin.exe",
            "QQ" or "QQNT" => "QQ.exe",
            _ => null
        };
        if (exeName != null)
        {
            foreach (var root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
            {
                try
                {
                    using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
                    var path = key?.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
                }
                catch
                {
                    // 无该注册表项则跳过
                }
            }
        }

        return null;
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

    [DllImport("user32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}
