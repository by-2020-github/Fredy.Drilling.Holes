using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HAL
{
    public sealed class MotionSimulator : IMoton
    {
        private const double DefaultSimulatedSpeed = 100d;
        private const double PositionTolerance = 0.001d;
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(20);

        private readonly ConcurrentDictionary<int, AxisState> _axes = new();
        private readonly ConcurrentDictionary<int, AxisParam> _axisParams = new();

        public void ConfigureAxis(AxisParam axis)
        {
            _axisParams[axis.AxisNo] = axis;
        }

        public void ConfigureAxes(params AxisParam[] axes)
        {
            ArgumentNullException.ThrowIfNull(axes);

            for (var i = 0; i < axes.Length; i++)
            {
                ConfigureAxis(axes[i]);
            }
        }

        public Task DisableAllAsync(int[] axisNos)
        {
            return ExecuteForAllAsync(axisNos, DisableAsync);
        }

        public Task DisableAsync(int axisNo)
        {
            var axisState = GetAxisState(axisNo);
            CancellationTokenSource? motionCts;

            lock (axisState.SyncRoot)
            {
                axisState.Enabled = false;
                motionCts = axisState.MotionCts;
                axisState.MotionCts = null;
            }

            CancelMotion(motionCts);
            return Task.CompletedTask;
        }

        public Task EmergencyStopAllAsync(int[] axisNos)
        {
            return ExecuteForAllAsync(axisNos, EmergencyStopAsync);
        }

        public Task EmergencyStopAsync(int axisNo)
        {
            var axisState = GetAxisState(axisNo);
            CancellationTokenSource? motionCts;

            lock (axisState.SyncRoot)
            {
                axisState.Enabled = false;
                motionCts = axisState.MotionCts;
                axisState.MotionCts = null;
            }

            CancelMotion(motionCts);
            return Task.CompletedTask;
        }

        public Task EnableAllAsync(int[] axisNos)
        {
            return ExecuteForAllAsync(axisNos, EnableAsync);
        }

        public Task EnableAsync(int axisNo)
        {
            var axisState = GetAxisState(axisNo);
            lock (axisState.SyncRoot)
            {
                axisState.Enabled = true;
            }

            return Task.CompletedTask;
        }

        public Task<double> GetPositionAsync(int axisNo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var axisState = GetAxisState(axisNo);
            lock (axisState.SyncRoot)
            {
                return Task.FromResult(axisState.Position);
            }
        }

        public Task HomeAsync(int axisNo, bool wait, CancellationToken cancellationToken = default)
        {
            return MoveCoreAsync(axisNo, 0d, wait, velocity: null, cancellationToken: cancellationToken);
        }

        public Task MoveAbsoluteAsync(int axisNo, double position, bool wait, double? velocity = null, CancellationToken cancellationToken = default)
        {
            return MoveCoreAsync(axisNo, position, wait, velocity: velocity, cancellationToken: cancellationToken);
        }

        public Task MoveRelativeAsync(int axisNo, double distance, bool wait, double? velocity = null, CancellationToken cancellationToken = default)
        {
            var axisState = GetAxisState(axisNo);
            double target;

            lock (axisState.SyncRoot)
            {
                target = axisState.Position + distance;
            }

            return MoveCoreAsync(axisNo, target, wait, velocity: velocity, cancellationToken: cancellationToken);
        }

        public Task StopAllAsync(int[] axisNos)
        {
            return ExecuteForAllAsync(axisNos, StopAsync);
        }

        public Task StopAsync(int axisNo)
        {
            var axisState = GetAxisState(axisNo);
            CancellationTokenSource? motionCts;

            lock (axisState.SyncRoot)
            {
                motionCts = axisState.MotionCts;
                axisState.MotionCts = null;
            }

            CancelMotion(motionCts);
            return Task.CompletedTask;
        }

        private static void CancelMotion(CancellationTokenSource? motionCts)
        {
            if (motionCts is null)
            {
                return;
            }

            motionCts.Cancel();
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

        private AxisState GetAxisState(int axisNo)
        {
            return _axes.GetOrAdd(axisNo, static _ => new AxisState());
        }

        private Task MoveCoreAsync(int axisNo, double targetPosition, bool wait, double? velocity, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var axisState = GetAxisState(axisNo);
            CancellationTokenSource? previousMotion;
            CancellationTokenSource motionCts;

            lock (axisState.SyncRoot)
            {
                if (!axisState.Enabled)
                {
                    throw new InvalidOperationException($"Axis {axisNo} is disabled.");
                }

                previousMotion = axisState.MotionCts;
                motionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                axisState.MotionCts = motionCts;
            }

            CancelMotion(previousMotion);

            var motionTask = SimulateMoveAsync(axisNo, axisState, motionCts, targetPosition, velocity);
            if (wait)
            {
                return motionTask;
            }

            ObserveTask(motionTask);
            return Task.CompletedTask;
        }

        private static void ObserveTask(Task task)
        {
            _ = task.ContinueWith(
                static completedTask => _ = completedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task SimulateMoveAsync(int axisNo, AxisState axisState, CancellationTokenSource motionCts, double targetPosition, double? velocity)
        {
            var simulatedSpeed = GetSimulatedSpeed(axisNo, velocity);

            try
            {
                while (true)
                {
                    motionCts.Token.ThrowIfCancellationRequested();

                    double currentPosition;
                    lock (axisState.SyncRoot)
                    {
                        currentPosition = axisState.Position;
                    }

                    var remainingDistance = targetPosition - currentPosition;
                    if (Math.Abs(remainingDistance) <= PositionTolerance)
                    {
                        lock (axisState.SyncRoot)
                        {
                            axisState.Position = targetPosition;
                        }

                        return;
                    }

                    var step = Math.Sign(remainingDistance) * Math.Min(
                        Math.Abs(remainingDistance),
                        simulatedSpeed * UpdateInterval.TotalSeconds);

                    await Task.Delay(UpdateInterval, motionCts.Token).ConfigureAwait(false);

                    lock (axisState.SyncRoot)
                    {
                        axisState.Position += step;
                    }
                }
            }
            finally
            {
                lock (axisState.SyncRoot)
                {
                    if (ReferenceEquals(axisState.MotionCts, motionCts))
                    {
                        axisState.MotionCts = null;
                    }
                }

                motionCts.Dispose();
            }
        }

        private double GetSimulatedSpeed(int axisNo, double? velocity)
        {
            if (velocity.HasValue && velocity.Value > 0)
            {
                return velocity.Value;
            }

            if (_axisParams.TryGetValue(axisNo, out var axis) && axis.Velocity > 0)
            {
                return axis.Velocity;
            }

            return DefaultSimulatedSpeed;
        }

        private sealed class AxisState
        {
            public bool Enabled { get; set; } = true;

            public double Position { get; set; }

            public CancellationTokenSource? MotionCts { get; set; }

            public object SyncRoot { get; } = new();
        }
    }
}
