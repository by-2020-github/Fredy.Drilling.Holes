using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HAL
{
    public sealed  class MotionSimulator : IMoton
    {
        private readonly ConcurrentDictionary<int, AxisState> _axes = new();

        public void MoveAbsolute(int axisNo, double position, double velocity, double acceleration, double deceleration, bool wait)
        {
            ExecuteMoveAsync(axisNo, position, velocity, wait, CancellationToken.None).GetAwaiter().GetResult();
        }

        public void MoveRelative(int axisNo, double distance, double velocity, double acceleration, double deceleration, bool wait)
        {
            var axisState = GetAxisState(axisNo);
            double targetPosition;

            lock (axisState.SyncRoot)
            {
                targetPosition = axisState.Position + distance;
            }

            ExecuteMoveAsync(axisNo, targetPosition, velocity, wait, CancellationToken.None).GetAwaiter().GetResult();
        }

        public void MoveAbsolute(IReadOnlyList<AxisParaml> axisParams, bool wait)
        {
            ExecuteMultiMoveAsync(axisParams, true, wait, CancellationToken.None).GetAwaiter().GetResult();
        }

        public void MoveRelative(IReadOnlyList<AxisParaml> axisParams, bool wait)
        {
            ExecuteMultiMoveAsync(axisParams, false, wait, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task MoveAbsoluteAsync(int axisNo, double position, double velocity, double acceleration, double deceleration, bool wait, CancellationToken cancellationToken = default)
        {
            return ExecuteMoveAsync(axisNo, position, velocity, wait, cancellationToken);
        }

        public Task MoveRelativeAsync(int axisNo, double distance, double velocity, double acceleration, double deceleration, bool wait, CancellationToken cancellationToken = default)
        {
            var axisState = GetAxisState(axisNo);
            double targetPosition;

            lock (axisState.SyncRoot)
            {
                targetPosition = axisState.Position + distance;
            }

            return ExecuteMoveAsync(axisNo, targetPosition, velocity, wait, cancellationToken);
        }

        public Task MoveAbsoluteAsync(IReadOnlyList<AxisParaml> axisParams, bool wait, CancellationToken cancellationToken = default)
        {
            return ExecuteMultiMoveAsync(axisParams, true, wait, cancellationToken);
        }

        public Task MoveRelativeAsync(IReadOnlyList<AxisParaml> axisParams, bool wait, CancellationToken cancellationToken = default)
        {
            return ExecuteMultiMoveAsync(axisParams, false, wait, cancellationToken);
        }

        public void Home(int axisNo, bool wait)
        {
            HomeAsync(axisNo, wait, CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task HomeAsync(int axisNo, bool wait, CancellationToken cancellationToken = default)
        {
            return ExecuteMoveAsync(axisNo, 0d, 100d, wait, cancellationToken, markHomed: true);
        }

        public void Stop(int axisNo)
        {
            var axisState = GetAxisState(axisNo);
            CancellationTokenSource? cts;

            lock (axisState.SyncRoot)
            {
                cts = axisState.MotionCts;
            }

            cts?.Cancel();
        }

        public void StopAll()
        {
            foreach (var axisNo in _axes.Keys)
            {
                Stop(axisNo);
            }
        }

        public void EmergencyStop(int axisNo)
        {
            var axisState = GetAxisState(axisNo);
            CancellationTokenSource? cts;

            lock (axisState.SyncRoot)
            {
                axisState.IsEmergencyStopped = true;
                cts = axisState.MotionCts;
            }

            cts?.Cancel();
        }

        public void EmergencyStopAll()
        {
            foreach (var axisNo in _axes.Keys)
            {
                EmergencyStop(axisNo);
            }
        }

        public void Enable(int axisNo)
        {
            var axisState = GetAxisState(axisNo);
            lock (axisState.SyncRoot)
            {
                axisState.IsEnabled = true;
            }
        }

        public void Disable(int axisNo)
        {
            var axisState = GetAxisState(axisNo);
            lock (axisState.SyncRoot)
            {
                axisState.IsEnabled = false;
            }
        }

        public void Reset(int axisNo)
        {
            var axisState = GetAxisState(axisNo);
            lock (axisState.SyncRoot)
            {
                axisState.IsEmergencyStopped = false;
            }
        }

        public double GetPosition(int axisNo)
        {
            var axisState = GetAxisState(axisNo);
            lock (axisState.SyncRoot)
            {
                return axisState.Position;
            }
        }

        public Task<double> GetPositionAsync(int axisNo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(GetPosition(axisNo));
        }

        private Task ExecuteMoveAsync(int axisNo, double targetPosition, double velocity, bool wait, CancellationToken cancellationToken, bool markHomed = false)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            var axisState = GetAxisState(axisNo);
            var motionTask = StartMove(axisState, targetPosition, velocity, cancellationToken, markHomed);
            return wait ? motionTask : ObserveAsync(motionTask);
        }

        private Task ExecuteMultiMoveAsync(IReadOnlyList<AxisParaml> axisParams, bool isAbsolute, bool wait, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(axisParams);

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            var tasks = new Task[axisParams.Count];
            for (var i = 0; i < axisParams.Count; i++)
            {
                var axisParam = axisParams[i];
                var axisState = GetAxisState(axisParam.AxisNo);
                var targetPosition = axisParam.Position;

                if (!isAbsolute)
                {
                    lock (axisState.SyncRoot)
                    {
                        targetPosition = axisState.Position + axisParam.Position;
                    }
                }

                tasks[i] = StartMove(axisState, targetPosition, axisParam.Velocity, cancellationToken, markHomed: false);
            }

            var whenAllTask = Task.WhenAll(tasks);
            return wait ? whenAllTask : ObserveAsync(whenAllTask);
        }

        private Task StartMove(AxisState axisState, double targetPosition, double velocity, CancellationToken cancellationToken, bool markHomed)
        {
            CancellationTokenSource linkedCts;
            Task motionTask;

            lock (axisState.SyncRoot)
            {
                EnsureAxisCanMove(axisState);
                axisState.MotionCts?.Cancel();
                axisState.MotionCts?.Dispose();
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                axisState.MotionCts = linkedCts;
                motionTask = RunMotionAsync(axisState, targetPosition, velocity, linkedCts.Token, markHomed);
                axisState.CurrentTask = motionTask;
            }

            return motionTask;
        }

        private static async Task ObserveAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task RunMotionAsync(AxisState axisState, double targetPosition, double velocity, CancellationToken cancellationToken, bool markHomed)
        {
            double startPosition;

            lock (axisState.SyncRoot)
            {
                startPosition = axisState.Position;
            }

            var distance = Math.Abs(targetPosition - startPosition);
            var normalizedVelocity = velocity <= 0 ? 100d : Math.Abs(velocity);
            var duration = TimeSpan.FromSeconds(distance / normalizedVelocity);

            if (duration > TimeSpan.Zero)
            {
                await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
            }

            lock (axisState.SyncRoot)
            {
                axisState.Position = targetPosition;
                axisState.IsHomed = markHomed || targetPosition == 0d;
            }
        }

        private static void EnsureAxisCanMove(AxisState axisState)
        {
            if (!axisState.IsEnabled)
            {
                throw new InvalidOperationException("Axis is disabled.");
            }

            if (axisState.IsEmergencyStopped)
            {
                throw new InvalidOperationException("Axis is in emergency stop state.");
            }
        }

        private AxisState GetAxisState(int axisNo)
        {
            return _axes.GetOrAdd(axisNo, _ => new AxisState());
        }

        private sealed class AxisState
        {
            public object SyncRoot { get; } = new();

            public double Position { get; set; }

            public bool IsEnabled { get; set; } = true;

            public bool IsHomed { get; set; }

            public bool IsEmergencyStopped { get; set; }

            public CancellationTokenSource? MotionCts { get; set; }

            public Task CurrentTask { get; set; } = Task.CompletedTask;
        }
    }
}
