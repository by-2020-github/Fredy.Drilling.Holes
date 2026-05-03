using Common.Models;
using HAL;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BLL
{
    public sealed class MotionManager : IMotionService
    {
        private readonly IMoton _motion;

        public IMoton Hardware => _motion;

        public AxisParam XAxis { get; private set; } = new(1, 0, 0, 0);
        public AxisParam YAxis { get; private set; } = new(2, 0, 0, 0);
        public AxisParam ZAxis { get; private set; } = new(3, 0, 0, 0);

        public MotionManager(IMoton motion)
        {
            _motion = motion ?? throw new ArgumentNullException(nameof(motion));
            _motion.ConfigureAxes(XAxis, YAxis, ZAxis);
        }

        public MotionManager(IMoton motion, AxisParam xAxis, AxisParam yAxis, AxisParam zAxis)
            : this(motion)
        {
            ConfigureAxes(xAxis, yAxis, zAxis);
        }

        public void ConfigureAxes(AxisParam xAxis, AxisParam yAxis, AxisParam zAxis)
        {
            XAxis = xAxis;
            YAxis = yAxis;
            ZAxis = zAxis;
            _motion.ConfigureAxes(XAxis, YAxis, ZAxis);
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
                await HomeAxisAsync(XAxis, true, cancellationToken).ConfigureAwait(false);
                await HomeAxisAsync(YAxis, true, cancellationToken).ConfigureAwait(false);
                await HomeAxisAsync(ZAxis, true, cancellationToken).ConfigureAwait(false);
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

        private async Task MoveAxisAsync(
            AxisParam axis,
            double position,
            double velocity,
            bool wait,
            CancellationToken cancellationToken,
            Action<AxisParam> updateAxis)
        {
            ValidateAxisLimit(axis, position);
            var updatedAxis = axis with { Velocity = velocity };
            updateAxis(updatedAxis);
            await _motion.EnableAsync(updatedAxis.AxisNo).ConfigureAwait(false);
            await _motion.MoveAbsoluteAsync(updatedAxis.AxisNo, position, wait, cancellationToken).ConfigureAwait(false);
        }

        private static void ValidateAxisLimit(AxisParam axis, double position)
        {
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
