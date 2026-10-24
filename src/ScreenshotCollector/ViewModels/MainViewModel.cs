using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        // 日志服务：清理7天前旧日志，记录启动
        LogService.Cleanup(7);
        LogService.Info("App", "程序启动");

        // 归档根目录与设置保持一致；保存设置时同步回写
        Archive.SaveRootFolder = Settings.SaveRootFolder;
        Settings.OnSaveRequested = () =>
        {
            Archive.SaveRootFolder = Settings.SaveRootFolder;
            LogService.Cleanup((int)(Settings.LogRetentionDays ?? 7));
        };

        // 归档成功 → 实时添加到界面列表（截图后立刻出现在中间列表，无需重启）
        Archive.Archived += (sourcePath, destPath, category, time) =>
        {
            Screenshots.Add(new ScreenshotItem
            {
                FileName = System.IO.Path.GetFileName(destPath),
                FilePath = destPath,
                SourcePath = sourcePath ?? string.Empty,
                SourceLabel = category,
                CaptureTime = time
            });
            LogService.Info("App", $"列表新增截图：{System.IO.Path.GetFileName(destPath)}（{category}，原始 {sourcePath ?? "未知"}）");
        };

        // 文件夹监听：注册默认截图目录（天际线/编辑器快照），实时更新状态灯
        FolderWatcher = new FolderWatcherService(Archive);
        FolderWatcher.FolderStatusChanged += (category, found, path) =>
        {
            var name = category switch
            {
                "天际线" => "天际线游戏截图",
                "编辑器快照" => "编辑器快照",
                _ => category
            };
            SetStatus(name,
                found ? ServiceStatusLevel.Green : ServiceStatusLevel.Red,
                found ? "监听中" : "未找到目录");
        };
        FolderWatcher.RegisterDefaultFolders();

        // Socket 通信：接收天际线模组推送，收到快照时状态灯变绿并显示最近时间
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

        // 模组连接状态智能刷新：每 5 秒检测游戏进程是否存在，
        // 未连接时给出明确原因（游戏未启动 / 游戏运行中等待模组），避免像卡死的假象
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

        // 初始状态灯：全部等待启动 → 由上方各服务启动事件刷新
        StatusItems.Add(new ServiceStatusItem { Name = "天际线游戏截图", Level = ServiceStatusLevel.Yellow, StatusText = "启动中" });
        StatusItems.Add(new ServiceStatusItem { Name = "编辑器快照", Level = ServiceStatusLevel.Yellow, StatusText = "启动中" });
        StatusItems.Add(new ServiceStatusItem { Name = "微信 / QQ 剪贴板", Level = ServiceStatusLevel.Yellow, StatusText = "等待窗口显示" });
        StatusItems.Add(new ServiceStatusItem { Name = "天际线模组通信", Level = ServiceStatusLevel.Yellow, StatusText = "等待模组连接" });
        StatusItems.Add(new ServiceStatusItem { Name = "全局热键", Level = ServiceStatusLevel.Yellow, StatusText = "启动中" });

        SelectedMenu = MenuItems[0];
        // 演示数据已移除：列表现在实时展示真实抓取结果（抓图后自动出现在这里）
        // LoadSampleData();

        // 启动时加载归档目录中的全部历史截图（按时间降序，今天/昨天/本周/更早分组展示）
        LoadAllArchived();
    }

    /// <summary>扫描保存根目录，把全部已归档截图加入列表（按时间降序）</summary>
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
                // 分类 = 根目录下的第一层文件夹名（如 微信、天际线、QQ）
                var rel = System.IO.Path.GetRelativePath(Archive.SaveRootFolder, file);
                var category = rel.Split(System.IO.Path.DirectorySeparatorChar).FirstOrDefault() ?? "未知";

                items.Add(new ScreenshotItem
                {
                    FileName = System.IO.Path.GetFileName(file),
                    FilePath = file,
                    SourceLabel = category,
                    CaptureTime = TryParseTime(System.IO.Path.GetFileName(file)) ?? System.IO.File.GetLastWriteTime(file)
                });
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

    /// <summary>从文件名解析时间戳（20261023_124132_微信.png → 2026-10-23 12:41:32），失败返回 null</summary>
    private static DateTime? TryParseTime(string fileName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(fileName, @"^(\d{8})_(\d{6})");
        if (!m.Success) return null;
        return DateTime.TryParseExact(m.Value, "yyyyMMdd_HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var t)
            ? t : null;
    }

    /// <summary>更新一个状态灯条目</summary>
    public void SetStatus(string name, ServiceStatusLevel level, string text)
    {
        foreach (var item in StatusItems)
        {
            if (item.Name == name)
            {
                item.Level = level;
                item.StatusText = text;
                return;
            }
        }
    }

    /// <summary>左侧菜单切换：设置页或按来源过滤截图列表</summary>
    partial void OnSelectedMenuChanged(MenuItem? value)
    {
        if (value == null) return;

        // 设置菜单 → 切换到设置面板
        if (value.IsSettings)
        {
            CurrentPage = MainPage.Settings;
            return;
        }

        // 普通菜单 → 回到列表页，并按来源过滤
        CurrentPage = MainPage.ScreenshotList;
        Screenshots.ApplyFilter(value.SourceLabel);
    }

    /// <summary>暂停 / 继续捕获</summary>
    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    /// <summary>打开保存目录（真实实现：explorer 打开归档根目录）</summary>
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

    // ---------- 演示用：生成五条示例数据，覆盖四个分组与多个来源 ----------

    private void LoadSampleData()
    {
        var now = DateTime.Now;

        // 今天
        Screenshots.Add(new ScreenshotItem
        {
            FileName = "20261023_102211_WeChat.png",
            FilePath = @"D:\Screenshots\WeChat\20261023_102211_WeChat.png",
            SourceLabel = "微信",
            CaptureTime = now.AddHours(-1),
            Thumbnail = MakeThumb("#10B981")
        });

        // 昨天
        Screenshots.Add(new ScreenshotItem
        {
            FileName = "20261022_090805_QQ.png",
            FilePath = @"D:\Screenshots\QQ\20261022_090805_QQ.png",
            SourceLabel = "QQ",
            CaptureTime = now.AddDays(-1).AddHours(3),
            Thumbnail = MakeThumb("#3B82F6")
        });

        // 本周（非今天/昨天）
        Screenshots.Add(new ScreenshotItem
        {
            FileName = "20261020_095512_Steam.png",
            FilePath = @"D:\Screenshots\SteamOverlay\20261020_095512_Steam.png",
            SourceLabel = "Steam",
            CaptureTime = now.AddDays(-3),
            Thumbnail = MakeThumb("#F59E0B")
        });

        // 更早（上周）
        Screenshots.Add(new ScreenshotItem
        {
            FileName = "20261018_093012_CitiesSkylines.png",
            FilePath = @"D:\Screenshots\CitiesSkylines_Game\20261018_093012_CitiesSkylines.png",
            SourceLabel = "天际线",
            CaptureTime = now.AddDays(-6),
            Thumbnail = MakeThumb("#8B5CF6")
        });

        // 更早（上上周）
        Screenshots.Add(new ScreenshotItem
        {
            FileName = "20261013_091047_Snapshot.png",
            FilePath = @"D:\Screenshots\CitiesSkylines_Snapshot\20261013_091047_Snapshot.png",
            SourceLabel = "编辑器快照",
            CaptureTime = now.AddDays(-12),
            Thumbnail = MakeThumb("#EF4444")
        });
    }

    /// <summary>生成一块纯色小图，作为示例缩略图（演示用）</summary>
    private static Bitmap MakeThumb(string hex)
    {
        var color = Color.Parse(hex);
        var rtb = new RenderTargetBitmap(new PixelSize(64, 40));
        using var ctx = rtb.CreateDrawingContext();
        ctx.FillRectangle(new SolidColorBrush(color), new Rect(0, 0, 64, 40));
        return rtb;
    }
}
