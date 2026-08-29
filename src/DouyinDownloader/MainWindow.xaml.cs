using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DouyinDownloader.ViewModels;

namespace DouyinDownloader;

public partial class MainWindow : Window
{
    private const double PreviewCornerRadius = 12;
    private const int GwlStyle = -16;
    private const int WsMaximizeBox = 0x10000;
    private const int WmSysCommand = 0x0112;
    private const int ScMaximize = 0xF030;
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GwlStyle);
        _ = SetWindowLong(hwnd, GwlStyle, style & ~WsMaximizeBox);

        if (HwndSource.FromHwnd(hwnd) is { } source)
        {
            source.AddHook(WndProc);
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSysCommand && (wParam.ToInt32() & 0xFFF0) == ScMaximize)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
            _viewModel = null;
        }
    }

    private void OnPreviewCardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            PreviewCornerRadius,
            PreviewCornerRadius);
    }

    private void OnCoverHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncPlayerSize();
    }

    private void OnPreviewMediaOpened(object sender, RoutedEventArgs e)
    {
        SyncPlayerSize();
    }

    private void SyncPlayerSize()
    {
        if (CoverHost.ActualWidth <= 0 || CoverHost.ActualHeight <= 0)
        {
            return;
        }

        PreviewPlayer.Width = CoverHost.ActualWidth;
        PreviewPlayer.Height = CoverHost.ActualHeight;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.PlaybackUri))
        {
            PreviewPlayer.Stop();
            PreviewPlayer.Source = _viewModel.PlaybackUri;
            if (_viewModel.PlaybackUri is not null && _viewModel.IsPlaying)
            {
                PreviewPlayer.Play();
                SyncPlayerSize();
            }
        }
        else if (e.PropertyName is nameof(MainViewModel.IsPlaying))
        {
            if (_viewModel.IsPlaying)
            {
                PreviewPlayer.Play();
            }
            else
            {
                PreviewPlayer.Pause();
            }
        }
        else if (e.PropertyName is nameof(MainViewModel.IsPlaybackVisible) && !_viewModel.IsPlaybackVisible)
        {
            PreviewPlayer.Stop();
            PreviewPlayer.Source = null;
            PreviewPlayer.Width = double.NaN;
            PreviewPlayer.Height = double.NaN;
        }
    }

    private async void OnPreviewMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.OnMediaFailedAsync().ConfigureAwait(true);
        }
    }

    private void OnPreviewMediaEnded(object sender, RoutedEventArgs e)
    {
        _viewModel?.OnMediaEnded();
    }
}
