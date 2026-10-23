using System;
using Harmony;
using ICities;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 模组入口：截图自动收集器 联动端。
    /// 采用工坊 Harmony 模组框架结构：
    ///   1) Harmony 初始化并补丁游戏截图入口（反射探测，命中即即时推送）；
    ///   2) 目录监听兜底（无论补丁是否命中，截图落盘都能推送）。
    /// 游戏内 F12 截图 / 编辑器快照 → 本地 TCP 23333 推送到桌面端「截图自动收集器」。
    /// </summary>
    public class Mod : IUserMod
    {
        /// <summary>Harmony 实例唯一标识（卸载时用于 UnpatchAll）</summary>
        public const string HarmonyId = "com.wangsiji.screenshotlinker";

        private ScreenshotWatcher _watcher;
        private HarmonyInstance _harmony;
        private bool _harmonyActive;

        /// <summary>游戏内模组名称（内容管理器中显示）</summary>
        public string Name => "截图自动收集器联动（ScreenshotLinker）";

        /// <summary>模组描述</summary>
        public string Description =>
            "把天际线 F12 截图与编辑器快照实时推送到桌面端「截图自动收集器」（TCP 23333），" +
            "自动归档并点亮状态灯。基于 Harmony 框架（随包自带 0Harmony.dll），需配合桌面端软件使用。";

        /// <summary>模组启用：Harmony 补丁 + 目录监听双通道</summary>
        public void OnEnabled()
        {
            // 通道1：Harmony 补丁游戏截图入口（反射探测，类名/方法名不硬编码）
            _harmonyActive = TryPatchScreenshot();

            // 通道2：目录监听兜底（保证任何情况下都能推送）
            if (_watcher == null) _watcher = new ScreenshotWatcher();
            _watcher.Start();

            ModLog.Info("模组已启用：Harmony " + (_harmonyActive ? "补丁成功" : "补丁未命中（目录监听兜底）"));
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
            ModLog.Info("模组已停用");
        }

        /// <summary>
        /// 反射探测游戏截图管理器并补丁其截图方法。
        /// 找不到类型/方法时不抛异常、直接降级（目录监听仍可推送）。
        /// </summary>
        private bool TryPatchScreenshot()
        {
            try
            {
                _harmony = HarmonyInstance.Create(HarmonyId);

                // 游戏截图管理器（天际线一代：Game.ScreenshotManager，程序集 Assembly-CSharp）
                var type = Type.GetType("Game.ScreenshotManager, Assembly-CSharp");
                if (type == null)
                {
                    ModLog.Info("未找到 Game.ScreenshotManager，跳过 Harmony 补丁");
                    return false;
                }

                var method = type.GetMethod("TakeScreenshot");
                if (method == null)
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
