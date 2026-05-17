using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fredy.Drilling.Holes.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Mat = OpenCvSharp.Mat;

namespace Fredy.Drilling.Holes.Views
{
    public partial class CenterRoiBinarySettingsWindow : System.Windows.Window
    {
        private readonly Mat? _sourceMat;
        private readonly BitmapSource? _sourceBitmap;

        public CenterRoiBinarySettingsViewModel ViewModel { get; }

        public CenterRoiBinarySettingsWindow(Mat? sourceMat, ImageSource? source)
        {
            InitializeComponent();

            _sourceMat = sourceMat;
            _sourceBitmap = source as BitmapSource;

            var configService = App.ServiceProvider.GetRequiredService<Services.ConfigService>();
            var logger = App.ServiceProvider.GetRequiredService<Serilog.ILogger>();
            ViewModel = new CenterRoiBinarySettingsViewModel(configService, logger);
            DataContext = ViewModel;

            ViewModel.SettingsChanged += UpdatePreview;
            Loaded += (_, _) => UpdatePreview();
            Closed += (_, _) => _sourceMat?.Dispose();
        }

        private void UpdatePreview()
        {
            ImageOriginal.Source = _sourceBitmap ?? (_sourceMat != null ? Tools.VisionUIHelper.MatToBitmapSource(_sourceMat) : null);

            using var preview = _sourceMat != null && !_sourceMat.Empty()
                ? Tools.VisionUIHelper.BuildCenterRoiBinaryPreview(_sourceMat, ViewModel.RoiWidth, ViewModel.RoiHeight, ViewModel.Threshold, ViewModel.Invert, ViewModel.CircleRadius)
                : _sourceBitmap != null
                    ? Tools.VisionUIHelper.BuildCenterRoiBinaryPreview(_sourceBitmap, ViewModel.RoiWidth, ViewModel.RoiHeight, ViewModel.Threshold, ViewModel.Invert, ViewModel.CircleRadius)
                    : null;

            if (preview == null)
            {
                ImageRoi.Source = null;
                ImageBinary.Source = null;
                return;
            }

            ImageRoi.Source = Tools.VisionUIHelper.MatToBitmapSource(preview.RoiImage);
            ImageBinary.Source = Tools.VisionUIHelper.MatToBitmapSource(preview.BinaryImage);
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SaveConfigCommand?.Execute(null);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
