using BLL;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.Services;
using HAL;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Fredy.Drilling.Holes.UserControls
{
    public sealed partial class MotionDebugViewModel : ObservableObject, IDisposable
    {
        private const string MotionSimulatorType = nameof(MotionSimulator);
        private const string MotionAdt8940Type = nameof(MotionAdt8940);
        private static readonly int[] DefaultAxisNumbers = [1, 2, 3];

        private IMoton? _motion;
        private bool _disposed;
        private long _lastNativeCallSequence;
        private bool _refreshFailureReported;
        private CancellationTokenSource? _refreshCancellationTokenSource;
        private Task? _refreshTask;
        private readonly ConfigService? _configService;
        private readonly IHardwareStateService? _hardwareStateService;
        private readonly IIOCard? _ioCard;
        private readonly ILogger _logger;
        private readonly Dictionary<int, AxisParam> _axisParameters = new();
        private static readonly TimeSpan PositionRefreshInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan AxisMoveMinTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan AxisMoveBufferTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan AxisMoveMaxTimeout = TimeSpan.FromSeconds(120);
        private const double PositionComparisonTolerance = 0.001d;

        public MotionDebugViewModel(ILogger logger)
        {
            _logger = logger.ForContext<MotionDebugViewModel>();
            _configService = App.ServiceProvider?.GetService<ConfigService>();
            _hardwareStateService = App.ServiceProvider?.GetService<IHardwareStateService>();
            _ioCard = App.ServiceProvider?.GetService<IIOCard>();
            MotionTypes = [MotionSimulatorType, MotionAdt8940Type];

            PrimeAxisParameterCache(_configService?.CurrentConfig);
            InitializeAxisItems();
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

        public ObservableCollection<MotionDebugAxisItem> AxisItems { get; } = new();

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
        private bool _isInitialized;
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

        public bool IsInitialized
        {
            get => _isInitialized;
            set => SetProperty(ref _isInitialized, value);
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
                PrimeAxisParameterCache(_configService?.CurrentConfig);
                _lastNativeCallSequence = 0;
                _motion = CreateMotion();
                IsInitialized = true;
                AppendNativeCallLogs();
                SetStatus($"{SelectedMotionType} 初始化成功。");
            }
            catch (Exception ex)
            {
                AppendNativeCallLogs();
                DisposeMotion();
                IsInitialized = false;
                SetError("初始化", ex);
            }

            await Task.CompletedTask;
        }

        [RelayCommand]
        private void AddAxis()
        {
            var existingAxisNumbers = new HashSet<int>(AxisItems.Select(item => item.AxisNo));
            var nextAxisNo = 1;
            while (existingAxisNumbers.Contains(nextAxisNo))
            {
                nextAxisNo++;
            }

            var axisItem = CreateAxisItem(nextAxisNo);
            AxisItems.Add(axisItem);
            ConfigureAxisSpeedCore(axisItem, applyToMotion: IsInitialized && _motion is not null);
            if (IsInitialized && _motion is not null)
            {
                _ = RefreshAxisPositionAsync(axisItem, CancellationToken.None);
            }

            SetStatus($"已添加调试轴 {nextAxisNo}。");
        }

        [RelayCommand]
        private void DeleteAxis(MotionDebugAxisItem? axisItem)
        {
            if (axisItem is null)
            {
                return;
            }

            if (!AxisItems.Remove(axisItem))
            {
                return;
            }

            _axisParameters.Remove(axisItem.AxisNo);
            SetStatus($"已移除调试轴 {axisItem.AxisNo}。");
        }

        [RelayCommand]
        private void ResetController()
        {
            AppendNativeCallLogs();
            DisposeMotion();
            _lastNativeCallSequence = 0;
            IsInitialized = false;
            SetStatus("控制器已释放。");
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task MoveAbsoluteAxisAsync(MotionDebugAxisItem? axisItem)
        {
            return ExecuteAxisMoveAsync(
                axisItem,
                (item, _) => item.AbsolutePosition,
                (item, targetPosition) => $"轴 {item.AxisNo} 绝对移动到 {targetPosition:F3} mm。",
                item => $"轴 {item.AxisNo} 绝对移动");
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task MoveRelativeAxisAsync(MotionDebugAxisItem? axisItem)
        {
            return ExecuteAxisMoveAsync(
                axisItem,
                (item, currentPosition) => currentPosition + item.RelativeDistance,
                (item, targetPosition) => $"轴 {item.AxisNo} 相对移动 {item.RelativeDistance:F3} mm，目标 {targetPosition:F3} mm。",
                item => $"轴 {item.AxisNo} 相对移动");
        }

        [RelayCommand]
        private void SetAxisSpeed(MotionDebugAxisItem? axisItem)
        {
            if (axisItem is null)
            {
                return;
            }

            try
            {
                ConfigureAxisSpeedCore(axisItem, applyToMotion: IsInitialized && _motion is not null);
                AppendNativeCallLogs();
                SetStatus(IsInitialized && _motion is not null
                    ? $"轴 {axisItem.AxisNo} 速度已设置为 {axisItem.Speed:F3} mm/s。"
                    : $"轴 {axisItem.AxisNo} 速度已保存，初始化后生效。");
            }
            catch (Exception ex)
            {
                AppendNativeCallLogs();
                SetError($"轴 {axisItem.AxisNo} 设置速度", ex);
            }
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task EnableAxisAsync(MotionDebugAxisItem? axisItem)
        {
            return ExecuteAxisMotionAsync(
                axisItem,
                (motion, item) =>
                {
                    ConfigureAxisSpeedCore(item, applyToMotion: true);
                    return motion.EnableAsync(item.AxisNo);
                },
                item => $"轴 {item.AxisNo} 已使能。",
                item => $"使能轴 {item.AxisNo}");
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task DisableAxisAsync(MotionDebugAxisItem? axisItem)
        {
            return ExecuteAxisMotionAsync(
                axisItem,
                (motion, item) => motion.DisableAsync(item.AxisNo),
                item => $"轴 {item.AxisNo} 已关闭使能。",
                item => $"关闭使能轴 {item.AxisNo}");
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task ResetAxisAsync(MotionDebugAxisItem? axisItem)
        {
            return ExecuteAxisMotionAsync(
                axisItem,
                (motion, item) => motion.HomeAsync(item.AxisNo, wait: true),
                item => $"轴 {item.AxisNo} 复位完成。",
                item => $"轴 {item.AxisNo} 复位",
                refreshPositionAfterExecute: true);
        }

        [RelayCommand(AllowConcurrentExecutions = false)]
        private Task EmergencyStopAxisAsync(MotionDebugAxisItem? axisItem)
        {
            return ExecuteAxisMotionAsync(
                axisItem,
                (motion, item) => motion.EmergencyStopAsync(item.AxisNo),
                item => $"轴 {item.AxisNo} 已急停。",
                item => $"急停轴 {item.AxisNo}");
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
            PrimeAxisParameterCache(_configService?.CurrentConfig);

            if (!IsMotionAdt8940Selected)
            {
                var simulator = new MotionSimulator(_logger);
                ApplyAxisItemsToMotion(simulator);
                return simulator;
            }

            _logger.Information("Initializing MotionAdt8940 with CardNo={CardNo}, StartSpeed={StartSpeed}, DriveSpeed={DriveSpeed}, Acceleration={Acceleration}, HomeSearchSpeed={HomeSearchSpeed}, HomeApproachSpeed={HomeApproachSpeed}", CardNo, StartSpeed, DriveSpeed, Acceleration, HomeSearchSpeed, HomeApproachSpeed);
            var motion = new MotionAdt8940(_logger, CardNo, StartSpeed, DriveSpeed, Acceleration, HomeSearchSpeed, HomeApproachSpeed);
            if (_axisParameters.Count > 0)
            {
                motion.ConfigureAxes(_axisParameters.Values.ToArray());
            }

            var config = _configService?.CurrentConfig;
            if (config is not null)
            {
                motion.ConfigureHoming(BuildAdtHomingOptions(config));
            }

            ApplyAxisItemsToMotion(motion);
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
                axisConfig.InPositionTolerance,
                axisConfig.FastHomeSearchSpeed,
                axisConfig.SlowHomeSearchSpeed,
                axisConfig.HomeTimeoutMs,
                axisConfig.HomeMaxRetryCount);
        }

        private static MotionAdt8940.HomingOptions BuildAdtHomingOptions(AppConfig config)
        {
            var homing = config.AdtHoming ?? new AdtHomingConfig();
            return new MotionAdt8940.HomingOptions(
                AxisHomingDefaults.ResolveSharedFastHomeSearchSpeed(config),
                config.IsIoHome,
                config.IsLatch,
                config.IsGratingHome,
                BuildHomingPort(config.XLimitPort),
                BuildHomingPort(config.YLimitPort),
                BuildHomingPort(homing.ZLimitPort),
                BuildHomingPort(homing.XGratingPort),
                BuildHomingPort(homing.YGratingPort),
                AxisHomingDefaults.ResolveSharedHomeTimeoutMs(config),
                homing.HomeBackoffMm,
                homing.ZHomeLiftMm,
                homing.ZHomeTowardPositiveDirection,
                homing.SlowHomeStartSpeed,
                AxisHomingDefaults.ResolveSharedSlowHomeSearchSpeed(config),
                homing.SlowHomeAcceleration,
                homing.GratingHomeStartSpeed,
                homing.GratingHomeSpeed,
                homing.GratingHomeAcceleration);
        }

        private static MotionAdt8940.HomingPort BuildHomingPort(PortItem port)
        {
            return new MotionAdt8940.HomingPort(port.PortIndex, port.IsNegative ?? port.IsLowLevelActive, port.IsLowLevelActive);
        }

        private async Task ExecuteAxisMotionAsync(
            MotionDebugAxisItem? axisItem,
            Func<IMoton, MotionDebugAxisItem, Task> action,
            Func<MotionDebugAxisItem, string> successMessageFactory,
            Func<MotionDebugAxisItem, string> actionNameFactory,
            bool refreshPositionAfterExecute = false)
        {
            if (axisItem is null)
            {
                return;
            }

            if (_motion is null || !IsInitialized)
            {
                SetStatus("请先初始化控制器。");
                return;
            }

            try
            {
                await action(_motion, axisItem).ConfigureAwait(true);
                if (refreshPositionAfterExecute)
                {
                    await RefreshAxisPositionAsync(axisItem, CancellationToken.None).ConfigureAwait(true);
                }

                AppendNativeCallLogs();
                SetStatus(successMessageFactory(axisItem));
            }
            catch (Exception ex)
            {
                AppendNativeCallLogs();
                SetError(actionNameFactory(axisItem), ex);
            }
        }

        private async Task ExecuteAxisMoveAsync(
            MotionDebugAxisItem? axisItem,
            Func<MotionDebugAxisItem, double, double> targetPositionFactory,
            Func<MotionDebugAxisItem, double, string> successMessageFactory,
            Func<MotionDebugAxisItem, string> actionNameFactory)
        {
            if (axisItem is null)
            {
                return;
            }

            if (_motion is null || !IsInitialized)
            {
                SetStatus("请先初始化控制器。");
                return;
            }

            var actionName = actionNameFactory(axisItem);
            double currentPosition = axisItem.CurrentPosition;
            double targetPosition = axisItem.CurrentPosition;
            var timeout = AxisMoveMinTimeout;

            try
            {
                ConfigureAxisSpeedCore(axisItem, applyToMotion: true);

                currentPosition = await _motion.GetPositionAsync(axisItem.AxisNo).ConfigureAwait(true);
                axisItem.CurrentPosition = currentPosition;

                targetPosition = targetPositionFactory(axisItem, currentPosition);
                var moveDistance = Math.Abs(targetPosition - currentPosition);
                if (moveDistance <= PositionComparisonTolerance)
                {
                    SetStatus($"轴 {axisItem.AxisNo} {actionName}跳过，当前位置 {currentPosition:F3} mm 已接近目标 {targetPosition:F3} mm。");
                    return;
                }

                timeout = CalculateAxisMoveTimeout(moveDistance, axisItem.Speed);
                using var timeoutCancellationTokenSource = new CancellationTokenSource(timeout);
                await _motion.MoveAbsoluteAsync(axisItem.AxisNo, targetPosition, wait: true, axisItem.Speed, timeoutCancellationTokenSource.Token).ConfigureAwait(true);
                await RefreshAxisPositionAsync(axisItem, CancellationToken.None).ConfigureAwait(true);

                AppendNativeCallLogs();
                SetStatus(successMessageFactory(axisItem, targetPosition));
            }
            catch (OperationCanceledException)
            {
                AppendNativeCallLogs();
                var refreshedPosition = await TryRefreshAxisPositionAsync(axisItem).ConfigureAwait(true);
                var finalPosition = refreshedPosition ?? axisItem.CurrentPosition;
                StatusMessage = $"{actionName}超时，轴已停止。目标 {targetPosition:F3} mm，当前位置 {finalPosition:F3} mm，超时 {timeout.TotalSeconds:F1} 秒。";
                _logger.Warning("{ActionName}超时: Axis={AxisNo}, Target={TargetPosition}, Current={CurrentPosition}, TimeoutSeconds={TimeoutSeconds}", actionName, axisItem.AxisNo, targetPosition, finalPosition, timeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                AppendNativeCallLogs();
                var refreshedPosition = await TryRefreshAxisPositionAsync(axisItem).ConfigureAwait(true);
                StatusMessage = refreshedPosition.HasValue
                    ? $"{actionName}失败: {ex.Message}；当前位置 {refreshedPosition.Value:F3} mm。"
                    : $"{actionName}失败: {ex.Message}";
                _logger.Error(ex, "{ActionName}失败", actionName);
            }
        }

        private void PrimeAxisParameterCache(AppConfig? config)
        {
            _axisParameters.Clear();

            if (config is null)
            {
                return;
            }

            RegisterAxisParameter(BuildAxisParam(config.XAxis));
            RegisterAxisParameter(BuildAxisParam(config.YAxis));
            RegisterAxisParameter(BuildAxisParam(config.ZAxis));
        }

        private void DisposeMotion()
        {
            if (_motion is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _motion = null;
        }

        private void InitializeAxisItems()
        {
            AxisItems.Clear();
            foreach (var axisNo in DefaultAxisNumbers)
            {
                AxisItems.Add(CreateAxisItem(axisNo));
            }
        }

        private MotionDebugAxisItem CreateAxisItem(int axisNo)
        {
            return new MotionDebugAxisItem
            {
                AxisNo = axisNo,
                AbsolutePosition = 0d,
                RelativeDistance = 1d,
                CurrentPosition = 0d,
                Speed = GetAxisParameter(axisNo).Velocity
            };
        }

        private static TimeSpan CalculateAxisMoveTimeout(double moveDistance, double speed)
        {
            var normalizedSpeed = NormalizePositive(speed, 1d);
            var expectedSeconds = moveDistance / normalizedSpeed;
            var timeout = TimeSpan.FromSeconds(expectedSeconds) + AxisMoveBufferTimeout;
            if (timeout < AxisMoveMinTimeout)
            {
                return AxisMoveMinTimeout;
            }

            if (timeout > AxisMoveMaxTimeout)
            {
                return AxisMoveMaxTimeout;
            }

            return timeout;
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
                    if (_motion is null || !IsInitialized || AxisItems.Count == 0)
                    {
                        continue;
                    }

                    try
                    {
                        var axisItems = AxisItems.ToArray();
                        for (var i = 0; i < axisItems.Length; i++)
                        {
                            await RefreshAxisPositionAsync(axisItems[i], cancellationToken).ConfigureAwait(false);
                        }

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

        private async Task RefreshAxisPositionAsync(MotionDebugAxisItem axisItem, CancellationToken cancellationToken)
        {
            if (_motion is null || !IsInitialized)
            {
                return;
            }

            var currentPosition = await _motion.GetPositionAsync(axisItem.AxisNo, cancellationToken).ConfigureAwait(false);
            await UpdateOnUiThreadAsync(() => axisItem.CurrentPosition = currentPosition).ConfigureAwait(false);
        }

        private async Task<double?> TryRefreshAxisPositionAsync(MotionDebugAxisItem axisItem)
        {
            try
            {
                await RefreshAxisPositionAsync(axisItem, CancellationToken.None).ConfigureAwait(true);
                return axisItem.CurrentPosition;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "刷新轴 {AxisNo} 当前位置失败", axisItem.AxisNo);
                return null;
            }
        }

        private void ApplyAxisItemsToMotion(IMoton motion)
        {
            foreach (var axisItem in AxisItems)
            {
                ConfigureAxisSpeedCore(axisItem, applyToMotion: false);
                motion.ConfigureAxis(_axisParameters[axisItem.AxisNo]);
            }
        }

        private void ConfigureAxisSpeedCore(MotionDebugAxisItem axisItem, bool applyToMotion)
        {
            var fallbackVelocity = NormalizePositive(DriveSpeed, 1d);
            var fallbackAcceleration = NormalizePositive(Acceleration, 1d);
            var currentAxisParameter = GetAxisParameter(axisItem.AxisNo);
            var normalizedVelocity = NormalizePositive(axisItem.Speed, fallbackVelocity);

            if (!axisItem.Speed.Equals(normalizedVelocity))
            {
                axisItem.Speed = normalizedVelocity;
            }

            var axisParameter = currentAxisParameter with
            {
                Velocity = normalizedVelocity,
                Acceleration = NormalizePositive(currentAxisParameter.Acceleration, fallbackAcceleration),
                Deceleration = NormalizePositive(currentAxisParameter.Deceleration, fallbackAcceleration)
            };

            RegisterAxisParameter(axisParameter);

            if (applyToMotion && _motion is not null)
            {
                _motion.ConfigureAxis(axisParameter);
            }
        }

        private void RegisterAxisParameter(AxisParam axisParameter)
        {
            _axisParameters[axisParameter.AxisNo] = axisParameter;
        }

        private AxisParam GetAxisParameter(int axisNo)
        {
            if (_axisParameters.TryGetValue(axisNo, out var axisParameter))
            {
                return axisParameter;
            }

            var fallbackVelocity = NormalizePositive(DriveSpeed, 1d);
            var fallbackAcceleration = NormalizePositive(Acceleration, 1d);
            return new AxisParam(axisNo, fallbackVelocity, fallbackAcceleration, fallbackAcceleration);
        }

        private static double NormalizePositive(double value, double fallback)
        {
            return value > 0d ? value : fallback;
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
