using System;

namespace CitiesSkylines.ScreenshotLinker.Patches
{
    /// <summary>
    /// Harmony 补丁：游戏截图保存完成后，把最新截图推送给桌面端。
    /// Postfix 采用无参数签名（Harmony 2.x 支持），与原方法签名零耦合，绝不因签名不匹配报错。
    /// </summary>
    public static class ScreenshotPatch
    {
        /// <summary>游戏内 F12 截图（Screenshots 目录）保存完成后触发</summary>
        public static void Postfix()
        {
            try
            {
                ScreenshotWatcher.PushLatest("天际线");
            }
            catch (Exception ex)
            {
                ModLog.Error("补丁触发推送异常：" + ex.Message);
            }
        }
    }
}
