using Avalonia.Controls;

namespace ScreenshotCollector.Views;

/// <summary>
/// 主窗口：三栏布局（左菜单 + 中列表 + 右侧预览/设置）。
/// 后置代码只做控件初始化与窗口行为（关闭时最小化到托盘）。
/// </summary>
public partial class MainWindow : Window
{
    private bool _allowClose; // 为 true 时才真正关闭（托盘「退出程序」时设置）

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    /// <summary>关闭按钮 → 最小化到托盘而不是退出（Windows 常驻工具惯例）</summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
    }

    /// <summary>允许真正关闭窗口（托盘菜单「退出程序」调用）</summary>
    public void AllowClose() => _allowClose = true;
}
