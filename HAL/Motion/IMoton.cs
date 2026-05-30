using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HAL
{
    public record struct AxisParam(
        int AxisNo,
        double Velocity,
        double Acceleration,
        double Deceleration,
        double? LeftLimit = null,
        double? RightLimit = null,
        double PulsesPerMillimeter = 1d,
        bool UseActualPositionFeedback = false,
        double? InPositionTolerance = null,
        double FastHomeSearchSpeed = 0d,
        double SlowHomeSearchSpeed = 0d,
        int HomeTimeoutMs = 0,
        int HomeMaxRetryCount = 3);

    public interface IMoton
    {
        void ConfigureAxis(AxisParam axis);

        void ConfigureAxes(params AxisParam[] axes);

        Task MoveAbsoluteAsync(int axisNo, double position, bool wait, double? velocity = null, CancellationToken cancellationToken = default);

        Task MoveRelativeAsync(int axisNo, double distance, bool wait, double? velocity = null, CancellationToken cancellationToken = default);

        Task HomeAsync(int axisNo, bool wait, CancellationToken cancellationToken = default);

        Task StopAsync(int axisNo);

        Task StopAllAsync(int[] axisNos);

        Task EmergencyStopAsync(int axisNo);

        Task EmergencyStopAllAsync(int[] axisNos);

        Task EnableAsync(int axisNo);

        Task EnableAllAsync(int[] axisNos);

        Task DisableAsync(int axisNo);

        Task DisableAllAsync(int[] axisNos);

        Task<double> GetPositionAsync(int axisNo, CancellationToken cancellationToken = default);
    }
}
