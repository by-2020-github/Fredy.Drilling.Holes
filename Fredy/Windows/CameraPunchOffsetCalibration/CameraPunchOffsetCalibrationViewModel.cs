using BLL;
using Common.Tools;
using Common.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.Services;
using HAL;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class CameraPunchOffsetCalibrationViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger<CameraPunchOffsetCalibrationViewModel>? _logger;
        private readonly IMotionService? _motionService;
        private readonly IIOCard? _ioCard;
        private readonly IHardwareStateService? _hardwareStateService;
        private readonly ICamera? _camera;
        private readonly CoordinateService? _coordinateService;
        private readonly ConfigService? _configService;
        private CancellationTokenSource? _cameraPreviewCancellationTokenSource;
        private Task? _cameraPreviewTask;
        private CancellationTokenSource? _surfaceSignalRefreshCancellationTokenSource;
        private Task? _surfaceSignalRefreshTask;
        private bool _surfaceSignalStopIssued;
        private double _lastObservedSurfaceMonitorZ;
        private bool _hasLastObservedSurfaceMonitorZ;
        private bool _disposed;
        private const double DefaultSlowSearchStep = 0.02d;
        private const double MaxSlowSearchStep = 19.999d;
        private const double SurfaceMonitorDirectionTolerance = 0.001d;
        private static readonly TimeSpan SurfaceSignalRefreshInterval = TimeSpan.FromMilliseconds(50);

        [ObservableProperty] private int _canvasSize = 200;
        [ObservableProperty] private int _ringSize = 50;
        [ObservableProperty] private OpenCvSharp.Mat? _cameraPreviewMat;

        [ObservableProperty] private double _cameraX;
        [ObservableProperty] private double _cameraY;
        [ObservableProperty] private double _offsetX;
        [ObservableProperty] private double _offsetY;
        [ObservableProperty] private bool _enableVisionAssist;
        [ObservableProperty] private double _testPunchTargetX;
        [ObservableProperty] private double _testPunchTargetY;
        [ObservableProperty] private double _testPunchReferenceZ;
        [ObservableProperty] private string _testPunchReferenceZText = "参考 Z: 未设置";
        [ObservableProperty] private double _testPunchSafeZ = 8500d;
        [ObservableProperty] private bool _testPunchSurfaceInputLowActive = true;
        [ObservableProperty] private double _testPunchPreparationZ = -12d;
        [ObservableProperty] private double _testPunchSurfaceSearchDistance = -12d;
        [ObservableProperty] private double _testPunchFastApproachSpeed = 9d;
        [ObservableProperty] private double _testPunchSlowSearchSpeed = 0.7d;
        [ObservableProperty] private double _testPunchSlowSearchStep = DefaultSlowSearchStep;
        [ObservableProperty] private int _testPunchSurfaceInputPort;
        [ObservableProperty] private bool _testPunchSurfaceInputRawValue;
        [ObservableProperty] private bool _testPunchSurfaceSignalActive;
        [ObservableProperty] private string _testPunchSurfaceInputRawText = "低电平";
        [ObservableProperty] private string _testPunchSurfaceSignalStateText = "未触发";
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(TestPunchCommand))]
        private bool _isTestPunchInProgress;

        public ObservableCollection<double> JogStepOptions { get; } = new() { 0.01, 0.1, 1, 5, 10, 50, 100, 1000 };
        public ObservableCollection<CalibrationJogAxisItem> AxisItems { get; } = new();
        public ObservableCollection<GpioItem> GpioIn { get; } = new();
        public ObservableCollection<GpioItem> GpioOut { get; } = new();

        public CameraPunchOffsetCalibrationViewModel()
        {
            InitializeAxisItems();
            InitializeCollections();
        }

        public CameraPunchOffsetCalibrationViewModel(ILogger<CameraPunchOffsetCalibrationViewModel> logger, IMotionService motionService, IIOCard ioCard, IHardwareStateService hardwareStateService, ICamera camera, CoordinateService coordinateService, ConfigService configService)
            : this()
        {
            _logger = logger;
            _motionService = motionService;
            _ioCard = ioCard;
            _hardwareStateService = hardwareStateService;
            _camera = camera;
            _coordinateService = coordinateService;
            _configService = configService;

            // 从已持久化的校准数据初始化 UI 显示值
            var config = configService.CurrentConfig;
            OffsetX = coordinateService.Calibration.CameraToPunchOffsetX;
            OffsetY = coordinateService.Calibration.CameraToPunchOffsetY;
            LoadTestPunchSettings(config.CameraPunchOffsetCalibrationTestPunch);
            SetTestPunchReference(config.WorkpieceReferenceZ, config.HasWorkpieceReferenceZ);

            InitializeCollections(hardwareStateService.InputCount, hardwareStateService.OutputCount);
            ApplySnapshot(hardwareStateService.CurrentState);
            _hardwareStateService.StateChanged += HardwareStateService_StateChanged;
            _ = _hardwareStateService.RefreshAsync();
            StartSurfaceSignalRefresh();
            StartCameraPreview();
            _logger.LogInformation("手动控制视图模型已初始化，当前相机偏移 OffsetX={OffsetX:F3}, OffsetY={OffsetY:F3}", OffsetX, OffsetY);
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
                var axisItem = GetAxisItem(direction);
                if (axisItem is null)
                {
                    _logger?.LogWarning("未找到轴向移动对应的轴项: {Direction}", direction);
                    return;
                }

                var stepMagnitude = Math.Abs(axisItem.SelectedStep);
                if (stepMagnitude <= 0d)
                {
                    stepMagnitude = 1d;
                }

                var step = direction.EndsWith("-", StringComparison.Ordinal) ? -stepMagnitude : stepMagnitude;

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
                _logger?.LogInformation("执行轴向移动命令: {Direction}, 步距: {Step}", direction, stepMagnitude);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "执行轴向移动命令失败: {Direction}", direction);
            }
        }

        [RelayCommand]
        private async Task MoveAbsoluteAxis(CalibrationJogAxisItem? axisItem)
        {
            if (axisItem is null || _motionService is null || _hardwareStateService is null)
            {
                _logger?.LogWarning("绝对移动命令无效或运动服务未初始化");
                return;
            }

            if (double.IsNaN(axisItem.AbsolutePosition) || double.IsInfinity(axisItem.AbsolutePosition))
            {
                _logger?.LogWarning("绝对移动目标无效: Axis={AxisName}, Target={Target}", axisItem.AxisName, axisItem.AbsolutePosition);
                return;
            }

            try
            {
                switch (axisItem.AxisName)
                {
                    case "X":
                        await _motionService.MoveXAsync(axisItem.AbsolutePosition, GetVelocity(_motionService.XAxis)).ConfigureAwait(true);
                        break;
                    case "Y":
                        await _motionService.MoveYAsync(axisItem.AbsolutePosition, GetVelocity(_motionService.YAxis)).ConfigureAwait(true);
                        break;
                    case "Z":
                        await _motionService.MoveZAsync(axisItem.AbsolutePosition, GetVelocity(_motionService.ZAxis)).ConfigureAwait(true);
                        break;
                    default:
                        _logger?.LogWarning("未识别的绝对移动轴: {AxisName}", axisItem.AxisName);
                        return;
                }

                await _hardwareStateService.RefreshAsync().ConfigureAwait(true);
                _logger?.LogInformation("执行绝对移动命令: Axis={AxisName}, Target={Target}", axisItem.AxisName, axisItem.AbsolutePosition);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "执行绝对移动命令失败: Axis={AxisName}, Target={Target}", axisItem.AxisName, axisItem.AbsolutePosition);
            }
        }

        [RelayCommand]
        private async Task EmergencyStopAll()
        {
            if (_motionService is null)
            {
                _logger?.LogWarning("急停失败，运动服务未初始化");
                return;
            }

            try
            {
                await _motionService.EmergencyStopAllAsync().ConfigureAwait(true);
                _surfaceSignalStopIssued = false;
                await SafeRefreshHardwareStateAsync().ConfigureAwait(true);
                _logger?.LogWarning("已执行总急停命令");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "执行总急停命令失败");
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

        [RelayCommand(CanExecute = nameof(CanTestPunch))]
        private async Task TestPunch()
        {
            if (_motionService is null || _ioCard is null || _hardwareStateService is null)
            {
                _logger?.LogWarning("测试冲孔失败，运动或 IO 服务未初始化");
                return;
            }

            var validationError = ValidateTestPunchParameters();
            if (validationError is not null)
            {
                MessageBox.Show(validationError, "测试冲孔", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsTestPunchInProgress = true;

            try
            {
                _logger?.LogInformation(
                    "开始执行测试冲孔: X={TargetX:F3}, Y={TargetY:F3}, SafeZ={SafeZ:F3}, Z1={PreparationZ:F3}, SearchDistance={SearchDistance:F3}, Step={Step:F3}, InputPort={InputPort}",
                    TestPunchTargetX,
                    TestPunchTargetY,
                    TestPunchSafeZ,
                    TestPunchPreparationZ,
                    TestPunchSurfaceSearchDistance,
                    NormalizeSlowSearchStep(TestPunchSlowSearchStep),
                    TestPunchSurfaceInputPort);

                await MoveToPunchTargetAsync().ConfigureAwait(true);
                await MoveToPreparationZAsync().ConfigureAwait(true);

                if (await ReadSurfaceInputAsync().ConfigureAwait(true))
                {
                    throw new InvalidOperationException($"检测 IO[{TestPunchSurfaceInputPort}] 在慢探前已经触发，请检查针尖高度或工件状态。");
                }

                var surfaceDetected = await SearchSurfaceAsync().ConfigureAwait(true);
                if (!surfaceDetected)
                {
                    var currentZ = await _motionService.GetZPositionAsync().ConfigureAwait(true);
                    var message = $"在设定搜索距离内未检测到 IO[{TestPunchSurfaceInputPort}] 触发，当前 Z={currentZ:F3} mm。";
                    _logger?.LogWarning(message);
                    MessageBox.Show(message, "测试冲孔", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var detectedZ = await _motionService.GetZPositionAsync().ConfigureAwait(true);
                SetTestPunchReference(detectedZ, hasReference: true);
                PersistTestPunchReferenceZ(detectedZ);
                await MoveToSafeZAsync().ConfigureAwait(true);

                _logger?.LogInformation("测试冲孔完成，已检测到表面，触发 Z={DetectedZ:F3}，已保存为全局参考 Z，已抬回安全 Z={SafeZ:F3}", detectedZ, TestPunchSafeZ);
                MessageBox.Show($"测试冲孔完成，已检测到表面。\n触发 Z: {detectedZ:F3} mm\n已保存参考 Z: {TestPunchReferenceZ:F3} mm\n安全 Z: {TestPunchSafeZ:F3} mm", "测试冲孔", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await SafeStopMotionAsync().ConfigureAwait(true);
                _logger?.LogError(ex, "执行测试冲孔失败");
                MessageBox.Show(ex.Message, "测试冲孔", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                await SafeRefreshHardwareStateAsync().ConfigureAwait(true);
                IsTestPunchInProgress = false;
            }
        }

        [RelayCommand]
        private void SaveTestPunchSettings()
        {
            if (_configService is null)
            {
                _logger?.LogWarning("ConfigService 未初始化，无法保存测试冲孔参数");
                return;
            }

            var validationError = ValidateTestPunchParameters();
            if (validationError is not null)
            {
                MessageBox.Show(validationError, "保存测试冲孔参数", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var config = _configService.CurrentConfig;
                config.CameraPunchOffsetCalibrationTestPunch = BuildTestPunchConfig();
                _configService.SaveWithArchive(config);
                _logger?.LogInformation("测试冲孔参数已保存");
                MessageBox.Show("测试冲孔参数已保存。", "参数保存", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "保存测试冲孔参数失败");
                MessageBox.Show(ex.Message, "参数保存", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void MarkCameraPosition()
        {
            if (_hardwareStateService is null) return;
            CameraX = _hardwareStateService.CurrentState.X;
            CameraY = _hardwareStateService.CurrentState.Y;
            _logger?.LogInformation("记录相机坐标: X={CameraX}, Y={CameraY}", CameraX, CameraY);
        }

        [RelayCommand]
        private void CalculateAndSaveOffset()
        {
            if (_coordinateService is null)
            {
                _logger?.LogWarning("CoordinateService 未初始化，无法保存偏移校准结果");
                return;
            }

            var validationError = ValidateTestPunchParameters();
            if (validationError is not null)
            {
                MessageBox.Show(validationError, "相机偏移校准", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var offset = _coordinateService.CalibrateCameraToPunchOffset(
                new Point2D(TestPunchTargetX, TestPunchTargetY),
                new Point2D(CameraX, CameraY));

            OffsetX = offset.X;
            OffsetY = offset.Y;

            _logger?.LogInformation(
                "按测试冲孔参数计算并保存 Offset: PunchX={PunchX:F3}, PunchY={PunchY:F3}, CameraX={CameraX:F3}, CameraY={CameraY:F3}, OffsetX={OffsetX:F3}, OffsetY={OffsetY:F3}",
                TestPunchTargetX,
                TestPunchTargetY,
                CameraX,
                CameraY,
                OffsetX,
                OffsetY);

            MessageBox.Show(
                $"校准完成并已保存：\nOffsetX: {OffsetX:F3} mm\nOffsetY: {OffsetY:F3} mm",
                "相机偏移校准", MessageBoxButton.OK, MessageBoxImage.Information);
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

            _surfaceSignalRefreshCancellationTokenSource?.Cancel();
            _surfaceSignalRefreshCancellationTokenSource = null;
            _surfaceSignalRefreshTask = null;

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private static double GetVelocity(AxisParam axis)
        {
            return axis.Velocity > 0 ? axis.Velocity : 1d;
        }

        private bool CanTestPunch()
        {
            return !IsTestPunchInProgress && _motionService is not null && _ioCard is not null && _hardwareStateService is not null;
        }

        private void LoadTestPunchSettings(CameraPunchOffsetCalibrationTestPunchConfig settings)
        {
            TestPunchTargetX = settings.TargetX;
            TestPunchTargetY = settings.TargetY;
            TestPunchSafeZ = settings.SafeZ;
            TestPunchSurfaceInputLowActive = settings.SurfaceDetectInputLowActive;
            TestPunchPreparationZ = settings.PreparationZ;
            TestPunchSurfaceSearchDistance = settings.SurfaceSearchDistance;
            TestPunchFastApproachSpeed = settings.FastApproachSpeed > 0d ? settings.FastApproachSpeed : 9d;
            TestPunchSlowSearchSpeed = settings.SlowSearchSpeed > 0d ? settings.SlowSearchSpeed : 0.7d;
            TestPunchSlowSearchStep = NormalizeSlowSearchStep(settings.SlowSearchStep);
            TestPunchSurfaceInputPort = settings.SurfaceDetectInputPort;
        }

        private CameraPunchOffsetCalibrationTestPunchConfig BuildTestPunchConfig()
        {
            return new CameraPunchOffsetCalibrationTestPunchConfig
            {
                TargetX = TestPunchTargetX,
                TargetY = TestPunchTargetY,
                SafeZ = TestPunchSafeZ,
                SurfaceDetectInputLowActive = TestPunchSurfaceInputLowActive,
                PreparationZ = TestPunchPreparationZ,
                SurfaceSearchDistance = TestPunchSurfaceSearchDistance,
                FastApproachSpeed = TestPunchFastApproachSpeed,
                SlowSearchSpeed = TestPunchSlowSearchSpeed,
                SlowSearchStep = NormalizeSlowSearchStep(TestPunchSlowSearchStep),
                SurfaceDetectInputPort = TestPunchSurfaceInputPort,
            };
        }

        private void SetTestPunchReference(double referenceZ, bool hasReference)
        {
            TestPunchReferenceZ = hasReference ? referenceZ : 0d;
            TestPunchReferenceZText = hasReference
                ? $"参考 Z: {referenceZ:F3}"
                : "参考 Z: 未设置";
        }

        private void PersistTestPunchReferenceZ(double referenceZ)
        {
            if (_configService is null)
            {
                _logger?.LogWarning("ConfigService 未初始化，无法保存工件参考 Z");
                return;
            }

            try
            {
                var config = _configService.CurrentConfig;
                config.WorkpieceReferenceZ = referenceZ;
                config.HasWorkpieceReferenceZ = true;
                config.CameraPunchOffsetCalibrationTestPunch = BuildTestPunchConfig();
                _configService.SaveWithArchive(config);
                _logger?.LogInformation("工件参考 Z 已保存到全局配置: ReferenceZ={ReferenceZ:F3}", referenceZ);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "保存工件参考 Z 失败");
            }
        }

        private string? ValidateTestPunchParameters()
        {
            if (_ioCard is null)
            {
                return "IO 服务未初始化。";
            }

            if (TestPunchFastApproachSpeed <= 0d)
            {
                return "快速移动速度必须大于 0。";
            }

            if (double.IsNaN(TestPunchSafeZ) || double.IsInfinity(TestPunchSafeZ))
            {
                return "安全 Z 必须是有效数值。";
            }

            if (TestPunchSlowSearchSpeed <= 0d)
            {
                return "慢速探测速度必须大于 0。";
            }

            if (Math.Abs(TestPunchSurfaceSearchDistance) <= 0d)
            {
                return "表面搜索距离不能为 0。";
            }

            if (TestPunchSlowSearchStep <= 0d)
            {
                return "慢探步长必须大于 0。";
            }

            if (TestPunchSlowSearchStep >= 20d)
            {
                return "慢探步长必须小于 20。";
            }

            if (TestPunchSurfaceInputPort < 0 || TestPunchSurfaceInputPort >= _ioCard.InputCount)
            {
                return $"检测 IO 端口必须在 0 到 {_ioCard.InputCount - 1} 之间。";
            }

            return null;
        }

        private async Task MoveToPunchTargetAsync()
        {
            if (_motionService is null)
            {
                return;
            }

            await Task.WhenAll(
                _motionService.MoveXAsync(TestPunchTargetX, GetVelocity(_motionService.XAxis)),
                _motionService.MoveYAsync(TestPunchTargetY, GetVelocity(_motionService.YAxis)))
                .ConfigureAwait(true);

            await SafeRefreshHardwareStateAsync().ConfigureAwait(true);
        }

        private async Task MoveToPreparationZAsync()
        {
            if (_motionService is null)
            {
                return;
            }

            await _motionService.MoveZAsync(TestPunchPreparationZ, TestPunchFastApproachSpeed).ConfigureAwait(true);
            await SafeRefreshHardwareStateAsync().ConfigureAwait(true);
        }

        private async Task MoveToSafeZAsync()
        {
            if (_motionService is null)
            {
                return;
            }

            await _motionService.MoveZAsync(TestPunchSafeZ, TestPunchFastApproachSpeed).ConfigureAwait(true);
            await SafeRefreshHardwareStateAsync().ConfigureAwait(true);
        }

        private async Task<bool> SearchSurfaceAsync()
        {
            if (_motionService is null)
            {
                return false;
            }

            var remainingDistance = Math.Abs(TestPunchSurfaceSearchDistance);
            var direction = Math.Sign(TestPunchSurfaceSearchDistance);
            var stepLength = NormalizeSlowSearchStep(TestPunchSlowSearchStep);

            while (remainingDistance > 1e-6)
            {
                var currentZ = await _motionService.GetZPositionAsync().ConfigureAwait(true);
                var actualStep = Math.Min(remainingDistance, stepLength);
                var nextZ = currentZ + direction * actualStep;

                await _motionService.MoveZAsync(nextZ, TestPunchSlowSearchSpeed).ConfigureAwait(true);

                var isTriggered = await ReadSurfaceInputAsync().ConfigureAwait(true);
                await SafeRefreshHardwareStateAsync().ConfigureAwait(true);
                if (isTriggered)
                {
                    _logger?.LogInformation("测试冲孔慢探检测到表面: Port={InputPort}, Z={CurrentZ:F3}", TestPunchSurfaceInputPort, nextZ);
                    return true;
                }

                remainingDistance -= actualStep;
            }

            return false;
        }

        private async Task<bool> ReadSurfaceInputAsync()
        {
            if (_ioCard is null)
            {
                return false;
            }

            var rawValue = await _ioCard.ReadInputAsync(TestPunchSurfaceInputPort).ConfigureAwait(true);
            UpdateSurfaceSignalState(rawValue);
            return TestPunchSurfaceSignalActive;
        }

        partial void OnTestPunchSurfaceInputLowActiveChanged(bool value)
        {
            UpdateSurfaceSignalState(TestPunchSurfaceInputRawValue);
        }

        partial void OnTestPunchSurfaceInputPortChanged(int value)
        {
            _ = RefreshSurfaceSignalAsync();
        }

        private void StartSurfaceSignalRefresh()
        {
            if (_ioCard is null)
            {
                return;
            }

            _surfaceSignalRefreshCancellationTokenSource?.Cancel();
            _surfaceSignalRefreshCancellationTokenSource = new CancellationTokenSource();
            _surfaceSignalRefreshTask = Task.Run(() => SurfaceSignalRefreshLoopAsync(_surfaceSignalRefreshCancellationTokenSource.Token));
        }

        private async Task SurfaceSignalRefreshLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(SurfaceSignalRefreshInterval);

            try
            {
                await RefreshSurfaceSignalAsync(cancellationToken).ConfigureAwait(false);

                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    await RefreshSurfaceSignalAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task RefreshSurfaceSignalAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || _ioCard is null)
            {
                return;
            }

            if (TestPunchSurfaceInputPort < 0 || TestPunchSurfaceInputPort >= _ioCard.InputCount)
            {
                UpdateSurfaceSignalState(false);
                return;
            }

            try
            {
                var rawValue = await _ioCard.ReadInputAsync(TestPunchSurfaceInputPort, cancellationToken).ConfigureAwait(false);
                var isSignalActive = IsSurfaceSignalActive(rawValue);
                var zMotionDirection = await GetCurrentZMotionDirectionAsync(cancellationToken).ConfigureAwait(false);
                await TryStopZAxisOnSurfaceSignalAsync(isSignalActive, zMotionDirection, cancellationToken).ConfigureAwait(false);
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.CheckAccess())
                {
                    UpdateSurfaceSignalState(rawValue);
                    return;
                }

                await dispatcher.InvokeAsync(() => UpdateSurfaceSignalState(rawValue), DispatcherPriority.Background, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "刷新测试冲孔接触信号状态失败");
            }
        }

        private void UpdateSurfaceSignalState(bool rawValue)
        {
            TestPunchSurfaceInputRawValue = rawValue;
            TestPunchSurfaceSignalActive = IsSurfaceSignalActive(rawValue);
            TestPunchSurfaceInputRawText = rawValue ? "高电平" : "低电平";
            TestPunchSurfaceSignalStateText = TestPunchSurfaceSignalActive ? "已触发" : "未触发";
        }

        private bool IsSurfaceSignalActive(bool rawValue)
        {
            return TestPunchSurfaceInputLowActive ? !rawValue : rawValue;
        }

        private async Task<int> GetCurrentZMotionDirectionAsync(CancellationToken cancellationToken)
        {
            if (_motionService is null)
            {
                return 0;
            }

            var currentZ = await _motionService.GetZPositionAsync(cancellationToken).ConfigureAwait(false);
            if (!_hasLastObservedSurfaceMonitorZ)
            {
                _lastObservedSurfaceMonitorZ = currentZ;
                _hasLastObservedSurfaceMonitorZ = true;
                return 0;
            }

            var delta = currentZ - _lastObservedSurfaceMonitorZ;
            _lastObservedSurfaceMonitorZ = currentZ;

            if (delta < -SurfaceMonitorDirectionTolerance)
            {
                return -1;
            }

            if (delta > SurfaceMonitorDirectionTolerance)
            {
                return 1;
            }

            return 0;
        }

        private async Task TryStopZAxisOnSurfaceSignalAsync(bool isSignalActive, int zMotionDirection, CancellationToken cancellationToken)
        {
            if (_motionService is null)
            {
                return;
            }

            if (!isSignalActive)
            {
                _surfaceSignalStopIssued = false;
                return;
            }

            if (zMotionDirection >= 0)
            {
                return;
            }

            var zAxisNo = _motionService.ZAxis.AxisNo;
            var zAxisStatus = await _motionService.Hardware.GetStatusAsync(zAxisNo, cancellationToken).ConfigureAwait(false);
            if (zAxisStatus == 0)
            {
                _surfaceSignalStopIssued = false;
                return;
            }

            if (_surfaceSignalStopIssued)
            {
                return;
            }

            _surfaceSignalStopIssued = true;
            _logger?.LogWarning("接触信号触发且 Z 轴正在负方向运动，立即停止 Z 轴。AxisNo={AxisNo}, Status={Status}, Direction={Direction}", zAxisNo, zAxisStatus, zMotionDirection);
            await _motionService.Hardware.EmergencyStopAsync(zAxisNo).ConfigureAwait(false);
        }

        private async Task SafeRefreshHardwareStateAsync()
        {
            if (_hardwareStateService is null)
            {
                return;
            }

            try
            {
                await _hardwareStateService.RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "刷新硬件状态失败");
            }
        }

        private async Task SafeStopMotionAsync()
        {
            if (_motionService is null)
            {
                return;
            }

            try
            {
                await _motionService.StopAllAsync().ConfigureAwait(true);
            }
            catch (Exception stopEx)
            {
                _logger?.LogWarning(stopEx, "测试冲孔失败后停止运动失败");
            }
        }

        private static double NormalizeSlowSearchStep(double step)
        {
            if (step <= 0d)
            {
                return DefaultSlowSearchStep;
            }

            return Math.Min(step, MaxSlowSearchStep);
        }

        private void InitializeAxisItems()
        {
            AxisItems.Clear();
            AxisItems.Add(new CalibrationJogAxisItem("X", Brushes.Goldenrod));
            AxisItems.Add(new CalibrationJogAxisItem("Y", Brushes.ForestGreen));
            AxisItems.Add(new CalibrationJogAxisItem("Z", Brushes.SteelBlue));
        }

        private CalibrationJogAxisItem? GetAxisItem(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                return null;
            }

            var axisName = direction[..1].ToUpperInvariant();
            foreach (var item in AxisItems)
            {
                if (string.Equals(item.AxisName, axisName, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
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
            foreach (var item in AxisItems)
            {
                var currentPosition = item.AxisName switch
                {
                    "X" => state.X,
                    "Y" => state.Y,
                    "Z" => state.Z,
                    _ => 0d
                };

                item.UpdateCurrentPosition(currentPosition);
            }

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
        }

  
    }

    public partial class CalibrationJogAxisItem : ObservableObject
    {
        [ObservableProperty] private double _currentPosition;
        [ObservableProperty] private double _selectedStep = 1d;
        [ObservableProperty] private double _absolutePosition;

        private bool _absolutePositionInitialized;

        public CalibrationJogAxisItem(string axisName, Brush axisBrush)
        {
            AxisName = axisName;
            AxisBrush = axisBrush;
        }

        public string AxisName { get; }

        public Brush AxisBrush { get; }

        public string NegativeDirection => $"{AxisName}-";

        public string PositiveDirection => $"{AxisName}+";

        public void UpdateCurrentPosition(double position)
        {
            CurrentPosition = position;

            if (_absolutePositionInitialized)
            {
                return;
            }

            AbsolutePosition = position;
            _absolutePositionInitialized = true;
        }
    }
}