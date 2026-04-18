using BLL;
using Common.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    public partial class CalibrationViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger<CalibrationViewModel>? _logger;
        private readonly IMotionService? _motionService;
        private readonly IHardwareStateService? _hardwareStateService;
        private readonly ICamera? _camera;

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

        public ObservableCollection<System.Windows.Point> EdgePoints { get; } = new();

        public CalibrationViewModel()
        {
        }

        public CalibrationViewModel(ILogger<CalibrationViewModel> logger, IMotionService motionService, IHardwareStateService hardwareStateService, ICamera camera)
        {
            _logger = logger;
            _motionService = motionService;
            _hardwareStateService = hardwareStateService;
            _camera = camera;

            _hardwareStateService.StateChanged += HardwareStateService_StateChanged;
            _ = _hardwareStateService.RefreshAsync();
            StartCameraPreview();
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

            var points = EdgePoints.Select(p => (p.X, p.Y));
            if (CircleFitter.FitCircle(points, out double cx, out double cy, out double r))
            {
                CalculatedCenterX = cx;
                CalculatedCenterY = cy;
                CalculatedRadius = r;
                _logger?.LogInformation("拟合成功: CX={CX}, CY={CY}, R={R}", cx, cy, r);
                MessageBox.Show($"圆心坐标 X: {cx:F3}\n圆心坐标 Y: {cy:F3}\n半径 R: {r:F3}", "拟合结果", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("特征点共线或异常，拟合失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static double GetVelocity(AxisParam axis) => axis.Velocity > 0 ? axis.Velocity : 1d;

        private void StartCameraPreview()
        {
            if (_camera is null) return;

            _cameraPreviewCancellationTokenSource?.Cancel();
            _cameraPreviewCancellationTokenSource = new CancellationTokenSource();
            _cameraPreviewTask = Task.Run(() => CameraPreviewLoopAsync(_cameraPreviewCancellationTokenSource.Token));
        }

        private async Task CameraPreviewLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_camera!.IsConnected) _camera.Open();

                while (!cancellationToken.IsCancellationRequested)
                {
                    var frame = await _camera.GrabAsync().ConfigureAwait(false);
                    var mat = Tools.VisionUIHelper.CameraArgsToMat(frame);

                    if (mat is not null && !mat.Empty())
                    {
                        var oldMat = CameraPreviewMat;
                        await Application.Current.Dispatcher.InvokeAsync(() => CameraPreviewMat = mat);
                        oldMat?.Dispose();
                    }

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "相机预览异常");
            }
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