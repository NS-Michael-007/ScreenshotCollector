using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

            // 启动画面：先显示 Splash（品牌背景图 + Logo），延时后自动切到主窗口。
            // 用 DispatcherTimer 而非 async/await：在游戏等独占前台应用运行时，
            // 不依赖 SynchronizationContext 恢复，杜绝"卡在启动画面"的隐患。
            var splash = new SplashWindow();
            splash.Show();
            LogService.Info("App", "启动画面已显示");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    LogService.Info("App", "启动画面结束，准备启动服务与主窗口");
                    if (desktop.MainWindow is not null)
                    {
                        // 服务先行：剪贴板/热键/托盘先就绪（不依赖窗口显示）；
                        // 即使窗口显示被系统阻塞，托盘仍然可用，不会"假死"
                        if (desktop.MainWindow.DataContext is MainViewModel vm)
                        {
                            vm.StartCaptureServices(desktop.MainWindow);
                            LogService.Info("App", "捕获服务已全部启动");
                        }
                        desktop.MainWindow.Show();
                        LogService.Info("App", "主窗口已显示");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error("App", "启动主窗口/服务失败：" + ex.Message);
                }
                splash.Close();
            };
            timer.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
