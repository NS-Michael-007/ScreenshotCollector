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
        var mutex = new Mutex(true, @"Global\ScreenshotCollector_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // 已有实例在运行：唤起它的主窗口（从托盘/后台恢复显示），而不是无声退出
            LogService.Info("App", "检测到已有实例在运行，正在唤起其主窗口…");
            ActivateExistingWindow();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    /// <summary>找到已有实例的主窗口并恢复到前台显示（双击 exe 相当于"打开程序"）</summary>
    private static void ActivateExistingWindow()
    {
        try
        {
            // 主窗口标题：截图自动收集器（MainWindow.axaml Title）
            var hwnd = FindWindow(null, "截图自动收集器");
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_RESTORE);       // 从托盘/最小化恢复
                SetForegroundWindow(hwnd);          // 置前
            }
            else
            {
                LogService.Warn("App", "未找到已有实例的主窗口句柄");
            }
        }
        catch (Exception ex)
        {
            LogService.Error("App", $"唤起已有实例窗口失败：{ex.Message}");
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // Avalonia 配置。不要移除，也不要在这之后添加其他代码行。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
