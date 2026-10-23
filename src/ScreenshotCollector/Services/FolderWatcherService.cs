using System;
using System.Collections.Generic;
using System.IO;

namespace ScreenshotCollector.Services;

/// <summary>
/// 文件夹监听服务（阶段5-2）：
/// 用 FileSystemWatcher 监控截图目录，发现新增图片后等待写入完成，
/// 交给 FileArchiveService 自动归档（去重/尺寸过滤/分类保存）。
/// </summary>
public class FolderWatcherService
{
    private readonly FileArchiveService _archive;
    private readonly List<FileSystemWatcher> _watchers = new();

    /// <summary>目录注册结果事件（分类, 是否成功监听, 路径）——用于更新状态灯</summary>
    public event Action<string, bool, string>? FolderStatusChanged;

    public FolderWatcherService(FileArchiveService archive)
    {
        _archive = archive;
    }

    /// <summary>
    /// 注册默认监听目录（启动时调用）：
    /// 天际线游戏截图、天际线编辑器快照、Steam 截图（自动探测路径）。
    /// 目录不存在的会跳过并写日志（用户没装游戏/Steam时正常现象）。
    /// </summary>
    public void RegisterDefaultFolders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var csDir = Path.Combine(local, "Colossal Order", "Cities_Skylines");

        AddWatch(Path.Combine(csDir, "Screenshots"), "天际线");
        AddWatch(Path.Combine(csDir, "Snapshots"), "编辑器快照");

        var steamShot = FindSteamScreenshotsFolder();
        if (steamShot != null)
        {
            AddWatch(steamShot, "Steam");
        }
        else
        {
            LogService.Info("FolderWatch", "未找到 Steam 截图目录（Cities:Skylines 尚未用 Steam 截图过）");
            FolderStatusChanged?.Invoke("Steam", false, "");
        }
    }

    /// <summary>注册一个监听目录（目录不存在则跳过并写日志）</summary>
    public void AddWatch(string folderPath, string category)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        if (!Directory.Exists(folderPath))
        {
            LogService.Info("FolderWatch", $"目录不存在，跳过监听：{folderPath}（{category}）");
            FolderStatusChanged?.Invoke(category, false, folderPath);
            return;
        }

        var watcher = new FileSystemWatcher(folderPath)
        {
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
        };
        watcher.Created += (_, e) => OnCreatedAsync(e.FullPath, category);
        _watchers.Add(watcher);

        LogService.Info("FolderWatch", $"已监听：{folderPath}（{category}）");
        FolderStatusChanged?.Invoke(category, true, folderPath);
    }

    /// <summary>新文件出现：等写入完成 → 归档图片</summary>
    private async void OnCreatedAsync(string path, string category)
    {
        try
        {
            // Steam 等程序写入是异步的，轮询等待文件可读（最长约5秒）
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    using var fs = File.OpenRead(path);
                    break; // 可以打开即认为写完
                }
                catch
                {
                    await Task.Delay(500);
                }
            }

            // 只处理图片文件
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp")) return;

            _archive.Archive(path, category, File.GetLastWriteTime(path));
        }
        catch (Exception ex)
        {
            LogService.Error("FolderWatch", $"处理文件失败 {path}：{ex.Message}");
        }
    }

    /// <summary>停止全部监听（退出程序时调用）</summary>
    public void StopAll()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }

    /// <summary>探测 Steam 的 Cities:Skylines 截图目录：userdata\*\760\remote\255710\screenshots</summary>
    private static string? FindSteamScreenshotsFolder()
    {
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Steam\userdata",
            @"C:\Program Files\Steam\userdata",
            @"D:\Steam\userdata",
            @"D:\SteamLibrary\userdata",
            @"E:\Steam\userdata"
        };

        foreach (var userData in candidates)
        {
            if (!Directory.Exists(userData)) continue;
            foreach (var userDir in Directory.GetDirectories(userData))
            {
                var shot = Path.Combine(userDir, "760", "remote", "255710", "screenshots");
                if (Directory.Exists(shot)) return shot;
            }
        }
        return null;
    }
}
