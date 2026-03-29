using BLL;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using HAL;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class ManualControlViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger<ManualControlViewModel>? _logger;
        private readonly IMotionService? _motionService;
        private readonly IIOCard? _ioCard;
        private readonly IHardwareStateService? _hardwareStateService;
        private readonly ICamera? _camera;
        private CancellationTokenSource? _cameraPreviewCancellationTokenSource;
        private Task? _cameraPreviewTask;
        private bool _disposed;

        [ObservableProperty] private MachineStatus _machineStatus = new();
        [ObservableProperty] private double _jogStep = 10;
        [ObservableProperty] private int _canvasSize = 200;
        [ObservableProperty] private int _ringSize = 50;
        [ObservableProperty] private ImageSource? _cameraPreviewImage;

        public ObservableCollection<double> JogStepOptions { get; } = new() { 0.01, 0.1, 1, 5, 10, 50 };
        public ObservableCollection<GpioItem> GpioIn { get; } = new();
        public ObservableCollection<GpioItem> GpioOut { get; } = new();

        public ManualControlViewModel()
        {
            InitializeCollections();
        }

        public ManualControlViewModel(ILogger<ManualControlViewModel> logger, IMotionService motionService, IIOCard ioCard, IHardwareStateService hardwareStateService, ICamera camera)
            : this()
        {
            _logger = logger;
            _motionService = motionService;
            _ioCard = ioCard;
            _hardwareStateService = hardwareStateService;
            _camera = camera;

            InitializeCollections(hardwareStateService.InputCount, hardwareStateService.OutputCount);
            ApplySnapshot(hardwareStateService.CurrentState);
            _hardwareStateService.StateChanged += HardwareStateService_StateChanged;
            _ = _hardwareStateService.RefreshAsync();
            StartCameraPreview();
            _logger.LogInformation("手动控制视图模型已初始化");
        }

        [RelayCommand]
        private async Task MoveAxis(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction) || _motionService is null || _hardwareStateService is null)
            {
                _logger?.LogWarning("轴向移动命令无效或运动服务未初始化: {Direction}", direction);
                return;
            }

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
                    case "Z":
                        await _motionService.MoveZAsync(currentState.Z + step, GetVelocity(_motionService.ZAxis)).ConfigureAwait(true);
                        break;
                    default:
                        _logger?.LogWarning("未识别的轴向移动命令: {Direction}", direction);
                        return;
                }

                await _hardwareStateService.RefreshAsync().ConfigureAwait(true);
                _logger?.LogInformation("执行轴向移动命令: {Direction}, 步距: {JogStep}", direction, JogStep);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "执行轴向移动命令失败: {Direction}", direction);
            }
        }

        [RelayCommand]
        private async Task HomeAxis(string axis)
        {
            if (string.IsNullOrWhiteSpace(axis) || _motionService is null || _hardwareStateService is null)
            {
                _logger?.LogWarning("轴回零命令无效或运动服务未初始化: {Axis}", axis);
                return;
            }

            try
            {
                switch (axis.ToUpperInvariant())
                {
                    case "X":
                        await _motionService.HomeXAsync().ConfigureAwait(true);
                        break;
                    case "Y":
                        await _motionService.HomeYAsync().ConfigureAwait(true);
                        break;
                    case "Z":
                        await _motionService.HomeZAsync().ConfigureAwait(true);
                        break;
                    default:
                        _logger?.LogWarning("未识别的轴回零命令: {Axis}", axis);
                        return;
                }

                MachineStatus.IsHomed = true;
                await _hardwareStateService.RefreshAsync().ConfigureAwait(true);
                _logger?.LogInformation("执行轴回零命令: {Axis}", axis);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "执行轴回零命令失败: {Axis}", axis);
            }
        }

        [RelayCommand]
        private async Task ToggleOutput(int portNo)
        {
            if (_ioCard is null || _hardwareStateService is null)
            {
                _logger?.LogWarning("GPIO 输出切换失败，IO 服务未初始化: {PortNo}", portNo);
                return;
            }

            try
            {
                var currentValue = _hardwareStateService.CurrentState.Outputs.TryGetValue(portNo, out var value) && value;
                await _ioCard.WriteOutputAsync(portNo, !currentValue).ConfigureAwait(true);
                await _hardwareStateService.RefreshAsync().ConfigureAwait(true);
                _logger?.LogInformation("GPIO 输出切换: Port={PortNo}, Value={Value}", portNo, !currentValue);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GPIO 输出切换失败: {PortNo}", portNo);
            }
        }

        [RelayCommand]
        private void TestPunch()
        {
            _logger?.LogInformation("执行测试冲孔命令");
        }

        [RelayCommand]
        private void ChangeSize(string typeAndDelta)
        {
            _logger?.LogInformation("调整画面参数: {TypeAndDelta}", typeAndDelta);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_hardwareStateService is not null)
            {
                _hardwareStateService.StateChanged -= HardwareStateService_StateChanged;
            }

            _cameraPreviewCancellationTokenSource?.Cancel();
            _cameraPreviewCancellationTokenSource = null;
            _cameraPreviewTask = null;

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private static double GetVelocity(AxisParam axis)
        {
            return axis.Velocity > 0 ? axis.Velocity : 1d;
        }

        private void InitializeCollections(int inputCount = 24, int outputCount = 9)
        {
            GpioIn.Clear();
            GpioOut.Clear();

            for (int i = 0; i < inputCount; i++)
            {
                GpioIn.Add(new GpioItem { Id = i });
            }

            for (int i = 0; i < outputCount; i++)
            {
                GpioOut.Add(new GpioItem { Id = i });
            }
        }

        private void ApplySnapshot(HardwareStateSnapshot state)
        {
            MachineStatus.IsMotionCardReady = state.IsMotionCardReady;
            MachineStatus.IsCameraConnected = state.IsCameraConnected;
            MachineStatus.PosX = state.X;
            MachineStatus.PosY = state.Y;
            MachineStatus.PosZ = state.Z;

            foreach (var item in GpioIn)
            {
                item.IsActive = state.Inputs.TryGetValue(item.Id, out var value) && value;
            }

            foreach (var item in GpioOut)
            {
                item.IsActive = state.Outputs.TryGetValue(item.Id, out var value) && value;
            }
        }

        private void HardwareStateService_StateChanged(object? sender, HardwareStateChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                ApplySnapshot(e.State);
                return;
            }

            _ = dispatcher.InvokeAsync(() => ApplySnapshot(e.State));
        }

        private void StartCameraPreview()
        {
            if (_camera is null)
            {
                return;
            }

            _cameraPreviewCancellationTokenSource?.Cancel();
            _cameraPreviewCancellationTokenSource = new CancellationTokenSource();
            _cameraPreviewTask = Task.Run(() => CameraPreviewLoopAsync(_cameraPreviewCancellationTokenSource.Token));
        }

        private async Task CameraPreviewLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_camera!.IsConnected)
                {
                    _camera.Open();
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    var frame = await _camera.GrabAsync().ConfigureAwait(false);
                    var bitmap = CreateBitmapSource(frame);

                    if (bitmap is not null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => CameraPreviewImage = bitmap);
                    }

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "手动界面相机预览异常");
            }
        }

        private static BitmapSource? CreateBitmapSource(CameraArgs? frame)
        {
            if (frame?.Data is null || frame.Width <= 0 || frame.Height <= 0)
            {
                return null;
            }

            var pixelFormat = frame.Format switch
            {
                HAL.PixelFormat.Mono8 => PixelFormats.Gray8,
                HAL.PixelFormat.RGB8 => PixelFormats.Rgb24,
                HAL.PixelFormat.BGR8 => PixelFormats.Bgr24,
                HAL.PixelFormat.RGBA8 => PixelFormats.Bgra32,
                HAL.PixelFormat.BGRA8 => PixelFormats.Bgra32,
                _ => PixelFormats.Bgr24
            };

            var stride = frame.Stride > 0 ? frame.Stride : ((frame.Width * pixelFormat.BitsPerPixel) + 7) / 8;
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, pixelFormat, null, frame.Data, stride);
            bitmap.Freeze();
            return bitmap;
        }
    }
}