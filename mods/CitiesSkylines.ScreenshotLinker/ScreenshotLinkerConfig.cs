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

        /// <summary>游戏内按钮不透明度（0-100%，设置页滑块控制）</summary>
        public int OpacityPercent { get; set; }

        /// <summary>桌面端程序默认路径（本地开发构建输出；玩家端路径不存在时自动回退到模组同目录打包工具）</summary>
        public const string DefaultDesktopAppPath = @"C:\Users\NAN SHENG CSUR\source\repos\ScreenshotCollector\src\ScreenshotCollector\bin\Debug\net8.0\ScreenshotCollector.exe";

        /// <summary>随模组包分发的桌面端工具子文件夹名（对标 RoadBuilder 的 Thumbnail Maker 打包结构）</summary>
        public const string BundledToolFolder = "ScreenshotCollectorTool";

        /// <summary>随模组包分发的桌面端工具可执行文件名</summary>
        public const string BundledToolFileName = "ScreenshotCollector.exe";

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
            OpacityPercent = 100;
        }

        /// <summary>
        /// 取桌面端程序路径：
        /// 1) 优先使用配置/默认路径（开发者本机直接命中本地构建 exe）；
        /// 2) 若该路径不存在（玩家端工坊订阅后默认值指向本地开发目录，必然不存在），
        ///    自动回退到「模组 DLL 同目录 \ ScreenshotCollectorTool \ ScreenshotCollector.exe」
        ///    ——即随工坊包分发的独立工具，保证玩家游戏内按钮可直接启动，对标 RoadBuilder。
        /// </summary>
        public string GetDesktopAppPath()
        {
            string p = DesktopAppPath;
            if (ReferenceEquals(p, null) || p.Length == 0) p = DefaultDesktopAppPath;
            if (!System.IO.File.Exists(p))
            {
                string modDir = PathUtil.ModDirectory();
                if (modDir.Length > 0)
                {
                    string bundled = PathUtil.Join(PathUtil.Join(modDir, BundledToolFolder), BundledToolFileName);
                    if (System.IO.File.Exists(bundled)) return bundled;
                }
            }
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

        /// <summary>取按钮不透明度（%）：非法值（≤0 或 &gt;100）回退 100</summary>
        public int GetOpacityPercent()
        {
            if (OpacityPercent <= 0 || OpacityPercent > 100) return 100;
            return OpacityPercent;
        }

        public static void EnsureLoaded()
        {
            if (Current == null) Current = Load();
        }

        private static string ConfigPath()
        {
            string dllDir = PathUtil.ModDirectory();
            if (dllDir.Length == 0) dllDir = Environment.CurrentDirectory;
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
