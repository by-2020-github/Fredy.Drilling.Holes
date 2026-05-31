using Serilog;
using System;
using System.Threading;

namespace BLL
{
    // 众为兴 ADT8940A1 控制卡实现
    public class Adt8940Controller : IHardwareController
    {
        private readonly IMotionService _motionManager;
        private readonly HAL.IIOCard _ioCard;
        private readonly SurfaceDetectionService _surfaceDetectionService;
        private readonly ILogger _logger;
        private HAL.MotionAdt8940 AdtMotion => _motionManager.Hardware as HAL.MotionAdt8940 ?? 
            throw new InvalidOperationException("底层硬件不是 MotionAdt8940 实现，无法使用 ADT8940Controller 特定功能。");

        public Adt8940Controller(IMotionService motionManager, HAL.IIOCard ioCard, ILogger logger)
        {
            _motionManager = motionManager ?? throw new ArgumentNullException(nameof(motionManager));
            _ioCard = ioCard ?? throw new ArgumentNullException(nameof(ioCard));
            _surfaceDetectionService = new SurfaceDetectionService(_motionManager, _ioCard);
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

        public void FastMoveZ(double distance = 0.0, double speed = 0.0)
        {
            double currentZ = _motionManager.GetZPosition();
            double targetZ = currentZ + distance;
            double moveSpeed = speed > 0d ? speed : _motionManager.ZAxis.Velocity;
            _motionManager.MoveZAsync(targetZ, moveSpeed, true).Wait();
        }

        public void SlowMoveZ(double distance = 0.0, double speed = 0.0)
        {
            int axisZ = _motionManager.ZAxis.AxisNo;
            
            // 每次慢速探测前，先清除旧的锁存标志
            AdtMotion.ClearLockStatusAsync(axisZ).Wait();

            // 设置位置锁存: mode=1(有效), regi=1(实际位置), logical=1(电平由低到高触发—此处需根据实际探针信号极性调整)
            AdtMotion.SetLockPositionModeAsync(axisZ, 1, 1, 1).Wait();

            double currentZ = _motionManager.GetZPosition();
            double targetZ = currentZ + distance;
            double moveSpeed = speed > 0d ? speed : 0.5d;
            _motionManager.MoveZAsync(targetZ, moveSpeed, true).Wait();
        }

        public void LiftZ(double safeZ, double speed = 0.0)
        {
            double moveSpeed = speed > 0d ? speed : _motionManager.ZAxis.Velocity;
            _motionManager.MoveZAsync(safeZ, moveSpeed, true).Wait();
        }

        public SurfaceDetectionResult ProbeSurface(double fastDistance, double fastSpeed, double slowDistance, double slowSpeed, SurfaceDetectionOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var result = _surfaceDetectionService.ProbeSurface(fastDistance, fastSpeed, slowDistance, slowSpeed, options);
            if (result.Detected)
            {
                _logger.Information("探面完成: Mode={Mode}, SurfaceZ={SurfaceZ:F4}", options.Mode, result.SurfaceZ);
            }

            return result;
        }

        public SurfaceDetectionResult PunchDown(double commandValue = 0.0, bool isAbsoluteTarget = false, bool detectSurface = false, SurfaceDetectionOptions? detectionOptions = null, double speed = 0.0)
        {
            double currentZ = _motionManager.GetZPosition();
            double targetZ = isAbsoluteTarget ? commandValue : currentZ + commandValue;
            double moveSpeed = speed > 0d ? speed : _motionManager.ZAxis.Velocity;
            SurfaceDetectionResult detectionResult = default;

            if (detectSurface)
            {
                if (detectionOptions is null)
                {
                    throw new ArgumentNullException(nameof(detectionOptions));
                }

                _surfaceDetectionService.PrepareSurfaceDetection(detectionOptions);
                _motionManager.MoveZAsync(targetZ, moveSpeed, false).Wait();

                while (_surfaceDetectionService.IsZAxisMoving())
                {
                    if (!detectionResult.Detected && _surfaceDetectionService.TryReadSurfaceDetection(detectionOptions, out var surfaceZ))
                    {
                        detectionResult = new SurfaceDetectionResult(true, surfaceZ);
                    }

                    Thread.Sleep(SurfaceDetectionService.ResolvePollInterval(detectionOptions));
                }

                if (!detectionResult.Detected && _surfaceDetectionService.TryReadSurfaceDetection(detectionOptions, out var finalSurfaceZ))
                {
                    detectionResult = new SurfaceDetectionResult(true, finalSurfaceZ);
                }
            }
            else
            {
                _motionManager.MoveZAsync(targetZ, moveSpeed, true).Wait();
            }

            _logger.Information("执行冲孔下压: CurrentZ={CurrentZ:F4}, CommandValue={CommandValue:F4}, IsAbsoluteTarget={IsAbsoluteTarget}, TargetZ={TargetZ:F4}, Speed={Speed:F4}, DetectSurface={DetectSurface}, SurfaceDetected={SurfaceDetected}, SurfaceZ={SurfaceZ:F4}", currentZ, commandValue, isAbsoluteTarget, targetZ, moveSpeed, detectSurface, detectionResult.Detected, detectionResult.SurfaceZ);
            return detectionResult;
        }

        public void StopZ()
        {
            AdtMotion.EmergencyStopAsync(_motionManager.ZAxis.AxisNo).Wait();
        }

        public void WaitForZStop()
        {
            while (_surfaceDetectionService.IsZAxisMoving())
            {
                Thread.Sleep(10);
            }
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
