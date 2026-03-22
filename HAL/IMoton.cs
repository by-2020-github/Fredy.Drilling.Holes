using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HAL
{
    public record struct AxisParaml(
        int AxisNo,
        double Position,
        double Velocity,
        double Acceleration,
        double Deceleration);

    public interface IMoton
    {
        void MoveAbsolute(int axisNo, double position, double velocity, double acceleration, double deceleration, bool wait);

        void MoveRelative(int axisNo, double distance, double velocity, double acceleration, double deceleration, bool wait);

        void MoveAbsolute(IReadOnlyList<AxisParaml> axisParams, bool wait);

        void MoveRelative(IReadOnlyList<AxisParaml> axisParams, bool wait);

        Task MoveAbsoluteAsync(int axisNo, double position, double velocity, double acceleration, double deceleration, bool wait, CancellationToken cancellationToken = default);

        Task MoveRelativeAsync(int axisNo, double distance, double velocity, double acceleration, double deceleration, bool wait, CancellationToken cancellationToken = default);

        Task MoveAbsoluteAsync(IReadOnlyList<AxisParaml> axisParams, bool wait, CancellationToken cancellationToken = default);

        Task MoveRelativeAsync(IReadOnlyList<AxisParaml> axisParams, bool wait, CancellationToken cancellationToken = default);

        void Home(int axisNo, bool wait);

        Task HomeAsync(int axisNo, bool wait, CancellationToken cancellationToken = default);

        void Stop(int axisNo);

        void StopAll();

        void EmergencyStop(int axisNo);

        void EmergencyStopAll();

        void Enable(int axisNo);

        void Disable(int axisNo);

        void Reset(int axisNo);

        double GetPosition(int axisNo);

        Task<double> GetPositionAsync(int axisNo, CancellationToken cancellationToken = default);
    }
}
