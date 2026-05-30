using Common.Models;
using HAL;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BLL
{
    public sealed class MotionManager : IMotionService
    {
        private readonly IMoton _motion;
        private readonly ILogger _logger;
        private double _zHomeLiftMm;

        public IMoton Hardware => _motion;

        public AxisParam XAxis { get; private set; } = new(1, 0, 0, 0);
        public AxisParam YAxis { get; private set; } = new(2, 0, 0, 0);
        public AxisParam ZAxis { get; private set; } = new(3, 0, 0, 0);

        public MotionManager(IMoton motion)
            : this(motion, Log.Logger)
        {
        }

        public MotionManager(IMoton motion, ILogger logger)
        {
            _motion = motion ?? throw new ArgumentNullException(nameof(motion));
            _logger = (logger ?? Log.Logger).ForContext<MotionManager>();
            _motion.ConfigureAxes(XAxis, YAxis, ZAxis);
            _logger.Information("运动服务已初始化");
        }

        public MotionManager(IMoton motion, AxisParam xAxis, AxisParam yAxis, AxisParam zAxis)
            : this(motion, Log.Logger)
        {
            ConfigureAxes(xAxis, yAxis, zAxis);
        }

        public MotionManager(IMoton motion, AxisParam xAxis, AxisParam yAxis, AxisParam zAxis, ILogger logger)
            : this(motion, logger)
        {
            ConfigureAxes(xAxis, yAxis, zAxis);
        }

        public void ConfigureAxes(AxisParam xAxis, AxisParam yAxis, AxisParam zAxis)
        {
            XAxis = xAxis;
            YAxis = yAxis;
            ZAxis = zAxis;
            _motion.ConfigureAxes(XAxis, YAxis, ZAxis);
            _logger.Information("运动轴参数已更新: X轴={XAxisNo}, Y轴={YAxisNo}, Z轴={ZAxisNo}", XAxis.AxisNo, YAxis.AxisNo, ZAxis.AxisNo);
        }

        public void ConfigureXAxis(AxisParam axis)
        {
            XAxis = axis;
            _motion.ConfigureAxis(XAxis);
        }

        public void ConfigureYAxis(AxisParam axis)
        {
            YAxis = axis;
            _motion.ConfigureAxis(YAxis);
        }

        public void ConfigureZAxis(AxisParam axis)
        {
            ZAxis = axis;
            _motion.ConfigureAxis(ZAxis);
        }

        public void ConfigureZHomeLift(double liftMm)
        {
            _zHomeLiftMm = Math.Max(0d, liftMm);
            _logger.Information("Z轴复位后抬升距离已更新: {ZHomeLiftMm}", _zHomeLiftMm);
        }

        public void HomeAll(bool wait)
        {
            ExecuteSync(HomeAllAsync(wait));
        }

        public async Task HomeAllAsync(bool wait, CancellationToken cancellationToken = default)
        {
            var axisNos = GetAllAxisNos();
            await _motion.EnableAllAsync(axisNos).ConfigureAwait(false);

            if (wait)
            {
                await HomeXAsync(true, cancellationToken).ConfigureAwait(false);
                await HomeYAsync(true, cancellationToken).ConfigureAwait(false);
                await HomeZAsync(true, cancellationToken).ConfigureAwait(false);
                return;
            }

            await _motion.HomeAsync(XAxis.AxisNo, false, cancellationToken).ConfigureAwait(false);
            await _motion.HomeAsync(YAxis.AxisNo, false, cancellationToken).ConfigureAwait(false);
            await _motion.HomeAsync(ZAxis.AxisNo, false, cancellationToken).ConfigureAwait(false);
        }

        public void HomeX(bool wait = true)
        {
            ExecuteSync(HomeXAsync(wait));
        }

        public async Task HomeXAsync(bool wait = true, CancellationToken cancellationToken = default)
        {
            await _motion.EnableAsync(XAxis.AxisNo).ConfigureAwait(false);
            await HomeAxisAsync(XAxis, wait, cancellationToken).ConfigureAwait(false);
        }

        public void HomeY(bool wait = true)
        {
            ExecuteSync(HomeYAsync(wait));
        }

        public async Task HomeYAsync(bool wait = true, CancellationToken cancellationToken = default)
        {
            await _motion.EnableAsync(YAxis.AxisNo).ConfigureAwait(false);
            await HomeAxisAsync(YAxis, wait, cancellationToken).ConfigureAwait(false);
        }

        public void HomeZ(bool wait = true)
        {
            ExecuteSync(HomeZAsync(wait));
        }

        public async Task HomeZAsync(bool wait = true, CancellationToken cancellationToken = default)
        {
            await _motion.EnableAsync(ZAxis.AxisNo).ConfigureAwait(false);
            await HomeAxisAsync(ZAxis, wait, cancellationToken).ConfigureAwait(false);
            await ExecuteZPostHomeLiftAsync(wait, cancellationToken).ConfigureAwait(false);
        }

        public void MoveX(double position, double velocity, bool wait = true)
        {
            ExecuteSync(MoveXAsync(position, velocity, wait));
        }

        public Task MoveXAsync(double position, double velocity, bool wait = true, CancellationToken cancellationToken = default)
        {
            return MoveAxisAsync(XAxis, position, velocity, wait, cancellationToken, axis => XAxis = axis);
        }

        public void MoveY(double position, double velocity, bool wait = true)
        {
            ExecuteSync(MoveYAsync(position, velocity, wait));
        }

        public Task MoveYAsync(double position, double velocity, bool wait = true, CancellationToken cancellationToken = default)
        {
            return MoveAxisAsync(YAxis, position, velocity, wait, cancellationToken, axis => YAxis = axis);
        }

        public void MoveZ(double position, double velocity, bool wait = true)
        {
            ExecuteSync(MoveZAsync(position, velocity, wait));
        }

        public Task MoveZAsync(double position, double velocity, bool wait = true, CancellationToken cancellationToken = default)
        {
            return MoveAxisAsync(ZAxis, position, velocity, wait, cancellationToken, axis => ZAxis = axis);
        }

        public void StopAll()
        {
            ExecuteSync(_motion.StopAllAsync(GetAllAxisNos()));
        }

        public Task StopAllAsync()
        {
            return _motion.StopAllAsync(GetAllAxisNos());
        }

        public void EmergencyStopAll()
        {
            ExecuteSync(_motion.EmergencyStopAllAsync(GetAllAxisNos()));
        }

        public Task EmergencyStopAllAsync()
        {
            return _motion.EmergencyStopAllAsync(GetAllAxisNos());
        }

        public void EnableAll()
        {
            ExecuteSync(_motion.EnableAllAsync(GetAllAxisNos()));
        }

        public Task EnableAllAsync()
        {
            return _motion.EnableAllAsync(GetAllAxisNos());
        }

        public void DisableAll()
        {
            ExecuteSync(_motion.DisableAllAsync(GetAllAxisNos()));
        }

        public Task DisableAllAsync()
        {
            return _motion.DisableAllAsync(GetAllAxisNos());
        }

        public double GetXPosition()
        {
            return ExecuteSync(_motion.GetPositionAsync(XAxis.AxisNo));
        }

        public Task<double> GetXPositionAsync(CancellationToken cancellationToken = default)
        {
            return _motion.GetPositionAsync(XAxis.AxisNo, cancellationToken);
        }

        public double GetYPosition()
        {
            return ExecuteSync(_motion.GetPositionAsync(YAxis.AxisNo));
        }

        public Task<double> GetYPositionAsync(CancellationToken cancellationToken = default)
        {
            return _motion.GetPositionAsync(YAxis.AxisNo, cancellationToken);
        }

        public double GetZPosition()
        {
            return ExecuteSync(_motion.GetPositionAsync(ZAxis.AxisNo));
        }

        public Task<double> GetZPositionAsync(CancellationToken cancellationToken = default)
        {
            return _motion.GetPositionAsync(ZAxis.AxisNo, cancellationToken);
        }

        private async Task HomeAxisAsync(AxisParam axis, bool wait, CancellationToken cancellationToken)
        {
            await _motion.HomeAsync(axis.AxisNo, wait, cancellationToken).ConfigureAwait(false);
        }

        private async Task ExecuteZPostHomeLiftAsync(bool wait, CancellationToken cancellationToken)
        {
            if (_zHomeLiftMm <= 0d)
            {
                return;
            }

            if (!wait)
            {
                _logger.Information("Z轴复位后抬升已配置为 {ZHomeLiftMm} mm，但当前复位未等待完成，跳过本次业务抬升。", _zHomeLiftMm);
                return;
            }

            var liftVelocity = ZAxis.Velocity;
            var startPosition = await _motion.GetPositionAsync(ZAxis.AxisNo, cancellationToken).ConfigureAwait(false);
            var liftTimeoutMs = CalculateMoveTimeoutMs(startPosition, _zHomeLiftMm, liftVelocity, ZAxis.HomeTimeoutMs);
            _logger.Information("Z轴复位完成，开始执行业务抬升: Start={StartPosition}, Target={LiftTarget}, Velocity={LiftVelocity}, TimeoutMs={LiftTimeoutMs}", startPosition, _zHomeLiftMm, liftVelocity, liftTimeoutMs);
            await MoveAxisWithTimeoutAsync(ZAxis, _zHomeLiftMm, liftVelocity, liftTimeoutMs, cancellationToken, axis => ZAxis = axis).ConfigureAwait(false);
            _logger.Information("Z轴复位后业务抬升完成: Target={LiftTarget}", _zHomeLiftMm);
        }

        private async Task MoveAxisWithTimeoutAsync(
            AxisParam axis,
            double position,
            double velocity,
            int timeoutMs,
            CancellationToken cancellationToken,
            Action<AxisParam> updateAxis)
        {
            _logger.Verbose("请求移动轴 {AxisNo} 到位置 {Position}，速度 {Velocity}，等待完成: True，超时: {TimeoutMs}ms", axis.AxisNo, position, velocity, timeoutMs);
            ValidateAxisLimit(axis, position);
            updateAxis(axis);
            await _motion.EnableAsync(axis.AxisNo).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                await _motion.MoveAbsoluteAsync(axis.AxisNo, position, true, velocity, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                var currentPosition = await SafeGetAxisPositionAsync(axis.AxisNo).ConfigureAwait(false);
                throw new TimeoutException($"Axis {axis.AxisNo} move to {position} timed out after {timeoutMs} ms. CurrentPosition={currentPosition:F3}, Velocity={velocity:F3}.");
            }
        }

        private async Task MoveAxisAsync(
            AxisParam axis,
            double position,
            double velocity,
            bool wait,
            CancellationToken cancellationToken,
            Action<AxisParam> updateAxis)
        {
            _logger.Verbose("请求移动轴 {AxisNo} 到位置 {Position}，速度 {Velocity}，等待完成: {Wait}", axis.AxisNo, position, velocity, wait);
            ValidateAxisLimit(axis, position);
            updateAxis(axis);
            await _motion.EnableAsync(axis.AxisNo).ConfigureAwait(false);
            await _motion.MoveAbsoluteAsync(axis.AxisNo, position, wait, velocity, cancellationToken).ConfigureAwait(false);
        }

        private async Task<double> SafeGetAxisPositionAsync(int axisNo)
        {
            try
            {
                return await _motion.GetPositionAsync(axisNo).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "读取轴 {AxisNo} 当前位置失败。", axisNo);
                return double.NaN;
            }
        }

        private static int CalculateMoveTimeoutMs(double startPosition, double targetPosition, double velocity, int fallbackTimeoutMs)
        {
            var effectiveVelocity = velocity > 0d ? velocity : 1d;
            var expectedMoveMs = (int)Math.Ceiling(Math.Abs(targetPosition - startPosition) / effectiveVelocity * 1000d);
            var bufferedTimeoutMs = expectedMoveMs + 3000;
            var normalizedFallbackTimeoutMs = fallbackTimeoutMs > 0 ? fallbackTimeoutMs : 10000;
            return Math.Max(bufferedTimeoutMs, normalizedFallbackTimeoutMs);
        }

        private void ValidateAxisLimit(AxisParam axis, double position)
        {
            _logger.Verbose("验证轴 {AxisNo} 的目标位置 {Position} 是否在限位范围内,限位：左 {LeftLimit}, 右 {RightLimit}", axis.AxisNo, position, axis.LeftLimit, axis.RightLimit);
            if (axis.LeftLimit.HasValue && position < axis.LeftLimit.Value)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position, $"Axis {axis.AxisNo} position is less than left limit {axis.LeftLimit.Value}.");
            }

            if (axis.RightLimit.HasValue && position > axis.RightLimit.Value)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position, $"Axis {axis.AxisNo} position is greater than right limit {axis.RightLimit.Value}.");
            }
        }

        private int[] GetAllAxisNos()
        {
            return [XAxis.AxisNo, YAxis.AxisNo, ZAxis.AxisNo];
        }

        private static void ExecuteSync(Task task)
        {
            task.GetAwaiter().GetResult();
        }

        private static T ExecuteSync<T>(Task<T> task)
        {
            return task.GetAwaiter().GetResult();
        }
    }
}
