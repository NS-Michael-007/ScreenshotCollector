using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CitiesSkylines.ScreenshotLinker
{
    public sealed class ScreenshotWatcher
    {
        public delegate string SnapshotClassifier(string filePath);

        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private int _startedFlag;
        private Timer _retryTimer;

        // 🚀 新增：用于替代闭包传参的包装类
        private class DeliverState
        {
            public string Path;
            public string Category;
        }

        private class WatcherContext
        {
            public string DefaultCategory;
            public SnapshotClassifier Classifier;
        }

        // 用于在事件触发时查找对应的配置
        private readonly Dictionary<FileSystemWatcher, WatcherContext> _watcherContexts = new Dictionary<FileSystemWatcher, WatcherContext>();

        private static string GameRoot()
        {
            return PathUtil.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Colossal Order", "Cities_Skylines");
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref _startedFlag, 1, 0) != 0) return;

            TryWatch("Screenshots", "天际线");
            TryWatchCore("Snapshots", "编辑器快照", true, ClassifySnapshot);

            // 🚀 修复：Timer 改用具名方法
            _retryTimer = new Timer(RetryTimerCallback, null, 60000, 60000);
        }

        public void Stop()
        {
            Interlocked.Exchange(ref _startedFlag, 0);

            if (_retryTimer != null) { _retryTimer.Dispose(); _retryTimer = null; }

            foreach (var w in _watchers) { try { w.Dispose(); } catch { } }
            _watchers.Clear();
            _watcherContexts.Clear(); // 清理上下文
        }

        // 🚀 修复：替代 _ => RetryAll()
        private void RetryTimerCallback(object state)
        {
            RetryAll();
        }

        private void RetryAll()
        {
            if (Interlocked.CompareExchange(ref _startedFlag, 0, 0) == 0) return;
            TryWatch("Screenshots", "天际线");
            TryWatchCore("Snapshots", "编辑器快照", true, ClassifySnapshot);
        }

        private void TryWatch(string subDir, string category)
        {
            TryWatchCore(subDir, category, false, null);
        }

        private void TryWatchCore(string subDir, string defaultCategory, bool recursive, SnapshotClassifier classifier)
        {
            string dir = PathUtil.Join(GameRoot(), subDir);
            try
            {
                if (!Directory.Exists(dir))
                {
                    ModLog.Info("目录尚未创建，等待重试：" + dir);
                    return;
                }
                foreach (var w in _watchers)
                {
                    if (string.Equals(w.Path.TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        return;
                }

                var watcher = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = recursive,
                    Filter = "*.png"
                };

                // 🚀 修复：替代 (s, e) => OnNewFile(...)
                WatcherContext context = new WatcherContext();
                context.DefaultCategory = defaultCategory;
                context.Classifier = classifier;
                _watcherContexts[watcher] = context;

                watcher.Created += Watcher_Created;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);

                ModLog.Info("已监听：" + dir + "（分类：" + defaultCategory + (recursive ? "，含子目录" : "") + "）");
            }
            catch (Exception ex)
            {
                ModLog.Error("监听失败 " + dir + "：" + ex.Message);
            }
        }

        // 🚀 修复：具名事件处理方法
        private void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            FileSystemWatcher w = sender as FileSystemWatcher;
            if (w != null && _watcherContexts.ContainsKey(w))
            {
                WatcherContext context = _watcherContexts[w];
                string category = context.Classifier != null ? context.Classifier(e.FullPath) : context.DefaultCategory;
                OnNewFile(e.FullPath, category);
            }
        }

        private static string ClassifySnapshot(string path)
        {
            string fn = FileNameOf(path);
            if (fn.StartsWith("thumbnail", StringComparison.OrdinalIgnoreCase)) return "编辑器缩略图";
            if (fn.StartsWith("tooltip", StringComparison.OrdinalIgnoreCase)) return "编辑器提示图";
            return "编辑器快照";
        }

        private static string FileNameOf(string path)
        {
            int i = path.LastIndexOf('\\');
            return i >= 0 ? path.Substring(i + 1) : path;
        }

        public static void PushLatest(string category)
        {
            try
            {
                string dir = PathUtil.Join(GameRoot(), category == "编辑器快照" ? "Snapshots" : "Screenshots");
                if (!Directory.Exists(dir)) return;

                FileInfo latest = null;
                foreach (FileInfo f in new DirectoryInfo(dir).GetFiles("*.png"))
                {
                    if (latest == null || f.LastWriteTime > latest.LastWriteTime) latest = f;
                }
                if (latest == null) return;

                // 🚀 修复：替代 ThreadPool.QueueUserWorkItem(_ => DeliverAfterSettle(...))
                DeliverState state = new DeliverState();
                state.Path = latest.FullName;
                state.Category = category;
                ThreadPool.QueueUserWorkItem(DeliverWorker, state);
            }
            catch (Exception ex)
            {
                ModLog.Error("PushLatest 异常：" + ex.Message);
            }
        }

        private void OnNewFile(string path, string category)
        {
            // 🚀 修复：替代 ThreadPool.QueueUserWorkItem(_ => DeliverAfterSettle(...))
            DeliverState state = new DeliverState();
            state.Path = path;
            state.Category = category;
            ThreadPool.QueueUserWorkItem(DeliverWorker, state);
        }

        // 🚀 修复：线程池具名回调
        private static void DeliverWorker(object stateObj)
        {
            DeliverState state = stateObj as DeliverState;
            if (state != null)
            {
                DeliverAfterSettle(state.Path, state.Category);
            }
        }

        private static void DeliverAfterSettle(string path, string category)
        {
            try
            {
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