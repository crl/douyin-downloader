using System.Windows;
using System.Windows.Threading;
using DouyinDownloader.ViewModels;

namespace DouyinDownloader;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "抖音无水印下载", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
