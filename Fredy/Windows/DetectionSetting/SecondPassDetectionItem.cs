using CommunityToolkit.Mvvm.ComponentModel;

namespace Fredy.Drilling.Holes.Models
{
    public partial class SecondPassDetectionItem : ObservableObject
    {
        [ObservableProperty]
        private int _index; // 圈数

        [ObservableProperty]
        private int _detectionCount; // 探测次数
    }
}