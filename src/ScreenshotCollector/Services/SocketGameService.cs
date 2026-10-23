using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenshotCollector.Services;

/// <summary>
/// 天际线模组通信服务（阶段5-4）：
/// 本地 TCP 监听（默认端口 23333），接收配套 Cities:Skylines Harmony 模组推送的截图信息。
/// 模组在游戏截图/编辑器快照生成瞬间，把图片路径推送到本程序，实现即时捕获。
/// 协议：一行 JSON：
///   {"path":"D:\\screens\\1.png","category":"天际线","time":"2026-10-23 12:00:00"}
/// category 可选：天际线 / 编辑器快照
/// </summary>
public class SocketGameService : IDisposable
{
    private readonly FileArchiveService _archive;
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>收到一次模组推送并成功归档后触发（category, 快照时间）——用于更新状态灯</summary>
    public event Action<string, DateTime>? SnapshotReceived;

    public SocketGameService(FileArchiveService archive, int port = 23333)
    {
        _archive = archive;
        _port = port;
    }

    /// <summary>启动监听（后台线程接收）；端口被占用等失败时返回 false 并写日志，不让程序崩溃</summary>
    public bool Start()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            LogService.Info("Socket", $"天际线模组通信已启动（端口 {_port}）");
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error("Socket", $"端口 {_port} 被占用，模组通信不可用：{ex.Message}");
            return false;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener;
        if (listener == null) return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogService.Error("Socket", $"接受连接失败：{ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using var reader = new StreamReader(client.GetStream());
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) return;

            // 解析一行 JSON
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var path = root.GetProperty("path").GetString();
                var category = root.TryGetProperty("category", out var c)
                    ? c.GetString() ?? "天际线"
                    : "天际线";
                var time = root.TryGetProperty("time", out var t) && t.TryGetDateTime(out var dt)
                    ? dt
                    : DateTime.Now;

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var archived = _archive.Archive(path, category, time);
                    if (archived != null)
                    {
                        SnapshotReceived?.Invoke(category, time);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("Socket", $"解析模组推送数据失败：{ex.Message}");
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Socket", $"处理客户端连接失败：{ex.Message}");
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>停止服务（退出程序时调用）</summary>
    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
    }

    public void Dispose() => Stop();
}
