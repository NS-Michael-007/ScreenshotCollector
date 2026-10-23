using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ScreenshotCollector.ViewModels;

/// <summary>
/// 单个捕获源的设置项：启用开关 + 快捷键（可自定义替换默认值，可恢复默认）。
/// </summary>
public partial class CaptureSourceSetting : BaseViewModel
{
    /// <summary>来源显示名，如 微信截图</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>说明文字，如 （默认 Alt+A）</summary>
    public string Hint { get; init; } = string.Empty;

    /// <summary>出厂默认快捷键，如 Alt+A（编辑器快照等无快捷键的项为空）</summary>
    public string DefaultHotkey { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _hotkey = string.Empty;

    /// <summary>当前是否等于默认值（决定「恢复默认」按钮是否显示）</summary>
    public bool IsDefault => string.Equals(Hotkey, DefaultHotkey, System.StringComparison.Ordinal);

    /// <summary>快捷键被替换后，刷新「恢复默认」按钮显隐</summary>
    partial void OnHotkeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsDefault));
    }

    /// <summary>替换为默认快捷键（替换功能的恢复入口）</summary>
    [RelayCommand]
    private void ResetHotkey() => Hotkey = DefaultHotkey;
}
