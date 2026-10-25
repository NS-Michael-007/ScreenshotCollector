using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Controls.ApplicationLifetimes;

namespace ScreenshotCollector.Services;

/// <summary>
/// 托盘图标服务：常驻通知区 + 右键菜单（打开 / 暂停 / 隐藏 / 退出）
/// </summary>
public class TrayIconService : IDisposable
{
    private TrayIcon? _trayIcon;
    private bool _isPaused = false;

    /// <summary>
    /// 创建托盘图标与右键菜单
    /// </summary>
    /// <param name="onOpen">打开主窗口回调</param>
    /// <param name="onTogglePause">暂停/继续回调（返回当前是否处于暂停状态）</param>
    /// <param name="onExit">彻底退出回调</param>
    public void Create(Action onOpen, Action<bool> onTogglePause, Action onExit)
    {
        try
        {
            var icon = new WindowIcon(AssetLoader.Open(
                new Uri("avares://ScreenshotCollector/Assets/Brand/app_icon_pic.ico")));

            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "截图自动收集器（正在运行）",
                IsVisible = true
            };

            var menu = new NativeMenu();

            // 1. 打开主面板
            var openItem = new NativeMenuItem("打开主面板");
            openItem.Click += (_, _) => onOpen();

            // 2. 暂停 / 继续
            var pauseItem = new NativeMenuItem("暂停捕获");
            pauseItem.Click += (_, _) =>
            {
                _isPaused = !_isPaused;
                pauseItem.Header = _isPaused ? "继续捕获" : "暂停捕获";
                _trayIcon.ToolTipText = _isPaused ? "截图自动收集器（已暂停）" : "截图自动收集器（正在运行）";
                onTogglePause(_isPaused);
            };

            // 3. 隐藏到托盘
            var hideItem = new NativeMenuItem("隐藏到托盘后台");
            hideItem.Click += (_, _) =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow?.Hide();
                }
            };

            // 4. 彻底退出
            var exitItem = new NativeMenuItem("彻底退出程序");
            exitItem.Click += (_, _) => onExit();

            menu.Items.Add(openItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(pauseItem);
            menu.Items.Add(hideItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(exitItem);

            _trayIcon.Menu = menu;
            _trayIcon.Clicked += (_, _) => onOpen();

            LogService.Info("Tray", "托盘图标已创建");
        }
        catch (Exception ex)
        {
            LogService.Error("Tray", $"托盘图标创建失败：{ex.Message}");
        }
    }

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue("ScreenshotCollector", $"\"{exePath}\"");
                    LogService.Info("Tray", "已设置开机自启");
                }
            }
            else
            {
                key.DeleteValue("ScreenshotCollector", false);
                LogService.Info("Tray", "已关闭开机自启");
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Tray", $"设置开机自启失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}