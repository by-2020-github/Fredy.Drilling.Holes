using System;

namespace BLL
{
    public interface IHardwareController
    {
        void MoveXY(double targetX, double targetY);
        void MoveXYToOffset(double offsetX, double offsetY);
        void FastMoveZ(double distance = 0.0);
        void SlowMoveZ(double distance = 0.0);
        void LiftZ();
        void PunchDown(double compensation = 0.0);
        void StopZ();
        void WaitForZStop();
        bool CheckContactSignal();
        double CalculateDifference();
        double ReadRecordedSurfaceZ();

        // 还可以扩展连接和断开等公共生命周期方法
        bool Initialize();
        void Close();
    }
}
