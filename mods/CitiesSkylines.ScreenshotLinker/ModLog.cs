using System;
using System.IO;
using System.Threading;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 模组轻量日志：写到模组 DLL 同目录 ScreenshotLinker.log（游戏内无控制台，靠日志排查）。
    /// 游戏进程内异常不会导致游戏崩溃（全部 try/catch 兜底）。
    /// </summary>
    public static class ModLog
    {
        private static readonly object Sync = new object();
        private static string _logPath;

        public static void Info(string msg) => Write("INFO", msg);

        public static void Warn(string msg) => Write("WARN", msg);

        public static void Error(string msg) => Write("ERROR", msg);

        private static void Write(string level, string msg)
        {
            // 注意：不能用 lock 语句（net46 编译生成 Monitor.Enter(object) 单参重载，
            // 游戏 Mono 的 .NET 3.5 API 只有 Monitor.Enter(object, ref bool)，运行时会
            // MissingMethodException）→ 显式双参数写法
            bool lockTaken = false;
            try
            {
                Monitor.Enter(Sync, ref lockTaken);
                if (_logPath == null)
                {
                    string dllDir = Path.GetDirectoryName(
                        new Uri(typeof(ModLog).Assembly.CodeBase).LocalPath);
                    _logPath = Path.Combine(dllDir ?? ".", "ScreenshotLinker.log");
                }
                File.AppendAllText(_logPath, string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}{3}",
                    DateTime.Now, level, msg, Environment.NewLine));
            }
            catch
            {
                // 日志失败不影响主流程
            }
            finally
            {
                if (lockTaken) Monitor.Exit(Sync);
            }
        }
    }
}
