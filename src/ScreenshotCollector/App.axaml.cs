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
        // 全局异常捕获
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogService.Error("Fatal", $"未处理异常：{e.ExceptionObject}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                // 🔥 唯一一次实例化 ViewModel
                var mainViewModel = new MainViewModel();

                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };

                var splash = new SplashWindow();
                splash.Show();
                LogService.Info("App", "启动画面已显示");

                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        desktop.MainWindow.Show();
                        LogService.Info("App", "主窗口已显示");
                    }
                    catch (Exception ex)
                    {
                        LogService.Error("App", "主窗口显示失败：" + ex.Message);
                    }
                    splash.Close();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                LogService.Error("Fatal", "MainViewModel 初始化失败：" + ex.ToString());
                // 兜底：即使 ViewModel 挂了，也把窗口显示出来，方便看报错
                desktop.MainWindow = new MainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}