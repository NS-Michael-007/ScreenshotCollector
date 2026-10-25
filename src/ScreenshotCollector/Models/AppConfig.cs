using System.Collections.Generic;

namespace ScreenshotCollector.Models;

/// <summary>
/// 纯粹的本地配置数据模型（用于 JSON 序列化，不包含 UI 通知逻辑）
/// </summary>
public class AppConfig
{
    // 基础设置
    public string SaveRootFolder { get; set; } = "";
    public bool EnableDateSubFolder { get; set; } = true;
    public bool EnableDedupe { get; set; } = true;
    public decimal? ClipboardCooldownMs { get; set; } = 2000;
    public bool ShowCaptureNotice { get; set; } = true;
    public bool FrontWindowCheck { get; set; } = true;
    public bool IsDarkTheme { get; set; } = false;
    public decimal? ThumbWidth { get; set; } = 220;
    public decimal? MinWidth { get; set; }
    public decimal? MinHeight { get; set; }
    public decimal? LogRetentionDays { get; set; } = 7;

    // 截图源状态（Key = 截图源名称，Value = 配置）
    public Dictionary<string, CaptureSourceConfig> CaptureSources { get; set; } = new();
}

/// <summary>
/// 单个截图源的配置项
/// </summary>
public class CaptureSourceConfig
{
    public bool IsEnabled { get; set; }
    public string Hotkey { get; set; } = "";
}