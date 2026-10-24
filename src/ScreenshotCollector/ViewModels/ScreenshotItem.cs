using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ScreenshotCollector.ViewModels;

/// <summary>
/// 单条截图记录（模型）。
/// 对应中间截图列表里的一行：复选框 + 缩略图 + 文件名 + 来源 + 时间。
/// </summary>
public class ScreenshotItem : BaseViewModel
{
    /// <summary>文件名，如 20261023_102211_WeChat.png</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>本地完整路径（归档后的保存路径）</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// 来源原始路径（如游戏目录 Snapshots\&lt;UUID&gt;\snapshot.png）。
    /// 归档时记录；启动补扫按 .src 元数据恢复；无法追溯时为空字符串。
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>捕获时间</summary>
    public DateTime CaptureTime { get; init; } = DateTime.Now;

    /// <summary>来源标签，如 天际线 / Steam / 微信 / QQ</summary>
    public string SourceLabel { get; init; } = string.Empty;

    /// <summary>缩略图（列表与预览共用）</summary>
    public Bitmap? Thumbnail { get; set; }

    /// <summary>捕获时间的显示文本</summary>
    public string CaptureTimeText => CaptureTime.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>原始路径的显示文本（无原始路径时给出说明，便于 UI 直接绑定）</summary>
    public string SourcePathDisplay => string.IsNullOrEmpty(SourcePath)
        ? "（历史归档项，未记录原始路径）"
        : $"原始路径：{SourcePath}";

    private bool _isSelected;

    /// <summary>复选框：是否被勾选（用于批量操作）</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// 阶段3：异步加载磁盘图片缩略图（后台解码，不卡 UI）。
    /// 文件不存在或已有缩略图（示例色块）时直接跳过。
    /// </summary>
    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        if (!File.Exists(FilePath)) return;      // 示例数据路径是演示用的，不真实存在
        if (Thumbnail != null) return;           // 已有缩略图不覆盖

        Bitmap? decoded = null;
        try
        {
            // 后台线程解码，缩到宽度 220，节省内存
            decoded = await Task.Run(() =>
            {
                using var fs = File.OpenRead(FilePath);
                return Bitmap.DecodeToWidth(fs, 220);
            });
        }
        catch
        {
            return; // 解码失败（损坏/格式不支持）静默跳过
        }

        var thumb = decoded;
        if (thumb == null) return;

        // 回 UI 线程更新属性，界面自动刷新
        await Dispatcher.UIThread.InvokeAsync(() => Thumbnail = thumb);
    }
}
