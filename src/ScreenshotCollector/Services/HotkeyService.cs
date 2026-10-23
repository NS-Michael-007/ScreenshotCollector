using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScreenshotCollector.Services;

/// <summary>
/// 全局热键服务（阶段5-6）：
/// 用户如果自定义了某来源的快捷键（如把微信改成 Ctrl+Shift+A），
/// 本服务用低级键盘钩子(WH_KEYBOARD_LL)全局监听；
/// 按下自定义热键时，自动模拟发送该来源的原生截图快捷键（如微信的 Alt+A），
/// 从而用"自己习惯的键"触发微信/QQ 截图，截图进剪贴板后再由剪贴板服务自动保存。
/// 默认快捷键本身就是原生键，无需注册（避免双重触发）。
/// </summary>
public class HotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_MENU = 0x12;

    private static readonly List<(string Custom, string Source, string Native)> Entries = new();
    private static IntPtr HookHandle;
    private Thread? _hookThread;
    private static volatile bool Suppress; // 模拟按键期间屏蔽自身钩子，防止循环

    private static readonly LowLevelKeyboardProc Callback = HookProc;

    /// <summary>注册一个自定义热键：按下 Custom 时模拟发送 Native</summary>
    public void Register(string customHotkey, string source, string nativeHotkey)
    {
        if (string.IsNullOrWhiteSpace(customHotkey) || string.IsNullOrWhiteSpace(nativeHotkey)) return;
        Entries.Add((customHotkey.Trim(), source, nativeHotkey.Trim()));
    }

    /// <summary>已注册的自定义热键数量（用于状态灯显示）</summary>
    public static int RegisteredCount => Entries.Count;

    /// <summary>启动全局监听（专用线程 + 消息循环）</summary>
    public void Start()
    {
        _hookThread = new Thread(() =>
        {
            HookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, Callback, GetModuleHandle(null), 0);
            if (HookHandle == IntPtr.Zero)
            {
                LogService.Error("Hotkey", $"全局热键钩子安装失败（错误码 {Marshal.GetLastWin32Error()}）");
                return;
            }
            LogService.Info("Hotkey", $"全局热键监听已启动（已注册 {Entries.Count} 个自定义热键）");

            // 消息循环：低级钩子回调需要消息泵
            while (GetMessage(out _, IntPtr.Zero, 0, 0)) { }
        })
        {
            IsBackground = true
        };
        _hookThread.Start();
    }

    /// <summary>低级键盘钩子回调（在钩子线程执行）</summary>
    private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN && !Suppress)
        {
            try
            {
                var vk = Marshal.ReadInt32(lParam);

                // 遍历已注册的自定义热键
                foreach (var entry in Entries)
                {
                    var (mods, mainKey) = Parse(entry.Custom);
                    if (vk == mainKey && ModsMatch(mods))
                    {
                        LogService.Info("Hotkey", $"热键触发：{entry.Custom} → {entry.Source}");
                        SimulateNative(entry.Native);
                        break;
                    }
                }
            }
            catch { /* 钩子异常不影响系统 */ }
        }
        return CallNextHookEx(HookHandle, nCode, wParam, lParam);
    }

    /// <summary>模拟发送原生截图快捷键到当前前台窗口</summary>
    private static void SimulateNative(string nativeHotkey)
    {
        var (mods, mainKey) = Parse(nativeHotkey);
        if (mainKey == 0) return;

        Suppress = true;
        try
        {
            if (mods.Contains("Ctrl")) keybd_event(VK_CONTROL, 0, 0, 0);
            if (mods.Contains("Alt")) keybd_event(VK_MENU, 0, 0, 0);
            if (mods.Contains("Shift")) keybd_event(VK_SHIFT, 0, 0, 0);

            keybd_event(mainKey, 0, 0, 0);
            keybd_event(mainKey, 0, KEYEVENTF_KEYUP, 0);

            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, 0);
        }
        finally
        {
            Suppress = false;
        }
    }

    /// <summary>解析 "Ctrl+Alt+A" 为 修饰键列表 + 主键虚拟键码</summary>
    private static (List<string> Mods, byte MainKey) Parse(string hotkey)
    {
        var mods = new List<string>();
        byte main = 0;
        foreach (var part in hotkey.Split('+'))
        {
            var t = part.Trim();
            if (t.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods.Add("Ctrl");
            else if (t.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods.Add("Alt");
            else if (t.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods.Add("Shift");
            else main = KeyCodeOf(t);
        }
        return (mods, main);
    }

    private static byte KeyCodeOf(string name) => name switch
    {
        "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
        "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
        "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
        "Esc" => 0x1B, "Space" => 0x20, "Enter" => 0x0D,
        _ when name.Length == 1 => (byte)char.ToUpperInvariant(name[0]),
        _ => 0
    };

    /// <summary>检查当前物理按下的修饰键与热键要求是否一致</summary>
    private static bool ModsMatch(List<string> required)
    {
        bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

        return required.Contains("Ctrl") == ctrl
            && required.Contains("Alt") == alt
            && required.Contains("Shift") == shift;
    }

    /// <summary>释放钩子并结束线程</summary>
    public void Dispose()
    {
        if (HookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(HookHandle);
            HookHandle = IntPtr.Zero;
        }
        _hookThread?.Join(500);
        _hookThread = null;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }
}
