using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenshotCollector.Models;
using ScreenshotCollector.Services;
using ScreenshotCollector.Views;

namespace ScreenshotCollector.ViewModels;

/// <summary>
/// 全局根 ViewModel：
/// 左侧菜单（按来源筛选）、页面切换（列表页/设置页）、暂停状态。
/// </summary>
public partial class MainViewModel : BaseViewModel
{
    /// <summary>左侧深色菜单项：点击后按来源过滤截图列表</summary>
    public ObservableCollection<MenuItem> MenuItems { get; } = new()
    {
        new() { DisplayName = "\U0001F5BC 全部截图" },
        new() { DisplayName = "\U0001F3D9 天际线游戏截图", SourceLabel = "天际线", SubText = "（F12键截图文件）" },
        new() { DisplayName = "\U0001F5FA 编辑器快照", SourceLabel = "编辑器快照" },
        new() { DisplayName = "\U0001F4C1 编辑器缩略图", SourceLabel = "编辑器缩略图" },
        new() { DisplayName = "\U0001F4A1 编辑器提示图", SourceLabel = "编辑器提示图" },
        new() { DisplayName = "\U0001F4AC 微信截图", SourceLabel = "微信" },
        new() { DisplayName = "\U0001F9E9 QQ截图", SourceLabel = "QQ" },
        new() { DisplayName = "\u2699 设置", IsSettings = true }
    };

    /// <summary>中间截图列表（含来源筛选与搜索过滤）</summary>
    public ScreenshotListViewModel Screenshots { get; } = new();

    /// <summary>设置面板</summary>
    public SettingsViewModel Settings { get; } = new();

    /// <summary>图片归档服务（保存/去重/分类）</summary>
    public FileArchiveService Archive { get; } = new();

    /// <summary>文件夹监听服务（天际线/Steam截图目录）</summary>
    public FolderWatcherService FolderWatcher { get; }

    /// <summary>剪贴板捕获服务（微信/QQ 截图，窗口显示后启动）</summary>
    public ClipboardCaptureService? ClipboardWatcher { get; private set; }

    /// <summary>天际线模组通信服务（本地Socket，接收模组推送）</summary>
    public SocketGameService Socket { get; }

    /// <summary>托盘图标服务（窗口显示后创建）</summary>
    public TrayIconService Tray { get; } = new();

    /// <summary>全局热键服务（自定义快捷键转发为原生截图快捷键）</summary>
    public HotkeyService Hotkeys { get; } = new();

    /// <summary>🚥 服务状态指示灯（左栏底部展示）</summary>
    public ObservableCollection<ServiceStatusItem> StatusItems { get; } = new();

    private Window? _window; // 主窗口引用（应用主题、托盘交互）

    /// <summary>模组连接状态检测定时器（每 5 秒探测游戏进程，让状态灯可读）</summary>
    private System.Threading.Timer? _gameCheckTimer;

    // ---- 🚀 高阶防卡顿：后台批量更新缓冲队列 ----
    private readonly List<ScreenshotItem> _pendingItems = new();
    private readonly object _pendingLock = new object();
    private bool _isFlushScheduled = false;

    // ---- 模组连接三态灯：红=断开 / 黄=等待 / 绿=已连接（真实状态驱动，非装饰） ----
    // 🚀 修复：赋予默认值（透明），消除 Nullable 警告，后续由 UpdateConnectionLights 刷新真实状态
    public IBrush ConnRedBrush { get; private set; } = Brushes.Transparent;
    public IBrush ConnYellowBrush { get; private set; } = Brushes.Transparent;
    public IBrush ConnGreenBrush { get; private set; } = Brushes.Transparent;
    public string ConnStatusText { get; private set; } = "等待模组连接";

    private static readonly IBrush _redBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush _yellowBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush _greenBrush = new SolidColorBrush(Color.Parse("#10B981"));
    private static readonly IBrush _inactiveBrush = new SolidColorBrush(Color.Parse("#CBD5E1"));

    /// <summary>按模组连接真实状态刷新三色灯（与「天际线模组通信」状态灯同一状态源）</summary>
    private void UpdateConnectionLights(ServiceStatusLevel level, string text)
    {
        ConnRedBrush = level == ServiceStatusLevel.Red ? _redBrush : _inactiveBrush;
        ConnYellowBrush = level == ServiceStatusLevel.Yellow ? _yellowBrush : _inactiveBrush;
        ConnGreenBrush = level == ServiceStatusLevel.Green ? _greenBrush : _inactiveBrush;
        ConnStatusText = text;
        OnPropertyChanged(nameof(ConnRedBrush));
        OnPropertyChanged(nameof(ConnYellowBrush));
        OnPropertyChanged(nameof(ConnGreenBrush));
        OnPropertyChanged(nameof(ConnStatusText));
    }

