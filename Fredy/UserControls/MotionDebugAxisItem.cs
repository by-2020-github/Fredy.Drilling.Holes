using CommunityToolkit.Mvvm.ComponentModel;

namespace Fredy.Drilling.Holes.UserControls
{
    public sealed partial class MotionDebugAxisItem : ObservableObject
    {
        [ObservableProperty] private int _axisNo;
        [ObservableProperty] private double _absolutePosition;
        [ObservableProperty] private double _relativeDistance = 1d;
        [ObservableProperty] private double _currentPosition;
        [ObservableProperty] private double _speed = 9d;
    }
}