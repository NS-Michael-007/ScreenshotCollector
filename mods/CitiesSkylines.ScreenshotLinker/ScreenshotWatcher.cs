using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 监听游戏官方截图目录：
    ///   %LOCALAPPDATA%\Colossal Order\Cities_Skylines\Screenshots  （游戏内 F12 截图）
    ///   %LOCALAPPDATA%\Colossal Order\Cities_Skylines\Snapshots    （编辑器快照）
    /// 新图片出现（Created 事件）→ 等待文件写完（防抖）→ 推送到桌面端。
    /// 不依赖游戏内部 API / Harmony，纯文件系统监听，兼容性最稳。
    /// </summary>
    public sealed class ScreenshotWatcher
    {
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        // 启动标志用 int + Interlocked（.NET 1.1 API，绝对存在），刻意不用 lock/Monitor：
        // 游戏 Mono 环境极不稳定（曾报 Monitor.Enter 缺失），Interlocked 无任何重载风险。
        private int _startedFlag;
        private Timer _retryTimer;

        /// <summary>游戏数据目录（Colossal Order\Cities_Skylines）</summary>
        private static string GameRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Colossal Order", "Cities_Skylines");
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref _startedFlag, 1, 0) != 0) return;

            // 监听两个截图目录（分类对应桌面端归档分类）
            TryWatch("Screenshots", "天际线");
            TryWatch("Snapshots", "编辑器快照");

            // 目录尚未创建时（如从未截图过），每 60 秒重试一次
            _retryTimer = new Timer(_ => RetryAll(), null, 60000, 60000);
        }

        public void Stop()
        {
            Interlocked.Exchange(ref _startedFlag, 0);

            if (_retryTimer != null) { _retryTimer.Dispose(); _retryTimer = null; }

            foreach (var w in _watchers) { try { w.Dispose(); } catch { } }
            _watchers.Clear();
        }

        private void RetryAll()
        {
            // 读取启动标志（CompareExchange 原值读法）
            if (Interlocked.CompareExchange(ref _startedFlag, 0, 0) == 0) return;
            TryWatch("Screenshots", "天际线");
            TryWatch("Snapshots", "编辑器快照");
        }

        /// <summary>尝试监听一个目录；目录不存在时记录日志并等重试</summary>
        private void TryWatch(string subDir, string category)
        {
            string dir = Path.Combine(GameRoot(), subDir);
            try
            {
                if (!Directory.Exists(dir))
                {
                    ModLog.Info("目录尚未创建，等待重试：" + dir);
                    return;
                }
                // 无锁：Start/Stop 在主线程、RetryAll 在 Timer 线程，潜在竞态窗口极小；
                // 若遍历中被 Stop 修改集合 → foreach 抛异常 → 外层 catch 兜底，不影响主流程。
                foreach (var w in _watchers)
                {
                    if (string.Equals(w.Path.TrimEnd(Path.DirectorySeparatorChar),
                                      dir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                        return;
                }

                var watcher = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = false,
                    Filter = "*.png"
                };
                watcher.Created += (s, e) => OnNewFile(e.FullPath, category);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
                ModLog.Info("已监听：" + dir + "（分类：" + category + "）");
            }
            catch (Exception ex)
            {
                ModLog.Error("监听失败 " + dir + "：" + ex.Message);
            }
        }

        /// <summary>
        /// 供 Harmony 补丁调用：把指定分类目录里最新的一张截图推送给桌面端。
        /// 补丁触发时机早于文件系统事件，实现"截图瞬间即推送"。
        /// </summary>
        public static void PushLatest(string category)
        {
            try
            {
                string dir = Path.Combine(GameRoot(), category == "编辑器快照" ? "Snapshots" : "Screenshots");
                if (!Directory.Exists(dir)) return;

                var latest = new DirectoryInfo(dir)
                    .GetFiles("*.png")
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();
                if (latest == null) return;

                // 复用防抖推送：等文件写完再发
                ThreadPool.QueueUserWorkItem(_ => DeliverAfterSettle(latest.FullName, category));
            }
            catch (Exception ex)
            {
                ModLog.Error("PushLatest 异常：" + ex.Message);
            }
        }

        private void OnNewFile(string path, string category)
        {
            // 后台线程：等文件写完再推（截图写入过程可能触发多次事件）
            ThreadPool.QueueUserWorkItem(_ => DeliverAfterSettle(path, category));
        }

        private static void DeliverAfterSettle(string path, string category)
        {
            try
            {
                // 最多等 5 秒，直到文件尺寸稳定（写完）
                long lastSize = -1;
                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(500);
                    if (!File.Exists(path)) return;
                    long size = new FileInfo(path).Length;
                    if (size > 0 && size == lastSize) break;
                    lastSize = size;
                }

                if (!File.Exists(path)) return;
                var info = new FileInfo(path);
                string timeText = info.LastWriteTime.ToString("yyyy-MM-ddTHH:mm:ss");
                bool ok = SocketSender.Send(info.FullName, category, timeText);
                ModLog.Info(string.Format("推送{0}：{1}（{2}）", ok ? "成功" : "失败", info.Name, category));
            }
            catch (Exception ex)
            {
                ModLog.Error("推送异常：" + ex.Message);
            }
        }
    }
}
