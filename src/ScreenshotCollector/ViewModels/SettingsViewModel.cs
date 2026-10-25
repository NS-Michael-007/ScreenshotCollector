using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenshotCollector.Models;
using ScreenshotCollector.Services;

namespace ScreenshotCollector.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    /// <summary>
    /// 保存设置时触发的事件，主界面(MainViewModel)需要订阅这个事件来实现服务的“热更新”
    /// </summary>
    public Action? OnSaveRequested;

    [ObservableProperty]
    private string _saveRootFolder = "";

    public ObservableCollection<CaptureSourceSetting> CaptureSources { get; } = new();

    [ObservableProperty] private bool _enableDateSubFolder = true;
    [ObservableProperty] private decimal? _minWidth;
    [ObservableProperty] private decimal? _minHeight;
    [ObservableProperty] private bool _enableDedupe = true;
    [ObservableProperty] private decimal? _clipboardCooldownMs = 2000;
    [ObservableProperty] private bool _showCaptureNotice = true;
    [ObservableProperty] private bool _frontWindowCheck = true;
    [ObservableProperty] private decimal? _logRetentionDays = 7;
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private decimal? _thumbWidth = 220;

    private bool _isAutoStart;
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

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public int SocketPort => 23333;
    public string LogFolder => LogService.LogDirectory;

    public SettingsViewModel()
    {
        // 1. 初始化 UI 默认的捕获源（如果在初始化前 Load 配置，可能会抛异常，所以先初始化集合）
        InitDefaultCaptureSources();

        // 2. 从本地 JSON 读取配置并覆盖 UI
        LoadSettings();

        // 3. 读取开机自启状态
        CheckAutoStartStatus();
    }

    private void InitDefaultCaptureSources()
    {
        CaptureSources.Add(new() { Name = "天际线游戏截图", Hint = "（默认 Shift+F12）", DefaultHotkey = "Shift+F12", Hotkey = "Shift+F12", IsEnabled = true });
        CaptureSources.Add(new() { Name = "天际线编辑器快照", Hint = "（游戏内快照按钮）", DefaultHotkey = "", Hotkey = "", IsEnabled = true });
        CaptureSources.Add(new() { Name = "Steam F12截图", Hint = "（默认 F12）", DefaultHotkey = "F12", Hotkey = "F12", IsEnabled = true });
        CaptureSources.Add(new() { Name = "微信截图", Hint = "（默认 Alt+A）", DefaultHotkey = "Alt+A", Hotkey = "Alt+A", IsEnabled = true });
        CaptureSources.Add(new() { Name = "QQ截图", Hint = "（默认 Ctrl+Alt+A）", DefaultHotkey = "Ctrl+Alt+A", Hotkey = "Ctrl+Alt+A", IsEnabled = true });
    }

    private void LoadSettings()
    {
        var config = ConfigService.Load();

        // 如果没有保存过路径，给个默认图片文件夹
        SaveRootFolder = string.IsNullOrWhiteSpace(config.SaveRootFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenshotCollector")
            : config.SaveRootFolder;

        EnableDateSubFolder = config.EnableDateSubFolder;
        MinWidth = config.MinWidth;
        MinHeight = config.MinHeight;
        EnableDedupe = config.EnableDedupe;
        ClipboardCooldownMs = config.ClipboardCooldownMs;
        ShowCaptureNotice = config.ShowCaptureNotice;
        FrontWindowCheck = config.FrontWindowCheck;
        LogRetentionDays = config.LogRetentionDays;
        IsDarkTheme = config.IsDarkTheme;
        ThumbWidth = config.ThumbWidth;

        // 恢复各截图源状态
        foreach (var source in CaptureSources)
        {
            if (config.CaptureSources.TryGetValue(source.Name, out var savedSource))
            {
                source.IsEnabled = savedSource.IsEnabled;
                if (!string.IsNullOrEmpty(savedSource.Hotkey))
                {
                    source.Hotkey = savedSource.Hotkey;
                }
            }
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        // 1. 组装数据
        var config = new AppConfig
        {
            SaveRootFolder = this.SaveRootFolder,
            EnableDateSubFolder = this.EnableDateSubFolder,
            MinWidth = this.MinWidth,
            MinHeight = this.MinHeight,
            EnableDedupe = this.EnableDedupe,
            ClipboardCooldownMs = this.ClipboardCooldownMs,
            ShowCaptureNotice = this.ShowCaptureNotice,
            FrontWindowCheck = this.FrontWindowCheck,
            LogRetentionDays = this.LogRetentionDays,
            IsDarkTheme = this.IsDarkTheme,
            ThumbWidth = this.ThumbWidth
        };

        foreach (var source in CaptureSources)
        {
            config.CaptureSources[source.Name] = new CaptureSourceConfig
            {
                IsEnabled = source.IsEnabled,
                Hotkey = source.Hotkey
            };
        }

        // 2. 存盘
        ConfigService.Save(config);

        // 3. 通知主 ViewModel 去热更新后台服务！
        OnSaveRequested?.Invoke();

        LogService.Info("Settings", "设置已保存并触发热更新");
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.MainWindow;
            if (window != null)
            {
                var result = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "请选择截图保存目录",
                    AllowMultiple = false
                });

                if (result != null && result.Count > 0)
                {
                    SaveRootFolder = result[0].Path.LocalPath;
                }
            }
        }
    }

    [RelayCommand]
    private void ResetConfig()
    {
        foreach (var source in CaptureSources)
        {
            source.ResetHotkeyCommand.Execute(null);
            source.IsEnabled = true;
        }

        SaveRootFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenshotCollector");
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

        SaveSettings(); // 重置后立即保存并触发事件
    }

    private void CheckAutoStartStatus()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            _isAutoStart = key?.GetValue("ScreenshotCollector") != null;
            OnPropertyChanged(nameof(IsAutoStart));
        }
        catch
        {
            _isAutoStart = false;
        }
    }
}