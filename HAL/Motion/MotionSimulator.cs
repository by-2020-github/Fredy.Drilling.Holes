using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace HAL
{
    public sealed class MotionSimulator : IMoton
    {
        private const double DefaultSimulatedSpeed = 100d;
        private const double PositionTolerance = 0.001d;
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(20);
        private static readonly Random _random = new();

        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<int, AxisState> _axes = new();
        private readonly ConcurrentDictionary<int, AxisParam> _axisParams = new();

        public MotionSimulator(ILogger logger)
        {
            _logger = logger.ForContext<MotionSimulator>();
        }

        public void ConfigureAxis(AxisParam axis)
        {
            _axisParams[axis.AxisNo] = axis;
            _logger.Debug("[MotionSimulator] ConfigureAxis: AxisNo={AxisNo}, Velocity={Velocity}", axis.AxisNo, axis.Velocity);
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

            _logger.Debug("[MotionSimulator] DisableAsync: AxisNo={AxisNo}", axisNo);
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

            _logger.Debug("[MotionSimulator] EmergencyStopAsync: AxisNo={AxisNo}", axisNo);
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

            _logger.Debug("[MotionSimulator] EnableAsync: AxisNo={AxisNo}", axisNo);
            return Task.CompletedTask;
        }

        public Task<double> GetPositionAsync(int axisNo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var axisState = GetAxisState(axisNo);
            double position;
            lock (axisState.SyncRoot)
            {
                position = axisState.Position;
            }

            _logger.Verbose("[MotionSimulator] GetPositionAsync: AxisNo={AxisNo}, Position={Position}", axisNo, position);
            return Task.FromResult(position);
        }

        public async Task HomeAsync(int axisNo, bool wait, CancellationToken cancellationToken = default)
        {
            var delayMs = _random.Next(1000, 3001);
            _logger.Debug("[MotionSimulator] HomeAsync: AxisNo={AxisNo}, Wait={Wait}, SimulatedDelay={DelayMs}ms", axisNo, wait, delayMs);
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            await MoveCoreAsync(axisNo, 0d, wait, velocity: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            _logger.Debug("[MotionSimulator] HomeAsync completed: AxisNo={AxisNo}", axisNo);
        }

        public Task MoveAbsoluteAsync(int axisNo, double position, bool wait, double? velocity = null, CancellationToken cancellationToken = default)
        {
            _logger.Debug("[MotionSimulator] MoveAbsoluteAsync: AxisNo={AxisNo}, Target={Position}, Wait={Wait}, Velocity={Velocity}", axisNo, position, wait, velocity);
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

            _logger.Debug("[MotionSimulator] MoveRelativeAsync: AxisNo={AxisNo}, Distance={Distance}, Target={Target}, Wait={Wait}, Velocity={Velocity}", axisNo, distance, target, wait, velocity);
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

            _logger.Debug("[MotionSimulator] StopAsync: AxisNo={AxisNo}", axisNo);
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
            _logger.Debug("[MotionSimulator] SimulateMoveAsync start: AxisNo={AxisNo}, Target={Target}, Speed={Speed}", axisNo, targetPosition, simulatedSpeed);

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

                        _logger.Debug("[MotionSimulator] SimulateMoveAsync done: AxisNo={AxisNo}, Position={Position}", axisNo, targetPosition);
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
