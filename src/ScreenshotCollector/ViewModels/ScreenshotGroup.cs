using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ScreenshotCollector.ViewModels;

/// <summary>
/// 一个时间分组（QQ邮箱邮件分组风格）：组标题 + 组内截图集合。
/// 支持折叠/展开：折叠后只显示组标题。
/// </summary>
public partial class ScreenshotGroup : BaseViewModel
{
    public ScreenshotGroup(string title)
    {
        GroupTitle = title;

        // 组内截图数量变化时，刷新「是否为空」状态（空组自动隐藏标题）
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasItems));
    }

    /// <summary>分组标题：今天 / 昨天 / 本周 / 更早</summary>
    public string GroupTitle { get; }

    /// <summary>该组内的截图集合</summary>
    public ObservableCollection<ScreenshotItem> Items { get; } = new();

    /// <summary>分组是否展开（true 显示组内截图，false 只显示组标题）</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>该组是否有截图（空组不显示组标题）</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>点击组标题：折叠 / 展开切换</summary>
    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;
}
