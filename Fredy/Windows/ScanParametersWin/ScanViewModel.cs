using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class ScanViewModel : ObservableObject
    {
        [ObservableProperty] private ScanParameters _params = new();

        // 命令实现
        [RelayCommand]
        private void CalculateCoordinates()
        {
            // TODO: 计算扫描路径逻辑
            Params.ScanStatus = "坐标计算完成";
        }

        [RelayCommand]
        private void StartScan() => Params.ScanStatus = "正在扫描...";

        [RelayCommand]
        private void StopScan() => Params.ScanStatus = "已停止";

        [RelayCommand]
        private void Test() { /* 测试逻辑 */ }
    }
}