using BLL;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class ManualControlViewModel : ObservableObject
    {
        private readonly ILogger<ManualControlViewModel>? _logger;
        private readonly IMotionService? _motionService;

        [ObservableProperty] private MachineStatus _machineStatus = new();
        [ObservableProperty] private double _jogStep = 10;
        [ObservableProperty] private int _canvasSize = 200;
        [ObservableProperty] private int _ringSize = 50;

        public ObservableCollection<double> JogStepOptions { get; } = new() { 0.01, 0.1, 1, 5, 10, 50 };
        public ObservableCollection<GpioItem> GpioIn { get; } = new();
        public ObservableCollection<GpioItem> GpioOut { get; } = new();

        public ManualControlViewModel()
        {
            InitializeCollections();
        }

        public ManualControlViewModel(ILogger<ManualControlViewModel> logger, IMotionService motionService)
            : this()
        {
            _logger = logger;
            _motionService = motionService;
            MachineStatus.IsMotionCardReady = true;

            _ = RefreshPositionsAsync();
            _logger.LogInformation("手动控制视图模型已初始化");
        }

        [RelayCommand]
        private async Task MoveAxis(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction) || _motionService is null)
            {
                _logger?.LogWarning("轴向移动命令无效或运动服务未初始化: {Direction}", direction);
                return;
            }

            try
            {
                var step = direction.EndsWith("-", StringComparison.Ordinal) ? -Math.Abs(JogStep) : Math.Abs(JogStep);

                switch (direction[..1].ToUpperInvariant())
                {
                    case "X":
                        await _motionService.MoveXAsync(
                            await _motionService.GetXPositionAsync().ConfigureAwait(true) + step,
                            GetVelocity(_motionService.XAxis)).ConfigureAwait(true);
                        break;
                    case "Y":
                        await _motionService.MoveYAsync(
                            await _motionService.GetYPositionAsync().ConfigureAwait(true) + step,
                            GetVelocity(_motionService.YAxis)).ConfigureAwait(true);
                        break;
                    case "Z":
                        await _motionService.MoveZAsync(
                            await _motionService.GetZPositionAsync().ConfigureAwait(true) + step,
                            GetVelocity(_motionService.ZAxis)).ConfigureAwait(true);
                        break;
                    default:
                        _logger?.LogWarning("未识别的轴向移动命令: {Direction}", direction);
                        return;
                }

                await RefreshPositionsAsync().ConfigureAwait(true);
                _logger?.LogInformation("执行轴向移动命令: {Direction}, 步距: {JogStep}", direction, JogStep);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "执行轴向移动命令失败: {Direction}", direction);
            }
        }

        [RelayCommand]
        private async Task HomeAxis(string axis)
        {
            if (string.IsNullOrWhiteSpace(axis) || _motionService is null)
            {
                _logger?.LogWarning("轴回零命令无效或运动服务未初始化: {Axis}", axis);
                return;
            }

            try
            {
                switch (axis.ToUpperInvariant())
                {
                    case "X":
                        await _motionService.HomeXAsync().ConfigureAwait(true);
                        break;
                    case "Y":
                        await _motionService.HomeYAsync().ConfigureAwait(true);
                        break;
                    case "Z":
                        await _motionService.HomeZAsync().ConfigureAwait(true);
                        break;
                    default:
                        _logger?.LogWarning("未识别的轴回零命令: {Axis}", axis);
                        return;
                }

                MachineStatus.IsHomed = true;
                await RefreshPositionsAsync().ConfigureAwait(true);
                _logger?.LogInformation("执行轴回零命令: {Axis}", axis);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "执行轴回零命令失败: {Axis}", axis);
            }
        }

        [RelayCommand]
        private void TestPunch()
        {
            _logger?.LogInformation("执行测试冲孔命令");
        }

        [RelayCommand]
        private void ChangeSize(string typeAndDelta)
        {
            _logger?.LogInformation("调整画面参数: {TypeAndDelta}", typeAndDelta);
        }

        private static double GetVelocity(HAL.AxisParam axis)
        {
            return axis.Velocity > 0 ? axis.Velocity : 1d;
        }

        private void InitializeCollections()
        {
            if (GpioIn.Count > 0 || GpioOut.Count > 0)
            {
                return;
            }

            for (int i = 0; i < 24; i++)
            {
                GpioIn.Add(new GpioItem { Id = i });
            }

            for (int i = 0; i < 9; i++)
            {
                GpioOut.Add(new GpioItem { Id = i, IsActive = true });
            }
        }

        private async Task RefreshPositionsAsync()
        {
            if (_motionService is null)
            {
                return;
            }

            MachineStatus.PosX = await _motionService.GetXPositionAsync().ConfigureAwait(true);
            MachineStatus.PosY = await _motionService.GetYPositionAsync().ConfigureAwait(true);
            MachineStatus.PosZ = await _motionService.GetZPositionAsync().ConfigureAwait(true);
        }
    }
}