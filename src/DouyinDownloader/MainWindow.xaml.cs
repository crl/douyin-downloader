using System.Windows;
using DouyinDownloader.ViewModels;

namespace DouyinDownloader;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }
}
