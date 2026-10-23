using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 向桌面端「截图自动收集器」推送截图信息（TCP 127.0.0.1:23333）。
    /// 协议：一行 JSON（与桌面端 SocketGameService 完全一致）：
    ///   {"path":"D:\\...\\1.png","category":"天际线","time":"2026-10-23T12:00:00"}
    /// category 取值：天际线 / 编辑器快照
    /// </summary>
    public static class SocketSender
    {
        public const int DefaultPort = 23333;

        /// <summary>发送成功返回 true；桌面端未启动 / 网络异常返回 false（不抛异常）</summary>
        public static bool Send(string path, string category, string timeText)
        {
            try
            {
                string json = "{\"path\":\"" + Escape(path) +
                              "\",\"category\":\"" + Escape(category) +
                              "\",\"time\":\"" + Escape(timeText) + "\"}";

                using (var client = new TcpClient())
                {
                    // 2 秒连接超时，避免阻塞游戏；
                    // 用 BeginConnect/EndConnect（.NET 2.0 API）而非 ConnectAsync（.NET 4.5），
                    // 兼容游戏 Mono 的 .NET 3.5 API surface
                    var result = client.BeginConnect("127.0.0.1", DefaultPort, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(2000))
                    {
                        client.Close();
                        return false;
                    }
                    client.EndConnect(result);

                    using (var stream = client.GetStream())
                    {
                        byte[] data = Encoding.UTF8.GetBytes(json + "\n");
                        stream.Write(data, 0, data.Length);
                        stream.Flush();
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
