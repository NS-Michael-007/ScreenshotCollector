using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenshotCollector.Services;

namespace ScreenshotCollector.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    [ObservableProperty] private bool _isSettingsVisible = false;
    [ObservableProperty] private bool _isPaused = false;

    public bool IsListVisible => !IsSettingsVisible;

    partial void OnIsSettingsVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListVisible));
    }

    public ScreenshotListViewModel Screenshots { get; }
    public SettingsViewModel Settings { get; }
    public ObservableCollection<MenuItem> MenuItems { get; } = new();

    [ObservableProperty] private MenuItem? _selectedMenu;
    public ObservableCollection<ServiceStatusItem> StatusItems { get; } = new();

    // ================== 🚥 模组连接信号灯逻辑 ==================
    public IBrush ConnRedBrush => Brushes.Red;
    public IBrush ConnYellowBrush => Brushes.Orange;
    public IBrush ConnGreenBrush => Brushes.Green;

    private bool _isConnRed = true;
    public bool IsConnRed { get => _isConnRed; set => SetProperty(ref _isConnRed, value); }

    private bool _isConnYellow = false;
    public bool IsConnYellow { get => _isConnYellow; set => SetProperty(ref _isConnYellow, value); }

    private bool _isConnGreen = false;
    public bool IsConnGreen { get => _isConnGreen; set => SetProperty(ref _isConnGreen, value); }

    [ObservableProperty] private bool _isWaitingForGame;
    [ObservableProperty] private bool _isGameConnected;
    [ObservableProperty] private string _connStatusText = "游戏未启动 · 模组离线";

    private int _moduleFailCount = 0;
    private const int MaxFailCount = 3;

    public void UpdateModuleConnectionStatus(bool isConnected)
    {
        if (isConnected)
        {
            _moduleFailCount = 0;
            SetConnectionState(ServiceStatusLevel.Green, "已连接");
        }
        else
        {
            _moduleFailCount++;
            if (_moduleFailCount >= MaxFailCount)
            {
                SetConnectionState(ServiceStatusLevel.Red, "游戏未启动 · 模组离线");
            }
            else
            {
                SetConnectionState(ServiceStatusLevel.Yellow, "正在寻找游戏...");
            }
        }
    }

    private void SetConnectionState(ServiceStatusLevel level, string text)
    {
        if (ConnStatusText == text) return;
        ConnStatusText = text;
        IsConnRed = level == ServiceStatusLevel.Red;
        IsConnYellow = level == ServiceStatusLevel.Yellow;
        IsConnGreen = level == ServiceStatusLevel.Green;
        IsGameConnected = level == ServiceStatusLevel.Green;
        IsWaitingForGame = level == ServiceStatusLevel.Yellow;
    }

    // ================== 构造函数 ==================

    public MainViewModel()
    {
        Screenshots = new ScreenshotListViewModel();
        Settings = new SettingsViewModel();

        Settings.OnSaveRequested += () => LogService.Info("UI", "设置已更新并应用");

        MenuItems.Add(new MenuItem { DisplayName = "📁 全部截图", SourceLabel = null });
        MenuItems.Add(new MenuItem { DisplayName = "🖼️ 天际线游戏截图", SubText = "(F12键截图文件)", SourceLabel = "天际线" });
        MenuItems.Add(new MenuItem { DisplayName = "📸 编辑器快照", SourceLabel = "编辑器快照" });
        MenuItems.Add(new MenuItem { DisplayName = "🖼️ 编辑器缩略图", SourceLabel = "编辑器缩略图" });
        MenuItems.Add(new MenuItem { DisplayName = "💡 编辑器提示图", SourceLabel = "编辑器提示图" });
        MenuItems.Add(new MenuItem { DisplayName = "💬 微信截图", SourceLabel = "微信" });
        MenuItems.Add(new MenuItem { DisplayName = "🐧 QQ截图", SourceLabel = "QQ" });
        MenuItems.Add(new MenuItem { DisplayName = "⚙️ 设置", IsSettings = true });

        SelectedMenu = MenuItems[0];

        StatusItems.Add(new ServiceStatusItem { Name = "天际线游戏截图", StatusText = "监听中", Level = ServiceStatusLevel.Green });
        StatusItems.Add(new ServiceStatusItem { Name = "编辑器快照", StatusText = "监听中", Level = ServiceStatusLevel.Green });
        StatusItems.Add(new ServiceStatusItem { Name = "微信/QQ 剪贴板", StatusText = "监听中", Level = ServiceStatusLevel.Green });
        StatusItems.Add(new ServiceStatusItem { Name = "天际线游戏", StatusText = "等待中", Level = ServiceStatusLevel.Yellow });

        // 🔥 监听1：点击复选框时触发
        ScreenshotItem.OnItemChecked += (item) =>
        {
            if (Screenshots != null && item != null)
            {
                Screenshots.SelectedItem = item;
            }
            CloseSettingsPanel();
        };

        // 🔥 监听2：直接点击列表行触发
        Screenshots.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ScreenshotListViewModel.SelectedItem) && Screenshots.SelectedItem != null)
            {
                CloseSettingsPanel();
            }
        };
    }

    private void CloseSettingsPanel()
    {
        if (IsSettingsVisible)
        {
            IsSettingsVisible = false;
            if (SelectedMenu?.IsSettings == true)
            {
                SelectedMenu = MenuItems[0]; // 切回全部截图
            }
        }
    }

    public void SetPauseState(bool isPaused) => IsPaused = isPaused;

    partial void OnSelectedMenuChanged(MenuItem? value)
    {
        if (value == null) return;
        IsSettingsVisible = value.IsSettings;
    }

    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;
        LogService.Info("UI", IsPaused ? "已暂停捕获" : "已恢复捕获");
    }

    [RelayCommand]
    private void OpenSaveFolder()
    {
        var path = Settings.SaveRootFolder;
        if (System.IO.Directory.Exists(path))
        {
            System.Diagnostics.Process.Start("explorer.exe", path);
        }
    }
}