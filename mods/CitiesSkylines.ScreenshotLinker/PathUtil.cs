using System;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 手写路径工具：零 System.IO.Path 依赖。
    /// 原因：游戏 Mono（.NET 3.5 surface）对 Path.Combine 多参重载支持不稳
    /// （Path.Combine(string,string,string) 是 .NET 4.0 才有的重载，曾导致
    /// MissingMethodException），这里全部用 .NET 1.x 基础 API 手写拼接，
    /// 任何 Mono 版本都必然存在。
    /// </summary>
    public static class PathUtil
    {
        /// <summary>拼接两个路径段（自动补分隔符），仅用字符串运算符（.NET 1.x）</summary>
        public static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            if (a.EndsWith("\\") || a.EndsWith("/")) return a + b;
            return a + "\\" + b;
        }

        /// <summary>拼接三个路径段</summary>
        public static string Join(string a, string b, string c)
        {
            return Join(Join(a, b), c);
        }

        /// <summary>取路径所在目录（去掉最后一段），仅用 LastIndexOf/Substring（.NET 1.x）</summary>
        public static string DirName(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            int idx = path.LastIndexOf('\\');
            if (idx < 0) idx = path.LastIndexOf('/');
            if (idx < 0) return string.Empty;
            return path.Substring(0, idx);
        }

        /// <summary>
        /// 取当前模组 DLL 所在目录。
        /// 优先 Assembly.Location（游戏 Mono 用 LoadFile/LoadFrom 加载时是文件路径）；
        /// 回退 CodeBase（个别加载方式下 Location 为空）；两者都不可用返回空串（调用方兜底当前目录）。
        /// 修复：游戏某 Mono 加载路径下 CodeBase 为空/抛异常，导致日志与配置写不出去（静默 catch 吞掉）。
        /// </summary>
        public static string ModDirectory()
        {
            try
            {
                var asm = typeof(PathUtil).Assembly;
                string loc = asm.Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    string dir = DirName(loc);
                    if (dir.Length > 0) return dir;
                }
                string cb = asm.CodeBase;
                if (!string.IsNullOrEmpty(cb))
                {
                    string dir = DirName(new Uri(cb).LocalPath);
                    if (dir.Length > 0) return dir;
                }
            }
            catch
            {
                // 任一路径取不到都不影响模组主流程
            }
            return string.Empty;
        }
    }
}
