namespace ScreenshotCollector.ViewModels;

/// <summary>
/// 左侧菜单项（阶段4）：显示名 + 对应截图来源 + 可选小字说明。
/// 点击菜单后，列表按 SourceLabel 过滤；null 表示「全部截图」。
/// </summary>
public class MenuItem
{
    /// <summary>菜单显示文字（含图标 emoji）</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>菜单项下方的小字说明（如「（F12键截图文件）」），无则隐藏</summary>
    public string? SubText { get; init; }

    /// <summary>是否显示小字说明</summary>
    public bool HasSubText => !string.IsNullOrEmpty(SubText);

    /// <summary>
    /// 对应截图来源标签：
    /// null = 全部截图；"天际线" / "编辑器快照" / "微信" / "QQ" 按来源过滤。
    /// 「设置」菜单该项为 null 且 IsSettings = true。
    /// </summary>
    public string? SourceLabel { get; init; }

    /// <summary>是否为「设置」菜单（点击后切换到设置面板）</summary>
    public bool IsSettings { get; init; }
}
