using BLL;
using Common.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Services;
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

        /// <summary>相机对准工件圆心时的机械坐标（拟合原始值）。</summary>
        [ObservableProperty] private double _cameraAtWorkpieceCenterX;
        [ObservableProperty] private double _cameraAtWorkpieceCenterY;

        public ObservableCollection<System.Windows.Point> EdgePoints { get; } = new();

        public WorkpieceCenterCalibrationViewModel()
        {
        }

        public WorkpieceCenterCalibrationViewModel(ILogger<WorkpieceCenterCalibrationViewModel> logger, IMotionService motionService, IHardwareStateService hardwareStateService, ICamera camera, CoordinateService coordinateService)
        {
            _logger = logger;
            _motionService = motionService;
            _hardwareStateService = hardwareStateService;
            _camera = camera;
            _coordinateService = coordinateService;

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
            _logger?.LogInformation("记录边缘点: X={X}, Y={Y}", p.X, p.Y);
        }

        [RelayCommand]
        private void ClearPoints()
        {
            EdgePoints.Clear();
            CalculatedCenterX = 0;
            CalculatedCenterY = 0;
            CalculatedRadius = 0;
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

        private static double GetVelocity(AxisParam axis) => axis.Velocity > 0 ? axis.Velocity : 1d;

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