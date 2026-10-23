using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ScreenshotCollector.Services;

/// <summary>
/// Windows 剪贴板位图读取（P/Invoke）：
/// 优先读 PNG 私有格式（微信/QQ 截图通常以 PNG 存入剪贴板），
/// 失败再读 CF_DIB（设备无关位图）并转成 BMP 流解码。
/// Avalonia 11 的 IClipboard 已移除图像专用方法，故用原生 API。
/// </summary>
internal static class WinClipboard
{
    private const uint CF_DIB = 8;

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalSize(IntPtr hMem);

    /// <summary>读取剪贴板中的位图；没有图片返回 null</summary>
    public static Bitmap? ReadBitmap()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            // 1. 优先 PNG 格式
            var pngHandle = GetClipboardData(RegisterClipboardFormat("PNG"));
            if (pngHandle != IntPtr.Zero)
            {
                var bytes = CopyGlobal(pngHandle);
                if (bytes != null)
                {
                    try
                    {
                        using var ms = new MemoryStream(bytes);
                        return new Bitmap(ms);
                    }
                    catch (Exception ex)
                    {
                        // PNG 数据存在但解码失败：可能是私有格式（无文件头），记录后继续尝试 DIB
                        LogService.Warn("Clipboard", $"剪贴板 PNG 解码失败：{ex.Message}");
                    }
                }
            }

            // 2. 其次 DIB（设备无关位图）
            var dibHandle = GetClipboardData(CF_DIB);
            if (dibHandle != IntPtr.Zero)
            {
                var bytes = CopyGlobal(dibHandle);
                if (bytes != null)
                {
                    try
                    {
                        return BitmapFromDib(bytes);
                    }
                    catch (Exception ex)
                    {
                        LogService.Warn("Clipboard", $"剪贴板 DIB 解码失败：{ex.Message}");
                    }
                }
            }

            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>把剪贴板内存块复制为字节数组</summary>
    private static byte[]? CopyGlobal(IntPtr hMem)
    {
        var size = GlobalSize(hMem);
        if (size == IntPtr.Zero) return null;

        var ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero) return null;

        try
        {
            var bytes = new byte[(int)size];
            Marshal.Copy(ptr, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(hMem);
        }
    }

    /// <summary>DIB（BITMAPINFOHEADER + 像素）转成标准 BMP 文件流再解码</summary>
    private static Bitmap? BitmapFromDib(byte[] dib)
    {
        if (dib.Length < 40) return null;

        // 标准 BMP 文件头（14 字节）+ DIB 数据
        var fileHeader = new byte[14];
        fileHeader[0] = (byte)'B';
        fileHeader[1] = (byte)'M';
        var fileSize = dib.Length + 14;
        fileHeader[2] = (byte)fileSize;
        fileHeader[3] = (byte)(fileSize >> 8);
        fileHeader[4] = (byte)(fileSize >> 16);
        fileHeader[5] = (byte)(fileSize >> 24);
        fileHeader[10] = 14 + 40; // bfOffBits：文件头 + BITMAPINFOHEADER（常规 BI_RGB 场景）

        var bmpBytes = fileHeader.Concat(dib).ToArray();
        using var ms = new MemoryStream(bmpBytes);
        return new Bitmap(ms);
    }

    // ==================== 写入：把图片复制到系统剪贴板（分享用） ====================

    /// <summary>把图片文件复制到剪贴板（PNG 私有格式 + CF_DIB 双格式，微信/QQ 均可粘贴）</summary>
    public static bool CopyImageToClipboard(string imagePath)
    {
        try
        {
            byte[] pngBytes;
            byte[] dibBytes;
            using (var bmp = new Bitmap(imagePath))
            {
                using var ms = new MemoryStream();
                bmp.Save(ms); // Avalonia 默认输出 PNG
                pngBytes = ms.ToArray();
                dibBytes = DibOf(bmp);
            }

            if (!OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                EmptyClipboard();
                SetClipboardBytes(RegisterClipboardFormat("PNG"), pngBytes);
                SetClipboardBytes(CF_DIB, dibBytes);
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>把 Avalonia 位图转成 DIB（BITMAPINFOHEADER + BGRA 像素，自下而上）</summary>
    private static byte[] DibOf(Bitmap bmp)
    {
        var w = bmp.PixelSize.Width;
        var h = bmp.PixelSize.Height;
        var stride = w * 4;
        var bufferSize = stride * h;

        byte[] pixels;
        var ptr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            // Avalonia 11：CopyPixels(PixelRect, IntPtr buffer, int bufferSize, int stride)，默认 Bgra8888 字节序（与 DIB 一致）
            bmp.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), ptr, bufferSize, stride);
            pixels = new byte[bufferSize];
            Marshal.Copy(ptr, pixels, 0, bufferSize);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        // BITMAPINFOHEADER（40 字节）
        var header = new byte[40];
        WriteInt32(header, 0, 40);            // biSize
        WriteInt32(header, 4, w);             // biWidth
        WriteInt32(header, 8, h);             // biHeight（正值 = 自下而上）
        WriteInt16(header, 12, 1);            // biPlanes
        WriteInt16(header, 14, 32);           // biBitCount
        WriteInt32(header, 16, 0);            // biCompression = BI_RGB
        WriteInt32(header, 20, bufferSize);   // biSizeImage

        var dib = new byte[40 + bufferSize];
        Array.Copy(header, dib, 40);
        // 像素自下而上：原第 0 行 → DIB 最后一行
        for (var row = 0; row < h; row++)
        {
            Array.Copy(pixels, row * stride, dib, 40 + (h - 1 - row) * stride, stride);
        }
        return dib;
    }

    private static void WriteInt16(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>把字节数组写入剪贴板指定格式（内存句柄所有权交给剪贴板）</summary>
    private static void SetClipboardBytes(uint format, byte[] data)
    {
        if (data == null || data.Length == 0) return;
        var hMem = GlobalAlloc(GMEM_MOVEABLE, data.Length);
        if (hMem == IntPtr.Zero) return;

        var ptr = GlobalLock(hMem);
        if (ptr != IntPtr.Zero)
        {
            Marshal.Copy(data, 0, ptr, data.Length);
            GlobalUnlock(hMem);
            SetClipboardData(format, hMem);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, int dwBytes);
}