    /// <summary>主窗口显示后调用：启动需要窗口对象的捕获服务与托盘</summary>
    public void StartCaptureServices(Window window)
    {
        _window = window;

        // 主题联动：设置里切深色/浅色 → 应用到主窗口
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Settings.IsDarkTheme) && _window != null)
            {
                _window.RequestedThemeVariant = Settings.IsDarkTheme
                    ? Avalonia.Styling.ThemeVariant.Dark
                    : Avalonia.Styling.ThemeVariant.Light;
            }
        };

        ClipboardWatcher = new ClipboardCaptureService(Archive);
        ClipboardWatcher.Start();
        SetStatus("微信 / QQ 剪贴板", ServiceStatusLevel.Green, "监听中");

        // 全局热键：仅注册「用户自定义过（≠默认值）」的快捷键，避免与原生快捷键双重触发
        foreach (var source in Settings.CaptureSources)
        {
            if (string.IsNullOrEmpty(source.Hotkey)) continue;
            if (source.Hotkey == source.DefaultHotkey) continue; // 默认键就是原生键，不注册
            Hotkeys.Register(source.Hotkey, source.Name, source.DefaultHotkey);
        }
        Hotkeys.Start();
        SetStatus("全局热键",
            HotkeyService.RegisteredCount > 0 ? ServiceStatusLevel.Green : ServiceStatusLevel.Yellow,
            HotkeyService.RegisteredCount > 0 ? $"监听中（{HotkeyService.RegisteredCount} 个自定义键）" : "默认键无需转发");

        // 托盘：打开主面板 / 暂停继续 / 隐藏到托盘后台运行 / 退出程序（关闭窗口 = 直接退出）
        Tray.Create(
            window,
            onOpen: () => window.Show(),
            onTogglePause: TogglePause,
            onHideToTray: () => window.Hide(),
            onExit: () => window.Close());
    }

    // ---------- 页面切换 ----------

    [ObservableProperty]
    private MainPage _currentPage = MainPage.ScreenshotList;

    /// <summary>中栏截图列表是否可见（列表页时显示，设置页时隐藏）</summary>
    public bool IsListVisible => CurrentPage == MainPage.ScreenshotList;

    /// <summary>右侧设置面板是否可见</summary>
    public bool IsSettingsVisible => CurrentPage == MainPage.Settings;

    /// <summary>页面切换时，通知 UI 刷新中栏与右侧的显隐</summary>
    partial void OnCurrentPageChanged(MainPage value)
    {
        OnPropertyChanged(nameof(IsListVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
    }

    // ---------- 其他全局状态 ----------

    [ObservableProperty]
    private MenuItem? _selectedMenu;

    [ObservableProperty]
    private bool _isPaused;

    public MainViewModel()
    {
        // 1. 日志服务：清理7天前旧日志，记录启动
        LogService.Cleanup(7);
        LogService.Info("App", "程序启动");

        // 2. 🚥【关键修复】必须最先初始化状态灯，否则后面的服务事件找不到目标，状态灯会永远卡在“启动中”
        StatusItems.Add(new ServiceStatusItem { Name = "天际线游戏截图", Level = ServiceStatusLevel.Yellow, StatusText = "启动中" });
        StatusItems.Add(new ServiceStatusItem { Name = "编辑器快照", Level = ServiceStatusLevel.Yellow, StatusText = "启动中" });
        StatusItems.Add(new ServiceStatusItem { Name = "微信 / QQ 剪贴板", Level = ServiceStatusLevel.Yellow, StatusText = "等待窗口显示" });
        StatusItems.Add(new ServiceStatusItem { Name = "天际线模组通信", Level = ServiceStatusLevel.Yellow, StatusText = "等待模组连接" });
        StatusItems.Add(new ServiceStatusItem { Name = "全局热键", Level = ServiceStatusLevel.Yellow, StatusText = "启动中" });
        UpdateConnectionLights(ServiceStatusLevel.Yellow, "等待模组连接");

        // 3. 归档根目录与设置保持一致；保存设置时同步回写
        Archive.SaveRootFolder = Settings.SaveRootFolder;
        Settings.OnSaveRequested = () =>
        {
            Archive.SaveRootFolder = Settings.SaveRootFolder;
            LogService.Cleanup((int)(Settings.LogRetentionDays ?? 7));
        };

        // 4. 🚀 归档成功 → 推入缓冲队列
        Archive.Archived += (sourcePath, destPath, category, time) =>
        {
            var fileName = System.IO.Path.GetFileName(destPath);
            var newItem = new ScreenshotItem
            {
                FileName = fileName,
                FilePath = destPath,
                SourcePath = sourcePath ?? string.Empty,
                SourceLabel = category,
                CaptureTime = time
            };

            lock (_pendingLock)
            {
                _pendingItems.Add(newItem);
                if (_isFlushScheduled) return;
                _isFlushScheduled = true;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(FlushToUI, Avalonia.Threading.DispatcherPriority.Background);
        };

        // 5. 文件夹监听
        FolderWatcher = new FolderWatcherService(Archive);
        FolderWatcher.FolderStatusChanged += (category, found, path) =>
        {
            var name = category switch
            {
                "天际线" => "天际线游戏截图",
                "编辑器快照" => "编辑器快照",
                _ => category
            };
            SetStatus(name, found ? ServiceStatusLevel.Green : ServiceStatusLevel.Red, found ? "监听中" : "未找到目录");
        };
        FolderWatcher.RegisterDefaultFolders();

        // 6. Socket 通信
        Socket = new SocketGameService(Archive);
        Socket.SnapshotReceived += (category, time) =>
        {
            SetStatus("天际线模组通信", ServiceStatusLevel.Green, $"已连接 · 最近快照 {time:HH:mm:ss}");
        };
        var socketOk = Socket.Start();
        if (!socketOk)
        {
            SetStatus("天际线模组通信", ServiceStatusLevel.Red, "端口被占用");
        }

        // 7. 模组连接状态智能刷新定时器
        _gameCheckTimer = new System.Threading.Timer(_ =>
        {
            var running = System.Diagnostics.Process.GetProcessesByName("Cities").Length > 0;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var item = StatusItems.FirstOrDefault(i => i.Name == "天际线模组通信");
                if (item == null || item.Level == ServiceStatusLevel.Green) return; // 已连接则不打扰
                SetStatus("天际线模组通信", ServiceStatusLevel.Yellow,
                    running ? "游戏运行中 · 等待模组连接" : "游戏未启动 · 模组离线");
            });
        }, null, 3000, 5000);

        // 8. 🚀【关键修复】先加载历史数据，确保 UI 首次渲染时有数据
        LoadAllArchived();

        // 9. 启动补扫
        SweepGameSources();

        // 10. 🚀【关键修复】最后设置选中菜单，避免触发 ApplyFilter 时列表为空导致白屏
        SelectedMenu = MenuItems[0];
    }

    /// <summary>将缓冲区中的截图批量安全地添加到 UI 列表</summary>
    private void FlushToUI()
    {
        List<ScreenshotItem> itemsToAdd;
        lock (_pendingLock)
        {
            itemsToAdd = _pendingItems.ToList();
            _pendingItems.Clear();
            _isFlushScheduled = false;
        }

        foreach (var item in itemsToAdd)
        {
            try
            {
                // 🚀【关键修复】加入列表的同时，立即触发异步加载缩略图。不加载缩略图很多UI会导致不显示。
                _ = item.LoadThumbnailAsync();

                Screenshots.Add(item);
                LogService.Info("App", $"列表新增截图：{item.FileName}（{item.SourceLabel}，原始 {item.SourcePath}）");
            }
            catch (Exception ex)
            {
                LogService.Error("App", $"添加截图到列表失败：{item.FileName}，{ex.Message}");
            }
        }
    }

    /// <summary>更新一个状态灯条目（线程安全：自动调度到 UI 线程）</summary>
    public void SetStatus(string name, ServiceStatusLevel level, string text)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SetStatus(name, level, text));
            return;
        }

        foreach (var item in StatusItems)
        {
            if (item.Name == name)
            {
                item.Level = level;
                item.StatusText = text;
                if (name == "天际线模组通信") UpdateConnectionLights(level, text);
                return;
            }
        }
    }

    private void SweepGameSources()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var csDir = System.IO.Path.Combine(local, "Colossal Order", "Cities_Skylines");
            var screenshots = System.IO.Path.Combine(csDir, "Screenshots");
            var snapshots = System.IO.Path.Combine(csDir, "Snapshots");

            int added = 0, backfilled = 0;

            if (System.IO.Directory.Exists(screenshots))
            {
                foreach (var f in System.IO.Directory.GetFiles(screenshots, "*.png")
                    .OrderByDescending(System.IO.File.GetLastWriteTime).Take(20))
                {
                    var time = System.IO.File.GetLastWriteTime(f);
                    var dest = Archive.GetDestPath(f, "天际线", time);
                    if (System.IO.File.Exists(dest))
                    {
                        if (BackfillSourcePath(dest, f)) backfilled++;
                    }
                    else
                    {
                        if (Archive.Archive(f, "天际线", time) != null) added++;
                    }
                }
            }

            if (System.IO.Directory.Exists(snapshots))
            {
                foreach (var dir in System.IO.Directory.GetDirectories(snapshots)
                    .OrderByDescending(System.IO.File.GetLastWriteTime).Take(20))
                {
                    foreach (var f in System.IO.Directory.GetFiles(dir, "*.png"))
                    {
                        var cat = ClassifySnapshot(f);
                        var time = System.IO.File.GetLastWriteTime(f);
                        var dest = Archive.GetDestPath(f, cat, time);
                        if (System.IO.File.Exists(dest))
                        {
                            if (BackfillSourcePath(dest, f)) backfilled++;
                        }
                        else
                        {
                            if (Archive.Archive(f, cat, time) != null) added++;
                        }
                    }
                }
            }

            if (added > 0 || backfilled > 0)
            {
                LogService.Info("App", $"启动补扫：新归档 {added} 张，回填原始路径 {backfilled} 张");
            }
        }
        catch (Exception ex)
        {
            LogService.Error("App", $"启动补扫失败：{ex.Message}");
        }
    }

    private bool BackfillSourcePath(string destPath, string sourcePath)
    {
        foreach (var item in Screenshots.AllItems)
        {
            if (string.Equals(item.FilePath, destPath, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(item.SourcePath))
            {
                item.SourcePath = sourcePath;
                FileArchiveService.WriteSrc(destPath, sourcePath);
                LogService.Info("App", $"回填原始路径：{System.IO.Path.GetFileName(destPath)} → {sourcePath}");
                return true;
            }
        }

        if (!System.IO.File.Exists(destPath + ".src"))
        {
            FileArchiveService.WriteSrc(destPath, sourcePath);
        }
        return false;
    }

    private static string ClassifySnapshot(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        if (name.StartsWith("thumbnail", StringComparison.OrdinalIgnoreCase)) return "编辑器缩略图";
        if (name.StartsWith("tooltip", StringComparison.OrdinalIgnoreCase)) return "编辑器提示图";
        return "编辑器快照";
    }

    private void LoadAllArchived()
    {
        try
        {
            if (!System.IO.Directory.Exists(Archive.SaveRootFolder)) return;

            var files = System.IO.Directory.GetFiles(Archive.SaveRootFolder, "*.*",
                    System.IO.SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => System.IO.File.GetLastWriteTime(f))
                .ToList();

            var items = new List<ScreenshotItem>(files.Count);
            foreach (var file in files)
            {
                var rel = System.IO.Path.GetRelativePath(Archive.SaveRootFolder, file);
                var category = rel.Split(System.IO.Path.DirectorySeparatorChar).FirstOrDefault() ?? "未知";

                var item = new ScreenshotItem
                {
                    FileName = System.IO.Path.GetFileName(file),
                    FilePath = file,
                    SourcePath = FileArchiveService.ReadSrc(file) ?? string.Empty,
                    SourceLabel = category,
                    CaptureTime = TryParseTime(System.IO.Path.GetFileName(file)) ?? System.IO.File.GetLastWriteTime(file)
                };

                // 🚀【关键修复】历史图片载入时，立即触发缩略图加载
                _ = item.LoadThumbnailAsync();
                items.Add(item);
            }

            Screenshots.LoadExisting(items);
            if (files.Count > 0)
            {
                LogService.Info("App", $"已加载历史截图 {files.Count} 张（分类：{string.Join("/", items.Select(i => i.SourceLabel).Distinct())}）");
            }
        }
        catch (Exception ex)
        {
            LogService.Error("App", $"加载历史截图失败：{ex.Message}");
        }
    }

    private static DateTime? TryParseTime(string fileName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(fileName, @"^(\d{8})_(\d{6})");
        if (!m.Success) return null;
        return DateTime.TryParseExact(m.Value, "yyyyMMdd_HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var t)
            ? t : null;
    }

    partial void OnSelectedMenuChanged(MenuItem? value)
    {
        if (value == null) return;

        if (value.IsSettings)
        {
            CurrentPage = MainPage.Settings;
            return;
        }

        CurrentPage = MainPage.ScreenshotList;
        Screenshots.ApplyFilter(value.SourceLabel);
    }

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private void OpenSaveFolder()
    {
        try
        {
            var folder = Archive.SaveRootFolder;
            if (!System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
            }
            System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
            LogService.Info("UI", $"打开保存目录：{folder}");
        }
        catch (Exception ex)
        {
            LogService.Error("UI", $"打开保存目录失败：{ex.Message}");
        }
    }
}