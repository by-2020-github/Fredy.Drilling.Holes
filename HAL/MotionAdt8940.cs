using Demo;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HAL
{
    public class MotionAdt8940 : IMoton
    {
        private delegate int NativeCallWithValue(out int value);

        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

        private readonly ConcurrentDictionary<int, bool> _axisEnabled = new();
        private readonly object _syncRoot = new();
        private readonly int _cardNo;
        private readonly int _startSpeed;
        private readonly int _driveSpeed;
        private readonly int _acceleration;
        private readonly int _homeSearchSpeed;
        private readonly int _homeApproachSpeed;
        private bool _initialized;

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

            ExecuteNative(
                (out int pos) => adt8940a1.adt8940a1_get_actual_pos(_cardNo, axisNo, out pos),
                $"Get position for axis {axisNo}",
                out var position);

            return Task.FromResult((double)position);
        }

        public Task HomeAsync(int axisNo, bool wait, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);
            EnsureAxisEnabled(axisNo);

            ConfigureHome(axisNo);
            ExecuteNative(() => adt8940a1.adt8940a1_HomeProcess_Ex(_cardNo, axisNo), $"Home axis {axisNo}");

            return wait ? WaitForHomeAsync(axisNo, cancellationToken) : Task.CompletedTask;
        }

        public Task MoveAbsoluteAsync(int axisNo, double position, bool wait, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);
            EnsureAxisEnabled(axisNo);

            ConfigureMotion(axisNo);

            var targetPulse = ToPulse(position);
            var currentPulse = GetCommandPosition(axisNo);
            var distance = checked(targetPulse - currentPulse);
            if (distance == 0)
            {
                return Task.CompletedTask;
            }

            ExecuteNative(() => adt8940a1.adt8940a1_pmove(_cardNo, axisNo, distance), $"Move absolute axis {axisNo}");
            return wait ? WaitForAxisStopAsync(axisNo, cancellationToken) : Task.CompletedTask;
        }

        public Task MoveRelativeAsync(int axisNo, double distance, bool wait, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAxisReady(axisNo);
            EnsureAxisEnabled(axisNo);

            ConfigureMotion(axisNo);

            var pulseDistance = ToPulse(distance);
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

        private static int ToPulse(double value)
        {
            return checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
        }

        private async Task WaitForAxisStopAsync(int axisNo, CancellationToken cancellationToken)
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
                    return;
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task WaitForHomeAsync(int axisNo, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var status = adt8940a1.adt8940a1_GetHomeStatus_Ex(_cardNo, axisNo);
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

                var result = adt8940a1.adt8940a1_initial();
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

        private void ResetPosition(int axisNo)
        {
            ExecuteNative(() => adt8940a1.adt8940a1_set_command_pos(_cardNo, axisNo, 0), $"Reset command position for axis {axisNo}");
            ExecuteNative(() => adt8940a1.adt8940a1_set_actual_pos(_cardNo, axisNo, 0), $"Reset actual position for axis {axisNo}");
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

        private static void ExecuteNative(Func<int> action, string operation)
        {
            try
            {
                var result = action();
                if (result != 0)
                {
                    throw new InvalidOperationException($"{operation} failed with code {result}.");
                }
            }
            catch (DllNotFoundException ex)
            {
                throw new InvalidOperationException("ADT8940 driver library 8940A1m.dll was not found.", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new InvalidOperationException("ADT8940 driver entry point was not found.", ex);
            }
        }

        private static void ExecuteNative(NativeCallWithValue action, string operation, out int value)
        {
            value = default;

            try
            {
                var result = action(out value);
                if (result != 0)
                {
                    throw new InvalidOperationException($"{operation} failed with code {result}.");
                }
            }
            catch (DllNotFoundException ex)
            {
                throw new InvalidOperationException("ADT8940 driver library 8940A1m.dll was not found.", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new InvalidOperationException("ADT8940 driver entry point was not found.", ex);
            }
        }
    }
}
