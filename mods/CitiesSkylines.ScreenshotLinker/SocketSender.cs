using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 向桌面端「截图自动收集器」推送截图信息（TCP 127.0.0.1:23333）。
    /// </summary>
    public static class SocketSender
    {
        public const int DefaultPort = 23333;
        public static int Port = DefaultPort;

        /// <summary>发送成功返回 true</summary>
        public static bool Send(string path, string category, string timeText)
        {
            try
            {
                string json = "{\"path\":\"" + Escape(path) +
                              "\",\"category\":\"" + Escape(category) +
                              "\",\"time\":\"" + Escape(timeText) + "\"}";

                return SendData(json);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>发送心跳包（模组启动时调用，通知桌面端已连接）</summary>
        public static bool SendHeartbeat()
        {
            try
            {
                // category 设为 "心跳"，桌面端会识别此标记并只更新状态灯，不处理图片
                string json = "{\"path\":\"__heartbeat__\",\"category\":\"心跳\",\"time\":\"" +
                              DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "\"}";
                return SendData(json);
            }
            catch
            {
                return false;
            }
        }

        private static bool SendData(string json)
        {
            using (var client = new TcpClient())
            {
                var result = client.BeginConnect("127.0.0.1", Port, null, null);
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

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}