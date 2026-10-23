using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ScreenshotCollector.Views.Controls;

/// <summary>
/// 快捷键录入控件：
/// 1. 点击进入录制状态，界面提示「请按下快捷键…」
/// 2. 按下组合键（如 Ctrl+Alt+A）→ 立即替换 Hotkey 值，并退出录制
/// 3. 按 Esc 取消本次录制；按 Delete / Backspace 清空快捷键
/// </summary>
public partial class HotkeyBox : UserControl
{
    private bool _recording;

    /// <summary>当前快捷键文本（双向绑定，替换后自动写回 ViewModel）</summary>
    public static readonly StyledProperty<string> HotkeyProperty =
        AvaloniaProperty.Register<HotkeyBox, string>(nameof(Hotkey), string.Empty);

    public string Hotkey
    {
        get => GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public HotkeyBox()
    {
        InitializeComponent();

        // 点击进入录制态
        RootBorder.PointerPressed += (_, _) => StartRecording();

        // 录制态下捕获按键
        RootBorder.KeyDown += OnKeyDown;

        // 外部值变化时刷新显示
        HotkeyProperty.Changed.AddClassHandler<HotkeyBox>((_, _) => UpdateDisplay());

        UpdateDisplay();
    }

    private void StartRecording()
    {
        _recording = true;
        RootBorder.Focus();
        UpdateDisplay();
    }

    private void StopRecording()
    {
        _recording = false;
        UpdateDisplay();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true; // 阻止事件继续冒泡，避免误触界面其它控件

        // Esc 取消本次录制
        if (e.Key == Key.Escape)
        {
            StopRecording();
            return;
        }

        // Delete / Backspace 清空快捷键（相当于移除该来源的快捷键）
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            Hotkey = string.Empty;
            StopRecording();
            return;
        }

        // 只按了修饰键（Ctrl/Shift/Alt）本身，不算完整快捷键
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)
        {
            return;
        }

        // 组装组合键文本，例如：Ctrl+Alt+A
        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        parts.Add(e.Key.ToString());

        Hotkey = string.Join("+", parts); // 替换为新快捷键
        StopRecording();
    }

    private void UpdateDisplay()
    {
        if (DisplayText == null) return; // InitializeComponent 尚未完成

        if (_recording)
        {
            DisplayText.Text = "请按下快捷键…（Esc取消）";
            RootBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFF7E6"));
            RootBorder.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F59E0B"));
        }
        else
        {
            DisplayText.Text = string.IsNullOrEmpty(Hotkey) ? "（无快捷键）" : Hotkey;
            RootBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F1F5F9"));
            RootBorder.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E5E7EB"));
        }
    }
}
