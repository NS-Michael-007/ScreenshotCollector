using System;
using System.IO;
using System.Xml.Serialization;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 模组配置（XML 持久化到模组 DLL 同目录 ScreenshotLinkerConfig.xml）。
    /// 兼容游戏 Mono 的 .NET 3.5 API：仅用 XmlSerializer / File（.NET 1.x/2.x API）。
    /// </summary>
    [XmlRoot("ScreenshotLinkerConfig")]
    public class ScreenshotLinkerConfig
    {
        /// <summary>桌面端监听端口（默认 23333，与桌面端 SocketGameService 一致）</summary>
        public int Port { get; set; }

        /// <summary>是否启用 Harmony 补丁通道（即时推送）</summary>
        public bool UseHarmonyPatch { get; set; }

        /// <summary>是否启用目录监听兜底通道</summary>
        public bool UseFolderWatch { get; set; }

        /// <summary>桌面端截图收集器程序路径（游戏内按钮一键启动用）</summary>
        public string DesktopAppPath { get; set; }

        /// <summary>游戏内按钮文字（最长 8 个字符）</summary>
        public string ButtonText { get; set; }

        /// <summary>桌面端程序默认路径（与桌面端工程构建输出一致）</summary>
        public const string DefaultDesktopAppPath = @"C:\Users\NAN SHENG CSUR\source\repos\ScreenshotCollector\src\ScreenshotCollector\bin\Debug\net8.0\ScreenshotCollector.exe";

        /// <summary>游戏内按钮文字最大长度</summary>
        public const int ButtonTextMaxLength = 8;

        /// <summary>当前配置（模组启用时加载，设置页修改后即时保存）</summary>
        public static ScreenshotLinkerConfig Current { get; private set; }

        public ScreenshotLinkerConfig()
        {
            Port = 23333;
            UseHarmonyPatch = true;
            UseFolderWatch = true;
            DesktopAppPath = DefaultDesktopAppPath;
            ButtonText = "启动截图收集器";
        }

        /// <summary>取桌面端程序路径：空则用默认路径</summary>
        public string GetDesktopAppPath()
        {
            string p = DesktopAppPath;
            if (ReferenceEquals(p, null) || p.Length == 0) return DefaultDesktopAppPath;
            return p;
        }

        /// <summary>取按钮文字：空则用默认文案，超长则截断到 8 字</summary>
        public string GetButtonText()
        {
            string t = ButtonText;
            if (ReferenceEquals(t, null) || t.Length == 0) return "启动截图收集器";
            if (t.Length > ButtonTextMaxLength) return t.Substring(0, ButtonTextMaxLength);
            return t;
        }

        public static void EnsureLoaded()
        {
            if (Current == null) Current = Load();
        }

        private static string ConfigPath()
        {
            string dllDir = PathUtil.DirName(
                new Uri(typeof(ScreenshotLinkerConfig).Assembly.CodeBase).LocalPath);
            return PathUtil.Join(dllDir, "ScreenshotLinkerConfig.xml");
        }

        public static ScreenshotLinkerConfig Load()
        {
            try
            {
                string p = ConfigPath();
                if (File.Exists(p))
                {
                    var ser = new XmlSerializer(typeof(ScreenshotLinkerConfig));
                    using (var fs = File.OpenRead(p))
                        return (ScreenshotLinkerConfig)ser.Deserialize(fs);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("读取配置失败（使用默认值）：" + ex.Message);
            }
            return new ScreenshotLinkerConfig();
        }

        public void Save()
        {
            try
            {
                var ser = new XmlSerializer(typeof(ScreenshotLinkerConfig));
                using (var fs = File.Create(ConfigPath()))
                    ser.Serialize(fs, this);
                ModLog.Info("配置已保存：" + ConfigPath());
            }
            catch (Exception ex)
            {
                ModLog.Error("保存配置失败：" + ex.Message);
            }
        }
    }
}
