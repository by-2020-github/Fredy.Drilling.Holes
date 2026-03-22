using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Fredy.Drilling.Holes.Models
{
    // 深度项模型
    public partial class DepthItem : ObservableObject
    {
        [ObservableProperty] private string _label; // No.1, No.2...
        [ObservableProperty] private int _value;
    }

    public partial class ProcessParams : ObservableObject
    {
        [ObservableProperty] private string _workpieceType = "PS60-6X500-0.05X0.075X10";

        // 头道参数
        [ObservableProperty] private int _firstPunchDepth = 186;
        [ObservableProperty] private int _firstAlarmDepth = 50;
        [ObservableProperty] private int _firstLiftHeight = 260;
        [ObservableProperty] private int _firstPeckDepth = 400;
        [ObservableProperty] private int _firstPeckSingleDepth = 200;

        // 二道参数
        [ObservableProperty] private int _secondPunchDepth = 200;
        [ObservableProperty] private int _secondAlarmDepth = 120;
        [ObservableProperty] private int _secondLiftHeight = 260;
        [ObservableProperty] private int _minSafeDepth = 20;
        [ObservableProperty] private bool _isSecondDetectionActive;

        // 冲孔深度列表
        public ObservableCollection<DepthItem> PunchDepths { get; } = new();
    }
}