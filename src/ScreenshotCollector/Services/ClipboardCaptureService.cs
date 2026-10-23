using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ScreenshotCollector.Services;

/// <summary>
/// 剪贴板捕获服务（阶段5-3）：
/// 周期性检查系统剪贴板 —— 当前台窗口是微信(WeChat)/QQ(QQ/QQNT) 且剪贴板里有图片时，
/// 视为一次截图，保存到临时文件并交给 FileArchiveService 归档。
/// 用途：解决微信 Alt+A、QQ Ctrl+Alt+A 截图后「直接发送不点保存」时抓不到原图的问题。
/// </summary>
public class ClipboardCaptureService : IDisposable
{
    private readonly FileArchiveService _archive;
    private readonly DispatcherTimer _timer;

    private DateTime _lastCapture = DateTime.MinValue;
    private readonly int _cooldownMs = 2000; // 冷却：防止同一张图连续抓取

    public ClipboardCaptureService(FileArchiveService archive)
    {
        _archive = archive;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _timer.Tick += async (_, _) => await CheckAsync();
    }

    /// <summary>启动监听</summary>
    public void Start()
    {
        _timer.Start();
        LogService.Info("Clipboard", "剪贴板捕获已启动（前台为微信/QQ 时自动保存截图）");
    }

    public void Stop() => _timer.Stop();

    public void Dispose() => _timer.Stop();

    private async Task CheckAsync()
    {
        try
        {
            // 冷却期跳过，避免同一张图反复保存
            if (DateTime.Now - _lastCapture < TimeSpan.FromMilliseconds(_cooldownMs)) return;

            // 前台窗口校验：只有微信/QQ 在前台时才可能是它们的截图
            var source = ForegroundSource();
            if (source == null) return;

            // 读取剪贴板位图（PNG 优先，其次 DIB）
            var image = await Task.Run(WinClipboard.ReadBitmap);
            if (image == null) return; // 剪贴板里没有图片

            _lastCapture = DateTime.Now;

            // 保存到临时文件后归档（归档会自动去重/分类）
            var tmp = Path.Combine(Path.GetTempPath(), $"clip_{Guid.NewGuid():N}.png");
            image.Save(tmp);
            _archive.Archive(tmp, source, DateTime.Now);
            try { File.Delete(tmp); } catch { /* 临时文件清理失败不影响 */ }
        }
        catch
        {
            // 剪贴板读取偶发失败，忽略等待下次轮询
        }
    }

    /// <summary>判断前台窗口进程是否为微信/QQ，是则返回来源分类，否则返回 null</summary>
    private static string? ForegroundSource()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;

        try
        {
            var processName = Process.GetProcessById((int)pid).ProcessName;
            return processName switch
            {
                // 微信 3.x 进程名 WeChat；微信 4.0 新架构进程名 Weixin —— 两者都算微信
                "WeChat" or "Weixin" => "微信",
                "QQ" or "QQNT" => "QQ",
                _ => null // 前台是其它程序：不是微信/QQ 截图，忽略
            };
        }
        catch
        {
            return null; // 进程可能已退出
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
