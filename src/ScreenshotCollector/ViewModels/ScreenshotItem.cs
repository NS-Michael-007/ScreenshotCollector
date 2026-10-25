using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScreenshotCollector.ViewModels;

public partial class ScreenshotItem : BaseViewModel
{
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public DateTime CaptureTime { get; init; } = DateTime.Now;
    public string SourceLabel { get; init; } = string.Empty;

    public string CaptureTimeText => CaptureTime.ToString("yyyy-MM-dd HH:mm:ss");

    public string SourcePathDisplay => string.IsNullOrEmpty(SourcePath)
        ? "（历史归档项，未记录原始路径）"
        : $"原始路径：{SourcePath}";

    public static event Action<ScreenshotItem>? OnItemChecked;

    private bool isSelected;
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected != value)
            {
                isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
                if (value)
                {
                    OnItemChecked?.Invoke(this);
                }
            }
        }
    }

    private Bitmap? thumbnail;
    private bool isLoadingThumbnail = false;

    public Bitmap? Thumbnail
    {
        get
        {
            if (thumbnail == null && !isLoadingThumbnail && !string.IsNullOrEmpty(FilePath))
            {
                _ = LoadThumbnailAsync();
            }
            return thumbnail;
        }
        set
        {
            if (thumbnail != value)
            {
                thumbnail = value;
                OnPropertyChanged(nameof(Thumbnail));
            }
        }
    }

    private static readonly SemaphoreSlim decodeSemaphore = new SemaphoreSlim(3, 3);

    public async Task LoadThumbnailAsync()
    {
        if (thumbnail != null || isLoadingThumbnail) return;
        if (string.IsNullOrEmpty(FilePath)) return;

        isLoadingThumbnail = true;
        try
        {
            if (!File.Exists(FilePath))
            {
                System.Diagnostics.Debug.WriteLine($"[缩略图加载失败] 找不到文件: {FilePath}");
                return;
            }

            await decodeSemaphore.WaitAsync();
            try
            {
                Bitmap? decoded = null;
                await Task.Run(() =>
                {
                    try
                    {
                        using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        if (fs.Length > 0)
                        {
                            decoded = Bitmap.DecodeToWidth(fs, 220);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[缩略图解码异常] {FilePath}: {ex.Message}");
                    }
                });

                if (decoded != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => Thumbnail = decoded);
                }
            }
            finally
            {
                decodeSemaphore.Release();
            }
        }
        finally
        {
            isLoadingThumbnail = false;
        }
    }
}