namespace ScreenshotCollector.Models;

/// <summary>
/// 主窗口右侧显示的页面类型（阶段2：页面切换逻辑）。
/// </summary>
public enum MainPage
{
    /// <summary>截图列表页：中栏列表显示，右侧为「信封占位 / 大图预览」</summary>
    ScreenshotList,

    /// <summary>设置面板页：中栏列表隐藏，右侧显示设置卡片</summary>
    Settings
}
