using CommunityToolkit.Mvvm.ComponentModel;

namespace Fredy.Drilling.Holes.Models
{
    public partial class MachineStatus : ObservableObject
    {
        // 生产数据
        [ObservableProperty] private string _workpieceType = "PS60-6X500...";
        [ObservableProperty] private int _totalHoles = 3000;
        [ObservableProperty] private int _currentRing = 5;
        [ObservableProperty] private int _currentHole = 22;
        [ObservableProperty] private int _punchedHoles = 81;
        [ObservableProperty] private int _remainingHoles = 2919;
        [ObservableProperty] private string _speed = "1孔/分钟";
        [ObservableProperty] private string _remainingTime = "0分钟";

        // 机台坐标
        [ObservableProperty] private double _posX = 0.000;
        [ObservableProperty] private double _posY = 0.000;
        [ObservableProperty] private double _posZ = 0.000;

        // 硬件连接状态 (true 为正常)
        [ObservableProperty] private bool _isMotionCardReady;
        [ObservableProperty] private bool _isCameraConnected;
        [ObservableProperty] private bool _isHomed;
    }
}