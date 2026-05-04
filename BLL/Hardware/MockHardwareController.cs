using Serilog;
using System;

namespace BLL
{
    // 模拟控制器（用于离线测试或纯UI调试）
    public class MockHardwareController : IHardwareController
    {
        private bool _contactSignal;
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

        public void FastMoveZ(double distance = 0.0)
        {
            // 快速接近阶段，默认未接触
            _contactSignal = false;
        }

        public void SlowMoveZ(double distance = 0.0)
        {
            // 慢速探测阶段，模拟触发表面接触
            _contactSignal = true;
            _surfaceZ += 0.01;
        }

        public void LiftZ()
        {
            _contactSignal = false;
            _logger.Debug("Mock: Z轴抬起到安全位置");
        }

        public void PunchDown(double compensation = 0.0)
        {
            // 模拟冲孔后记录该点表面值（带补偿）
            _surfaceZ += compensation * 0.1;
            _logger.Information("Mock: 执行冲孔动作，补偿={Compensation}", compensation);
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
