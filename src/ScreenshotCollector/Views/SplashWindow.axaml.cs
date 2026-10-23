using Avalonia.Controls;

namespace ScreenshotCollector.Views;

/// <summary>
/// 启动画面：显示品牌背景图、圆形Logo与「都市天际线 王四季」字样。
/// 由 App.axaml.cs 在启动时先显示，延时后自动切换主窗口。
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }
}
