using System.Windows;

namespace VideoManager.Views;

public partial class RenameDialog : Window
{
    public RenameDialog(string current)
    {
        InitializeComponent();
        TitleBox.Text = current;
        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    public string ResultTitle { get; private set; } = "";

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var text = TitleBox.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }
        ResultTitle = text;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
