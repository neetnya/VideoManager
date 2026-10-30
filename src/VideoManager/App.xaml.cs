using System.Windows;
using System.Windows.Threading;
using VideoManager.ViewModels;
using VideoManager.Views;

namespace VideoManager;

public partial class App : Application
{
    public static AppStorageHost Host { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                ShowFatal(ex);
            }
        };

        Host = new AppStorageHost();
        var vm = new MainViewModel(Host);
        var window = new MainWindow(vm);
        MainWindow = window;
        window.Show();

        _ = vm.StartupAsync();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatal(e.Exception);
        e.Handled = true;
    }

    private static void ShowFatal(Exception ex)
    {
        try
        {
            MessageBox.Show(
                $"出了点小状况……\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                "鲸鱼娘打了个嗝",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // 弹窗都失败就只能算了
        }
    }
}
