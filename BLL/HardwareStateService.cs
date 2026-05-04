using HAL;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BLL
{
    public sealed class HardwareStateService : IHardwareStateService
    {
        private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMilliseconds(200);

        private readonly IMotionService _motionService;
        private readonly IIOCard _ioCard;
        private readonly ICamera _camera;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private readonly CancellationTokenSource _disposeCts = new();
        private readonly Task _backgroundTask;
        private readonly ILogger _logger;

        private HardwareStateSnapshot _currentState = HardwareStateSnapshot.Empty;

        public HardwareStateService(IMotionService motionService, IIOCard ioCard, ICamera camera, ILogger logger)
        {
            _motionService = motionService ?? throw new ArgumentNullException(nameof(motionService));
            _ioCard = ioCard ?? throw new ArgumentNullException(nameof(ioCard));
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _logger = (logger ?? Log.Logger).ForContext<HardwareStateService>();
            InputCount = ioCard.InputCount;
            OutputCount = ioCard.OutputCount;
            _backgroundTask = Task.Run(BackgroundRefreshLoopAsync);
            _logger.Information("硬件状态服务已启动，输入点数={InputCount}，输出点数={OutputCount}", InputCount, OutputCount);
        }

        public int InputCount { get; }

        public int OutputCount { get; }

        public HardwareStateSnapshot CurrentState => _currentState;

        public event EventHandler<HardwareStateChangedEventArgs>? StateChanged;

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            return RefreshCoreAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (_disposeCts.IsCancellationRequested)
            {
                return;
            }

            _disposeCts.Cancel();

            try
            {
                _backgroundTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _refreshLock.Dispose();
                _disposeCts.Dispose();
                _logger.Information("硬件状态服务已释放");
            }
        }

        private async Task BackgroundRefreshLoopAsync()
        {
            using var timer = new PeriodicTimer(DefaultRefreshInterval);

            try
            {
                await TryRefreshAsync(_disposeCts.Token).ConfigureAwait(false);

                while (await timer.WaitForNextTickAsync(_disposeCts.Token).ConfigureAwait(false))
                {
                    await TryRefreshAsync(_disposeCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task TryRefreshAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "硬件状态刷新失败，已回退到当前缓存状态");
                PublishSnapshot(_currentState with
                {
                    IsMotionCardReady = false,
                    IsCameraConnected = _camera.IsConnected,
                    Timestamp = DateTime.Now,
                    Inputs = _currentState.Inputs.Count == 0 ? new Dictionary<int, bool>() : _currentState.Inputs,
                    Outputs = _currentState.Outputs.Count == 0 ? new Dictionary<int, bool>() : _currentState.Outputs
                });
            }
        }

        private async Task RefreshCoreAsync(CancellationToken cancellationToken)
        {
            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var inputPorts = Enumerable.Range(0, InputCount).ToArray();
                var outputPorts = Enumerable.Range(0, OutputCount).ToArray();

                var xTask = _motionService.GetXPositionAsync(cancellationToken);
                var yTask = _motionService.GetYPositionAsync(cancellationToken);
                var zTask = _motionService.GetZPositionAsync(cancellationToken);
                var inputsTask = _ioCard.ReadInputsAsync(inputPorts, cancellationToken);
                var outputsTask = _ioCard.ReadOutputsAsync(outputPorts, cancellationToken);

                await Task.WhenAll(xTask, yTask, zTask, inputsTask, outputsTask).ConfigureAwait(false);

                PublishSnapshot(new HardwareStateSnapshot(
                    true,
                    _camera.IsConnected,
                    xTask.Result,
                    yTask.Result,
                    zTask.Result,
                    inputsTask.Result,
                    outputsTask.Result,
                    DateTime.Now));
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private void PublishSnapshot(HardwareStateSnapshot snapshot)
        {
            _currentState = snapshot;
            StateChanged?.Invoke(this, new HardwareStateChangedEventArgs(snapshot));
        }
    }
}