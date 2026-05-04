using BLL;
using Fredy.Drilling.Holes.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using HAL;
using System.Windows;

namespace Fredy.Drilling.Holes.Views
{
    /// <summary>
    /// Window1.xaml 的交互逻辑
    /// </summary>
    public partial class ConfigWindow : Window
    {
        public ConfigWindow()
        {
            InitializeComponent();
            DataContext = App.ServiceProvider.GetRequiredService<ConfigViewModel>();
        }

        private void OpenNinePointCalibrationButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ConfigViewModel configViewModel)
            {
                return;
            }

            var motionService = App.ServiceProvider.GetService<IMotionService>();
            var camera = App.ServiceProvider.GetService<ICamera>();
            var logger = App.ServiceProvider.GetRequiredService<Serilog.ILogger>();
            var viewModel = new NinePointCalibrationViewModel(motionService, camera, logger)
            {
                ManualPixelSizeX = configViewModel.Camera.PixelSizeX,
                ManualPixelSizeY = configViewModel.Camera.PixelSizeY
            };

            var window = new NinePointCalibrationWindow
            {
                Owner = this,
                DataContext = viewModel,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var dialogResult = window.ShowDialog();
            if (dialogResult != true || window.CalibrationResult is null)
            {
                return;
            }

            configViewModel.Camera.PixelSizeX = window.CalibrationResult.PixelSizeXUm;
            configViewModel.Camera.PixelSizeY = window.CalibrationResult.PixelSizeYUm;
        }
    }
}
