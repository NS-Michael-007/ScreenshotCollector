using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using static ScreenshotCollector.Services.NativeMethods;

namespace ScreenshotCollector.Services;

/// <summary>
/// 纯原生 Win32 剪贴板监听服务（不依赖 WinForms）
/// </summary>
public class ClipboardMonitorService : IDisposable
{
    private Thread? _thread;
    private uint _threadId;
    private bool _isRunning;
    private NativeMessageWindow? _messageWindow;

    // 当捕获到图片时触发（已自动切回 Avalonia UI 线程）
    public event Action<Bitmap>? ImageCaptured;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = "Win32ClipboardMonitor"
        };

        // 剪贴板操作需要 STA 线程
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void ThreadProc()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _messageWindow = new NativeMessageWindow(OnClipboardUpdate);

        // 注册剪贴板监听
        if (!NativeMethods.AddClipboardFormatListener(_messageWindow.Handle))
        {
            LogService.Warn("Clipboard", "注册剪贴板监听失败！");
            return;
        }

        // 运行原生消息循环
        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
    }

    private int _lastTick;

    private void OnClipboardUpdate()
    {
        // 防抖：200ms 内的重复触发忽略
        int now = Environment.TickCount;
        if (now - _lastTick < 200) return;
        _lastTick = now;

        // 调用你写好的 WinClipboard 直接读取 Avalonia 的 Bitmap
        var bitmap = WinClipboard.ReadBitmap();
        if (bitmap != null)
        {
            // 切换到 Avalonia UI 线程触发事件，方便界面绑定
            Dispatcher.UIThread.Post(() =>
            {
                ImageCaptured?.Invoke(bitmap);
            });
        }
    }

    public void Dispose()
    {
        if (!_isRunning) return;

        if (_messageWindow != null)
        {
            NativeMethods.RemoveClipboardFormatListener(_messageWindow.Handle);
            _messageWindow.Destroy();
        }

        // 向后台消息循环发送退出指令
        if (_threadId != 0)
        {
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        _isRunning = false;
    }
}

/// <summary>
/// 纯原生隐藏窗口，用于接收 WM_CLIPBOARDUPDATE 消息
/// </summary>
internal class NativeMessageWindow
{
    public IntPtr Handle { get; private set; }
    private readonly Action _onMessage;

    // 必须保留委托引用，防止被垃圾回收导致崩溃
    private readonly WndProcDelegate _wndProcDelegate;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public NativeMessageWindow(Action onMessage)
    {
        _onMessage = onMessage;
        _wndProcDelegate = WndProc;

        // 1. 注册窗口类
        var wndClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            lpszClassName = "ClipboardMonitorWindow_" + Guid.NewGuid().ToString("N")
        };

        NativeMethods.RegisterClassEx(ref wndClass);

        // 2. 创建隐藏窗口
        Handle = NativeMethods.CreateWindowEx(
            0, wndClass.lpszClassName, "", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_CLIPBOARDUPDATE = 0x031D;

        if (msg == WM_CLIPBOARDUPDATE)
        {
            _onMessage?.Invoke();
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Destroy()
    {
        if (Handle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }
    }
}

/// <summary>
/// 封装原生 Win32 API
/// </summary>
internal static class NativeMethods
{
    public const uint WM_QUIT = 0x0012;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }
}