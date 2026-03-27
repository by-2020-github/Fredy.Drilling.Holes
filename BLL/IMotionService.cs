using Common.Models;
using HAL;
using System.Threading;
using System.Threading.Tasks;

namespace BLL
{
    public interface IMotionService
    {
        AxisParam XAxis { get; }
        AxisParam YAxis { get; }
        AxisParam ZAxis { get; }

        void ConfigureAxes(AxisParam xAxis, AxisParam yAxis, AxisParam zAxis);
        void ConfigureXAxis(AxisParam axis);
        void ConfigureYAxis(AxisParam axis);
        void ConfigureZAxis(AxisParam axis);

        void HomeAll(bool wait);
        Task HomeAllAsync(bool wait, CancellationToken cancellationToken = default);
        void HomeX(bool wait = true);
        Task HomeXAsync(bool wait = true, CancellationToken cancellationToken = default);
        void HomeY(bool wait = true);
        Task HomeYAsync(bool wait = true, CancellationToken cancellationToken = default);
        void HomeZ(bool wait = true);
        Task HomeZAsync(bool wait = true, CancellationToken cancellationToken = default);

        void MoveX(double position, double velocity, bool wait = true);
        Task MoveXAsync(double position, double velocity, bool wait = true, CancellationToken cancellationToken = default);

        void MoveY(double position, double velocity, bool wait = true);
        Task MoveYAsync(double position, double velocity, bool wait = true, CancellationToken cancellationToken = default);

        void MoveZ(double position, double velocity, bool wait = true);
        Task MoveZAsync(double position, double velocity, bool wait = true, CancellationToken cancellationToken = default);

        void MoveToPunchPoint(PunchPoint punchPoint);
        Task MoveToPunchPointAsync(PunchPoint punchPoint, CancellationToken cancellationToken = default);

        void StopAll();
        Task StopAllAsync();

        void EmergencyStopAll();
        Task EmergencyStopAllAsync();

        void EnableAll();
        Task EnableAllAsync();

        void DisableAll();
        Task DisableAllAsync();

        double GetXPosition();
        Task<double> GetXPositionAsync(CancellationToken cancellationToken = default);

        double GetYPosition();
        Task<double> GetYPositionAsync(CancellationToken cancellationToken = default);

        double GetZPosition();
        Task<double> GetZPositionAsync(CancellationToken cancellationToken = default);
    }
}