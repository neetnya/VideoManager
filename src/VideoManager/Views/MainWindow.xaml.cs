using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using VideoManager.ViewModels;

namespace VideoManager.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        Loaded += OnLoaded;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsDetailOpen) && vm.IsDetailOpen)
            {
                AnimateDrawer();
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var s = App.Host.Settings;

        // 用 Win32 拿当前显示器的真实工作区（像素），再换算成 WPF 的 DIP，
        // 避免高 DPI 下 SystemParameters.WorkArea 与实际不符导致窗口超出屏幕。
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var work = GetMonitorWorkAreaDip(this, dpi.DpiScaleX, dpi.DpiScaleY);

        var maxW = Math.Max(MinWidth, work.Width - 20);
        var maxH = Math.Max(MinHeight, work.Height - 40);

        var w = s.WindowWidth > 900 ? s.WindowWidth : 1360;
        var h = s.WindowHeight > 600 ? s.WindowHeight : 860;
        Width = Math.Min(w, maxW);
        Height = Math.Min(h, maxH);

        // 位置一律自己算：把窗口完全约束在显示器工作区内，
        // 否则 WPF 按 XAML 里的初始尺寸居中会落到屏幕外（顶部标题栏被裁掉）。
        WindowStartupLocation = WindowStartupLocation.Manual;

        var hasSaved = !double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop);
        var left = hasSaved ? s.WindowLeft : work.Left + (work.Width - Width) / 2;
        var top = hasSaved ? s.WindowTop : work.Top + (work.Height - Height) / 2;

        Left = Math.Min(Math.Max(left, work.Left), Math.Max(work.Left, work.Right - Width));
        Top = Math.Min(Math.Max(top, work.Top), Math.Max(work.Top, work.Bottom - Height));

        // 默认就是最大化：用户上次怎么退出不重要，打开就该铺满屏幕。
        // 同时把 Normal 状态的还原尺寸算好，取消最大化时不会变成奇怪的大小。
        WindowState = WindowState.Maximized;

        SearchBox.Focus();
    }

    private static Rect GetMonitorWorkAreaDip(System.Windows.Media.Visual visual, double scaleX, double scaleY)
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper((Window)visual).Handle;
            var monitor = MonitorFromWindow(handle, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor != IntPtr.Zero)
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var r = info.Work;
                    return new Rect(
                        r.Left / scaleX,
                        r.Top / scaleY,
                        (r.Right - r.Left) / scaleX,
                        (r.Bottom - r.Top) / scaleY);
                }
            }
        }
        catch
        {
            // 拿不到就退回 WPF 的值
        }

        var wa = SystemParameters.WorkArea;
        return new Rect(wa.X, wa.Y, wa.Width / scaleX, wa.Height / scaleY);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var s = App.Host.Settings;
        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
            s.WindowLeft = Left;
            s.WindowTop = Top;
        }
        s.WindowMaximized = WindowState == WindowState.Maximized;
        App.Host.Save();
    }

    private void AnimateDrawer()
    {
        var transform = new System.Windows.Media.TranslateTransform();
        DetailPanel.RenderTransform = transform;
        var anim = new DoubleAnimation(60, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        transform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_vm.IsDetailOpen) _vm.IsDetailOpen = false;
            else if (SearchBox.IsKeyboardFocused && SearchBox.Text.Length > 0)
            {
                SearchBox.Clear();
            }
        }
        else if (e.Key == Key.F5)
        {
            if (_vm.ScanCommand.CanExecute(null)) _vm.ScanCommand.Execute(null);
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    // ─── 窗口按钮 ───

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OnMaximize(sender, e);
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed && WindowState == WindowState.Normal)
        {
            DragMove();
        }
    }

    // ─── 拖放导入 ───

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] items) return;
        e.Handled = true;

        // 文件 → 取它所在的文件夹；文件夹 → 直接用
        var dirs = items
            .Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p))
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _vm.AddRootPathsAsync(dirs);
    }

    // ─── 卡片 / 单集交互 ───

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SeriesCardViewModel card })
        {
            _vm.SelectedCard = card;
            _vm.IsDetailOpen = true;
            e.Handled = true;
        }
    }

    private void OnEpisodeClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: EpisodeItemViewModel ep })
        {
            _vm.PlayEpisodeCommand.Execute(ep);
            e.Handled = true;
        }
    }

    private void OnDetailBackdropClick(object sender, MouseButtonEventArgs e)
    {
        _vm.IsDetailOpen = false;
        e.Handled = true;
    }

    private void OnTitleDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;

        var dialog = new RenameDialog(_vm.SelectedTitle) { Owner = this };
        if (dialog.ShowDialog() == true && _vm.SelectedCard is not null)
        {
            _vm.RenameSeries(_vm.SelectedCard, dialog.ResultTitle);
        }
    }
}
