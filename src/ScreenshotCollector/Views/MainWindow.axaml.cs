using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ScreenshotCollector.Services;
using ScreenshotCollector.ViewModels;

namespace ScreenshotCollector.Views;

public partial class MainWindow : Window
{
    private readonly FileArchiveService _archive = new();
    private ClipboardMonitorService? _clipboardService;
    private FolderWatcherService? _folderWatcherService;
    private SocketGameService? _socketGameService;
    private ClipboardCaptureService? _clipboardCaptureService;
    private TrayIconService? _trayIconService;
    private bool _isRealExit = false;

    public MainWindow()
    {
        // 🔥 强行加载 XAML，防止代码生成器卡死
        AvaloniaXamlLoader.Load(this);

        // 🔥 核心兜底：如果 DataContext 为空，手动补一个，防止所有绑定失效导致面板重叠
        if (DataContext == null)
        {
            DataContext = new MainViewModel();
        }

        this.Loaded += MainWindow_Loaded;
        this.Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        var viewModel = DataContext as MainViewModel;

        // 归档目录与设置保持一致（新截图与历史截图落在同一目录）
        if (viewModel != null)
        {
            _archive.SaveRootFolder = viewModel.Settings.SaveRootFolder;
        }

        // 🔥 关键接线：任何来源归档成功后，立刻把它加入界面列表（真实文件路径）
        _archive.Archived += (sourcePath, dest, category, time) =>
        {
            if (viewModel == null) return;
            viewModel.Screenshots.AddItem(dest, category, time, sourcePath);
        };

        // 🔥 启动时加载已有截图（历史截图也能显示，修复「全部截图不显示」）
        if (viewModel != null)
        {
            var existing = ScanExistingScreenshots(_archive.SaveRootFolder).ToList();
            viewModel.Screenshots.LoadExisting(existing);
            LogService.Info("UI", $"已加载历史截图 {existing.Count} 张，目录：{_archive.SaveRootFolder}");
        }

        // 游戏截图目录 + 编辑器快照监听（F12 截图 / 编辑器快照/缩略图/提示图）
        _folderWatcherService = new FolderWatcherService(_archive);
        _folderWatcherService.RegisterDefaultFolders();

        // 天际线模组推送
        _socketGameService = new SocketGameService(_archive);
        _socketGameService.Start();

        // 微信/QQ 前台时的剪贴板截图（自动归档为「微信」/「QQ」）
        _clipboardCaptureService = new ClipboardCaptureService(_archive);
        _clipboardCaptureService.Start();

        // 通用剪贴板监听：任何图片复制到剪贴板即归档为「剪贴板」
        _clipboardService = new ClipboardMonitorService();
        _clipboardService.ImageCaptured += bitmap =>
        {
            if (viewModel == null) return;
            try
            {
                var tmp = Path.Combine(Path.GetTempPath(), $"clip_{Guid.NewGuid():N}.png");
                bitmap.Save(tmp);
                _archive.Archive(tmp, "剪贴板", DateTime.Now);
                try { File.Delete(tmp); } catch { /* 临时文件清理失败不影响 */ }
            }
            catch (Exception ex)
            {
                LogService.Error("Clipboard", $"剪贴板图片归档失败：{ex.Message}");
            }
        };
        _clipboardService.Start();

        _trayIconService = new TrayIconService();
        _trayIconService.Create(
            onOpen: ShowMainWindow,
            onTogglePause: ToggleCapture,
            onExit: RealExit
        );
    }

    /// <summary>
    /// 扫描归档目录里已有的图片，构造成 ScreenshotItem 供启动加载。
    /// 目录结构：根\分类\日期\yyyyMMdd_HHmmss_分类.png；原始路径从同名 .src 元数据读取。
    /// </summary>
    private static IEnumerable<ScreenshotItem> ScanExistingScreenshots(string root)
    {
        var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp" };
        if (!Directory.Exists(root)) yield break;

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (Array.IndexOf(extensions, ext) < 0) continue; // 只认图片，跳过 .src 等元数据

            // 分类 = 文件目录的父目录名（根\分类\日期\file → 分类）
            var fileDir = Path.GetDirectoryName(file);
            var categoryDir = fileDir == null ? null : Path.GetDirectoryName(fileDir);
            var category = string.IsNullOrEmpty(categoryDir) ? "" : Path.GetFileName(categoryDir)!;
            if (string.IsNullOrWhiteSpace(category)) category = "其它";

            // 时间：归档文件名形如 yyyyMMdd_HHmmss_分类.png，优先从文件名解析，失败则用文件修改时间
            var name = Path.GetFileNameWithoutExtension(file);
            var time = File.GetLastWriteTime(file);
            if (name.Length >= 15 &&
                DateTime.TryParseExact(name.Substring(0, 15), "yyyyMMdd_HHmmss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                time = parsed;
            }

            yield return new ScreenshotItem
            {
                FileName = Path.GetFileName(file),
                FilePath = file,
                SourcePath = FileArchiveService.ReadSrc(file) ?? string.Empty,
                SourceLabel = category,
                CaptureTime = time
            };
        }
    }

    private void ShowMainWindow()
    {
        this.Show();
        this.WindowState = WindowState.Normal;
        this.Activate();
    }

    private void ToggleCapture(bool isPaused)
    {
        if (DataContext is MainViewModel viewModel) viewModel.SetPauseState(isPaused);
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (!_isRealExit)
        {
            e.Cancel = true;
            this.Hide();
        }
    }

    public void RealExit()
    {
        _isRealExit = true;
        Task.Run(() =>
        {
            try { _clipboardService?.Dispose(); } catch { }
            try { _clipboardCaptureService?.Dispose(); } catch { }
            try { _socketGameService?.Dispose(); } catch { }
            try { _folderWatcherService?.StopAll(); } catch { }
            try { _trayIconService?.Dispose(); } catch { }
        });

        Task.Delay(100).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                else
                    this.Close();
            });
        });
    }
}
