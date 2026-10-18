using System.Windows;

namespace VideoManager.Views;

/// <summary>一个和 RenameDialog 同样风格的确认框，用于「删除到回收站」这类不可轻率的操作。</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string headline, string detail, string confirmText = "删掉")
    {
        InitializeComponent();
        HeadlineText.Text = headline;
        DetailText.Text = detail;
        ConfirmButton.Content = confirmText;
    }

    /// <summary>弹一个确认框；用户点了确认按钮才返回 true。</summary>
    public static bool Ask(Window? owner, string headline, string detail, string confirmText = "删掉")
    {
        var dlg = new ConfirmDialog(headline, detail, confirmText);
        if (owner is not null && !ReferenceEquals(owner, dlg))
        {
            dlg.Owner = owner;
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dlg.ShowDialog() == true;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
