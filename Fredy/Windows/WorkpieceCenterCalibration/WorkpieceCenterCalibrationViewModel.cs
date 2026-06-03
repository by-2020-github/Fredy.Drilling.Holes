using BLL;
using Common.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Services;
using Fredy.Drilling.Holes.Models;
using HAL;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class WorkpieceCenterCalibrationViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger<WorkpieceCenterCalibrationViewModel>? _logger;
        private readonly IMotionService? _motionService;
        private readonly IHardwareStateService? _hardwareStateService;
        private readonly ICamera? _camera;
        private readonly CoordinateService? _coordinateService;
        private readonly ConfigService? _configService;

        private CancellationTokenSource? _cameraPreviewCancellationTokenSource;
        private Task? _cameraPreviewTask;
        private bool _disposed;

        // 基础参数
        [ObservableProperty] private double _jogStep = 10;
        public ObservableCollection<double> JogStepOptions { get; } = new() { 0.01, 0.1, 1, 5, 10, 50 };

        [ObservableProperty] private OpenCvSharp.Mat? _cameraPreviewMat;
        [ObservableProperty] private double _currentX;
        [ObservableProperty] private double _currentY;

        [ObservableProperty] private double _calculatedCenterX;
        [ObservableProperty] private double _calculatedCenterY;
        [ObservableProperty] private double _calculatedRadius;
        [ObservableProperty] private string _interactionStatus = "图像区右键可使用圆孔ROI识别或边缘点自动拾取。";

        /// <summary>相机对准工件圆心时的机械坐标（拟合原始值）。</summary>
        [ObservableProperty] private double _cameraAtWorkpieceCenterX;
        [ObservableProperty] private double _cameraAtWorkpieceCenterY;

        public ObservableCollection<System.Windows.Point> EdgePoints { get; } = new();

        public WorkpieceCenterCalibrationViewModel()
        {
        }

        public WorkpieceCenterCalibrationViewModel(ILogger<WorkpieceCenterCalibrationViewModel> logger, IMotionService motionService, IHardwareStateService hardwareStateService, ICamera camera, CoordinateService coordinateService, ConfigService configService)
        {
            _logger = logger;
            _motionService = motionService;
            _hardwareStateService = hardwareStateService;
            _camera = camera;
            _coordinateService = coordinateService;
            _configService = configService;

            // 从已持久化的校准数据初始化 UI 显示值
            CalculatedCenterX = coordinateService.Calibration.WorkpieceCenterX;
            CalculatedCenterY = coordinateService.Calibration.WorkpieceCenterY;
            CameraAtWorkpieceCenterX = coordinateService.Calibration.CameraAtWorkpieceCenterX;
            CameraAtWorkpieceCenterY = coordinateService.Calibration.CameraAtWorkpieceCenterY;

            _hardwareStateService.StateChanged += HardwareStateService_StateChanged;
            _ = _hardwareStateService.RefreshAsync();
            StartCameraPreview();
            _logger.LogInformation("工件圆心校准视图已初始化，当前圆心 CX={CX:F3}, CY={CY:F3}", CalculatedCenterX, CalculatedCenterY);
        }

        private void HardwareStateService_StateChanged(object? sender, HardwareStateChangedEventArgs e)
        {
            if (_disposed) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                CurrentX = e.State.X;
                CurrentY = e.State.Y;
                return;
            }

            _ = dispatcher.InvokeAsync(() =>
            {
                CurrentX = e.State.X;
                CurrentY = e.State.Y;
            });
        }

        [RelayCommand]
        private async Task MoveAxis(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction) || _motionService is null || _hardwareStateService is null) return;

            try
            {
                var currentState = _hardwareStateService.CurrentState;
                var step = direction.EndsWith("-", StringComparison.Ordinal) ? -Math.Abs(JogStep) : Math.Abs(JogStep);

                switch (direction[..1].ToUpperInvariant())
                {
                    case "X":
                        await _motionService.MoveXAsync(currentState.X + step, GetVelocity(_motionService.XAxis)).ConfigureAwait(true);
                        break;
                    case "Y":
                        await _motionService.MoveYAsync(currentState.Y + step, GetVelocity(_motionService.YAxis)).ConfigureAwait(true);
                        break;
                }

                await _hardwareStateService.RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "执行轴向移动命令失败: {Direction}", direction);
            }
        }

        [RelayCommand]
        private void RecordEdgePoint()
        {
            if (_hardwareStateService is null) return;
            var p = new System.Windows.Point(_hardwareStateService.CurrentState.X, _hardwareStateService.CurrentState.Y);
            EdgePoints.Add(p);
            InteractionStatus = $"已记录手动边缘点：X={p.X:F3} mm, Y={p.Y:F3} mm";
            _logger?.LogInformation("记录边缘点: X={X}, Y={Y}", p.X, p.Y);
        }

        [RelayCommand]
        private void ClearPoints()
        {
            EdgePoints.Clear();
            CalculatedCenterX = 0;
            CalculatedCenterY = 0;
            CalculatedRadius = 0;
            InteractionStatus = "已清空边缘点记录。";
        }

        [RelayCommand]
        private void FitCircle()
        {
            if (EdgePoints.Count < 3)
            {
                MessageBox.Show("特征点不足 3 个，无法拟合圆心", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_coordinateService is null)
            {
                _logger?.LogWarning("CoordinateService 未初始化，无法保存工件圆心");
                return;
            }

            try
            {
                var edgePoints = EdgePoints.Select(p => new Point2D(p.X, p.Y));
                var center = _coordinateService.CalibrateWorkpieceByEdgePoints(edgePoints, out double r);

                CalculatedCenterX = center.X;
                CalculatedCenterY = center.Y;
                CalculatedRadius = r;
                CameraAtWorkpieceCenterX = _coordinateService.Calibration.CameraAtWorkpieceCenterX;
                CameraAtWorkpieceCenterY = _coordinateService.Calibration.CameraAtWorkpieceCenterY;
                InteractionStatus = $"拟合完成：冲针圆心 X={center.X:F3} mm, Y={center.Y:F3} mm, 半径={r:F3} mm";

                MessageBox.Show(
                    $"校准完成并已保存：\n圆心 X: {center.X:F3} mm\n圆心 Y: {center.Y:F3} mm\n半径 R: {r:F3} mm",
                    "工件圆心校准", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "工件圆心拟合失败");
                MessageBox.Show($"拟合失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ApplyDetectedHoleCenter(double pixelX, double pixelY, double pixelRadius, int sourceWidth, int sourceHeight)
        {
            if (_coordinateService is null)
            {
                _logger?.LogWarning("CoordinateService 未初始化，无法应用圆孔圆心识别结果");
                return;
            }

            try
            {
                var cameraCenter = ConvertImagePixelToMachinePoint(pixelX, pixelY, sourceWidth, sourceHeight);
                var workpieceCenter = _coordinateService.CalibrateWorkpieceByCameraCenter(cameraCenter);
                double detectedRadiusMm = ConvertPixelRadiusToMillimeters(pixelRadius);

                CameraAtWorkpieceCenterX = _coordinateService.Calibration.CameraAtWorkpieceCenterX;
                CameraAtWorkpieceCenterY = _coordinateService.Calibration.CameraAtWorkpieceCenterY;
                CalculatedCenterX = workpieceCenter.X;
                CalculatedCenterY = workpieceCenter.Y;
                CalculatedRadius = detectedRadiusMm;
                InteractionStatus = $"圆孔ROI识别完成：相机圆心 X={cameraCenter.X:F3} mm, Y={cameraCenter.Y:F3} mm";

                MessageBox.Show(
                    $"圆孔圆心识别完成并已保存：\n相机圆心 X: {cameraCenter.X:F3} mm\n相机圆心 Y: {cameraCenter.Y:F3} mm\n冲针圆心 X: {workpieceCenter.X:F3} mm\n冲针圆心 Y: {workpieceCenter.Y:F3} mm\n识别圆半径: {detectedRadiusMm:F3} mm",
                    "工件圆心校准",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "应用圆孔圆心识别结果失败");
                MessageBox.Show($"圆孔识别结果应用失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void AddDetectedEdgePoint(double pixelX, double pixelY, int sourceWidth, int sourceHeight)
        {
            try
            {
                var edgePoint = ConvertImagePixelToMachinePoint(pixelX, pixelY, sourceWidth, sourceHeight);
                var displayPoint = new System.Windows.Point(edgePoint.X, edgePoint.Y);
                EdgePoints.Add(displayPoint);
                InteractionStatus = $"已自动寻边并记录：X={displayPoint.X:F3} mm, Y={displayPoint.Y:F3} mm";
                _logger?.LogInformation("自动记录边缘点: X={X:F3}, Y={Y:F3}", displayPoint.X, displayPoint.Y);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "添加自动寻边边缘点失败");
                MessageBox.Show($"自动寻边失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static double GetVelocity(AxisParam axis) => axis.Velocity > 0 ? axis.Velocity : 1d;

        private Point2D ConvertImagePixelToMachinePoint(double pixelX, double pixelY, int sourceWidth, int sourceHeight)
        {
            var cameraConfig = EnsureCameraConfig();
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                throw new InvalidOperationException("当前图像尺寸无效，无法进行像素到机械坐标换算。");
            }

            double pixelSizeXmm = Math.Max(0.0001, cameraConfig.PixelSizeX / 1000.0);
            double pixelSizeYmm = Math.Max(0.0001, cameraConfig.PixelSizeY / 1000.0);
            double centerX = sourceWidth / 2.0;
            double centerY = sourceHeight / 2.0;
            double dxPixel = pixelX - centerX;
            double dyPixel = pixelY - centerY;

            return new Point2D(
                CurrentX + (dxPixel * pixelSizeXmm),
                CurrentY - (dyPixel * pixelSizeYmm));
        }

        private double ConvertPixelRadiusToMillimeters(double pixelRadius)
        {
            var cameraConfig = EnsureCameraConfig();
            double pixelSizeXmm = Math.Max(0.0001, cameraConfig.PixelSizeX / 1000.0);
            double pixelSizeYmm = Math.Max(0.0001, cameraConfig.PixelSizeY / 1000.0);
            return pixelRadius * ((pixelSizeXmm + pixelSizeYmm) / 2.0);
        }

        private CameraConfig EnsureCameraConfig()
        {
            var cameraConfig = _configService?.CurrentConfig.Camera;
            if (cameraConfig is null)
            {
                throw new InvalidOperationException("相机配置未初始化，无法执行视觉坐标换算。");
            }

            if (cameraConfig.PixelSizeX <= 0 || cameraConfig.PixelSizeY <= 0)
            {
                throw new InvalidOperationException("像素尺寸未配置，请先在参数配置中完成 PixelSizeX / PixelSizeY 设置。");
            }

            return cameraConfig;
        }

        private void StartCameraPreview()
        {
            if (_camera is null) return;

            _cameraPreviewCancellationTokenSource?.Cancel();
            _cameraPreviewCancellationTokenSource = new CancellationTokenSource();
        }

   
        public void Dispose()
        {
            if (_disposed) return;
            if (_hardwareStateService is not null) _hardwareStateService.StateChanged -= HardwareStateService_StateChanged;

            _cameraPreviewCancellationTokenSource?.Cancel();
            _cameraPreviewCancellationTokenSource = null;
            _cameraPreviewTask = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}