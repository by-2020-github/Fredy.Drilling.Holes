using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class CalibrationViewModel : ObservableObject
    {
        // 基础参数
        [ObservableProperty] private double _jogStep = 10;
        public ObservableCollection<double> JogStepOptions { get; } = new() { 0.01, 0.1, 1, 5, 10, 50 };

        [ObservableProperty] private int _canvasZoom = 200;
        [ObservableProperty] private int _ringSize = 50;

        // 运动控制
        [RelayCommand]
        private void MoveAxis(string direction) { /* 处理 X+/X-/Y+/Y- */ }

        // 校准步骤命令
        [RelayCommand]
        private void PunchTest() { /* 执行点击冲孔逻辑 */ }

        [RelayCommand]
        private void ConfirmCalibration() { /* 执行确定校针逻辑，计算偏移量 */ }

        // 缩放控制
        [RelayCommand]
        private void AdjustValue(string typeAndDelta)
        {
            // 例如 typeAndDelta = "Zoom+5"
            if (typeAndDelta.StartsWith("Zoom"))
                CanvasZoom += typeAndDelta.Contains("+") ? 10 : -10;
            else
                RingSize += typeAndDelta.Contains("+") ? 5 : -5;
        }
    }
}