using Demo;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HAL
{
    public class MotionAdt8940 : IMoton, IIOCard
    {
        private readonly ILogger _logger;

        /// <summary>
        /// IsNegative 指示传感器是否在负方向安装：
        //  true：传感器安装在负方向，传感器激活时当前位于负方向
        //  false：传感器安装在正方向，传感器激活时当前位于正方向
        /// IsLowLevelActive 指示传感器是否低电平有效：
        //  true：0 表示传感器激活，1 表示未激活
        //  false：1 表示传感器激活，0 表示未激活
        /// </summary>
        /// <param name="PortIndex"></param>
        /// <param name="IsNegative"></param>
        /// <param name="IsLowLevelActive"></param>
        public readonly record struct HomingPort(
            int PortIndex,
            bool IsNegative,
            bool IsLowLevelActive = false);

        public readonly record struct HomingOptions(
            double HomeSearchSpeed,
            bool IsIoHome,
            bool IsLatch,
            bool IsGratingHome,
            HomingPort XLimitPort,
            HomingPort YLimitPort,
            HomingPort ZLimitPort,
            HomingPort XGratingPort,
            HomingPort YGratingPort,
            int HomeTimeoutMs = 10000,
            double HomeBackoffMm = 0.2,
            double ZHomeLiftMm = 0.0,
            bool ZHomeTowardPositiveDirection = false,
            double SlowHomeStartSpeed = 0.1,
            double SlowHomeSpeed = 0.5,
            double SlowHomeAcceleration = 1.0,
            double GratingHomeStartSpeed = 0.5,
            double GratingHomeSpeed = 2.0,
            double GratingHomeAcceleration = 2.0);

        public readonly record struct NativeCallRecord(
            long SequenceId,
            DateTime Timestamp,
            string Operation,
            int? ResultCode,
            string Message,
            bool IsSuccess);

        private delegate int NativeCallWithValue(out int value);

        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);
        private static readonly TimeSpan HomeStopReadyPollInterval = TimeSpan.FromMilliseconds(5);
        private const int HomeStopReadyMaxWaitMs = 200;
        private const int MaxNativeCallRecordCount = 500;

        private readonly ConcurrentDictionary<int, bool> _axisEnabled = new();
        private readonly ConcurrentDictionary<int, AxisParam> _axisParams = new();
        private readonly ConcurrentQueue<NativeCallRecord> _nativeCallRecords = new();
        private readonly object _syncRoot = new();
        private readonly int _cardNo;
        private readonly double _startSpeed;
        private readonly double _driveSpeed;
        private readonly double _acceleration;
        private double _homeSearchSpeed;
        private double _homeApproachSpeed;
        private bool _initialized;
        private int _nativeCallRecordCount;
        private long _nativeCallSequence;
        private HomingOptions _homingOptions;

        public NativeCallRecord[] NativeCallRecords => _nativeCallRecords.ToArray();

        public void ConfigureAxis(AxisParam axis)
        {
            ValidateAxisNo(axis.AxisNo);
            _axisParams[axis.AxisNo] = axis with
            {
                PulsesPerMillimeter = NormalizePulseEquivalent(axis.PulsesPerMillimeter),
                FastHomeSearchSpeed = NormalizeOptionalNonNegative(axis.FastHomeSearchSpeed),
                SlowHomeSearchSpeed = NormalizeOptionalNonNegative(axis.SlowHomeSearchSpeed),
                HomeTimeoutMs = NormalizeOptionalNonNegative(axis.HomeTimeoutMs),
                HomeMaxRetryCount = NormalizeHomeMaxRetryCount(axis.HomeMaxRetryCount)
            };
        }

        public void ConfigureAxes(params AxisParam[] axes)
        {
            ArgumentNullException.ThrowIfNull(axes);

            for (var i = 0; i < axes.Length; i++)
            {
                ConfigureAxis(axes[i]);
            }
        }

        public MotionAdt8940(
            ILogger logger,
            int cardNo = 0,
            double startSpeed = 0.1,
            double driveSpeed = 9.0,
            double acceleration = 1.25,
            double homeSearchSpeed = 3.0,
            double homeApproachSpeed = 0.7)
        {
            _logger = (logger ?? Log.Logger).ForContext<MotionAdt8940>();
            ArgumentOutOfRangeException.ThrowIfNegative(cardNo);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(startSpeed, 0d);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(driveSpeed, 0d);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(acceleration, 0d);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(homeSearchSpeed, 0d);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(homeApproachSpeed, 0d);

            _cardNo = cardNo;
            _startSpeed = startSpeed;
            _driveSpeed = driveSpeed;
            _acceleration = acceleration;
            _homeSearchSpeed = homeSearchSpeed;
            _homeApproachSpeed = homeApproachSpeed;
            _homingOptions = new HomingOptions(
                homeSearchSpeed,
                false,
                false,
                false,
                new HomingPort(14, false, false),
                new HomingPort(14, false, false),
                new HomingPort(14, false, false),
                new HomingPort(0, true, true),
                new HomingPort(0, true, true));
        }

        public void ConfigureHoming(HomingOptions options)
        {
            _homingOptions = options with
            {
                HomeSearchSpeed = Math.Max(0.001, options.HomeSearchSpeed),
                HomeTimeoutMs = Math.Max(100, options.HomeTimeoutMs),
                HomeBackoffMm = Math.Max(0d, options.HomeBackoffMm),
                ZHomeLiftMm = Math.Max(0d, options.ZHomeLiftMm),
                SlowHomeStartSpeed = Math.Max(0.001, options.SlowHomeStartSpeed),
                SlowHomeSpeed = Math.Max(0.001, options.SlowHomeSpeed),
                SlowHomeAcceleration = Math.Max(0.001, options.SlowHomeAcceleration),
                GratingHomeStartSpeed = Math.Max(0.001, options.GratingHomeStartSpeed),
                GratingHomeSpeed = Math.Max(0.001, options.GratingHomeSpeed),
                GratingHomeAcceleration = Math.Max(0.001, options.GratingHomeAcceleration)
            };

            _homeSearchSpeed = _homingOptions.HomeSearchSpeed;
        }

        public Task DisableAllAsync(int[] axisNos)
        {
            return ExecuteForAllAsync(axisNos, DisableAsync);
        }

        public Task DisableAsync(int axisNo)
        {
            EnsureAxisReady(axisNo);
            _axisEnabled[axisNo] = false;
            ExecuteNative(() => adt8940a1.adt8940a1_dec_stop(_cardNo, axisNo), $"Disable axis {axisNo}");
            return Task.CompletedTask;
        }

        public Task EmergencyStopAllAsync(int[] axisNos)
        {
            return ExecuteForAllAsync(axisNos, EmergencyStopAsync);
        }

        public Task EmergencyStopAsync(int axisNo)
        {
            EnsureAxisReady(axisNo);
            _axisEnabled[axisNo] = false;
            ExecuteNative(() => adt8940a1.adt8940a1_sudden_stop(_cardNo, axisNo), $"Emergency stop axis {axisNo}");
            return Task.CompletedTask;
        }

        public Task EnableAllAsync(int[] axisNos)
        {
            return ExecuteForAllAsync(axisNos, EnableAsync);
        }

        public Task EnableAsync(int axisNo)
        {
            EnsureAxisReady(axisNo);
            _axisEnabled[axisNo] = true;
            return Task.CompletedTask;
        }

        public Task<double> GetPositionAsync(int axisNo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);
            var axis = GetAxisParam(axisNo);

            ExecuteNative(
                axis.UseActualPositionFeedback
                    ? (out int pos) => adt8940a1.adt8940a1_get_actual_pos(_cardNo, axisNo, out pos)
                    : (out int pos) => adt8940a1.adt8940a1_get_command_pos(_cardNo, axisNo, out pos),
                $"Get position for axis {axisNo}",
                out var position);

            return Task.FromResult(ToMillimeter(position, axis));
        }

        public Task HomeAsync(int axisNo, bool wait, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);
            EnsureAxisEnabled(axisNo);

            var task = HomeCoreAsync(axisNo, cancellationToken);
            if (wait)
            {
                return task;
            }

            _ = task;
            return Task.CompletedTask;
        }

        public Task MoveAbsoluteAsync(int axisNo, double position, bool wait, double? velocity = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);
            EnsureAxisEnabled(axisNo);
            var axis = GetAxisParam(axisNo);

            ConfigureMotion(axisNo, velocity, axis);

            var targetPulse = ToPulse(position, axis);
            var currentPulse = GetCommandPosition(axisNo);
            var distance = checked(targetPulse - currentPulse);
            if (distance == 0)
            {
                return Task.CompletedTask;
            }

            ExecuteNative(() => adt8940a1.adt8940a1_pmove(_cardNo, axisNo, distance), $"Move absolute axis {axisNo}");
            return wait ? WaitForAxisStopAsync(axisNo, cancellationToken) : Task.CompletedTask;
        }

        private async Task HomeCoreAsync(int axisNo, CancellationToken cancellationToken)
        {
            var axis = GetAxisParam(axisNo);
            var maxRetryCount = ResolveHomeMaxRetryCount(axis);
            Exception? lastException = null;
            LogEffectiveHomeParameters(axis);

            LogHomeDebug("HomeCoreAsync started for axis {AxisNo}. IsZAxis={IsZAxis}", axisNo, axisNo == 3);
            RecordNativeCall($"Home axis {axisNo}", null, "开始执行旧 ProcessManager 风格回零流程。", true);

            for (var retry = 0; retry < maxRetryCount; retry++)
            {
                try
                {
                    LogHomeDebug("Axis {AxisNo} home attempt {Attempt}/{MaxRetryCount} started.", axisNo, retry + 1, maxRetryCount);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (axisNo == 3)
                    {
                        LogHomeDebug("Axis {AxisNo} entering Z-axis homing flow.", axisNo);
                        await HomeZAxisAsync(axis, cancellationToken).ConfigureAwait(false);
                        LogHomeDebug("Axis {AxisNo} finished Z-axis homing flow.", axisNo);
                    }
                    else
                    {
                        LogHomeDebug("Axis {AxisNo} entering XY-axis homing flow.", axisNo);
                        await HomeXYAxisAsync(axis, cancellationToken).ConfigureAwait(false);
                        LogHomeDebug("Axis {AxisNo} finished XY-axis homing flow.", axisNo);
                    }

                    RecordNativeCall($"Home axis {axisNo}", null, $"轴 {axisNo} 回零完成。", true);
                    LogHomeDebug("Axis {AxisNo} home attempt {Attempt}/{MaxRetryCount} succeeded.", axisNo, retry + 1, maxRetryCount);
                    return;
                }
                catch (OperationCanceledException)
                {
                    LogHomeDebug("Axis {AxisNo} homing canceled on attempt {Attempt}/{MaxRetryCount}. Triggering emergency stop on all axes.", axisNo, retry + 1, maxRetryCount);
                    // 流程图：紧急停止统一处理 - 立即停止所有轴运动
                    await EmergencyStopAllEnabledAxesAsync().ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    LogHomeDebug(ex, "Axis {AxisNo} home attempt {Attempt}/{MaxRetryCount} failed.", axisNo, retry + 1, maxRetryCount);
                    RecordNativeCall($"Home axis {axisNo}", null, $"轴 {axisNo} 回零失败，准备重试：{ex.Message}", false);
                    await StopAsync(axisNo).ConfigureAwait(false);
                }
            }

            LogHomeDebug(lastException, "Axis {AxisNo} homing failed after maximum retries.", axisNo);
            // 流程图：错误处理 - 达到最大重试次数后安全停止所有轴
            await SafeStopAllAxesAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"Home axis {axisNo} failed after maximum retries.", lastException);
        }

        private async Task HomeXYAxisAsync(AxisParam axis, CancellationToken cancellationToken)
        {
            var axisNo = axis.AxisNo;
            await HomeXYMechanicalAsync(axis, cancellationToken).ConfigureAwait(false);
            LogHomeDebug("Axis {AxisNo} mechanical homing completed.", axisNo);

            if (_homingOptions.IsGratingHome)
            {
                LogHomeDebug("Axis {AxisNo} grating homing enabled. IsLatch={IsLatch}, IsIoHome={IsIoHome}", axisNo, _homingOptions.IsLatch, _homingOptions.IsIoHome);
                if (_homingOptions.IsLatch)
                {
                    LogHomeDebug("Axis {AxisNo} entering latch homing step.", axisNo);
                    await HomeXYByLatchAsync(axis, cancellationToken).ConfigureAwait(false);
                    LogHomeDebug("Axis {AxisNo} latch homing step completed.", axisNo);
                }
                else if (_homingOptions.IsIoHome)
                {
                    LogHomeDebug("Axis {AxisNo} entering grating IO homing step.", axisNo);
                    await HomeXYByGratingIoAsync(axis, cancellationToken).ConfigureAwait(false);
                    LogHomeDebug("Axis {AxisNo} grating IO homing step completed.", axisNo);
                }
            }

            LogHomeDebug("Axis {AxisNo} resetting position after homing.", axisNo);
            ResetPosition(axisNo);
        }

        private async Task HomeZAxisAsync(AxisParam axis, CancellationToken cancellationToken)
        {
            var axisNo = axis.AxisNo;
            var port = GetMechanicalPort(axisNo);
            var towardHomeDirection = GetTowardHomeDirection(port);
            var awayFromHomeDirection = GetOppositeDirection(towardHomeDirection);
            var currentLevel = NormalizeSignalLevel(await ReadInputLevelAsync(port.PortIndex, cancellationToken).ConfigureAwait(false));
            var activeLevel = GetPortActiveSignalLevel(port);
            var inactiveLevel = GetPortInactiveSignalLevel(port);
            var homeTimeoutMs = ResolveHomeTimeoutMs(axis);
            var fastHomeSearchSpeed = ResolveFastHomeSearchSpeed(axis);
            var startSpeedPulse = ToSpeedPulse(ResolveSearchStartSpeed(fastHomeSearchSpeed), axis);
            var searchSpeedPulse = ToSpeedPulse(fastHomeSearchSpeed, axis);
            var accelPulse = ToAccelerationPulse(_acceleration, axis);

            LogHomeDebug("Axis {AxisNo} Z homing start. Port={PortIndex}, CurrentLevel={CurrentLevel}, ActiveLevel={ActiveLevel}, IsNegative={IsNegative}, IsLowLevelActive={IsLowLevelActive}, TowardDirection={TowardDirection}, AwayDirection={AwayDirection}", axisNo, port.PortIndex, currentLevel, activeLevel, port.IsNegative, port.IsLowLevelActive, towardHomeDirection, awayFromHomeDirection);

            if (currentLevel == activeLevel)
            {
                LogHomeDebug("Axis {AxisNo} Z homing detected active level initially. Moving away from sensor first.", axisNo);
                await MoveContinuousUntilInputLevelAsync(
                    axisNo,
                    awayFromHomeDirection,
                    port,
                    inactiveLevel,
                    startSpeedPulse,
                    searchSpeedPulse,
                    accelPulse,
                    homeTimeoutMs,
                    cancellationToken).ConfigureAwait(false);
            }

            LogHomeDebug("Axis {AxisNo} Z homing moving toward home sensor. TowardDirection={TowardDirection}", axisNo, towardHomeDirection);
            await MoveContinuousUntilInputLevelAsync(
                axisNo,
                towardHomeDirection,
                port,
                activeLevel,
                startSpeedPulse,
                searchSpeedPulse,
                accelPulse,
                homeTimeoutMs,
                cancellationToken).ConfigureAwait(false);

            LogHomeDebug("Axis {AxisNo} Z homing reached sensor. Resetting position.", axisNo);
            ResetPosition(axisNo);

            if (_homingOptions.ZHomeLiftMm > 0)
            {
                var liftPulse = ToPulse(_homingOptions.ZHomeLiftMm, axis);
                LogHomeDebug("Axis {AxisNo} Z homing lift enabled. LiftMm={LiftMm}, LiftPulse={LiftPulse}", axisNo, _homingOptions.ZHomeLiftMm, liftPulse);
                await MoveRelativePulseAsync(axisNo, liftPulse, cancellationToken).ConfigureAwait(false);
            }

            RecordNativeCall($"Home axis {axisNo}", null, $"轴 {axisNo} Z 回零流程完成。", true);
            LogHomeDebug("Axis {AxisNo} Z homing completed.", axisNo);
        }

        private async Task HomeXYMechanicalAsync(AxisParam axis, CancellationToken cancellationToken)
        {
            var axisNo = axis.AxisNo;
            var port = GetMechanicalPort(axisNo);
            var currentLevel = NormalizeSignalLevel(await ReadInputLevelAsync(port.PortIndex, cancellationToken).ConfigureAwait(false));
            var homeTimeoutMs = ResolveHomeTimeoutMs(axis);
            var fastHomeSearchSpeed = ResolveFastHomeSearchSpeed(axis);
            var slowHomeSearchSpeed = ResolveSlowHomeSearchSpeed(axis, fastHomeSearchSpeed);
            var startSpeedPulse = ToSpeedPulse(ResolveSearchStartSpeed(fastHomeSearchSpeed), axis);
            var searchSpeedPulse = ToSpeedPulse(fastHomeSearchSpeed, axis);
            var accelPulse = ToAccelerationPulse(_acceleration, axis);
            var currentActiveState = GetPortActiveState(port, currentLevel);
            var firstTargetLevel = currentLevel == 0 ? 1 : 0;
            var firstDirection = GetDirectionToFlipHomeInput(port, currentActiveState);
            var slowDirection = GetOppositeDirection(firstDirection);
            var slowTargetLevel = currentLevel;
            var slowStartPulse = ToSpeedPulse(ResolveSearchStartSpeed(slowHomeSearchSpeed), axis);
            var slowSpeedPulse = ToSpeedPulse(slowHomeSearchSpeed, axis);
            var slowAccelPulse = accelPulse;
            var slowTimeoutMs = ScaleSlowHomeTimeout(homeTimeoutMs, fastHomeSearchSpeed, slowHomeSearchSpeed);

            LogHomeDebug(
                "Axis {AxisNo} mechanical homing start. Port={PortIndex}, IsNegative={IsNegative}, IsLowLevelActive={IsLowLevelActive}, CurrentLevel={CurrentLevel}, CurrentActiveState={CurrentActiveState}, FirstDirection={FirstDirection}, FirstTargetLevel={FirstTargetLevel}, SlowDirection={SlowDirection}, SlowTargetLevel={SlowTargetLevel}",
                axisNo,
                port.PortIndex,
                port.IsNegative,
                port.IsLowLevelActive,
                currentLevel,
                currentActiveState,
                firstDirection,
                firstTargetLevel,
                slowDirection,
                slowTargetLevel);

            // 第一段：根据当前 ON/OFF 状态判断当前位置，并向能够让传感器第一次取反的方向运动。
            await MoveContinuousUntilInputLevelAsync(
                axisNo,
                firstDirection,
                port,
                firstTargetLevel,
                startSpeedPulse,
                searchSpeedPulse,
                accelPulse,
                homeTimeoutMs,
                cancellationToken).ConfigureAwait(false);

            // 第二段：速度降为第一段的slowK分之一，反方向慢速运动，直到传感器再次取反，认为到达零点边沿。
            LogHomeDebug("Axis {AxisNo} mechanical homing slow reverse. SlowDirection={SlowDirection}, SlowTargetLevel={SlowTargetLevel}", axisNo, slowDirection, slowTargetLevel);
            await MoveContinuousUntilInputLevelAsync(
                axisNo,
                slowDirection,
                port,
                slowTargetLevel,
                slowStartPulse,
                slowSpeedPulse,
                slowAccelPulse,
                slowTimeoutMs,
                cancellationToken).ConfigureAwait(false);
            LogHomeDebug("Axis {AxisNo} mechanical homing completed.", axisNo);
        }

        private async Task HomeXYByGratingIoAsync(AxisParam axis, CancellationToken cancellationToken)
        {
            var axisNo = axis.AxisNo;
            var port = GetGratingPort(axisNo);
            ValidatePort(port, $"Axis {axisNo} grating");
            var towardHomeDirection = GetTowardHomeDirection(port);
            var activeLevel = GetPortActiveSignalLevel(port);

            LogHomeDebug("Axis {AxisNo} grating IO homing start. Port={PortIndex}, ActiveLevel={ActiveLevel}, TowardDirection={TowardDirection}", axisNo, port.PortIndex, activeLevel, towardHomeDirection);

            await MoveContinuousUntilInputLevelAsync(
                axisNo,
                towardHomeDirection,
                port,
                activeLevel,
                ToSpeedPulse(_homingOptions.GratingHomeStartSpeed, axis),
                ToSpeedPulse(_homingOptions.GratingHomeSpeed, axis),
                ToAccelerationPulse(_homingOptions.GratingHomeAcceleration, axis),
                ResolveHomeTimeoutMs(axis),
                cancellationToken).ConfigureAwait(false);
            LogHomeDebug("Axis {AxisNo} grating IO homing completed.", axisNo);
        }

        private async Task HomeXYByLatchAsync(AxisParam axis, CancellationToken cancellationToken)
        {
            var axisNo = axis.AxisNo;
            var port = GetGratingPort(axisNo);
            ValidatePort(port, $"Axis {axisNo} grating");
            var towardHomeDirection = GetTowardHomeDirection(port);

            LogHomeDebug("Axis {AxisNo} latch homing start.", axisNo);
            await ClearLockStatusAsync(axisNo, cancellationToken).ConfigureAwait(false);
            await SetLockPositionModeAsync(axisNo, 1, 0, 1, cancellationToken).ConfigureAwait(false);

            try
            {
                LogHomeDebug("Axis {AxisNo} latch mode configured. Starting continuous move.", axisNo);
                ConfigureMotion(axisNo, _homingOptions.GratingHomeStartSpeed, _homingOptions.GratingHomeSpeed, _homingOptions.GratingHomeAcceleration);
                ExecuteNative(() => adt8940a1.adt8940a1_continue_move(_cardNo, axisNo, towardHomeDirection), $"Start latch home move for axis {axisNo}");

                await WaitForLockStatusAsync(axisNo, ResolveHomeTimeoutMs(axis), cancellationToken).ConfigureAwait(false);
                LogHomeDebug("Axis {AxisNo} latch signal detected. Stopping axis.", axisNo);
                await StopAsync(axisNo).ConfigureAwait(false);

                var lockPosition = GetLockPositionPulse(axisNo);
                var currentPosition = GetCommandPosition(axisNo);
                var moveDistance = lockPosition - currentPosition;
                LogHomeDebug("Axis {AxisNo} latch position acquired. LockPosition={LockPosition}, CurrentPosition={CurrentPosition}, MoveDistance={MoveDistance}", axisNo, lockPosition, currentPosition, moveDistance);
                if (moveDistance != 0)
                {
                    await MoveRelativePulseAsync(axisNo, moveDistance, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                LogHomeDebug("Axis {AxisNo} latch homing cleanup start.", axisNo);
                await SetLockPositionModeAsync(axisNo, 0, 0, 0, cancellationToken).ConfigureAwait(false);
                await ClearLockStatusAsync(axisNo, cancellationToken).ConfigureAwait(false);
                LogHomeDebug("Axis {AxisNo} latch homing cleanup completed.", axisNo);
            }
        }

        private async Task MoveContinuousUntilInputLevelAsync(
            int axisNo,
            int dir,
            HomingPort port,
            int targetLevel,
            int startSpeed,
            int driveSpeed,
            int acceleration,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            ValidatePort(port, $"Axis {axisNo} home");
            LogHomeDebug("Axis {AxisNo} continuous move wait start. Dir={Dir}, Port={PortIndex}, TargetLevel={TargetLevel}, StartSpeed={StartSpeed}, DriveSpeed={DriveSpeed}, Acceleration={Acceleration}, TimeoutMs={TimeoutMs}", axisNo, dir, port.PortIndex, targetLevel, startSpeed, driveSpeed, acceleration, timeoutMs);
            
            // 修复：此处的 startSpeed, driveSpeed, acceleration 已经是 Pulse 单位。
            // 直接调用底层，避免原 ConfigureMotion 误将其作为 mm/s 再次乘以脉冲当量导致速度极大而电机堵转
            ExecuteNative(() => adt8940a1.adt8940a1_set_startv(_cardNo, axisNo, Math.Max(100, startSpeed)), $"Set start speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_speed(_cardNo, axisNo, Math.Max(100, driveSpeed)), $"Set drive speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_acc(_cardNo, axisNo, ToCardAcceleration(acceleration)), $"Set acceleration for axis {axisNo}");
            
            ExecuteNative(() => adt8940a1.adt8940a1_continue_move(_cardNo, axisNo, dir), $"Continuous move axis {axisNo} dir {dir}");

            var targetLevelNormalized = NormalizeSignalLevel(targetLevel);
            var stopHandled = false;

            try
            {
                var startedAt = Environment.TickCount64;
                var times = 0;
                while (true)
                {
                    times++;
                    cancellationToken.ThrowIfCancellationRequested();

                    var level = await ReadInputLevelAsync(port.PortIndex, cancellationToken).ConfigureAwait(false);
                    var normalizedLevel = NormalizeSignalLevel(level);
                    if (normalizedLevel == targetLevelNormalized)
                    {
                        LogHomeDebug("Axis {AxisNo} continuous move reached target level. Port={PortIndex}, Level={Level}, NormalizedLevel={NormalizedLevel}, TargetLevel={TargetLevel}", axisNo, port.PortIndex, level, normalizedLevel, targetLevelNormalized);
                        var stopStartedAt = Environment.TickCount64;
                        ExecuteNative(() => adt8940a1.adt8940a1_sudden_stop(_cardNo, axisNo), $"Immediate stop axis {axisNo} on home input match");
                        stopHandled = true;
                        var stopReady = await WaitForAxisMotionIdleAsync(axisNo, Math.Min(timeoutMs, HomeStopReadyMaxWaitMs), cancellationToken).ConfigureAwait(false);
                        LogHomeDebug("Axis {AxisNo} home stop settle completed. Ready={Ready}, ElapsedMs={ElapsedMs}", axisNo, stopReady, Environment.TickCount64 - stopStartedAt);
                        return;
                    }

                    if (Environment.TickCount64 - startedAt >= timeoutMs)
                    {
                        LogHomeDebug("Axis {AxisNo} continuous move wait timed out. Port={PortIndex}, LastLevel={Level}, NormalizedLevel={NormalizedLevel}, TargetLevel={TargetLevel}, TimeoutMs={TimeoutMs}", axisNo, port.PortIndex, level, normalizedLevel, targetLevelNormalized, timeoutMs);
                        throw new TimeoutException($"Axis {axisNo} home input wait timed out on port {port.PortIndex}.");
                    }

                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                    if (times % 10 == 0)
                    {
                        _logger.Debug("Axis {AxisNo} continuous move waiting. Port={PortIndex}, TargetLevel={TargetLevel}", axisNo, port.PortIndex, targetLevelNormalized);
                    }
                }
            }
            finally
            {
                if (!stopHandled)
                {
                    LogHomeDebug("Axis {AxisNo} continuous move wait exit without target match. Issuing stop.", axisNo);
                    await StopAsync(axisNo).ConfigureAwait(false);
                }
            }
        }

        private async Task MoveRelativePulseAsync(int axisNo, int pulseDistance, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pulseDistance == 0)
            {
                return;
            }

            ConfigureMotion(axisNo);
            ExecuteNative(() => adt8940a1.adt8940a1_pmove(_cardNo, axisNo, pulseDistance), $"Move relative pulse axis {axisNo}");
            await WaitForAxisStopAsync(axisNo, cancellationToken).ConfigureAwait(false);
        }

        private async Task<int> ReadInputLevelAsync(int portNo, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();

            var result = ExecuteNativeWithRawResult(
                () => adt8940a1.adt8940a1_read_bit(_cardNo, portNo),
                $"Read input level {portNo}");

            if (result < 0)
            {
                throw new InvalidOperationException($"Read input {portNo} failed with code {result}.");
            }

            return result;
        }

        private async Task<bool> WaitForAxisMotionIdleAsync(int axisNo, int maxWaitMs, CancellationToken cancellationToken)
        {
            var startedAt = Environment.TickCount64;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ExecuteNative(
                    (out int status) => adt8940a1.adt8940a1_get_status(_cardNo, axisNo, out status),
                    $"Get status for axis {axisNo}",
                    out var motionStatus);

                if (motionStatus == 0)
                {
                    return true;
                }

                if (Environment.TickCount64 - startedAt >= maxWaitMs)
                {
                    _logger.Warning("Axis {AxisNo} did not report idle within {MaxWaitMs}ms after sudden stop; continuing homing.", axisNo, maxWaitMs);
                    return false;
                }

                await Task.Delay(HomeStopReadyPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task WaitForLockStatusAsync(int axisNo, int timeoutMs, CancellationToken cancellationToken)
        {
            LogHomeDebug("Axis {AxisNo} waiting for latch signal. TimeoutMs={TimeoutMs}", axisNo, timeoutMs);
            var startedAt = Environment.TickCount64;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await GetLockStatusAsync(axisNo, cancellationToken).ConfigureAwait(false))
                {
                    LogHomeDebug("Axis {AxisNo} latch signal detected.", axisNo);
                    return;
                }

                if (Environment.TickCount64 - startedAt >= timeoutMs)
                {
                    LogHomeDebug("Axis {AxisNo} latch wait timed out.", axisNo);
                    throw new TimeoutException($"Axis {axisNo} latch home timed out.");
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task MoveRelativeAsync(int axisNo, double distance, bool wait, double? velocity = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);
            EnsureAxisEnabled(axisNo);
            var axis = GetAxisParam(axisNo);

            ConfigureMotion(axisNo, velocity, axis);

            var pulseDistance = ToPulse(distance, axis);
            if (pulseDistance == 0)
            {
                return Task.CompletedTask;
            }

            ExecuteNative(() => adt8940a1.adt8940a1_pmove(_cardNo, axisNo, pulseDistance), $"Move relative axis {axisNo}");
            return wait ? WaitForAxisStopAsync(axisNo, cancellationToken) : Task.CompletedTask;
        }

        public Task StopAllAsync(int[] axisNos)
        {
            return ExecuteForAllAsync(axisNos, StopAsync);
        }

        public Task StopAsync(int axisNo)
        {
            EnsureAxisReady(axisNo);
            ExecuteNative(() => adt8940a1.adt8940a1_dec_stop(_cardNo, axisNo), $"Stop axis {axisNo}");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 流程图：紧急停止统一处理 - 立即停止所有已启用轴，记录当前位置日志。
        /// </summary>
        private async Task EmergencyStopAllEnabledAxesAsync()
        {
            var axisNos = new[] { 1, 2, 3 };
            foreach (var axisNo in axisNos)
            {
                try
                {
                    _axisEnabled.TryAdd(axisNo, false);
                    _axisEnabled[axisNo] = false;
                    ExecuteNative(() => adt8940a1.adt8940a1_sudden_stop(_cardNo, axisNo), $"Emergency stop axis {axisNo}");

                    // 流程图：SAVE_POSITION - 记录当前位置
                    ExecuteNative(
                        (out int pos) => adt8940a1.adt8940a1_get_command_pos(_cardNo, axisNo, out pos),
                        $"Save position for axis {axisNo} on emergency stop",
                        out var savedPos);
                    _logger.Warning("紧急停止：轴 {AxisNo} 当前位置 = {Position} 脉冲", axisNo, savedPos);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "紧急停止轴 {AxisNo} 时发生错误，已忽略。", axisNo);
                }
            }
            RecordNativeCall("EmergencyStopAllAxes", null, "紧急停止：所有轴已立即停止并记录位置。", true);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// 流程图：错误处理 - 达到最大重试次数后安全停止所有轴。
        /// </summary>
        private async Task SafeStopAllAxesAsync()
        {
            var axisNos = new[] { 1, 2, 3 };
            foreach (var axisNo in axisNos)
            {
                try
                {
                    ExecuteNative(() => adt8940a1.adt8940a1_dec_stop(_cardNo, axisNo), $"Safe stop axis {axisNo}");
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "安全停止轴 {AxisNo} 时发生错误，已忽略。", axisNo);
                }
            }
            RecordNativeCall("SafeStopAllAxes", null, "回零失败：已安全停止所有轴。", true);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public int InputCount => 40;

        public int OutputCount => 16;

        public Task<bool> ReadInputAsync(int portNo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();

            var result = ExecuteNativeWithRawResult(
                () => adt8940a1.adt8940a1_read_bit(_cardNo, portNo),
                $"Read input {portNo}");

            if (result < 0)
            {
                throw new InvalidOperationException($"Read input {portNo} failed with code {result}.");
            }

            return Task.FromResult(result > 0);
        }

        public async Task<IReadOnlyDictionary<int, bool>> ReadInputsAsync(int[] portNos, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<int, bool>(portNos.Length);
            foreach (var portNo in portNos)
            {
                results[portNo] = await ReadInputAsync(portNo, cancellationToken).ConfigureAwait(false);
            }
            return results;
        }

        public Task<bool> ReadOutputAsync(int portNo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();

            var result = ExecuteNativeWithRawResult(
                () => adt8940a1.adt8940a1_get_out(_cardNo, portNo),
                $"Read output {portNo}");

            if (result < 0)
            {
                throw new InvalidOperationException($"Read output {portNo} failed with code {result}.");
            }

            return Task.FromResult(result > 0);
        }

        public async Task<IReadOnlyDictionary<int, bool>> ReadOutputsAsync(int[] portNos, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<int, bool>(portNos.Length);
            foreach (var portNo in portNos)
            {
                results[portNo] = await ReadOutputAsync(portNo, cancellationToken).ConfigureAwait(false);
            }
            return results;
        }

        public Task WriteOutputAsync(int portNo, bool value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();

            ExecuteNative(
                () => adt8940a1.adt8940a1_write_bit(_cardNo, portNo, value ? 1 : 0),
                $"Write output {portNo} to {value}");

            return Task.CompletedTask;
        }

        public async Task WriteOutputsAsync(IReadOnlyDictionary<int, bool> outputs, CancellationToken cancellationToken = default)
        {
            foreach (var kvp in outputs)
            {
                await WriteOutputAsync(kvp.Key, kvp.Value, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task ClearLockStatusAsync(int axisNo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);

            ExecuteNative(
                () => adt8940a1.adt8940a1_clr_lock_status(_cardNo, axisNo),
                $"Clear lock status for axis {axisNo}");

            return Task.CompletedTask;
        }

        public Task<double> GetLockPositionAsync(int axisNo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);
            var axis = GetAxisParam(axisNo);

            ExecuteNative(
                (out int pos) => adt8940a1.adt8940a1_get_lock_position(_cardNo, axisNo, out pos),
                $"Get lock position for axis {axisNo}",
                out var position);

            return Task.FromResult(ToMillimeter(position, axis));
        }

        public Task<bool> GetLockStatusAsync(int axisNo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);

            ExecuteNative(
                (out int status) => adt8940a1.adt8940a1_get_lock_status(_cardNo, axisNo, out status),
                $"Get lock status for axis {axisNo}",
                out var lockStatus);

            return Task.FromResult(lockStatus != 0);
        }

        public Task SetLockPositionModeAsync(int axisNo, int mode, int regi, int logical, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);

            ExecuteNative(
                () => adt8940a1.adt8940a1_set_lock_position(_cardNo, axisNo, mode, regi, logical),
                $"Set lock position mode for axis {axisNo}");

            return Task.CompletedTask;
        }

        private static async Task ExecuteForAllAsync(int[] axisNos, Func<int, Task> action)
        {
            ArgumentNullException.ThrowIfNull(axisNos);

            var tasks = new Task[axisNos.Length];
            for (var i = 0; i < axisNos.Length; i++)
            {
                tasks[i] = action(axisNos[i]);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static int ToPulse(double value, AxisParam axis)
        {
            return checked((int)Math.Round(value * NormalizePulseEquivalent(axis.PulsesPerMillimeter), MidpointRounding.AwayFromZero));
        }

        private static double ToMillimeter(int pulse, AxisParam axis)
        {
            return pulse / NormalizePulseEquivalent(axis.PulsesPerMillimeter);
        }

        private async Task WaitForAxisStopAsync(int axisNo, CancellationToken cancellationToken)
        {
            var axis = GetAxisParam(axisNo);

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ExecuteNative(
                        (out int status) => adt8940a1.adt8940a1_get_status(_cardNo, axisNo, out status),
                        $"Get status for axis {axisNo}",
                        out var motionStatus);

                    if (motionStatus == 0)
                    {
                        if (axis.UseActualPositionFeedback || !axis.InPositionTolerance.HasValue)
                        {
                            return;
                        }

                        var commandPosition = ToMillimeter(GetCommandPosition(axisNo), axis);
                        var actualPosition = ToMillimeter(GetActualPosition(axisNo), axis);
                        if (Math.Abs(commandPosition - actualPosition) <= NormalizeInPositionTolerance(axis.InPositionTolerance.Value))
                        {
                            return;
                        }
                    }

                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                _ = StopAsync(axisNo);
                throw;
            }
        }

        private async Task WaitForHomeAsync(int axisNo, CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var status = ExecuteNativeWithRawResult(
                        () => adt8940a1.adt8940a1_GetHomeStatus_Ex(_cardNo, axisNo),
                        $"Get home status for axis {axisNo}");

                    if (status == 0)
                    {
                        ResetPosition(axisNo);
                        return;
                    }

                    if (status < 0)
                    {
                        throw new InvalidOperationException($"Home axis {axisNo} failed with status code {status}.");
                    }

                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                _ = StopAsync(axisNo);
                throw;
            }
        }

        private void ConfigureMotion(int axisNo)
        {
            var axis = GetAxisParam(axisNo);
            var startSpeedPulse = ToSpeedPulse(_startSpeed, axis);
            var driveSpeedPulse = ToSpeedPulse(_driveSpeed, axis);
            ExecuteNative(() => adt8940a1.adt8940a1_set_startv(_cardNo, axisNo, startSpeedPulse), $"Set start speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_speed(_cardNo, axisNo, driveSpeedPulse), $"Set drive speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_acc(_cardNo, axisNo, ToCardAcceleration(ToAccelerationPulse(_acceleration, axis))), $"Set acceleration for axis {axisNo}");
        }

        private void ConfigureMotion(int axisNo, double? velocity, AxisParam axis)
        {
            var driveSpeedPulse = ResolveDriveSpeed(axis, velocity);
            var accelerationPulse = ResolveAcceleration(axis);
            var startSpeedPulse = Math.Min(Math.Max(1, ToSpeedPulse(_startSpeed, axis)), driveSpeedPulse);

            ExecuteNative(() => adt8940a1.adt8940a1_set_startv(_cardNo, axisNo, startSpeedPulse), $"Set start speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_speed(_cardNo, axisNo, driveSpeedPulse), $"Set drive speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_acc(_cardNo, axisNo, ToCardAcceleration(accelerationPulse)), $"Set acceleration for axis {axisNo}");
        }

        private void ConfigureMotion(int axisNo, double startSpeedMmPerSec, double driveSpeedMmPerSec, double accelerationMmPerSec2)
        {
            var axis = GetAxisParam(axisNo);
            var startSpeedPulse = Math.Max(1, ToSpeedPulse(startSpeedMmPerSec, axis));
            var driveSpeedPulse = Math.Max(1, ToSpeedPulse(driveSpeedMmPerSec, axis));
            var accelerationPulse = Math.Max(1, ToAccelerationPulse(accelerationMmPerSec2, axis));
            ExecuteNative(() => adt8940a1.adt8940a1_set_startv(_cardNo, axisNo, startSpeedPulse), $"Set start speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_speed(_cardNo, axisNo, driveSpeedPulse), $"Set drive speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_acc(_cardNo, axisNo, ToCardAcceleration(accelerationPulse)), $"Set acceleration for axis {axisNo}");
        }

        private void EnsureAxisEnabled(int axisNo)
        {
            if (!_axisEnabled.TryGetValue(axisNo, out var enabled) || !enabled)
            {
                throw new InvalidOperationException($"Axis {axisNo} is disabled.");
            }
        }

        private void EnsureAxisReady(int axisNo)
        {
            ValidateAxisNo(axisNo);
            EnsureInitialized();
            _axisEnabled.TryAdd(axisNo, true);
            _axisParams.TryAdd(axisNo, CreateDefaultAxisParam(axisNo));
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            lock (_syncRoot)
            {
                if (_initialized)
                {
                    return;
                }

                var result = ExecuteNativeWithRawResult(
                    () => adt8940a1.adt8940a1_initial(),
                    "Initialize ADT8940");

                if (result <= 0)
                {
                    throw new InvalidOperationException($"Initialize ADT8940 failed with code {result}.");
                }

                _initialized = true;
            }
        }

        private int GetCommandPosition(int axisNo)
        {
            ExecuteNative(
                (out int pos) => adt8940a1.adt8940a1_get_command_pos(_cardNo, axisNo, out pos),
                $"Get command position for axis {axisNo}",
                out var position);

            return position;
        }

        private int GetActualPosition(int axisNo)
        {
            ExecuteNative(
                (out int pos) => adt8940a1.adt8940a1_get_actual_pos(_cardNo, axisNo, out pos),
                $"Get actual position for axis {axisNo}",
                out var position);

            return position;
        }

        private int GetLockPositionPulse(int axisNo)
        {
            ExecuteNative(
                (out int pos) => adt8940a1.adt8940a1_get_lock_position(_cardNo, axisNo, out pos),
                $"Get lock position pulse for axis {axisNo}",
                out var position);

            return position;
        }

        private void ResetPosition(int axisNo)
        {
            ExecuteNative(() => adt8940a1.adt8940a1_set_command_pos(_cardNo, axisNo, 0), $"Reset command position for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_actual_pos(_cardNo, axisNo, 0), $"Reset actual position for axis {axisNo}");
        }

        private AxisParam GetAxisParam(int axisNo)
        {
            return _axisParams.TryGetValue(axisNo, out var axis)
                ? axis
                : CreateDefaultAxisParam(axisNo);
        }

        private HomingPort GetMechanicalPort(int axisNo)
        {
            return axisNo switch
            {
                1 => _homingOptions.XLimitPort,
                2 => _homingOptions.YLimitPort,
                3 => _homingOptions.ZLimitPort,
                _ => throw new ArgumentOutOfRangeException(nameof(axisNo), axisNo, "Unsupported homing axis.")
            };
        }

        private HomingPort GetGratingPort(int axisNo)
        {
            return axisNo switch
            {
                1 => _homingOptions.XGratingPort,
                2 => _homingOptions.YGratingPort,
                _ => throw new ArgumentOutOfRangeException(nameof(axisNo), axisNo, "Grating homing is only supported for X/Y axes.")
            };
        }

        /// <summary>
        /// 计算朝向原点传感器的运动方向。
        /// 控制卡方向定义：dir=1 表示正方向，dir=0 表示负方向。
        /// 当 IsNegative=true 时，表示原点传感器安装在负方向，朝向原点应走负方向；
        /// 当 IsNegative=false 时，表示原点传感器安装在正方向，朝向原点应走正方向。
        /// </summary>
        private static int GetTowardHomeDirection(HomingPort port)
        {
            return port.IsNegative ? 0 : 1;
        }

        /// <summary>
        /// 计算指定方向的反方向。
        /// 控制卡方向定义：direction=1（正方向），direction=0（负方向）。
        /// </summary>
        private static int GetOppositeDirection(int direction)
        {
            return direction == 0 ? 1 : 0;
        }

        /// <summary>
        /// 根据当前传感器激活状态计算第一段运动方向，使传感器状态发生第一次取反。
        /// 控制卡方向定义：dir=1 表示正方向，dir=0 表示负方向。
        /// currentActiveState=1 表示当前传感器已激活，currentActiveState=0 表示当前传感器未激活。
        /// </summary>
        private static int GetDirectionToFlipHomeInput(HomingPort port, int currentActiveState)
        {
            var towardHomeDirection = GetTowardHomeDirection(port);
            return currentActiveState == 0
                ? towardHomeDirection
                : GetOppositeDirection(towardHomeDirection);
        }

        private static int NormalizeSignalLevel(int level)
        {
            return level == 0 ? 0 : 1;
        }

        private static int GetPortActiveSignalLevel(HomingPort port)
        {
            return port.IsLowLevelActive ? 0 : 1;
        }

        private static int GetPortInactiveSignalLevel(HomingPort port)
        {
            return port.IsLowLevelActive ? 1 : 0;
        }

        private static int GetPortActiveState(HomingPort port, int signalLevel)
        {
            return NormalizeSignalLevel(signalLevel) == GetPortActiveSignalLevel(port) ? 1 : 0;
        }

        private static void ValidatePort(HomingPort port, string name)
        {
            if (port.PortIndex < 0)
            {
                throw new InvalidOperationException($"{name} port is not configured.");
            }
        }

        private static AxisParam CreateDefaultAxisParam(int axisNo)
        {
            return new AxisParam(axisNo, 0, 0, 0, PulsesPerMillimeter: 1d, UseActualPositionFeedback: false, InPositionTolerance: null);
        }

        private static double NormalizeInPositionTolerance(double tolerance)
        {
            return tolerance >= 0d ? tolerance : 0d;
        }

        private static double NormalizePulseEquivalent(double pulseEquivalent)
        {
            return pulseEquivalent > 0d ? pulseEquivalent : 1d;
        }

        private static int ToCardAcceleration(int acceleration)
        {
            return Math.Clamp((int)Math.Ceiling(acceleration / 125d), 1, 64000);
        }

        // mm/s 转 pulse/s（依据轴的 PulsesPerMillimeter）
        private static int ToSpeedPulse(double speedMmPerSec, AxisParam axis)
        {
            return Math.Max(1, (int)Math.Ceiling(speedMmPerSec * NormalizePulseEquivalent(axis.PulsesPerMillimeter)));
        }

        // mm/s² 转 pulse/s²（依据轴的 PulsesPerMillimeter）
        private static int ToAccelerationPulse(double accelMmPerSec2, AxisParam axis)
        {
            return Math.Max(1, (int)Math.Ceiling(accelMmPerSec2 * NormalizePulseEquivalent(axis.PulsesPerMillimeter)));
        }

        private int ResolveDriveSpeed(AxisParam axis, double? velocity)
        {
            double mmPerSec;
            if (velocity.HasValue && velocity.Value > 0)
            {
                mmPerSec = velocity.Value;
            }
            else if (axis.Velocity > 0)
            {
                mmPerSec = axis.Velocity;
            }
            else
            {
                mmPerSec = _driveSpeed;
            }

            return ToSpeedPulse(mmPerSec, axis);
        }

        private int ResolveAcceleration(AxisParam axis)
        {
            double mmPerSec2 = axis.Acceleration > 0 ? axis.Acceleration : _acceleration;
            return ToAccelerationPulse(mmPerSec2, axis);
        }

        private static double NormalizeOptionalNonNegative(double value)
        {
            return value > 0d ? value : 0d;
        }

        private static int NormalizeOptionalNonNegative(int value)
        {
            return value > 0 ? value : 0;
        }

        private static int NormalizeHomeMaxRetryCount(int value)
        {
            return value > 0 ? value : 3;
        }

        private int ResolveHomeTimeoutMs(AxisParam axis)
        {
            return axis.HomeTimeoutMs > 0 ? axis.HomeTimeoutMs : _homingOptions.HomeTimeoutMs;
        }

        private static int ResolveHomeMaxRetryCount(AxisParam axis)
        {
            return axis.HomeMaxRetryCount > 0 ? axis.HomeMaxRetryCount : 3;
        }

        private void LogEffectiveHomeParameters(AxisParam axis)
        {
            var fastHomeSearchSpeed = ResolveFastHomeSearchSpeed(axis);
            var slowHomeSearchSpeed = ResolveSlowHomeSearchSpeed(axis, fastHomeSearchSpeed);
            var homeTimeoutMs = ResolveHomeTimeoutMs(axis);
            var homeMaxRetryCount = ResolveHomeMaxRetryCount(axis);
            var fastSource = axis.FastHomeSearchSpeed > 0d ? nameof(AxisParam.FastHomeSearchSpeed) : nameof(HomingOptions.HomeSearchSpeed);
            var slowSource = axis.SlowHomeSearchSpeed > 0d ? nameof(AxisParam.SlowHomeSearchSpeed) : nameof(HomingOptions.SlowHomeSpeed);
            var timeoutSource = axis.HomeTimeoutMs > 0 ? nameof(AxisParam.HomeTimeoutMs) : nameof(HomingOptions.HomeTimeoutMs);
            var retrySource = axis.HomeMaxRetryCount > 0 ? nameof(AxisParam.HomeMaxRetryCount) : "Default(3)";

            LogHomeDebug(
                "Axis {AxisNo} effective home parameters. FastHomeSearchSpeed={FastHomeSearchSpeed}, SlowHomeSearchSpeed={SlowHomeSearchSpeed}, HomeTimeoutMs={HomeTimeoutMs}, HomeMaxRetryCount={HomeMaxRetryCount}, FastSource={FastSource}, SlowSource={SlowSource}, TimeoutSource={TimeoutSource}, RetrySource={RetrySource}",
                axis.AxisNo,
                fastHomeSearchSpeed,
                slowHomeSearchSpeed,
                homeTimeoutMs,
                homeMaxRetryCount,
                fastSource,
                slowSource,
                timeoutSource,
                retrySource);

            RecordNativeCall(
                $"Home axis {axis.AxisNo}",
                null,
                $"轴 {axis.AxisNo} 生效回零参数: 快速寻零={fastHomeSearchSpeed:F3}mm/s, 慢速寻零={slowHomeSearchSpeed:F3}mm/s, 超时={homeTimeoutMs}ms, 最大重试={homeMaxRetryCount}。",
                true);
        }

        private double ResolveFastHomeSearchSpeed(AxisParam axis)
        {
            if (axis.FastHomeSearchSpeed > 0d)
            {
                return axis.FastHomeSearchSpeed;
            }

            if (_homingOptions.HomeSearchSpeed > 0d)
            {
                return _homingOptions.HomeSearchSpeed;
            }

            return Math.Max(0.001d, _homeSearchSpeed);
        }

        private double ResolveSlowHomeSearchSpeed(AxisParam axis, double fastHomeSearchSpeed)
        {
            if (axis.SlowHomeSearchSpeed > 0d)
            {
                return axis.SlowHomeSearchSpeed;
            }

            if (_homingOptions.SlowHomeSpeed > 0d)
            {
                return _homingOptions.SlowHomeSpeed;
            }

            return Math.Max(0.001d, fastHomeSearchSpeed / 2d);
        }

        private double ResolveSearchStartSpeed(double driveSpeedMmPerSec)
        {
            return Math.Max(0.001d, Math.Min(Math.Max(0.001d, _startSpeed), driveSpeedMmPerSec));
        }

        private static int ScaleSlowHomeTimeout(int baseTimeoutMs, double fastHomeSearchSpeed, double slowHomeSearchSpeed)
        {
            if (baseTimeoutMs <= 0)
            {
                return 100;
            }

            if (fastHomeSearchSpeed <= 0d || slowHomeSearchSpeed <= 0d)
            {
                return baseTimeoutMs;
            }

            var ratio = Math.Max(1d, fastHomeSearchSpeed / slowHomeSearchSpeed);
            var scaledTimeout = (long)Math.Ceiling(baseTimeoutMs * ratio);
            return scaledTimeout >= int.MaxValue ? int.MaxValue : (int)scaledTimeout;
        }

        private static void ValidateAxisNo(int axisNo)
        {
            if (axisNo < 1 || axisNo > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(axisNo), axisNo, "ADT8940 axis number must be between 1 and 4.");
            }
        }

        private void ExecuteNative(Func<int> action, string operation)
        {
            try
            {
                var result = action();
                RecordNativeCall(operation, result, result == 0 ? "调用成功。" : "调用返回失败码。", result == 0);
                if (result != 0)
                {
                    throw new InvalidOperationException($"{operation} failed with code {result}.");
                }
            }
            catch (DllNotFoundException ex)
            {
                RecordNativeCallException(operation, ex);
                throw new InvalidOperationException("ADT8940 driver library 8940A1m.dll was not found.", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                RecordNativeCallException(operation, ex);
                throw new InvalidOperationException("ADT8940 driver entry point was not found.", ex);
            }
        }

        private void ExecuteNative(NativeCallWithValue action, string operation, out int value)
        {
            value = default;

            try
            {
                var result = action(out value);
                RecordNativeCall(operation, result, $"调用返回值: {value}", result == 0);
                if (result != 0)
                {
                    throw new InvalidOperationException($"{operation} failed with code {result}.");
                }
            }
            catch (DllNotFoundException ex)
            {
                RecordNativeCallException(operation, ex);
                throw new InvalidOperationException("ADT8940 driver library 8940A1m.dll was not found.", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                RecordNativeCallException(operation, ex);
                throw new InvalidOperationException("ADT8940 driver entry point was not found.", ex);
            }
        }

        private int ExecuteNativeWithRawResult(Func<int> action, string operation)
        {
            try
            {
                var result = action();
                RecordNativeCall(operation, result, $"调用返回状态: {result}", result >= 0);
                return result;
            }
            catch (DllNotFoundException ex)
            {
                RecordNativeCallException(operation, ex);
                throw new InvalidOperationException("ADT8940 driver library 8940A1m.dll was not found.", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                RecordNativeCallException(operation, ex);
                throw new InvalidOperationException("ADT8940 driver entry point was not found.", ex);
            }
        }

        private void RecordNativeCall(string operation, int? resultCode, string message, bool isSuccess)
        {
            var sequenceId = Interlocked.Increment(ref _nativeCallSequence);
            var record = new NativeCallRecord(
                sequenceId,
                DateTime.Now,
                operation,
                resultCode,
                message,
                isSuccess);

            _nativeCallRecords.Enqueue(record);
            Interlocked.Increment(ref _nativeCallRecordCount);

            while (_nativeCallRecordCount > MaxNativeCallRecordCount && _nativeCallRecords.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _nativeCallRecordCount);
            }

            Debug.WriteLine($"[{record.Timestamp:HH:mm:ss.fff}] [ADT8940] Seq={record.SequenceId} | {record.Operation} | Code={record.ResultCode?.ToString() ?? "N/A"} | Success={record.IsSuccess} | {record.Message}");
        }

        private void RecordNativeCallException(string operation, Exception exception)
        {
            RecordNativeCall(operation, null, exception.Message, false);
        }

        private void LogHomeDebug(string messageTemplate, params object?[] propertyValues)
        {
            _logger.Information(messageTemplate, propertyValues);
        }

        private void LogHomeDebug(Exception? exception, string messageTemplate, params object?[] propertyValues)
        {
            if (exception is null)
            {
                _logger.Information(messageTemplate, propertyValues);
                return;
            }

            _logger.Error(exception, messageTemplate, propertyValues);
        }
    }
}
