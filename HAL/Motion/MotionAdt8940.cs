using Demo;
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
        public readonly record struct HomingPort(
            int PortIndex,
            bool IsLowLevelActive);

        public readonly record struct HomingOptions(
            int HomeSearchSpeed,
            bool IsIoHome,
            bool IsLatch,
            bool IsGratingHome,
            HomingPort XLimitPort,
            HomingPort YLimitPort,
            HomingPort ZLimitPort,
            HomingPort XGratingPort,
            HomingPort YGratingPort,
            int HomeTimeoutMs = 10000,
            int HomeBackoffPulse = 200,
            int ZHomeLiftPulse = 0,
            bool ZHomeTowardPositiveDirection = false,
            int SlowHomeStartSpeed = 100,
            int SlowHomeSpeed = 500,
            int SlowHomeAcceleration = 1000,
            int GratingHomeStartSpeed = 500,
            int GratingHomeSpeed = 2000,
            int GratingHomeAcceleration = 2000);

        public readonly record struct NativeCallRecord(
            long SequenceId,
            DateTime Timestamp,
            string Operation,
            int? ResultCode,
            string Message,
            bool IsSuccess);

        private delegate int NativeCallWithValue(out int value);

        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);
        private const int MaxNativeCallRecordCount = 500;

        private readonly ConcurrentDictionary<int, bool> _axisEnabled = new();
        private readonly ConcurrentDictionary<int, AxisParam> _axisParams = new();
        private readonly ConcurrentQueue<NativeCallRecord> _nativeCallRecords = new();
        private readonly object _syncRoot = new();
        private readonly int _cardNo;
        private readonly int _startSpeed;
        private readonly int _driveSpeed;
        private readonly int _acceleration;
        private int _homeSearchSpeed;
        private int _homeApproachSpeed;
        private bool _initialized;
        private int _nativeCallRecordCount;
        private long _nativeCallSequence;
        private HomingOptions _homingOptions;

        public NativeCallRecord[] NativeCallRecords => _nativeCallRecords.ToArray();

        public void ConfigureAxis(AxisParam axis)
        {
            ValidateAxisNo(axis.AxisNo);
            _axisParams[axis.AxisNo] = axis with { PulsesPerMillimeter = NormalizePulseEquivalent(axis.PulsesPerMillimeter) };
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
            int cardNo = 0,
            int startSpeed = 100,
            int driveSpeed = 9000,
            int acceleration = 1250,
            int homeSearchSpeed = 3000,
            int homeApproachSpeed = 700)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(cardNo);
            ArgumentOutOfRangeException.ThrowIfLessThan(startSpeed, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(driveSpeed, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(acceleration, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(homeSearchSpeed, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(homeApproachSpeed, 1);

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
                new HomingPort(14, false),
                new HomingPort(14, false),
                new HomingPort(14, false),
                new HomingPort(0, true),
                new HomingPort(0, true));
        }

        public void ConfigureHoming(HomingOptions options)
        {
            _homingOptions = options with
            {
                HomeSearchSpeed = Math.Max(1, options.HomeSearchSpeed),
                HomeTimeoutMs = Math.Max(100, options.HomeTimeoutMs),
                HomeBackoffPulse = Math.Max(0, options.HomeBackoffPulse),
                SlowHomeStartSpeed = Math.Max(1, options.SlowHomeStartSpeed),
                SlowHomeSpeed = Math.Max(1, options.SlowHomeSpeed),
                SlowHomeAcceleration = Math.Max(1, options.SlowHomeAcceleration),
                GratingHomeStartSpeed = Math.Max(1, options.GratingHomeStartSpeed),
                GratingHomeSpeed = Math.Max(1, options.GratingHomeSpeed),
                GratingHomeAcceleration = Math.Max(1, options.GratingHomeAcceleration)
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

        public Task MoveAbsoluteAsync(int axisNo, double position, bool wait, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);
            EnsureAxisEnabled(axisNo);
            var axis = GetAxisParam(axisNo);

            ConfigureMotion(axisNo);

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
            RecordNativeCall($"Home axis {axisNo}", null, "开始执行旧 ProcessManager 风格回零流程。", true);

            if (axisNo == 3)
            {
                await HomeZAxisAsync(axisNo, cancellationToken).ConfigureAwait(false);
                return;
            }

            await HomeXYAxisAsync(axisNo, cancellationToken).ConfigureAwait(false);
        }

        private async Task HomeXYAxisAsync(int axisNo, CancellationToken cancellationToken)
        {
            Exception? lastException = null;

            for (var retry = 0; retry < 3; retry++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await HomeXYMechanicalAsync(axisNo, cancellationToken).ConfigureAwait(false);

                    if (_homingOptions.IsGratingHome)
                    {
                        if (_homingOptions.IsLatch)
                        {
                            await HomeXYByLatchAsync(axisNo, cancellationToken).ConfigureAwait(false);
                        }
                        else if (_homingOptions.IsIoHome)
                        {
                            await HomeXYByGratingIoAsync(axisNo, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    ResetPosition(axisNo);
                    RecordNativeCall($"Home axis {axisNo}", null, $"轴 {axisNo} 回零完成。", true);
                    return;
                }
                catch (OperationCanceledException)
                {
                    await EmergencyStopAsync(axisNo).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    RecordNativeCall($"Home axis {axisNo}", null, $"轴 {axisNo} 回零失败，准备重试：{ex.Message}", false);
                    await StopAsync(axisNo).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException($"Home axis {axisNo} failed after maximum retries.", lastException);
        }

        private async Task HomeZAxisAsync(int axisNo, CancellationToken cancellationToken)
        {
            var port = GetMechanicalPort(axisNo);
            var activeLevel = GetActiveLevel(port);
            var inactiveLevel = GetInactiveLevel(port);
            var currentLevel = await ReadInputLevelAsync(port.PortIndex, cancellationToken).ConfigureAwait(false);

            if (currentLevel == activeLevel)
            {
                await MoveContinuousUntilInputLevelAsync(
                    axisNo,
                    _homingOptions.ZHomeTowardPositiveDirection ? 0 : 1,
                    port,
                    inactiveLevel,
                    _startSpeed,
                    _homingOptions.HomeSearchSpeed,
                    _acceleration,
                    _homingOptions.HomeTimeoutMs,
                    cancellationToken).ConfigureAwait(false);
            }

            await MoveContinuousUntilInputLevelAsync(
                axisNo,
                _homingOptions.ZHomeTowardPositiveDirection ? 1 : 0,
                port,
                activeLevel,
                _startSpeed,
                _homingOptions.HomeSearchSpeed,
                _acceleration,
                _homingOptions.HomeTimeoutMs,
                cancellationToken).ConfigureAwait(false);

            ResetPosition(axisNo);

            if (_homingOptions.ZHomeLiftPulse > 0)
            {
                await MoveRelativePulseAsync(axisNo, _homingOptions.ZHomeLiftPulse, cancellationToken).ConfigureAwait(false);
            }

            RecordNativeCall($"Home axis {axisNo}", null, $"轴 {axisNo} Z 回零流程完成。", true);
        }

        private async Task HomeXYMechanicalAsync(int axisNo, CancellationToken cancellationToken)
        {
            var port = GetMechanicalPort(axisNo);
            var activeLevel = GetActiveLevel(port);
            var inactiveLevel = GetInactiveLevel(port);
            var currentLevel = await ReadInputLevelAsync(port.PortIndex, cancellationToken).ConfigureAwait(false);

            if (currentLevel == activeLevel)
            {
                await MoveContinuousUntilInputLevelAsync(
                    axisNo,
                    1,
                    port,
                    inactiveLevel,
                    _startSpeed,
                    _homingOptions.HomeSearchSpeed,
                    _acceleration,
                    _homingOptions.HomeTimeoutMs,
                    cancellationToken).ConfigureAwait(false);

                if (_homingOptions.HomeBackoffPulse > 0)
                {
                    await MoveRelativePulseAsync(axisNo, _homingOptions.HomeBackoffPulse, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                await MoveContinuousUntilInputLevelAsync(
                    axisNo,
                    0,
                    port,
                    inactiveLevel,
                    _startSpeed,
                    _homingOptions.HomeSearchSpeed,
                    _acceleration,
                    _homingOptions.HomeTimeoutMs,
                    cancellationToken).ConfigureAwait(false);
            }

            await MoveContinuousUntilInputLevelAsync(
                axisNo,
                1,
                port,
                activeLevel,
                _homingOptions.SlowHomeStartSpeed,
                _homingOptions.SlowHomeSpeed,
                _homingOptions.SlowHomeAcceleration,
                _homingOptions.HomeTimeoutMs,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task HomeXYByGratingIoAsync(int axisNo, CancellationToken cancellationToken)
        {
            var port = GetGratingPort(axisNo);
            ValidatePort(port, $"Axis {axisNo} grating");

            await MoveContinuousUntilInputLevelAsync(
                axisNo,
                0,
                port,
                GetActiveLevel(port),
                _homingOptions.GratingHomeStartSpeed,
                _homingOptions.GratingHomeSpeed,
                _homingOptions.GratingHomeAcceleration,
                _homingOptions.HomeTimeoutMs,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task HomeXYByLatchAsync(int axisNo, CancellationToken cancellationToken)
        {
            await ClearLockStatusAsync(axisNo, cancellationToken).ConfigureAwait(false);
            await SetLockPositionModeAsync(axisNo, 1, 0, 1, cancellationToken).ConfigureAwait(false);

            try
            {
                ConfigureMotion(axisNo, _homingOptions.GratingHomeStartSpeed, _homingOptions.GratingHomeSpeed, _homingOptions.GratingHomeAcceleration);
                ExecuteNative(() => adt8940a1.adt8940a1_continue_move(_cardNo, axisNo, 0), $"Start latch home move for axis {axisNo}");

                await WaitForLockStatusAsync(axisNo, cancellationToken).ConfigureAwait(false);
                await StopAsync(axisNo).ConfigureAwait(false);

                var lockPosition = GetLockPositionPulse(axisNo);
                var currentPosition = GetCommandPosition(axisNo);
                var moveDistance = lockPosition - currentPosition;
                if (moveDistance != 0)
                {
                    await MoveRelativePulseAsync(axisNo, moveDistance, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                await SetLockPositionModeAsync(axisNo, 0, 0, 0, cancellationToken).ConfigureAwait(false);
                await ClearLockStatusAsync(axisNo, cancellationToken).ConfigureAwait(false);
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
            ConfigureMotion(axisNo, startSpeed, driveSpeed, acceleration);
            ExecuteNative(() => adt8940a1.adt8940a1_continue_move(_cardNo, axisNo, dir), $"Continuous move axis {axisNo} dir {dir}");

            try
            {
                var startedAt = Environment.TickCount64;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var level = await ReadInputLevelAsync(port.PortIndex, cancellationToken).ConfigureAwait(false);
                    if (level == targetLevel)
                    {
                        return;
                    }

                    if (Environment.TickCount64 - startedAt >= timeoutMs)
                    {
                        throw new TimeoutException($"Axis {axisNo} home input wait timed out on port {port.PortIndex}.");
                    }

                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                await StopAsync(axisNo).ConfigureAwait(false);
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

        private async Task WaitForLockStatusAsync(int axisNo, CancellationToken cancellationToken)
        {
            var startedAt = Environment.TickCount64;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await GetLockStatusAsync(axisNo, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                if (Environment.TickCount64 - startedAt >= _homingOptions.HomeTimeoutMs)
                {
                    throw new TimeoutException($"Axis {axisNo} latch home timed out.");
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task MoveRelativeAsync(int axisNo, double distance, bool wait, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);
            EnsureAxisEnabled(axisNo);
            var axis = GetAxisParam(axisNo);

            ConfigureMotion(axisNo);

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

        private void ConfigureHome(int axisNo)
        {
            ExecuteNative(
                () => adt8940a1.adt8940a1_SetHomeMode_Ex(_cardNo, axisNo, 0, 0, 0, -1, 100, 10, 0),
                $"Configure home mode for axis {axisNo}");

            ExecuteNative(
                () => adt8940a1.adt8940a1_SetHomeSpeed_Ex(_cardNo, axisNo, _startSpeed, _homeSearchSpeed, _homeApproachSpeed, ToCardAcceleration(_acceleration), _homeApproachSpeed),
                $"Configure home speed for axis {axisNo}");
        }

        private void ConfigureMotion(int axisNo)
        {
            ExecuteNative(() => adt8940a1.adt8940a1_set_startv(_cardNo, axisNo, _startSpeed), $"Set start speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_speed(_cardNo, axisNo, _driveSpeed), $"Set drive speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_acc(_cardNo, axisNo, ToCardAcceleration(_acceleration)), $"Set acceleration for axis {axisNo}");
        }

        private void ConfigureMotion(int axisNo, int startSpeed, int driveSpeed, int acceleration)
        {
            ExecuteNative(() => adt8940a1.adt8940a1_set_startv(_cardNo, axisNo, Math.Max(1, startSpeed)), $"Set start speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_speed(_cardNo, axisNo, Math.Max(1, driveSpeed)), $"Set drive speed for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_acc(_cardNo, axisNo, ToCardAcceleration(Math.Max(1, acceleration))), $"Set acceleration for axis {axisNo}");
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

        private static int GetActiveLevel(HomingPort port)
        {
            return port.IsLowLevelActive ? 0 : 1;
        }

        private static int GetInactiveLevel(HomingPort port)
        {
            return port.IsLowLevelActive ? 1 : 0;
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
    }
}
