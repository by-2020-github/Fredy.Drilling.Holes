using System;

namespace BLL
{
    public enum SurfaceDetectionMode
    {
        Latch,
        IoPolling
    }

    public sealed class SurfaceDetectionOptions
    {
        public SurfaceDetectionMode Mode { get; set; } = SurfaceDetectionMode.Latch;

        public int InputPort { get; set; }

        public bool InputLowActive { get; set; } = true;

        public int PollIntervalMs { get; set; } = 10;
    }

    public readonly record struct SurfaceDetectionResult(bool Detected, double SurfaceZ);

    public interface IHardwareController
    {
        void MoveXY(double targetX, double targetY);
        void MoveXYToOffset(double offsetX, double offsetY);
        void FastMoveZ(double distance = 0.0, double speed = 0.0);
        void SlowMoveZ(double distance = 0.0, double speed = 0.0);
        void LiftZ(double safeZ, double speed = 0.0);
        SurfaceDetectionResult ProbeSurface(double fastDistance, double fastSpeed, double slowDistance, double slowSpeed, SurfaceDetectionOptions options);
        SurfaceDetectionResult PunchDown(double commandValue = 0.0, bool isAbsoluteTarget = false, bool detectSurface = false, SurfaceDetectionOptions? detectionOptions = null, double speed = 0.0);
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
