using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ScreenshotCollector.Services;
using ScreenshotCollector.ViewModels;
using ScreenshotCollector.Views;

namespace ScreenshotCollector;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 全局异常处理：未捕获异常写入日志（阶段8 清单项，兼作崩溃诊断）
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogService.Error("Fatal", $"未处理异常：{e.ExceptionObject}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 先注册主窗口（否则 Avalonia 生命周期会立即关闭应用）
            desktop.MainWindow = new MainWindow();

            // 启动画面：先显示 Splash（品牌背景图 + Logo），延时后自动切到主窗口
            var splash = new SplashWindow();
            splash.Show();
            _ = ShowMainAfterSplashAsync(splash, desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>启动画面展示指定时长后，打开主窗口并关闭启动画面</summary>
    private static async Task ShowMainAfterSplashAsync(SplashWindow splash, IClassicDesktopStyleApplicationLifetime desktop)
    {
        await Task.Delay(2500); // 启动画面停留时长（毫秒）

        // 主窗口的数据上下文已在 MainWindow.axaml 中声明（<vm:MainViewModel/>）
        if (desktop.MainWindow is not null)
        {
            desktop.MainWindow.Show();

            // 窗口显示后启动需要窗口对象的服务（剪贴板捕获等）
            if (desktop.MainWindow.DataContext is MainViewModel vm)
            {
                vm.StartCaptureServices(desktop.MainWindow);
            }
        }
        splash.Close();
    }
}
