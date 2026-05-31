using HAL;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BLL
{
    /// <summary>
    /// 统一的工件表面探测服务，支持运动卡锁存与 IO 轮询两种方式。
    /// </summary>
    public sealed class SurfaceDetectionService
    {
        private readonly IMotionService _motionService;
        private readonly IIOCard _ioCard;

        public SurfaceDetectionService(IMotionService motionService, IIOCard ioCard)
        {
            _motionService = motionService ?? throw new ArgumentNullException(nameof(motionService));
            _ioCard = ioCard ?? throw new ArgumentNullException(nameof(ioCard));
        }

        public SurfaceDetectionResult ProbeSurface(double fastDistance, double fastSpeed, double slowDistance, double slowSpeed, SurfaceDetectionOptions options)
        {
            return ProbeSurfaceAsync(fastDistance, fastSpeed, slowDistance, slowSpeed, options).GetAwaiter().GetResult();
        }

        public async Task<SurfaceDetectionResult> ProbeSurfaceAsync(double fastDistance, double fastSpeed, double slowDistance, double slowSpeed, SurfaceDetectionOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            await PrepareSurfaceDetectionAsync(options, cancellationToken).ConfigureAwait(false);
            await MoveZRelativeAsync(fastDistance, ResolveFastSpeed(fastSpeed), wait: true, cancellationToken).ConfigureAwait(false);

            var earlyResult = await ReadSurfaceDetectionAsync(options, cancellationToken).ConfigureAwait(false);
            if (earlyResult.Detected)
            {
                throw new InvalidOperationException($"快速接近阶段已触发表面检测信号，Z={earlyResult.SurfaceZ:F4}，请检查针尖高度或工件状态。");
            }

            await PrepareSurfaceDetectionAsync(options, cancellationToken).ConfigureAwait(false);
            await MoveZRelativeAsync(slowDistance, ResolveSlowSpeed(slowSpeed), wait: false, cancellationToken).ConfigureAwait(false);

            while (await IsZAxisMovingAsync(cancellationToken).ConfigureAwait(false))
            {
                var result = await ReadSurfaceDetectionAsync(options, cancellationToken).ConfigureAwait(false);
                if (result.Detected)
                {
                    await StopZAsync(cancellationToken).ConfigureAwait(false);
                    await WaitForZStopAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }

                await Task.Delay(ResolvePollInterval(options), cancellationToken).ConfigureAwait(false);
            }

            return await ReadSurfaceDetectionAsync(options, cancellationToken).ConfigureAwait(false);
        }

        public void PrepareSurfaceDetection(SurfaceDetectionOptions options)
        {
            PrepareSurfaceDetectionAsync(options).GetAwaiter().GetResult();
        }

        public async Task PrepareSurfaceDetectionAsync(SurfaceDetectionOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.Mode != SurfaceDetectionMode.Latch)
            {
                return;
            }

            var adtMotion = GetAdtMotion();
            var axisZ = _motionService.ZAxis.AxisNo;
            await adtMotion.ClearLockStatusAsync(axisZ, cancellationToken).ConfigureAwait(false);
            await adtMotion.SetLockPositionModeAsync(axisZ, 1, 1, 1, cancellationToken).ConfigureAwait(false);
        }

        public bool TryReadSurfaceDetection(SurfaceDetectionOptions options, out double surfaceZ)
        {
            var result = ReadSurfaceDetectionAsync(options).GetAwaiter().GetResult();
            surfaceZ = result.SurfaceZ;
            return result.Detected;
        }

        public async Task<SurfaceDetectionResult> ReadSurfaceDetectionAsync(SurfaceDetectionOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.Mode == SurfaceDetectionMode.Latch)
            {
                var adtMotion = GetAdtMotion();
                var axisZ = _motionService.ZAxis.AxisNo;
                if (!await adtMotion.GetLockStatusAsync(axisZ, cancellationToken).ConfigureAwait(false))
                {
                    return default;
                }

                var surfaceZ = await adtMotion.GetLockPositionAsync(axisZ, cancellationToken).ConfigureAwait(false);
                return new SurfaceDetectionResult(true, surfaceZ);
            }

            var rawValue = await _ioCard.ReadInputAsync(options.InputPort, cancellationToken).ConfigureAwait(false);
            var isActive = options.InputLowActive ? !rawValue : rawValue;
            if (!isActive)
            {
                return default;
            }

            var currentZ = await _motionService.GetZPositionAsync(cancellationToken).ConfigureAwait(false);
            return new SurfaceDetectionResult(true, currentZ);
        }

        public bool IsZAxisMoving()
        {
            return IsZAxisMovingAsync().GetAwaiter().GetResult();
        }

        public async Task<bool> IsZAxisMovingAsync(CancellationToken cancellationToken = default)
        {
            return await _motionService.Hardware.GetStatusAsync(_motionService.ZAxis.AxisNo, cancellationToken).ConfigureAwait(false) != 0;
        }

        public async Task WaitForZStopAsync(CancellationToken cancellationToken = default)
        {
            while (await IsZAxisMovingAsync(cancellationToken).ConfigureAwait(false))
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task StopZAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _motionService.Hardware.EmergencyStopAsync(_motionService.ZAxis.AxisNo).ConfigureAwait(false);
        }

        public static int ResolvePollInterval(SurfaceDetectionOptions options)
        {
            return Math.Max(1, options.PollIntervalMs);
        }

        private async Task MoveZRelativeAsync(double distance, double speed, bool wait, CancellationToken cancellationToken)
        {
            var currentZ = await _motionService.GetZPositionAsync(cancellationToken).ConfigureAwait(false);
            await _motionService.MoveZAsync(currentZ + distance, speed, wait, cancellationToken).ConfigureAwait(false);
        }

        private double ResolveFastSpeed(double speed)
        {
            return speed > 0d ? speed : ResolveAxisSpeed();
        }

        private double ResolveSlowSpeed(double speed)
        {
            return speed > 0d ? speed : 0.5d;
        }

        private double ResolveAxisSpeed()
        {
            return _motionService.ZAxis.Velocity > 0d ? _motionService.ZAxis.Velocity : 1d;
        }

        private HAL.MotionAdt8940 GetAdtMotion()
        {
            return _motionService.Hardware as HAL.MotionAdt8940
                ?? throw new InvalidOperationException("当前运动控制器不支持锁存表面探测，请改用 IO 轮询模式或切换到 ADT8940 控制器。");
        }
    }
}
