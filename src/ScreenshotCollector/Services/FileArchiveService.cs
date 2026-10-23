using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Avalonia.Media.Imaging;

namespace ScreenshotCollector.Services;

/// <summary>
/// 图片归档服务（阶段5-1）：
/// 把捕获到的图片复制到保存根目录，按【来源分类 / 日期】分文件夹，
/// 文件名带时间戳；支持 MD5 去重与最小分辨率过滤。
/// 归档规则：
///   根目录\微信\2026-10-23\20261023_102211_微信.png
/// </summary>
public class FileArchiveService
{
    /// <summary>保存根目录（默认：图片库\ScreenshotCollector，可在设置里修改）</summary>
    public string SaveRootFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenshotCollector");

    private readonly HashSet<string> _seenHashes = new(); // 已见过的图片哈希（去重）

    /// <summary>归档成功后触发（归档路径, 来源分类, 捕获时间）——用于把新截图实时显示到界面列表</summary>
    public event Action<string, string, DateTime>? Archived;

    /// <summary>
    /// 归档一张图片。
    /// </summary>
    /// <param name="sourcePath">来源图片完整路径（或临时文件）</param>
    /// <param name="sourceCategory">来源分类：天际线 / 编辑器快照 / Steam / 微信 / QQ</param>
    /// <param name="capturedAt">捕获时间</param>
    /// <param name="minWidth">最小宽度过滤（0=不限制）</param>
    /// <param name="minHeight">最小高度过滤（0=不限制）</param>
    /// <returns>归档后的完整路径；不满足规则（重复/太小/损坏）返回 null</returns>
    public string? Archive(string sourcePath, string sourceCategory, DateTime capturedAt,
                           int minWidth = 0, int minHeight = 0)
    {
        try
        {
            // 1. 计算哈希并去重
            var hash = Md5Of(sourcePath);
            if (hash == null) return null;
            if (!_seenHashes.Add(hash)) return null; // 已存在相同图片，跳过

            // 2. 最小分辨率过滤
            if (!MeetsMinSize(sourcePath, minWidth, minHeight)) return null;

            // 3. 目标目录：根\分类\日期
            var folder = Path.Combine(SaveRootFolder, sourceCategory, capturedAt.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(folder);

            // 4. 目标文件名：时间戳_分类.扩展名（重名自动加序号）
            var ext = Path.GetExtension(sourcePath);
            var baseName = $"{capturedAt:yyyyMMdd_HHmmss}_{sourceCategory}{ext}";
            var dest = Path.Combine(folder, baseName);
            var n = 1;
            while (File.Exists(dest))
            {
                dest = Path.Combine(folder, $"{capturedAt:yyyyMMdd_HHmmss}_{sourceCategory}_{n++}{ext}");
            }

            // 5. 复制图片（保留原文件，安全不破坏来源）
            File.Copy(sourcePath, dest);
            LogService.Info("Archive", $"已归档：{dest}（来源 {sourceCategory}）");
            Archived?.Invoke(dest, sourceCategory, capturedAt);
            return dest;
        }
        catch (Exception ex)
        {
            LogService.Error("Archive", $"归档失败 {sourcePath}：{ex.Message}");
            return null;
        }
    }

    /// <summary>计算文件 MD5（读取失败返回 null）</summary>
    private static string? Md5Of(string path)
    {
        try
        {
            using var md5 = MD5.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(md5.ComputeHash(fs));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>检查图片是否达到最小宽高（解码失败视为不合格）</summary>
    private static bool MeetsMinSize(string path, int minWidth, int minHeight)
    {
        if (minWidth <= 0 && minHeight <= 0) return true;
        try
        {
            using var bmp = new Bitmap(path);
            return (minWidth <= 0 || bmp.PixelSize.Width >= minWidth)
                && (minHeight <= 0 || bmp.PixelSize.Height >= minHeight);
        }
        catch
        {
            return false;
        }
    }
}
