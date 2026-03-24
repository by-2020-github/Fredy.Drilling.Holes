using CommunityToolkit.Mvvm.ComponentModel;

namespace Fredy.Drilling.Holes.Models
{
    // 通用运动参数 (初速度, 驱动速度, 加速度, 延时)
    public partial class MotionParams : ObservableObject
    {
        [ObservableProperty] private int _startSpeed;
        [ObservableProperty] private int _driveSpeed;
        [ObservableProperty] private int _acceleration;
        [ObservableProperty] private int _delay;
        [ObservableProperty] private int _pulseEquivalent; // 脉冲当量
    }

    // 端口项 (编号 + 低电平取反)
    public partial class PortItem : ObservableObject
    {
        [ObservableProperty] private int _portIndex;
        [ObservableProperty] private bool _isLowLevelActive;
    }

    public partial class MotionControllerConfig : ObservableObject
    {
        [ObservableProperty] private string _controllerType = "模拟运动控制卡";
        [ObservableProperty] private string _connectionString = "127.0.0.1:5000";
    }

    public partial class CameraConfig : ObservableObject
    {
        [ObservableProperty] private string _cameraType = "模拟相机";
        [ObservableProperty] private string _connectionString = "Index=0";
        [ObservableProperty] private double _pixelSizeX = 3.45;
        [ObservableProperty] private double _pixelSizeY = 3.45;
        [ObservableProperty] private double _fovWidth = 12.0;
        [ObservableProperty] private double _fovHeight = 9.0;
        [ObservableProperty] private int _resolutionWidth = 2448;
        [ObservableProperty] private int _resolutionHeight = 2048;
    }
}