using System.Windows;
using Fredy.Drilling.Holes.UserControls;
using Fredy.Drilling.Holes.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fredy.Drilling.Holes.Views
{
    public partial class WorkpieceCenterCalibrationWindow : Window
    {
        public WorkpieceCenterCalibrationWindow()
        {
            InitializeComponent();
            DataContext = App.ServiceProvider.GetRequiredService<WorkpieceCenterCalibrationViewModel>();
            CalibrationCameraViewer.WorkpieceHoleCenterDetected += CalibrationCameraViewer_WorkpieceHoleCenterDetected;
            CalibrationCameraViewer.WorkpieceEdgePointDetected += CalibrationCameraViewer_WorkpieceEdgePointDetected;
        }

        private void CalibrationCameraViewer_WorkpieceHoleCenterDetected(object? sender, WorkpieceHoleCenterDetectedEventArgs e)
        {
            if (DataContext is WorkpieceCenterCalibrationViewModel viewModel)
            {
                viewModel.ApplyDetectedHoleCenter(e.PixelX, e.PixelY, e.RadiusPixels, e.SourceWidth, e.SourceHeight);
            }
        }

        private void CalibrationCameraViewer_WorkpieceEdgePointDetected(object? sender, WorkpieceEdgePointDetectedEventArgs e)
        {
            if (DataContext is WorkpieceCenterCalibrationViewModel viewModel)
            {
                viewModel.AddDetectedEdgePoint(e.PixelX, e.PixelY, e.SourceWidth, e.SourceHeight);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            CalibrationCameraViewer.WorkpieceHoleCenterDetected -= CalibrationCameraViewer_WorkpieceHoleCenterDetected;
            CalibrationCameraViewer.WorkpieceEdgePointDetected -= CalibrationCameraViewer_WorkpieceEdgePointDetected;
            base.OnClosed(e);
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
