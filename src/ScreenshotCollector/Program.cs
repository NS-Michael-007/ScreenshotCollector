using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using ScreenshotCollector.Services;

namespace ScreenshotCollector;

internal class Program
{
    // 初始化代码。不要在这里编写主要逻辑，请使用 OnFrameworkInitializationCompleted。
    [STAThread]
    public static void Main(string[] args)
    {
        // 单实例保护：防止用户重复启动导致多个进程同时抓图（重复归档、端口冲突）
        const string mutexName = @"Global\ScreenshotCollector_SingleInstance";
        var mutex = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            // 已有实例在运行：先尝试唤起它的主窗口（从托盘/后台恢复显示）
            LogService.Info("App", "检测到已有实例在运行，正在唤起其主窗口…");
            if (TryActivateExistingWindow())
            {
                return; // 唤起成功，退出本实例
            }

            // 已有实例在运行但找不到任何窗口（异常的无头状态，会一直占着单实例锁让人"点了没反应"）：
            // 结束它并作为新实例接管，保证界面一定能弹出来。
            LogService.Warn("App", "已有实例无可见窗口（异常状态），结束它后重新启动…");
            KillHeadlessInstances();
            Thread.Sleep(500);
            try
            {
                mutex.Dispose();
            }
            catch
            {
            }
            mutex = new Mutex(true, mutexName, out createdNew);
            if (!createdNew)
            {
                LogService.Warn("App", "仍有其他实例占用单实例锁，本次退出");
                return;
            }
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try
            {
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// 唤起已有实例的主窗口并恢复到前台显示（双击 exe 相当于"打开程序"）。
    /// 1) 先按窗口标题精确查找；2) 找不到则按进程 PID 枚举顶层窗口兜底。
    /// </summary>
    private static bool TryActivateExistingWindow()
    {
        try
        {
            // 主窗口标题：截图自动收集器（MainWindow.axaml Title）
            var hwnd = FindWindow(null, "截图自动收集器");
            if (hwnd == IntPtr.Zero)
            {
                // 标题不匹配/找不到时，按 PID 找它的顶层窗口（更可靠）
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("ScreenshotCollector"))
                {
                    if (p.Id == Environment.ProcessId) continue;
                    var h = FindWindowByPid(p.Id);
                    if (h != IntPtr.Zero)
                    {
                        hwnd = h;
                        break;
                    }
                }
            }

            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_RESTORE);       // 从托盘/最小化恢复
                SetForegroundWindow(hwnd);          // 置前
                return true;
            }

            LogService.Warn("App", "未找到已有实例的主窗口句柄");
            return false;
        }
        catch (Exception ex)
        {
            LogService.Error("App", $"唤起已有实例窗口失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>结束"无窗口僵尸实例"（异常状态下占着单实例锁的进程），为接管腾出空间</summary>
    private static void KillHeadlessInstances()
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("ScreenshotCollector"))
        {
            if (p.Id == Environment.ProcessId) continue;
            try
            {
                p.Kill();
                LogService.Warn("App", $"已结束无窗口实例 PID={p.Id}");
            }
            catch (Exception ex)
            {
                LogService.Error("App", $"结束无窗口实例失败（PID={p.Id}）：{ex.Message}");
            }
        }
    }

    /// <summary>按进程 PID 枚举其第一个顶层窗口（不依赖窗口标题）</summary>
    private static IntPtr FindWindowByPid(int pid)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out var wpid);
            if (wpid == pid && IsWindowVisible(hWnd))
            {
                result = hWnd;
                return false; // 找到即可停止枚举
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Avalonia 配置。不要移除，也不要在这之后添加其他代码行。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
