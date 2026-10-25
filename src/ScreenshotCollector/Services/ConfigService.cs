using System;
using System.IO;
using System.Text.Json;
using ScreenshotCollector.Models;

namespace ScreenshotCollector.Services;

/// <summary>
/// 专门负责配置的加载和持久化服务
/// </summary>
public static class ConfigService
{
    // 配置文件存放在程序运行目录下 (appsettings.json)
    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

    /// <summary>
    /// 从本地加载配置，如果不存在或失败则返回默认实例
    /// </summary>
    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null) return config;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Config", $"读取配置失败: {ex.Message}");
        }

        return new AppConfig(); // 读取失败则返回默认配置
    }

    /// <summary>
    /// 将配置保存到本地 JSON 文件
    /// </summary>
    public static void Save(AppConfig config)
    {
        try
        {
            // WriteIndented = true 可以让 JSON 文件排版更好看，方便你打开调试
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
            LogService.Info("Config", "配置已成功保存到本地");
        }
        catch (Exception ex)
        {
            LogService.Error("Config", $"保存配置失败: {ex.Message}");
        }
    }
}