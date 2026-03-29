using Fredy.Drilling.Holes.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace Fredy.Drilling.Holes.Views
{
    /// <summary>
    /// ScanWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ScanWindow : Window
    {
        private readonly MainViewModel? _mainViewModel;
        private bool _mainPreviewSuspended;

        public ScanWindow()
        {
            InitializeComponent();
            DataContext = App.ServiceProvider.GetService<ScanViewModel>() ?? new ScanViewModel();
            _mainViewModel = App.ServiceProvider.GetService<MainViewModel>();

            Loaded += ScanWindow_Loaded;
            Closed += ScanWindow_Closed;
        }

        private void ScanWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_mainViewModel is null || _mainPreviewSuspended)
            {
                return;
            }

            _mainViewModel.SuspendCameraPreview();
            _mainPreviewSuspended = true;
        }

        private void ScanWindow_Closed(object? sender, EventArgs e)
        {
            if (_mainViewModel is null || !_mainPreviewSuspended)
            {
                return;
            }

            _mainViewModel.ResumeCameraPreview();
            _mainPreviewSuspended = false;
        }
    }
}
