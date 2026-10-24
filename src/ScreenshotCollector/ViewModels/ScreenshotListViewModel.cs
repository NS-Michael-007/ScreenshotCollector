using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenshotCollector.Services;

namespace ScreenshotCollector.ViewModels;

/// <summary>
/// 中间截图列表的 ViewModel：
/// 保存全部截图（AllItems），按【来源过滤 + 搜索关键词】重建时间分组（今天/昨天/本周/更早），
/// 维护选中项，并据此切换右侧「信封占位 / 大图预览」。
/// </summary>
public partial class ScreenshotListViewModel : BaseViewModel
{
    // 四个时间分组（QQ邮箱邮件分组风格）
    private readonly ScreenshotGroup _today = new("今天");
    private readonly ScreenshotGroup _yesterday = new("昨天");
    private readonly ScreenshotGroup _thisWeek = new("本周");
    private readonly ScreenshotGroup _earlier = new("更早");

    /// <summary>分组集合（顺序即显示顺序：今天→昨天→本周→更早）</summary>
    public ObservableCollection<ScreenshotGroup> Groups { get; } = new();

    /// <summary>全部截图（完整数据源，过滤时按此重建分组）</summary>
    public ObservableCollection<ScreenshotItem> AllItems { get; } = new();

    /// <summary>搜索关键词（顶部搜索框双向绑定，输入即实时过滤）</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    private string? _sourceFilter; // 当前来源过滤条件；null = 全部

    [ObservableProperty]
    private ScreenshotItem? _selectedItem;

    /// <summary>未选中时显示信封占位</summary>
    public bool IsPlaceholderVisible => SelectedItem == null;

    /// <summary>选中时显示大图预览</summary>
    public bool IsPreviewVisible => SelectedItem != null;

    /// <summary>列表是否为空（无任何截图时显示引导提示）</summary>
    public bool IsEmpty => AllItems.Count == 0;

    public ScreenshotListViewModel()
    {
        Groups.Add(_today);
        Groups.Add(_yesterday);
        Groups.Add(_thisWeek);
        Groups.Add(_earlier);
    }

    /// <summary>选中项变化时，自动刷新右侧面板显隐</summary>
    partial void OnSelectedItemChanged(ScreenshotItem? value)
    {
        OnPropertyChanged(nameof(IsPlaceholderVisible));
        OnPropertyChanged(nameof(IsPreviewVisible));
    }

    /// <summary>搜索词变化 → 立即重建分组（实时过滤）</summary>
    partial void OnSearchTextChanged(string value) => RebuildGroups();

    /// <summary>切换左侧菜单：设置来源过滤条件并重建分组</summary>
    public void ApplyFilter(string? source)
    {
        _sourceFilter = source;
        RebuildGroups();
    }

