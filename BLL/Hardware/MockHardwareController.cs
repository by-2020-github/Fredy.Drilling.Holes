using System;

namespace BLL
{
    // 模拟控制器（用于离线测试或纯UI调试）
    public class MockHardwareController : IHardwareController
    {
        private bool _contactSignal;
        private double _surfaceZ;

        public bool Initialize() => true;

        public void MoveXY(double targetX, double targetY)
            => Console.WriteLine($"Mock: 模拟XY走位 X={targetX:F4}, Y={targetY:F4}");

        public void MoveXYToOffset(double offsetX, double offsetY)
            => Console.WriteLine($"Mock: 模拟XY偏移走位 offsetX={offsetX}, offsetY={offsetY}");

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
        }

        public void PunchDown(double compensation = 0.0)
        {
            // 模拟冲孔后记录该点表面值（带补偿）
            _surfaceZ += compensation * 0.1;
        }

        public void StopZ() { }
        public void WaitForZStop() { }
        public bool CheckContactSignal() => _contactSignal;
        public double CalculateDifference() => 0.1;
        public double ReadRecordedSurfaceZ() => _surfaceZ;

        public void Close()
        {
        }
    }

    /// <summary>
    /// 模拟硬件操作接口 (根据实际控制卡API替换)
    /// </summary>
    public class HardwareSimulation : IHardwareController
    {
        public void MoveXY(double targetX, double targetY) { /* 调用 ADT8940A1 走位 */ }
        public void MoveXYToOffset(double offsetX, double offsetY) { /* 调用 ADT8940A1 偏移走位 */ }
        public void FastMoveZ(double distance = 0.0) { /* Z轴快速下探 */ }
        public void SlowMoveZ(double distance = 0.0) { /* Z轴慢速下探 */ }
        public void LiftZ() { /* Z轴抬起 */ }
        public void PunchDown(double compensation = 0.0) { /* Z轴冲孔 */ }
        public void StopZ() { /* 急停Z轴 */ }
        public void WaitForZStop() { /* 阻塞或轮询等待轴停止 */ }
        public bool CheckContactSignal() => false; // 模拟读取接触IO
        public double CalculateDifference() => 0.0; // 模拟计算当前位置与预期表面位置差值
        public double ReadRecordedSurfaceZ() => 0.0; // 模拟读取控制器记录的表面高度

        public bool Initialize()
        {
            return true;
        }

        public void Close()
        {
        }
    }
}
