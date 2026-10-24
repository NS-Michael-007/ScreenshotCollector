using Avalonia.Controls;

namespace ScreenshotCollector.Views;

/// <summary>
/// 主窗口：三栏布局（左菜单 + 中列表 + 右侧预览/设置）。
/// 关闭按钮 = 直接退出程序（不再拦截为最小化到托盘；后台运行交给托盘菜单/开机自启）。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
