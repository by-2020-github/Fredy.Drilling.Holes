using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Fredy.Drilling.Holes.ViewModels;

namespace Fredy.Drilling.Holes.Views
{
    public partial class CircleDetectionSettingsWindow : Window
    {
        public CircleDetectionSettingsViewModel ViewModel { get; }

        public CircleDetectionSettingsWindow(ImageSource source)
        {
            InitializeComponent();

            var configService = App.ServiceProvider.GetRequiredService<Services.ConfigService>();
            var logger = App.ServiceProvider.GetRequiredService<Serilog.ILogger>();
            ViewModel = new CircleDetectionSettingsViewModel(configService, logger);
            DataContext = ViewModel;

            ViewModel.OnSettingsChanged += () =>
            {
                UpdatePreview();
            };

            PreviewViewer.ImageSource = source;
            this.Loaded += (s, e) => UpdatePreview();
        }

        private void UpdatePreview()
        {
            using var result = PreviewViewer.RunCircleDetection(
                isDarkHole: ViewModel.IsDarkHoleTarget,
                minRadius: ViewModel.MinRadius,
                maxRadius: ViewModel.MaxRadius,
                param1: ViewModel.Param1,
                param2: ViewModel.Param2
            );

            if (result != null)
            {
                ImageBinary.Source = Tools.VisionUIHelper.MatToBitmapSource(result.BinaryImage);
                ImageEdges.Source = Tools.VisionUIHelper.MatToBitmapSource(result.EdgesImage);
                ImageContours.Source = Tools.VisionUIHelper.MatToBitmapSource(result.ContoursImage);
            }
            else
            {
                ImageBinary.Source = null;
                ImageEdges.Source = null;
                ImageContours.Source = null;
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SaveConfigCommand.Execute(null);
            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
