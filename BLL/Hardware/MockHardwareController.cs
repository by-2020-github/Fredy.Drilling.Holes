using Serilog;
using System;

namespace BLL
{
    // 模拟控制器（用于离线测试或纯UI调试）
    public class MockHardwareController : IHardwareController
    {
        private bool _contactSignal;
        private double _currentZ;
        private double _surfaceZ;

        private readonly ILogger _logger;

        public MockHardwareController()
            : this(Log.Logger)
        {
        }

        public MockHardwareController(ILogger logger)
        {
            _logger = (logger ?? Log.Logger).ForContext<MockHardwareController>();
        }

        public bool Initialize() => true;

        public void MoveXY(double targetX, double targetY)
            => _logger.Information("Mock: 模拟XY走位 X={TargetX:F4}, Y={TargetY:F4}", targetX, targetY);

        public void MoveXYToOffset(double offsetX, double offsetY)
            => _logger.Information("Mock: 模拟XY偏移走位 offsetX={OffsetX}, offsetY={OffsetY}", offsetX, offsetY);

        public void FastMoveZ(double distance = 0.0, double speed = 0.0)
        {
            _currentZ += distance;
            _contactSignal = false;
        }

        public void SlowMoveZ(double distance = 0.0, double speed = 0.0)
        {
            _currentZ += distance;
            _contactSignal = true;
            _surfaceZ = _currentZ;
        }

        public void LiftZ(double safeZ, double speed = 0.0)
        {
            _currentZ = safeZ;
            _contactSignal = false;
            _logger.Debug("Mock: Z轴抬起到安全位置 Z={SafeZ:F4}, Speed={Speed:F4}", safeZ, speed);
        }

        public SurfaceDetectionResult ProbeSurface(double fastDistance, double fastSpeed, double slowDistance, double slowSpeed, SurfaceDetectionOptions options)
        {
            _currentZ += fastDistance + slowDistance * 0.5d;
            _surfaceZ = _currentZ;
            _contactSignal = true;
            _logger.Information("Mock: 探面完成 SurfaceZ={SurfaceZ:F4}", _surfaceZ);
            return new SurfaceDetectionResult(true, _surfaceZ);
        }

        public SurfaceDetectionResult PunchDown(double commandValue = 0.0, bool isAbsoluteTarget = false, bool detectSurface = false, SurfaceDetectionOptions? detectionOptions = null, double speed = 0.0)
        {
            _currentZ = isAbsoluteTarget ? commandValue : _currentZ + commandValue;
            SurfaceDetectionResult result = default;
            if (detectSurface)
            {
                _surfaceZ = _currentZ + 0.01d;
                result = new SurfaceDetectionResult(true, _surfaceZ);
            }

            _logger.Information("Mock: 执行冲孔动作，CommandValue={CommandValue:F4}, IsAbsoluteTarget={IsAbsoluteTarget}, TargetZ={TargetZ:F4}, Speed={Speed:F4}, DetectSurface={DetectSurface}", commandValue, isAbsoluteTarget, _currentZ, speed, detectSurface);
            return result;
        }

        public void StopZ() { }
        public void WaitForZStop() { }
        public bool CheckContactSignal() => _contactSignal;
        public double CalculateDifference() => 0.1;
        public double ReadRecordedSurfaceZ() => _surfaceZ;

        public void Close()
        {
            _logger.Information("Mock: 硬件控制器已关闭");
        }
    }
}