    /// <summary>批量加载已有截图（程序启动时把归档目录的全部截图加入列表）</summary>
    public void LoadExisting(IEnumerable<ScreenshotItem> items)
    {
        // 强制在主线程中操作，避免任何跨线程引发的 UI 不渲染问题
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => LoadExisting(items));
            return;
        }

        // 清空旧数据
        AllItems.Clear();

        var list = items.ToList();
        foreach (var item in list)
        {
            AllItems.Add(item);
            _ = item.LoadThumbnailAsync();
        }

        RebuildGroups();

        // 强制通知 UI 刷新
        OnPropertyChanged(nameof(AllItems));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>新增一条截图记录（新截图在组内置顶）</summary>
    public void Add(ScreenshotItem item)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Add(item));
            return;
        }

        AllItems.Insert(0, item);
        _ = item.LoadThumbnailAsync();
        RebuildGroups();

        // 强制通知 UI 刷新
        OnPropertyChanged(nameof(AllItems));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>按【来源 + 搜索词】过滤全部截图，重建四个时间分组</summary>
    private void RebuildGroups()
    {
        foreach (var group in Groups) group.Items.Clear();

        var text = (SearchText ?? string.Empty).Trim();

        // 🚀【修复】强制按时间降序排列，保证最新的截图永远在最上面
        var filteredItems = AllItems.OrderByDescending(i => i.CaptureTime).ToList();

        foreach (var item in filteredItems)
        {
            // 1. 来源过滤（点击左侧菜单）
            if (_sourceFilter != null &&
                !string.Equals(item.SourceLabel, _sourceFilter, StringComparison.Ordinal))
            {
                continue;
            }

            // 2. 搜索过滤（文件名 / 来源 / 时间文本）
            if (text.Length > 0)
            {
                var hit = item.FileName.Contains(text, StringComparison.OrdinalIgnoreCase)
                       || item.SourceLabel.Contains(text, StringComparison.OrdinalIgnoreCase)
                       || item.CaptureTimeText.Contains(text, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }

            // 3. 按时间归组（🚀【修复】使用 Add 而不是 Insert(0)，因为已经是降序遍历）
            GroupOf(item.CaptureTime).Items.Add(item);
        }

        // 强制通知 UI 刷新绑定
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>根据时间判断属于哪个分组</summary>
    private ScreenshotGroup GroupOf(DateTime time)
    {
        var today = DateTime.Today;
        if (time.Date == today) return _today;
        if (time.Date == today.AddDays(-1)) return _yesterday;

        // 本周起点 = 本周一（DayOfWeek：周日=0，周一=1）
        var daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
        if (time.Date >= today.AddDays(-daysFromMonday)) return _thisWeek;

        return _earlier;
    }

    /// <summary>打开选中截图的所在目录（explorer 定位到该文件）</summary>
    [RelayCommand]
    private void OpenFileLocation()
    {
        if (SelectedItem == null) return;
        try
        {
            var path = SelectedItem.FilePath;
            if (!File.Exists(path))
            {
                LogService.Warn("UI", $"文件不存在：{path}");
                return;
            }
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            LogService.Info("UI", $"打开文件位置：{path}");
        }
        catch (Exception ex)
        {
            LogService.Error("UI", $"打开文件位置失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 打开选中截图在来源目录（游戏内）的真实位置——如 Snapshots\&lt;UUID&gt;\snapshot.png。
    /// 原始路径为空（历史归档项）或文件已被删除时给出提示，不静默失败。
    /// </summary>
    [RelayCommand]
    private void OpenSourceLocation()
    {
        if (SelectedItem == null) return;
        var path = SelectedItem.SourcePath;
        if (string.IsNullOrEmpty(path))
        {
            LogService.Warn("UI", "该条目为历史归档项，未记录原始路径");
            return;
        }
        if (!File.Exists(path))
        {
            LogService.Warn("UI", $"原始文件不存在（可能已在游戏内删除）：{path}");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            LogService.Info("UI", $"打开原始位置：{path}");
        }
        catch (Exception ex)
        {
            LogService.Error("UI", $"打开原始位置失败：{ex.Message}");
        }
    }

    /// <summary>复制选中截图路径到剪贴板：优先复制原始路径（真实路径），无原始路径时复制归档路径</summary>
    [RelayCommand]
    private async Task CopyPath()
    {
        if (SelectedItem == null) return;
        try
        {
            var copyPath = !string.IsNullOrEmpty(SelectedItem.SourcePath)
                ? SelectedItem.SourcePath
                : SelectedItem.FilePath;
            var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(copyPath);
                LogService.Info("UI", $"已复制路径：{copyPath}");
            }
        }
        catch (Exception ex)
        {
            LogService.Error("UI", $"复制路径失败：{ex.Message}");
        }
    }

    /// <summary>删除选中截图：真实删除本地文件 + 从列表移除</summary>
    [RelayCommand]
    private void Delete()
    {
        if (SelectedItem == null) return;
        var item = SelectedItem;

        try
        {
            if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
            if (File.Exists(item.FilePath + ".src")) File.Delete(item.FilePath + ".src"); // 原始路径元数据一并删除
        }
        catch (Exception ex)
        {
            LogService.Error("UI", $"删除文件失败：{ex.Message}");
        }

        // 从全部数据源移除并重建分组
        AllItems.Remove(item);
        SelectedItem = null;
        RebuildGroups();
        LogService.Info("UI", $"已删除截图：{item.FileName}");
    }

    /// <summary>全选 / 取消全选：只针对当前界面上可见的截图生效</summary>
    [RelayCommand]
    private void SelectAll()
    {
        // 🚀【修复】获取当前界面上可见的所有图片（过滤后的），而不是底层的全部数据
        var visibleItems = Groups.SelectMany(g => g.Items).ToList();
        if (visibleItems.Count == 0) return;

        // 如果当前可见的已经全部选中，则执行取消全选；否则执行全选
        var allSelected = visibleItems.All(i => i.IsSelected);

        foreach (var item in visibleItems)
        {
            item.IsSelected = !allSelected;
        }
    }

    /// <summary>批量删除勾选的截图（真实删除本地文件）</summary>
    [RelayCommand]
    private void DeleteSelected()
    {
        // 🚀 获取所有被勾选的项（全局查找，因为可能有些被勾选了但在其他分组）
        var items = AllItems.Where(i => i.IsSelected).ToList();
        if (items.Count == 0)
        {
            LogService.Warn("UI", "没有勾选任何截图");
            return;
        }

        foreach (var item in items)
        {
            try
            {
                if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
                if (File.Exists(item.FilePath + ".src")) File.Delete(item.FilePath + ".src"); // 原始路径元数据一并删除
            }
            catch (Exception ex)
            {
                LogService.Error("UI", $"删除文件失败 {item.FileName}：{ex.Message}");
            }
            AllItems.Remove(item);
        }

        if (SelectedItem != null && items.Contains(SelectedItem)) SelectedItem = null;
        RebuildGroups();
        LogService.Info("UI", $"批量删除 {items.Count} 张截图");
    }

    // ---------- 分享：复制图片到剪贴板 + 唤起微信/QQ 聊天窗口 ----------

    /// <summary>分享到微信：图片复制到剪贴板，唤起微信窗口，用户在聊天框 Ctrl+V 粘贴</summary>
    [RelayCommand]
    private void ShareToWeChat()
    {
        ShareTo("WeChat", "Weixin", "微信");
    }

    /// <summary>分享到QQ：图片复制到剪贴板，唤起QQ窗口，用户在聊天框 Ctrl+V 粘贴</summary>
    [RelayCommand]
    private void ShareToQQ()
    {
        ShareTo("QQ", "QQNT", "QQ");
    }

    private void ShareTo(string process1, string process2, string displayName)
    {
        if (SelectedItem == null || !File.Exists(SelectedItem.FilePath))
        {
            LogService.Warn("UI", "没有可分享的截图（文件不存在）");
            return;
        }

        if (!WinClipboard.CopyImageToClipboard(SelectedItem.FilePath))
        {
            LogService.Error("UI", "复制图片到剪贴板失败");
            return;
        }

        // 唤起对应聊天软件主窗口（新/旧进程名都试）
        var activated = WindowActivator.Activate(process1) || WindowActivator.Activate(process2);
        if (activated)
        {
            LogService.Info("UI", $"已复制图片到剪贴板，正在唤起{displayName}…（在聊天框按 Ctrl+V 粘贴）");
        }
        else
        {
            LogService.Warn("UI", $"图片已复制到剪贴板，但未找到{displayName}窗口（{displayName}未打开？）");
        }
    }
}