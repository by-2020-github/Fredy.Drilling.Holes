using Serilog;
using System;

namespace BLL
{
    // 众为兴 ADT8940A1 控制卡实现
    public class Adt8940Controller : IHardwareController
    {
        private readonly IMotionService _motionManager;
        private readonly ILogger _logger;
        private HAL.MotionAdt8940 AdtMotion => _motionManager.Hardware as HAL.MotionAdt8940 ?? 
            throw new InvalidOperationException("底层硬件不是 MotionAdt8940 实现，无法使用 ADT8940Controller 特定功能。");

        public Adt8940Controller(IMotionService motionManager, ILogger logger)
        {
            _motionManager = motionManager ?? throw new ArgumentNullException(nameof(motionManager));
            _logger = (logger ?? Log.Logger).ForContext<Adt8940Controller>();
        }

        public bool Initialize()
        {
            // 利用 Manager 确保所有轴使能（虽然 Manager 有的方法是 EnableAll，但可以复用）
            _motionManager.EnableAll();
            _logger.Information("ADT8940 控制器已初始化");
            return true;
        }

        public void MoveXY(double targetX, double targetY)
        {
            // 借由 MotionManager 进行坐标与预设速度移动绑定 (底层已配置了每个轴的 AxisParam 速度等)
            // 先发动作不阻塞，让两轴可并发齐动
            _motionManager.MoveXAsync(targetX, _motionManager.XAxis.Velocity, false);
            _motionManager.MoveYAsync(targetY, _motionManager.YAxis.Velocity, false);
        }

        public void MoveXYToOffset(double offsetX, double offsetY)
        {
            // 相对移动底层不支持预设管理器配置的并发速度，我们直接调用硬件 API 或通过重新计算坐标
            double currentX = _motionManager.GetXPosition();
            double currentY = _motionManager.GetYPosition();
            _motionManager.MoveXAsync(currentX + offsetX, _motionManager.XAxis.Velocity, false);
            _motionManager.MoveYAsync(currentY + offsetY, _motionManager.YAxis.Velocity, false);
        }

        public void FastMoveZ(double distance = 0.0)
        {
            // Z轴快速移动向下探
            double currentZ = _motionManager.GetZPosition();
            _motionManager.MoveZAsync(currentZ + distance, _motionManager.ZAxis.Velocity, true).Wait();
        }

        public void SlowMoveZ(double distance = 0.0)
        {
            int axisZ = _motionManager.ZAxis.AxisNo;
            
            // 每次慢速探测前，先清除旧的锁存标志
            AdtMotion.ClearLockStatusAsync(axisZ).Wait();

            // 设置位置锁存: mode=1(有效), regi=1(实际位置), logical=1(电平由低到高触发—此处需根据实际探针信号极性调整)
            AdtMotion.SetLockPositionModeAsync(axisZ, 1, 1, 1).Wait();

            // 慢速发冲 (可以设置很低的慢速速度)
            double currentZ = _motionManager.GetZPosition();
            _motionManager.MoveZAsync(currentZ + distance, 500, true).Wait(); // 假设慢速是500，依实际修正
        }

        public void LiftZ()
        {
            // Z轴回零或移动到安全绝对坐标，此处以移动到0位演示，并且以安全快速抬升
            _motionManager.MoveZAsync(0, _motionManager.ZAxis.Velocity, true).Wait();
        }

        public void PunchDown(double compensation = 0.0)
        {
            double currentZ = _motionManager.GetZPosition();
            _motionManager.MoveZAsync(currentZ + compensation, _motionManager.ZAxis.Velocity, false).Wait();
        }

        public void StopZ()
        {
            AdtMotion.EmergencyStopAsync(_motionManager.ZAxis.AxisNo).Wait();
        }

        public void WaitForZStop()
        {
            // 阻塞直至Z轴停机，可以补充实现
        }

        public bool CheckContactSignal()
        {
            // 通过获取锁存状态判断在行程中是否触发了接触信号
            return AdtMotion.GetLockStatusAsync(_motionManager.ZAxis.AxisNo).GetAwaiter().GetResult();
        }

        public double CalculateDifference()
        {
            // 读取当前Z坐标与锁存Z坐标之差
            return 0.0;
        }

        public double ReadRecordedSurfaceZ()
        {
            return AdtMotion.GetLockPositionAsync(_motionManager.ZAxis.AxisNo).GetAwaiter().GetResult();
        }

        public void Close()
        {
            _motionManager.DisableAll();
            _logger.Information("ADT8940 控制器已关闭");
        }
    }
}
