using BLL;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.Services;
using HAL;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Fredy.Drilling.Holes.UserControls
{
    public sealed partial class MotionDebugViewModel : ObservableObject, IDisposable
    {
        private const string MotionSimulatorType = nameof(MotionSimulator);
        private const string MotionAdt8940Type = nameof(MotionAdt8940);
        private static readonly TimeSpan PositionRefreshInterval = TimeSpan.FromMilliseconds(250);

        private CancellationTokenSource? _refreshCancellationTokenSource;
        private Task? _refreshTask;
        private IMoton? _motion;
        private bool _disposed;
        private bool _refreshFailureReported;
        private long _lastNativeCallSequence;
        private readonly ConfigService? _configService;
        private readonly IHardwareStateService? _hardwareStateService;
        private readonly IIOCard? _ioCard;
        private readonly ILogger _logger;

        public MotionDebugViewModel(ILogger logger)
        {
            _logger = logger.ForContext<MotionDebugViewModel>();
            _configService = App.ServiceProvider?.GetService<ConfigService>();
            _hardwareStateService = App.ServiceProvider?.GetService<IHardwareStateService>();
            _ioCard = App.ServiceProvider?.GetService<IIOCard>();
            MotionTypes = [MotionSimulatorType, MotionAdt8940Type];
            InitializeGpioCollections(_hardwareStateService?.InputCount ?? 24, _hardwareStateService?.OutputCount ?? 9);
            if (_hardwareStateService is not null)
            {
                ApplySnapshot(_hardwareStateService.CurrentState);
                _hardwareStateService.StateChanged += HardwareStateService_StateChanged;
                _ = _hardwareStateService.RefreshAsync();
            }

            StartRefreshLoop();
            LogInformation("调试面板已就绪，请先初始化控制器。");
        }

        public ObservableCollection<string> MotionTypes { get; }

        public ObservableCollection<GpioItem> GpioIn { get; } = new();

        public ObservableCollection<GpioItem> GpioOut { get; } = new();

        public IMoton? CurrentMotion => _motion;

        public bool IsMotionAdt8940Selected => string.Equals(SelectedMotionType, MotionAdt8940Type, StringComparison.Ordinal);

        private string _selectedMotionType = MotionSimulatorType;
        private int _cardNo;
        private double _startSpeed = 0.1;
        private double _driveSpeed = 9.0;
        private double _acceleration = 1.25;
        private double _homeSearchSpeed = 3.0;
        private double _homeApproachSpeed = 0.7;
        private int _axisNo = 1;
        private double _absolutePosition;
        private double _relativeDistance = 10d;
        private bool _waitForCompletion = true;
        private bool _isInitialized;
        private double _currentPosition;
        private string _statusMessage = "请选择控制器并执行初始化。";

        public string SelectedMotionType
        {
            get => _selectedMotionType;
            set
            {
                if (SetProperty(ref _selectedMotionType, value))
                {
                    OnPropertyChanged(nameof(IsMotionAdt8940Selected));
                }
            }
        }

        public int CardNo
        {
            get => _cardNo;
            set => SetProperty(ref _cardNo, value);
        }

        public double StartSpeed
        {
            get => _startSpeed;
            set => SetProperty(ref _startSpeed, value);
        }

        public double DriveSpeed
        {
            get => _driveSpeed;
            set => SetProperty(ref _driveSpeed, value);
        }

        public double Acceleration
        {
            get => _acceleration;
            set => SetProperty(ref _acceleration, value);
        }

        public double HomeSearchSpeed
        {
            get => _homeSearchSpeed;
            set => SetProperty(ref _homeSearchSpeed, value);
        }

        public double HomeApproachSpeed
        {
            get => _homeApproachSpeed;
            set => SetProperty(ref _homeApproachSpeed, value);
        }

        public int AxisNo
        {
            get => _axisNo;
            set
            {
                if (SetProperty(ref _axisNo, value) && IsInitialized)
                {
                    _ = RefreshPositionCoreAsync(CancellationToken.None);
                }
            }
        }

        public double AbsolutePosition
        {
            get => _absolutePosition;
            set => SetProperty(ref _absolutePosition, value);
        }

        public double RelativeDistance
        {
            get => _relativeDistance;
            set => SetProperty(ref _relativeDistance, value);
        }

        public bool WaitForCompletion
        {
            get => _waitForCompletion;
            set => SetProperty(ref _waitForCompletion, value);
        }

        public bool IsInitialized
        {
            get => _isInitialized;
            set => SetProperty(ref _isInitialized, value);
        }

        public double CurrentPosition
        {
            get => _currentPosition;
            set => SetProperty(ref _currentPosition, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private async Task InitializeAsync()
        {
            try
            {
                DisposeMotion();
                _lastNativeCallSequence = 0;
                _motion = CreateMotion();
                _refreshFailureReported = false;
                IsInitialized = true;
                await RefreshPositionCoreAsync(CancellationToken.None).ConfigureAwait(true);
                AppendNativeCallLogs();
                SetStatus($"{SelectedMotionType} 初始化成功。");
            }
            catch (Exception ex)
            {
                AppendNativeCallLogs();
                DisposeMotion();
                IsInitialized = false;
                CurrentPosition = 0;
                SetError("初始化", ex);
            }
        }

        [RelayCommand]
        private void ResetController()
        {
            AppendNativeCallLogs();
            DisposeMotion();
            _refreshFailureReported = false;
            _lastNativeCallSequence = 0;
            IsInitialized = false;
            CurrentPosition = 0;
            SetStatus("控制器已释放。");
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task RefreshPositionAsync()
        {
            return ExecuteMotionAsync(
                motion => RefreshPositionCoreAsync(CancellationToken.None),
                "读取位置成功。",
                "读取位置",
                refreshPositionAfterExecute: false);
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task EnableAxisAsync()
        {
            return ExecuteMotionAsync(
                motion => motion.EnableAsync(AxisNo),
                $"轴 {AxisNo} 已使能。",
                $"使能轴 {AxisNo}");
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task DisableAxisAsync()
        {
            return ExecuteMotionAsync(
                motion => motion.DisableAsync(AxisNo),
                $"轴 {AxisNo} 已禁用。",
                $"禁用轴 {AxisNo}");
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task HomeAxisAsync()
        {
            return ExecuteMotionAsync(
                motion => motion.HomeAsync(AxisNo, WaitForCompletion),
                $"轴 {AxisNo} 回零命令已发送。",
                $"轴 {AxisNo} 回零",
                refreshPositionAfterExecute: WaitForCompletion);
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task MoveAbsoluteAsync()
        {
            return ExecuteMotionAsync(
                motion => motion.MoveAbsoluteAsync(AxisNo, AbsolutePosition, WaitForCompletion, DriveSpeed),
                $"轴 {AxisNo} 绝对移动到 {AbsolutePosition:F3} mm。",
                $"轴 {AxisNo} 绝对移动",
                refreshPositionAfterExecute: WaitForCompletion);
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task MoveRelativeAsync()
        {
            return ExecuteMotionAsync(
                motion => motion.MoveRelativeAsync(AxisNo, RelativeDistance, WaitForCompletion, DriveSpeed),
                $"轴 {AxisNo} 相对移动 {RelativeDistance:F3} mm。",
                $"轴 {AxisNo} 相对移动",
                refreshPositionAfterExecute: WaitForCompletion);
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task StopAxisAsync()
        {
            return ExecuteMotionAsync(
                motion => motion.StopAsync(AxisNo),
                $"轴 {AxisNo} 已减速停止。",
                $"停止轴 {AxisNo}");
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task EmergencyStopAxisAsync()
        {
            return ExecuteMotionAsync(
                motion => motion.EmergencyStopAsync(AxisNo),
                $"轴 {AxisNo} 已急停。",
                $"急停轴 {AxisNo}");
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private async Task ToggleOutputAsync(int portNo)
        {
            if (_ioCard is null || _hardwareStateService is null)
            {
                SetStatus("IO 服务未初始化，无法切换输出。");
                return;
            }

            try
            {
                var currentValue = _hardwareStateService.CurrentState.Outputs.TryGetValue(portNo, out var value) && value;
                await _ioCard.WriteOutputAsync(portNo, !currentValue).ConfigureAwait(true);
                await _hardwareStateService.RefreshAsync().ConfigureAwait(true);
                _logger.Information("GPIO 输出切换: Port={PortNo}, Value={Value}", portNo, !currentValue);
            }
            catch (Exception ex)
            {
                SetError($"GPIO 输出 {portNo} 切换", ex);
            }
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

            _refreshCancellationTokenSource?.Cancel();
            _refreshCancellationTokenSource?.Dispose();
            _refreshCancellationTokenSource = null;
            _refreshTask = null;
            AppendNativeCallLogs();
            DisposeMotion();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private IMoton CreateMotion()
        {
            if (!IsMotionAdt8940Selected)
            {
                return new MotionSimulator(_logger);
            }
            _logger.Information("Initializing MotionAdt8940 with CardNo={CardNo}, StartSpeed={StartSpeed}, DriveSpeed={DriveSpeed}, Acceleration={Acceleration}, HomeSearchSpeed={HomeSearchSpeed}, HomeApproachSpeed={HomeApproachSpeed}", CardNo, StartSpeed, DriveSpeed, Acceleration, HomeSearchSpeed, HomeApproachSpeed);
            var motion = new MotionAdt8940(_logger, CardNo, StartSpeed, DriveSpeed, Acceleration, HomeSearchSpeed, HomeApproachSpeed);
            var config = _configService?.CurrentConfig;
            if (config is not null)
            {
                motion.ConfigureAxes(
                    BuildAxisParam(config.XAxis),
                    BuildAxisParam(config.YAxis),
                    BuildAxisParam(config.ZAxis));
                motion.ConfigureHoming(BuildAdtHomingOptions(config));
            }

            return motion;
        }

        private static AxisParam BuildAxisParam(AxisParamConfig axisConfig)
        {
            return new AxisParam(
                axisConfig.AxisNo,
                axisConfig.Velocity,
                axisConfig.Acceleration,
                axisConfig.Deceleration,
                axisConfig.LeftLimit,
                axisConfig.RightLimit,
                axisConfig.PulsesPerMillimeter > 0 ? axisConfig.PulsesPerMillimeter : 1d,
                axisConfig.UseActualPositionFeedback,
                axisConfig.InPositionTolerance);
        }

        private static MotionAdt8940.HomingOptions BuildAdtHomingOptions(AppConfig config)
        {
            var homing = config.AdtHoming ?? new AdtHomingConfig();
            return new MotionAdt8940.HomingOptions(
                config.HomeSearchSpeed,
                config.IsIoHome,
                config.IsLatch,
                config.IsGratingHome,
                BuildHomingPort(config.XLimitPort),
                BuildHomingPort(config.YLimitPort),
                BuildHomingPort(homing.ZLimitPort),
                BuildHomingPort(homing.XGratingPort),
                BuildHomingPort(homing.YGratingPort),
                homing.HomeTimeoutMs,
                homing.HomeBackoffMm,
                homing.ZHomeLiftMm,
                homing.ZHomeTowardPositiveDirection,
                homing.SlowHomeStartSpeed,
                homing.SlowHomeSpeed,
                homing.SlowHomeAcceleration,
                homing.GratingHomeStartSpeed,
                homing.GratingHomeSpeed,
                homing.GratingHomeAcceleration);
        }

        private static MotionAdt8940.HomingPort BuildHomingPort(PortItem port)
        {
            return new MotionAdt8940.HomingPort(port.PortIndex, port.IsLowLevelActive);
        }

        private async Task ExecuteMotionAsync(Func<IMoton, Task> action, string successMessage, string actionName, bool refreshPositionAfterExecute = true)
        {
            if (_motion is null || !IsInitialized)
            {
                SetStatus("请先初始化控制器。");
                return;
            }

            try
            {
                await action(_motion).ConfigureAwait(true);
                if (refreshPositionAfterExecute)
                {
                    await RefreshPositionCoreAsync(CancellationToken.None).ConfigureAwait(true);
                }

                AppendNativeCallLogs();
                SetStatus(successMessage);
            }
            catch (Exception ex)
            {
                AppendNativeCallLogs();
                SetError(actionName, ex);
            }
        }

        private async Task RefreshPositionCoreAsync(CancellationToken cancellationToken)
        {
            if (_motion is null || !IsInitialized)
            {
                return;
            }

            var position = await _motion.GetPositionAsync(AxisNo, cancellationToken).ConfigureAwait(false);
            await UpdateOnUiThreadAsync(() =>
            {
                CurrentPosition = position;
                AppendNativeCallLogs();
            }).ConfigureAwait(false);
        }

        private void DisposeMotion()
        {
            if (_motion is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _motion = null;
        }

        private void AppendNativeCallLogs()
        {
            if (_motion is not MotionAdt8940 motionAdt8940)
            {
                return;
            }

            var records = motionAdt8940.NativeCallRecords;
            foreach (var record in records)
            {
                if (record.SequenceId <= _lastNativeCallSequence)
                {
                    continue;
                }

                _lastNativeCallSequence = record.SequenceId;
                if (record.IsSuccess)
                {
                    _logger.Debug(
                        "ADT8940 {Operation} 成功，Code={ResultCode}，Message={Message}",
                        record.Operation,
                        record.ResultCode?.ToString() ?? "N/A",
                        record.Message);
                }
                else
                {
                    _logger.Warning(
                        "ADT8940 {Operation} 失败，Code={ResultCode}，Message={Message}",
                        record.Operation,
                        record.ResultCode?.ToString() ?? "N/A",
                        record.Message);
                }
            }
        }

        private void InitializeGpioCollections(int inputCount, int outputCount)
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

        private void StartRefreshLoop()
        {
            _refreshCancellationTokenSource?.Cancel();
            _refreshCancellationTokenSource?.Dispose();
            _refreshCancellationTokenSource = new CancellationTokenSource();
            _refreshTask = Task.Run(() => RefreshLoopAsync(_refreshCancellationTokenSource.Token));
        }

        private async Task RefreshLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(PositionRefreshInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (_motion is null || !IsInitialized)
                    {
                        continue;
                    }

                    try
                    {
                        await RefreshPositionCoreAsync(cancellationToken).ConfigureAwait(false);
                        _refreshFailureReported = false;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (_refreshFailureReported)
                        {
                            continue;
                        }

                        _refreshFailureReported = true;
                        await UpdateOnUiThreadAsync(() =>
                        {
                            StatusMessage = $"后台位置刷新失败: {ex.Message}";
                        }).ConfigureAwait(false);
                        _logger.Warning(ex, "后台位置刷新失败");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private void SetStatus(string message)
        {
            StatusMessage = message;
            LogInformation(message);
        }

        private void SetError(string actionName, Exception ex)
        {
            StatusMessage = $"{actionName}失败: {ex.Message}";
            _logger.Error(ex, "{ActionName}失败", actionName);
        }

        private void LogInformation(string message)
        {
            _logger.Information("{Message}", message);
        }

        private static Task UpdateOnUiThreadAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(action).Task;
        }
    }
}
