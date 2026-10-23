using ICities;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 模组入口：截图自动收集器 联动端。
    /// 游戏内 F12 截图 / 编辑器快照生成后，通过本地 TCP 23333 推送到桌面端
    /// 「截图自动收集器」（ScreenshotCollector），实现即时归档 + 状态灯联动。
    /// </summary>
    public class Mod : IUserMod
    {
        private ScreenshotWatcher _watcher;

        /// <summary>游戏内模组名称（内容管理器中显示）</summary>
        public string Name => "截图自动收集器联动（ScreenshotLinker）";

        /// <summary>模组描述</summary>
        public string Description =>
            "把天际线 F12 截图与编辑器快照实时推送到桌面端「截图自动收集器」（TCP 23333），" +
            "自动归档并点亮状态灯。需配合桌面端软件使用。";

        /// <summary>模组启用：启动目录监听</summary>
        public void OnEnabled()
        {
            if (_watcher == null) _watcher = new ScreenshotWatcher();
            _watcher.Start();
            ModLog.Info("模组已启用，开始监听游戏截图目录");
        }

        /// <summary>模组停用：释放监听</summary>
        public void OnDisabled()
        {
            if (_watcher != null) _watcher.Stop();
            ModLog.Info("模组已停用");
        }
    }
}
