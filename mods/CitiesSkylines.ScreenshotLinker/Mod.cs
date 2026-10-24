using System;
using HarmonyLib;
using ICities;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 模组入口：截图自动收集器 联动端。
    /// 采用工坊 Harmony 模组框架结构：
    ///   1) Harmony 初始化并补丁游戏截图入口（反射探测，命中即即时推送）；
    ///   2) 目录监听兜底（无论补丁是否命中，截图落盘都能推送）。
    /// 游戏内 F12 截图 / 编辑器快照 → 本地 TCP 23333 推送到桌面端「截图自动收集器」。
    /// 设置：OnSettingsUI(UIHelperBase) → 显示在游戏「选项 → Mods」页签。
    /// </summary>
    public class Mod : IUserMod
    {
        /// <summary>Harmony 实例唯一标识（卸载时用于 UnpatchAll）</summary>
        public const string HarmonyId = "com.wangsiji.screenshotlinker";

        private ScreenshotWatcher _watcher;
        private Harmony _harmony;
        private bool _harmonyActive;
        private UnityEngine.GameObject _uiObject;

        /// <summary>游戏内模组名称（内容管理器中显示，独立存在）</summary>
        public string Name => "截图自动收集器联动（ScreenshotLinker）";

        /// <summary>描述置空：内容管理器列表只保留模组名称，不显示描述块</summary>
        public string Description => string.Empty;

        /// <summary>模组启用：按配置加载配置并启动对应通道</summary>
        public void OnEnabled()
        {
            ScreenshotLinkerConfig.EnsureLoaded();
            var cfg = ScreenshotLinkerConfig.Current;
            SocketSender.Port = cfg.Port;

            ModLog.Info("配置：端口=" + cfg.Port + "，Harmony补丁=" + cfg.UseHarmonyPatch + "，目录监听=" + cfg.UseFolderWatch);

            // 通道1：Harmony 补丁游戏截图入口（反射探测，类名/方法名不硬编码；可关闭）
            if (cfg.UseHarmonyPatch)
            {
                _harmonyActive = TryPatchScreenshot();
            }
            else
            {
                ModLog.Info("已按设置关闭 Harmony 补丁通道");
            }

            // 通道2：目录监听兜底（保证任何情况下都能推送；可关闭）
            if (cfg.UseFolderWatch)
            {
                if (_watcher == null) _watcher = new ScreenshotWatcher();
                _watcher.Start();
            }
            else
            {
                ModLog.Info("已按设置关闭目录监听兜底通道");
            }

            ModLog.Info("模组已启用：Harmony " + (_harmonyActive ? "补丁成功" : "补丁未命中/关闭（目录监听兜底）"));

            // —— 游戏界面「一键启动」按钮 ——
            if (ReferenceEquals(_uiObject, null))
            {
                try
                {
                    _uiObject = new UnityEngine.GameObject("ScreenshotLinkerUIRoot");
                    _uiObject.AddComponent<ScreenshotLinkerUI>();
                    UnityEngine.Object.DontDestroyOnLoad(_uiObject);
                    ModLog.Info("已挂载游戏内按钮组件");
                }
                catch (Exception ex)
                {
                    ModLog.Warn("挂载游戏内按钮失败：" + ex.Message);
                }
            }
        }

        /// <summary>模组停用：卸载补丁 + 释放监听</summary>
        public void OnDisabled()
        {
            if (_watcher != null) _watcher.Stop();
            try
            {
                if (_harmony != null) _harmony.UnpatchAll(HarmonyId);
            }
            catch (Exception ex)
            {
                ModLog.Error("卸载 Harmony 补丁失败：" + ex.Message);
            }
            if (!ReferenceEquals(_uiObject, null))
            {
                UnityEngine.Object.Destroy(_uiObject);
                _uiObject = null;
            }
            ModLog.Info("模组已停用");
        }

        /// <summary>
        /// 模组设置页：显示在游戏「选项 → Mods」页签。
        /// 游戏通过反射查找 OnSettingsUI(UIHelperBase) 方法（鸭子类型，天际线一代标准做法）。
        /// 设置修改即时写入 XML 配置。
        /// </summary>
        public void OnSettingsUI(UIHelperBase helper)
        {
            ScreenshotLinkerConfig.EnsureLoaded();
            var cfg = ScreenshotLinkerConfig.Current;

            helper.AddGroup("桌面端连接");
            helper.AddTextfield(
                "桌面端端口",
                cfg.Port.ToString(),
                text => { },
                text =>
                {
                    int v;
                    if (int.TryParse(text, out v) && v > 0 && v < 65536)
                    {
                        cfg.Port = v;
                        cfg.Save();
                        ModLog.Info("端口已更新为 " + v);
                    }
                    else
                    {
                        ModLog.Warn("端口无效：" + text);
                    }
                });

            helper.AddGroup("捕获通道");
            helper.AddCheckbox("启用 Harmony 补丁（截图瞬间即时推送）", cfg.UseHarmonyPatch,
                isChecked =>
                {
                    cfg.UseHarmonyPatch = isChecked;
                    cfg.Save();
                    ModLog.Info("Harmony 补丁通道 " + (isChecked ? "已开启" : "已关闭") + "（重启模组生效）");
                });
            helper.AddCheckbox("启用目录监听兜底（任何情况都能推送）", cfg.UseFolderWatch,
                isChecked =>
                {
                    cfg.UseFolderWatch = isChecked;
                    cfg.Save();
                    ModLog.Info("目录监听通道 " + (isChecked ? "已开启" : "已关闭") + "（重启模组生效）");
                });

            helper.AddSpace(8);

            // —— 一键启动配置 ——
            helper.AddGroup("一键启动（游戏界面按钮）");
            helper.AddTextfield(
                "桌面端程序路径",
                cfg.GetDesktopAppPath(),
                text => { },
                text =>
                {
                    cfg.DesktopAppPath = text;
                    cfg.Save();
                    ModLog.Info("已更新桌面端程序路径：" + text);
                });
            helper.AddTextfield(
                "游戏内按钮文字（最长 8 个字符）",
                cfg.GetButtonText(),
                text => { },
                text =>
                {
                    if (!ReferenceEquals(text, null) && text.Length > ScreenshotLinkerConfig.ButtonTextMaxLength)
                    {
                        text = text.Substring(0, ScreenshotLinkerConfig.ButtonTextMaxLength);
                        ModLog.Warn("按钮文字超过 " + ScreenshotLinkerConfig.ButtonTextMaxLength + " 字，已截断为：" + text);
                    }
                    cfg.ButtonText = text;
                    cfg.Save();
                });

            // 按钮不透明度滑块（0-100%，"中心填充百分比"；回调立即应用到游戏内按钮）
            helper.AddSlider("按钮不透明度（%）", 30f, 100f, 5f,
                (float)cfg.GetOpacityPercent(),
                value =>
                {
                    cfg.OpacityPercent = (int)value;
                    cfg.Save();
                    ScreenshotLinkerUI.SetOpacity(value);
                    ModLog.Info("按钮不透明度已设为 " + (int)value + "%");
                });

            helper.AddSpace(8);
            helper.AddButton("保存配置（重启模组生效）", () =>
            {
                cfg.Save();
                ModLog.Info("已手动保存配置");
            });
        }

        /// <summary>启动桌面端截图收集器（游戏内按钮 / 设置页按钮共用）。</summary>
        public static void LaunchDesktopApp()
        {
            string path = ScreenshotLinkerConfig.Current.GetDesktopAppPath();
            if (!System.IO.File.Exists(path))
            {
                ModLog.Warn("桌面端程序不存在：" + path);
                return;
            }
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = path;
                psi.UseShellExecute = true; // 等价双击启动；桌面端自带单实例保护，不会多开
                System.Diagnostics.Process.Start(psi);
                ModLog.Info("已请求启动桌面端：" + path);
            }
            catch (Exception ex)
            {
                ModLog.Warn("启动桌面端失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 反射探测游戏截图管理器并补丁其截图方法。
        /// 找不到类型/方法时不抛异常、直接降级（目录监听仍可推送）。
        /// </summary>
        private bool TryPatchScreenshot()
        {
            try
            {
                _harmony = new Harmony(HarmonyId);

                // 游戏截图管理器（天际线一代：Game.ScreenshotManager，程序集 Assembly-CSharp）
                // 注意：游戏 Mono 环境为 .NET 3.5 API surface，`type == null` 会触发
                // Type.op_Equality（.NET 4.0 才存在）→ MissingMethodException，必须用引用比较
                var type = Type.GetType("Game.ScreenshotManager, Assembly-CSharp");
                if (ReferenceEquals(type, null))
                {
                    ModLog.Info("未找到 Game.ScreenshotManager，跳过 Harmony 补丁");
                    return false;
                }

                var method = type.GetMethod("TakeScreenshot");
                if (ReferenceEquals(method, null))
                {
                    ModLog.Info("未找到 TakeScreenshot 方法，跳过 Harmony 补丁");
                    return false;
                }

                _harmony.Patch(method,
                    postfix: new HarmonyMethod(typeof(Patches.ScreenshotPatch), nameof(Patches.ScreenshotPatch.Postfix)));
                ModLog.Info("Harmony 补丁成功：" + type.FullName + ".TakeScreenshot");
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Harmony 补丁失败（降级目录监听）：" + ex.Message);
                return false;
            }
        }
    }
}
