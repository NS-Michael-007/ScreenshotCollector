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
    }
}
