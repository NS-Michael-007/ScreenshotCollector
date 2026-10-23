using System;
using System.IO;

namespace ScreenshotCollector.Services;

/// <summary>
/// 日志服务（阶段5-1）：
/// 写入 exe 同级 logs\ 目录，每天一个 log_yyyyMMdd.txt；
/// 启动时自动清理超过保留天数（默认7天）的旧日志。
/// </summary>
public static class LogService
{
    private static readonly object LockObj = new();

    /// <summary>日志目录：exe 同级 logs\</summary>
    public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>今天的日志文件</summary>
    public static string LogFile => Path.Combine(LogDirectory, $"log_{DateTime.Now:yyyyMMdd}.txt");

    public static void Info(string category, string message) => Write("INFO", category, message);

    public static void Warn(string category, string message) => Write("WARN", category, message);

    public static void Error(string category, string message) => Write("ERROR", category, message);

    private static void Write(string level, string category, string message)
    {
        try
        {
            lock (LockObj)
            {
                Directory.CreateDirectory(LogDirectory);
                // 带 BOM 的 UTF-8：Windows 记事本打开中文不乱码
                File.AppendAllText(
                    LogFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] [{category}] {message}{Environment.NewLine}",
                    new System.Text.UTF8Encoding(true));
            }
        }
        catch
        {
            // 日志写入失败不能导致程序崩溃，静默忽略
        }
    }

    /// <summary>清理超过保留天数的日志文件（程序启动时调用）</summary>
    public static void Cleanup(int keepDays = 7)
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.GetFiles(LogDirectory, "log_*.txt"))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < cutoff) File.Delete(file);
                }
                catch { /* 单个文件清理失败不影响其它 */ }
            }
        }
        catch { /* 清理失败忽略 */ }
    }
}
