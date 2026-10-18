using System.Windows;

namespace VideoManager.Common;

/// <summary>
/// 把 Window 的 DataContext 递进 ContextMenu 里。
///
/// 右键菜单的视觉树挂在 Popup 下面，不在 Window 的树里，所以
/// <c>RelativeSource AncestorType=Window</c> 找不到任何东西 —— 绑定会静默失败，
/// Command 变成 null，菜单项点下去毫无反应。「全都无效」就是这么来的。
/// Freezable 可以跨过这条边界继承 DataContext，于是用它当桥。
/// </summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy),
            new UIPropertyMetadata(null));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
