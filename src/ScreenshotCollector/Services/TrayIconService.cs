using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform;

namespace ScreenshotCollector.Services;

/// <summary>
/// 托盘图标服务（阶段5-5）：
/// 任务栏通知区常驻图标 + 右键菜单（打开主面板 / 暂停继续 / 隐藏到托盘后台运行 / 退出程序）；
/// 关闭窗口 = 直接退出（见 MainWindow），需要后台运行时用托盘菜单「隐藏到托盘」；
/// 提供注册表开机自启能力（开关由设置面板控制）。
/// </summary>
public class TrayIconService : IDisposable
{
    private TrayIcon? _trayIcon;

    /// <summary>创建托盘图标与右键菜单</summary>
    public void Create(Window window, Action onOpen, Action onTogglePause, Action onHideToTray, Action onExit)
    {
        try
        {
            // 图标复用 exe 图标素材
            var icon = new WindowIcon(AssetLoader.Open(
                new Uri("avares://ScreenshotCollector/Assets/Brand/app_icon_pic.ico")));

            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "截图自动收集器",
                IsVisible = true
            };

            // 右键菜单
            var menu = new NativeMenu();
            var openItem = new NativeMenuItem("打开主面板");
            openItem.Click += (_, _) => onOpen();
            var pauseItem = new NativeMenuItem("暂停捕获 / 继续捕获");
            pauseItem.Click += (_, _) => onTogglePause();
            var hideItem = new NativeMenuItem("隐藏到托盘（后台运行）");
            hideItem.Click += (_, _) => onHideToTray();
            var exitItem = new NativeMenuItem("退出程序");
            exitItem.Click += (_, _) => onExit();

            menu.Items.Add(openItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(pauseItem);
            menu.Items.Add(hideItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(exitItem);

            _trayIcon.Menu = menu;

            // 托盘图标左键点击：打开主面板
            _trayIcon.Clicked += (_, _) => onOpen();

            LogService.Info("Tray", "托盘图标已创建");
        }
        catch (Exception ex)
        {
            LogService.Error("Tray", $"托盘图标创建失败：{ex.Message}");
        }
    }

    /// <summary>设置开机自启（写入 HKCU 注册表 Run 项）</summary>
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

    /// <summary>移除托盘图标（退出程序时）</summary>
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
