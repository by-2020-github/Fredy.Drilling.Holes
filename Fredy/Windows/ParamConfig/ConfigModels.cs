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
}