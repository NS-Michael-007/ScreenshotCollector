using System;
using System.IO;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 模组轻量日志：写到模组 DLL 同目录 ScreenshotLinker.log
    /// </summary>
    public static class ModLog
    {
        private static string _logPath;

        // 🚀 修复：去掉 => 语法
        public static void Info(string msg)
        {
            Write("INFO", msg);
        }

        public static void Warn(string msg)
        {
            Write("WARN", msg);
        }

        public static void Error(string msg)
        {
            Write("ERROR", msg);
        }

        private static void Write(string level, string msg)
        {
            try
            {
                if (_logPath == null) _logPath = ResolveLogPath();
                File.AppendAllText(_logPath, BuildLine(level, msg));
            }
            catch
            {
                try
                {
                    string fallback = PathUtil.Join(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "ScreenshotLinker.log");
                    File.AppendAllText(fallback, BuildLine(level, msg));
                }
                catch
                {
                    // 日志失败不影响主流程
                }
            }
        }

        private static string BuildLine(string level, string msg)
        {
            return string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}{3}",
                DateTime.Now, level, msg, Environment.NewLine);
        }

        private static string ResolveLogPath()
        {
            string dllDir = PathUtil.ModDirectory();
            if (dllDir.Length > 0) return PathUtil.Join(dllDir, "ScreenshotLinker.log");
            return PathUtil.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ScreenshotLinker.log");
        }
    }
}