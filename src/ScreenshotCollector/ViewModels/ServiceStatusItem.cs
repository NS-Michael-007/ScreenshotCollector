using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScreenshotCollector.ViewModels;

/// <summary>服务状态等级：绿=正常 / 黄=等待或待机 / 红=异常</summary>
public enum ServiceStatusLevel
{
    Green,
    Yellow,
    Red
}

/// <summary>
/// 🚥 服务状态指示灯条目：左侧菜单底部展示各捕获服务运行状态。
/// 绿=监听中/已连接，黄=等待/待机，红=未找到或失败。
/// </summary>
public partial class ServiceStatusItem : BaseViewModel
{
    /// <summary>服务显示名，如 天际线游戏截图</summary>
    public string Name { get; init; } = string.Empty;

    [ObservableProperty]
    private ServiceStatusLevel _level;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>状态圆点颜色（绿/黄/红）</summary>
    public IBrush StatusBrush => Level switch
    {
        ServiceStatusLevel.Green => GreenBrush,
        ServiceStatusLevel.Red => RedBrush,
        _ => YellowBrush
    };

    partial void OnLevelChanged(ServiceStatusLevel value)
    {
        OnPropertyChanged(nameof(StatusBrush));
    }

    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#10B981"));
    private static readonly IBrush YellowBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#EF4444"));
}
