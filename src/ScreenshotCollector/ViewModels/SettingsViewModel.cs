using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenshotCollector.Services;

namespace ScreenshotCollector.ViewModels;

/// <summary>
/// 设置面板 ViewModel（阶段6 完整版）：
/// 捕获源开关 + 快捷键录制/替换、保存归档规则、图片过滤规则、
/// 程序行为（含开机自启真实写注册表）、剪贴板高级、日志配置、外观、调试信息。
/// 点「保存设置」统一生效；「重置配置」恢复默认值。
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    /// <summary>保存设置时通知主 ViewModel 执行实际生效动作（如同步归档根目录）</summary>
    public Action? OnSaveRequested;

    /// <summary>保存根目录（默认：图片库\ScreenshotCollector）</summary>
    [ObservableProperty]
    private string _saveRootFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenshotCollector");

    /// <summary>全部捕获源设置项（启用开关 + 可录制的快捷键）</summary>
    public ObservableCollection<CaptureSourceSetting> CaptureSources { get; } = new()
    {
        new()
        {
            Name = "天际线游戏截图",
            Hint = "（默认 Shift+F12）",
            DefaultHotkey = "Shift+F12",
            Hotkey = "Shift+F12",
            IsEnabled = true
        },
        new()
        {
            Name = "天际线编辑器快照",
            Hint = "（游戏内快照按钮，无默认快捷键）",
            DefaultHotkey = "",
            Hotkey = "",
            IsEnabled = true
        },
        new()
        {
            Name = "Steam F12截图",
            Hint = "（默认 F12）",
            DefaultHotkey = "F12",
            Hotkey = "F12",
            IsEnabled = true
        },
        new()
        {
            Name = "微信截图",
            Hint = "（默认 Alt+A）",
            DefaultHotkey = "Alt+A",
            Hotkey = "Alt+A",
            IsEnabled = true
        },
        new()
        {
            Name = "QQ截图",
            Hint = "（默认 Ctrl+Alt+A）",
            DefaultHotkey = "Ctrl+Alt+A",
            Hotkey = "Ctrl+Alt+A",
            IsEnabled = true
        }
    };

    // ---------- 保存归档规则 ----------

    /// <summary>按日期自动创建子文件夹（根目录\分类\日期）</summary>
    [ObservableProperty]
    private bool _enableDateSubFolder = true;

    // ---------- 图片过滤规则 ----------

    /// <summary>最小宽度过滤（0=不限制）</summary>
    [ObservableProperty]
    private decimal? _minWidth;

    /// <summary>最小高度过滤（0=不限制）</summary>
    [ObservableProperty]
    private decimal? _minHeight;

    /// <summary>MD5 去重（相同图片只保存一份）</summary>
    [ObservableProperty]
    private bool _enableDedupe = true;

    /// <summary>剪贴板捕获冷却（毫秒，防同一张图反复保存）</summary>
    [ObservableProperty]
    private decimal? _clipboardCooldownMs = 2000;

    // ---------- 程序行为 ----------

    private bool _isAutoStart;

    /// <summary>开机自启（写入/删除 HKCU 注册表 Run 项，立即生效）</summary>
    public bool IsAutoStart
    {
        get => _isAutoStart;
        set
        {
            if (SetProperty(ref _isAutoStart, value))
            {
                TrayIconService.SetAutoStart(value);
            }
        }
    }

    /// <summary>关闭窗口时最小化到托盘（固定开启）</summary>
    public bool IsTrayOnClose => true;

    /// <summary>捕获到新截图时显示通知（预留：气泡通知阶段5-5 已具备能力）</summary>
    [ObservableProperty]
    private bool _showCaptureNotice = true;

    // ---------- 剪贴板高级 ----------

    /// <summary>仅前台为微信/QQ 时才捕获剪贴板（防止误抓其它程序复制内容）</summary>
    [ObservableProperty]
    private bool _frontWindowCheck = true;

    // ---------- 日志配置 ----------

    /// <summary>日志保留天数（自动清理更早的日志文件）</summary>
    [ObservableProperty]
    private decimal? _logRetentionDays = 7;

    // ---------- 外观 ----------

    /// <summary>深色主题（预留：当前版本界面配色固定浅色，切换后需重新设计）</summary>
    [ObservableProperty]
    private bool _isDarkTheme;

    /// <summary>列表缩略图宽度（像素）</summary>
    [ObservableProperty]
    private decimal? _thumbWidth = 220;

    // ---------- 调试信息 ----------

    /// <summary>程序版本号</summary>
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>天际线模组通信端口</summary>
    public int SocketPort => 23333;

    /// <summary>日志目录</summary>
    public string LogFolder => LogService.LogDirectory;

    public SettingsViewModel()
    {
        // 读取开机自启当前状态（注册表 Run 项）
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run");
            _isAutoStart = key?.GetValue("ScreenshotCollector") != null;
        }
        catch
        {
            _isAutoStart = false;
        }
    }

    /// <summary>保存设置：同步各服务生效参数（热键/监听目录等改动需重启程序后生效）</summary>
    [RelayCommand]
    private void SaveSettings()
    {
        OnSaveRequested?.Invoke();
        LogService.Info("Settings", "设置已保存");
    }

    /// <summary>恢复全部默认值并保存</summary>
    [RelayCommand]
    private void ResetConfig()
    {
        foreach (var source in CaptureSources)
        {
            source.ResetHotkeyCommand.Execute(null);
        }

        SaveRootFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenshotCollector");
        EnableDateSubFolder = true;
        MinWidth = null;
        MinHeight = null;
        EnableDedupe = true;
        ClipboardCooldownMs = 2000;
        FrontWindowCheck = true;
        LogRetentionDays = 7;
        IsDarkTheme = false;
        ThumbWidth = 220;
        ShowCaptureNotice = true;

        LogService.Info("Settings", "配置已重置为默认值");
        SaveSettings();
    }
}
