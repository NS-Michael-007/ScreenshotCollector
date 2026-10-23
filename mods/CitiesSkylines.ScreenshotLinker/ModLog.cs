using System;
using System.IO;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 模组轻量日志：写到模组 DLL 同目录 ScreenshotLinker.log（游戏内无控制台，靠日志排查）。
    /// 游戏进程内异常不会导致游戏崩溃（全部 try/catch 兜底）。
    /// </summary>
    public static class ModLog
    {
        private static string _logPath;

        public static void Info(string msg) => Write("INFO", msg);

        public static void Warn(string msg) => Write("WARN", msg);

        public static void Error(string msg) => Write("ERROR", msg);

        private static void Write(string level, string msg)
        {
            // 刻意无锁：游戏 Mono 环境极不稳定（.NET 3.5 surface 曾报 Monitor.Enter 缺失），
            // 日志仅是排障辅助，多线程交错最坏丢一行/一次 IOException，全部 catch 兜底，
            // 绝不允许日志机制反过来拖垮模组加载。
            try
            {
                if (_logPath == null)
                {
                    string dllDir = PathUtil.DirName(
                        new Uri(typeof(ModLog).Assembly.CodeBase).LocalPath);
                    _logPath = PathUtil.Join(dllDir, "ScreenshotLinker.log");
                }
                File.AppendAllText(_logPath, string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}{3}",
                    DateTime.Now, level, msg, Environment.NewLine));
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }
}
