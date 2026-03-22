using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class ManualControlViewModel : ObservableObject
    {
        private readonly ILogger<ManualControlViewModel> _logger;

        // 坐标与状态
        [ObservableProperty] private MachineStatus _machineStatus = new();

        // 步距与设置
        [ObservableProperty] private double _jogStep = 10;
        public ObservableCollection<double> JogStepOptions { get; } = new() { 0.01, 0.1, 1, 5, 10, 50 };

        [ObservableProperty] private int _canvasSize = 200;
        [ObservableProperty] private int _ringSize = 50;

        // GPIO 集合
        public ObservableCollection<GpioItem> GpioIn { get; } = new();
        public ObservableCollection<GpioItem> GpioOut { get; } = new();
        public ManualControlViewModel() { }
        public ManualControlViewModel(ILogger<ManualControlViewModel> logger)
        {
            _logger = logger;

            // 初始化 GPIO IN (0-23)
            for (int i = 0; i < 24; i++) GpioIn.Add(new GpioItem { Id = i });
            // 初始化 GPIO OUT (0-8)
            for (int i = 0; i < 9; i++) GpioOut.Add(new GpioItem { Id = i, IsActive = true });

            _logger.LogInformation("手动控制视图模型已初始化");
        }

        // 轴向运动命令 (参数如 "X+", "Y-")
        [RelayCommand]
        private void MoveAxis(string direction)
        {
            _logger.LogInformation("执行轴向移动命令: {Direction}", direction);
        }

        [RelayCommand]
        private void HomeAxis(string axis)
        {
            _logger.LogInformation("执行轴回零命令: {Axis}", axis);
        }

        [RelayCommand]
        private void TestPunch()
        {
            _logger.LogInformation("执行测试冲孔命令");
        }

        [RelayCommand]
        private void ChangeSize(string typeAndDelta)
        {
            _logger.LogInformation("调整画面参数: {TypeAndDelta}", typeAndDelta);
        }
    }
}