using CommunityToolkit.Mvvm.ComponentModel;

namespace Fredy.Drilling.Holes.Models
{
    public partial class ScanParameters : ObservableObject
    {
        // 静态信息
        [ObservableProperty] private string _workpieceType = "PS60-6X500...";
        [ObservableProperty] private string _diameter = "42.7 mm";

        // 输入参数
        [ObservableProperty] private double _fovSize = 5;       // 视野大小
        [ObservableProperty] private double _scanExpand = 1;     // 扫描区扩展
        [ObservableProperty] private int _settleTime = 200;      // 机台稳定时间

        // 运行时状态
        [ObservableProperty] private string _scanStatus = "开始延时...";
        [ObservableProperty] private int _photoIndex = 22;
        [ObservableProperty] private double _currentX = -10.000;
        [ObservableProperty] private double _currentY = 10.000;
        [ObservableProperty] private double _progressValue = 35; // 进度百分比
    }
}