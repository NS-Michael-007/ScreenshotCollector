using CommunityToolkit.Mvvm.ComponentModel;

namespace ScreenshotCollector.ViewModels;

/// <summary>
/// 所有 ViewModel 的基类。
/// 继承 ObservableObject（CommunityToolkit.Mvvm），自动提供属性变更通知。
/// </summary>
public abstract class BaseViewModel : ObservableObject
{
}
